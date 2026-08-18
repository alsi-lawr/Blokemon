namespace Blokemon.Game.Tests

open System.Collections.Generic
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private DeferredBranchChoiceFixtures =

    let coinSwitchRequest () =
        { MatchId = MatchId "coin-branch-e2e"
          Seed = MatchSeed 1UL
          FirstDeck =
            FrozenDeckSnapshot.Create(
                MatchScenario.FirstPlayer,
                Seq.append (Seq.replicate 4 "BLK-052") (Seq.replicate 56 "VIM-SOBER")
            )
          SecondDeck =
            FrozenDeckSnapshot.Create(
                MatchScenario.SecondPlayer,
                Seq.append (Seq.replicate 4 "BLK-001") (Seq.replicate 56 "VIM-SOBER")
            ) }

    let seedForBadge () =
        let rec search (seed: uint64) =
            if seed >= 100UL then 0UL
            elif BlokemonSeededRandom(seed).NextInt 2 = 1 then seed
            else search (seed + 1UL)

        search 0UL

    let playKitAction (engine: MatchEngine) (state: MatchState) (kit: CardState) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun candidate ->
            candidate.Kind = LegalActionKind.PlayKit
            && (match candidate.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = kit.Id
                | _ -> false))
        |> Seq.exactlyOne

