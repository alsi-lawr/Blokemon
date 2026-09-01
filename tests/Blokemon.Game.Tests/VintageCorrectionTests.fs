namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

module private VintageCorrectionScenarios =

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

    let attackAction effect (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.find (fun action ->
            match action.Command.Action with
            | MatchAction.Attack(_, attack) -> attack = EffectId effect
            | _ -> false)

    let trainerAction card (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.find (fun action ->
            match action.Command.Action with
            | MatchAction.PlayKit(source, _) -> source = CardInstanceId card
            | _ -> false)

    let cardChoice (requirement: ChoiceRequirement) cards =
        EffectChoice.Cards(requirement.Id, ImmutableArray.CreateRange cards)

    let seedForTosses (expected: bool list) =
        let rec find seed =
            let random = BlokemonSeededRandom seed
            let actual = expected |> List.map (fun _ -> random.NextInt 2 = 1)
            if actual = expected then seed else find (seed + 1UL)

        find 0UL

type VintageCorrectionPowerTests() =

    [<Test>]
    member _.``energy Burn should make every attached Energy provide Fire for the turn``() =
        let state =
            MatchScenario.BattleState
                "BLK-006"
                "BLK-006"
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                1601UL

        let engine = MatchScenario.Engine()

        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            match action.Command.Action with
            | MatchAction.Attack(_, attack) -> attack = EffectId "BLK-006-B01"
            | _ -> false)
        |> should be False

        let burned =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    (VintageCorrectionScenarios.powerAction "BLK-006-T01" state).Command
                )
            )

        let fireSpin = VintageCorrectionScenarios.attackAction "BLK-006-B01" burned
        let discard = fireSpin.ChoiceRequirements |> Seq.exactlyOne

        let attacked =
            MatchScenario.Applied(
                engine.Apply(
                    burned,
                    { fireSpin.Command with
                        Choices =
                            ImmutableArray.Create(
                                VintageCorrectionScenarios.cardChoice
                                    discard
                                    [ CardInstanceId "vim-0"; CardInstanceId "vim-1" ]
                            ) }
                )
            )

        (attacked.Card(CardInstanceId "defender")).Damage |> should equal 100

        [ CardInstanceId "vim-0"; CardInstanceId "vim-1" ]
        |> List.map (fun card -> (attacked.Card card).Zone)
        |> should equal [ CardZone.EmptiesTray; CardZone.EmptiesTray ]

    [<Test>]
    member _.``rain Dance should attach Water Energy from the hand to a Water Pokemon repeatedly``
        ()
        =
        let original = MatchScenario.BattleState "BLK-009" "BLK-004" [] 1602UL

        let waterBench =
            MatchScenario.PlainCard
                "water-bench"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let fireBench =
            MatchScenario.PlainCard
                "fire-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                1

        let firstEnergy =
            MatchScenario.PlainCard
                "rain-energy-1"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let secondEnergy =
            MatchScenario.PlainCard
                "rain-energy-2"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.WithCards original [ waterBench; fireBench; firstEnergy; secondEnergy ]

        let action = VintageCorrectionScenarios.powerAction "BLK-009-T01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        requirement.EligibleTargets |> should contain waterBench.Id
        requirement.EligibleTargets |> should not' (contain fireBench.Id)

        let danced =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.Attachments(
                                        requirement.Id,
                                        ImmutableArray.Create(
                                            { Vim = firstEnergy.Id
                                              Bloke = waterBench.Id }
                                        )
                                    )
                                ) }
                    )
            )

        (danced.Card firstEnergy.Id).AttachedTo
        |> should equal (ValueSome waterBench.Id)

        VintageCorrectionScenarios.hasPowerAction "BLK-009-T01" danced |> should be True

    [<Test>]
    member _.``energy Trans should move one Grass Energy between the player's Pokemon``() =
        let original = MatchScenario.BattleState "BLK-003" "BLK-004" [ "VIM-BLAZED" ] 1603UL

        let receiver =
            MatchScenario.PlainCard "receiver" "BLK-001" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ receiver ]
        let action = VintageCorrectionScenarios.powerAction "BLK-003-T01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let moved =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.Attachments(
                                        requirement.Id,
                                        ImmutableArray.Create(
                                            { Vim = CardInstanceId "vim-0"
                                              Bloke = receiver.Id }
                                        )
                                    )
                                ) }
                    )
            )

        (moved.Card(CardInstanceId "vim-0")).AttachedTo
        |> should equal (ValueSome receiver.Id)

    [<Test>]
    member _.``heal should remove one damage counter only after a heads toss``() =
        let original =
            MatchScenario.BattleState
                "BLK-045"
                "BLK-004"
                []
                (VintageCorrectionScenarios.seedForTosses [ true ])

        let damaged =
            MatchScenario.PlainCard "damaged" "BLK-001" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ { damaged with Damage = 20 } ]

        let engine = MatchScenario.Engine()

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    (VintageCorrectionScenarios.powerAction "BLK-045-T01" state).Command
                )
            )

        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let healed =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(
                            VintageCorrectionScenarios.cardChoice requirement [ damaged.Id ]
                        ))
                )
            )

        (healed.Card damaged.Id).Damage |> should equal 10

        VintageCorrectionScenarios.hasPowerAction "BLK-045-T01" healed
        |> should be False

    [<Test>]
    member _.``strange Behavior should move one damage counter onto Slowbro without knocking it out``
        ()
        =
        let original = MatchScenario.BattleState "BLK-080" "BLK-004" [] 1604UL

        let damaged =
            MatchScenario.PlainCard "damaged" "BLK-001" MatchScenario.FirstPlayer CardZone.Booth 0

        let state = MatchScenario.WithCards original [ { damaged with Damage = 20 } ]
        let action = VintageCorrectionScenarios.powerAction "BLK-080-T01" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                let card =
                    if requirement.Id.Value.EndsWith(":from") then
                        damaged.Id
                    else
                        CardInstanceId "attacker"

                VintageCorrectionScenarios.cardChoice requirement [ card ])
            |> ImmutableArray.CreateRange

        let moved =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices = choices }
                    )
            )

        (moved.Card damaged.Id).Damage |> should equal 10
        (moved.Card(CardInstanceId "attacker")).Damage |> should equal 10

    [<Test>]
    member _.``curse should move one opponent damage counter between their Pokemon``() =
        let original = MatchScenario.BattleState "BLK-094" "BLK-004" [] 1605UL

        let damagedDefender =
            { original.Card(CardInstanceId "defender") with
                Damage = 20 }

        let receiver =
            MatchScenario.PlainCard
                "opponent-receiver"
                "BLK-001"
                MatchScenario.SecondPlayer
                CardZone.Booth
                0

        let state = MatchScenario.WithCards original [ damagedDefender; receiver ]
        let action = VintageCorrectionScenarios.powerAction "BLK-094-T01" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                let card =
                    if requirement.Id.Value.EndsWith(":from") then
                        damagedDefender.Id
                    else
                        receiver.Id

                VintageCorrectionScenarios.cardChoice requirement [ card ])
            |> ImmutableArray.CreateRange

        let moved =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices = choices }
                    )
            )

        (moved.Card damagedDefender.Id).Damage |> should equal 10
        (moved.Card receiver.Id).Damage |> should equal 10

    [<Test>]
    member _.``thick Skinned should prevent a Special Condition from an opponent's attack``() =
        let state = MatchScenario.BattleState "BLK-039" "BLK-143" [ "VIM-DODGY" ] 1606UL

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-039-B01")
            )

        (applied.Card(CardInstanceId "defender")).RoughStates |> should be Empty

    [<Test>]
    member _.``clairvoyance should expose the opponent's hand only while its Power is enabled``() =
        let original = MatchScenario.BattleState "BLK-138" "BLK-004" [] 1607UL

        let hidden =
            MatchScenario.PlainCard "hidden" "VIM-SOBER" MatchScenario.SecondPlayer CardZone.Mitt -1

        let state = MatchScenario.WithCards original [ hidden ]
        let engine = MatchScenario.Engine()

        engine.CanRevealHand(state, MatchScenario.FirstPlayer, MatchScenario.SecondPlayer)
        |> should be True

        engine.CanRevealCard(state, MatchScenario.FirstPlayer, hidden.Id)
        |> should be True

        let disabled =
            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "attacker") with
                      RoughStates =
                          ImmutableArray.Create(
                              MatchScenario.RoughState BlokemonRoughState.Muddled 1
                          ) } ]

        engine.CanRevealHand(disabled, MatchScenario.FirstPlayer, MatchScenario.SecondPlayer)
        |> should be False

    [<Test>]
    member _.``peek should publish the chosen hidden card only to the player using the Power``() =
        let state = MatchScenario.BattleState "BLK-056" "BLK-004" [] 1608UL
        let action = VintageCorrectionScenarios.powerAction "BLK-056-T01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne
        let chosen = CardInstanceId "second-draw"

        let applied, events =
            MatchScenario.AppliedWith(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice requirement [ chosen ]
                                ) }
                    )
            )

        let revealed =
            events
            |> Seq.find (fun event ->
                event.Kind = MatchEventKind.CardsRevealed
                && event.Effect = ValueSome(EffectId "BLK-056-T01"))

        revealed.Actor |> should equal (ValueSome MatchScenario.FirstPlayer)
        revealed.TargetCards |> Seq.toList |> should equal [ chosen ]

        MatchScenario
            .Engine()
            .CanRevealEventCard(applied, revealed, MatchScenario.FirstPlayer, chosen)
        |> should be True

        MatchScenario
            .Engine()
            .CanRevealEventCard(applied, revealed, MatchScenario.SecondPlayer, chosen)
        |> should be False

        MatchScenario.Engine().CanRevealCard(applied, MatchScenario.FirstPlayer, chosen)
        |> should be False

        MatchScenario.Engine().CanRevealCard(applied, MatchScenario.SecondPlayer, chosen)
        |> should be False

