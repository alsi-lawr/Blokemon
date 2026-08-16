using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class CardFlowTests
{
    [Test]
    public async Task StackSearch_FiltersBasicBlokesAndMovesTheChosenCardToTheBooth()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-016", "BLK-003", ["VIM-SOBER"], 307);
        var basic = MatchScenario.Card(
            "search-basic",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            0
        );
        var evolved = MatchScenario.Card(
            "search-evolved",
            "BLK-003",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Where(card => card.Id.Value != "first-draw")
                    .Append(basic)
                    .Append(evolved)
                    .OrderBy(card => card.Id)
            ),
        };
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-016-B01"));
        var optional = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Optional
        );
        var requested = (CommandOutcome.Applied)
            engine.Apply(
                state,
                MatchScenario.AttackCommand(
                    state,
                    "BLK-016-B01",
                    FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, true))
                )
            );
        var requirement = requested.State.PendingEffect!.Requirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.ResolveEffectChoiceCommand(
            requested.State,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(requirement.Id, FrozenList<CardInstanceId>.Create(basic.Id))
            )
        );

        var outcome = engine.Apply(requested.State, command);
        ((outcome as CommandOutcome.Rejected)?.Rejection.Code).ShouldBeNull();
        var applied = (CommandOutcome.Applied)outcome;

        missing.Rejection.ChoiceRequirements.Count.ShouldBe(1);
        requirement.EligibleCards.ShouldBe([basic.Id]);
        applied.State.Card(basic.Id).Zone.ShouldBe(CardZone.Booth);
        applied.State.Card(evolved.Id).Zone.ShouldBe(CardZone.Stack);
    }

    [Test]
    public async Task Supporter_MoveAndAttachUsesExplicitOwnersSourcesAndDestinations()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-001", "BLK-003", [], 311);
        var kit = MatchScenario.Card(
            "supporter",
            "KIT-010",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var ownVim = MatchScenario.Card(
            "own-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var otherVim = MatchScenario.Card(
            "other-vim",
            "VIM-BLAZED",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(otherVim.Id),
                            }
                            : card
                    )
                    .Append(kit)
                    .Append(ownVim)
                    .Append(otherVim)
                    .OrderBy(card => card.Id)
            ),
        };
        var actions = engine.GetLegalActions(state, MatchScenario.FirstPlayer);
        actions
            .Any(value =>
                value.Kind == LegalActionKind.PlayKit
                && value.Command is MatchCommand.PlayKit play
                && play.Kit == kit.Id
            )
            .ShouldBeTrue();
        var action = actions.Single(value =>
            value.Kind == LegalActionKind.PlayKit
            && value.Command is MatchCommand.PlayKit play
            && play.Kit == kit.Id
        );

        var requested = MatchScenario.Applied(engine.Apply(state, action.Command));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var applied = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(ownVim.Id)
                        )
                    )
                )
            )
        );

        applied.Card(otherVim.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.Card(otherVim.Id).Owner.ShouldBe(MatchScenario.SecondPlayer);
        applied.Card(ownVim.Id).AttachedTo.ShouldBe(new CardInstanceId("attacker"));
        applied.Card(kit.Id).Zone.ShouldBe(CardZone.EmptiesTray);
    }
}