type DeferredBranchChoiceTests() =

    [<Test>]
    member _.``a parked beer-mat branch should preserve the random stream, replay identically and refuse a second resolution``
        ()
        =
        let engine = MatchScenario.Engine()
        let request = coinSwitchRequest ()

        let startedState, startedEvents =
            match engine.Start request with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected _ -> failwith "The start was rejected."

        let allEvents = List<MatchEvent> startedEvents
        let mutable state = startedState

        let apply (command: MatchCommand) =
            match engine.Apply(state, command) with
            | CommandOutcome.Applied(applied, events) ->
                state <- applied
                allEvents.AddRange events
                applied, events
            | CommandOutcome.Rejected(_, rejection) ->
                failwith $"The command was rejected with {rejection.Code}."

        for player in [ MatchScenario.FirstPlayer; MatchScenario.SecondPlayer ] do
            let playerState = state.Player player

            if playerState.MulliganBonusAllowance > 0 && not playerState.MulliganBonusChosen then
                apply (
                    MatchScenario.Command
                        state
                        $"mulligan:{player.Value}"
                        player
                        FrozenList.empty
                        (MatchAction.ChooseMulliganBonus 0)
                )
                |> ignore

        let attacker =
            state.CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
            |> Seq.filter (fun card -> card.MechanicalId.Value = "BLK-052")
            |> Seq.exactlyOne

        let defenders =
            state.CardsIn(MatchScenario.SecondPlayer, CardZone.Mitt)
            |> Seq.filter (fun card -> card.MechanicalId.Value = "BLK-001")
            |> Seq.truncate 2
            |> Seq.toArray

        apply (
            MatchScenario.Command
                state
                "opening:first"
                MatchScenario.FirstPlayer
                FrozenList.empty
                (MatchAction.ChooseOpening(attacker.Id, FrozenList.empty))
        )
        |> ignore

        apply (
            MatchScenario.Command
                state
                "opening:second"
                MatchScenario.SecondPlayer
                FrozenList.empty
                (MatchAction.ChooseOpening(
                    defenders[0].Id,
                    FrozenList<CardInstanceId>.Create defenders[1].Id
                ))
        )
        |> ignore

        apply (
            MatchScenario.Command
                state
                "end-opening-round"
                MatchScenario.SecondPlayer
                FrozenList.empty
                MatchAction.EndRound
        )
        |> ignore

        let vim =
            state.CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
            |> Seq.find (fun card -> card.Kind = CardKind.Vim)

        apply (
            MatchScenario.Command
                state
                "attach-for-coin-switch"
                MatchScenario.FirstPlayer
                FrozenList.empty
                (MatchAction.AttachVim(vim.Id, attacker.Id))
        )
        |> ignore

        let requested, _ =
            apply (
                MatchScenario.Command
                    state
                    "coin-switch"
                    MatchScenario.FirstPlayer
                    FrozenList.empty
                    (MatchAction.Attack(attacker.Id, EffectId "BLK-052-B01"))
            )

        let pending = requested.PendingEffect.Value
        let requirement = pending.Requirements |> Seq.exactlyOne
        let target = requirement.EligibleCards |> Seq.exactlyOne

        let resolve =
            MatchScenario.Command
                requested
                "resolve-coin-switch"
                pending.Chooser
                (FrozenList<EffectChoice>
                    .Create(
                        EffectChoice.Cards(requirement.Id, FrozenList<CardInstanceId>.Create target)
                    ))
                MatchAction.ResolveEffectChoice

        let wrongChooser =
            { resolve with
                Id = CommandId "resolve-coin-switch-wrong-chooser"
                Actor = requested.Other pending.Chooser }

        let eventsBeforeWrongChooser = allEvents.ToArray()

        let rejectedChooserState, rejectedChooser =
            MatchScenario.Rejected(engine.Apply(requested, wrongChooser))

        let resolved, resolvedEvents = apply resolve

        let restartedState, restartedEvents =
            match MatchScenario.Engine().Apply(requested, resolve) with
            | CommandOutcome.Applied(applied, events) -> applied, events
            | CommandOutcome.Rejected _ -> failwith "The resolution was rejected."

        let finalEvents = allEvents.ToArray()

        let duplicateState, duplicate =
            MatchScenario.Rejected(engine.Apply(resolved, resolve))

        pending.BeerMatResults |> Seq.toList |> should equal [ true ]

        resolved.Random.ConsumptionIndex
        |> should equal requested.Random.ConsumptionIndex

        restartedState |> should equal resolved
        restartedEvents |> should equal resolvedEvents
        rejectedChooser.Code |> should equal CommandRejectionCode.WrongChooser
        obj.ReferenceEquals(rejectedChooserState, requested) |> should be True
        rejectedChooserState |> should equal requested

        allEvents
        |> Seq.truncate eventsBeforeWrongChooser.Length
        |> Seq.toArray
        |> should equal eventsBeforeWrongChooser

        duplicate.Code |> should equal CommandRejectionCode.DuplicateCommand
        obj.ReferenceEquals(duplicateState, resolved) |> should be True
        duplicateState |> should equal resolved
        allEvents.ToArray() |> should equal finalEvents

    [<Test>]
    member _.``a basic vim attachment attack should skip its secondary effect when there is no booth``
        ()
        =
        let state = MatchScenario.BattleState "BLK-123" "BLK-001" [ "VIM-BLAZED" ] 503UL

        let discardedVim =
            MatchScenario.PlainCard
                "discarded-vim"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards state [ discardedVim ]
        let engine = MatchScenario.Engine()

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Attack
                && (match candidate.Command.Action with
                    | MatchAction.Attack(_, attackId) -> attackId = EffectId "BLK-123-B01"
                    | _ -> false))
            |> Seq.exactlyOne

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        action.ChoiceRequirements.Count |> should equal 0
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20
        (applied.Card discardedVim.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``a beer-mat attachment kit should skip its badge branch when there is no booth to attach to``
        ()
        =
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] (seedForBadge ())

        let kit =
            MatchScenario.PlainCard "coin-kit" "KIT-008" MatchScenario.FirstPlayer CardZone.Mitt -1

        let discardedVim =
            MatchScenario.PlainCard
                "discarded-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards state [ kit; discardedVim ]
        let engine = MatchScenario.Engine()
        let action = playKitAction engine state kit

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))

        action.ChoiceRequirements.Count |> should equal 0
        applied.PendingEffect.IsNone |> should be True
        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray
        (applied.Card discardedVim.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``a beer-mat attachment kit should ask for its target only after the badge lands``() =
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] (seedForBadge ())

        let bench =
            MatchScenario.PlainCard
                "own-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let kit =
            MatchScenario.PlainCard "coin-kit" "KIT-008" MatchScenario.FirstPlayer CardZone.Mitt -1

        let discardedVim =
            MatchScenario.PlainCard
                "discarded-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards state [ bench; kit; discardedVim ]
        let engine = MatchScenario.Engine()
        let action = playKitAction engine state kit
        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))
        let cpu = DeterministicCpu()

        let choice =
            MatchScenario.Chosen(cpu.Choose(engine, requested, MatchScenario.FirstPlayer))

        let resolved = MatchScenario.Applied(engine.Apply(requested, choice.Command))

        action.ChoiceRequirements.Count |> should equal 0
        requested.PendingEffect.IsSome |> should be True

        (requested.PendingEffect.Value.Requirements |> Seq.exactlyOne).EligibleTargets
        |> Seq.toList
        |> should equal [ bench.Id ]

        (resolved.Card discardedVim.Id).AttachedTo |> should equal (ValueSome bench.Id)
        (resolved.Card kit.Id).Zone |> should equal CardZone.EmptiesTray
