using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Blokemon.Product;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using ClientStartMatchRequest = Blokemon.Web.Client.Api.StartMatchRequest;
using GameCommandId = Blokemon.Game.CommandId;

namespace Blokemon.Web.Application;

public sealed record MatchServiceResult(MatchView? View, ApiError? Error);

public sealed class LocalMatchService(BlokemonCatalogue catalogue, StateDocumentStore documents)
{
    private const string _matchKey = "match";
    private const int _matchSchemaVersion = 1;
    private const int _maximumCpuCommandsPerRequest = 256;
    private const string _cpuPlayerId = "cpu:local";
    private const string _cpuName = "The Regular";

    private readonly MatchEngine _engine = new(catalogue.Mechanics);
    private readonly DeterministicCpu _cpu = new();

    public async Task<MatchServiceResult> State(
        LocalProfile profile,
        string displayName,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await Load(profile, cancellationToken);
        return loaded.Error is not null
            ? new(null, loaded.Error)
            : new(loaded.Match is null ? null : ToView(loaded.Match, displayName), null);
    }

    public async Task<MatchServiceResult> Start(
        LocalProfile profile,
        string displayName,
        ClientStartMatchRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.CommandId == Guid.Empty)
        {
            return Failure("match.command_id", "The match command ID is required.");
        }

        var loaded = await Load(profile, cancellationToken);
        if (loaded.Error is not null)
        {
            return new(null, loaded.Error);
        }

        var fingerprint = StartFingerprint(request);
        if (loaded.Match is { } existing)
        {
            if (existing.Document.StartCommand.ClientCommandId == request.CommandId)
            {
                return string.Equals(
                    existing.Document.StartCommand.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal
                )
                    ? new(ToView(existing, displayName), null)
                    : Failure(
                        "match.command_conflict",
                        "That command ID was already used with a different match request."
                    );
            }

            if (
                existing.Document.ClientCommands.Any(receipt =>
                    receipt.ClientCommandId == request.CommandId
                )
            )
            {
                return Failure(
                    "match.command_conflict",
                    "That command ID was already used for another match action."
                );
            }

            if (existing.State.Phase != MatchPhase.Complete)
            {
                return Failure(
                    "match.active",
                    "Finish the active match before starting another one."
                );
            }
        }

        var deckIdResult = DeckId.Create(request.DeckId.ToString("D"));
        if (
            deckIdResult is not DomainResult<DeckId, TextValueFailure>.Succeeded deckId
            || !profile.SavedDecks.TryGetValue(deckId.Value, out var savedDeck)
        )
        {
            return Failure("match.deck_not_found", "The selected saved deck no longer exists.");
        }

        var validation = DeckValidator.Validate(
            profile,
            catalogue.Mechanics,
            savedDeck.Cards.Select(static card => new DeckCardSelection(card.Key, card.Value))
        );
        if (validation is not DeckValidationResult.Valid validDeck)
        {
            return Failure(
                "match.deck_illegal",
                "The selected deck is no longer legal under the current card authority."
            );
        }

        var human = HumanPlayer(profile);
        var cpu = CpuPlayer;
        var cards = validDeck
            .Deck.Cards.OrderBy(static card => card.Key.Value, StringComparer.Ordinal)
            .SelectMany(static card => Enumerable.Repeat(card.Key.Value, card.Value))
            .ToArray();
        var start = new MatchStartRequest(
            new MatchId(request.CommandId.ToString("D")),
            MatchSeedFor(profile, request.CommandId),
            FrozenDeckSnapshot.Create(human, cards),
            FrozenDeckSnapshot.Create(cpu, cards)
        );
        if (_engine.Start(start) is not MatchStartOutcome.Started started)
        {
            return Failure(
                "match.deck_illegal",
                "The selected deck was rejected by the authoritative match engine."
            );
        }

        var commands = new List<MatchCommand>();
        var events = started.Events.ToList();
        var advanced = AdvanceCpu(started.State, commands, events);
        if (advanced.Error is not null)
        {
            return new(null, advanced.Error);
        }

        var document = new MatchDocument(
            _matchSchemaVersion,
            catalogue.Mechanics.ManifestVersion,
            new MatchStartReceipt(
                request.CommandId,
                request.DeckId,
                fingerprint,
                GameStartFingerprint(start)
            ),
            start,
            FrozenList<MatchCommand>.Create(commands),
            []
        );
        var json = JsonSerializer.Serialize(document, MatchJson.Options);
        var write = loaded.Match is null
            ? await documents.Create(_matchKey, json, cancellationToken)
            : await documents.Update(
                _matchKey,
                loaded.Match.DocumentRevision,
                json,
                cancellationToken
            );
        if (write is not DocumentWriteResult.Written written)
        {
            return await ReconcileStartConflict(
                profile,
                displayName,
                request.CommandId,
                fingerprint,
                cancellationToken
            );
        }