type VintageCorrectionAttackTests() =

    [<Test>]
    [<Arguments("BLK-035", "BLK-035-B02")>]
    [<Arguments("BLK-036", "BLK-036-B01")>]
    member _.``metronome should copy a Defending attack without its Energy or discard cost``
        (pokemon: string)
        (metronome: string)
        =
        let state =
            MatchScenario.BattleState pokemon "BLK-006" [ "VIM-DODGY"; "VIM-DODGY" ] 1701UL

        let action = VintageCorrectionScenarios.attackAction metronome state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    EffectChoice.Attack(requirement.Id, EffectId "BLK-006-B01")
                                ) }
                    )
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 100

        [ CardInstanceId "vim-0"; CardInstanceId "vim-1" ]
        |> List.map (fun energy -> (applied.Card energy).Zone)
        |> should equal [ CardZone.Attached; CardZone.Attached ]

    [<Test>]
    member _.``mirror Move should repeat the last attack damage and Special Conditions received``
        ()
        =
        let original =
            MatchScenario.BattleState "BLK-017" "BLK-044" [ "VIM-DODGY"; "VIM-DODGY" ] 1702UL

        let defendingEnergy =
            MatchScenario.AttachedCard
                "defending-energy"
                "VIM-BLAZED"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { original.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create defendingEnergy.Id }

        let state =
            { MatchScenario.WithCards original [ defender; defendingEnergy ] with
                ActivePlayer = MatchScenario.SecondPlayer
                RoundUsage = RoundUsage.Empty MatchScenario.SecondPlayer }

        let opponentAttack =
            MatchScenario.Command
                state
                "opponent-poison"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(defender.Id, EffectId "BLK-044-B01"))

        let remembered =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, opponentAttack))

        (remembered.Card(CardInstanceId "attacker")).RoughStates
        |> Seq.map _.State
        |> should contain BlokemonRoughState.DodgyPint

        let mirrored =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(remembered, MatchScenario.AttackCommand remembered "BLK-017-B02")
            )

        (mirrored.Card defender.Id).RoughStates
        |> Seq.map _.State
        |> should contain BlokemonRoughState.DodgyPint

    [<Test>]
    member _.``destiny Bond should Knock Out the attacker that Knocks Out Gastly next turn``() =
        let original =
            MatchScenario.BattleState "BLK-092" "BLK-003" [ "VIM-GEEKED"; "VIM-DODGY" ] 1703UL

        let opponentEnergy =
            [ for index in 0..3 ->
                  MatchScenario.AttachedCard
                      $"opponent-energy-{index}"
                      "VIM-BLAZED"
                      MatchScenario.SecondPlayer
                      CardZone.Attached
                      -1
                      (CardInstanceId "defender") ]

        let defender =
            { original.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.CreateRange(opponentEnergy |> Seq.map _.Id) }

        let firstBench =
            MatchScenario.PlainCard
                "first-bench"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let secondBench =
            MatchScenario.PlainCard
                "second-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                0

        let state =
            MatchScenario.WithCards
                original
                (seq {
                    yield defender
                    yield firstBench
                    yield secondBench
                    yield! opponentEnergy
                })

        let bond = VintageCorrectionScenarios.attackAction "BLK-092-B02" state
        let discard = bond.ChoiceRequirements |> Seq.exactlyOne

        let bonded =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { bond.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        discard
                                        [ CardInstanceId "vim-0" ]
                                ) }
                    )
            )

        let knockout =
            MatchScenario.Command
                bonded
                "knock-out-gastly"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(defender.Id, EffectId "BLK-003-B01"))

        let resolved = MatchScenario.Applied(MatchScenario.Engine().Apply(bonded, knockout))

        (resolved.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (resolved.Card defender.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``prophecy should put the chosen top three cards into the submitted order``() =
        let original = MatchScenario.BattleState "BLK-097" "BLK-004" [ "VIM-GEEKED" ] 1704UL

        let second =
            MatchScenario.PlainCard
                "own-deck-2"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Stack
                1

        let third =
            MatchScenario.PlainCard
                "own-deck-3"
                "VIM-CURRY"
                MatchScenario.FirstPlayer
                CardZone.Stack
                2

        let state = MatchScenario.WithCards original [ second; third ]
        let action = VintageCorrectionScenarios.attackAction "BLK-097-B01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let reordered =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        requirement
                                        [ third.Id; second.Id; CardInstanceId "first-draw" ]
                                ) }
                    )
            )

        reordered.CardsIn(MatchScenario.FirstPlayer, CardZone.Stack)
        |> Seq.map _.Id
        |> Seq.take 3
        |> Seq.toList
        |> should equal [ third.Id; second.Id; CardInstanceId "first-draw" ]

    [<Test>]
    member _.``big Eggsplosion should flip once per attached Energy and deal twenty per heads``() =
        let state =
            MatchScenario.BattleState
                "BLK-103"
                "BLK-003"
                [ "VIM-DODGY"; "VIM-DODGY"; "VIM-DODGY" ]
                (VintageCorrectionScenarios.seedForTosses [ true; false; true ])

        let applied, events =
            MatchScenario.AppliedWith(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-103-B02")
            )

        events
        |> Seq.filter (fun event -> event.Kind = MatchEventKind.BeerMatTossed)
        |> Seq.length
        |> should equal 3

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 40

    [<Test>]
    member _.``stone Barrage should keep flipping until tails and deal ten per heads``() =
        let state =
            MatchScenario.BattleState
                "BLK-074"
                "BLK-003"
                [ "VIM-LAIRY"; "VIM-DODGY" ]
                (VintageCorrectionScenarios.seedForTosses [ true; true; false ])

        let applied, events =
            MatchScenario.AppliedWith(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-074-B01")
            )

        events
        |> Seq.filter (fun event -> event.Kind = MatchEventKind.BeerMatTossed)
        |> Seq.length
        |> should equal 3

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``super Fang should deal half the Defending Pokemon's remaining HP rounded up``() =
        let original =
            MatchScenario.BattleState "BLK-020" "BLK-003" [ "VIM-DODGY"; "VIM-DODGY" ] 1705UL

        let state =
            MatchScenario.WithCards
                original
                [ { original.Card(CardInstanceId "defender") with
                      Damage = 30 } ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-020-B02")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 70

    [<Test>]
    member _.``conversion 1 should change subsequent Weakness damage and do nothing without one``
        ()
        =
        let noWeakness =
            MatchScenario.BattleState "BLK-137" "BLK-094" [ "VIM-DODGY" ] 1706UL

        let noWeaknessAction =
            VintageCorrectionScenarios.attackAction "BLK-137-B01" noWeakness

        noWeaknessAction.ChoiceRequirements |> should be Empty

        let original = MatchScenario.BattleState "BLK-137" "BLK-004" [ "VIM-DODGY" ] 1707UL

        let attackingEnergy =
            MatchScenario.AttachedCard
                "conversion-one-energy"
                "VIM-CURRY"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "conversion-one-attacker")

        let attackingPokemon =
            { MatchScenario.PlainCard
                  "conversion-one-attacker"
                  "BLK-004"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  0 with
                Attachments = ImmutableArray.Create attackingEnergy.Id }

        let switch =
            MatchScenario.PlainCard
                "conversion-one-switch"
                "KIT-004"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.WithCards original [ attackingPokemon; attackingEnergy; switch ]

        let fireAttack state =
            MatchScenario.Command
                state
                "conversion-one-fire-attack"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(attackingPokemon.Id, EffectId "BLK-004-B01"))

        let switchToAttacker state =
            let action = VintageCorrectionScenarios.trainerAction "conversion-one-switch" state
            let requirement = action.ChoiceRequirements |> Seq.exactlyOne

            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        requirement
                                        [ attackingPokemon.Id ]
                                ) }
                    )
            )

        let unconverted =
            switchToAttacker state
            |> fun switched ->
                MatchScenario.Applied(MatchScenario.Engine().Apply(switched, fireAttack switched))

        (unconverted.Card(CardInstanceId "defender")).Damage |> should equal 10

        let action = VintageCorrectionScenarios.attackAction "BLK-137-B01" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let changed =
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

        let opponentEnded =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        changed,
                        MatchScenario.Command
                            changed
                            "end-opponent-round-after-conversion-one"
                            MatchScenario.SecondPlayer
                            ImmutableArray<_>.Empty
                            MatchAction.EndRound
                    )
            )

        let attacked =
            switchToAttacker opponentEnded
            |> fun switched ->
                MatchScenario.Applied(MatchScenario.Engine().Apply(switched, fireAttack switched))

        (attacked.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``conversion 2 should change subsequent Resistance damage to the chosen type``() =
        let original = MatchScenario.BattleState "BLK-137" "BLK-004" [ "VIM-DODGY" ] 1708UL

        let opponentEnergy =
            MatchScenario.AttachedCard
                "conversion-two-energy"
                "VIM-CURRY"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let opponent =
            { original.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create opponentEnergy.Id }

        let state = MatchScenario.WithCards original [ opponent; opponentEnergy ]

        let unconvertedState =
            { state with
                ActivePlayer = MatchScenario.SecondPlayer
                RoundUsage = RoundUsage.Empty MatchScenario.SecondPlayer }

        let opponentAttack state =
            MatchScenario.Command
                state
                "conversion-two-opponent-attack"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(opponent.Id, EffectId "BLK-004-B01"))

        let unconverted =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(unconvertedState, opponentAttack unconvertedState)
            )

        (unconverted.Card(CardInstanceId "attacker")).Damage |> should equal 10

        let action = VintageCorrectionScenarios.attackAction "BLK-137-B02" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let changed =
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
            MatchScenario.Applied(MatchScenario.Engine().Apply(changed, opponentAttack changed))

        (attacked.Card(CardInstanceId "attacker")).Damage |> should equal 0

    [<Test>]
    member _.``leech Seed should heal ten only after its attack deals damage``() =
        let original =
            MatchScenario.BattleState "BLK-001" "BLK-004" [ "VIM-BLAZED"; "VIM-BLAZED" ] 1709UL

        let state =
            MatchScenario.WithCards
                original
                [ { original.Card(CardInstanceId "attacker") with
                      Damage = 20 } ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20
        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 10

    [<Test>]
    [<Arguments("BLK-075", "BLK-075-B01", "BLK-004", "BLK-004-B01", "VIM-CURRY", 1, 0)>]
    [<Arguments("BLK-095", "BLK-095-B02", "BLK-003", "BLK-003-B01", "VIM-BLAZED", 4, 120)>]
    member _.``damage prevention up to thirty should block smaller damage but not larger damage``
        (protector: string)
        (protectionAttack: string)
        (opponent: string)
        (opponentAttack: string)
        (opponentEnergy: string)
        (opponentEnergyCount: int)
        (expectedDamage: int)
        =
        let original =
            MatchScenario.BattleState protector opponent [ "VIM-LAIRY"; "VIM-LAIRY" ] 1710UL

        let opponentEnergy =
            [ for index in 0 .. opponentEnergyCount - 1 ->
                  MatchScenario.AttachedCard
                      $"protection-energy-{index}"
                      opponentEnergy
                      MatchScenario.SecondPlayer
                      CardZone.Attached
                      -1
                      (CardInstanceId "defender") ]

        let defender =
            { original.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.CreateRange(opponentEnergy |> Seq.map _.Id) }

        let state =
            MatchScenario.WithCards original (Seq.append [ defender ] opponentEnergy)

        let protectedState =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(state, MatchScenario.AttackCommand state protectionAttack)
            )

        let response =
            MatchScenario.Command
                protectedState
                $"response:{opponentAttack}"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(defender.Id, EffectId opponentAttack))

        let applied =
            MatchScenario.Applied(MatchScenario.Engine().Apply(protectedState, response))

        (applied.Card(CardInstanceId "attacker")).Damage |> should equal expectedDamage

    [<Test>]
    member _.``harden should reduce only the next attack from the Pokemon it defended against``() =
        let original =
            MatchScenario.BattleState "BLK-053" "BLK-004" [ "VIM-DODGY"; "VIM-DODGY" ] 1711UL

        let defendingEnergy =
            MatchScenario.AttachedCard
                "harden-energy"
                "VIM-CURRY"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { original.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create defendingEnergy.Id }

        let state = MatchScenario.WithCards original [ defender; defendingEnergy ]

        let hardened =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-053-B02")
            )

        let response =
            MatchScenario.Command
                hardened
                "scratch-hardened"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Attack(defender.Id, EffectId "BLK-004-B01"))

        let applied =
            MatchScenario.Applied(MatchScenario.Engine().Apply(hardened, response))

        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 0

