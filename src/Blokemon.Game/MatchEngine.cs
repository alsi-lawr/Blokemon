using System.Diagnostics;
using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public sealed class MatchEngine
{
    private readonly AuthorityCatalog _catalog;
    private readonly BlokemonInterpreter _interpreter;
    private readonly bool _authorityIsValid;

    public MatchEngine(BlokemonRuntimeManifest authority)
    {
        _catalog = new AuthorityCatalog(authority);
        _interpreter = new BlokemonInterpreter(authority);
        _authorityIsValid =
            BlokemonSetValidator.ValidateRuntime(authority).IsValid
            && _interpreter.AuditAuthority().IsInventoryComplete;
    }

    public MatchStartOutcome Start(MatchStartRequest request)
    {
        var issues = ValidateStart(request);
        if (issues.Count > 0)
        {
            return new MatchStartOutcome.Rejected(FrozenList<DeckIssue>.Create(issues));
        }

        var players = new[] { request.FirstDeck.Owner, request.SecondDeck.Owner };
        var random = new DeterministicRandom(new MatchRandomState(request.Seed.Value, 0));
        var openingPlayer = players[random.NextInt(players.Length)];
        var cards = CreateCards(request.FirstDeck, 1)
            .Concat(CreateCards(request.SecondDeck, 2))
            .ToArray();
        var initial = new MatchState(
            request.MatchId,
            _catalog.Manifest.ManifestVersion,
            request.Seed,
            random.Snapshot,
            new MatchRevision(0),
            0,
            MatchPhase.OpeningPlacement,
            openingPlayer,
            openingPlayer,
            0,
            FrozenList<PlayerState>.Create(
                players.Select(player => new PlayerState(
                    player,
                    _catalog.Manifest.BaseRules.Opening.BarChitCount,
                    0,
                    0,
                    false,
                    false,
                    0
                ))
            ),
            FrozenList<CardState>.Create(cards),
            [],
            [],
            RoundUsage.Empty(openingPlayer),
            null,
            null,
            [],
            null,
            false,
            null,
            0
        );
        var builder = new MatchBuilder(initial, _catalog);
        builder.Events.Add(
            new PendingMatchEvent(MatchEventKind.MatchStarted, StartRequest: request)
        );
        DealOpeningMitts(builder);
        AssignMulliganBonuses(builder);
        return CommitStart(builder);
    }

    public CommandOutcome Apply(MatchState state, MatchCommand command)
    {
        var boundaryRejection = ValidateCommandBoundary(state, command);
        if (boundaryRejection is { } rejection)
        {
            return Reject(state, rejection);
        }

        var builder = new MatchBuilder(state, _catalog);
        builder.Events.Add(
            new PendingMatchEvent(MatchEventKind.CommandApplied, command.Actor, Command: command)
        );
        RefreshContinuousEffects(builder);
        var result = command.Match(
            value => ChooseMulliganBonus(builder, value),
            value => ChooseOpening(builder, value),
            value => AttachVim(builder, value),
            value => PlayBloke(builder, value),
            value => Promote(builder, value),
            value => PlayKit(builder, value),
            value => Taxi(builder, value),
            value => UsePartyTrick(builder, value),
            value => Attack(builder, value),
            value => ChuckFossil(builder, value),
            value => EndRound(builder, value),
            value => ChooseReplacement(builder, value),
            value => ResolveEffectChoice(builder, value),
            value => ResolveKnockoutTrigger(builder, value),
            value => ResolveBarChitTrigger(builder, value)
        );
        if (result.Rejection is { } handlerRejection)
        {
            return Reject(state, handlerRejection, result.Requirements);
        }

        builder.RecordCommand(command.Id);
        return CommitCommand(builder);
    }

    public FrozenList<LegalAction> GetLegalActions(MatchState state, PlayerId actor)
    {
        if (!state.Players.Any(player => player.Id == actor) || state.Phase == MatchPhase.Complete)
        {
            return [];
        }

        var proposed = state.Phase switch
        {
            MatchPhase.MulliganBonus => MulliganBonusActions(state, actor),
            MatchPhase.OpeningPlacement => OpeningActions(state, actor),
            MatchPhase.Playing => PlayingActions(state, actor),
            MatchPhase.AwaitingEffectChoice => EffectChoiceActions(state, actor),
            MatchPhase.AwaitingTriggerChoice => TriggerChoiceActions(state, actor),
            MatchPhase.AwaitingReplacement => ReplacementActions(state, actor),
            MatchPhase.Complete => [],
            _ => throw new UnreachableException(),
        };
        return FrozenList<LegalAction>.Create(
            proposed
                .Where(action => Apply(state, action.Command) is CommandOutcome.Applied)
                .OrderBy(static action => action.Kind)
                .ThenBy(static action => action.StableKey, StringComparer.Ordinal)
        );
    }

    public ReplayOutcome ReplayEvents(IEnumerable<MatchEvent> events)
    {
        MatchState? replayed = null;
        long lastSequence = 0;
        var lastRevision = new MatchRevision(0);
        long position = 0;
        foreach (var matchEvent in events)
        {
            position++;
            if (matchEvent.Sequence <= lastSequence)
            {
                return new ReplayOutcome.Rejected(
                    new ReplayIssue(ReplayIssueCode.NonIncreasingEventSequence, position)
                );
            }

            if (matchEvent.Revision.Value < lastRevision.Value)
            {
                return new ReplayOutcome.Rejected(
                    new ReplayIssue(ReplayIssueCode.RevisionWentBackwards, position)
                );
            }

            if (
                replayed is not null
                && matchEvent.CommittedState is { } next
                && next.Id != replayed.Id
            )
            {
                return new ReplayOutcome.Rejected(
                    new ReplayIssue(ReplayIssueCode.DifferentMatch, position)
                );
            }

            lastSequence = matchEvent.Sequence;
            lastRevision = matchEvent.Revision;
            if (matchEvent.Kind == MatchEventKind.MatchStarted)
            {
                if (
                    replayed is not null
                    || matchEvent.StartRequest is not { } request
                    || Start(request) is not MatchStartOutcome.Started started
                )
                {
                    return new ReplayOutcome.Rejected(
                        new ReplayIssue(ReplayIssueCode.CommandRejected, position)
                    );
                }

                replayed = started.State;
            }
            else if (matchEvent.Kind == MatchEventKind.CommandApplied)
            {
                if (
                    replayed is null
                    || matchEvent.Command is not { } command
                    || Apply(replayed, command) is not CommandOutcome.Applied applied
                )
                {
                    return new ReplayOutcome.Rejected(
                        new ReplayIssue(ReplayIssueCode.CommandRejected, position)
                    );
                }

                replayed = applied.State;
            }
            else if (
                matchEvent.Kind == MatchEventKind.StateCommitted
                && (replayed is null || matchEvent.CommittedState != replayed)
            )
            {
                return new ReplayOutcome.Rejected(
                    new ReplayIssue(ReplayIssueCode.StateMismatch, position)
                );
            }
        }

        return replayed is null
            ? new ReplayOutcome.Rejected(
                new ReplayIssue(ReplayIssueCode.NoCommittedState, position)
            )
            : new ReplayOutcome.Replayed(replayed);
    }

    public ReplayOutcome ReplayCommands(
        MatchStartRequest request,
        IEnumerable<MatchCommand> commands
    )
    {
        if (Start(request) is not MatchStartOutcome.Started started)
        {
            return new ReplayOutcome.Rejected(new ReplayIssue(ReplayIssueCode.CommandRejected, 0));
        }

        var state = started.State;
        long position = 0;
        foreach (var command in commands)
        {
            position++;
            if (Apply(state, command) is not CommandOutcome.Applied applied)
            {
                return new ReplayOutcome.Rejected(
                    new ReplayIssue(ReplayIssueCode.CommandRejected, position)
                );
            }

            state = applied.State;
        }

        return new ReplayOutcome.Replayed(state);
    }

    private List<DeckIssue> ValidateStart(MatchStartRequest request)
    {
        var issues = new List<DeckIssue>();
        if (!_authorityIsValid)
        {
            issues.Add(new DeckIssue(DeckIssueCode.AuthorityInvalid, null, null, 0, 0));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.MatchId.Value))
        {
            issues.Add(new DeckIssue(DeckIssueCode.InvalidMatchId, null, null, 0, 0));
        }

        if (
            string.IsNullOrWhiteSpace(request.FirstDeck.Owner.Value)
            || string.IsNullOrWhiteSpace(request.SecondDeck.Owner.Value)
        )
        {
            issues.Add(new DeckIssue(DeckIssueCode.InvalidPlayerId, null, null, 0, 0));
        }

        if (request.FirstDeck.Owner == request.SecondDeck.Owner)
        {
            issues.Add(
                new DeckIssue(DeckIssueCode.DuplicatePlayer, request.FirstDeck.Owner, null, 2, 1)
            );
        }

        ValidateDeck(request.FirstDeck, issues);
        ValidateDeck(request.SecondDeck, issues);
        return issues;
    }

    private void ValidateDeck(FrozenDeckSnapshot deck, List<DeckIssue> issues)
    {
        if (deck.Cards.Count != _catalog.Manifest.BaseRules.Stack.CardCount)
        {
            issues.Add(
                new DeckIssue(
                    DeckIssueCode.WrongCardCount,
                    deck.Owner,
                    null,
                    deck.Cards.Count,
                    _catalog.Manifest.BaseRules.Stack.CardCount
                )
            );
        }

        foreach (var unknown in deck.Cards.Where(card => !_catalog.Contains(card)).Distinct())
        {
            issues.Add(
                new DeckIssue(DeckIssueCode.UnknownMechanicalCard, deck.Owner, unknown, 0, 0)
            );
        }

        foreach (var group in deck.Cards.Where(_catalog.Contains).GroupBy(static card => card))
        {
            var limit = _catalog.CopyLimit(group.Key);
            if (group.Count() > limit)
            {
                issues.Add(
                    new DeckIssue(
                        DeckIssueCode.TooManyCopies,
                        deck.Owner,
                        group.Key,
                        group.Count(),
                        limit
                    )
                );
            }
        }

        if (!deck.Cards.Where(_catalog.Contains).Any(_catalog.IsRegular))
        {
            issues.Add(new DeckIssue(DeckIssueCode.MissingRegularBloke, deck.Owner, null, 0, 1));
        }
    }

    private IEnumerable<CardState> CreateCards(FrozenDeckSnapshot deck, int playerNumber)
    {
        for (var index = 0; index < deck.Cards.Count; index++)
        {
            var mechanicalId = deck.Cards[index];
            yield return new CardState(
                new CardInstanceId($"C{playerNumber}-{index + 1:D3}"),
                mechanicalId,
                deck.Owner,
                _catalog.Kind(mechanicalId),
                CardZone.Stack,
                false,
                index,
                null,
                [],
                [],
                0,
                [],
                0,
                -1
            );
        }
    }

    private void DealOpeningMitts(MatchBuilder builder)
    {
        foreach (var player in builder.Players.Select(static player => player.Id))
        {
            builder.Shuffle(player);
        }

        foreach (var player in builder.Players.Select(static player => player.Id))
        {
            builder.Draw(
                player,
                _catalog.Manifest.BaseRules.Opening.MittSize,
                DrawReason.OpeningMitt
            );
        }

        while (true)
        {
            var mulliganPlayers = builder
                .Players.Select(static player => player.Id)
                .Where(player =>
                    !builder
                        .CardsIn(player, CardZone.Mitt)
                        .Any(card =>
                            card.Kind == CardKind.Bloke && _catalog.IsRegular(card.MechanicalId)
                        )
                )
                .ToArray();
            if (mulliganPlayers.Length == 0)
            {
                return;
            }

            foreach (var player in mulliganPlayers)
            {
                builder.ReturnMittToStack(player);
                var state = builder.Player(player);
                builder.SetPlayer(state with { MulliganCount = state.MulliganCount + 1 });
            }

            foreach (var player in mulliganPlayers)
            {
                builder.Shuffle(player);
            }

            foreach (var player in mulliganPlayers)
            {
                builder.Draw(
                    player,
                    _catalog.Manifest.BaseRules.Opening.MittSize,
                    DrawReason.OpeningMitt
                );
            }
        }
    }

    private static void AssignMulliganBonuses(MatchBuilder builder)
    {
        var players = builder.Players.ToArray();
        foreach (var player in players)
        {
            var other = players.Single(candidate => candidate.Id != player.Id);
            var allowance = Math.Max(0, other.MulliganCount - player.MulliganCount);
            builder.SetPlayer(
                player with
                {
                    MulliganBonusAllowance = allowance,
                    MulliganBonusChosen = allowance == 0,
                }
            );
        }

        builder.Phase = players.Any(player => builder.Player(player.Id).MulliganBonusAllowance > 0)
            ? MatchPhase.MulliganBonus
            : MatchPhase.OpeningPlacement;
    }

    private HandlerResult ChooseMulliganBonus(
        MatchBuilder builder,
        MatchCommand.ChooseMulliganBonus command
    )
    {
        if (builder.Phase != MatchPhase.MulliganBonus)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        var player = builder.Player(command.Actor);
        if (
            player.MulliganBonusChosen
            || player.MulliganBonusAllowance == 0
            || command.CardsToDraw < 0
            || command.CardsToDraw > player.MulliganBonusAllowance
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.RuleLimitReached);
        }

        builder.Draw(command.Actor, command.CardsToDraw, DrawReason.MulliganBonus);
        builder.SetPlayer(player with { MulliganBonusChosen = true });
        if (builder.Players.All(current => builder.Player(current.Id).MulliganBonusChosen))
        {
            builder.Phase = MatchPhase.OpeningPlacement;
        }

        return HandlerResult.Accepted;
    }

    private void RefreshContinuousEffects(MatchBuilder builder)
    {
        foreach (var source in builder.Cards.Where(IsInPlay).OrderBy(static card => card.Id))
        {
            foreach (
                var trick in _catalog
                    .PartyTricks(source)
                    .Where(static trick => trick.Trigger == BlokemonTrigger.Continuous)
            )
            {
                var effect = new EffectId(trick.MechanicalId);
                builder.RemoveEffects(effect);
                _interpreter.Execute(
                    builder,
                    source.Owner,
                    source,
                    effect,
                    trick.Program,
                    [],
                    false
                );
            }
        }

        foreach (
            var source in builder.Cards.Where(card =>
                card.Kind == CardKind.Kit && card.Zone == CardZone.Attached
            )
        )
        {
            foreach (
                var rule in _catalog
                    .HouseRules(source)
                    .Where(static rule =>
                        !ContainsCondition(rule.Program, BlokemonCondition.Optional)
                    )
            )
            {
                var effect = new EffectId(rule.MechanicalId);
                builder.RemoveEffects(effect);
                _interpreter.Execute(
                    builder,
                    source.Owner,
                    source,
                    effect,
                    rule.Program,
                    [],
                    false,
                    true
                );
            }
        }
    }

    private static bool ContainsOpcode(
        BlokemonEffectInstruction[] program,
        BlokemonOpcode opcode
    ) =>
        program.Any(instruction =>
            instruction.Opcode == opcode
            || ContainsOpcode(instruction.Then, opcode)
            || ContainsOpcode(instruction.Otherwise, opcode)
        );

    private static bool ContainsCondition(
        BlokemonEffectInstruction[] program,
        BlokemonCondition condition
    ) =>
        program.Any(instruction =>
            instruction.Predicates.Any(predicate => predicate.Condition == condition)
            || ContainsCondition(instruction.Then, condition)
            || ContainsCondition(instruction.Otherwise, condition)
        );

    private static bool IsDeclarativeHouseRule(BlokemonHouseRule rule) =>
        FlattenProgram(rule.Program)
            .All(instruction =>
                instruction.Opcode
                    is BlokemonOpcode.Conditional
                        or BlokemonOpcode.ContinuousPartyTrick
            );

    private static IEnumerable<BlokemonEffectInstruction> FlattenProgram(
        BlokemonEffectInstruction[] program
    ) =>
        program.SelectMany(instruction =>
            instruction
                .Yield()
                .Concat(FlattenProgram(instruction.Then))
                .Concat(FlattenProgram(instruction.Otherwise))
        );

    private HandlerResult ChooseOpening(MatchBuilder builder, MatchCommand.ChooseOpening command)
    {
        if (builder.Phase != MatchPhase.OpeningPlacement)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        var player = builder.Player(command.Actor);
        var oche = builder.FindCard(command.Oche);
        var booth = command.Booth.Select(builder.FindCard).ToArray();
        if (
            player.OpeningChosen
            || oche is null
            || oche.Owner != command.Actor
            || oche.Zone != CardZone.Mitt
            || oche.Kind != CardKind.Bloke
            || !_catalog.IsRegular(oche.MechanicalId)
            || command.Booth.Count > _catalog.Manifest.BaseRules.Opening.BoothLimit
            || command.Booth.Distinct().Count() != command.Booth.Count
            || command.Booth.Contains(command.Oche)
            || booth.Any(card =>
                card is null
                || card.Owner != command.Actor
                || card.Zone != CardZone.Mitt
                || card.Kind != CardKind.Bloke
                || !_catalog.IsRegular(card.MechanicalId)
            )
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.IllegalOpening);
        }

        builder.MoveCard(command.Oche, CardZone.Oche);
        builder.SetCard(
            builder.Card(command.Oche) with
            {
                EnteredAtOwnerRound = builder.Player(command.Actor).RoundsStarted,
            }
        );
        foreach (var card in command.Booth)
        {
            builder.MoveCard(card, CardZone.Booth);
            builder.SetCard(
                builder.Card(card) with
                {
                    EnteredAtOwnerRound = builder.Player(command.Actor).RoundsStarted,
                }
            );
        }

        builder.SetPlayer(player with { OpeningChosen = true });
        if (builder.Players.All(current => builder.Player(current.Id).OpeningChosen))
        {
            foreach (var current in builder.Players.ToArray())
            {
                builder.SetAsideBarChits(
                    current.Id,
                    _catalog.Manifest.BaseRules.Opening.BarChitCount
                );
            }

            StartRound(builder, builder.OpeningPlayer);
        }

        return HandlerResult.Accepted;
    }

    private HandlerResult AttachVim(MatchBuilder builder, MatchCommand.AttachVim command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var vim = builder.FindCard(command.Vim);
        var target = builder.FindCard(command.Bloke);
        if (vim is null || target is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotFound);
        }

        if (vim.Owner != command.Actor || target.Owner != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotOwned);
        }

        if (
            vim.Kind != CardKind.Vim
            || vim.Zone != CardZone.Mitt
            || !IsInPlay(target)
            || builder.RoundUsage.VimAttachments
                >= _catalog.Manifest.BaseRules.Vim.NormalAttachmentPerRound
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.RuleLimitReached);
        }

        builder.Attach(vim.Id, target.Id);
        builder.RoundUsage = builder.RoundUsage with
        {
            VimAttachments = builder.RoundUsage.VimAttachments + 1,
        };
        return HandlerResult.Accepted;
    }

    private HandlerResult PlayBloke(MatchBuilder builder, MatchCommand.PlayBloke command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var bloke = builder.FindCard(command.Bloke);
        if (bloke is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotFound);
        }

        if (bloke.Owner != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotOwned);
        }

        if (
            bloke.Kind != CardKind.Bloke
            || bloke.Zone != CardZone.Mitt
            || !_catalog.IsRegular(bloke.MechanicalId)
            || builder.CardsIn(command.Actor, CardZone.Booth).Count()
                >= _catalog.Manifest.BaseRules.Opening.BoothLimit
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.RuleLimitReached);
        }

        builder.MoveCard(bloke.Id, CardZone.Booth);
        builder.SetCard(
            builder.Card(bloke.Id) with
            {
                EnteredAtOwnerRound = builder.Player(command.Actor).RoundsStarted,
            }
        );
        return HandlerResult.Accepted;
    }

    private HandlerResult Promote(MatchBuilder builder, MatchCommand.Promote command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var promotion = builder.FindCard(command.Promotion);
        var target = builder.FindCard(command.Bloke);
        if (promotion is null || target is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotFound);
        }

        var player = builder.Player(command.Actor);
        if (
            promotion.Owner != command.Actor
            || target.Owner != command.Actor
            || promotion.Kind != CardKind.Bloke
            || promotion.Zone != CardZone.Mitt
            || !IsInPlay(target)
            || player.RoundsStarted <= 1
            || target.EnteredAtOwnerRound == player.RoundsStarted
            || target.LastPromotedRound == builder.RoundNumber
            || _catalog.Bloke(promotion.MechanicalId).PromotesFromId != target.MechanicalId.Value
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.IneligiblePromotion);
        }

        var zone = target.Zone;
        builder.SetCard(
            target with
            {
                Zone = CardZone.Attached,
                AttachedTo = promotion.Id,
                Attachments = [],
                RoughStates = [],
            }
        );
        builder.SetCard(
            promotion with
            {
                Zone = zone,
                StackPosition = -1,
                Damage = target.Damage,
                Attachments = target.Attachments,
                UnderlyingCards = FrozenList<CardInstanceId>.Create(
                    target.UnderlyingCards.Append(target.Id)
                ),
                RoughStates = [],
                EnteredAtOwnerRound = target.EnteredAtOwnerRound,
                LastPromotedRound = builder.RoundNumber,
            }
        );
        foreach (var attachmentId in target.Attachments)
        {
            builder.SetCard(builder.Card(attachmentId) with { AttachedTo = promotion.Id });
        }

        builder.RemoveEffectsFor(target.Id);
        foreach (
            var trick in _catalog
                .PartyTricks(builder.Card(promotion.Id))
                .Where(static trick => trick.Trigger == BlokemonTrigger.OnPromotionFromMitt)
        )
        {
            var execution = _interpreter.Execute(
                builder,
                command.Actor,
                builder.Card(promotion.Id),
                new EffectId(trick.MechanicalId),
                trick.Program,
                command.Choices,
                false
            );
            if (!execution.IsApplied)
            {
                return HandlerResult.Reject(
                    execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                    execution.Requirements
                );
            }

            ResolveSendHome(builder, execution.ForcedSendHome);
        }

        return HandlerResult.Accepted;
    }

    private HandlerResult PlayKit(MatchBuilder builder, MatchCommand.PlayKit command)
    {
        return PlayKit(builder, command, false, []);
    }

    private HandlerResult PlayKit(
        MatchBuilder builder,
        MatchCommand.PlayKit command,
        bool isResuming,
        FrozenList<bool> beerMatResults
    )
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var kitCard = builder.FindCard(command.Kit);
        if (kitCard is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotFound);
        }

        if (kitCard.Owner != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotOwned);
        }

        if (kitCard.Kind != CardKind.Kit || kitCard.Zone != CardZone.Mitt)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongZone);
        }

        var kit = _catalog.Kit(kitCard.MechanicalId);
        var categoryRejection = ValidateKitCategory(builder, command.Actor, kit, command.Target);
        if (categoryRejection is not null)
        {
            return HandlerResult.Reject(categoryRejection.Value);
        }

        var executableRules = kit
            .HouseRules.Where(rule =>
                !ContainsOpcode(rule.Program, BlokemonOpcode.OncePerRound)
                && !IsDeclarativeHouseRule(rule)
            )
            .ToArray();
        if (!isResuming)
        {
            var requirements = FrozenList<ChoiceRequirement>.Create(
                executableRules
                    .SelectMany(rule =>
                        _interpreter.InspectChoices(
                            builder,
                            command.Actor,
                            kitCard,
                            new EffectId(rule.MechanicalId),
                            rule.Program
                        )
                    )
                    .DistinctBy(static requirement => requirement.Id)
            );
            var choiceRejection = _interpreter.ValidateChoiceSubmission(
                command.Choices,
                requirements,
                command.Actor
            );
            if (choiceRejection is not null)
            {
                return HandlerResult.Reject(choiceRejection.Value, requirements);
            }
        }

        foreach (var houseRule in executableRules)
        {
            var effect = new EffectId(houseRule.MechanicalId);
            var plan = _interpreter.Plan(
                builder,
                command.Actor,
                kitCard,
                effect,
                houseRule.Program,
                command.Choices,
                false,
                true,
                beerMatResults
            );
            if (plan.IsApplied)
            {
                continue;
            }

            if (plan.Rejection != CommandRejectionCode.ChoiceRequired)
            {
                return HandlerResult.Reject(
                    plan.Rejection ?? CommandRejectionCode.InvalidChoice,
                    plan.Requirements
                );
            }

            return PendEffect(
                builder,
                command,
                kitCard.Id,
                effect,
                plan.Requirements,
                beerMatResults,
                plan.BeerMatResults,
                false
            );
        }

        if (kit.Kind == BlokemonKitKind.BarKit && command.Target is { } targetId)
        {
            builder.Attach(kitCard.Id, targetId);
        }
        else if (kit.Kind == BlokemonKitKind.Local)
        {
            var current = builder.Cards.SingleOrDefault(card => card.Zone == CardZone.Local);
            if (current is not null)
            {
                builder.MoveCard(current.Id, CardZone.EmptiesTray);
            }

            builder.MoveCard(kitCard.Id, CardZone.Local);
        }

        foreach (var houseRule in executableRules)
        {
            var execution = _interpreter.Execute(
                builder,
                command.Actor,
                builder.Card(kitCard.Id),
                new EffectId(houseRule.MechanicalId),
                houseRule.Program,
                command.Choices,
                false,
                true,
                beerMatResults: beerMatResults
            );
            if (!execution.IsApplied)
            {
                return HandlerResult.Reject(
                    execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                    execution.Requirements
                );
            }
        }

        if (builder.Card(kitCard.Id).Zone == CardZone.Mitt)
        {
            builder.MoveCard(kitCard.Id, CardZone.EmptiesTray);
        }

        builder.RoundUsage = kit.Kind switch
        {
            BlokemonKitKind.BarBit => builder.RoundUsage,
            BlokemonKitKind.Mate => builder.RoundUsage with
            {
                MatesPlayed = builder.RoundUsage.MatesPlayed + 1,
            },
            BlokemonKitKind.Local => builder.RoundUsage with
            {
                LocalsPlayed = builder.RoundUsage.LocalsPlayed + 1,
            },
            BlokemonKitKind.BarKit => builder.RoundUsage,
            _ => throw new UnreachableException(),
        };
        return HandlerResult.Accepted;
    }

    private CommandRejectionCode? ValidateKitCategory(
        MatchBuilder builder,
        PlayerId actor,
        BlokemonKit kit,
        CardInstanceId? targetId
    )
    {
        var restricted = builder.Effects.Any(effect =>
            effect.Owner != actor
            && (
                effect.Kind == TemporaryEffectKind.RestrictKit
                || (
                    kit.Kind == BlokemonKitKind.Local
                    && effect.Kind == TemporaryEffectKind.RestrictLocal
                )
            )
        );
        if (restricted)
        {
            return CommandRejectionCode.EffectUnavailable;
        }

        return kit.Kind switch
        {
            BlokemonKitKind.BarBit => null,
            BlokemonKitKind.Mate => builder.RoundUsage.MatesPlayed
                >= _catalog.Manifest.BaseRules.Kit.MatesPerRound
            || (actor == builder.OpeningPlayer && builder.Player(actor).RoundsStarted == 1)
                ? CommandRejectionCode.RuleLimitReached
                : null,
            BlokemonKitKind.Local => ValidateLocal(builder, kit),
            BlokemonKitKind.BarKit => ValidateBarKit(builder, actor, targetId),
            _ => throw new UnreachableException(),
        };
    }

    private CommandRejectionCode? ValidateLocal(MatchBuilder builder, BlokemonKit kit)
    {
        if (builder.RoundUsage.LocalsPlayed >= _catalog.Manifest.BaseRules.Kit.LocalsPerRound)
        {
            return CommandRejectionCode.RuleLimitReached;
        }

        var current = builder.Cards.SingleOrDefault(card => card.Zone == CardZone.Local);
        return current?.MechanicalId.Value == kit.Id ? CommandRejectionCode.RuleLimitReached : null;
    }

    private CommandRejectionCode? ValidateBarKit(
        MatchBuilder builder,
        PlayerId actor,
        CardInstanceId? targetId
    )
    {
        if (targetId is null)
        {
            return CommandRejectionCode.CardNotFound;
        }

        var target = builder.FindCard(targetId.Value);
        if (target is null || target.Owner != actor || !IsInPlay(target))
        {
            return CommandRejectionCode.CardNotOwned;
        }

        return target
            .Attachments.Select(builder.Card)
            .Any(card =>
                card.Kind == CardKind.Kit
                && _catalog.Kit(card.MechanicalId).Kind == BlokemonKitKind.BarKit
            )
            ? CommandRejectionCode.RuleLimitReached
            : null;
    }

    private HandlerResult Taxi(MatchBuilder builder, MatchCommand.Taxi command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var outgoing = builder.Oche(command.Actor);
        var incoming = builder.FindCard(command.BoothBloke);
        if (outgoing is null || incoming is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.CardNotFound);
        }

        if (incoming.Owner != command.Actor || incoming.Zone != CardZone.Booth)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongZone);
        }

        if (
            builder.RoundUsage.TaxisUsed >= _catalog.Manifest.BaseRules.Taxi.PerRound
            || outgoing.RoughStates.Any(entry =>
                entry.State is BlokemonRoughState.NoddedOff or BlokemonRoughState.Legless
            )
            || (outgoing.Kind == CardKind.Kit && _catalog.IsFossil(outgoing.MechanicalId))
            || builder.Effects.Any(effect =>
                effect.TargetCard == outgoing.Id && effect.Kind == TemporaryEffectKind.RestrictTaxi
            )
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectUnavailable);
        }

        var fare = EffectiveTaxiFare(builder, outgoing);
        var attachedVim = outgoing
            .Attachments.Select(builder.Card)
            .Where(static card => card.Kind == CardKind.Vim)
            .ToArray();
        if (
            command.VimToChuck.Count != fare
            || command.VimToChuck.Distinct().Count() != command.VimToChuck.Count
            || command.VimToChuck.Any(id => !attachedVim.Any(card => card.Id == id))
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.InvalidTaxiFare);
        }

        foreach (var vim in command.VimToChuck)
        {
            builder.DetachTo(vim, CardZone.EmptiesTray);
        }

        builder.MoveCard(outgoing.Id, CardZone.Booth);
        builder.ClearRoughStates(command.Actor, outgoing.Id);
        builder.RemoveEffectsFor(outgoing.Id);
        builder.MoveCard(incoming.Id, CardZone.Oche);
        builder.RoundUsage = builder.RoundUsage with
        {
            TaxisUsed = builder.RoundUsage.TaxisUsed + 1,
        };
        return HandlerResult.Accepted;
    }

    private HandlerResult UsePartyTrick(MatchBuilder builder, MatchCommand.UsePartyTrick command)
    {
        return UsePartyTrick(builder, command, false, []);
    }

    private HandlerResult UsePartyTrick(
        MatchBuilder builder,
        MatchCommand.UsePartyTrick command,
        bool isResuming,
        FrozenList<bool> beerMatResults
    )
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var source = builder.FindCard(command.Source);
        var trick = _catalog.PartyTrick(command.Effect);
        var houseRule = _catalog.HouseRule(command.Effect);
        if (source is null || (trick is null && houseRule is null))
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectNotFound);
        }

        var isActivatedTrick =
            trick is not null
            && source.Owner == command.Actor
            && IsInPlay(source)
            && _catalog
                .PartyTricks(source)
                .Any(candidate =>
                    candidate.MechanicalId == command.Effect.Value
                    && candidate.Trigger == BlokemonTrigger.Activated
                );
        var isActivatedLocalRule =
            houseRule is not null
            && source.Kind == CardKind.Kit
            && source.Zone == CardZone.Local
            && _catalog
                .HouseRules(source)
                .Any(candidate =>
                    candidate.MechanicalId == command.Effect.Value
                    && ContainsOpcode(candidate.Program, BlokemonOpcode.OncePerRound)
                );
        if (
            (!isActivatedTrick && !isActivatedLocalRule)
            || builder.RoundUsage.EffectsUsed.Contains(command.Effect)
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectUnavailable);
        }

        var program = trick?.Program ?? houseRule!.Program;
        if (!isResuming)
        {
            var requirements = _interpreter.InspectChoices(
                builder,
                command.Actor,
                source,
                command.Effect,
                program
            );
            var choiceRejection = _interpreter.ValidateChoiceSubmission(
                command.Choices,
                requirements,
                command.Actor
            );
            if (choiceRejection is not null)
            {
                return HandlerResult.Reject(choiceRejection.Value, requirements);
            }
        }

        var plan = _interpreter.Plan(
            builder,
            command.Actor,
            source,
            command.Effect,
            program,
            command.Choices,
            false,
            isActivatedLocalRule,
            beerMatResults
        );
        if (!plan.IsApplied)
        {
            if (plan.Rejection != CommandRejectionCode.ChoiceRequired)
            {
                return HandlerResult.Reject(
                    plan.Rejection ?? CommandRejectionCode.InvalidChoice,
                    plan.Requirements
                );
            }

            return PendEffect(
                builder,
                command,
                source.Id,
                command.Effect,
                plan.Requirements,
                beerMatResults,
                plan.BeerMatResults,
                false
            );
        }

        var execution = _interpreter.Execute(
            builder,
            command.Actor,
            source,
            command.Effect,
            program,
            command.Choices,
            false,
            isActivatedLocalRule,
            beerMatResults: beerMatResults
        );
        if (!execution.IsApplied)
        {
            return HandlerResult.Reject(
                execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                execution.Requirements
            );
        }

        ResolveSendHome(builder, execution.ForcedSendHome);
        ResolveVoluntarySourceChuck(builder, source, execution);
        return HandlerResult.Accepted;
    }

    private HandlerResult Attack(MatchBuilder builder, MatchCommand.Attack command)
    {
        return Attack(builder, command, false, false, []);
    }

    private HandlerResult Attack(
        MatchBuilder builder,
        MatchCommand.Attack command,
        bool isResuming,
        bool attackStarted,
        FrozenList<bool> beerMatResults
    )
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var attacker = builder.FindCard(command.Attacker);
        var attack = _catalog.Attack(command.AttackId);
        if (attacker is null || attack is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectNotFound);
        }

        if (
            attacker.Owner != command.Actor
            || !_catalog
                .Attacks(attacker)
                .Any(candidate => candidate.MechanicalId == command.AttackId.Value)
            || (
                attacker.Zone != CardZone.Oche
                && !(attacker.Zone == CardZone.Booth && attack.CanBeUsedFromBench)
            )
            || (
                command.Actor == builder.OpeningPlayer
                && builder.Player(command.Actor).RoundsStarted == 1
            )
            || attacker.RoughStates.Any(entry =>
                entry.State is BlokemonRoughState.NoddedOff or BlokemonRoughState.Legless
            )
            || builder.Effects.Any(effect =>
                effect.TargetCard == attacker.Id
                && effect.Kind == TemporaryEffectKind.RestrictAttack
            )
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectUnavailable);
        }

        if (!CanPayAttack(builder, attacker, attack))
        {
            return HandlerResult.Reject(CommandRejectionCode.InsufficientVim);
        }

        var requirements = _interpreter.InspectChoices(
            builder,
            command.Actor,
            attacker,
            command.AttackId,
            attack.Program
        );
        if (!isResuming)
        {
            var choiceRejection = _interpreter.ValidateChoiceSubmission(
                command.Choices,
                requirements,
                command.Actor
            );
            if (choiceRejection is not null)
            {
                return HandlerResult.Reject(choiceRejection.Value, requirements);
            }

            var deferred = requirements
                .Where(requirement => requirement.Chooser != command.Actor)
                .ToArray();
            if (deferred.Length > 0)
            {
                var chooser = deferred[0].Chooser;
                if (deferred.Any(requirement => requirement.Chooser != chooser))
                {
                    return HandlerResult.Reject(CommandRejectionCode.InvalidChoice, requirements);
                }

                builder.PendingEffect = new PendingEffectResolution(
                    command,
                    attacker.Id,
                    command.AttackId,
                    chooser,
                    FrozenList<ChoiceRequirement>.Create(deferred),
                    beerMatResults,
                    false
                );
                builder.Phase = MatchPhase.AwaitingEffectChoice;
                builder.Events.Add(
                    new PendingMatchEvent(
                        MatchEventKind.EffectChoiceRequested,
                        chooser,
                        attacker.Id,
                        Effect: command.AttackId
                    )
                );
                return HandlerResult.Accepted;
            }
        }

        var defendingCard = builder.Oche(builder.Other(command.Actor));
        var defendingDamageBefore = defendingCard?.Damage ?? 0;

        if (!attackStarted)
        {
            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.AttackDeclared,
                    command.Actor,
                    attacker.Id,
                    Effect: command.AttackId
                )
            );
            if (attacker.RoughStates.Any(entry => entry.State == BlokemonRoughState.Muddled))
            {
                var badge = builder.Random.NextInt(2) == 1;
                builder.Events.Add(
                    new PendingMatchEvent(
                        MatchEventKind.BeerMatTossed,
                        command.Actor,
                        attacker.Id,
                        Effect: command.AttackId,
                        BadgeSide: badge
                    )
                );
                if (!badge)
                {
                    builder.PlaceDamage(
                        command.Actor,
                        attacker.Id,
                        30,
                        DamageKind.PlacedCounter,
                        attacker.Id
                    );
                    builder.Events.Add(
                        new PendingMatchEvent(
                            MatchEventKind.AttackCancelled,
                            command.Actor,
                            attacker.Id,
                            Effect: command.AttackId
                        )
                    );
                    ResolveSendHome(builder, []);
                    FinishOrPendRound(builder);
                    return HandlerResult.Accepted;
                }
            }
        }

        var plan = _interpreter.Plan(
            builder,
            command.Actor,
            attacker,
            command.AttackId,
            attack.Program,
            command.Choices,
            true,
            beerMatResults: beerMatResults
        );
        if (!plan.IsApplied)
        {
            if (plan.Rejection != CommandRejectionCode.ChoiceRequired)
            {
                return HandlerResult.Reject(
                    plan.Rejection ?? CommandRejectionCode.InvalidChoice,
                    plan.Requirements
                );
            }

            var pending = PendEffect(
                builder,
                command,
                attacker.Id,
                command.AttackId,
                plan.Requirements,
                beerMatResults,
                plan.BeerMatResults,
                true
            );
            return pending;
        }

        var execution = _interpreter.Execute(
            builder,
            command.Actor,
            attacker,
            command.AttackId,
            attack.Program,
            command.Choices,
            true,
            beerMatResults: beerMatResults
        );
        if (!execution.IsApplied)
        {
            return HandlerResult.Reject(
                execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                execution.Requirements
            );
        }

        ResolveReactiveAttackTriggers(builder, attacker, defendingCard, defendingDamageBefore);
        if (!ResolveSendHome(builder, execution.ForcedSendHome, attacker.Id, true))
        {
            return HandlerResult.Accepted;
        }

        FinishOrPendRound(builder);
        return HandlerResult.Accepted;
    }

    private HandlerResult ResolveEffectChoice(
        MatchBuilder builder,
        MatchCommand.ResolveEffectChoice command
    )
    {
        var pending = builder.PendingEffect;
        if (builder.Phase != MatchPhase.AwaitingEffectChoice || pending is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        if (pending.Chooser != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongChooser, pending.Requirements);
        }

        var choiceRejection = _interpreter.ValidateChoiceSubmission(
            command.Choices,
            pending.Requirements,
            command.Actor
        );
        if (choiceRejection is not null)
        {
            return HandlerResult.Reject(choiceRejection.Value, pending.Requirements);
        }

        builder.PendingEffect = null;
        builder.Phase = MatchPhase.Playing;
        var resumed = WithChoices(
            pending.Command,
            FrozenList<EffectChoice>.Create(pending.Command.Choices.Concat(command.Choices))
        );
        return resumed switch
        {
            MatchCommand.Attack attack => Attack(
                builder,
                attack,
                true,
                pending.AttackStarted,
                pending.BeerMatResults
            ),
            MatchCommand.PlayKit playKit => PlayKit(builder, playKit, true, pending.BeerMatResults),
            MatchCommand.UsePartyTrick usePartyTrick => UsePartyTrick(
                builder,
                usePartyTrick,
                true,
                pending.BeerMatResults
            ),
            _ => HandlerResult.Reject(CommandRejectionCode.InvalidChoice),
        };
    }

    private HandlerResult PendEffect(
        MatchBuilder builder,
        MatchCommand command,
        CardInstanceId source,
        EffectId effect,
        FrozenList<ChoiceRequirement> requirements,
        FrozenList<bool> recordedBeerMats,
        FrozenList<bool> plannedBeerMats,
        bool attackStarted
    )
    {
        if (requirements.Count == 0)
        {
            return HandlerResult.Reject(CommandRejectionCode.InvalidChoice);
        }

        var chooser = requirements[0].Chooser;
        if (requirements.Any(requirement => requirement.Chooser != chooser))
        {
            return HandlerResult.Reject(CommandRejectionCode.InvalidChoice, requirements);
        }

        foreach (var expected in plannedBeerMats.Skip(recordedBeerMats.Count))
        {
            var actual = builder.Random.NextInt(2) == 1;
            if (actual != expected)
            {
                return HandlerResult.Reject(CommandRejectionCode.AuthorityMismatch);
            }

            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.BeerMatTossed,
                    command.Actor,
                    source,
                    Effect: effect,
                    BadgeSide: actual
                )
            );
        }

        builder.PendingEffect = new PendingEffectResolution(
            command,
            source,
            effect,
            chooser,
            requirements,
            plannedBeerMats,
            attackStarted
        );
        builder.Phase = MatchPhase.AwaitingEffectChoice;
        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.EffectChoiceRequested,
                chooser,
                source,
                Effect: effect
            )
        );
        return HandlerResult.Accepted;
    }

    private static MatchCommand WithChoices(
        MatchCommand command,
        FrozenList<EffectChoice> choices
    ) =>
        command switch
        {
            MatchCommand.Attack attack => attack with { Choices = choices },
            MatchCommand.PlayKit playKit => playKit with { Choices = choices },
            MatchCommand.UsePartyTrick usePartyTrick => usePartyTrick with { Choices = choices },
            _ => command,
        };

    private HandlerResult ResolveKnockoutTrigger(
        MatchBuilder builder,
        MatchCommand.ResolveKnockoutTrigger command
    )
    {
        var pending = builder.PendingKnockout;
        if (builder.Phase != MatchPhase.AwaitingTriggerChoice || pending is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        if (pending.Chooser != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongChooser);
        }

        if (command.Vim is { } vim && !pending.EligibleVim.Contains(vim))
        {
            return HandlerResult.Reject(CommandRejectionCode.InvalidChoice);
        }

        var source = builder.Card(pending.TriggerSource);
        var trick = _catalog
            .PartyTricks(source)
            .Single(value => new EffectId(value.MechanicalId) == pending.TriggerEffect);
        var context = new TriggerContext(pending.KnockedOutCard, pending.AttackingCard);
        var requirements = _interpreter.InspectChoices(
            builder,
            command.Actor,
            source,
            pending.TriggerEffect,
            trick.Program,
            context
        );
        var optional = requirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Optional
        );
        var acceptedOptional = new EffectChoice.Optional(optional.Id, command.Vim is not null);
        if (command.Vim is not null)
        {
            requirements = _interpreter
                .Plan(
                    builder,
                    command.Actor,
                    source,
                    pending.TriggerEffect,
                    trick.Program,
                    FrozenList<EffectChoice>.Create(acceptedOptional),
                    false,
                    triggerContext: context
                )
                .Requirements;
        }

        var choices = new List<EffectChoice> { acceptedOptional };
        if (command.Vim is { } selected)
        {
            var cards = requirements.Single(requirement =>
                requirement.Kind == ChoiceRequirementKind.Cards
            );
            choices.Add(
                new EffectChoice.Cards(cards.Id, FrozenList<CardInstanceId>.Create(selected))
            );
        }

        var execution = _interpreter.Execute(
            builder,
            command.Actor,
            source,
            pending.TriggerEffect,
            trick.Program,
            FrozenList<EffectChoice>.Create(choices),
            false,
            triggerContext: context
        );
        if (!execution.IsApplied)
        {
            return HandlerResult.Reject(
                execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                execution.Requirements
            );
        }

        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.TriggerResolved,
                command.Actor,
                pending.TriggerSource,
                command.Vim is { } moved
                    ? FrozenList<CardInstanceId>.Create(moved)
                    : FrozenList<CardInstanceId>.Empty,
                pending.TriggerEffect
            )
        );

        var remainingVim = pending
            .EligibleVim.Where(vim => builder.Card(vim).AttachedTo == pending.KnockedOutCard)
            .ToArray();
        if (pending.TriggerSources.Count > 0 && remainingVim.Length > 0)
        {
            var nextSource = pending.TriggerSources[0];
            var nextTrick = _catalog
                .PartyTricks(builder.Card(nextSource))
                .Single(value =>
                    value.Trigger == BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
                );
            builder.PendingKnockout = pending with
            {
                TriggerSources = FrozenList<CardInstanceId>.Create(pending.TriggerSources.Skip(1)),
                TriggerSource = nextSource,
                TriggerEffect = new EffectId(nextTrick.MechanicalId),
                EligibleVim = FrozenList<CardInstanceId>.Create(remainingVim),
            };
            return HandlerResult.Accepted;
        }

        builder.PendingKnockout = null;
        builder.Phase = MatchPhase.Playing;
        SendHomeOne(builder, builder.Card(pending.KnockedOutCard), pending.AttackingCard);
        var completed = ResolveSendHome(
            builder,
            pending.RemainingKnockouts,
            pending.AttackingCard,
            pending.FinishRoundAfterResolution
        );
        if (completed && pending.FinishRoundAfterResolution)
        {
            FinishOrPendRound(builder);
        }

        return HandlerResult.Accepted;
    }

    private HandlerResult ResolveBarChitTrigger(
        MatchBuilder builder,
        MatchCommand.ResolveBarChitTrigger command
    )
    {
        var pending = builder.PendingBarChits.FirstOrDefault();
        if (builder.Phase != MatchPhase.AwaitingTriggerChoice || pending is null)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        if (pending.Player != command.Actor)
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongChooser);
        }

        var card = builder.Card(pending.Card);
        if (
            command.PutOntoBooth
            && (
                card.Zone != CardZone.Mitt
                || builder.CardsIn(command.Actor, CardZone.Booth).Count()
                    >= _catalog.Manifest.BaseRules.Opening.BoothLimit
            )
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.InvalidChoice);
        }

        builder.RemoveBarChit(pending);
        var trick = _catalog
            .PartyTricks(card)
            .Single(value => new EffectId(value.MechanicalId) == pending.Effect);
        var requirements = _interpreter.InspectChoices(
            builder,
            command.Actor,
            card,
            pending.Effect,
            trick.Program
        );
        var optional = requirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Optional
        );
        var execution = _interpreter.Execute(
            builder,
            command.Actor,
            card,
            pending.Effect,
            trick.Program,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Optional(optional.Id, command.PutOntoBooth)
            ),
            false
        );
        if (!execution.IsApplied)
        {
            return HandlerResult.Reject(
                execution.Rejection ?? CommandRejectionCode.InvalidChoice,
                execution.Requirements
            );
        }

        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.TriggerResolved,
                command.Actor,
                card.Id,
                Effect: pending.Effect,
                Amount: command.PutOntoBooth ? 1 : 0
            )
        );
        ResolveWins(builder);
        if (builder.Phase == MatchPhase.Complete)
        {
            return HandlerResult.Accepted;
        }

        if (builder.PendingBarChits.Any())
        {
            builder.Phase = MatchPhase.AwaitingTriggerChoice;
            return HandlerResult.Accepted;
        }

        builder.Phase = MatchPhase.Playing;
        if (pending.FinishRoundAfterResolution)
        {
            FinishOrPendRound(builder);
        }

        return HandlerResult.Accepted;
    }

    private HandlerResult ChuckFossil(MatchBuilder builder, MatchCommand.ChuckFossil command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        var fossil = builder.FindCard(command.Fossil);
        if (
            fossil is null
            || fossil.Owner != command.Actor
            || fossil.Kind != CardKind.Kit
            || !_catalog.IsFossil(fossil.MechanicalId)
            || !IsInPlay(fossil)
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.EffectUnavailable);
        }

        builder.ChuckBloke(fossil.Id);
        if (fossil.Zone == CardZone.Oche)
        {
            AssignReplacement(builder, command.Actor);
        }

        return HandlerResult.Accepted;
    }

    private HandlerResult EndRound(MatchBuilder builder, MatchCommand.EndRound command)
    {
        var turn = ValidatePlayingTurn(builder, command.Actor);
        if (turn is not null)
        {
            return HandlerResult.Reject(turn.Value);
        }

        FinishOrPendRound(builder);
        return HandlerResult.Accepted;
    }

    private HandlerResult ChooseReplacement(
        MatchBuilder builder,
        MatchCommand.ChooseReplacement command
    )
    {
        if (
            builder.Phase != MatchPhase.AwaitingReplacement
            || builder.ReplacementPlayer != command.Actor
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongPhase);
        }

        var replacement = builder.FindCard(command.BoothBloke);
        if (
            replacement is null
            || replacement.Owner != command.Actor
            || replacement.Zone != CardZone.Booth
        )
        {
            return HandlerResult.Reject(CommandRejectionCode.WrongZone);
        }

        builder.MoveCard(replacement.Id, CardZone.Oche);
        builder.ReplacementPlayer = NextReplacement(builder);
        if (builder.ReplacementPlayer is null)
        {
            if (builder.PendingRoundEnd)
            {
                builder.PendingRoundEnd = false;
                CompleteRound(builder);
            }
            else
            {
                builder.Phase = MatchPhase.Playing;
            }
        }

        return HandlerResult.Accepted;
    }

    private void FinishOrPendRound(MatchBuilder builder)
    {
        if (builder.Phase == MatchPhase.Complete)
        {
            return;
        }

        builder.PendingRoundEnd = true;
        if (builder.ReplacementPlayer is not null)
        {
            builder.Phase = MatchPhase.AwaitingReplacement;
            return;
        }

        CompleteRound(builder);
    }

    private void CompleteRound(MatchBuilder builder)
    {
        var completedPlayer = builder.ActivePlayer;
        builder.Events.Add(new PendingMatchEvent(MatchEventKind.RoundEnded, completedPlayer));
        RunCheckup(builder, completedPlayer);
        ResolveSendHome(builder, []);
        if (builder.Phase == MatchPhase.Complete)
        {
            builder.PendingRoundEnd = false;
            return;
        }

        if (builder.ReplacementPlayer is not null)
        {
            builder.Phase = MatchPhase.AwaitingReplacement;
            return;
        }

        builder.PendingRoundEnd = false;
        builder.ExpireEffects(builder.RoundNumber);
        StartRound(builder, builder.Other(completedPlayer));
    }

    private void RunCheckup(MatchBuilder builder, PlayerId completedPlayer)
    {
        foreach (var player in builder.Players.Select(static player => player.Id))
        {
            var oche = builder.Oche(player);
            if (oche is null)
            {
                continue;
            }

            foreach (var roughState in _catalog.Manifest.BaseRules.Checkup.RoughStateOrder)
            {
                var current = builder.Card(oche.Id);
                if (!current.RoughStates.Any(entry => entry.State == roughState))
                {
                    continue;
                }

                switch (roughState)
                {
                    case BlokemonRoughState.DodgyPint:
                        builder.PlaceDamage(player, current.Id, 10, DamageKind.RoughState);
                        break;
                    case BlokemonRoughState.Singed:
                        builder.PlaceDamage(player, current.Id, 20, DamageKind.RoughState);
                        if (TossCheckup(builder, player, current.Id))
                        {
                            builder.ClearRoughStates(player, current.Id, roughState);
                        }
                        break;
                    case BlokemonRoughState.NoddedOff:
                        if (TossCheckup(builder, player, current.Id))
                        {
                            builder.ClearRoughStates(player, current.Id, roughState);
                        }
                        break;
                    case BlokemonRoughState.Legless:
                        var entry = current.RoughStates.Single(value => value.State == roughState);
                        if (builder.Player(player).RoundsStarted > entry.AppliedAtOwnerRound)
                        {
                            builder.ClearRoughStates(player, current.Id, roughState);
                        }
                        break;
                    case BlokemonRoughState.Muddled:
                        break;
                }
            }
        }

        foreach (
            var effect in builder
                .Effects.Where(effect => effect.Kind == TemporaryEffectKind.EndRoundEffect)
                .ToArray()
        )
        {
            var kit = builder.FindCard(effect.SourceCard);
            if (
                kit?.AttachedTo is { } targetId
                && effect.Owner == completedPlayer
                && builder.Card(targetId).Zone == CardZone.Oche
            )
            {
                builder.Heal(effect.Owner, targetId, effect.Amount, effect.SourceCard);
            }
            else if (
                effect.TargetCard is { } deferredTarget
                && effect.ExpiresAfterRound <= builder.RoundNumber
            )
            {
                builder.PlaceDamage(
                    effect.Owner,
                    deferredTarget,
                    effect.Amount * 10,
                    DamageKind.PlacedCounter,
                    effect.SourceCard
                );
                builder.RemoveEffect(effect);
            }
        }
    }

    private static bool TossCheckup(MatchBuilder builder, PlayerId player, CardInstanceId card)
    {
        var badge = builder.Random.NextInt(2) == 1;
        builder.Events.Add(
            new PendingMatchEvent(MatchEventKind.BeerMatTossed, player, card, BadgeSide: badge)
        );
        return badge;
    }

    private bool ResolveSendHome(
        MatchBuilder builder,
        FrozenList<CardInstanceId> forcedSendHome,
        CardInstanceId? attackingCard = null,
        bool finishRoundAfterResolution = false
    )
    {
        var candidates = builder
            .Cards.Where(IsInPlay)
            .Where(card =>
                forcedSendHome.Contains(card.Id)
                || card.Damage >= EffectiveStayingPower(builder, card)
            )
            .OrderBy(static card => card.Owner)
            .ThenBy(static card => card.Id)
            .ToList();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var current = builder.FindCard(candidate.Id);
            if (current is null || !IsInPlay(current))
            {
                continue;
            }

            if (
                attackingCard is { } recoverAttacker
                && TryRecover(builder, current, recoverAttacker)
            )
            {
                continue;
            }

            if (
                attackingCard is { } attacker
                && QueueKnockoutTrigger(
                    builder,
                    current,
                    attacker,
                    FrozenList<CardInstanceId>.Create(
                        candidates.Skip(index + 1).Select(static card => card.Id)
                    ),
                    finishRoundAfterResolution
                )
            )
            {
                return false;
            }

            if (SendHomeOne(builder, current, attackingCard) && attackingCard is { } reflected)
            {
                var attackerState = builder.FindCard(reflected);
                if (attackerState is not null && IsInPlay(attackerState))
                {
                    candidates.Add(attackerState);
                }
            }
        }

        ResolveWins(builder);
        if (builder.Phase != MatchPhase.Complete && builder.PendingBarChits.Any())
        {
            builder.Phase = MatchPhase.AwaitingTriggerChoice;
            return false;
        }

        return true;
    }

    private bool TryRecover(MatchBuilder builder, CardState card, CardInstanceId attackingCard)
    {
        var recovery = _catalog
            .PartyTricks(card)
            .FirstOrDefault(static trick =>
                trick.Trigger == BlokemonTrigger.BeforeSelfSentHomeByAttackDamage
            );
        if (recovery is null)
        {
            return false;
        }

        var effect = new EffectId(recovery.MechanicalId);
        var execution = _interpreter.Execute(
            builder,
            card.Owner,
            card,
            effect,
            recovery.Program,
            [],
            false,
            triggerContext: new TriggerContext(card.Id, attackingCard)
        );
        if (!execution.IsApplied)
        {
            return false;
        }

        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.TriggerResolved,
                card.Owner,
                card.Id,
                Effect: effect
            )
        );
        var recovered = builder.Card(card.Id);
        return recovered.Damage < EffectiveStayingPower(builder, recovered);
    }

    private bool QueueKnockoutTrigger(
        MatchBuilder builder,
        CardState knockedOut,
        CardInstanceId attackingCard,
        FrozenList<CardInstanceId> remainingKnockouts,
        bool finishRoundAfterResolution
    )
    {
        var sources = builder
            .Cards.Where(card =>
                card.Owner == knockedOut.Owner && card.Id != knockedOut.Id && IsInPlay(card)
            )
            .Where(card =>
                _catalog
                    .PartyTricks(card)
                    .Any(static trick =>
                        trick.Trigger == BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
                    )
            )
            .OrderBy(static card => card.Id)
            .ToArray();
        if (sources.Length == 0)
        {
            return false;
        }

        var first = sources[0];
        var trick = _catalog
            .PartyTricks(first)
            .Single(static value =>
                value.Trigger == BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
            );
        var effect = new EffectId(trick.MechanicalId);
        var requirements = _interpreter.InspectChoices(
            builder,
            knockedOut.Owner,
            first,
            effect,
            trick.Program,
            new TriggerContext(knockedOut.Id, attackingCard)
        );
        var optional = requirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Optional
        );
        var branch = _interpreter.Plan(
            builder,
            knockedOut.Owner,
            first,
            effect,
            trick.Program,
            FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, true)),
            false,
            triggerContext: new TriggerContext(knockedOut.Id, attackingCard)
        );
        var eligibleVim = branch
            .Requirements.Where(requirement => requirement.Kind == ChoiceRequirementKind.Cards)
            .SelectMany(static requirement => requirement.EligibleCards)
            .Distinct()
            .Order()
            .ToArray();
        if (eligibleVim.Length == 0)
        {
            return false;
        }

        builder.PendingKnockout = new PendingKnockoutResolution(
            knockedOut.Id,
            remainingKnockouts,
            FrozenList<CardInstanceId>.Create(sources.Skip(1).Select(static card => card.Id)),
            first.Id,
            effect,
            knockedOut.Owner,
            FrozenList<CardInstanceId>.Create(eligibleVim),
            attackingCard,
            finishRoundAfterResolution
        );
        builder.Phase = MatchPhase.AwaitingTriggerChoice;
        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.TriggerQueued,
                knockedOut.Owner,
                first.Id,
                FrozenList<CardInstanceId>.Create(knockedOut.Id),
                effect
            )
        );
        return true;
    }

    private bool SendHomeOne(MatchBuilder builder, CardState current, CardInstanceId? attackingCard)
    {
        var retaliation =
            attackingCard is not null && current.Zone == CardZone.Oche
                ? _catalog
                    .PartyTricks(current)
                    .FirstOrDefault(static trick =>
                        trick.Trigger == BlokemonTrigger.AfterSelfSentHomeByAttackDamage
                    )
                : null;
        var wasOche = current.Zone == CardZone.Oche;
        builder.ChuckBloke(current.Id);
        builder.Events.Add(
            new PendingMatchEvent(
                MatchEventKind.BlokeSentHome,
                builder.Other(current.Owner),
                current.Id,
                FrozenList<CardInstanceId>.Create(current.Id)
            )
        );
        var takingPlayer = builder.Other(current.Owner);
        var taken = builder.TakeBarChits(takingPlayer, _catalog.BarChits(current), current.Id);
        QueueBarChitTriggers(builder, takingPlayer, taken, attackingCard is not null);
        var retaliates = false;
        if (retaliation is not null && attackingCard is { } attacker)
        {
            var effect = new EffectId(retaliation.MechanicalId);
            var execution = _interpreter.Execute(
                builder,
                current.Owner,
                current,
                effect,
                retaliation.Program,
                [],
                false,
                triggerContext: new TriggerContext(current.Id, attacker)
            );
            retaliates = execution.ForcedSendHome.Contains(attacker);
            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.TriggerResolved,
                    current.Owner,
                    current.Id,
                    execution.ForcedSendHome,
                    effect
                )
            );
        }

        if (wasOche)
        {
            AssignReplacement(builder, current.Owner);
        }

        return retaliates;
    }

    private void QueueBarChitTriggers(
        MatchBuilder builder,
        PlayerId player,
        FrozenList<CardInstanceId> cards,
        bool finishRoundAfterResolution
    )
    {
        foreach (var cardId in cards)
        {
            var card = builder.Card(cardId);
            var trick = _catalog
                .PartyTricks(card)
                .FirstOrDefault(static value => value.Trigger == BlokemonTrigger.OnBarChitTaken);
            if (
                trick is null
                || builder.CardsIn(player, CardZone.Booth).Count()
                    >= _catalog.Manifest.BaseRules.Opening.BoothLimit
            )
            {
                continue;
            }

            var pending = new PendingBarChitResolution(
                player,
                cardId,
                new EffectId(trick.MechanicalId),
                finishRoundAfterResolution
            );
            builder.QueueBarChit(pending);
            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.TriggerQueued,
                    player,
                    cardId,
                    Effect: pending.Effect
                )
            );
        }
    }

    private void ResolveReactiveAttackTriggers(
        MatchBuilder builder,
        CardState attacker,
        CardState? defenderBefore,
        int damageBefore
    )
    {
        if (defenderBefore is null)
        {
            return;
        }

        var defender = builder.FindCard(defenderBefore.Id);
        if (defender is null || defender.Damage <= damageBefore)
        {
            return;
        }

        foreach (
            var trick in _catalog
                .PartyTricks(defender)
                .Where(static trick => trick.Trigger == BlokemonTrigger.AfterSelfDamagedByAttack)
        )
        {
            var effect = new EffectId(trick.MechanicalId);
            _interpreter.Execute(
                builder,
                defender.Owner,
                defender,
                effect,
                trick.Program,
                [],
                false,
                triggerContext: new TriggerContext(defender.Id, attacker.Id)
            );
            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.TriggerResolved,
                    defender.Owner,
                    defender.Id,
                    FrozenList<CardInstanceId>.Create(attacker.Id),
                    effect
                )
            );
        }
    }

    private void AssignReplacement(MatchBuilder builder, PlayerId player)
    {
        if (builder.Oche(player) is null && builder.CardsIn(player, CardZone.Booth).Any())
        {
            builder.ReplacementPlayer ??= player;
            builder.Phase = MatchPhase.AwaitingReplacement;
        }
    }

    private void ResolveVoluntarySourceChuck(
        MatchBuilder builder,
        CardState sourceBeforeExecution,
        BlokemonInterpreter.InterpreterExecution execution
    )
    {
        if (!execution.SourceChucked)
        {
            return;
        }

        if (sourceBeforeExecution.Zone == CardZone.Oche)
        {
            AssignReplacement(builder, sourceBeforeExecution.Owner);
        }

        ResolveWins(builder);
    }

    private PlayerId? NextReplacement(MatchBuilder builder) =>
        builder
            .Players.Select(static player => player.Id)
            .FirstOrDefault(player =>
                builder.Oche(player) is null && builder.CardsIn(player, CardZone.Booth).Any()
            )
            is var value
        && value != default
            ? value
            : null;

    private void ResolveWins(MatchBuilder builder, PlayerId? failedRequiredDraw = null)
    {
        var methods = builder.Players.ToDictionary(static player => player.Id, static _ => 0);
        foreach (var player in builder.Players)
        {
            if (builder.Player(player.Id).BarChitsRemaining == 0)
            {
                methods[player.Id]++;
            }

            var other = builder.Other(player.Id);
            if (!builder.Cards.Any(card => card.Owner == other && IsInPlay(card)))
            {
                methods[player.Id]++;
            }

            if (failedRequiredDraw == other)
            {
                methods[player.Id]++;
            }
        }

        var winners = methods.Where(static pair => pair.Value > 0).ToArray();
        if (winners.Length == 0)
        {
            return;
        }

        if (winners.Length == 1 || winners[0].Value != winners[1].Value)
        {
            var winner = winners.OrderByDescending(static pair => pair.Value).First().Key;
            builder.Winner = winner;
            builder.Phase = MatchPhase.Complete;
            builder.ReplacementPlayer = null;
            builder.Events.Add(new PendingMatchEvent(MatchEventKind.MatchWon, winner));
            return;
        }

        builder.SuddenDeathCount++;
        foreach (var player in builder.Players.ToArray())
        {
            builder.ResetBarChits(player.Id, _catalog.Manifest.BaseRules.Win.SuddenDeathBarChits);
        }

        builder.Events.Add(new PendingMatchEvent(MatchEventKind.SuddenDeathStarted));
    }

    private void StartRound(MatchBuilder builder, PlayerId player)
    {
        builder.ActivePlayer = player;
        builder.RoundNumber++;
        var playerState = builder.Player(player);
        builder.SetPlayer(playerState with { RoundsStarted = playerState.RoundsStarted + 1 });
        builder.RoundUsage = RoundUsage.Empty(player);
        builder.Phase = MatchPhase.Playing;
        builder.Events.Add(new PendingMatchEvent(MatchEventKind.RoundStarted, player));
        if (!builder.CardsIn(player, CardZone.Stack).Any())
        {
            ResolveWins(builder, player);
            return;
        }

        builder.Draw(player, 1, DrawReason.RequiredRoundDraw);
    }

    private CommandRejectionCode? ValidateCommandBoundary(MatchState state, MatchCommand command)
    {
        if (command.MatchId != state.Id)
        {
            return CommandRejectionCode.WrongMatch;
        }

        if (state.ProcessedCommands.Contains(command.Id))
        {
            return CommandRejectionCode.DuplicateCommand;
        }

        if (command.ExpectedRevision != state.Revision)
        {
            return CommandRejectionCode.StaleRevision;
        }

        if (
            !StringComparer.Ordinal.Equals(
                state.AuthorityVersion,
                _catalog.Manifest.ManifestVersion
            )
        )
        {
            return CommandRejectionCode.AuthorityMismatch;
        }

        if (!state.Players.Any(player => player.Id == command.Actor))
        {
            return CommandRejectionCode.UnknownActor;
        }

        return state.Phase == MatchPhase.Complete ? CommandRejectionCode.MatchComplete : null;
    }

    private static CommandRejectionCode? ValidatePlayingTurn(MatchBuilder builder, PlayerId actor)
    {
        if (builder.Phase != MatchPhase.Playing)
        {
            return CommandRejectionCode.WrongPhase;
        }

        return builder.ActivePlayer != actor ? CommandRejectionCode.NotActorsTurn : null;
    }

    private bool CanPayAttack(MatchBuilder builder, CardState attacker, BlokemonAttack attack)
    {
        if (
            builder.Effects.Any(effect =>
                effect.TargetCard == attacker.Id
                && effect.Kind == TemporaryEffectKind.ModifyAttackCost
                && EffectMatchesCardRank(effect, attacker)
                && effect.Amount < 0
            )
        )
        {
            return true;
        }

        var costs = attack.VimCost.ToList();
        foreach (
            var effect in builder.Effects.Where(effect =>
                effect.TargetCard == attacker.Id
                && effect.Kind == TemporaryEffectKind.ModifyAttackCost
                && EffectMatchesCardRank(effect, attacker)
                && effect.Amount > 0
            )
        )
        {
            costs.AddRange(
                effect.MechanicalTypes.Count == 0
                    ? Enumerable.Repeat(BlokemonMechanicalType.Colorless, effect.Amount)
                    : effect.MechanicalTypes
            );
        }

        var available = attacker
            .Attachments.Select(builder.Card)
            .Where(static card => card.Kind == CardKind.Vim)
            .Select(card => _catalog.Vim(card.MechanicalId).MechanicalType)
            .ToList();
        foreach (
            var typedCost in costs.Where(static cost => cost != BlokemonMechanicalType.Colorless)
        )
        {
            var index = available.FindIndex(vim => vim == typedCost);
            if (index < 0)
            {
                return false;
            }

            available.RemoveAt(index);
        }

        return available.Count
            >= costs.Count(static cost => cost == BlokemonMechanicalType.Colorless);
    }

    private int EffectiveTaxiFare(MatchBuilder builder, CardState card)
    {
        var fare = _catalog.TaxiFare(card);
        foreach (
            var modifier in builder.Effects.Where(effect =>
                (effect.TargetCard == card.Id || effect.TargetCard is null)
                && effect.Kind == TemporaryEffectKind.ModifyTaxiFare
                && EffectMatchesCardRank(effect, card)
            )
        )
        {
            fare = modifier.MechanicalTypes.Count == 0 ? 0 : Math.Max(0, fare + modifier.Amount);
        }

        return fare;
    }

    private int EffectiveStayingPower(MatchBuilder builder, CardState card) =>
        _catalog.StayingPower(card)
        + builder
            .Effects.Where(effect =>
                effect.TargetCard == card.Id
                && effect.Kind == TemporaryEffectKind.ModifyStayingPower
                && EffectMatchesCardRank(effect, card)
            )
            .Sum(static effect => effect.Amount);

    private bool EffectMatchesCardRank(TemporaryEffect effect, CardState card)
    {
        if (card.Kind != CardKind.Bloke)
        {
            return !effect.Conditions.Any(condition =>
                condition
                    is BlokemonCondition.TargetIsRegular
                        or BlokemonCondition.TargetIsSeasoned
                        or BlokemonCondition.TargetIsLandlord
            );
        }

        var rank = _catalog.Bloke(card.MechanicalId).Rank;
        return (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsRegular)
                || rank == BlokemonRank.Regular
            )
            && (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsSeasoned)
                || rank == BlokemonRank.Seasoned
            )
            && (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsLandlord)
                || rank == BlokemonRank.Landlord
            );
    }

    private IEnumerable<LegalAction> MulliganBonusActions(MatchState state, PlayerId actor)
    {
        var player = state.Player(actor);
        if (player.MulliganBonusChosen || player.MulliganBonusAllowance == 0)
        {
            return [];
        }

        return Enumerable
            .Range(0, player.MulliganBonusAllowance + 1)
            .Select(count => new LegalAction(
                LegalActionKind.ChooseMulliganBonus,
                new MatchCommand.ChooseMulliganBonus(
                    CpuCommandId(state, $"bonus:{actor.Value}:{count}"),
                    state.Id,
                    actor,
                    state.Revision,
                    count
                ),
                [],
                $"bonus:{count:D3}"
            ));
    }

    private IEnumerable<LegalAction> OpeningActions(MatchState state, PlayerId actor)
    {
        if (state.Player(actor).OpeningChosen)
        {
            return [];
        }

        var regulars = state
            .CardsIn(actor, CardZone.Mitt)
            .Where(card => card.Kind == CardKind.Bloke && _catalog.IsRegular(card.MechanicalId))
            .ToArray();
        return regulars.Select(oche =>
        {
            var booth = regulars.Where(card => card.Id != oche.Id).Select(static card => card.Id);
            var requirement = new ChoiceRequirement(
                new EffectChoiceId("opening:booth"),
                ChoiceRequirementKind.Cards,
                actor,
                0,
                Math.Min(_catalog.Manifest.BaseRules.Opening.BoothLimit, regulars.Length - 1),
                FrozenList<CardInstanceId>.Create(booth),
                [],
                [],
                null
            );
            return new LegalAction(
                LegalActionKind.ChooseOpening,
                new MatchCommand.ChooseOpening(
                    CpuCommandId(state, $"opening:{actor.Value}:{oche.Id.Value}"),
                    state.Id,
                    actor,
                    state.Revision,
                    oche.Id,
                    []
                ),
                FrozenList<ChoiceRequirement>.Create(requirement),
                $"opening:{oche.Id.Value}"
            );
        });
    }

    private IEnumerable<LegalAction> ReplacementActions(MatchState state, PlayerId actor)
    {
        if (state.ReplacementPlayer != actor)
        {
            return [];
        }

        return state
            .CardsIn(actor, CardZone.Booth)
            .Select(card => new LegalAction(
                LegalActionKind.ChooseReplacement,
                new MatchCommand.ChooseReplacement(
                    CpuCommandId(state, $"replacement:{card.Id.Value}"),
                    state.Id,
                    actor,
                    state.Revision,
                    card.Id
                ),
                [],
                $"replacement:{card.Id.Value}"
            ));
    }

    private IEnumerable<LegalAction> EffectChoiceActions(MatchState state, PlayerId actor)
    {
        var pending = state.PendingEffect;
        if (pending is null || pending.Chooser != actor)
        {
            return [];
        }

        var choices = StableChoices(pending.Requirements);
        return
        [
            new LegalAction(
                LegalActionKind.ResolveEffectChoice,
                new MatchCommand.ResolveEffectChoice(
                    CpuCommandId(state, $"choice:{pending.Command.Id.Value}"),
                    state.Id,
                    actor,
                    state.Revision,
                    choices
                ),
                pending.Requirements,
                $"choice:{pending.Command.Id.Value}"
            ),
        ];
    }

    private IEnumerable<LegalAction> TriggerChoiceActions(MatchState state, PlayerId actor) =>
        state.PendingKnockout is not null
            ? KnockoutTriggerActions(state, actor)
            : BarChitTriggerActions(state, actor);

    private IEnumerable<LegalAction> KnockoutTriggerActions(MatchState state, PlayerId actor)
    {
        var pending = state.PendingKnockout;
        if (pending is null || pending.Chooser != actor)
        {
            return [];
        }

        return pending
            .EligibleVim.Select<CardInstanceId, CardInstanceId?>(static vim => vim)
            .Prepend(null)
            .Select(vim => new LegalAction(
                LegalActionKind.ResolveKnockoutTrigger,
                new MatchCommand.ResolveKnockoutTrigger(
                    CpuCommandId(
                        state,
                        $"trigger:{pending.TriggerEffect.Value}:{vim?.Value ?? "decline"}"
                    ),
                    state.Id,
                    actor,
                    state.Revision,
                    vim
                ),
                [],
                vim is null ? "trigger:1:decline" : $"trigger:0:{vim.Value.Value}"
            ));
    }

    private IEnumerable<LegalAction> BarChitTriggerActions(MatchState state, PlayerId actor)
    {
        var pending = state.PendingBarChits.FirstOrDefault();
        if (pending is null || pending.Player != actor)
        {
            return [];
        }

        return new[] { true, false }.Select(putOntoBooth => new LegalAction(
            LegalActionKind.ResolveBarChitTrigger,
            new MatchCommand.ResolveBarChitTrigger(
                CpuCommandId(
                    state,
                    $"bar-chit:{pending.Card.Value}:{(putOntoBooth ? "booth" : "mitt")}"
                ),
                state.Id,
                actor,
                state.Revision,
                putOntoBooth
            ),
            [],
            putOntoBooth ? "bar-chit:0:booth" : "bar-chit:1:mitt"
        ));
    }

    private IEnumerable<LegalAction> PlayingActions(MatchState state, PlayerId actor)
    {
        if (state.ActivePlayer != actor)
        {
            return [];
        }

        var actions = new List<LegalAction>();
        var inPlay = state.Cards.Where(card => card.Owner == actor && IsInPlay(card)).ToArray();
        foreach (
            var vim in state
                .CardsIn(actor, CardZone.Mitt)
                .Where(static card => card.Kind == CardKind.Vim)
        )
        {
            actions.AddRange(
                inPlay.Select(target => new LegalAction(
                    LegalActionKind.AttachVim,
                    new MatchCommand.AttachVim(
                        CpuCommandId(state, $"attach:{vim.Id.Value}:{target.Id.Value}"),
                        state.Id,
                        actor,
                        state.Revision,
                        vim.Id,
                        target.Id
                    ),
                    [],
                    $"attach:{vim.Id.Value}:{target.Id.Value}"
                ))
            );
        }

        foreach (
            var promotion in state
                .CardsIn(actor, CardZone.Mitt)
                .Where(static card => card.Kind == CardKind.Bloke)
        )
        {
            var requirements = FrozenList<ChoiceRequirement>.Create(
                _catalog
                    .PartyTricks(promotion)
                    .Where(static trick => trick.Trigger == BlokemonTrigger.OnPromotionFromMitt)
                    .SelectMany(trick =>
                        _interpreter.GetChoiceRequirements(
                            state,
                            new EffectInvocation(
                                actor,
                                promotion.Id,
                                new EffectId(trick.MechanicalId),
                                []
                            )
                        )
                    )
                    .DistinctBy(static requirement => requirement.Id)
            );
            actions.AddRange(
                inPlay.Select(target => new LegalAction(
                    LegalActionKind.Promote,
                    new MatchCommand.Promote(
                        CpuCommandId(state, $"promote:{promotion.Id.Value}:{target.Id.Value}"),
                        state.Id,
                        actor,
                        state.Revision,
                        promotion.Id,
                        target.Id,
                        StableChoices(requirements)
                    ),
                    requirements,
                    $"promote:{promotion.Id.Value}:{target.Id.Value}"
                ))
            );
        }

        foreach (
            var bloke in state
                .CardsIn(actor, CardZone.Mitt)
                .Where(card => card.Kind == CardKind.Bloke && _catalog.IsRegular(card.MechanicalId))
        )
        {
            actions.Add(
                new LegalAction(
                    LegalActionKind.PlayBloke,
                    new MatchCommand.PlayBloke(
                        CpuCommandId(state, $"play:{bloke.Id.Value}"),
                        state.Id,
                        actor,
                        state.Revision,
                        bloke.Id
                    ),
                    [],
                    $"play:{bloke.Id.Value}"
                )
            );
        }

        foreach (
            var kitCard in state
                .CardsIn(actor, CardZone.Mitt)
                .Where(static card => card.Kind == CardKind.Kit)
        )
        {
            var kit = _catalog.Kit(kitCard.MechanicalId);
            var targets =
                kit.Kind == BlokemonKitKind.BarKit
                    ? inPlay.Select(static card => (CardInstanceId?)card.Id)
                    : [null];
            var requirements = kit
                .HouseRules.Where(rule =>
                    !ContainsOpcode(rule.Program, BlokemonOpcode.OncePerRound)
                    && !IsDeclarativeHouseRule(rule)
                )
                .SelectMany(rule =>
                    _interpreter.GetChoiceRequirements(
                        state,
                        new EffectInvocation(actor, kitCard.Id, new EffectId(rule.MechanicalId), [])
                    )
                );
            var frozenRequirements = FrozenList<ChoiceRequirement>.Create(
                requirements.DistinctBy(static requirement => requirement.Id)
            );
            var choices = StableChoices(frozenRequirements);
            foreach (var target in targets)
            {
                actions.Add(
                    new LegalAction(
                        LegalActionKind.PlayKit,
                        new MatchCommand.PlayKit(
                            CpuCommandId(
                                state,
                                $"kit:{kitCard.Id.Value}:{target?.Value ?? "none"}"
                            ),
                            state.Id,
                            actor,
                            state.Revision,
                            kitCard.Id,
                            target,
                            choices
                        ),
                        frozenRequirements,
                        $"kit:{kitCard.Id.Value}:{target?.Value ?? "none"}"
                    )
                );
            }
        }

        foreach (var source in inPlay)
        {
            foreach (
                var trick in _catalog
                    .PartyTricks(source)
                    .Where(static trick => trick.Trigger == BlokemonTrigger.Activated)
            )
            {
                var effect = new EffectId(trick.MechanicalId);
                var requirements = _interpreter.GetChoiceRequirements(
                    state,
                    new EffectInvocation(actor, source.Id, effect, [])
                );
                actions.Add(
                    new LegalAction(
                        LegalActionKind.UsePartyTrick,
                        new MatchCommand.UsePartyTrick(
                            CpuCommandId(state, $"trick:{source.Id.Value}:{effect.Value}"),
                            state.Id,
                            actor,
                            state.Revision,
                            source.Id,
                            effect,
                            StableChoices(
                                FrozenList<ChoiceRequirement>.Create(
                                    requirements.Where(requirement => requirement.Chooser == actor)
                                )
                            )
                        ),
                        requirements,
                        $"trick:{source.Id.Value}:{effect.Value}"
                    )
                );
            }

            foreach (var attack in _catalog.Attacks(source))
            {
                var effect = new EffectId(attack.MechanicalId);
                var requirements = _interpreter.GetChoiceRequirements(
                    state,
                    new EffectInvocation(actor, source.Id, effect, [])
                );
                actions.Add(
                    new LegalAction(
                        LegalActionKind.Attack,
                        new MatchCommand.Attack(
                            CpuCommandId(state, $"attack:{source.Id.Value}:{effect.Value}"),
                            state.Id,
                            actor,
                            state.Revision,
                            source.Id,
                            effect,
                            StableChoices(
                                FrozenList<ChoiceRequirement>.Create(
                                    requirements.Where(requirement => requirement.Chooser == actor)
                                )
                            )
                        ),
                        requirements,
                        $"attack:{source.Id.Value}:{effect.Value}"
                    )
                );
            }

            if (source.Kind == CardKind.Kit && _catalog.IsFossil(source.MechanicalId))
            {
                actions.Add(
                    new LegalAction(
                        LegalActionKind.ChuckFossil,
                        new MatchCommand.ChuckFossil(
                            CpuCommandId(state, $"chuck:{source.Id.Value}"),
                            state.Id,
                            actor,
                            state.Revision,
                            source.Id
                        ),
                        [],
                        $"chuck:{source.Id.Value}"
                    )
                );
            }
        }

        var oche = state.Oche(actor);
        if (oche is not null)
        {
            var fare = EffectiveTaxiFare(new MatchBuilder(state, _catalog), oche);
            var vim = oche
                .Attachments.Select(state.Card)
                .Where(static card => card.Kind == CardKind.Vim)
                .Take(fare)
                .Select(static card => card.Id);
            actions.AddRange(
                state
                    .CardsIn(actor, CardZone.Booth)
                    .Select(booth => new LegalAction(
                        LegalActionKind.Taxi,
                        new MatchCommand.Taxi(
                            CpuCommandId(state, $"taxi:{booth.Id.Value}"),
                            state.Id,
                            actor,
                            state.Revision,
                            booth.Id,
                            FrozenList<CardInstanceId>.Create(vim)
                        ),
                        [],
                        $"taxi:{booth.Id.Value}"
                    ))
            );
        }

        foreach (var source in state.Cards.Where(static card => card.Zone == CardZone.Local))
        {
            foreach (
                var rule in _catalog
                    .HouseRules(source)
                    .Where(rule => ContainsOpcode(rule.Program, BlokemonOpcode.OncePerRound))
            )
            {
                var effect = new EffectId(rule.MechanicalId);
                var requirements = _interpreter.GetChoiceRequirements(
                    state,
                    new EffectInvocation(actor, source.Id, effect, [])
                );
                actions.Add(
                    new LegalAction(
                        LegalActionKind.UsePartyTrick,
                        new MatchCommand.UsePartyTrick(
                            CpuCommandId(state, $"local:{source.Id.Value}:{effect.Value}"),
                            state.Id,
                            actor,
                            state.Revision,
                            source.Id,
                            effect,
                            StableChoices(requirements)
                        ),
                        requirements,
                        $"local:{source.Id.Value}:{effect.Value}"
                    )
                );
            }
        }

        actions.Add(
            new LegalAction(
                LegalActionKind.EndRound,
                new MatchCommand.EndRound(
                    CpuCommandId(state, "end"),
                    state.Id,
                    actor,
                    state.Revision
                ),
                [],
                "end"
            )
        );
        return actions;
    }

    private static FrozenList<EffectChoice> StableChoices(
        FrozenList<ChoiceRequirement> requirements
    ) => FrozenList<EffectChoice>.Create(requirements.SelectMany(StableChoice));

    private static IEnumerable<EffectChoice> StableChoice(ChoiceRequirement requirement) =>
        requirement.Kind switch
        {
            ChoiceRequirementKind.Optional => [new EffectChoice.Optional(requirement.Id, true)],
            ChoiceRequirementKind.Amount =>
            [
                new EffectChoice.Amount(requirement.Id, requirement.Minimum),
            ],
            ChoiceRequirementKind.Cards =>
            [
                new EffectChoice.Cards(
                    requirement.Id,
                    FrozenList<CardInstanceId>.Create(
                        requirement.EligibleCards.Take(requirement.Minimum)
                    )
                ),
            ],
            ChoiceRequirementKind.MechanicalType => requirement
                .EligibleMechanicalTypes.Take(1)
                .Select(type =>
                    (EffectChoice)new EffectChoice.MechanicalType(requirement.Id, type)
                ),
            ChoiceRequirementKind.Attack => requirement
                .EligibleEffects.Take(1)
                .Select(effect => (EffectChoice)new EffectChoice.Attack(requirement.Id, effect)),
            ChoiceRequirementKind.Distribution => requirement
                .EligibleCards.Take(1)
                .Select(card =>
                    (EffectChoice)
                        new EffectChoice.Distribution(
                            requirement.Id,
                            FrozenList<DamageAllocation>.Create(
                                new DamageAllocation(card, requirement.Maximum)
                            )
                        )
                ),
            ChoiceRequirementKind.Attachments => requirement
                .EligibleTargets.Take(1)
                .Select(target =>
                    (EffectChoice)
                        new EffectChoice.Attachments(
                            requirement.Id,
                            FrozenList<VimAttachment>.Create(
                                requirement.EligibleCards.Select(vim => new VimAttachment(
                                    vim,
                                    target
                                ))
                            )
                        )
                ),
            _ => throw new UnreachableException(),
        };

    private static CommandId CpuCommandId(MatchState state, string key) =>
        new($"cpu:{state.Revision.Value}:{key}");

    private static bool IsInPlay(CardState card) => card.Zone is CardZone.Oche or CardZone.Booth;

    private static CommandOutcome Reject(
        MatchState state,
        CommandRejectionCode rejection,
        FrozenList<ChoiceRequirement> requirements = default
    ) => new CommandOutcome.Rejected(state, new CommandRejection(rejection, requirements));

    private MatchStartOutcome CommitStart(MatchBuilder builder)
    {
        var events = Commit(builder, builder.Revision);
        var state = events[^1].CommittedState!;
        return new MatchStartOutcome.Started(state, events);
    }

    private CommandOutcome CommitCommand(MatchBuilder builder)
    {
        var events = Commit(builder, builder.Revision.Next());
        var state = events[^1].CommittedState!;
        return new CommandOutcome.Applied(state, events);
    }

    private static FrozenList<MatchEvent> Commit(MatchBuilder builder, MatchRevision revision)
    {
        builder.Revision = revision;
        var firstSequence = builder.LastEventSequence + 1;
        builder.LastEventSequence += builder.Events.Count + 1;
        var state = builder.Snapshot();
        var events = builder
            .Events.Select(
                (pending, index) =>
                    new MatchEvent(
                        firstSequence + index,
                        revision,
                        pending.Kind,
                        pending.Actor,
                        pending.SourceCard,
                        pending.TargetCards,
                        pending.Effect,
                        pending.RoughState,
                        pending.DamageKind,
                        pending.DrawReason,
                        pending.Amount,
                        pending.BadgeSide,
                        pending.StartRequest,
                        pending.Command,
                        null
                    )
            )
            .Append(
                new MatchEvent(
                    builder.LastEventSequence,
                    revision,
                    MatchEventKind.StateCommitted,
                    null,
                    null,
                    [],
                    null,
                    null,
                    null,
                    null,
                    0,
                    null,
                    null,
                    null,
                    state
                )
            );
        return FrozenList<MatchEvent>.Create(events);
    }

    private sealed record HandlerResult(
        CommandRejectionCode? Rejection,
        FrozenList<ChoiceRequirement> Requirements
    )
    {
        public static HandlerResult Accepted { get; } = new(null, []);

        public static HandlerResult Reject(
            CommandRejectionCode rejection,
            FrozenList<ChoiceRequirement> requirements = default
        ) => new(rejection, requirements);
    }
}
