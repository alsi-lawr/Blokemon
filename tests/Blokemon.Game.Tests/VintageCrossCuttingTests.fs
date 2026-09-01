namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

type VintageCrossCuttingTests() =

    [<Test>]
    member _.``evolution should retain damage and Energy while clearing conditions and attack effects``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [ "VIM-BLAZED" ] 1201UL

        let target =
            { original.Card(CardInstanceId "attacker") with
                Damage = 20
                RoughStates =
                    ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 1)
                EnteredAtOwnerRound = 1 }

        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-002" MatchScenario.FirstPlayer CardZone.Mitt -1

        let attackEffect =
            { SourceEffect = EffectId "BLK-001-B01"
              SourceCard = target.Id
              Owner = MatchScenario.FirstPlayer
              TargetCard = ValueSome target.Id
              Kind = TemporaryEffectKind.RestrictAttack
              Amount = 0
              MechanicalTypes = ImmutableArray<_>.Empty
              RoughStates = ImmutableArray<_>.Empty
              RelatedCards = ImmutableArray<_>.Empty
              Conditions = ImmutableArray<_>.Empty
              Duration = EffectDuration.UntilEndOfOpponentsNextRound
              AppliesFromRound = original.RoundNumber
              ExpiresAfterRound = original.RoundNumber + 1 }

        let state =
            { MatchScenario.WithCards original [ target; promotion ] with
                Effects = ImmutableArray.Create attackEffect }

        let evolved =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "evolve-with-state"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            (MatchAction.Promote(promotion.Id, target.Id))
                    )
            )

        let evolvedCard = evolved.Card promotion.Id
        evolvedCard.Zone |> should equal CardZone.Oche
        evolvedCard.Damage |> should equal 20
        evolvedCard.Attachments |> Seq.toList |> should equal [ CardInstanceId "vim-0" ]
        evolvedCard.RoughStates |> should be Empty

        (evolved.Card(CardInstanceId "vim-0")).AttachedTo
        |> should equal (ValueSome promotion.Id)

        evolved.Effects
        |> Seq.exists (fun effect -> effect.Kind = TemporaryEffectKind.RestrictAttack)
        |> should be False

    [<Test>]
    [<Arguments("KIT-001")>]
    [<Arguments("KIT-002")>]
    member _.``discarding a Doll or Fossil should discard its attachments and require replacement without awarding a Prize``
        (mechanicalId: string)
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1202UL

        let standIn =
            { MatchScenario.PlainCard
                  "attacker"
                  mechanicalId
                  MatchScenario.FirstPlayer
                  CardZone.Oche
                  -1 with
                Attachments = ImmutableArray.Create(CardInstanceId "stand-in-energy") }

        let energy =
            MatchScenario.AttachedCard
                "stand-in-energy"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                standIn.Id

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let state = MatchScenario.WithCards original [ standIn; energy; replacement ]
        let engine = MatchScenario.Engine()

        let discarded =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.Command
                        state
                        $"discard:{mechanicalId}"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ChuckFossil standIn.Id)
                )
            )

        (discarded.Card standIn.Id).Zone |> should equal CardZone.EmptiesTray
        (discarded.Card energy.Id).Zone |> should equal CardZone.EmptiesTray

        (discarded.Player MatchScenario.SecondPlayer).BarChitsRemaining
        |> should equal 6

        discarded.Phase |> should equal MatchPhase.AwaitingReplacement

        discarded.ReplacementPlayer
        |> should equal (ValueSome MatchScenario.FirstPlayer)

        let replaced =
            MatchScenario.Applied(
                engine.Apply(
                    discarded,
                    MatchScenario.Command
                        discarded
                        $"replace:{mechanicalId}"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        (MatchAction.ChooseReplacement replacement.Id)
                )
            )

        (replaced.Card replacement.Id).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``checkup should finish both Poison knockouts before deciding the tied game``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-001" [] 1203UL

        let poisoned card =
            { original.Card(CardInstanceId card) with
                Damage = 30
                RoughStates =
                    ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.DodgyPint 1) }

        let state =
            MatchScenario.WithCards original [ poisoned "attacker"; poisoned "defender" ]
            |> MatchScenario.WithRestartableDecks

        let restarted, events =
            MatchScenario.AppliedWith(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "simultaneous-checkup"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            MatchAction.EndRound
                    )
            )

        events
        |> Seq.filter (fun event -> event.Kind = MatchEventKind.BlokeSentHome)
        |> Seq.map (fun event -> event.SourceCard.Value)
        |> Seq.truncate 2
        |> Seq.toList
        |> should equal [ CardInstanceId "attacker"; CardInstanceId "defender" ]

        events
        |> Seq.exists (fun event -> event.Kind = MatchEventKind.SuddenDeathStarted)
        |> should be True

        restarted.Phase |> should equal MatchPhase.OpeningPlacement
        restarted.Winner.IsNone |> should be True

    [<Test>]
    member _.``starting a required draw from an empty Deck should lose without drawing``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1204UL

        let emptySecondDeck =
            { original.Card(CardInstanceId "second-draw") with
                Zone = CardZone.EmptiesTray
                StackPosition = -1 }

        let state = MatchScenario.WithCards original [ emptySecondDeck ]

        let secondMittBefore =
            state.CardsIn(MatchScenario.SecondPlayer, CardZone.Mitt) |> Seq.length

        let finished, events =
            MatchScenario.AppliedWith(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "empty-required-draw"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            MatchAction.EndRound
                    )
            )

        finished.Winner |> should equal (ValueSome MatchScenario.FirstPlayer)
        finished.Phase |> should equal MatchPhase.Complete

        finished.CardsIn(MatchScenario.SecondPlayer, CardZone.Mitt)
        |> Seq.length
        |> should equal secondMittBefore

        events
        |> Seq.exists (fun event ->
            event.Kind = MatchEventKind.CardsDrawn
            && event.Actor = ValueSome MatchScenario.SecondPlayer)
        |> should be False

    [<Test>]
    member _.``retreat Aid should reduce a Bench teammate's fare by one``() =
        let original = MatchScenario.BattleState "BLK-040" "BLK-001" [ "VIM-SOBER" ] 1205UL

        let dodrio =
            MatchScenario.PlainCard "dodrio" "BLK-085" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ dodrio ]

        let taxi =
            MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action -> action.Kind = LegalActionKind.Taxi)

        taxi.Affordability |> should equal ActionAffordability.Payable

        let retreated =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, taxi.Command))

        (retreated.Card dodrio.Id).Zone |> should equal CardZone.Oche

        (retreated.Card(CardInstanceId "vim-0")).Zone
        |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``retreat Aid should not remain active while Dodrio is Active``() =
        let original = MatchScenario.BattleState "BLK-085" "BLK-001" [] 1206UL

        let persistedBenchModifier =
            { SourceEffect = EffectId "BLK-085-T01"
              SourceCard = CardInstanceId "attacker"
              Owner = MatchScenario.FirstPlayer
              TargetCard = ValueSome(CardInstanceId "attacker")
              Kind = TemporaryEffectKind.ModifyTaxiFare
              Amount = -1
              MechanicalTypes = ImmutableArray<_>.Empty
              RoughStates = ImmutableArray<_>.Empty
              RelatedCards = ImmutableArray<_>.Empty
              Conditions = ImmutableArray<_>.Empty
              Duration = EffectDuration.WhileSourceInPlay
              AppliesFromRound = original.RoundNumber
              ExpiresAfterRound = System.Int32.MaxValue }

        let state =
            { original with
                Effects = ImmutableArray.Create persistedBenchModifier }

        let advanced =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        MatchScenario.Command
                            state
                            "active-dodrio"
                            MatchScenario.FirstPlayer
                            ImmutableArray<_>.Empty
                            MatchAction.EndRound
                    )
            )

        advanced.Effects
        |> Seq.exists (fun effect -> effect.SourceEffect = EffectId "BLK-085-T01")
        |> should be False