type VintageCorrectionRuleTests() =

    [<Test>]
    [<Arguments("KIT-001", "BLK-039", "BLK-039-B01", "VIM-DODGY")>]
    [<Arguments("KIT-001", "BLK-044", "BLK-044-B01", "VIM-BLAZED")>]
    [<Arguments("KIT-001", "BLK-044", "BLK-044-B02", "VIM-BLAZED,VIM-BLAZED")>]
    [<Arguments("KIT-001", "BLK-080", "BLK-080-B01", "VIM-GEEKED,VIM-GEEKED")>]
    [<Arguments("KIT-002", "BLK-039", "BLK-039-B01", "VIM-DODGY")>]
    [<Arguments("KIT-002", "BLK-044", "BLK-044-B01", "VIM-BLAZED")>]
    [<Arguments("KIT-002", "BLK-044", "BLK-044-B02", "VIM-BLAZED,VIM-BLAZED")>]
    [<Arguments("KIT-002", "BLK-080", "BLK-080-B01", "VIM-GEEKED,VIM-GEEKED")>]
    member _.``a Doll or Fossil should be immune to every Special Condition``
        (kit: string)
        (attacker: string)
        (attack: string)
        (energy: string)
        =
        let state =
            MatchScenario.BattleState
                attacker
                kit
                (energy.Split ',')
                (VintageCorrectionScenarios.seedForTosses [ true ])

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state attack)
            )

        (applied.Card(CardInstanceId "defender")).RoughStates |> should be Empty

