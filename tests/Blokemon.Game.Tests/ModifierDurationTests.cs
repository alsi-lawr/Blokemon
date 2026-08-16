using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class ModifierDurationTests
{
    [Test]
    public async Task NextTurnDamageModifier_AppliesOnlyToItsSourceOnTheOwnersNextRound()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-076",
            "BLK-003",
            ["VIM-LAIRY", "VIM-SOBER", "VIM-SOBER"],
            211
        );

        state = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B01"))
        );
        var endOpponentRound = new MatchCommand.EndRound(
            new CommandId("end-opponent-round"),
            state.Id,
            MatchScenario.SecondPlayer,
            state.Revision
        );
        state = MatchScenario.Applied(engine.Apply(state, endOpponentRound));
        state = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B02"))
        );

        state.Card(new CardInstanceId("defender")).Damage.ShouldBe(350);
    }

    [Test]
    public async Task ContinuousGlobalTaxiModifier_IsRecomputedFromTheDeclarativeSource()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-001", "BLK-003", [], 223);
        var source = MatchScenario.Card(
            "free-taxi-source",
            "BLK-149",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(source).OrderBy(card => card.Id)
            ),
        };
        var command = new MatchCommand.Taxi(
            new CommandId("free-taxi"),
            state.Id,
            MatchScenario.FirstPlayer,
            state.Revision,
            source.Id,
            []
        );

        var applied = (CommandOutcome.Applied)engine.Apply(state, command);

        applied.State.Card(source.Id).Zone.ShouldBe(CardZone.Oche);
        applied.State.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.Booth);
    }
}
