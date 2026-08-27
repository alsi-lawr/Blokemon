namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private ExceptionalEffectFixtures =

    let addCardsAndAttachments (state: MatchState) (target: string) (attachments: CardState list) =
        let host = state.Card(CardInstanceId target)

        let host =
            { host with
                Attachments =
                    ImmutableArray.CreateRange(
                        Seq.append host.Attachments (attachments |> Seq.map (fun card -> card.Id))
                    ) }

        MatchScenario.WithCards state (host :: attachments)

    let partyTrick (state: MatchState) (effect: EffectId) choices =
        MatchScenario.Command
            state
            $"command:{effect.Value}"
            MatchScenario.FirstPlayer
            choices
            (MatchAction.UsePartyTrick(CardInstanceId "attacker", effect))

    let requirementOfKind kind (requirements: ImmutableArray<ChoiceRequirement>) =
        requirements |> Seq.filter (fun value -> value.Kind = kind) |> Seq.exactlyOne

    let isKitAction (kit: CardInstanceId) (action: LegalAction) =
        action.Kind = LegalActionKind.PlayKit
        && (match action.Command.Action with
            | MatchAction.PlayKit(played, _) -> played = kit
            | _ -> false)

    let kitAction (engine: MatchEngine) (state: MatchState) (kit: CardInstanceId) =
        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.filter (isKitAction kit)
        |> Seq.exactlyOne

    let boothBlokes count =
        [ for index in 0 .. count - 1 ->
              MatchScenario.PlainCard
                  $"other-booth-{index}"
                  "BLK-004"
                  MatchScenario.SecondPlayer
                  CardZone.Booth
                  index ]