type VintageCorrectionTrainerTests() =

    [<Test>]
    member _.``scoop Up should return only the Basic and discard every higher Stage and attachment``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1801UL

        let basic =
            MatchScenario.AttachedCard
                "scoop-basic"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "scoop-stage-two")

        let stageOne =
            MatchScenario.AttachedCard
                "scoop-stage-one"
                "BLK-002"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "scoop-stage-two")

        let energy =
            MatchScenario.AttachedCard
                "scoop-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "scoop-stage-two")

        let stageTwo =
            { MatchScenario.PlainCard
                  "scoop-stage-two"
                  "BLK-003"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  0 with
                UnderlyingCards = ImmutableArray.Create(basic.Id, stageOne.Id)
                Attachments = ImmutableArray.Create energy.Id }

        let scoop =
            MatchScenario.PlainCard "scoop-up" "KIT-019" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state =
            MatchScenario.WithCards original [ basic; stageOne; stageTwo; energy; scoop ]

        let action = VintageCorrectionScenarios.trainerAction "scoop-up" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        requirement
                                        [ stageTwo.Id ]
                                ) }
                    )
            )

        (applied.Card basic.Id).Zone |> should equal CardZone.Mitt

        [ stageOne.Id; stageTwo.Id; energy.Id ]
        |> List.map (fun card -> (applied.Card card).Zone)
        |> should equal [ CardZone.EmptiesTray; CardZone.EmptiesTray; CardZone.EmptiesTray ]

    [<Test>]
    member _.``devolution Spray should discard the chosen Stage and every Stage above it``() =
        let original = MatchScenario.BattleState "BLK-003" "BLK-004" [] 1802UL

        let basic =
            MatchScenario.AttachedCard
                "devolve-basic"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let stageOne =
            MatchScenario.AttachedCard
                "devolve-stage-one"
                "BLK-002"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let basicBench =
            MatchScenario.PlainCard
                "devolve-basic-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                0

        let energy =
            MatchScenario.AttachedCard
                "devolve-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let stageTwo =
            { original.Card(CardInstanceId "attacker") with
                Damage = 20
                UnderlyingCards = ImmutableArray.Create(basic.Id, stageOne.Id)
                Attachments = ImmutableArray.Create energy.Id }

        let spray =
            MatchScenario.PlainCard
                "devolution-spray"
                "KIT-016"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.WithCards
                original
                [ basic; basicBench; stageOne; stageTwo; energy; spray ]

        let action = VintageCorrectionScenarios.trainerAction "devolution-spray" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        requirement.EligibleCards |> should contain stageOne.Id
        requirement.EligibleCards |> should contain stageTwo.Id
        requirement.EligibleCards |> should not' (contain basic.Id)
        requirement.EligibleCards |> should not' (contain basicBench.Id)

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        requirement
                                        [ stageOne.Id ]
                                ) }
                    )
            )

        (applied.Card basic.Id).Zone |> should equal CardZone.Oche
        (applied.Card basic.Id).Damage |> should equal 20
        (applied.Card basic.Id).Attachments |> Seq.toList |> should equal [ energy.Id ]
        (applied.Card energy.Id).AttachedTo |> should equal (ValueSome basic.Id)
        (applied.Card stageOne.Id).Zone |> should equal CardZone.EmptiesTray
        (applied.Card stageTwo.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    [<Arguments("BLK-004", 30)>]
    [<Arguments("BLK-107", 40)>]
    member _.``revive should leave odd-half maximum HP rounded up to the nearest ten``
        (pokemon: string)
        (expectedRemainingHp: int)
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1803UL

        let revive =
            MatchScenario.PlainCard
                "rounding-revive"
                "KIT-026"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let basic =
            MatchScenario.PlainCard
                "rounding-basic"
                pokemon
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards original [ revive; basic ]
        let action = VintageCorrectionScenarios.trainerAction "rounding-revive" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice requirement [ basic.Id ]
                                ) }
                    )
            )

        let maximumHp =
            MatchScenario.Authority.Collectibles
            |> Seq.find (fun card -> card.Id = pokemon)
            |> _.StayingPower

        maximumHp - (applied.Card basic.Id).Damage |> should equal expectedRemainingHp

    [<Test>]
    member _.``pokemon Breeder should perform ordinary evolution cleanup and effect retargeting``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1804UL

        let energy =
            MatchScenario.AttachedCard
                "breeder-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let defender =
            MatchScenario.AttachedCard
                "breeder-defender"
                "KIT-014"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let basic =
            { original.Card(CardInstanceId "attacker") with
                Damage = 20
                Attachments = ImmutableArray.Create(energy.Id, defender.Id)
                RoughStates =
                    ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.NoddedOff 1) }

        let evolution =
            MatchScenario.PlainCard
                "breeder-evolution"
                "BLK-003"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let breeder =
            MatchScenario.PlainCard
                "pokemon-breeder"
                "KIT-018"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let attackEffect =
            { SourceEffect = EffectId "old-attack-effect"
              SourceCard = basic.Id
              Owner = MatchScenario.FirstPlayer
              TargetCard = ValueSome basic.Id
              Kind = TemporaryEffectKind.RestrictAttack
              Amount = 0
              MechanicalTypes = ImmutableArray<_>.Empty
              RoughStates = ImmutableArray<_>.Empty
              RelatedCards = ImmutableArray<_>.Empty
              Conditions = ImmutableArray<_>.Empty
              Duration = EffectDuration.UntilEndOfOpponentsNextRound
              AppliesFromRound = 4
              ExpiresAfterRound = 5 }

        let trainerEffect =
            { attackEffect with
                SourceEffect = EffectId "KIT-014-R01"
                SourceCard = defender.Id
                Kind = TemporaryEffectKind.ReduceDamage }

        let state =
            { MatchScenario.WithCards original [ basic; energy; defender; evolution; breeder ] with
                Effects = ImmutableArray.Create(attackEffect, trainerEffect) }

        let action = VintageCorrectionScenarios.trainerAction "pokemon-breeder" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                VintageCorrectionScenarios.cardChoice
                    requirement
                    [ if requirement.Id.Value.EndsWith(":evolution") then
                          evolution.Id
                      else
                          basic.Id ])
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

        (applied.Card evolution.Id).Zone |> should equal CardZone.Oche
        (applied.Card evolution.Id).Damage |> should equal 20
        (applied.Card evolution.Id).RoughStates |> should be Empty

        applied.Effects
        |> Seq.exists (fun effect -> effect.SourceEffect = attackEffect.SourceEffect)
        |> should be False

        applied.Effects
        |> Seq.exists (fun effect ->
            effect.SourceEffect = trainerEffect.SourceEffect
            && effect.TargetCard = ValueSome evolution.Id)
        |> should be True

    [<Test>]
    member _.``maintenance should shuffle both returned cards before drawing one``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1805UL

        let maintenance =
            MatchScenario.PlainCard
                "maintenance"
                "KIT-006"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let returned =
            [ MatchScenario.PlainCard
                  "maintenance-return-1"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "maintenance-return-2"
                  "VIM-CURRY"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

        let state = MatchScenario.WithCards original (Seq.append [ maintenance ] returned)
        let action = VintageCorrectionScenarios.trainerAction "maintenance" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice
                                        requirement
                                        (returned |> List.map _.Id)
                                ) }
                    )
            )

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Stack)
        |> Seq.forall (fun card -> card.StackPosition >= 0)
        |> should be True

    [<Test>]
    member _.``lass should shuffle the opponent's returned Trainer cards into their Deck``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1806UL

        let lass =
            MatchScenario.PlainCard "lass" "KIT-011" MatchScenario.FirstPlayer CardZone.Mitt -1

        let opponentTrainer =
            MatchScenario.PlainCard
                "opponent-trainer"
                "KIT-012"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards original [ lass; opponentTrainer ]

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(state, (VintageCorrectionScenarios.trainerAction "lass" state).Command)
            )

        (applied.Card opponentTrainer.Id).Zone |> should equal CardZone.Stack

        (applied.Card opponentTrainer.Id).StackPosition
        |> should be (greaterThanOrEqualTo 0)

    [<Test>]
    member _.``impostor Professor Oak should shuffle the whole opponent hand before drawing seven``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1807UL

        let oak =
            MatchScenario.PlainCard
                "impostor-oak"
                "KIT-017"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let opponentHand =
            [ for index in 0..9 ->
                  MatchScenario.PlainCard
                      $"opponent-hand-{index}"
                      "VIM-SOBER"
                      MatchScenario.SecondPlayer
                      CardZone.Mitt
                      -1 ]

        let opponentDeck =
            [ for index in 0..9 ->
                  MatchScenario.PlainCard
                      $"opponent-deck-{index}"
                      "VIM-CURRY"
                      MatchScenario.SecondPlayer
                      CardZone.Stack
                      (index + 1) ]

        let state =
            MatchScenario.WithCards
                original
                (seq {
                    yield oak
                    yield! opponentHand
                    yield! opponentDeck
                })

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        (VintageCorrectionScenarios.trainerAction "impostor-oak" state).Command
                    )
            )

        applied.CardsIn(MatchScenario.SecondPlayer, CardZone.Stack)
        |> Seq.forall (fun card -> card.StackPosition >= 0)
        |> should be True

    [<Test>]
    member _.``mr Fuji should shuffle the returned Benched pile into the Deck``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1808UL

        let energy =
            MatchScenario.AttachedCard
                "fuji-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "fuji-target")

        let target =
            { MatchScenario.PlainCard
                  "fuji-target"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  0 with
                Attachments = ImmutableArray.Create energy.Id }

        let fuji =
            MatchScenario.PlainCard "mr-fuji" "KIT-030" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.WithCards original [ target; energy; fuji ]
        let action = VintageCorrectionScenarios.trainerAction "mr-fuji" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        { action.Command with
                            Choices =
                                ImmutableArray.Create(
                                    VintageCorrectionScenarios.cardChoice requirement [ target.Id ]
                                ) }
                    )
            )

        [ target.Id; energy.Id ]
        |> List.map (fun card -> applied.Card card)
        |> List.iter (fun card ->
            card.Zone |> should equal CardZone.Stack
            card.StackPosition |> should be (greaterThanOrEqualTo 0))

    [<Test>]
    member _.``gambler should shuffle the returned hand before its coin-dependent draw``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1809UL

        let gambler =
            MatchScenario.PlainCard "gambler" "KIT-032" MatchScenario.FirstPlayer CardZone.Mitt -1

        let returned =
            [ for index in 0..9 ->
                  MatchScenario.PlainCard
                      $"gambler-return-{index}"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.Mitt
                      -1 ]

        let extraDeck =
            [ for index in 0..9 ->
                  MatchScenario.PlainCard
                      $"gambler-deck-{index}"
                      "VIM-CURRY"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      (index + 1) ]

        let state =
            MatchScenario.WithCards
                original
                (seq {
                    yield gambler
                    yield! returned
                    yield! extraDeck
                })

        let applied =
            MatchScenario.Applied(
                MatchScenario
                    .Engine()
                    .Apply(
                        state,
                        (VintageCorrectionScenarios.trainerAction "gambler" state).Command
                    )
            )

        applied.CardsIn(MatchScenario.FirstPlayer, CardZone.Stack)
        |> Seq.forall (fun card -> card.StackPosition >= 0)
        |> should be True
