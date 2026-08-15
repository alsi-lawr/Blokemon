using Blokemon.Game;

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
        await Assert.That((outcome as CommandOutcome.Rejected)?.Rejection.Code).IsNull();
        var applied = (CommandOutcome.Applied)outcome;

        await Assert.That(missing.Rejection.ChoiceRequirements.Count).IsEqualTo(1);
        await Assert.That(requirement.EligibleCards).IsEquivalentTo([basic.Id]);
        await Assert.That(applied.State.Card(basic.Id).Zone).IsEqualTo(CardZone.Booth);
        await Assert.That(applied.State.Card(evolved.Id).Zone).IsEqualTo(CardZone.Stack);
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
        await Assert
            .That(
                actions.Any(value =>
                    value.Kind == LegalActionKind.PlayKit
                    && value.Command is MatchCommand.PlayKit play
                    && play.Kit == kit.Id
                )
            )
            .IsTrue();
        var action = actions.Single(value =>
            value.Kind == LegalActionKind.PlayKit
            && value.Command is MatchCommand.PlayKit play
            && play.Kit == kit.Id
        );

        var applied = (CommandOutcome.Applied)engine.Apply(state, action.Command);

        await Assert.That(applied.State.Card(otherVim.Id).Zone).IsEqualTo(CardZone.Mitt);
        await Assert
            .That(applied.State.Card(otherVim.Id).Owner)
            .IsEqualTo(MatchScenario.SecondPlayer);
        await Assert
            .That(applied.State.Card(ownVim.Id).AttachedTo)
            .IsEqualTo(new CardInstanceId("attacker"));
        await Assert.That(applied.State.Card(kit.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }
}
