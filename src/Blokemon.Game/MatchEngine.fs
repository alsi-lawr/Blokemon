namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchCommit
open Blokemon.Game.MatchContinuous
open Blokemon.Game.MatchBasicHandlers
open Blokemon.Game.MatchPlayHandlers
open Blokemon.Game.MatchKitHandlers
open Blokemon.Game.MatchTrickHandlers
open Blokemon.Game.MatchAttackHandlers
open Blokemon.Game.MatchTriggerHandlers
open Blokemon.Game.MatchLegalActions
open Blokemon.Game.CpuObservations
open Blokemon.Game.CpuCandidateIds
open Blokemon.Game.CpuPolicyLimits

/// The one place a match state is ever advanced: a validated start request produces the first
/// state, and each command produces exactly one successor from exactly one predecessor.
type MatchEngine(authority: BlokemonRuntimeManifest) =
    let catalog = AuthorityCatalog authority
    let interpreter = BlokemonInterpreter authority

    let authorityIsValid =
        BlokemonSetValidator.ValidateRuntime(authority).IsValid
        && interpreter.AuditAuthority().IsInventoryComplete

    member internal _.ResolutionTrace
        with set trace = interpreter.ResolutionTrace <- trace

    member _.Start(request: MatchStartRequest) =
        let issues = validateStart catalog authorityIsValid request

        if issues.Count > 0 then
            MatchStartOutcome.Rejected(ImmutableArray.CreateRange issues)
        else
            let players = [| request.FirstDeck.Owner; request.SecondDeck.Owner |]

            let random =
                DeterministicRandom
                    { State = request.Seed.Value
                      ConsumptionIndex = 0 }

            let openingPlayer = players[random.NextInt players.Length]

            let cards =
                Array.append
                    (createCards catalog request.FirstDeck 1)
                    (createCards catalog request.SecondDeck 2)

            let initial =
                { Id = request.MatchId
                  AuthorityVersion = catalog.Manifest.ManifestVersion
                  Seed = request.Seed
                  Random = random.Snapshot
                  Revision = MatchRevision 0
                  LastEventSequence = 0L
                  Phase = MatchPhase.OpeningPlacement
                  OpeningPlayer = openingPlayer
                  ActivePlayer = openingPlayer
                  RoundNumber = 0
                  Players =
                    ImmutableArray.CreateRange(
                        players
                        |> Seq.map (fun player ->
                            { Id = player
                              BarChitsRemaining = catalog.Manifest.BaseRules.Opening.PrizeCardCount
                              MulliganCount = 0
                              MulliganBonusAllowance = 0
                              MulliganBonusChosen = false
                              BonusDrawn = ImmutableArray<_>.Empty
                              BonusPlacementChosen = true
                              OpeningChosen = false
                              RoundsStarted = 0 })
                    )
                  Cards = ImmutableArray.CreateRange cards
                  Effects = ImmutableArray<_>.Empty
                  ProcessedCommands = ImmutableArray<_>.Empty
                  RoundUsage = RoundUsage.Empty openingPlayer
                  PendingEffect = ValueNone
                  PendingKnockout = ValueNone
                  PendingBarChits = ImmutableArray<_>.Empty
                  ReplacementPlayer = ValueNone
                  PendingRoundEnd = false
                  Winner = ValueNone
                  SuddenDeathCount = 0 }

            let builder = MatchBuilder(initial, catalog)

            builder.Events.Add
                { PendingMatchEvent.ofKind MatchEventKind.MatchStarted with
                    StartRequest = ValueSome request }

            MatchSetup.dealOpeningMitts catalog builder
            MatchSetup.assignMulliganBonuses builder
            commitStart builder

    /// One command against one state. Nothing here re-derives the match from its history: the
    /// builder is seeded from the state it was handed and staged forward in place.
    member _.Apply(state: MatchState, command: MatchCommand) =
        match validateCommandBoundary catalog state command with
        | ValueSome rejection -> reject state rejection ImmutableArray<_>.Empty
        | ValueNone ->

            let builder = MatchBuilder(state, catalog)

            builder.Events.Add
                { PendingMatchEvent.forActor MatchEventKind.CommandApplied command.Actor with
                    Command = ValueSome command }

            refreshContinuousEffects catalog interpreter builder

            let result =
                match command.Action with
                | MatchAction.ChooseMulliganBonus cardsToDraw ->
                    chooseMulliganBonus catalog builder command.Actor cardsToDraw
                | MatchAction.ChooseBonusPlacement bonusBooth ->
                    chooseBonusPlacement catalog builder command.Actor bonusBooth
                | MatchAction.ChooseOpening(oche, booth) ->
                    chooseOpening catalog builder command.Actor oche booth
                | MatchAction.AttachVim(vim, target) ->
                    attachVim catalog builder command.Actor vim target
                | MatchAction.PlayBloke bloke -> playBloke catalog builder command.Actor bloke
                | MatchAction.Promote(promotion, promoted) ->
                    promote catalog interpreter builder command promotion promoted
                | MatchAction.PlayKit(kit, target) ->
                    playKit
                        catalog
                        interpreter
                        builder
                        command
                        kit
                        target
                        false
                        ImmutableArray<_>.Empty
                | MatchAction.Taxi(boothBloke, vimToChuck) ->
                    taxi catalog builder command.Actor boothBloke vimToChuck
                | MatchAction.UsePartyTrick(source, effect) ->
                    usePartyTrick
                        catalog
                        interpreter
                        builder
                        command
                        source
                        effect
                        false
                        ImmutableArray<_>.Empty
                | MatchAction.Attack(attacker, attackId) ->
                    attack
                        catalog
                        interpreter
                        builder
                        command
                        attacker
                        attackId
                        false
                        false
                        ImmutableArray<_>.Empty
                | MatchAction.ChuckFossil fossil -> chuckFossil catalog builder command.Actor fossil
                | MatchAction.EndRound -> endRound catalog interpreter builder command.Actor
                | MatchAction.ChooseReplacement replacement ->
                    chooseReplacement catalog interpreter builder command.Actor replacement
                | MatchAction.ResolveEffectChoice ->
                    resolveEffectChoice catalog interpreter builder command
                | MatchAction.ResolveKnockoutTrigger vim ->
                    resolveKnockoutTrigger catalog interpreter builder command.Actor vim
                | MatchAction.ResolveBarChitTrigger putOntoBooth ->
                    resolveBarChitTrigger catalog interpreter builder command.Actor putOntoBooth
                | MatchAction.Resign -> resign builder command.Actor

            match result.Rejection with
            | ValueSome rejection -> reject state rejection result.Requirements
            | ValueNone ->
                builder.RecordCommand command.Id
                commitCommand builder

    member _.CanRevealHand(state: MatchState, viewer: PlayerId, owner: PlayerId) =
        if viewer = owner then
            true
        else
            let builder = MatchBuilder(state, catalog)

            state.Cards
            |> Seq.exists (fun source ->
                source.Owner = viewer
                && PokemonPowers.hasActivePower catalog builder source BlokemonOpcode.Clairvoyance)

    member this.CanRevealCard(state: MatchState, viewer: PlayerId, cardId: CardInstanceId) =
        match state.Cards |> Seq.tryFind (fun card -> card.Id = cardId) with
        | None -> false
        | Some card ->
            match card.Zone with
            | CardZone.Oche
            | CardZone.Booth
            | CardZone.Attached
            | CardZone.EmptiesTray -> true
            | CardZone.Mitt -> card.Owner = viewer || this.CanRevealHand(state, viewer, card.Owner)
            | _ -> false

    member this.CanRevealEventCard
        (state: MatchState, matchEvent: MatchEvent, viewer: PlayerId, cardId: CardInstanceId)
        =
        matchEvent.TargetCards |> Seq.contains cardId
        && (matchEvent.Kind = MatchEventKind.CardsRevealed
            && (matchEvent.Effect.IsNone || matchEvent.Actor = ValueSome viewer)
            || this.CanRevealCard(state, viewer, cardId))

    member private _.GetProposedActions(state: MatchState, actor: PlayerId) =
        if
            not (state.Players |> Seq.exists (fun player -> player.Id = actor))
            || state.Phase = MatchPhase.Complete
        then
            Seq.empty
        else
            // Apply recomputes the same continuous effects before dispatch, so proposal and
            // validation read equivalent state without sharing a mutable builder.
            let legalState =
                if state.Phase = MatchPhase.Playing then
                    let builder = MatchBuilder(state, catalog)
                    refreshContinuousEffects catalog interpreter builder
                    builder.Snapshot()
                else
                    state

            // Resignation sits outside the phase switch: it is legal for either player in every
            // phase this method still serves, because Complete already returned above.
            Seq.append
                (proposed catalog interpreter legalState actor)
                (Seq.singleton (resignAction state actor))

    member private this.GetValidatedActions(state: MatchState, actor: PlayerId) =
        if
            not (state.Players |> Seq.exists (fun player -> player.Id = actor))
            || state.Phase = MatchPhase.Complete
        then
            ImmutableArray<_>.Empty
        else
            ImmutableArray.CreateRange(
                this.GetProposedActions(state, actor)
                |> Seq.filter (fun action ->
                    match this.Apply(state, action.Command) with
                    | CommandOutcome.Applied _ -> true
                    // An action the player cannot pay for is kept so the interface can show it
                    // unavailable and say what it needs. It survives only while the cost it
                    // declares is the single thing standing in its way: every other objection
                    // still removes it, and the action itself is not submittable.
                    | CommandOutcome.Rejected(_, rejection) ->
                        match action.Affordability with
                        | ActionAffordability.ShortOfTaxiFare _ ->
                            rejection.Code = CommandRejectionCode.InvalidTaxiFare
                        | ActionAffordability.Payable -> false)
                |> order
            )

    member this.GetLegalActions(state: MatchState, actor: PlayerId) =
        this.GetValidatedActions(state, actor)

    member this.GetCpuObservation(state: MatchState, actor: PlayerId, mode: CpuObservationMode) =
        if not (state.Players |> Seq.exists (fun player -> player.Id = actor)) then
            invalidArg (nameof actor) "A CPU observation requires a player in this match."

        let actionSources =
            this.GetProposedActions(state, actor)
            |> cpuOrder
            |> Seq.mapi (fun baseIndex action ->
                let actions =
                    materializeWithIndex state action
                    |> Seq.filter (fun (_, materialized) ->
                        materialized.Affordability = ActionAffordability.Payable
                        && (match this.Apply(state, materialized.Command) with
                            | CommandOutcome.Applied _ -> true
                            | CommandOutcome.Rejected _ -> false))

                baseIndex, actions)

        create state actor mode (fun owner -> this.CanRevealHand(state, actor, owner)) actionSources

    member internal this.TryMaterializeCpuAction
        (state: MatchState, actor: PlayerId, candidate: CpuCandidateId)
        =
        match tryParse state candidate with
        | ValueNone -> ValueNone
        | ValueSome(baseIndex, choiceIndex) ->
            this.GetProposedActions(state, actor)
            |> cpuOrder
            |> Seq.tryItem baseIndex
            |> Option.bind (fun action -> tryMaterializeAt state action choiceIndex)
            |> Option.bind (fun action ->
                if action.Affordability <> ActionAffordability.Payable then
                    None
                else
                    match this.Apply(state, action.Command) with
                    | CommandOutcome.Applied _ -> Some action
                    | CommandOutcome.Rejected _ -> None)
            |> ValueOption.ofOption

    member this.TryMaterializeCpuCommand
        (state: MatchState, actor: PlayerId, candidate: CpuCandidateId)
        =
        this.TryMaterializeCpuAction(state, actor, candidate)
        |> ValueOption.map _.Command

    member internal this.CreateCpuPlanningState
        (
            state: MatchState,
            observation: CpuObservation,
            mode: CpuObservationMode,
            seed: uint64,
            sampleIndex: uint64
        ) =
        match mode with
        | CpuObservationMode.Fair ->
            CpuPlanning.createFairState catalog state observation seed sampleIndex
        | CpuObservationMode.Authoritative -> state

    member internal _.ScoreCpuTransition
        (
            actor: PlayerId,
            kind: LegalActionKind,
            before: MatchState,
            beforeObservation: CpuObservation,
            after: MatchState,
            afterObservation: CpuObservation
        ) =
        let readyAttacks (observation: CpuObservation) =
            observation.Candidates
            |> Seq.truncate rootCandidateLimit
            |> Seq.choose (fun action ->
                match action.Action with
                | MatchAction.Attack(attacker, attack) ->
                    catalog.Attack attack
                    |> ValueOption.map (fun details -> attacker, details.PrintedDamage)
                    |> ValueOption.toOption
                | _ -> None)
            |> Seq.groupBy fst
            |> Seq.map (fun (attacker, attacks) -> attacker, attacks |> Seq.map snd |> Seq.max)
            |> Map.ofSeq

        CpuEvaluation.transitionScore
            catalog
            (readyAttacks beforeObservation)
            (readyAttacks afterObservation)
            actor
            kind
            before
            after
