namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private VintagePokemonPowerScenarios =

    let powerAction effect (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.find (fun action ->
            match action.Command.Action with
            | MatchAction.UsePartyTrick(_, power) -> power = EffectId effect
            | _ -> false)

    let hasPowerAction effect (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            match action.Command.Action with
            | MatchAction.UsePartyTrick(_, power) -> power = EffectId effect
            | _ -> false)

type VintagePokemonPowerTests() =

    [<Test>]
    member _.``shift should require another Pokemon's non-Colorless type and change attack damage``
        ()
        =
        let boundary = MatchScenario.BattleState "BLK-049" "BLK-085" [] 1301UL

        hasPowerAction "BLK-049-T01" boundary |> should be False

        let firePokemon =
            MatchScenario.PlainCard
                "fire-pokemon"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let state =
            MatchScenario.BattleState "BLK-049" "BLK-001" [ "VIM-BLAZED"; "VIM-BLAZED" ] 1301UL
            |> fun state -> MatchScenario.WithCards state [ firePokemon ]

        let unshifted =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-049-B01")
            )

        (unshifted.Card(CardInstanceId "defender")).Damage |> should equal 10

        let action = powerAction "BLK-049-T01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        requirement.EligibleMechanicalTypes
        |> should contain BlokemonMechanicalType.Fire

        requirement.EligibleMechanicalTypes
        |> should not' (contain BlokemonMechanicalType.Colorless)

        let shifted =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.MechanicalType(
                                        requirement.Id,
                                        BlokemonMechanicalType.Fire
                                    )
                                ) }
                    )
            )

        let attacked =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(shifted, MatchScenario.AttackCommand shifted "BLK-049-B01")
            )

        (attacked.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``cowardice should return an established damaged Tentacool and discard its Energy``() =
        let original = MatchScenario.BattleState "BLK-072" "BLK-001" [ "VIM-SOBER" ] 1302UL

        let tentacool =
            { original.Card(CardInstanceId "attacker") with
                Damage = 10
                EnteredAtOwnerRound = 1 }

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let state = MatchScenario.WithCards original [ tentacool; replacement ]
        let engine = MatchScenario.Engine()

        let returned =
            MatchScenario.Applied(engine.Apply(state, (powerAction "BLK-072-T01" state).Command))

        (returned.Card tentacool.Id).Zone |> should equal CardZone.Mitt
        (returned.Card tentacool.Id).Damage |> should equal 0

        (returned.Card(CardInstanceId "vim-0")).Zone
        |> should equal CardZone.EmptiesTray

        returned.Phase |> should equal MatchPhase.AwaitingReplacement

        let newlyPlayed =
            MatchScenario.WithCards
                original
                [ { tentacool with
                      Damage = 0
                      EnteredAtOwnerRound = 2 }
                  replacement ]

        hasPowerAction "BLK-072-T01" newlyPlayed |> should be False

    [<Test>]
    member _.``cowardice from the only active should emit one win when terminal resolution is revisited``
        ()
        =
        let state = MatchScenario.BattleState "BLK-072" "BLK-001" [] 1312UL
        let tentacool = state.Card(CardInstanceId "attacker")
        let engine = MatchScenario.Engine()

        let finished, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, (powerAction "BLK-072-T01" state).Command)
            )

        (finished.Card tentacool.Id).Zone |> should equal CardZone.Mitt
        finished.Winner |> should equal (ValueSome MatchScenario.SecondPlayer)
        finished.Phase |> should equal MatchPhase.Complete

        let matchWon =
            events
            |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.MatchWon)
            |> Seq.exactlyOne

        matchWon.Actor |> should equal (ValueSome MatchScenario.SecondPlayer)

        let afterCompletion =
            MatchScenario.Command
                finished
                "after-terminal-cowardice"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                MatchAction.EndRound

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(finished, afterCompletion))

        rejection.Code |> should equal CommandRejectionCode.MatchComplete
        rejectedState |> should equal finished

    [<Test>]
    member _.``buzzap should award one Prize and make Electrode provide two chosen Energy``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1303UL

        let electrode =
            MatchScenario.PlainCard "electrode" "BLK-101" MatchScenario.FirstPlayer CardZone.Booth 0

        let opponentPrize =
            MatchScenario.PlainCard
                "opponent-prize"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.BarChit
                0

        let state = MatchScenario.WithCards original [ electrode; opponentPrize ]
        let action = powerAction "BLK-101-T01" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                match requirement.Kind with
                | ChoiceRequirementKind.Cards ->
                    EffectChoice.Cards(
                        requirement.Id,
                        ImmutableArray.Create(CardInstanceId "attacker")
                    )
                | ChoiceRequirementKind.MechanicalType ->
                    EffectChoice.MechanicalType(requirement.Id, BlokemonMechanicalType.Grass)
                | other -> failwithf "Unexpected Buzzap requirement %A." other)
            |> ImmutableArray.CreateRange

        let engine = MatchScenario.Engine()

        let transformed =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices = choices }
                )
            )

        (transformed.Card electrode.Id).Zone |> should equal CardZone.Attached

        (transformed.Card electrode.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "attacker"))

        (transformed.Player MatchScenario.SecondPlayer).BarChitsRemaining
        |> should equal 5

        (transformed.Card opponentPrize.Id).Zone |> should equal CardZone.Mitt

        let attack =
            engine.GetLegalActions(transformed, MatchScenario.FirstPlayer)
            |> Seq.find (fun candidate ->
                match candidate.Command.Action with
                | MatchAction.Attack(_, effect) -> effect = EffectId "BLK-001-B01"
                | _ -> false)

        let attacked = MatchScenario.Applied(engine.Apply(transformed, attack.Command))
        (attacked.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``toxic Gas should disable other Pokemon Powers until Muk is Confused``() =
        let table mukRoughStates =
            let original = MatchScenario.BattleState "BLK-003" "BLK-089" [ "VIM-BLAZED" ] 1304UL

            let target =
                MatchScenario.PlainCard
                    "transfer-target"
                    "BLK-001"
                    MatchScenario.FirstPlayer
                    CardZone.Booth
                    0

            let muk =
                { original.Card(CardInstanceId "defender") with
                    RoughStates = mukRoughStates }

            MatchScenario.WithCards original [ target; muk ]

        hasPowerAction "BLK-003-T01" (table ImmutableArray<_>.Empty) |> should be False

        hasPowerAction
            "BLK-003-T01"
            (table (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.Muddled 1)))
        |> should be True

    [<Test>]
    member _.``invisible Wall should prevent thirty or more damage but allow twenty``() =
        let blocked =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-122"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED" ]
                1305UL

        let afterBlocked =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(blocked, MatchScenario.AttackCommand blocked "BLK-003-B01")
            )

        (afterBlocked.Card(CardInstanceId "defender")).Damage |> should equal 0

        let allowed =
            MatchScenario.BattleState "BLK-001" "BLK-122" [ "VIM-BLAZED"; "VIM-BLAZED" ] 1306UL

        let afterAllowed =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(allowed, MatchScenario.AttackCommand allowed "BLK-001-B01")
            )

        (afterAllowed.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``kabuto Armor should halve attack damage after Weakness and Resistance``() =
        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-140"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED" ]
                1307UL

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 60

    [<Test>]
    member _.``strikes Back should put one damage counter on the attacking Pokemon``() =
        let state = MatchScenario.BattleState "BLK-004" "BLK-068" [ "VIM-CURRY" ] 1308UL

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-004-B01")
            )

        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 10
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 10

    [<Test>]
    member _.``transform should let Ditto use the Defending Pokemon's attack with Colorless Energy``
        ()
        =
        let state = MatchScenario.BattleState "BLK-132" "BLK-001" [ "VIM-DODGY" ] 1309UL
        let engine = MatchScenario.Engine()

        let copied =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action ->
                match action.Command.Action with
                | MatchAction.Attack(source, effect) ->
                    source = CardInstanceId "attacker" && effect = EffectId "BLK-001-B01"
                | _ -> false)

        let applied = MatchScenario.Applied(engine.Apply(state, copied.Command))
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``prehistoric Power should prevent either player from evolving``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1310UL

        let aerodactyl =
            MatchScenario.PlainCard
                "aerodactyl"
                "BLK-142"
                MatchScenario.SecondPlayer
                CardZone.Booth
                0

        let promotion =
            MatchScenario.PlainCard "promotion" "BLK-002" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.WithCards original [ aerodactyl; promotion ]

        MatchScenario
            .Engine()
            .Apply(
                state,
                MatchScenario.Command
                    state
                    "blocked-evolution"
                    MatchScenario.FirstPlayer
                    ImmutableArray<_>.Empty
                    (MatchAction.Promote(promotion.Id, CardInstanceId "attacker"))
            )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.IneligiblePromotion

    [<Test>]
    member _.``step In should switch a Benched Dragonite with the Active Pokemon``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1311UL

        let dragonite =
            MatchScenario.PlainCard "dragonite" "BLK-149" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ dragonite ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, (powerAction "BLK-149-T01" state).Command)
            )

        (applied.Card dragonite.Id).Zone |> should equal CardZone.Oche
        (applied.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Booth
        applied.ActivePlayer |> should equal MatchScenario.FirstPlayer

    [<Test>]
    member _.``damage Swap should move one counter without knocking out the receiving Pokemon``() =
        let original = MatchScenario.BattleState "BLK-065" "BLK-004" [] 1312UL

        let source =
            { original.Card(CardInstanceId "attacker") with
                Damage = 10 }

        let receiver =
            MatchScenario.PlainCard "receiver" "BLK-001" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ source; receiver ]
        let action = powerAction "BLK-065-T01" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                let selected =
                    if requirement.Id.Value.EndsWith(":from") then
                        source.Id
                    else
                        receiver.Id

                EffectChoice.Cards(requirement.Id, ImmutableArray.Create selected))
            |> ImmutableArray.CreateRange

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices = choices }
                    )
            )

        (applied.Card source.Id).Damage |> should equal 0
        (applied.Card receiver.Id).Damage |> should equal 10
