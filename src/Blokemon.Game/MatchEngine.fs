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

/// Dealing the opening mitts and settling the mulligan bonuses: everything between a validated
/// start request and the first state anyone gets to see.
module internal MatchSetup =

    let dealOpeningMitts (catalog: AuthorityCatalog) (builder: MatchBuilder) =
        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            builder.Shuffle player

        for player in builder.Players |> Seq.map (fun player -> player.Id) |> Seq.toArray do
            builder.Draw(
                player,
                catalog.Manifest.BaseRules.Opening.MittSize,
                DrawReason.OpeningMitt
            )
            |> ignore

        let mutable settled = false

        while not settled do
            let mulliganPlayers =
                builder.Players
                |> Seq.map (fun player -> player.Id)
                |> Seq.filter (fun player ->
                    builder.CardsIn(player, CardZone.Mitt)
                    |> Seq.exists (fun card ->
                        card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)
                    |> not)
                |> Seq.toArray

            if mulliganPlayers.Length = 0 then
                settled <- true
            else
                for player in mulliganPlayers do
                    builder.ReturnMittToStack player
                    let state = builder.Player player

                    builder.SetPlayer
                        { state with
                            MulliganCount = state.MulliganCount + 1 }

                for player in mulliganPlayers do
                    builder.Shuffle player

                for player in mulliganPlayers do
                    builder.Draw(
                        player,
                        catalog.Manifest.BaseRules.Opening.MittSize,
                        DrawReason.OpeningMitt
                    )
                    |> ignore

    let assignMulliganBonuses (builder: MatchBuilder) =
        let players = builder.Players |> Seq.toArray

        for player in players do
            let other = players |> Array.find (fun candidate -> candidate.Id <> player.Id)
            let allowance = max 0 (other.MulliganCount - player.MulliganCount)

            builder.SetPlayer
                { player with
                    MulliganBonusAllowance = allowance
                    MulliganBonusChosen = allowance = 0 }

        builder.Phase <-
            if
                players
                |> Array.exists (fun player ->
                    (builder.Player player.Id).MulliganBonusAllowance > 0)
            then
                MatchPhase.MulliganBonus
            else
                MatchPhase.OpeningPlacement

/// The one place a match state is ever advanced: a validated start request produces the first
/// state, and each command produces exactly one successor from exactly one predecessor.
type MatchEngine(authority: BlokemonRuntimeManifest) =
    let catalog = AuthorityCatalog authority
    let interpreter = BlokemonInterpreter authority

    let authorityIsValid =
        BlokemonSetValidator.ValidateRuntime(authority).IsValid
        && interpreter.AuditAuthority().IsInventoryComplete

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
                              BarChitsRemaining = catalog.Manifest.BaseRules.Opening.BarChitCount
                              MulliganCount = 0
                              MulliganBonusAllowance = 0
                              MulliganBonusChosen = false
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
                    chooseMulliganBonus builder command.Actor cardsToDraw
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

    member this.GetLegalActions(state: MatchState, actor: PlayerId) =
        if
            not (state.Players |> Seq.exists (fun player -> player.Id = actor))
            || state.Phase = MatchPhase.Complete
        then
            ImmutableArray<_>.Empty
        else
            let legalState =
                if state.Phase = MatchPhase.Playing then
                    let builder = MatchBuilder(state, catalog)
                    refreshContinuousEffects catalog interpreter builder
                    builder.Snapshot()
                else
                    state

            // Resignation sits outside the phase switch: it is legal for either player in every
            // phase this method still serves, because Complete already returned above.
            ImmutableArray.CreateRange(
                Seq.append
                    (proposed catalog interpreter legalState actor)
                    (Seq.singleton (resignAction state actor))
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
