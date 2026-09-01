namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private CpuObservationScenarios =

    let pokedexState () =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 3401UL

        let hiddenCards =
            [ MatchScenario.PlainCard "pokedex" "KIT-024" MatchScenario.FirstPlayer CardZone.Mitt -1
              MatchScenario.PlainCard
                  "own-stack-1"
                  "VIM-CURRY"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  1
              MatchScenario.PlainCard
                  "own-stack-2"
                  "VIM-GEEKED"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  2
              MatchScenario.PlainCard
                  "opponent-mitt"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "opponent-stack-1"
                  "VIM-CURRY"
                  MatchScenario.SecondPlayer
                  CardZone.Stack
                  1
              MatchScenario.PlainCard
                  "opponent-prize"
                  "BLK-001"
                  MatchScenario.SecondPlayer
                  CardZone.BarChit
                  0 ]

        MatchScenario.WithCards original hiddenCards

    let substituteUnknownCards (state: MatchState) =
        let substitute (card: CardState) =
            match card.Owner, card.Zone with
            | owner, CardZone.Stack when owner = MatchScenario.FirstPlayer ->
                { card with
                    MechanicalId = MechanicalCardId "VIM-SOBER"
                    StackPosition = 2 - card.StackPosition }
            | owner, CardZone.Stack when owner = MatchScenario.SecondPlayer ->
                { card with
                    MechanicalId = MechanicalCardId "VIM-GEEKED"
                    StackPosition = 1 - card.StackPosition }
            | owner, CardZone.Mitt when owner = MatchScenario.SecondPlayer ->
                { card with
                    MechanicalId = MechanicalCardId "VIM-DODGY" }
            | _ -> card

        { state with
            Cards = ImmutableArray.CreateRange(state.Cards |> Seq.map substitute |> Seq.sortBy _.Id) }

    let wildfireState () =
        let original =
            MatchScenario.BattleState
                "BLK-146"
                "BLK-003"
                [ "VIM-CURRY"; "VIM-CURRY"; "VIM-CURRY" ]
                3402UL

        MatchScenario.WithCards
            original
            [ MatchScenario.PlainCard
                  "opponent-deck-1"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Stack
                  1
              MatchScenario.PlainCard
                  "opponent-deck-2"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Stack
                  2 ]

    let candidatesFor effect (observation: CpuObservation) =
        observation.Candidates
        |> Seq.filter (fun candidate ->
            match candidate.Action with
            | MatchAction.UsePartyTrick(_, current)
            | MatchAction.Attack(_, current) -> current = EffectId effect
            | _ -> false)
        |> Seq.toArray

    let kitCandidates card (observation: CpuObservation) =
        observation.Candidates
        |> Seq.filter (fun candidate ->
            match candidate.Action with
            | MatchAction.PlayKit(source, _) -> source = CardInstanceId card
            | _ -> false)
        |> Seq.toArray

    let choicesOf (candidate: CpuLegalCandidate) = candidate.Choices |> Seq.toList

    let assertCandidatesApply
        (engine: MatchEngine)
        (state: MatchState)
        (actor: PlayerId)
        (candidates: CpuLegalCandidate seq)
        =
        for candidate in candidates do
            match engine.TryMaterializeCpuCommand(state, actor, candidate.Id) with
            | ValueNone -> failwith $"Candidate {candidate.Id.Value} could not be materialized."
            | ValueSome command ->
                match engine.Apply(state, command) with
                | CommandOutcome.Applied _ -> ()
                | CommandOutcome.Rejected(_, rejection) ->
                    failwith $"Candidate {candidate.Id.Value} was rejected with {rejection.Code}."


