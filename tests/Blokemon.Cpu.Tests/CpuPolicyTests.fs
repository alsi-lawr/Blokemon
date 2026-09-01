namespace Blokemon.Cpu.Tests

open System.Collections.Immutable
open System.Runtime.InteropServices
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.Cpu
open Blokemon.Game.Tests
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private CpuPolicyScenarios =

    let input difficulty seed =
        { Difficulty = difficulty
          Seed = seed
          DecisionIndex = 0UL }

    let choose difficulty seed (state: MatchState) =
        DeterministicCpu()
            .Choose(MatchScenario.Engine(), state, MatchScenario.FirstPlayer, input difficulty seed)

    let selected (choice: CpuPolicyDecision) = MatchScenario.Chosen choice.Decision

    let replacePlayer (state: MatchState) (player: PlayerState) =
        { state with
            Players =
                state.Players
                |> Seq.map (fun current -> if current.Id = player.Id then player else current)
                |> ImmutableArray.CreateRange }

    let cardChoice (action: LegalAction) =
        action.Command.Choices
        |> Seq.choose (function
            | EffectChoice.Cards(_, cards) -> Some(cards |> Seq.toList)
            | _ -> None)
        |> Seq.tryHead

    let independentArray (values: ImmutableArray<'value>) =
        let copy = Array.zeroCreate<'value> values.Length
        values.CopyTo copy
        ImmutableCollectionsMarshal.AsImmutableArray copy

    let independentlyMaterializedEffect (effect: TemporaryEffect) =
        { effect with
            MechanicalTypes = independentArray effect.MechanicalTypes
            RoughStates = independentArray effect.RoughStates
            RelatedCards = independentArray effect.RelatedCards
            Conditions = independentArray effect.Conditions }

    let highCombinationWildfireState () =
        let original = MatchScenario.BattleState "BLK-146" "BLK-004" [] 3515UL

        let attachmentIds =
            [| for index in 0..58 -> CardInstanceId $"wildfire-vim-{index}" |]

        let attacker =
            { original.Card(CardInstanceId "attacker") with
                Attachments = ImmutableArray.CreateRange attachmentIds }

        let attachments =
            attachmentIds
            |> Seq.map (fun id ->
                MatchScenario.AttachedCard
                    id.Value
                    "VIM-CURRY"
                    MatchScenario.FirstPlayer
                    CardZone.Attached
                    -1
                    attacker.Id)

        let posed = MatchScenario.WithCards original (Seq.append [ attacker ] attachments)

        { posed with
            Cards =
                ImmutableArray.CreateRange(
                    posed.Cards |> Seq.filter (fun card -> card.Id <> CardInstanceId "first-draw")
                ) }

type CpuPolicyTests() =

    [<Test>]
    member _.``normal should put the stronger opening bloke at the oche and develop the booth``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 3501UL

        let weaker =
            { original.Card(CardInstanceId "attacker") with
                Zone = CardZone.Mitt }

        let stronger =
            MatchScenario.PlainCard
                "stronger-opening"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let first =
            { original.Player MatchScenario.FirstPlayer with
                OpeningChosen = false }

        let state =
            { MatchScenario.WithCards original [ weaker; stronger ] with
                Phase = MatchPhase.OpeningPlacement }
            |> fun table -> replacePlayer table first
            |> MatchScenario.WithRestartableDecks

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.ChooseOpening(oche, booth) ->
            oche |> should equal stronger.Id
            booth |> Seq.toList |> should contain weaker.Id
        | action -> failwith $"Expected an opening choice, got {action}."

    [<Test>]
    member _.``normal should play a basic bloke instead of ending an undeveloped round``() =
        let basic =
            MatchScenario.PlainCard
                "bench-development"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-004" [] 3502UL
            |> fun table -> MatchScenario.WithCards table [ basic ]

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.PlayBloke card -> card |> should equal basic.Id
        | action -> failwith $"Expected bench development, got {action}."

    [<Test>]
    member _.``normal should attach Energy where it makes an attack affordable``() =
        let bench =
            MatchScenario.PlainCard
                "energy-bench"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let energy =
            MatchScenario.PlainCard
                "planned-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.BattleState "BLK-004" "BLK-001" [] 3503UL
            |> fun table -> MatchScenario.WithCards table [ bench; energy ]

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.AttachVim(vim, target) ->
            vim |> should equal energy.Id
            target |> should equal (CardInstanceId "attacker")
        | action -> failwith $"Expected an Energy attachment, got {action}."

    [<Test>]
    member _.``normal should promote a developed bloke before ending the round``() =
        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-002" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-004" [] 3504UL
            |> fun table -> MatchScenario.WithCards table [ promotion ]

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.Promote(card, target) ->
            card |> should equal promotion.Id
            target |> should equal (CardInstanceId "attacker")
        | action -> failwith $"Expected a promotion, got {action}."

    [<Test>]
    member _.``normal should use the full useful Super Potion amount instead of paying for no healing``
        ()
        =
        let original =
            MatchScenario.BattleStateWith
                "BLK-040"
                "BLK-004"
                [ "VIM-DODGY" ]
                3505UL
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 2))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let damaged =
            { original.Card(CardInstanceId "attacker") with
                Damage = 30 }

        let potion =
            MatchScenario.PlainCard
                "super-potion"
                "KIT-027"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards original [ damaged; potion ]
        let action = selected (choose CpuDifficulty.Normal 0UL state)

        match action.Command.Action with
        | MatchAction.PlayKit(card, _) -> card |> should equal potion.Id
        | other -> failwith $"Expected Super Potion, got {other}."

        action.Command.Choices
        |> Seq.choose (function
            | EffectChoice.Amount(_, amount) -> Some amount
            | _ -> None)
        |> Seq.exactlyOne
        |> should equal 3

    [<Test>]
    member _.``normal should heal the more damaged target``() =
        let original =
            MatchScenario.BattleStateWith
                "BLK-040"
                "BLK-004"
                []
                3506UL
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 2))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let active =
            { original.Card(CardInstanceId "attacker") with
                Damage = 30 }

        let bench =
            { MatchScenario.PlainCard
                  "slightly-damaged-bench"
                  "BLK-004"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  0 with
                Damage = 10 }

        let potion =
            MatchScenario.PlainCard "potion" "KIT-012" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.WithCards original [ active; bench; potion ]
        let action = selected (choose CpuDifficulty.Normal 0UL state)

        match action.Command.Action with
        | MatchAction.PlayKit(card, _) -> card |> should equal potion.Id
        | other -> failwith $"Expected Potion, got {other}."

        cardChoice action |> should equal (Some [ active.Id ])

    [<Test>]
    member _.``normal should use Rain Dance to finish powering the active attacker``() =
        let energy =
            MatchScenario.PlainCard
                "rain-energy"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            { MatchScenario.BattleState "BLK-009" "BLK-004" [ "VIM-SOBER"; "VIM-SOBER" ] 3507UL with
                RoundUsage =
                    { RoundUsage.Empty MatchScenario.FirstPlayer with
                        VimAttachments = 1 } }
            |> fun table -> MatchScenario.WithCards table [ energy ]

        let action = selected (choose CpuDifficulty.Normal 0UL state)

        match action.Command.Action with
        | MatchAction.UsePartyTrick(source, effect) ->
            source |> should equal (CardInstanceId "attacker")
            effect |> should equal (EffectId "BLK-009-T01")
        | other -> failwith $"Expected Rain Dance, got {other}."

        action.Command.Choices
        |> Seq.choose (function
            | EffectChoice.Attachments(_, placements) -> Some(placements |> Seq.exactlyOne)
            | _ -> None)
        |> Seq.exactlyOne
        |> fun placement ->
            placement.Vim |> should equal energy.Id
            placement.Bloke |> should equal (CardInstanceId "attacker")

    [<Test>]
    member _.``normal should use Energy Burn once and then attack instead of repeating it``() =
        let engine = MatchScenario.Engine()

        let taxiAid =
            MatchScenario.PlainCard "taxi-aid" "BLK-085" MatchScenario.FirstPlayer CardZone.Booth 0

        let state =
            MatchScenario.BattleState
                "BLK-006"
                "BLK-004"
                [ "VIM-DODGY"; "VIM-DODGY"; "VIM-DODGY"; "VIM-DODGY" ]
                3516UL
            |> fun table -> MatchScenario.WithCards table [ taxiAid ]

        let first = selected (choose CpuDifficulty.Normal 0UL state)

        match first.Command.Action with
        | MatchAction.UsePartyTrick(source, effect) ->
            source |> should equal (CardInstanceId "attacker")
            effect |> should equal (EffectId "BLK-006-T01")
        | action -> failwith $"Expected the useful Energy Burn activation, got {action}."

        let afterBurn = MatchScenario.Applied(engine.Apply(state, first.Command))

        let rematerialized =
            { afterBurn with
                Effects =
                    afterBurn.Effects
                    |> Seq.map independentlyMaterializedEffect
                    |> ImmutableArray.CreateRange }

        let fairObservation =
            engine.GetCpuObservation(
                rematerialized,
                MatchScenario.FirstPlayer,
                CpuObservationMode.Fair
            )

        let fairState =
            CpuPlanning.createFairState engine rematerialized fairObservation 0UL 0UL

        let next = selected (choose CpuDifficulty.Normal 0UL fairState)

        match next.Command.Action with
        | MatchAction.Attack(attacker, effect) ->
            attacker |> should equal (CardInstanceId "attacker")
            effect |> should equal (EffectId "BLK-006-B01")
        | action ->
            failwith $"Expected the powered attack instead of repeated Energy Burn, got {action}."

        match engine.Apply(fairState, next.Command) with
        | CommandOutcome.Applied _ -> ()
        | CommandOutcome.Rejected(_, rejection) ->
            failwith $"The engine-issued attack was rejected with {rejection.Code}."

    [<Test>]
    member _.``normal should retreat a damaged rough active into a healthy attacker``() =
        let bench =
            MatchScenario.PlainCard
                "healthy-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let original =
            MatchScenario.BattleStateWith
                "BLK-001"
                "BLK-004"
                [ "VIM-BLAZED" ]
                3508UL
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.DodgyPint 2))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let damaged =
            { original.Card(CardInstanceId "attacker") with
                Damage = 30 }

        let state = MatchScenario.WithCards original [ damaged; bench ]

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.Taxi(target, payment) ->
            target |> should equal bench.Id
            payment |> Seq.toList |> should equal [ CardInstanceId "vim-0" ]
        | action -> failwith $"Expected a retreat, got {action}."

    [<Test>]
    member _.``normal should deal damage instead of repeating an existing rough state``() =
        let state =
            MatchScenario.BattleStateWith
                "BLK-039"
                "BLK-004"
                [ "VIM-DODGY"; "VIM-DODGY" ]
                3509UL
                ImmutableArray<_>.Empty
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 2))
                ImmutableArray<_>.Empty

        match selected (choose CpuDifficulty.Normal 0UL state) |> _.Command.Action with
        | MatchAction.Attack(_, effect) -> effect |> should equal (EffectId "BLK-039-B02")
        | action -> failwith $"Expected the damaging attack, got {action}."

    [<Test>]
    member _.``normal should pay an Energy cost to take a knockout and a Bar Chit``() =
        let original =
            MatchScenario.BattleState "BLK-004" "BLK-004" [ "VIM-CURRY"; "VIM-SOBER" ] 3510UL

        let defender =
            { original.Card(CardInstanceId "defender") with
                Damage = 20 }

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-007"
                MatchScenario.SecondPlayer
                CardZone.Booth
                0

        let barChit =
            MatchScenario.PlainCard
                "earned-bar-chit"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let state =
            MatchScenario.WithCards original [ defender; defenderBench; barChit ]
            |> fun table -> MatchScenario.WithBarChits table MatchScenario.FirstPlayer 1

        let engine = MatchScenario.Engine()

        let action =
            DeterministicCpu()
                .Choose(engine, state, MatchScenario.FirstPlayer, input CpuDifficulty.Normal 0UL)
            |> selected

        match action.Command.Action with
        | MatchAction.Attack(_, effect) -> effect |> should equal (EffectId "BLK-004-B02")
        | other -> failwith $"Expected the knockout attack, got {other}."

        let after = MatchScenario.Applied(engine.Apply(state, action.Command))

        (after.Card(CardInstanceId "defender")).Zone
        |> should equal CardZone.EmptiesTray

        (after.Card barChit.Id).Zone |> should equal CardZone.Mitt

        (after.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 0

    [<Test>]
    member _.``normal should use effective Weakness damage instead of paying an unnecessary attack cost``
        ()
        =
        let original =
            MatchScenario.BattleState "BLK-004" "BLK-001" [ "VIM-CURRY"; "VIM-SOBER" ] 3514UL

        let defender =
            { original.Card(CardInstanceId "defender") with
                Damage = 20 }

        let state = MatchScenario.WithCards original [ defender ]
        let action = selected (choose CpuDifficulty.Normal 0UL state)

        match action.Command.Action with
        | MatchAction.Attack(_, effect) -> effect |> should equal (EffectId "BLK-004-B01")
        | other -> failwith $"Expected the efficient knockout attack, got {other}."

        let after =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, action.Command))

        (after.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.Attached
        (after.Card(CardInstanceId "vim-1")).Zone |> should equal CardZone.Attached

    [<Test>]
    member _.``Easy should use its recorded seed to vary between plausible productive plays``() =
        let better =
            MatchScenario.PlainCard
                "better-basic"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let plausible =
            MatchScenario.PlainCard
                "plausible-basic"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.BattleState "BLK-004" "BLK-001" [] 3511UL
            |> fun table -> MatchScenario.WithCards table [ better; plausible ]

        let lower = selected (choose CpuDifficulty.Easy 0UL state)
        let best = selected (choose CpuDifficulty.Easy 2UL state)
        let repeated = selected (choose CpuDifficulty.Easy 0UL state)

        repeated |> should equal lower

        match lower.Command.Action, best.Command.Action with
        | MatchAction.PlayBloke lowerCard, MatchAction.PlayBloke bestCard ->
            lowerCard |> should equal plausible.Id
            bestCard |> should equal better.Id
        | actions -> failwith $"Expected two productive bench plays, got {actions}."

    [<Test>]
    member _.``Hard should repeat bounded forward planning through engine candidates``() =
        let basic =
            MatchScenario.PlainCard
                "hard-basic"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let energy =
            MatchScenario.PlainCard
                "hard-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let trainer =
            MatchScenario.PlainCard
                "hard-trainer"
                "KIT-007"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.BattleState "BLK-004" "BLK-001" [] 3512UL
            |> fun table -> MatchScenario.WithCards table [ basic; energy; trainer ]

        let first = choose CpuDifficulty.Hard 17UL state
        let repeated = choose CpuDifficulty.Hard 17UL state

        repeated |> should equal first

        first.Evidence.Work.NodesVisited
        |> should be (lessThanOrEqualTo first.Evidence.Work.NodeLimit)

        first.Evidence.Work.DepthReached
        |> should be (lessThanOrEqualTo first.Evidence.Work.DepthLimit)

        first.Evidence.Work.DepthReached |> should be (greaterThan 1)
        first.Evidence.Work.SamplesEvaluated |> should equal 2

        let observation =
            MatchScenario
                .Engine()
                .GetCpuObservation(state, MatchScenario.FirstPlayer, CpuObservationMode.Fair)

        observation.Candidates
        |> Seq.truncate first.Evidence.Work.NodeLimit
        |> Seq.map _.Id
        |> should contain first.Evidence.Candidate.Value

    [<Test>]
    member _.``CPU policy bounds a valid Wildfire choice domain before materializing it``() =
        let engine = MatchScenario.Engine()
        let state = highCombinationWildfireState ()

        let choice =
            DeterministicCpu()
                .Choose(engine, state, MatchScenario.FirstPlayer, input CpuDifficulty.Normal 0UL)

        choice.Evidence.Work.CandidatesConsidered
        |> should equal CpuPolicyLimits.rootCandidateLimit

        choice.Evidence.Work.NodesVisited
        |> should be (lessThanOrEqualTo choice.Evidence.Work.NodeLimit)

        let action = selected choice
        let candidate = choice.Evidence.Candidate.Value

        engine.TryMaterializeCpuCommand(state, MatchScenario.FirstPlayer, candidate)
        |> should equal (ValueSome action.Command)

        match engine.Apply(state, action.Command) with
        | CommandOutcome.Applied _ -> ()
        | CommandOutcome.Rejected(_, rejection) ->
            failwith $"Engine-issued CPU action was rejected with {rejection.Code}."

    [<Test>]
    member _.``every CPU policy ends a round when no productive move exists``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-004" [] 3516UL

        for difficulty in
            [ CpuDifficulty.Easy
              CpuDifficulty.Normal
              CpuDifficulty.Hard
              CpuDifficulty.Impossible ] do
            let choice =
                DeterministicCpu()
                    .Choose(engine, state, MatchScenario.FirstPlayer, input difficulty 19UL)

            let action = selected choice
            action.Kind |> should equal LegalActionKind.EndRound
            action.Command.Action |> should equal MatchAction.EndRound

            let candidate = choice.Evidence.Candidate.Value

            engine.TryMaterializeCpuCommand(state, MatchScenario.FirstPlayer, candidate)
            |> should equal (ValueSome action.Command)

            match engine.Apply(state, action.Command) with
            | CommandOutcome.Applied _ -> ()
            | CommandOutcome.Rejected(_, rejection) ->
                failwith $"{difficulty} engine-issued EndRound was rejected with {rejection.Code}."

    [<Test>]
    member _.``fair policies should ignore hidden identities while Impossible should use them``() =
        let trainer =
            MatchScenario.PlainCard "top-up" "KIT-015" MatchScenario.FirstPlayer CardZone.Mitt -1

        let strong =
            MatchScenario.PlainCard "stack-a" "BLK-113" MatchScenario.FirstPlayer CardZone.Stack 1

        let energy =
            MatchScenario.PlainCard "stack-b" "VIM-SOBER" MatchScenario.FirstPlayer CardZone.Stack 2

        let discardOne =
            MatchScenario.PlainCard
                "discard-one"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let discardTwo =
            MatchScenario.PlainCard
                "discard-two"
                "VIM-CURRY"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let hiddenOpponentCard =
            MatchScenario.PlainCard
                "opponent-hidden"
                "BLK-113"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state =
            { MatchScenario.BattleStateWith
                  "BLK-004"
                  "BLK-001"
                  []
                  3513UL
                  (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 2))
                  ImmutableArray<_>.Empty
                  ImmutableArray<_>.Empty with
                RoundUsage =
                    { RoundUsage.Empty MatchScenario.FirstPlayer with
                        VimAttachments = 1 } }
            |> fun table ->
                MatchScenario.WithCards
                    table
                    [ trainer; strong; energy; discardOne; discardTwo; hiddenOpponentCard ]

        let substituted =
            MatchScenario.WithCards
                state
                [ { strong with
                      MechanicalId = energy.MechanicalId
                      Kind = energy.Kind
                      StackPosition = energy.StackPosition }
                  { energy with
                      MechanicalId = strong.MechanicalId
                      Kind = strong.Kind
                      StackPosition = strong.StackPosition }
                  { hiddenOpponentCard with
                      MechanicalId = MechanicalCardId "VIM-GEEKED"
                      Kind = CardKind.Vim } ]

        let assertFairInvariant difficulty =
            let original = choose difficulty 31UL state
            let afterSubstitution = choose difficulty 31UL substituted

            afterSubstitution.Evidence.Candidate |> should equal original.Evidence.Candidate

        assertFairInvariant CpuDifficulty.Easy
        assertFairInvariant CpuDifficulty.Normal
        assertFairInvariant CpuDifficulty.Hard

        let impossible = selected (choose CpuDifficulty.Impossible 31UL state)
        let repeatedImpossible = selected (choose CpuDifficulty.Impossible 31UL substituted)

        let searchedCard (action: LegalAction) =
            action.Command.Choices
            |> Seq.choose (function
                | EffectChoice.Cards(_, cards) -> Some(cards |> Seq.toList)
                | _ -> None)
            |> Seq.last

        searchedCard impossible |> should equal [ strong.Id ]
        searchedCard repeatedImpossible |> should equal [ energy.Id ]

        let firstApplied =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, impossible.Command))

        let secondApplied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(substituted, repeatedImpossible.Command)
            )

        (firstApplied.Card strong.Id).Zone |> should equal CardZone.Mitt
        (secondApplied.Card energy.Id).Zone |> should equal CardZone.Mitt