        var committed = new LoadedMatch(
            written.Revision,
            document,
            advanced.State,
            FrozenList<MatchEvent>.Create(events)
        );
        return new(ToView(committed, displayName), null);
    }

    public async Task<MatchServiceResult> Apply(
        LocalProfile profile,
        string displayName,
        Guid routeMatchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.CommandId == Guid.Empty)
        {
            return Failure("match.command_id", "The match command ID is required.");
        }
        if (
            string.IsNullOrWhiteSpace(request.ActionId)
            || (request.Choices ?? []).Any(choice => !ChoiceSubmissionIsStructurallyValid(choice))
        )
        {
            return Failure("match.choice_invalid", "A submitted choice is invalid.");
        }

        var loaded = await Load(profile, cancellationToken);
        if (loaded.Error is not null)
        {
            return new(null, loaded.Error);
        }
        if (loaded.Match is null)
        {
            return Failure("match.required", "Start a local match before applying an action.");
        }

        var match = loaded.Match;
        var requestPayload = ActionPayload(routeMatchId, request);
        var fingerprint = Fingerprint(requestPayload);
        if (match.Document.StartCommand.ClientCommandId == request.CommandId)
        {
            return Failure(
                "match.command_conflict",
                "That command ID was already used to start the match."
            );
        }

        var receipt = match.Document.ClientCommands.SingleOrDefault(candidate =>
            candidate.ClientCommandId == request.CommandId
        );
        if (receipt is not null)
        {
            return string.Equals(receipt.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? new(ToView(match, displayName), null)
                : Failure(
                    "match.command_conflict",
                    "That command ID was already used with a different action request."
                );
        }

        if (!Guid.TryParse(match.State.Id.Value, out var persistedMatchId))
        {
            return Failure(
                "match.replay_invalid",
                "The persisted match log is invalid and was left unchanged."
            );
        }
        if (persistedMatchId != routeMatchId)
        {
            return Failure("match.wrong_match", "The requested match is not active.");
        }
        if (match.State.Phase == MatchPhase.Complete)
        {
            return Failure("match.complete", "This match is complete. Start a new match instead.");
        }
        if (match.State.Revision.Value != request.ExpectedRevision)
        {
            return Failure(
                "match.stale",
                "The match changed in another operation. Reload it before acting."
            );
        }

        var human = HumanPlayer(profile);
        var action = _engine
            .GetLegalActions(match.State, human)
            .SingleOrDefault(candidate =>
                string.Equals(candidate.StableKey, request.ActionId, StringComparison.Ordinal)
            );
        if (action is null)
        {
            return Failure(
                "match.action_illegal",
                "That action is not legal in the current match state."
            );
        }

        var materialized = MaterializeHumanCommand(
            action,
            match.State,
            human,
            request.CommandId,
            request.Choices ?? []
        );
        if (materialized.Error is not null)
        {
            return new(null, materialized.Error);
        }

        var command = materialized.Command!;
        var outcome = _engine.Apply(match.State, command);
        if (outcome is not CommandOutcome.Applied applied)
        {
            var rejected = (CommandOutcome.Rejected)outcome;
            return new(null, Rejection(rejected.Rejection.Code));
        }

        var commands = match.Document.Commands.ToList();
        commands.Add(command);
        var events = match.Events.ToList();
        events.AddRange(applied.Events);
        var advanced = AdvanceCpu(applied.State, commands, events);
        if (advanced.Error is not null)
        {
            return new(null, advanced.Error);
        }

        var clientCommands = match.Document.ClientCommands.ToList();
        clientCommands.Add(
            new MatchClientCommandReceipt(
                request.CommandId,
                fingerprint,
                requestPayload,
                command.Id,
                advanced.State.Revision
            )
        );
        var document = match.Document with
        {
            Commands = FrozenList<MatchCommand>.Create(commands),
            ClientCommands = FrozenList<MatchClientCommandReceipt>.Create(clientCommands),
        };
        var write = await documents.Update(
            _matchKey,
            match.DocumentRevision,
            JsonSerializer.Serialize(document, MatchJson.Options),
            cancellationToken
        );
        if (write is not DocumentWriteResult.Written written)
        {
            return await ReconcileActionConflict(
                profile,
                displayName,
                request.CommandId,
                fingerprint,
                cancellationToken
            );
        }

        var committed = new LoadedMatch(
            written.Revision,
            document,
            advanced.State,
            FrozenList<MatchEvent>.Create(events)
        );
        return new(ToView(committed, displayName), null);
    }

    private async Task<MatchServiceResult> ReconcileStartConflict(
        LocalProfile profile,
        string displayName,
        Guid commandId,
        string fingerprint,
        CancellationToken cancellationToken
    )
    {
        var reloaded = await Load(profile, cancellationToken);
        if (reloaded.Error is not null)
        {
            return new(null, reloaded.Error);
        }
        if (reloaded.Match is not { } match)
        {
            return StateConflict();
        }
        if (match.Document.StartCommand.ClientCommandId == commandId)
        {
            return string.Equals(
                match.Document.StartCommand.Fingerprint,
                fingerprint,
                StringComparison.Ordinal
            )
                ? new(ToView(match, displayName), null)
                : Failure(
                    "match.command_conflict",
                    "That command ID was already used with a different match request."
                );
        }
        if (match.Document.ClientCommands.Any(receipt => receipt.ClientCommandId == commandId))
        {
            return Failure(
                "match.command_conflict",
                "That command ID was already used for another match action."
            );
        }
        return StateConflict();
    }

    private async Task<MatchServiceResult> ReconcileActionConflict(
        LocalProfile profile,
        string displayName,
        Guid commandId,
        string fingerprint,
        CancellationToken cancellationToken
    )
    {
        var reloaded = await Load(profile, cancellationToken);
        if (reloaded.Error is not null)
        {
            return new(null, reloaded.Error);
        }
        if (reloaded.Match is not { } match)
        {
            return StateConflict();
        }
        if (match.Document.StartCommand.ClientCommandId == commandId)
        {
            return Failure(
                "match.command_conflict",
                "That command ID was already used to start the match."
            );
        }

        var receipt = match.Document.ClientCommands.SingleOrDefault(candidate =>
            candidate.ClientCommandId == commandId
        );
        if (receipt is null)
        {
            return StateConflict();
        }
        return string.Equals(receipt.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? new(ToView(match, displayName), null)
            : Failure(
                "match.command_conflict",
                "That command ID was already used with a different action request."
            );
    }

    private async Task<MatchLoad> Load(LocalProfile profile, CancellationToken cancellationToken)
    {
        var stored = await documents.Read(_matchKey, cancellationToken);
        if (stored is null)
        {
            return new(null, null);
        }

        var schemaVersion = ReadSchemaVersion(stored.Json);
        if (schemaVersion is null)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The persisted match document is corrupt and was left unchanged."
            );
        }
        if (schemaVersion != _matchSchemaVersion)
        {
            return InvalidDocument(
                "match.document_version",
                "The persisted match document uses an unsupported version and was left unchanged."
            );
        }

        MatchDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MatchDocument>(stored.Json, MatchJson.Options);
        }
        catch (JsonException)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The persisted match document is corrupt and was left unchanged."
            );
        }
        catch (NotSupportedException)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The persisted match document is corrupt and was left unchanged."
            );
        }

        if (document is null || document.StartCommand is null || document.Start is null)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The persisted match document is corrupt and was left unchanged."
            );
        }
        if (document.SchemaVersion != _matchSchemaVersion)
        {
            return InvalidDocument(
                "match.document_version",
                "The persisted match document uses an unsupported version and was left unchanged."
            );
        }

        var validationError = ValidateDocument(profile, document);
        if (validationError is not null)
        {
            return new(null, validationError);
        }

        if (_engine.Start(document.Start) is not MatchStartOutcome.Started started)
        {
            return InvalidReplay();
        }

        var human = HumanPlayer(profile);
        var receipts = document.ClientCommands.ToDictionary(
            static receipt => receipt.AppliedCommand,
            static receipt => receipt
        );
        var state = started.State;
        var events = started.Events.ToList();
        MatchClientCommandReceipt? pendingReceipt = null;
        foreach (var command in document.Commands)
        {
            if (command.Actor == CpuPlayer)
            {
                if (
                    _cpu.Choose(_engine, state, CpuPlayer) is not CpuDecision.Selected selected
                    || selected.Action.Command != command
                )
                {
                    return InvalidReplay();
                }
            }
            else if (command.Actor == human)
            {
                if (pendingReceipt is not null && pendingReceipt.ResultRevision != state.Revision)
                {
                    return InvalidReplay();
                }

                if (
                    !receipts.TryGetValue(command.Id, out var receipt)
                    || !IsClientCommand(command.Id)
                )
                {
                    return InvalidReplay();
                }
                var payload = ReadActionPayload(receipt.RequestPayload);
                if (
                    payload is null
                    || payload.MatchId.ToString("D") != state.Id.Value
                    || payload.ExpectedRevision != state.Revision.Value
                    || payload.Choices is null
                )
                {
                    return InvalidReplay();
                }
                var action = _engine
                    .GetLegalActions(state, human)
                    .SingleOrDefault(candidate =>
                        string.Equals(
                            candidate.StableKey,
                            payload.ActionId,
                            StringComparison.Ordinal
                        )
                    );
                if (action is null)
                {
                    return InvalidReplay();
                }
                var materialized = MaterializeHumanCommand(
                    action,
                    state,
                    human,
                    receipt.ClientCommandId,
                    payload.Choices
                );
                if (materialized.Error is not null || materialized.Command != command)
                {
                    return InvalidReplay();
                }
                pendingReceipt = receipt;
            }
            else
            {
                return InvalidReplay();
            }

            if (_engine.Apply(state, command) is not CommandOutcome.Applied applied)
            {
                return InvalidReplay();
            }
            state = applied.State;
            events.AddRange(applied.Events);
        }

        if (
            _cpu.Choose(_engine, state, CpuPlayer) is CpuDecision.Selected
            || (pendingReceipt is not null && pendingReceipt.ResultRevision != state.Revision)
        )
        {
            return InvalidReplay();
        }
        if (
            document.ClientCommands.Any(receipt =>
                receipt.ResultRevision.Value > state.Revision.Value
                || !document.Commands.Any(command => command.Id == receipt.AppliedCommand)
            )
        )
        {
            return InvalidReplay();
        }

        return new(
            new(stored.Revision, document, state, FrozenList<MatchEvent>.Create(events)),
            null
        );
    }

    private ApiError? ValidateDocument(LocalProfile profile, MatchDocument document)
    {
        if (
            !string.Equals(
                document.AuthorityVersion,
                catalogue.Mechanics.ManifestVersion,
                StringComparison.Ordinal
            )
        )
        {
            return new(
                "match.authority_changed",
                "The saved match uses a different card authority and was left unchanged."
            );
        }
        if (
            document.Start.FirstDeck is null
            || document.Start.SecondDeck is null
            || document.StartCommand.ClientCommandId == Guid.Empty
            || document.StartCommand.DeckId == Guid.Empty
            || string.IsNullOrWhiteSpace(document.StartCommand.Fingerprint)
            || document.StartCommand.Fingerprint
                != StartFingerprint(
                    new(document.StartCommand.ClientCommandId, document.StartCommand.DeckId)
                )
            || string.IsNullOrWhiteSpace(document.StartCommand.StartRequestFingerprint)
            || document.StartCommand.StartRequestFingerprint != GameStartFingerprint(document.Start)
            || document.Start.MatchId.Value != document.StartCommand.ClientCommandId.ToString("D")
            || document.Start.Seed != MatchSeedFor(profile, document.StartCommand.ClientCommandId)
            || document.Start.FirstDeck.Owner != HumanPlayer(profile)
            || document.Start.SecondDeck.Owner != CpuPlayer
            || !StartIsStructurallyValid(document.Start)
            || document.Commands.Any(command => !CommandIsStructurallyValid(command))
        )
        {
            return InvalidReplayError();
        }

        var duplicateClientCommands = document
            .ClientCommands.GroupBy(static receipt => receipt.ClientCommandId)
            .Any(static group => group.Count() > 1);
        var duplicateAppliedCommands = document
            .ClientCommands.GroupBy(static receipt => receipt.AppliedCommand)
            .Any(static group => group.Count() > 1);
        if (
            duplicateClientCommands
            || duplicateAppliedCommands
            || document.ClientCommands.Any(receipt =>
                receipt.ClientCommandId == Guid.Empty
                || receipt.ClientCommandId == document.StartCommand.ClientCommandId
                || string.IsNullOrWhiteSpace(receipt.Fingerprint)
                || string.IsNullOrWhiteSpace(receipt.RequestPayload)
                || Fingerprint(receipt.RequestPayload) != receipt.Fingerprint
                || receipt.AppliedCommand
                    != new GameCommandId($"client:{receipt.ClientCommandId:D}")
            )
        )
        {
            return InvalidReplayError();
        }

        return null;
    }

    private static bool StartIsStructurallyValid(MatchStartRequest start) =>
        HasValue(start.MatchId.Value)
        && HasValue(start.FirstDeck.Owner.Value)
        && HasValue(start.SecondDeck.Owner.Value)
        && start.FirstDeck.Cards.All(static card => HasValue(card.Value))
        && start.SecondDeck.Cards.All(static card => HasValue(card.Value));

    private static bool CommandIsStructurallyValid(MatchCommand? command)
    {
        if (
            command is null
            || !HasValue(command.Id.Value)
            || !HasValue(command.MatchId.Value)
            || !HasValue(command.Actor.Value)
            || command.ExpectedRevision.Value < 0
            || command.Choices.Any(choice => !EffectChoiceIsStructurallyValid(choice))
        )
        {
            return false;
        }

        return command.Match(
            static _ => true,
            static value => HasValue(value.Oche.Value) && value.Booth.All(CardIdHasValue),
            static value => HasValue(value.Vim.Value) && HasValue(value.Bloke.Value),
            static value => HasValue(value.Bloke.Value),
            static value => HasValue(value.Promotion.Value) && HasValue(value.Bloke.Value),
            static value =>
                HasValue(value.Kit.Value)
                && (value.Target is null || HasValue(value.Target.Value.Value)),
            static value =>
                HasValue(value.BoothBloke.Value) && value.VimToChuck.All(CardIdHasValue),
            static value => HasValue(value.Source.Value) && HasValue(value.Effect.Value),
            static value => HasValue(value.Attacker.Value) && HasValue(value.AttackId.Value),
            static value => HasValue(value.Fossil.Value),
            static _ => true,
            static value => HasValue(value.BoothBloke.Value),
            static _ => true,
            static value => value.Vim is null || HasValue(value.Vim.Value.Value),
            static _ => true
        );
    }

    private static bool EffectChoiceIsStructurallyValid(EffectChoice? choice)
    {
        if (choice is null || !HasValue(choice.Id.Value))
        {
            return false;
        }

        return choice.Match(
            static _ => true,
            static _ => true,
            static value => value.Values.All(CardIdHasValue),
            static _ => true,
            static value => HasValue(value.Value.Value),
            static value => value.Values.All(allocation => HasValue(allocation.Card.Value)),
            static value =>
                value.Values.All(attachment =>
                    HasValue(attachment.Vim.Value) && HasValue(attachment.Bloke.Value)
                )
        );
    }

    private static bool CardIdHasValue(CardInstanceId card) => HasValue(card.Value);

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private CpuAdvance AdvanceCpu(
        MatchState initial,
        List<MatchCommand> commands,
        List<MatchEvent> events
    )
    {
        var state = initial;
        for (var count = 0; count < _maximumCpuCommandsPerRequest; count++)
        {
            if (_cpu.Choose(_engine, state, CpuPlayer) is not CpuDecision.Selected selected)
            {
                return new(state, null);
            }
            if (_engine.Apply(state, selected.Action.Command) is not CommandOutcome.Applied applied)
            {
                return new(
                    state,
                    new(
                        "match.cpu_rejected",
                        "The authoritative engine rejected the deterministic CPU action."
                    )
                );
            }

            commands.Add(selected.Action.Command);
            events.AddRange(applied.Events);
            state = applied.State;
        }

        return _cpu.Choose(_engine, state, CpuPlayer) is CpuDecision.Selected
            ? new(
                state,
                new("match.cpu_limit", "The deterministic CPU exceeded its bounded action limit.")
            )
            : new(state, null);
    }

    private CommandMaterialization MaterializeHumanCommand(
        LegalAction action,
        MatchState state,
        PlayerId human,
        Guid clientCommandId,
        IReadOnlyCollection<MatchChoiceSelectionRequest> submitted
    )
    {
        if (submitted.Any(choice => !ChoiceSubmissionIsStructurallyValid(choice)))
        {
            return InvalidChoice("A submitted choice is invalid.");
        }

        var duplicate = submitted
            .GroupBy(static choice => choice.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return InvalidChoice("A choice was submitted more than once.");
        }

        var submittedById = submitted.ToDictionary(
            static choice => choice.Id,
            StringComparer.Ordinal
        );
        var requirements = action.ChoiceRequirements;
        foreach (var selection in submitted)
        {
            var requirement = requirements.SingleOrDefault(candidate =>
                candidate.Id.Value == selection.Id
            );
            if (requirement is null)
            {
                return InvalidChoice("The action contains an unknown choice.");
            }
            if (requirement.Chooser != human)
            {
                return new(
                    null,
                    new(
                        "match.choice_wrong_chooser",
                        "That choice belongs to the deterministic opponent."
                    )
                );
            }
        }

        var choices = new List<EffectChoice>();
        foreach (var requirement in requirements.Where(requirement => requirement.Chooser == human))
        {
            if (requirement.DependsOnOptional is { } dependency)
            {
                if (
                    !submittedById.TryGetValue(dependency.Value, out var parent)
                    || parent.Accepted is null
                )
                {
                    return RequiredChoice();
                }
                if (!parent.Accepted.Value)
                {
                    if (submittedById.ContainsKey(requirement.Id.Value))
                    {
                        return InvalidChoice(
                            "A choice was supplied for an optional branch that was declined."
                        );
                    }
                    continue;
                }
            }

            if (!submittedById.TryGetValue(requirement.Id.Value, out var selection))
            {
                return RequiredChoice();
            }

            var choice = ToEffectChoice(requirement, selection);
            if (choice is null)
            {
                return InvalidChoice("A submitted choice is not legal for this action.");
            }
            choices.Add(choice);
        }

        var commandId = new GameCommandId($"client:{clientCommandId:D}");
        var frozenChoices = FrozenList<EffectChoice>.Create(choices);
        var command = action.Command.Match<MatchCommand>(
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value =>
            {
                var booth = choices
                    .OfType<EffectChoice.Cards>()
                    .Single(choice => choice.Id.Value == "opening:booth")
                    .Values;
                return value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Booth = booth,
                };
            },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value =>
                value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Choices = frozenChoices,
                },
            value =>
                value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Choices = frozenChoices,
                },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value =>
                value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Choices = frozenChoices,
                },
            value =>
                value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Choices = frozenChoices,
                },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value =>
                value with
                {
                    Id = commandId,
                    ExpectedRevision = state.Revision,
                    Choices = frozenChoices,
                },
            value => value with { Id = commandId, ExpectedRevision = state.Revision },
            value => value with { Id = commandId, ExpectedRevision = state.Revision }
        );
        return new(command, null);
    }

    private static EffectChoice? ToEffectChoice(
        ChoiceRequirement requirement,
        MatchChoiceSelectionRequest selection
    )
    {
        if (selection.Kind != ChoiceKind(requirement.Kind))
        {
            return null;
        }

        return requirement.Kind switch
        {
            ChoiceRequirementKind.Optional when selection.Accepted is { } accepted =>
                new EffectChoice.Optional(requirement.Id, accepted),
            ChoiceRequirementKind.Amount when selection.Amount is { } amount =>
                new EffectChoice.Amount(requirement.Id, amount),
            ChoiceRequirementKind.Cards => new EffectChoice.Cards(
                requirement.Id,
                FrozenList<CardInstanceId>.Create(
                    (selection.CardInstanceIds ?? []).Select(static id => new CardInstanceId(id))
                )
            ),
            ChoiceRequirementKind.MechanicalType
                when Enum.TryParse<BlokemonMechanicalType>(
                    selection.MechanicalType,
                    false,
                    out var mechanicalType
                ) => new EffectChoice.MechanicalType(requirement.Id, mechanicalType),
            ChoiceRequirementKind.Attack when selection.EffectId is not null =>
                new EffectChoice.Attack(requirement.Id, new EffectId(selection.EffectId)),
            ChoiceRequirementKind.Distribution => new EffectChoice.Distribution(
                requirement.Id,
                FrozenList<DamageAllocation>.Create(
                    (selection.Distribution ?? []).Select(static allocation => new DamageAllocation(
                        new CardInstanceId(allocation.CardInstanceId),
                        allocation.Counters
                    ))
                )
            ),
            ChoiceRequirementKind.Attachments => new EffectChoice.Attachments(
                requirement.Id,
                FrozenList<VimAttachment>.Create(
                    (selection.Attachments ?? []).Select(static attachment => new VimAttachment(
                        new CardInstanceId(attachment.VimCardInstanceId),
                        new CardInstanceId(attachment.BlokeCardInstanceId)
                    ))
                )
            ),
            _ => null,
        };
    }

    private static bool ChoiceSubmissionIsStructurallyValid(MatchChoiceSelectionRequest? choice) =>
        choice is not null
        && !string.IsNullOrWhiteSpace(choice.Id)
        && choice.CardInstanceIds is not null
        && choice.CardInstanceIds.All(static id => !string.IsNullOrWhiteSpace(id))
        && choice.Distribution is not null
        && choice.Distribution.All(static allocation =>
            allocation is not null && !string.IsNullOrWhiteSpace(allocation.CardInstanceId)
        )
        && choice.Attachments is not null
        && choice.Attachments.All(static attachment =>
            attachment is not null
            && !string.IsNullOrWhiteSpace(attachment.VimCardInstanceId)
            && !string.IsNullOrWhiteSpace(attachment.BlokeCardInstanceId)
        );

    private MatchView ToView(LoadedMatch match, string displayName)
    {
        var human = match.Document.Start.FirstDeck.Owner;
        var cpu = match.Document.Start.SecondDeck.Owner;
        var state = match.State;
        return new(
            Guid.Parse(state.Id.Value),
            state.Revision.Value,
            state.RoundNumber,
            PhaseLabel(state.Phase),
            Side(state, cpu, _cpuName),
            Side(state, human, displayName),
            _engine
                .GetLegalActions(state, human)
                .Select(action => ActionView(state, human, displayName, action))
                .ToArray(),
            match
                .Events.Where(static matchEvent => matchEvent.Kind != MatchEventKind.StateCommitted)
                .TakeLast(16)
                .Select(matchEvent => EventLabel(state, human, displayName, matchEvent))
                .ToArray(),
            state.Phase == MatchPhase.Complete,
            state.Winner is { } winner ? PlayerName(winner, human, displayName) : null
        );
    }

    private MatchSideView Side(MatchState state, PlayerId player, string name)
    {
        var oche = state.Oche(player);
        return new(
            name,
            state.CardsIn(player, CardZone.Stack).Count(),
            state.CardsIn(player, CardZone.Mitt).Count(),
            state.Player(player).BarChitsRemaining,
            oche is null ? null : catalogue.Card(oche.MechanicalId.Value),
            state
                .CardsIn(player, CardZone.Booth)
                .Select(card => catalogue.Card(card.MechanicalId.Value))
                .ToArray(),
            oche?.Damage ?? 0,
            oche is null ? 0 : catalogue.StayingPower(oche.MechanicalId.Value),
            _engine.GetLegalActions(state, player).Count > 0
        );
    }

    private MatchActionView ActionView(
        MatchState state,
        PlayerId human,
        string displayName,
        LegalAction action
    ) =>
        new(
            action.StableKey,
            ActionLabel(state, action.Command),
            action.Kind
                is LegalActionKind.ChooseOpening
                    or LegalActionKind.Attack
                    or LegalActionKind.ResolveEffectChoice
                    or LegalActionKind.ResolveKnockoutTrigger
                    or LegalActionKind.ResolveBarChitTrigger,
            action
                .ChoiceRequirements.Select(requirement =>
                    RequirementView(state, human, displayName, requirement)
                )
                .ToArray()
        );

    private MatchChoiceRequirementView RequirementView(
        MatchState state,
        PlayerId human,
        string displayName,
        ChoiceRequirement requirement
    )
    {
        var exposeOptions = requirement.Chooser == human;
        return new(
            requirement.Id.Value,
            ChoiceKind(requirement.Kind),
            RequirementLabel(requirement),
            new(
                requirement.Chooser.Value,
                PlayerName(requirement.Chooser, human, displayName),
                requirement.Chooser == human
            ),
            requirement.Minimum,
            requirement.Maximum,
            exposeOptions
                ? requirement
                    .EligibleCards.Select(card => CardInstance(state, human, displayName, card))
                    .ToArray()
                : [],
            exposeOptions
                ? requirement
                    .EligibleMechanicalTypes.Select(type => new MatchMechanicalTypeOptionView(
                        type.ToString(),
                        Humanize(type.ToString())
                    ))
                    .ToArray()
                : [],
            exposeOptions
                ? requirement
                    .EligibleEffects.Select(effect => new MatchEffectOptionView(
                        effect.Value,
                        catalogue.EffectName(effect.Value)
                    ))
                    .ToArray()
                : [],
            requirement.DependsOnOptional?.Value,
            exposeOptions
                ? requirement
                    .EligibleTargets.Select(card => CardInstance(state, human, displayName, card))
                    .ToArray()
                : [],
            requirement.RequireDifferentMechanicalTypes,
            exposeOptions
                ? requirement
                    .EligibleCardTypes.Select(card => new MatchCardTypesView(
                        card.Card.Value,
                        card.Types.Select(type => Humanize(type.ToString())).ToArray()
                    ))
                    .ToArray()
                : []
        );
    }

    private MatchCardInstanceView CardInstance(
        MatchState state,
        PlayerId human,
        string displayName,
        CardInstanceId cardId
    )
    {
        var card = state.Card(cardId);
        return new(
            card.Id.Value,
            catalogue.Card(card.MechanicalId.Value),
            PlayerName(card.Owner, human, displayName),
            Humanize(card.Zone.ToString()),
            card.Damage
        );
    }

    private string ActionLabel(MatchState state, MatchCommand command) =>
        command.Match(
            value =>
                value.CardsToDraw == 0
                    ? "Decline the mulligan bonus"
                    : $"Draw {value.CardsToDraw} mulligan bonus card(s)",
            value => $"Put {CardName(state, value.Oche)} on the oche",
            value => $"Attach {CardName(state, value.Vim)} to {CardName(state, value.Bloke)}",
            value => $"Play {CardName(state, value.Bloke)} to the Booth",
            value =>
                $"Promote {CardName(state, value.Bloke)} to {CardName(state, value.Promotion)}",
            value => $"Play {CardName(state, value.Kit)}",
            value => $"Taxi to {CardName(state, value.BoothBloke)}",
            value => catalogue.EffectName(value.Effect.Value),
            value => $"Attack with {catalogue.EffectName(value.AttackId.Value)}",
            value => $"Chuck {CardName(state, value.Fossil)}",
            _ => "End the round",
            value => $"Promote {CardName(state, value.BoothBloke)} from the Booth",
            _ => "Resolve the pending effect choice",
            value =>
                value.Vim is null
                    ? "Decline the knockout trigger"
                    : $"Attach {CardName(state, value.Vim.Value)}",
            value =>
                value.PutOntoBooth ? "Put the card onto the Booth" : "Put the card into the Mitt"
        );

    private string EventLabel(
        MatchState state,
        PlayerId human,
        string displayName,
        MatchEvent matchEvent
    )
    {
        var actor = matchEvent.Actor is { } player
            ? PlayerName(player, human, displayName)
            : "The match";
        return matchEvent.Kind switch
        {
            MatchEventKind.MatchStarted => "The match started.",
            MatchEventKind.CommandApplied when matchEvent.Command is { } command =>
                $"{actor}: {ActionLabel(state, command)}.",
            MatchEventKind.CardsDrawn => $"{actor} drew {matchEvent.Amount} card(s).",
            MatchEventKind.BeerMatTossed =>
                $"{actor} tossed the beer mat: {(matchEvent.BadgeSide == true ? "badge" : "blank")} side.",
            MatchEventKind.DamagePlaced => $"{matchEvent.Amount} damage was placed.",
            MatchEventKind.DamageHealed => $"{matchEvent.Amount} damage was healed.",
            MatchEventKind.BarChitsTaken => $"{actor} took {matchEvent.Amount} Bar Chit(s).",
            MatchEventKind.RoundStarted => $"A round started for {actor}.",
            MatchEventKind.RoundEnded => $"{actor} ended the round.",
            MatchEventKind.BlokeSentHome => "A Blokemon was sent home.",
            MatchEventKind.AttackDeclared => "An attack was declared.",
            MatchEventKind.AttackCancelled => "The attack was cancelled.",
            MatchEventKind.EffectChoiceRequested => "A player choice is required.",
            MatchEventKind.SuddenDeathStarted => "Sudden death started.",
            MatchEventKind.MatchWon => $"{actor} won the match.",
            _ => Humanize(matchEvent.Kind.ToString()) + ".",
        };
    }

    private static string RequirementLabel(ChoiceRequirement requirement) =>
        requirement.Kind switch
        {
            ChoiceRequirementKind.Optional => "Use this optional effect?",
            ChoiceRequirementKind.Amount =>
                $"Choose an amount from {requirement.Minimum} to {requirement.Maximum}",
            ChoiceRequirementKind.Cards when requirement.Id.Value == "opening:booth" =>
                "Choose Blokemon for the Booth",
            ChoiceRequirementKind.Cards =>
                $"Choose {Range(requirement.Minimum, requirement.Maximum)} card(s)",
            ChoiceRequirementKind.MechanicalType => "Choose a mechanical type",
            ChoiceRequirementKind.Attack => "Choose an attack",
            ChoiceRequirementKind.Distribution =>
                $"Distribute exactly {requirement.Maximum} damage counter(s)",
            ChoiceRequirementKind.Attachments =>
                $"Choose targets for {requirement.Minimum} Vim attachment(s)",
            _ => throw new UnreachableException(),
        };

    private static MatchChoiceKindView ChoiceKind(ChoiceRequirementKind kind) =>
        kind switch
        {
            ChoiceRequirementKind.Optional => MatchChoiceKindView.Optional,
            ChoiceRequirementKind.Amount => MatchChoiceKindView.Amount,
            ChoiceRequirementKind.Cards => MatchChoiceKindView.Cards,
            ChoiceRequirementKind.MechanicalType => MatchChoiceKindView.MechanicalType,
            ChoiceRequirementKind.Attack => MatchChoiceKindView.Attack,
            ChoiceRequirementKind.Distribution => MatchChoiceKindView.Distribution,
            ChoiceRequirementKind.Attachments => MatchChoiceKindView.Attachments,
            _ => throw new UnreachableException(),
        };

    private static ApiError Rejection(CommandRejectionCode rejection) =>
        rejection switch
        {
            CommandRejectionCode.StaleRevision => new(
                "match.stale",
                "The match changed before the action could be applied."
            ),
            CommandRejectionCode.ChoiceRequired => new(
                "match.choice_required",
                "Complete every required player choice."
            ),
            CommandRejectionCode.InvalidChoice => new(
                "match.choice_invalid",
                "A submitted choice is not legal for this action."
            ),
            CommandRejectionCode.IllegalOpening => new(
                "match.choice_invalid",
                "The selected opening placement is not legal."
            ),
            CommandRejectionCode.WrongChooser => new(
                "match.choice_wrong_chooser",
                "That choice belongs to the deterministic opponent."
            ),
            CommandRejectionCode.DuplicateCommand => new(
                "match.command_conflict",
                "That command ID has already been applied."
            ),
            _ => new(
                "match.action_illegal",
                "The authoritative match engine rejected that action."
            ),
        };

    private static MatchSeed MatchSeedFor(LocalProfile profile, Guid commandId)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{profile.Id.Value}:match:{commandId:D}")
        );
        return new(BitConverter.ToUInt64(hash));
    }

    private static string StartFingerprint(ClientStartMatchRequest request) =>
        Fingerprint($"start:{request.DeckId:D}");

    private static string ActionPayload(Guid matchId, ApplyMatchActionRequest request)
    {
        var choices = (request.Choices ?? [])
            .OrderBy(static choice => choice.Id, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(
            new MatchActionPayload(matchId, request.ExpectedRevision, request.ActionId, choices),
            MatchJson.Options
        );
    }

    private static MatchActionPayload? ReadActionPayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MatchActionPayload>(json, MatchJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static int? ReadSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("schemaVersion", out var version)
                && version.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GameStartFingerprint(MatchStartRequest start) =>
        Fingerprint(JsonSerializer.Serialize(start, MatchJson.Options));

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static PlayerId HumanPlayer(LocalProfile profile) => new($"local:{profile.Id.Value}");

    private static PlayerId CpuPlayer { get; } = new(_cpuPlayerId);

    private static bool IsClientCommand(GameCommandId command) =>
        command.Value.StartsWith("client:", StringComparison.Ordinal)
        && Guid.TryParse(command.Value["client:".Length..], out _);

    private static string PlayerName(PlayerId player, PlayerId human, string displayName) =>
        player == human ? displayName : _cpuName;

    private string CardName(MatchState state, CardInstanceId card) =>
        catalogue.Card(state.Card(card).MechanicalId.Value).Name;

    private static string PhaseLabel(MatchPhase phase) =>
        phase switch
        {
            MatchPhase.MulliganBonus => "Mulligan bonus",
            MatchPhase.OpeningPlacement => "Opening setup",
            MatchPhase.Playing => "Playing",
            MatchPhase.AwaitingEffectChoice => "Effect choice",
            MatchPhase.AwaitingTriggerChoice => "Trigger choice",
            MatchPhase.AwaitingReplacement => "Choose replacement",
            MatchPhase.Complete => "Complete",
            _ => throw new UnreachableException(),
        };

    private static string Range(int minimum, int maximum) =>
        minimum == maximum ? minimum.ToString() : $"{minimum}–{maximum}";

    private static string Humanize(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                result.Append(' ');
            }
            result.Append(value[index]);
        }
        return result.ToString();
    }

    private static MatchServiceResult Failure(string code, string message) =>
        new(null, new(code, message));

    private static MatchServiceResult StateConflict() =>
        Failure(
            "state.conflict",
            "The local state changed in another operation. Retry this action."
        );

    private static CommandMaterialization InvalidChoice(string message) =>
        new(null, new("match.choice_invalid", message));

    private static CommandMaterialization RequiredChoice() =>
        new(null, new("match.choice_required", "Complete every required player choice."));

    private static MatchLoad InvalidDocument(string code, string message) =>
        new(null, new(code, message));

    private static MatchLoad InvalidReplay() => new(null, InvalidReplayError());

    private static ApiError InvalidReplayError() =>
        new("match.replay_invalid", "The persisted match log is invalid and was left unchanged.");

    private sealed record MatchDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string AuthorityVersion,
        [property: JsonRequired] MatchStartReceipt StartCommand,
        [property: JsonRequired] MatchStartRequest Start,
        [property: JsonRequired] FrozenList<MatchCommand> Commands,
        [property: JsonRequired] FrozenList<MatchClientCommandReceipt> ClientCommands
    );

    private sealed record MatchStartReceipt(
        [property: JsonRequired] Guid ClientCommandId,
        [property: JsonRequired] Guid DeckId,
        [property: JsonRequired] string Fingerprint,
        [property: JsonRequired] string StartRequestFingerprint
    );

    private sealed record MatchActionPayload(
        [property: JsonRequired] Guid MatchId,
        [property: JsonRequired] long ExpectedRevision,
        [property: JsonRequired] string ActionId,
        [property: JsonRequired] MatchChoiceSelectionRequest[] Choices
    );

    private sealed record MatchClientCommandReceipt(
        [property: JsonRequired] Guid ClientCommandId,
        [property: JsonRequired] string Fingerprint,
        [property: JsonRequired] string RequestPayload,
        [property: JsonRequired] GameCommandId AppliedCommand,
        [property: JsonRequired] MatchRevision ResultRevision
    );

    private sealed record LoadedMatch(
        long DocumentRevision,
        MatchDocument Document,
        MatchState State,
        FrozenList<MatchEvent> Events
    );

    private sealed record MatchLoad(LoadedMatch? Match, ApiError? Error);

    private sealed record CpuAdvance(MatchState State, ApiError? Error);

    private sealed record CommandMaterialization(MatchCommand? Command, ApiError? Error);
}