type CpuObservationTests() =

    [<Test>]
    member _.``fair observations hide unknown cards and stay unchanged when they are substituted``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = pokedexState ()
        let substituted = substituteUnknownCards state

        let fair =
            engine.GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)

        let repeatedAfterSubstitution =
            engine.GetCpuObservation(
                substituted,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Fair
            )

        repeatedAfterSubstitution.Actor |> should equal fair.Actor
        repeatedAfterSubstitution.State |> should equal fair.State

        repeatedAfterSubstitution.Candidates
        |> Seq.toArray
        |> should equal (fair.Candidates |> Seq.toArray)

        repeatedAfterSubstitution.AuthoritativeState
        |> should equal fair.AuthoritativeState

        fair.AuthoritativeState.IsNone |> should be True

        fair.State.Cards |> Seq.map _.Id |> should contain (CardInstanceId "pokedex")

        fair.State.Cards
        |> Seq.exists (fun card ->
            card.Zone = CardZone.Stack
            || card.Zone = CardZone.BarChit
            || (card.Owner = MatchScenario.SecondPlayer && card.Zone = CardZone.Mitt))
        |> should be False

        let authoritative =
            engine.GetCpuObservation(
                substituted,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Authoritative
            )

        authoritative.State |> should equal fair.State
        authoritative.AuthoritativeState |> should equal (ValueSome substituted)

    [<Test>]
    member _.``candidate tokens cover every up-to choice and rematerialize only accepted commands``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = wildfireState ()

        let observation =
            engine.GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)

        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            match action.Command.Action with
            | MatchAction.Attack(_, effect) -> effect = EffectId "BLK-146-B01"
            | _ -> false)
        |> Seq.length
        |> should equal 1

        let repeated =
            engine.GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)

        repeated.Candidates
        |> Seq.map _.Id
        |> should equal (observation.Candidates |> Seq.map _.Id)

        let selections =
            candidatesFor "BLK-146-B01" observation
            |> Seq.map (fun candidate ->
                match choicesOf candidate with
                | [ CpuChoiceCandidate.Cards(_, cards) ] ->
                    cards.HiddenCardCount |> should equal 0
                    cards.KnownCards |> Seq.sort |> Seq.toList
                | choices -> failwith $"Unexpected Wildfire choices {choices}.")
            |> Seq.sort
            |> Seq.toList

        let vims =
            [ CardInstanceId "vim-0"; CardInstanceId "vim-1"; CardInstanceId "vim-2" ]

        selections
        |> should
            equal
            [ []
              [ vims[0] ]
              [ vims[0]; vims[1] ]
              [ vims[0]; vims[1]; vims[2] ]
              [ vims[0]; vims[2] ]
              [ vims[1] ]
              [ vims[1]; vims[2] ]
              [ vims[2] ] ]

        assertCandidatesApply engine state MatchScenario.FirstPlayer observation.Candidates
        state |> should equal (wildfireState ())

        engine.TryMaterializeCpuCommand(
            state,
            MatchScenario.FirstPlayer,
            CpuCandidateId "not-a-candidate"
        )
        |> ValueOption.isNone
        |> should be True

    [<Test>]
    member _.``effect candidates describe every supported 1999 choice shape``() =
        let engine = MatchScenario.Engine()

        let fireBench =
            MatchScenario.PlainCard
                "fire-pokemon"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let shiftState =
            MatchScenario.BattleState "BLK-049" "BLK-001" [ "VIM-BLAZED"; "VIM-BLAZED" ] 3403UL
            |> fun state -> MatchScenario.WithCards state [ fireBench ]

        let shiftChoices =
            engine.GetCpuObservation(shiftState, MatchScenario.FirstPlayer, CpuObservationMode.Fair)
            |> candidatesFor "BLK-049-T01"
            |> Seq.collect _.Choices
            |> Seq.map (function
                | CpuChoiceCandidate.MechanicalType(_, value) -> value
                | choice -> failwith $"Unexpected Shift choice {choice}.")
            |> Seq.toList

        shiftChoices
        |> should equal [ BlokemonMechanicalType.Grass; BlokemonMechanicalType.Fire ]

        let metronomeState =
            MatchScenario.BattleState "BLK-035" "BLK-006" [ "VIM-DODGY"; "VIM-DODGY" ] 3404UL

        let copiedAttacks =
            engine.GetCpuObservation(
                metronomeState,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Fair
            )
            |> candidatesFor "BLK-035-B02"
            |> Seq.collect _.Choices
            |> Seq.map (function
                | CpuChoiceCandidate.Attack(_, effect) -> effect
                | choice -> failwith $"Unexpected Metronome choice {choice}.")
            |> Seq.toList

        copiedAttacks |> should equal [ EffectId "BLK-006-B01" ]

        let waterBench =
            MatchScenario.PlainCard
                "water-bench"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let energies =
            [ MatchScenario.PlainCard
                  "rain-energy-1"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "rain-energy-2"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

        let rainState =
            MatchScenario.BattleState "BLK-009" "BLK-004" [] 3405UL
            |> fun state -> MatchScenario.WithCards state (waterBench :: energies)

        let placements =
            engine.GetCpuObservation(rainState, MatchScenario.FirstPlayer, CpuObservationMode.Fair)
            |> candidatesFor "BLK-009-T01"
            |> Seq.map (fun candidate ->
                match choicesOf candidate with
                | [ CpuChoiceCandidate.Attachments(_, values) ] ->
                    let placement = values |> Seq.exactlyOne
                    placement.Vim.Value, placement.Bloke.Value
                | choices -> failwith $"Unexpected Rain Dance choices {choices}.")
            |> Set.ofSeq

        placements
        |> should
            equal
            (set
                [ CardInstanceId "rain-energy-1", CardInstanceId "attacker"
                  CardInstanceId "rain-energy-1", CardInstanceId "water-bench"
                  CardInstanceId "rain-energy-2", CardInstanceId "attacker"
                  CardInstanceId "rain-energy-2", CardInstanceId "water-bench" ])

        let potionBase =
            MatchScenario.BattleState "BLK-040" "BLK-004" [ "VIM-DODGY" ] 3406UL

        let damaged =
            { potionBase.Card(CardInstanceId "attacker") with
                Damage = 30 }

        let potion =
            MatchScenario.PlainCard
                "super-potion"
                "KIT-027"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let potionState = MatchScenario.WithCards potionBase [ damaged; potion ]

        let potionCandidates =
            engine.GetCpuObservation(
                potionState,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Fair
            )
            |> kitCandidates "super-potion"

        let healedAmounts =
            potionCandidates
            |> Seq.collect _.Choices
            |> Seq.choose (function
                | CpuChoiceCandidate.Amount(_, amount) -> Some amount
                | _ -> None)
            |> Seq.toList

        healedAmounts |> should equal [ 0; 1; 2; 3 ]
        potionCandidates |> Seq.length |> should equal 4

        assertCandidatesApply engine potionState MatchScenario.FirstPlayer potionCandidates

    [<Test>]
    member _.``ordered hidden deck choices stay opaque while authoritative choices include every order``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = pokedexState ()

        let fair =
            engine.GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)
            |> kitCandidates "pokedex"

        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            match action.Command.Action with
            | MatchAction.PlayKit(source, _) -> source = CardInstanceId "pokedex"
            | _ -> false)
        |> Seq.length
        |> should equal 1

        fair |> Seq.length |> should equal 6

        fair
        |> Seq.iter (fun candidate ->
            match choicesOf candidate with
            | [ CpuChoiceCandidate.Cards(_, cards) ] ->
                cards.KnownCards |> should be Empty
                cards.HiddenCardCount |> should equal 3
            | choices -> failwith $"Unexpected Pokedex choices {choices}.")

        let authoritativeOrders =
            engine.GetCpuObservation(
                state,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Authoritative
            )
            |> kitCandidates "pokedex"
            |> Seq.map (fun candidate ->
                match choicesOf candidate with
                | [ CpuChoiceCandidate.Cards(_, cards) ] -> cards.KnownCards |> Seq.toList
                | choices -> failwith $"Unexpected Pokedex choices {choices}.")
            |> Set.ofSeq

        let draw = CardInstanceId "first-draw"
        let first = CardInstanceId "own-stack-1"
        let second = CardInstanceId "own-stack-2"

        authoritativeOrders
        |> should
            equal
            (set
                [ [ draw; first; second ]
                  [ draw; second; first ]
                  [ first; draw; second ]
                  [ first; second; draw ]
                  [ second; draw; first ]
                  [ second; first; draw ] ])

    [<Test>]
    member _.``CPU taxi candidates include every exact payment without duplicating the human action``
        ()
        =
        let engine = MatchScenario.Engine()

        let bench =
            MatchScenario.PlainCard
                "taxi-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-004" [ "VIM-SOBER"; "VIM-CURRY" ] 3407UL
            |> fun current -> MatchScenario.WithCards current [ bench ]

        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.Taxi)
        |> Seq.length
        |> should equal 1

        let observation =
            engine.GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)

        let taxiCandidates =
            observation.Candidates
            |> Seq.filter (fun candidate -> candidate.Kind = LegalActionKind.Taxi)
            |> Seq.toArray

        taxiCandidates
        |> Seq.map (fun candidate ->
            match candidate.Action with
            | MatchAction.Taxi(_, payment) -> payment |> Seq.toList
            | action -> failwith $"Unexpected taxi action {action}.")
        |> Set.ofSeq
        |> should equal (set [ [ CardInstanceId "vim-0" ]; [ CardInstanceId "vim-1" ] ])

        assertCandidatesApply engine state MatchScenario.FirstPlayer taxiCandidates
