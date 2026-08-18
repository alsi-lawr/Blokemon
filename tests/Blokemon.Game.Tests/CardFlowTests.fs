namespace Blokemon.Game.Tests

open Blokemon.Game
open FsUnit
open TUnit.Core

type CardFlowTests() =

    [<Test>]
    member _.``a stack search should offer only regular blokes and move the chosen card to the booth``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-016" "BLK-003" [ "VIM-SOBER" ] 307UL

        let basic =
            MatchScenario.PlainCard
                "search-basic"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let evolved =
            MatchScenario.PlainCard
                "search-evolved"
                "BLK-003"
                MatchScenario.FirstPlayer
                CardZone.Stack
                1

        let state =
            { state with
                Cards =
                    FrozenList<CardState>
                        .Create(
                            state.Cards
                            |> Seq.filter (fun card -> card.Id.Value <> "first-draw")
                            |> fun cards -> Seq.append cards [ basic; evolved ]
                            |> Seq.sortBy (fun card -> card.Id)
                        ) }

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-016-B01")
            )

        let optional =
            missing.ChoiceRequirements
            |> Seq.find (fun value -> value.Kind = ChoiceRequirementKind.Optional)

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-016-B01"
                        (FrozenList<EffectChoice>.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let requirement =
            requested.PendingEffect.Value.Requirements
            |> Seq.find (fun value -> value.Kind = ChoiceRequirementKind.Cards)

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (FrozenList<EffectChoice>
                    .Create(
                        EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create basic.Id
                        )
                    ))

        let applied = MatchScenario.Applied(engine.Apply(requested, command))

        missing.ChoiceRequirements.Count |> should equal 1
        requirement.EligibleCards |> Seq.toList |> should equal [ basic.Id ]
        (applied.Card basic.Id).Zone |> should equal CardZone.Booth
        (applied.Card evolved.Id).Zone |> should equal CardZone.Stack

    [<Test>]
    member _.``a mate should move and attach using the owners, sources and destinations it names``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-003" [] 311UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-010" MatchScenario.FirstPlayer CardZone.Mitt -1

        let ownVim =
            MatchScenario.PlainCard "own-vim" "VIM-SOBER" MatchScenario.FirstPlayer CardZone.Mitt -1

        let otherVim =
            MatchScenario.AttachedCard
                "other-vim"
                "VIM-BLAZED"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create otherVim.Id }

        let state = MatchScenario.WithCards state [ defender; kit; ownVim; otherVim ]

        let actions = engine.GetLegalActions(state, MatchScenario.FirstPlayer)

        let isThisKit (action: LegalAction) =
            action.Kind = LegalActionKind.PlayKit
            && (match action.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = kit.Id
                | _ -> false)

        actions |> Seq.exists isThisKit |> should be True
        let action = actions |> Seq.filter isThisKit |> Seq.exactlyOne

        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let applied =
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

        (applied.Card otherVim.Id).Zone |> should equal CardZone.Mitt
        (applied.Card otherVim.Id).Owner |> should equal MatchScenario.SecondPlayer

        (applied.Card ownVim.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "attacker"))

        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray
