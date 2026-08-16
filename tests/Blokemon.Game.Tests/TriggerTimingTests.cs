using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class TriggerTimingTests
{
    [Test]
    public async Task RuleBoxKnockout_TakesBothBarChitsAndWins()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-026",
            "BLK-151",
            ["VIM-BEER", "VIM-BEER", "VIM-SOBER"],
            97
        );
        var firstPrize = MatchScenario.Card(
            "prize-1",
            "VIM-LAIRY",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            0
        );
        var secondPrize = MatchScenario.Card(
            "prize-2",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(firstPrize).Append(secondPrize).OrderBy(static card => card.Id)
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

        var applied = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-026-B01"));

        applied.State.Card(firstPrize.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.State.Card(secondPrize.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.State.Player(MatchScenario.FirstPlayer).BarChitsRemaining.ShouldBe(0);
        applied.State.Winner.ShouldBe(MatchScenario.FirstPlayer);
    }

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
        defender.Zone.ShouldBe(CardZone.Oche);
        defender.Damage.ShouldBe(170);
    }

    [Test]
    public async Task DamagedActiveTrigger_PlacesCountersOnTheAttackerBeforeKnockouts()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-076", "BLK-107", ["VIM-LAIRY"], 107);

        var applied = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B01"));

        applied.State.Card(new CardInstanceId("attacker")).Damage.ShouldBe(30);
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

        attacked.State.Phase.ShouldBe(MatchPhase.AwaitingTriggerChoice);
        attacked.State.PendingBarChits.Count.ShouldBe(1);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, attacked.State, MatchScenario.FirstPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(attacked.State, decision.Action.Command);

        decision.Action.Kind.ShouldBe(LegalActionKind.ResolveBarChitTrigger);
        resolved.State.Card(triggeredPrize.Id).Zone.ShouldBe(CardZone.Booth);
        resolved.State.Card(extraPrize.Id).Zone.ShouldBe(CardZone.Mitt);
        resolved.State.Winner.ShouldBe(MatchScenario.FirstPlayer);
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

        attacked.State.Phase.ShouldBe(MatchPhase.AwaitingTriggerChoice);
        attacked.State.PendingKnockout.ShouldNotBeNull();
        attacked.State.Card(movableVim.Id).AttachedTo.ShouldBe(new CardInstanceId("defender"));
        attacked.State.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Oche);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, attacked.State, MatchScenario.SecondPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(attacked.State, decision.Action.Command);

        decision.Action.Kind.ShouldBe(LegalActionKind.ResolveKnockoutTrigger);
        resolved.State.Card(movableVim.Id).AttachedTo.ShouldBe(triggerSource.Id);
        resolved.State.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.EmptiesTray);
        resolved.State.Card(prize.Id).Zone.ShouldBe(CardZone.Mitt);
    }
}