type ExceptionalEffectTests() =

    [<Test>]
    member _.``a switch a card effect forces should say which cards traded places``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-106" "BLK-001" [ "VIM-LAIRY" ] 397UL

        let ownBench =
            MatchScenario.PlainCard
                "own-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ ownBench ]

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-106-B01")
            )

        let choice =
            requirementOfKind ChoiceRequirementKind.Cards missing.ChoiceRequirements

        let _, events =
            MatchScenario.AppliedWith(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-106-B01"
                        (ImmutableArray.Create(
                            EffectChoice.Cards(choice.Id, ImmutableArray.Create ownBench.Id)
                        ))
                )
            )

        let swap =
            events
            |> Seq.filter (fun value -> value.Kind = MatchEventKind.OcheSwapped)
            |> Seq.exactlyOne

        swap.TargetCards
        |> Seq.toList
        |> should equal [ ownBench.Id; CardInstanceId "attacker" ]

    [<Test>]
    member _.``a spread attack should damage every opponent and switch with the chosen own booth``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-106" "BLK-001" [ "VIM-LAIRY" ] 397UL

        let ownBench =
            MatchScenario.PlainCard
                "own-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let otherBench =
            MatchScenario.PlainCard
                "other-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ ownBench; otherBench ]

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-106-B01")
            )

        let choice =
            requirementOfKind ChoiceRequirementKind.Cards missing.ChoiceRequirements

        let command =
            MatchScenario.AttackCommandWith
                state
                "BLK-106-B01"
                (ImmutableArray.Create(
                    EffectChoice.Cards(choice.Id, ImmutableArray.Create ownBench.Id)
                ))

        let applied = MatchScenario.Applied(engine.Apply(state, command))

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 10
        (applied.Card otherBench.Id).Damage |> should equal 10
        (applied.Card ownBench.Id).Zone |> should equal CardZone.Oche
        (applied.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Booth

    [<Test>]
    member _.``chucking bar kit should use the chosen attached kit before it scales the attack damage``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-101" "BLK-150" [ "VIM-BEER" ] 401UL

        let firstTool =
            MatchScenario.AttachedCard
                "tool-1"
                "KIT-012"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let secondTool =
            MatchScenario.AttachedCard
                "tool-2"
                "KIT-013"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let state = addCardsAndAttachments state "attacker" [ firstTool; secondTool ]

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-101-B01")
            )

        let optional =
            requirementOfKind ChoiceRequirementKind.Optional missing.ChoiceRequirements

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    MatchScenario.AttackCommandWith
                        state
                        "BLK-101-B01"
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let cards =
            requirementOfKind ChoiceRequirementKind.Cards requested.PendingEffect.Value.Requirements

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(cards.Id, ImmutableArray.Create(firstTool.Id, secondTool.Id))
                ))

        let applied = MatchScenario.Applied(engine.Apply(requested, command))

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 100
        (applied.Card firstTool.Id).Zone |> should equal CardZone.EmptiesTray
        (applied.Card secondTool.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``an attached-vim chuck should choose and discard the opposing oche's vim``() =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState
                "BLK-130"
                "BLK-150"
                [ "VIM-SOBER"; "VIM-BLAZED"; "VIM-CURRY"; "VIM-LAIRY" ]
                409UL

        let opposingVim =
            MatchScenario.AttachedCard
                "opposing-vim"
                "VIM-BEER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let state = addCardsAndAttachments state "defender" [ opposingVim ]

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-130-B01")
            )

        let cards = requirementOfKind ChoiceRequirementKind.Cards missing.ChoiceRequirements

        let command =
            MatchScenario.AttackCommandWith
                state
                "BLK-130-B01"
                (ImmutableArray.Create(
                    EffectChoice.Cards(cards.Id, ImmutableArray.Create opposingVim.Id)
                ))

        let applied = MatchScenario.Applied(engine.Apply(state, command))

        cards.EligibleCards |> Seq.toList |> should equal [ opposingVim.Id ]
        (applied.Card opposingVim.Id).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``a voluntary self-chuck should damage the chosen opponent without awarding bar chits``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-121" "BLK-150" [] 419UL

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state = MatchScenario.WithCards state [ replacement ]
        let effect = EffectId "BLK-121-T01"

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, partyTrick state effect ImmutableArray<_>.Empty)
            )

        let optional =
            requirementOfKind ChoiceRequirementKind.Optional missing.ChoiceRequirements

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    partyTrick
                        state
                        effect
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let target =
            requirementOfKind ChoiceRequirementKind.Cards requested.PendingEffect.Value.Requirements

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(target.Id, ImmutableArray.Create(CardInstanceId "defender"))
                ))

        let applied = MatchScenario.Applied(engine.Apply(requested, command))

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

        (applied.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (applied.Player MatchScenario.SecondPlayer).BarChitsRemaining |> should equal 6

        applied.Phase |> should equal MatchPhase.AwaitingReplacement

    [<Test>]
    member _.``a voluntary self-chuck with no remaining bloke should win once for the opponent``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-121" "BLK-150" [] 421UL
        let effect = EffectId "BLK-121-T01"

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, partyTrick state effect ImmutableArray<_>.Empty)
            )

        let optional =
            requirementOfKind ChoiceRequirementKind.Optional missing.ChoiceRequirements

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    partyTrick
                        state
                        effect
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let target =
            requirementOfKind ChoiceRequirementKind.Cards requested.PendingEffect.Value.Requirements

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(target.Id, ImmutableArray.Create(CardInstanceId "defender"))
                ))

        let applied, events = MatchScenario.AppliedWith(engine.Apply(requested, command))

        applied.Winner |> should equal (ValueSome MatchScenario.SecondPlayer)
        applied.Phase |> should equal MatchPhase.Complete
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

        (applied.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        events
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.MatchWon)
        |> Seq.length
        |> should equal 1

    [<Test>]
    member _.``a first-round transform should replace its source and discard what was attached to it``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-132" "BLK-150" [ "VIM-SOBER" ] 421UL

        let replacement =
            MatchScenario.PlainCard
                "stack-basic"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Stack
                0

        let state =
            { state with
                Players =
                    ImmutableArray.CreateRange(
                        state.Players
                        |> Seq.map (fun player ->
                            if player.Id = MatchScenario.FirstPlayer then
                                { player with RoundsStarted = 1 }
                            else
                                player)
                    )
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card -> card.Id.Value <> "first-draw")
                        |> fun cards -> Seq.append cards [ replacement ]
                        |> Seq.sortBy (fun card -> card.Id)
                    ) }

        let effect = EffectId "BLK-132-T01"

        let _, missing =
            MatchScenario.Rejected(
                engine.Apply(state, partyTrick state effect ImmutableArray<_>.Empty)
            )

        let optional =
            requirementOfKind ChoiceRequirementKind.Optional missing.ChoiceRequirements

        let requested =
            MatchScenario.Applied(
                engine.Apply(
                    state,
                    partyTrick
                        state
                        effect
                        (ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)))
                )
            )

        let searched =
            requirementOfKind ChoiceRequirementKind.Cards requested.PendingEffect.Value.Requirements

        let command =
            MatchScenario.ResolveEffectChoiceCommand
                requested
                (ImmutableArray.Create(
                    EffectChoice.Cards(searched.Id, ImmutableArray.Create replacement.Id)
                ))

        let applied = MatchScenario.Applied(engine.Apply(requested, command))

        (applied.Card replacement.Id).Zone |> should equal CardZone.Oche

        (applied.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        (applied.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.EmptiesTray

        (applied.Player MatchScenario.SecondPlayer).BarChitsRemaining |> should equal 6

    [<Test>]
    member _.``a once-per-round local should use the active player's mitt and be answered by the policy``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 431UL

        let local =
            MatchScenario.PlainCard "local" "KIT-006" MatchScenario.SecondPlayer CardZone.Local -1

        let vim =
            MatchScenario.PlainCard
                "mitt-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards state [ local; vim ]

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun value ->
                value.Kind = LegalActionKind.UsePartyTrick
                && (match value.Command.Action with
                    | MatchAction.UsePartyTrick(_, effect) -> effect = EffectId "KIT-006-R01"
                    | _ -> false))
            |> Seq.exactlyOne

        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))
        let cpu = DeterministicCpu()

        let decision =
            MatchScenario.Chosen(cpu.Choose(engine, requested, MatchScenario.FirstPlayer))

        let applied = MatchScenario.Applied(engine.Apply(requested, decision.Command))

        (applied.Card vim.Id).Zone |> should equal CardZone.EmptiesTray
        (applied.Card(CardInstanceId "first-draw")).Zone |> should equal CardZone.Mitt

        applied.RoundUsage.EffectsUsed
        |> Seq.contains (EffectId "KIT-006-R01")
        |> should be True

    [<Test>]
    member _.``a search with no eligible bloke should still require an explicit empty answer and resolve deterministically``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 433UL

        let kit =
            MatchScenario.PlainCard
                "talent-scout"
                "KIT-005"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let topCards =
            [ for index in 0..7 ->
                  MatchScenario.PlainCard
                      $"top-{index}"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      index ]

        let outsideWindow =
            MatchScenario.PlainCard
                "outside-window"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Stack
                8

        let state =
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card -> card.Id.Value <> "first-draw")
                        |> fun cards ->
                            Seq.concat
                                [ cards; Seq.singleton kit; topCards; Seq.singleton outsideWindow ]
                        |> Seq.sortBy (fun card -> card.Id)
                    ) }

        let play = kitAction engine state kit.Id
        let requested = MatchScenario.Applied(engine.Apply(state, play.Command))
        let requirement = requested.PendingEffect.Value.Requirements |> Seq.exactlyOne

        let resolve =
            engine.GetLegalActions(requested, MatchScenario.FirstPlayer)
            |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
            |> Seq.exactlyOne

        let omittedState, omitted =
            MatchScenario.Rejected(
                engine.Apply(
                    requested,
                    { resolve.Command with
                        Id = CommandId "omit-empty-talent-scout-choice"
                        Choices = ImmutableArray<_>.Empty }
                )
            )

        let appliedState, appliedEvents =
            MatchScenario.AppliedWith(engine.Apply(requested, resolve.Command))

        let repeatedState, repeatedEvents =
            MatchScenario.AppliedWith(MatchScenario.Engine().Apply(requested, resolve.Command))

        requirement.Kind |> should equal ChoiceRequirementKind.Cards
        requirement.Minimum |> should equal 0
        requirement.Maximum |> should equal 0
        requirement.EligibleCards.Length |> should equal 0
        omitted.Code |> should equal CommandRejectionCode.ChoiceRequired
        omittedState |> should equal requested
        appliedState |> should equal repeatedState
        appliedEvents |> should equal repeatedEvents
        (appliedState.Card kit.Id).Zone |> should equal CardZone.EmptiesTray

        topCards @ [ outsideWindow ]
        |> List.map (fun card -> (appliedState.Card card.Id).Zone)
        |> should equal (List.replicate 9 CardZone.Stack)

    [<Test>]
    member _.``a mate should put a chosen regular from the opponent's mitt onto the booth and then switch it in``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 439UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-009" MatchScenario.FirstPlayer CardZone.Mitt -1

        let basic =
            MatchScenario.PlainCard
                "other-basic"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards state [ kit; basic ]
        let action = kitAction engine state kit.Id

        let applied, events = MatchScenario.AppliedWith(engine.Apply(state, action.Command))

        (applied.Card basic.Id).Zone |> should equal CardZone.Oche
        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Booth
        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray

        events
        |> Seq.filter (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.CardMoved
            || matchEvent.Kind = MatchEventKind.OcheSwapped)
        |> Seq.map (fun matchEvent -> matchEvent.Kind, matchEvent.TargetCards |> Seq.toList)
        |> Seq.toList
        |> should
            equal
            [ MatchEventKind.CardMoved, [ basic.Id ]
              MatchEventKind.CardMoved, [ CardInstanceId "defender" ]
              MatchEventKind.CardMoved, [ basic.Id ]
              MatchEventKind.OcheSwapped, [ basic.Id; CardInstanceId "defender" ]
              MatchEventKind.CardMoved, [ kit.Id ] ]

    [<Test>]
    member _.``a mate should not switch an existing opposing booth bloke when no regular moves from the mitt``
        ()
        =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 443UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-009" MatchScenario.FirstPlayer CardZone.Mitt -1

        let existingBasic =
            MatchScenario.PlainCard
                "existing-basic"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let ineligibleBloke =
            MatchScenario.PlainCard
                "other-seasoned"
                "BLK-005"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards state [ kit; existingBasic; ineligibleBloke ]
        let action = kitAction engine state kit.Id

        let applied, events = MatchScenario.AppliedWith(engine.Apply(state, action.Command))

        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche
        (applied.Card existingBasic.Id).Zone |> should equal CardZone.Booth
        (applied.Card ineligibleBloke.Id).Zone |> should equal CardZone.Mitt
        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray

        events
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.OcheSwapped)
        |> should be False

    [<Test>]
    member _.``a mate should be absent and reject without changing a full opposing booth``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 443UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-009" MatchScenario.FirstPlayer CardZone.Mitt -1

        let basic =
            MatchScenario.PlainCard
                "other-basic"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state = MatchScenario.WithCards state (kit :: basic :: boothBlokes 5)

        engine.GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (isKitAction kit.Id)
        |> should be False

        let command =
            MatchScenario.Command
                state
                "stale-full-booth-matchmaker"
                MatchScenario.FirstPlayer
                ImmutableArray<_>.Empty
                (MatchAction.PlayKit(kit.Id, ValueNone))

        let rejectedState, rejection = MatchScenario.Rejected(engine.Apply(state, command))

        rejection.Code |> should equal CommandRejectionCode.RuleLimitReached
        rejectedState |> should equal state
        rejectedState.Revision |> should equal state.Revision
        rejectedState.LastEventSequence |> should equal state.LastEventSequence
        rejectedState.ProcessedCommands |> should equal state.ProcessedCommands
        rejectedState.RoundUsage |> should equal state.RoundUsage
        (rejectedState.Card kit.Id).Zone |> should equal CardZone.Mitt
        (rejectedState.Card basic.Id).Zone |> should equal CardZone.Mitt

    [<Test>]
    member _.``a mate should fill four opposing booth places by switching without losing cards``() =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 449UL

        let kit =
            MatchScenario.PlainCard "supporter" "KIT-009" MatchScenario.FirstPlayer CardZone.Mitt -1

        let basic =
            MatchScenario.PlainCard
                "other-basic"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let attached =
            MatchScenario.AttachedCard
                "defender-vim"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let state =
            MatchScenario.WithCards
                (addCardsAndAttachments state "defender" [ attached ])
                (kit :: basic :: boothBlokes 4)

        let originalIds = state.Cards |> Seq.map (fun card -> card.Id) |> Seq.toList
        let action = kitAction engine state kit.Id
        let applied, events = MatchScenario.AppliedWith(engine.Apply(state, action.Command))
        let defender = applied.Card(CardInstanceId "defender")

        (applied.Card basic.Id).Zone |> should equal CardZone.Oche
        defender.Zone |> should equal CardZone.Booth

        applied.CardsIn(MatchScenario.SecondPlayer, CardZone.Booth)
        |> Seq.length
        |> should equal 5

        (applied.Card kit.Id).Zone |> should equal CardZone.EmptiesTray

        applied.RoundUsage.MatesPlayed
        |> should equal (state.RoundUsage.MatesPlayed + 1)

        applied.RoundUsage.KitsPlayed |> Seq.contains kit.MechanicalId |> should be True

        applied.Cards
        |> Seq.map (fun card -> card.Id)
        |> Seq.toList
        |> should equal originalIds

        defender.Attachments |> Seq.toList |> should equal [ attached.Id ]
        (applied.Card attached.Id).Zone |> should equal CardZone.Attached
        (applied.Card attached.Id).AttachedTo |> should equal (ValueSome defender.Id)

        let swapped =
            events
            |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.OcheSwapped)
            |> Seq.exactlyOne

        swapped.TargetCards
        |> Seq.toList
        |> should equal [ basic.Id; CardInstanceId "defender" ]
