namespace Blokemon.Game.Tests

open System
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private CardSemanticsFixtures =

    let attackDamage (events: FrozenList<MatchEvent>) =
        events
        |> Seq.filter (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.DamagePlaced
            && matchEvent.DamageKind = ValueSome DamageKind.Attack
            && Seq.contains (CardInstanceId "defender") matchEvent.TargetCards)
        |> Seq.exactlyOne
        |> fun matchEvent -> matchEvent.Amount

    let kitAction (engine: MatchEngine) (state: MatchState) (kit: CardInstanceId) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action ->
            action.Kind = LegalActionKind.PlayKit
            && (match action.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = kit
                | _ -> false))
        |> Seq.exactlyOne

    let namedMateState (mateId: string) =
        let state =
            MatchScenario.BattleState
                "BLK-112"
                "BLK-150"
                [ "VIM-LAIRY"; "VIM-LAIRY"; "VIM-SOBER" ]
                (if mateId = "KIT-010" then 803UL else 801UL)

        { state with
            RoundUsage =
                { state.RoundUsage with
                    MatesPlayed = 1
                    KitsPlayed = FrozenList<MechanicalCardId>.Create(MechanicalCardId mateId) } }

    let attackGateState (seed: uint64) =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-SOBER" ] seed

        { state with
            Effects =
                FrozenList<TemporaryEffect>.Create
                    { SourceEffect = EffectId "BLK-117-B01"
                      SourceCard = CardInstanceId "defender"
                      Owner = MatchScenario.SecondPlayer
                      TargetCard = ValueSome(CardInstanceId "attacker")
                      Kind = TemporaryEffectKind.RestrictAttackOnBeerMat
                      Amount = 2
                      MechanicalTypes = FrozenList.empty
                      RoughStates = FrozenList.empty
                      RelatedCards = FrozenList.empty
                      Conditions = FrozenList.empty
                      Duration = EffectDuration.UntilEndOfOpponentsNextRound
                      AppliesFromRound = state.RoundNumber
                      ExpiresAfterRound = state.RoundNumber + 1 } }

    let seedForTwoTosses (allBadges: bool) =
        let rec search (seed: uint64) =
            if seed >= 1000UL then
                failwith "No two-toss seed was found."
            else
                let random = BlokemonSeededRandom seed
                let result = random.NextInt 2 = 1 && random.NextInt 2 = 1

                if result = allBadges then seed else search (seed + 1UL)

        search 0UL

type CardSemanticsTests() =

    [<Test>]
    member _.``a named-mate condition should match only the mate it names``() =
        let engine = MatchScenario.Engine()
        let wrongMate = namedMateState "KIT-009"
        let requiredMate = namedMateState "KIT-010"

        let _, wrongEvents =
            MatchScenario.AppliedWith(
                engine.Apply(wrongMate, MatchScenario.AttackCommand wrongMate "BLK-112-B02")
            )

        let _, requiredEvents =
            MatchScenario.AppliedWith(
                engine.Apply(requiredMate, MatchScenario.AttackCommand requiredMate "BLK-112-B02")
            )

        attackDamage wrongEvents |> should equal 10
        attackDamage requiredEvents |> should equal 150

    [<Test>]
    member _.``a typed attached-vim value should count only the type it asked for``() =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-119"
                "BLK-150"
                [ "VIM-SOBER"; "VIM-BEER"; "VIM-BEER" ]
                811UL

        let _, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-119-B02")
            )

        attackDamage events |> should equal 90

    [<Test>]
    member _.``a conditional move branch should not attach vim when nothing was returned``() =
        let engine = MatchScenario.Engine()

        let ownVim =
            MatchScenario.PlainCard
                "own-mitt-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let kit =
            MatchScenario.PlainCard "guvnor" "KIT-010" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-001" "BLK-150" [] 821UL)
                [ ownVim; kit ]

        let play = kitAction engine state kit.Id
        let applied = MatchScenario.Applied(engine.Apply(state, play.Command))

        (applied.Card ownVim.Id).Zone |> should equal CardZone.Mitt
        (applied.Card ownVim.Id).AttachedTo.IsNone |> should be True

    [<Test>]
    member _.``a conditional move branch should ask for and attach vim after a return``() =
        let engine = MatchScenario.Engine()

        let ownVim =
            MatchScenario.PlainCard
                "own-mitt-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let opposingVim =
            MatchScenario.AttachedCard
                "opposing-vim"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let kit =
            MatchScenario.PlainCard "guvnor" "KIT-010" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 823UL

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create opposingVim.Id }

        let state = MatchScenario.WithCards state [ defender; ownVim; opposingVim; kit ]

        let play = kitAction engine state kit.Id
        let requested = MatchScenario.Applied(engine.Apply(state, play.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    requirement.Id,
                                    FrozenList<CardInstanceId>.Create ownVim.Id
                                )
                            ))
                )
            )

        (resolved.Card opposingVim.Id).Zone |> should equal CardZone.Mitt

        (resolved.Card ownVim.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "attacker"))

    [<Test>]
    member _.``a stack move should take each bloke together with the cards attached to it``() =
        let engine = MatchScenario.Engine()

        let ownVim =
            MatchScenario.AttachedCard
                "own-vim"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let opposingBench =
            MatchScenario.PlainCard
                "opposing-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let opposingVim =
            MatchScenario.AttachedCard
                "opposing-vim"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                opposingBench.Id

        let state =
            MatchScenario.BattleState "BLK-012" "BLK-150" [ "VIM-SOBER"; "VIM-SOBER" ] 827UL

        let attacker = state.Card(CardInstanceId "attacker")

        let state =
            MatchScenario.WithCards
                state
                [ { attacker with
                      Attachments =
                          FrozenList<CardInstanceId>
                              .Create(Seq.append attacker.Attachments [ ownVim.Id ]) }
                  { opposingBench with
                      Attachments = FrozenList<CardInstanceId>.Create opposingVim.Id }
                  ownVim
                  opposingVim ]

        let attack =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun action ->
                action.Kind = LegalActionKind.Attack
                && (match action.Command.Action with
                    | MatchAction.Attack(_, attackId) -> attackId = EffectId "BLK-012-B02"
                    | _ -> false))
            |> Seq.exactlyOne
            |> fun action -> action.Command

        let requested = MatchScenario.Applied(engine.Apply(state, attack))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    requirement.Id,
                                    FrozenList<CardInstanceId>.Create opposingBench.Id
                                )
                            ))
                )
            )

        for card in [ CardInstanceId "attacker"; ownVim.Id; opposingBench.Id; opposingVim.Id ] do
            (resolved.Card card).Zone |> should equal CardZone.Stack

        (resolved.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``an attack gate should cancel on either blank and allow the attack on two badges``() =
        let blocked = attackGateState (seedForTwoTosses false)
        let allowed = attackGateState (seedForTwoTosses true)
        let engine = MatchScenario.Engine()

        let blockedState, blockedEvents =
            MatchScenario.AppliedWith(
                engine.Apply(blocked, MatchScenario.AttackCommand blocked "BLK-001-B01")
            )

        let allowedState, _ =
            MatchScenario.AppliedWith(
                engine.Apply(allowed, MatchScenario.AttackCommand allowed "BLK-001-B01")
            )

        blockedEvents
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.BeerMatTossed)
        |> Seq.length
        |> should equal 2

        blockedEvents
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackCancelled)
        |> should be True

        (blockedState.Card(CardInstanceId "defender")).Damage |> should equal 0
        (allowedState.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``playing a fossil kit should be ungated and should put the kit into the booth``() =
        let engine = MatchScenario.Engine()

        let fossil =
            MatchScenario.PlainCard "reenactor" "KIT-001" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-001" "BLK-150" [] 829UL)
                [ fossil ]

        let play = kitAction engine state fossil.Id
        let applied = MatchScenario.Applied(engine.Apply(state, play.Command))

        play.ChoiceRequirements
        |> Seq.exists (fun requirement -> requirement.Kind = ChoiceRequirementKind.Optional)
        |> should be False

        (applied.Card fossil.Id).Zone |> should equal CardZone.Booth

        engine.GetLegalActions(applied, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            action.Kind = LegalActionKind.ChuckFossil
            && (match action.Command.Action with
                | MatchAction.ChuckFossil chucked -> chucked = fossil.Id
                | _ -> false))
        |> should be True

    [<Test>]
    member _.``every fossil kit should share the same ungated play program``() =
        for fossilId in [ "KIT-001"; "KIT-002"; "KIT-003" ] do
            let rule =
                MatchScenario.Authority.Kits
                |> Array.find (fun kit -> kit.Id = fossilId)
                |> fun kit ->
                    kit.HouseRules
                    |> Array.find (fun houseRule -> houseRule.MechanicalId = $"{fossilId}-R01")

            rule.Program
            |> Array.map (fun instruction -> instruction.Opcode)
            |> Array.toList
            |> should
                equal
                [ BlokemonOpcode.ModifyTaxiFare
                  BlokemonOpcode.RestrictTaxi
                  BlokemonOpcode.PlayAsBloke
                  BlokemonOpcode.ChuckSelf ]

            rule.Program
            |> Array.forall (fun instruction ->
                instruction.Predicates.Length = 0
                && instruction.Then.Length = 0
                && instruction.Otherwise.Length = 0)
            |> should be True

    [<Test>]
    member _.``revealing cards should emit one generic reveal and leave the bar chits where they were``
        ()
        =
        let engine = MatchScenario.Engine()

        let auntie =
            MatchScenario.PlainCard "auntie" "KIT-007" MatchScenario.FirstPlayer CardZone.Mitt -1

        let prizes =
            [ for index in 0..5 ->
                  MatchScenario.PlainCard
                      $"prize-{index}"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      index ]

        let stacked =
            [ for index in 0..1 ->
                  MatchScenario.PlainCard
                      $"stacked-{index}"
                      "VIM-LAIRY"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      index ]

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-001" "BLK-150" [] 833UL)
                (List.concat [ [ auntie ]; prizes; stacked ])

        let play = kitAction engine state auntie.Id

        let appliedState, events =
            MatchScenario.AppliedWith(engine.Apply(state, play.Command))

        let reveal =
            events
            |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.CardsRevealed)
            |> Seq.exactlyOne

        reveal.TargetCards
        |> Seq.map (fun card -> card.Value)
        |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
        |> Seq.toList
        |> should
            equal
            (prizes
             |> List.map (fun prize -> prize.Id.Value)
             |> List.sortWith (fun left right -> String.CompareOrdinal(left, right)))

        prizes
        |> List.iteri (fun index prize ->
            let after = appliedState.Card prize.Id
            after.Zone |> should equal CardZone.BarChit
            after.StackPosition |> should equal index)
