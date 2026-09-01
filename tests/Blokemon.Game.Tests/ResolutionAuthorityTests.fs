namespace Blokemon.Game.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private ResolutionAuthorityScenarios =

    let expectedAttackSteps =
        [ BlokemonAttackResolutionStep.ValidateDeclaredAttackAndVim
          BlokemonAttackResolutionStep.ResolveMuddledCheck
          BlokemonAttackResolutionStep.MakeRequiredChoices
          BlokemonAttackResolutionStep.PayOrPerformUseRequirements
          BlokemonAttackResolutionStep.ApplyEffectsThatAlterOrCancelAttack
          BlokemonAttackResolutionStep.ApplyBeforeDamageEffects
          BlokemonAttackResolutionStep.CalculateAndPlaceDamage
          BlokemonAttackResolutionStep.ResolveOtherEffects
          BlokemonAttackResolutionStep.CheckAllSentHome
          BlokemonAttackResolutionStep.TakeBarChitsAndPromote
          BlokemonAttackResolutionStep.EndRound ]

    let expectedDamageSteps =
        [ BlokemonDamageResolutionStep.PrintedOrProgramBaseDamage
          BlokemonDamageResolutionStep.EffectsOnAttackingBloke
          BlokemonDamageResolutionStep.StopWhenDamageIsZero
          BlokemonDamageResolutionStep.Weakness
          BlokemonDamageResolutionStep.Resistance
          BlokemonDamageResolutionStep.TrainerEffects
          BlokemonDamageResolutionStep.PokemonPowers
          BlokemonDamageResolutionStep.PlaceDamageCounters
          BlokemonDamageResolutionStep.EffectsAfterDamage ]

    let tracedEngine () =
        let trace = ResizeArray<ResolutionTraceEntry>()
        let engine = MatchScenario.Engine()
        engine.ResolutionTrace <- trace.Add
        engine, trace

    let attackSteps (trace: ResizeArray<ResolutionTraceEntry>) =
        trace
        |> Seq.choose (function
            | AttackStep step -> Some step
            | DamageStep _ -> None)
        |> Seq.toList

    let damageSteps (trace: ResizeArray<ResolutionTraceEntry>) =
        trace
        |> Seq.choose (function
            | DamageStep step -> Some step
            | AttackStep _ -> None)
        |> Seq.toList

    let attackGateState (seed: uint64) =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-BLAZED" ] seed

        { state with
            Effects =
                ImmutableArray.Create
                    { SourceEffect = EffectId "BLK-117-B01"
                      SourceCard = CardInstanceId "defender"
                      Owner = MatchScenario.SecondPlayer
                      TargetCard = ValueSome(CardInstanceId "attacker")
                      Kind = TemporaryEffectKind.RestrictAttackOnBeerMat
                      Amount = 2
                      MechanicalTypes = ImmutableArray<_>.Empty
                      RoughStates = ImmutableArray<_>.Empty
                      RelatedCards = ImmutableArray<_>.Empty
                      Conditions = ImmutableArray<_>.Empty
                      Duration = EffectDuration.UntilEndOfOpponentsNextRound
                      AppliesFromRound = state.RoundNumber
                      ExpiresAfterRound = state.RoundNumber + 1 } }

    let seedWithBlankGate () =
        let rec search seed =
            if seed >= 1000UL then
                failwith "No cancelling two-toss seed was found."
            else
                let random = BlokemonSeededRandom seed
                let bothBadges = random.NextInt 2 = 1 && random.NextInt 2 = 1

                if bothBadges then search (seed + 1UL) else seed

        search 0UL

type ResolutionAuthorityTests() =

    [<Test>]
    member _.``a completed damage attack should execute both authority orders and finish the round``
        ()
        =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-BLAZED" ] 1201UL

        let engine, trace = tracedEngine ()

        let applied, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        attackSteps trace |> should equal expectedAttackSteps
        damageSteps trace |> should equal expectedDamageSteps
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20
        applied.ActivePlayer |> should equal MatchScenario.SecondPlayer

        events
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.RoundEnded)
        |> should be True

    [<Test>]
    member _.``bench damage should use the damage order without soft spot modification``() =
        let original =
            MatchScenario.BattleState
                "BLK-094"
                "BLK-001"
                [ "VIM-GEEKED"; "VIM-GEEKED"; "VIM-GEEKED" ]
                1202UL

        let opposingBench =
            MatchScenario.PlainCard
                "opposing-bench"
                "BLK-150"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards original [ opposingBench ]
        let engine, trace = tracedEngine ()

        let requirement =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action ->
                match action.Command.Action with
                | MatchAction.Attack(_, effect) -> effect = EffectId "BLK-094-B01"
                | _ -> false)
            |> _.ChoiceRequirements
            |> Seq.exactlyOne

        let choice =
            EffectChoice.Cards(requirement.Id, ImmutableArray.Create opposingBench.Id)

        let applied =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-094-B01"
                        (ImmutableArray.Create choice)
                )
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 30
        (applied.Card opposingBench.Id).Damage |> should equal 10

    [<Test>]
    member _.``a cancelled attack should stop at the cancelling authority step``() =
        let state = attackGateState (seedWithBlankGate ())
        let engine, trace = tracedEngine ()

        let applied, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        attackSteps trace
        |> should
            equal
            [ BlokemonAttackResolutionStep.ValidateDeclaredAttackAndVim
              BlokemonAttackResolutionStep.ResolveMuddledCheck
              BlokemonAttackResolutionStep.MakeRequiredChoices
              BlokemonAttackResolutionStep.PayOrPerformUseRequirements
              BlokemonAttackResolutionStep.ApplyEffectsThatAlterOrCancelAttack ]

        damageSteps trace |> should be Empty

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackCancelled)
        |> Seq.exactlyOne
        |> fun matchEvent -> matchEvent.Effect
        |> should equal (ValueSome(EffectId "BLK-001-B01"))

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 0

    [<Test>]
    member _.``a deferred opponent choice should resume at the next authority step``() =
        let original = MatchScenario.BattleState "BLK-012" "BLK-001" [ "VIM-DODGY" ] 1203UL

        let opposingBench =
            MatchScenario.PlainCard
                "opposing-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards original [ opposingBench ]
        let engine, trace = tracedEngine ()

        let requested, _ =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-012-B01")
            )

        attackSteps trace |> should equal (expectedAttackSteps |> List.take 3)
        damageSteps trace |> should be Empty
        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice

        let pending = requested.PendingEffect.Value
        let requirement = pending.Requirements |> Seq.exactlyOne

        let resolve =
            MatchScenario.ResolveEffectChoiceCommandBy
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(requirement.Id, ImmutableArray.Create opposingBench.Id)
                ))
                pending.Chooser

        let resolved = MatchScenario.Applied(engine.Apply(requested, resolve))

        attackSteps trace |> should equal expectedAttackSteps
        damageSteps trace |> should equal expectedDamageSteps
        (resolved.Card(CardInstanceId "defender")).Damage |> should equal 20
        (resolved.Card opposingBench.Id).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``a no-damage attack should execute the attack order without a damage calculation``() =
        let state = MatchScenario.BattleState "BLK-040" "BLK-150" [ "VIM-SOBER" ] 1205UL

        let engine, trace = tracedEngine ()

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-040-B01")
            )

        attackSteps trace |> should equal expectedAttackSteps
        damageSteps trace |> should be Empty

        (applied.Card(CardInstanceId "defender")).RoughStates
        |> Seq.map (fun entry -> entry.State)
        |> should contain BlokemonRoughState.NoddedOff
