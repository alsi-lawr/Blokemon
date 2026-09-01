namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private VintageTrainerScenarios =

    let trainerAction card (state: MatchState) =
        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.find (fun action ->
            match action.Command.Action with
            | MatchAction.PlayKit(source, _) -> source = CardInstanceId card
            | _ -> false)

    let cardChoice (requirement: ChoiceRequirement) cards =
        EffectChoice.Cards(requirement.Id, ImmutableArray.CreateRange cards)

type VintageTrainerSemanticsTests() =

    [<Test>]
    member _.``item Finder should pay its two-card cost before choosing a Trainer from the discard pile``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1401UL

        let cards =
            [ MatchScenario.PlainCard
                  "item-finder"
                  "KIT-003"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "discarded-choice"
                  "KIT-007"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "discarded-other"
                  "KIT-013"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

        let state = MatchScenario.WithCards original cards
        let engine = MatchScenario.Engine()
        let action = trainerAction "item-finder" state
        let cost = action.ChoiceRequirements |> Seq.exactlyOne

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices =
                            ImmutableArray.Create(
                                cardChoice
                                    cost
                                    [ CardInstanceId "discarded-choice"
                                      CardInstanceId "discarded-other" ]
                            ) }
                )
            )

        requested.Phase |> should equal MatchPhase.AwaitingEffectChoice
        let recovery = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne
        recovery.EligibleCards |> should contain (CardInstanceId "discarded-choice")

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(
                            cardChoice recovery [ CardInstanceId "discarded-choice" ]
                        ))
                )
            )

        (resolved.Card(CardInstanceId "discarded-choice")).Zone
        |> should equal CardZone.Mitt

        (resolved.Card(CardInstanceId "discarded-other")).Zone
        |> should equal CardZone.EmptiesTray

        (resolved.Card(CardInstanceId "item-finder")).Zone
        |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``pokemon Trader should put the offered Pokemon into the Deck before the search``() =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1402UL

        let trader =
            MatchScenario.PlainCard "trader" "KIT-005" MatchScenario.FirstPlayer CardZone.Mitt -1

        let offered =
            MatchScenario.PlainCard "offered" "BLK-004" MatchScenario.FirstPlayer CardZone.Mitt -1

        let state = MatchScenario.WithCards original [ trader; offered ]
        let engine = MatchScenario.Engine()
        let action = trainerAction "trader" state
        let cost = action.ChoiceRequirements |> Seq.exactlyOne

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices = ImmutableArray.Create(cardChoice cost [ offered.Id ]) }
                )
            )

        let search = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne
        search.EligibleCards |> should contain offered.Id

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(cardChoice search [ offered.Id ]))
                )
            )

        (resolved.Card offered.Id).Zone |> should equal CardZone.Mitt
        (resolved.Card trader.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``energy Retrieval should allow the discarded Basic Energy to be one of the two recovered``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1403UL

        let retrieval =
            MatchScenario.PlainCard "retrieval" "KIT-021" MatchScenario.FirstPlayer CardZone.Mitt -1

        let costEnergy =
            MatchScenario.PlainCard
                "cost-energy"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let oldEnergy =
            MatchScenario.PlainCard
                "old-energy"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards original [ retrieval; costEnergy; oldEnergy ]
        let engine = MatchScenario.Engine()
        let action = trainerAction "retrieval" state
        let cost = action.ChoiceRequirements |> Seq.exactlyOne

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices = ImmutableArray.Create(cardChoice cost [ costEnergy.Id ]) }
                )
            )

        let recovery = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne
        recovery.EligibleCards |> should contain costEnergy.Id

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (ImmutableArray.Create(cardChoice recovery [ costEnergy.Id; oldEnergy.Id ]))
                )
            )

        (resolved.Card costEnergy.Id).Zone |> should equal CardZone.Mitt
        (resolved.Card oldEnergy.Id).Zone |> should equal CardZone.Mitt
        (resolved.Card retrieval.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``plusPower should add damage before Weakness and discard at the end of the turn``() =
        let original = MatchScenario.BattleState "BLK-004" "BLK-002" [ "VIM-CURRY" ] 1404UL

        let plusPower =
            MatchScenario.PlainCard
                "plus-power"
                "KIT-022"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let first = MatchScenario.WithCards original [ plusPower ]
        let engine = MatchScenario.Engine()

        let powered =
            MatchScenario.Applied(engine.Apply(first, (trainerAction "plus-power" first).Command))

        (powered.Card plusPower.Id).Zone |> should equal CardZone.Attached

        let attacked =
            MatchScenario.Applied(
                engine.Apply(powered, MatchScenario.AttackCommand powered "BLK-004-B01")
            )

        (attacked.Card(CardInstanceId "defender")).Damage |> should equal 40
        (attacked.Card plusPower.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``super Potion should discard one Energy and heal the chosen amount up to forty``() =
        let original = MatchScenario.BattleState "BLK-040" "BLK-004" [ "VIM-DODGY" ] 1405UL

        let damaged =
            { original.Card(CardInstanceId "attacker") with
                Damage = 30 }

        let superPotion =
            MatchScenario.PlainCard
                "super-potion"
                "KIT-027"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards original [ damaged; superPotion ]
        let engine = MatchScenario.Engine()
        let action = trainerAction "super-potion" state

        let choices =
            action.ChoiceRequirements
            |> Seq.map (fun requirement ->
                match requirement.Kind with
                | ChoiceRequirementKind.Cards when requirement.Id.Value.EndsWith(":energy") ->
                    cardChoice requirement [ CardInstanceId "vim-0" ]
                | ChoiceRequirementKind.Cards -> cardChoice requirement [ damaged.Id ]
                | ChoiceRequirementKind.Amount -> EffectChoice.Amount(requirement.Id, 3)
                | other -> failwithf "Unexpected Super Potion requirement %A." other)
            |> ImmutableArray.CreateRange

        let healed =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices = choices }
                )
            )

        (healed.Card damaged.Id).Damage |> should equal 0
        (healed.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.EmptiesTray
        (healed.Card superPotion.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``revive should return a Basic Pokemon to the Bench with half its printed HP as damage``
        ()
        =
        let original = MatchScenario.BattleState "BLK-001" "BLK-004" [] 1406UL

        let revive =
            MatchScenario.PlainCard "revive" "KIT-026" MatchScenario.FirstPlayer CardZone.Mitt -1

        let basic =
            MatchScenario.PlainCard
                "revived-basic"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state = MatchScenario.WithCards original [ revive; basic ]
        let engine = MatchScenario.Engine()
        let action = trainerAction "revive" state
        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let revived =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    { action.Command with
                        Choices = ImmutableArray.Create(cardChoice requirement [ basic.Id ]) }
                )
            )

        (revived.Card basic.Id).Zone |> should equal CardZone.Booth
        (revived.Card basic.Id).Damage |> should equal 20
        (revived.Card revive.Id).Zone |> should equal CardZone.EmptiesTray
