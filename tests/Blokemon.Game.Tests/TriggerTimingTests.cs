using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class TriggerTimingTests
{
    [Test]
    public async Task WouldBeKnockedOutTrigger_ResolvesBeforeKnockout()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-076",
            "BLK-068",
            ["VIM-LAIRY", "VIM-SOBER", "VIM-SOBER"],
            0
        );

        var applied = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B02"));

        var defender = applied.State.Card(new CardInstanceId("defender"));
        await Assert.That(defender.Zone).IsEqualTo(CardZone.Oche);
        await Assert.That(defender.Damage).IsEqualTo(170);
    }

    [Test]
    public async Task DamagedActiveTrigger_PlacesCountersOnTheAttackerBeforeKnockouts()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-076", "BLK-107", ["VIM-LAIRY"], 107);

        var applied = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B01"));

        await Assert.That(applied.State.Card(new CardInstanceId("attacker")).Damage).IsEqualTo(30);
    }

    [Test]
    public async Task TakenFaceDownTrigger_WaitsForThePrizeOwnerAndCpuCompletesIt()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-003",
            "BLK-001",
            ["VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER"],
            0
        );
        var triggeredPrize = MatchScenario.Card(
            "triggered-prize",
            "BLK-113",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            0
        );
        var extraPrize = MatchScenario.Card(
            "extra-prize",
            "VIM-LAIRY",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            1
        );
        var defenderBench = MatchScenario.Card(
            "defender-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Append(triggeredPrize)
                    .Append(extraPrize)
                    .Append(defenderBench)
                    .OrderBy(card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 2,
                        }
                        : player
                )
            ),
        };

        var attacked = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"));

        await Assert.That(attacked.State.Phase).IsEqualTo(MatchPhase.AwaitingTriggerChoice);
        await Assert.That(attacked.State.PendingBarChits.Count).IsEqualTo(1);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, attacked.State, MatchScenario.FirstPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(attacked.State, decision.Action.Command);

        await Assert.That(decision.Action.Kind).IsEqualTo(LegalActionKind.ResolveBarChitTrigger);
        await Assert.That(resolved.State.Card(triggeredPrize.Id).Zone).IsEqualTo(CardZone.Booth);
        await Assert.That(resolved.State.Card(extraPrize.Id).Zone).IsEqualTo(CardZone.Mitt);
        await Assert.That(resolved.State.Winner).IsEqualTo(MatchScenario.FirstPlayer);
    }

    [Test]
    public async Task KnockoutEnergyMove_WaitsForItsOwnerBeforeKnockoutCompletes()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-003",
            "BLK-001",
            ["VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER"],
            103
        );
        var triggerSource = MatchScenario.Card(
            "trigger-source",
            "BLK-026",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var movableVim = MatchScenario.Card(
            "movable-vim",
            "VIM-BEER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var prize = MatchScenario.Card(
            "prize",
            "VIM-LAIRY",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            0
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(movableVim.Id),
                            }
                            : card
                    )
                    .Append(triggerSource)
                    .Append(movableVim)
                    .Append(prize)
                    .OrderBy(card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 1,
                        }
                        : player
                )
            ),
        };

        var attacked = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"));

        await Assert.That(attacked.State.Phase).IsEqualTo(MatchPhase.AwaitingTriggerChoice);
        await Assert.That(attacked.State.PendingKnockout).IsNotNull();
        await Assert
            .That(attacked.State.Card(movableVim.Id).AttachedTo)
            .IsEqualTo(new CardInstanceId("defender"));
        await Assert
            .That(attacked.State.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.Oche);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, attacked.State, MatchScenario.SecondPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(attacked.State, decision.Action.Command);

        await Assert.That(decision.Action.Kind).IsEqualTo(LegalActionKind.ResolveKnockoutTrigger);
        await Assert
            .That(resolved.State.Card(movableVim.Id).AttachedTo)
            .IsEqualTo(triggerSource.Id);
        await Assert
            .That(resolved.State.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.EmptiesTray);
        await Assert.That(resolved.State.Card(prize.Id).Zone).IsEqualTo(CardZone.Mitt);
    }
}
