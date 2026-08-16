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

public sealed record MatchServiceResult(
    MatchView? View,
    ApiError? Error,
    MatchPresentationView? Presentation = null
);

public sealed class LocalMatchService(BlokemonCatalogue catalogue, IStateDocumentStore documents)
{
    private const string _matchKey = "match";
    private const string _matchHistoryKey = "match-history";
    private const int _matchSchemaVersion = 1;
    private const int _matchHistorySchemaVersion = 1;
    private const int _maximumCpuCommandsPerRequest = 256;
    private const string _cpuPlayerId = "cpu:local";
    private const string _cpuName = "The Regular";

    private readonly MatchEngine _engine = new(catalogue.Mechanics);
    private readonly DeterministicCpu _cpu = new();

    // The verified reconstruction of the stored document identified by DocumentRevision.
    // Skips the O(history) deserialize-and-replay on every action; any revision mismatch
    // (another writer, cold load) falls back to the full verified replay.
    private LoadedMatch? _cachedMatch;

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
            return Failure("match.command_id", "Select the action again.");
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
                        "This request conflicts with a saved move. Start the battle again."
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
                    "This request conflicts with a saved move. Start the battle again."
                );
            }

            if (existing.State.Phase != MatchPhase.Complete)
            {
                return Failure(
                    "match.active",
                    "Finish the current battle before you start another battle."
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
                "This deck does not follow the current deck rules."
            );
        }

        var human = HumanPlayer(profile);
        var cpu = CpuPlayer;
        var cards = validDeck
            .Deck.Cards.OrderBy(static card => card.Key.Value, StringComparer.Ordinal)
            .SelectMany(static card => Enumerable.Repeat(card.Key.Value, card.Value))
            .ToArray();
        var cpuDeck = catalogue.StarterDecks.OpponentFor(profile.LatestStarterDeckClaim?.Id.Value);
        var start = new MatchStartRequest(
            new MatchId(request.CommandId.ToString("D")),
            MatchSeedFor(profile, request.CommandId),
            FrozenDeckSnapshot.Create(human, cards),
            FrozenDeckSnapshot.Create(cpu, cpuDeck.ExpandedCardIds)
        );
        if (_engine.Start(start) is not MatchStartOutcome.Started started)
        {
            return Failure("match.deck_illegal", "The game cannot start with this deck.");
        }

        var commands = new List<MatchCommand>();
        var events = started.Events.ToList();
        var presentation = new List<PendingPresentation> { new(started.State, started.Events) };
        var advanced = AdvanceCpu(started.State, commands, events, presentation);
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
        if (loaded.Match is { State.Phase: MatchPhase.Complete } completed)
        {
            var historyError = await ArchiveCompletedMatch(profile, completed, cancellationToken);
            if (historyError is not null)
            {
                return new(null, historyError);
            }
        }

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
        _cachedMatch = committed;
        return new(
            ToView(committed, displayName),
            null,
            ToPresentation(document, displayName, presentation)
        );
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
            return Failure("match.command_id", "Select the move again.");
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
            return Failure("match.required", "Start a battle before you select a move.");
        }

        var match = loaded.Match;
        var requestPayload = ActionPayload(routeMatchId, request);
        var fingerprint = Fingerprint(requestPayload);
        if (match.Document.StartCommand.ClientCommandId == request.CommandId)
        {
            return Failure(
                "match.command_conflict",
                "This request conflicts with the saved battle. Select the move again."
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
                    "This move conflicts with a saved move. Select the move again."
                );
        }

        if (!Guid.TryParse(match.State.Id.Value, out var persistedMatchId))
        {
            return Failure("match.replay_invalid", "The saved battle is damaged. No data changed.");
        }
        if (persistedMatchId != routeMatchId)
        {
            return Failure("match.wrong_match", "This battle is not active.");
        }
        if (match.State.Phase == MatchPhase.Complete)
        {
            return Failure("match.complete", "This battle is complete. Start a new battle.");
        }
        if (match.State.Revision.Value != request.ExpectedRevision)
        {
            return Failure("match.stale", "The battle changed. Select the move again.");
        }

        var human = HumanPlayer(profile);
        var action = _engine
            .GetLegalActions(match.State, human)
            .SingleOrDefault(candidate =>
                string.Equals(candidate.StableKey, request.ActionId, StringComparison.Ordinal)
            );
        if (action is null)
        {
            return Failure("match.action_illegal", "You cannot use that move now.");
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
        var presentation = new List<PendingPresentation> { new(applied.State, applied.Events) };
        var advanced = AdvanceCpu(applied.State, commands, events, presentation);
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
        _cachedMatch = committed;
        return new(
            ToView(committed, displayName),
            null,
            ToPresentation(document, displayName, presentation)
        );
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
                    "This request conflicts with a saved move. Start the battle again."
                );
        }
        if (match.Document.ClientCommands.Any(receipt => receipt.ClientCommandId == commandId))
        {
            return Failure(
                "match.command_conflict",
                "This request conflicts with a saved move. Start the battle again."
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
                "This request conflicts with the saved battle. Select the move again."
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
                "This request conflicts with a saved move. Select the move again."
            );
    }

    public async Task PurgeSavedMatches(CancellationToken cancellationToken = default)
    {
        await documents.Delete(_matchKey, cancellationToken);
        await documents.Delete(_matchHistoryKey, cancellationToken);
        _cachedMatch = null;
    }

    private async Task<MatchLoad> Load(LocalProfile profile, CancellationToken cancellationToken)
    {
        var stored = await documents.Read(_matchKey, cancellationToken);
        if (stored is null)
        {
            _cachedMatch = null;
            return new(null, null);
        }
        if (_cachedMatch is { } cached && cached.DocumentRevision == stored.Revision)
        {
            return new(cached, null);
        }

        var schemaVersion = ReadSchemaVersion(stored.Json);
        if (schemaVersion is null)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The saved battle is damaged. No data changed."
            );
        }
        if (schemaVersion != _matchSchemaVersion)
        {
            return InvalidDocument(
                "match.document_version",
                "This saved battle uses an unsupported version. No data changed."
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
                "The saved battle is damaged. No data changed."
            );
        }
        catch (NotSupportedException)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The saved battle is damaged. No data changed."
            );
        }

        if (document is null || document.StartCommand is null || document.Start is null)
        {
            return InvalidDocument(
                "match.document_corrupt",
                "The saved battle is damaged. No data changed."
            );
        }
        var replayed = ReplayDocument(profile, stored.Revision, document);
        _cachedMatch = replayed.Match;
        return replayed;
    }

    private MatchLoad ReplayDocument(
        LocalProfile profile,
        long documentRevision,
        MatchDocument document
    )
    {
        if (document.StartCommand is null || document.Start is null)
        {
            return InvalidReplay();
        }
        if (document.SchemaVersion != _matchSchemaVersion)
        {
            return InvalidDocument(
                "match.document_version",
                "This saved battle uses an unsupported version. No data changed."
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
            new(documentRevision, document, state, FrozenList<MatchEvent>.Create(events)),
            null
        );
    }

    private async Task<ApiError?> ArchiveCompletedMatch(
        LocalProfile profile,
        LoadedMatch completed,
        CancellationToken cancellationToken
    )
    {
        var stored = await documents.Read(_matchHistoryKey, cancellationToken);
        MatchHistoryDocument history;
        if (stored is null)
        {
            history = new(_matchHistorySchemaVersion, catalogue.Mechanics.ManifestVersion, []);
        }
        else
        {
            try
            {
                history =
                    JsonSerializer.Deserialize<MatchHistoryDocument>(stored.Json, MatchJson.Options)
                    ?? throw new JsonException();
            }
            catch (JsonException)
            {
                return HistoryCorrupt();
            }
            catch (NotSupportedException)
            {
                return HistoryCorrupt();
            }

            if (history.SchemaVersion != _matchHistorySchemaVersion)
            {
                return HistoryVersion();
            }
            if (
                !string.Equals(
                    history.AuthorityVersion,
                    catalogue.Mechanics.ManifestVersion,
                    StringComparison.Ordinal
                )
            )
            {
                return HistoryAuthorityChanged();
            }
        }

        foreach (var archived in history.Matches)
        {
            if (archived is null || archived.StartCommand is null || archived.Start is null)
            {
                return HistoryCorrupt();
            }
            if (archived.SchemaVersion != _matchSchemaVersion)
            {
                return HistoryVersion();
            }

            var replay = ReplayDocument(profile, 0, archived);
            if (replay.Error?.Code == "match.authority_changed")
            {
                return HistoryAuthorityChanged();
            }
            if (replay.Error is not null || replay.Match?.State.Phase != MatchPhase.Complete)
            {
                return HistoryCorrupt();
            }
        }

        if (
            history
                .Matches.GroupBy(static match => match.Start.MatchId)
                .Any(static matches => matches.Count() > 1)
        )
        {
            return HistoryCorrupt();
        }

        var duplicate = history.Matches.SingleOrDefault(match =>
            match.Start.MatchId == completed.Document.Start.MatchId
        );
        if (duplicate is not null)
        {
            return DocumentsMatch(duplicate, completed.Document) ? null : HistoryCorrupt();
        }

        var changed = history with
        {
            Matches = FrozenList<MatchDocument>.Create(history.Matches.Append(completed.Document)),
        };
        var json = JsonSerializer.Serialize(changed, MatchJson.Options);
        var write = stored is null
            ? await documents.Create(_matchHistoryKey, json, cancellationToken)
            : await documents.Update(_matchHistoryKey, stored.Revision, json, cancellationToken);
        return write is DocumentWriteResult.Written
            ? null
            : new(
                "state.conflict",
                "The saved battle history changed in another tab. Start the battle again."
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
                "The card rules changed after this battle started. Start a new battle."
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

        if (document.ClientCommands.Any(static receipt => receipt is null))
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
        List<MatchEvent> events,
        List<PendingPresentation> presentation
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
                return new(state, new("match.cpu_rejected", "The computer made an invalid move."));
            }

            commands.Add(selected.Action.Command);
            events.AddRange(applied.Events);
            presentation.Add(new(applied.State, applied.Events));
            state = applied.State;
        }

        return _cpu.Choose(_engine, state, CpuPlayer) is CpuDecision.Selected
            ? new(state, new("match.cpu_limit", "The computer could not complete its turn."))
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
            return InvalidChoice("Choose each option once.");
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
                return InvalidChoice("This choice is not available.");
            }
            if (requirement.Chooser != human)
            {
                return new(
                    null,
                    new("match.choice_wrong_chooser", "The computer must make this choice.")
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
        var legalActions = _engine.GetLegalActions(match.State, human);
        return new(
            Frame(match.Document, match.State, displayName),
            legalActions
                .Select(action => ActionView(match.State, human, displayName, action))
                .ToArray(),
            Attacks(match.State, human, legalActions),
            match
                .Events.Where(IsPublicEvent)
                .TakeLast(16)
                .Select(matchEvent => EventLabel(match.State, human, displayName, matchEvent))
                .ToArray()
        );
    }

    private static bool IsPublicEvent(MatchEvent matchEvent) =>
        matchEvent.Kind
            is MatchEventKind.MatchStarted
                or MatchEventKind.CommandApplied
                or MatchEventKind.CardsShuffled
                or MatchEventKind.CardsDrawn
                or MatchEventKind.CardsRevealed
                or MatchEventKind.BeerMatTossed
                or MatchEventKind.DamagePlaced
                or MatchEventKind.DamageHealed
                or MatchEventKind.RoughStateApplied
                or MatchEventKind.RoughStateCleared
                or MatchEventKind.AttackDeclared
                or MatchEventKind.AttackCancelled
                or MatchEventKind.BlokeSentHome
                or MatchEventKind.BarChitsTaken
                or MatchEventKind.RoundStarted
                or MatchEventKind.RoundEnded
                or MatchEventKind.SuddenDeathStarted
                or MatchEventKind.MatchWon;

    private MatchPresentationView ToPresentation(
        MatchDocument document,
        string displayName,
        IReadOnlyCollection<PendingPresentation> pending
    )
    {
        var human = document.Start.FirstDeck.Owner;
        return new(
            pending
                .Select(step => new MatchPresentationStepView(
                    Frame(document, step.State, displayName),
                    step.Events.Select(matchEvent =>
                            Cue(step.State, human, displayName, matchEvent, step.Events)
                        )
                        .OfType<MatchEventCueView>()
                        .ToArray()
                ))
                .ToArray()
        );
    }

    private MatchFrameView Frame(MatchDocument document, MatchState state, string displayName)
    {
        var human = document.Start.FirstDeck.Owner;
        var cpu = document.Start.SecondDeck.Owner;
        return new(
            Guid.Parse(state.Id.Value),
            state.Revision.Value,
            state.RoundNumber,
            PhaseLabel(state.Phase),
            Side(state, cpu, human, _cpuName, CpuDeckName(document.Start.SecondDeck), false),
            Side(
                state,
                human,
                human,
                displayName,
                PlayerDeckName(document.StartCommand.DeckId),
                true
            ),
            state.Phase == MatchPhase.Complete,
            state.Winner is { } winner ? PlayerName(winner, human, displayName) : null
        );
    }

    private MatchSideView Side(
        MatchState state,
        PlayerId player,
        PlayerId human,
        string name,
        string deckName,
        bool exposeHand
    )
    {
        var active = state.Oche(player);
        return new(
            name,
            deckName,
            state.CardsIn(player, CardZone.Stack).Count(),
            state.CardsIn(player, CardZone.Mitt).Count(),
            state.Player(player).BarChitsRemaining,
            active is null ? null : CardInstance(state, human, name, active.Id),
            state
                .CardsIn(player, CardZone.Booth)
                .Select(card => CardInstance(state, human, name, card.Id))
                .ToArray(),
            exposeHand
                ? state
                    .CardsIn(player, CardZone.Mitt)
                    .Select(card => CardInstance(state, human, name, card.Id))
                    .ToArray()
                : [],
            _engine.GetLegalActions(state, player).Count > 0
        );
    }

    private MatchActionView ActionView(
        MatchState state,
        PlayerId human,
        string displayName,
        LegalAction action
    )
    {
        var subject = ActionSubject(action.Command);
        return new(
            action.StableKey,
            ActionKind(action.Kind),
            ActionLabel(state, action.Command),
            action.Kind
                is LegalActionKind.ChooseOpening
                    or LegalActionKind.Attack
                    or LegalActionKind.ResolveEffectChoice
                    or LegalActionKind.ResolveKnockoutTrigger
                    or LegalActionKind.ResolveBarChitTrigger,
            subject.Source,
            subject.Target,
            subject.Effect,
            action
                .ChoiceRequirements.Select(requirement =>
                    RequirementView(state, human, displayName, requirement)
                )
                .ToArray()
        );
    }

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
                    .EligibleCards.Where(card => CanReveal(state, human, card))
                    .Select(card => CardInstance(state, human, displayName, card))
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
                    .EligibleTargets.Where(card => CanReveal(state, human, card))
                    .Select(card => CardInstance(state, human, displayName, card))
                    .ToArray()
                : [],
            requirement.RequireDifferentMechanicalTypes,
            exposeOptions
                ? requirement
                    .EligibleCardTypes.Where(card => CanReveal(state, human, card.Card))
                    .Select(card => new MatchCardTypesView(
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
            card.Damage,
            catalogue.StayingPower(card.MechanicalId.Value),
            card.Attachments.Select(state.Card)
                .Where(static attachment => attachment.Kind == CardKind.Vim)
                .Select(attachment => catalogue.Card(attachment.MechanicalId.Value))
                .ToArray(),
            card.Attachments.Select(state.Card)
                .Where(static attachment => attachment.Kind == CardKind.Kit)
                .Select(attachment => catalogue.Card(attachment.MechanicalId.Value))
                .ToArray(),
            card.UnderlyingCards.Select(state.Card)
                .Select(underlying => catalogue.Card(underlying.MechanicalId.Value))
                .ToArray(),
            card.RoughStates.Select(state => Humanize(state.State.ToString())).ToArray()
        );
    }

    private MatchAttackView[] Attacks(
        MatchState state,
        PlayerId human,
        IReadOnlyCollection<LegalAction> legalActions
    )
    {
        if (state.Oche(human) is not { } active || active.Kind != CardKind.Bloke)
        {
            return [];
        }

        var legal = legalActions
            .Where(static action => action.Command is MatchCommand.Attack)
            .ToDictionary(
                static action => ((MatchCommand.Attack)action.Command).AttackId.Value,
                static action => action.StableKey,
                StringComparer.Ordinal
            );
        var mechanical = catalogue.Mechanics.Collectibles.Single(card =>
            string.Equals(card.Id, active.MechanicalId.Value, StringComparison.Ordinal)
        );
        return mechanical
            .Attacks.Select(attack =>
            {
                legal.TryGetValue(attack.MechanicalId, out var actionId);
                return new MatchAttackView(
                    active.Id.Value,
                    attack.MechanicalId,
                    catalogue.EffectName(attack.MechanicalId),
                    attack.VimCost.Select(EnergyLabel).ToArray(),
                    attack.PrintedDamage,
                    actionId,
                    actionId is null ? AttackDisabledReason(state, human, active) : null
                );
            })
            .ToArray();
    }

    private static string AttackDisabledReason(MatchState state, PlayerId human, CardState active)
    {
        if (state.Phase != MatchPhase.Playing)
        {
            return "Complete setup before attacking.";
        }
        if (state.ActivePlayer != human)
        {
            return "Wait for your turn.";
        }
        return "Attach the required Energy or satisfy the attack's requirements.";
    }

    private string EnergyLabel(BlokemonMechanicalType type) =>
        catalogue
            .Mechanics.ApprovedMechanicalDisplayMap.FirstOrDefault(entry =>
                entry.MechanicalType == type
            )
            ?.ApprovedLabel.ToString()
        ?? Humanize(type.ToString());

    private static MatchActionKindView ActionKind(LegalActionKind kind) =>
        kind switch
        {
            LegalActionKind.ChooseMulliganBonus => MatchActionKindView.ChooseMulliganBonus,
            LegalActionKind.ChooseOpening => MatchActionKindView.ChooseOpening,
            LegalActionKind.ChooseReplacement => MatchActionKindView.ChooseReplacement,
            LegalActionKind.AttachVim => MatchActionKindView.AttachEnergy,
            LegalActionKind.PlayBloke => MatchActionKindView.PlayBlokemon,
            LegalActionKind.Promote => MatchActionKindView.Evolve,
            LegalActionKind.PlayKit => MatchActionKindView.PlayTrainer,
            LegalActionKind.UsePartyTrick => MatchActionKindView.UseAbility,
            LegalActionKind.Attack => MatchActionKindView.Attack,
            LegalActionKind.Taxi => MatchActionKindView.Retreat,
            LegalActionKind.ChuckFossil => MatchActionKindView.DiscardFossil,
            LegalActionKind.EndRound => MatchActionKindView.EndTurn,
            LegalActionKind.ResolveEffectChoice => MatchActionKindView.ResolveChoice,
            LegalActionKind.ResolveKnockoutTrigger => MatchActionKindView.ResolveKnockout,
            LegalActionKind.ResolveBarChitTrigger => MatchActionKindView.TakePrize,
            _ => throw new UnreachableException(),
        };

    private static ActionSubjectView ActionSubject(MatchCommand command) =>
        command.Match<ActionSubjectView>(
            static _ => new(null, null, null),
            static value => new(value.Oche.Value, null, null),
            static value => new(value.Vim.Value, value.Bloke.Value, null),
            static value => new(value.Bloke.Value, null, null),
            static value => new(value.Promotion.Value, value.Bloke.Value, null),
            static value => new(value.Kit.Value, value.Target?.Value, null),
            static value => new(value.BoothBloke.Value, null, null),
            static value => new(value.Source.Value, null, value.Effect.Value),
            static value => new(value.Attacker.Value, null, value.AttackId.Value),
            static value => new(value.Fossil.Value, null, null),
            static _ => new(null, null, null),
            static value => new(value.BoothBloke.Value, null, null),
            static _ => new(null, null, null),
            static value => new(value.Vim?.Value, null, null),
            static _ => new(null, null, null)
        );

    private string ActionLabel(MatchState state, MatchCommand command) =>
        command.Match(
            value =>
                value.CardsToDraw == 0
                    ? "Draw no extra cards"
                    : $"Draw {value.CardsToDraw} extra {(value.CardsToDraw == 1 ? "card" : "cards")}",
            value => $"Make {CardName(state, value.Oche)} your Active Blokemon",
            value => $"Attach {CardName(state, value.Vim)} to {CardName(state, value.Bloke)}",
            value => $"Play {CardName(state, value.Bloke)} to the Bench",
            value =>
                $"Evolve {CardName(state, value.Bloke)} into {CardName(state, value.Promotion)}",
            value => $"Play {CardName(state, value.Kit)}",
            value => $"Retreat to {CardName(state, value.BoothBloke)}",
            value => catalogue.EffectName(value.Effect.Value),
            value => $"Attack with {catalogue.EffectName(value.AttackId.Value)}",
            value => $"Discard {CardName(state, value.Fossil)}",
            _ => "End the turn",
            value => $"Move {CardName(state, value.BoothBloke)} from the Bench to Active",
            _ => "Make the required choice",
            value =>
                value.Vim is null
                    ? "Do not attach Energy"
                    : $"Attach {CardName(state, value.Vim.Value)}",
            value => value.PutOntoBooth ? "Put the card on the Bench" : "Put the card in your Hand"
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
            MatchEventKind.MatchStarted => "The battle started.",
            MatchEventKind.CommandApplied
                when matchEvent.Command is MatchCommand.PlayKit { Target: { } target } playKit =>
                $"{actor}: Attached {CardName(state, playKit.Kit)} to {CardName(state, target)}.",
            MatchEventKind.CommandApplied when matchEvent.Command is { } command =>
                $"{actor}: {ActionLabel(state, command)}.",
            MatchEventKind.CardsShuffled => $"{actor} shuffled the Deck.",
            MatchEventKind.CardsDrawn =>
                $"{actor} drew {matchEvent.Amount} {(matchEvent.Amount == 1 ? "card" : "cards")}.",
            MatchEventKind.CardsRevealed
                when matchEvent.TargetCards.Count > 0
                    && matchEvent.TargetCards.All(card =>
                        state.Card(card).Zone == CardZone.BarChit
                    ) => $"{actor} looked at their Prize Cards.",
            MatchEventKind.CardsRevealed =>
                $"{actor} revealed {matchEvent.TargetCards.Count} {(matchEvent.TargetCards.Count == 1 ? "card" : "cards")}.",
            MatchEventKind.BeerMatTossed =>
                $"The coin landed on {(matchEvent.BadgeSide == true ? "badge" : "blank")}.",
            MatchEventKind.DamagePlaced => $"{actor} did {matchEvent.Amount} damage.",
            MatchEventKind.DamageHealed => $"{actor} healed {matchEvent.Amount} damage.",
            MatchEventKind.RoughStateApplied =>
                $"{Humanize(matchEvent.RoughState!.Value.ToString())} started.",
            MatchEventKind.RoughStateCleared =>
                $"{Humanize(matchEvent.RoughState!.Value.ToString())} ended.",
            MatchEventKind.BarChitsTaken =>
                $"{actor} took {matchEvent.Amount} Prize {(matchEvent.Amount == 1 ? "Card" : "Cards")}.",
            MatchEventKind.RoundStarted => $"{actor}'s turn started.",
            MatchEventKind.RoundEnded => $"{actor} ended the turn.",
            MatchEventKind.BlokeSentHome => "A Blokemon was Knocked Out.",
            MatchEventKind.AttackDeclared when matchEvent.Effect is { } attack =>
                $"{actor} used {catalogue.EffectName(attack.Value)}.",
            MatchEventKind.AttackDeclared => $"{actor} attacked.",
            MatchEventKind.AttackCancelled => "The attack stopped.",
            MatchEventKind.SuddenDeathStarted => "Sudden death started.",
            MatchEventKind.MatchWon => $"{actor} won the battle.",
            _ => throw new UnreachableException(),
        };
    }

    private MatchEventCueView? Cue(
        MatchState state,
        PlayerId human,
        string displayName,
        MatchEvent matchEvent,
        IReadOnlyCollection<MatchEvent> stepEvents
    )
    {
        var kind = AnimationKind(matchEvent);
        if (kind is null)
        {
            return null;
        }
        var eventSource = matchEvent.SourceCard ?? CommandSource(matchEvent.Command);
        var source =
            eventSource is { } sourceCard && CanReveal(state, human, sourceCard)
                ? sourceCard.Value
                : null;
        var visibleTargets = matchEvent
            .TargetCards.Where(card => CanReveal(state, human, card))
            .ToArray();
        // Reveal faces only for authorised cards the presentation would otherwise hide
        // (face-down Prize Cards, deck cards). Cards the viewer already sees — their own
        // hand, anything in a public zone — need no reveal overlay.
        CardView[] revealed =
            matchEvent.Kind == MatchEventKind.CardsRevealed
                ? visibleTargets
                    .Where(card =>
                        state.Card(card).Zone is CardZone.Stack or CardZone.BarChit
                    )
                    .Select(card => catalogue.Card(state.Card(card).MechanicalId.Value))
                    .ToArray()
                : [];
        return new(
            matchEvent.Sequence,
            kind.Value,
            EventLabel(state, human, displayName, matchEvent),
            source,
            visibleTargets.Select(static card => card.Value).ToArray(),
            matchEvent.Kind == MatchEventKind.AttackDeclared
                ? ResolvedAttackDamage(matchEvent, stepEvents)
                : matchEvent.Amount,
            matchEvent.BadgeSide,
            matchEvent.Actor is { } actor ? actor == human : null,
            revealed
        );
    }

    private static int ResolvedAttackDamage(
        MatchEvent attack,
        IEnumerable<MatchEvent> stepEvents
    ) =>
        stepEvents
            .Where(matchEvent =>
                matchEvent.Sequence >= attack.Sequence
                && matchEvent.Kind == MatchEventKind.DamagePlaced
                && matchEvent.Actor == attack.Actor
                && matchEvent.SourceCard == attack.SourceCard
                && matchEvent.DamageKind is DamageKind.Attack or DamageKind.BoothAttack
            )
            .Sum(static matchEvent => matchEvent.Amount);

    private static CardInstanceId? CommandSource(MatchCommand? command) =>
        command switch
        {
            MatchCommand.ChooseOpening value => value.Oche,
            MatchCommand.AttachVim value => value.Vim,
            MatchCommand.PlayBloke value => value.Bloke,
            MatchCommand.Promote value => value.Promotion,
            MatchCommand.PlayKit value => value.Kit,
            MatchCommand.Taxi value => value.BoothBloke,
            MatchCommand.UsePartyTrick value => value.Source,
            MatchCommand.Attack value => value.Attacker,
            MatchCommand.ChuckFossil value => value.Fossil,
            _ => null,
        };

    private static MatchAnimationKindView? AnimationKind(MatchEvent matchEvent) =>
        matchEvent.Kind switch
        {
            MatchEventKind.MatchStarted => MatchAnimationKindView.Setup,
            MatchEventKind.CardsShuffled => MatchAnimationKindView.Shuffle,
            MatchEventKind.CardsDrawn => MatchAnimationKindView.Draw,
            MatchEventKind.CardsRevealed => MatchAnimationKindView.Reveal,
            MatchEventKind.BeerMatTossed => MatchAnimationKindView.Coin,
            MatchEventKind.CommandApplied when matchEvent.Command is MatchCommand.ChooseOpening =>
                MatchAnimationKindView.Setup,
            MatchEventKind.CommandApplied when matchEvent.Command is MatchCommand.AttachVim =>
                MatchAnimationKindView.Attach,
            MatchEventKind.CommandApplied when matchEvent.Command is MatchCommand.Promote =>
                MatchAnimationKindView.Evolve,
            MatchEventKind.CommandApplied
                when matchEvent.Command
                    is MatchCommand.PlayBloke
                        or MatchCommand.PlayKit
                        or MatchCommand.UsePartyTrick
                        or MatchCommand.Taxi => MatchAnimationKindView.Play,
            MatchEventKind.AttackDeclared => MatchAnimationKindView.Attack,
            MatchEventKind.DamagePlaced => MatchAnimationKindView.Damage,
            MatchEventKind.DamageHealed => MatchAnimationKindView.Heal,
            MatchEventKind.RoughStateApplied or MatchEventKind.RoughStateCleared =>
                MatchAnimationKindView.Condition,
            MatchEventKind.BlokeSentHome => MatchAnimationKindView.Knockout,
            MatchEventKind.BarChitsTaken => MatchAnimationKindView.Prize,
            MatchEventKind.RoundStarted => MatchAnimationKindView.Turn,
            MatchEventKind.MatchWon => MatchAnimationKindView.Victory,
            _ => null,
        };

    private static bool CanReveal(MatchState state, PlayerId human, CardInstanceId cardId)
    {
        var card = state.Card(cardId);
        return card.Owner == human
            || card.Zone
                is CardZone.Oche
                    or CardZone.Booth
                    or CardZone.Attached
                    or CardZone.EmptiesTray;
    }

    private string PlayerDeckName(Guid deckId) =>
        catalogue.StarterDecks.Decks.SingleOrDefault(deck => deck.SavedDeckId == deckId)?.Name
        ?? "Custom deck";

    private string CpuDeckName(FrozenDeckSnapshot snapshot)
    {
        var quantities = snapshot
            .Cards.GroupBy(static card => card.Value)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal
            );
        return catalogue
                .StarterDecks.Decks.SingleOrDefault(deck =>
                    deck.Entries.Count == quantities.Count
                    && deck.Entries.All(entry =>
                        quantities.GetValueOrDefault(entry.CardId) == entry.Quantity
                    )
                )
                ?.Name
            ?? "Starter deck";
    }

    private static string RequirementLabel(ChoiceRequirement requirement) =>
        requirement.Kind switch
        {
            ChoiceRequirementKind.Optional => "Use this effect?",
            ChoiceRequirementKind.Amount =>
                $"Choose an amount from {requirement.Minimum} to {requirement.Maximum}",
            ChoiceRequirementKind.Cards when requirement.Id.Value == "opening:booth" =>
                "Choose Blokemon for the Bench",
            ChoiceRequirementKind.Cards => CardChoiceLabel(
                requirement.Minimum,
                requirement.Maximum
            ),
            ChoiceRequirementKind.MechanicalType => "Choose an Energy type",
            ChoiceRequirementKind.Attack => "Choose an attack",
            ChoiceRequirementKind.Distribution =>
                $"Place {requirement.Maximum} damage {(requirement.Maximum == 1 ? "counter" : "counters")}",
            ChoiceRequirementKind.Attachments =>
                $"Choose targets for {requirement.Minimum} Energy {(requirement.Minimum == 1 ? "card" : "cards")}",
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
                "The battle changed. Choose the move again."
            ),
            CommandRejectionCode.ChoiceRequired => new(
                "match.choice_required",
                "Make each required choice."
            ),
            CommandRejectionCode.InvalidChoice => new(
                "match.choice_invalid",
                "This choice is not available."
            ),
            CommandRejectionCode.IllegalOpening => new(
                "match.choice_invalid",
                "The selected opening placement is not legal."
            ),
            CommandRejectionCode.WrongChooser => new(
                "match.choice_wrong_chooser",
                "The opponent must make this choice."
            ),
            CommandRejectionCode.DuplicateCommand => new(
                "match.command_conflict",
                "This move was already used."
            ),
            _ => new("match.action_illegal", "You cannot use that move now."),
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
            MatchPhase.MulliganBonus => "Extra draw",
            MatchPhase.OpeningPlacement => "Choose starting Blokemon",
            MatchPhase.Playing => "Battle",
            MatchPhase.AwaitingEffectChoice => "Choose an effect",
            MatchPhase.AwaitingTriggerChoice => "Make a required choice",
            MatchPhase.AwaitingReplacement => "Choose replacement",
            MatchPhase.Complete => "Complete",
            _ => throw new UnreachableException(),
        };

    private static string CardChoiceLabel(int minimum, int maximum)
    {
        if (minimum == maximum)
        {
            return $"Choose {minimum} {(minimum == 1 ? "card" : "cards")}";
        }

        return minimum == 0
            ? $"Choose up to {maximum} {(maximum == 1 ? "card" : "cards")}"
            : $"Choose {minimum} to {maximum} cards";
    }

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
        Failure("state.conflict", "The saved battle changed. Select the action again.");

    private static CommandMaterialization InvalidChoice(string message) =>
        new(null, new("match.choice_invalid", message));

    private static CommandMaterialization RequiredChoice() =>
        new(null, new("match.choice_required", "Make each required choice."));

    private static MatchLoad InvalidDocument(string code, string message) =>
        new(null, new(code, message));

    private static MatchLoad InvalidReplay() => new(null, InvalidReplayError());

    private static ApiError InvalidReplayError() =>
        new("match.replay_invalid", "The saved battle is damaged. No data changed.");

    private static bool DocumentsMatch(MatchDocument left, MatchDocument right) =>
        string.Equals(
            JsonSerializer.Serialize(left, MatchJson.Options),
            JsonSerializer.Serialize(right, MatchJson.Options),
            StringComparison.Ordinal
        );

    private static ApiError HistoryCorrupt() =>
        new("match.history_corrupt", "The saved battle history is damaged. No data changed.");

    private static ApiError HistoryVersion() =>
        new(
            "match.history_version",
            "The saved battle history uses an unsupported version. No data changed."
        );

    private static ApiError HistoryAuthorityChanged() =>
        new(
            "match.authority_changed",
            "The card rules changed after these battles were saved. No data changed."
        );

    private sealed record MatchDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string AuthorityVersion,
        [property: JsonRequired] MatchStartReceipt StartCommand,
        [property: JsonRequired] MatchStartRequest Start,
        [property: JsonRequired] FrozenList<MatchCommand> Commands,
        [property: JsonRequired] FrozenList<MatchClientCommandReceipt> ClientCommands
    );

    private sealed record MatchHistoryDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string AuthorityVersion,
        [property: JsonRequired] FrozenList<MatchDocument> Matches
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

    private sealed record PendingPresentation(MatchState State, FrozenList<MatchEvent> Events);

    private sealed record ActionSubjectView(string? Source, string? Target, string? Effect);

    private sealed record CommandMaterialization(MatchCommand? Command, ApiError? Error);
}
