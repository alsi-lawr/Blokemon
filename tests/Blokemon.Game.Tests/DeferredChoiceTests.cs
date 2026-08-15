using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class DeferredChoiceTests
{
    [Test]
    public async Task OpponentChosenDiscard_PersistsUntilTheOpponentChoosesTheirHandCards()
    {
        var engine = MatchScenario.Engine();
        var initial = MatchScenario.BattleState(
            "BLK-024",
            "BLK-150",
            ["VIM-DODGY", "VIM-DODGY", "VIM-DODGY"],
            79
        );
        var first = MatchScenario.Card(
            "other-mitt-1",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        var second = MatchScenario.Card(
            "other-mitt-2",
            "VIM-SOBER",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        initial = initial with
        {
            Cards = FrozenList<CardState>.Create(
                initial.Cards.Append(first).Append(second).OrderBy(static card => card.Id)
            ),
        };

        var requested = (CommandOutcome.Applied)
            engine.Apply(initial, MatchScenario.AttackCommand(initial, "BLK-024-B02"));
        var requirement = requested.State.PendingEffect!.Requirements.Single();
        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.SecondPlayer);
        var resolved = MatchScenario.Applied(
            engine.Apply(requested.State, decision.Action.Command)
        );

        await Assert.That(requested.State.Phase).IsEqualTo(MatchPhase.AwaitingEffectChoice);
        await Assert.That(requirement.Chooser).IsEqualTo(MatchScenario.SecondPlayer);
        await Assert.That(requirement.Minimum).IsEqualTo(2);
        await Assert.That(resolved.Card(first.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
        await Assert.That(resolved.Card(second.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    [Test]
    public async Task OpponentChosenSwitch_PersistsUntilTheOpponentChooses()
    {
        var engine = MatchScenario.Engine();
        var initial = MatchScenario.BattleState("BLK-012", "BLK-001", ["VIM-BLAZED"], 81);
        var bench = MatchScenario.Card(
            "defender-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        initial = initial with
        {
            Cards = FrozenList<CardState>.Create(
                initial.Cards.Append(bench).OrderBy(card => card.Id)
            ),
        };

        var requested = (CommandOutcome.Applied)
            engine.Apply(initial, MatchScenario.AttackCommand(initial, "BLK-012-B01"));

        await Assert.That(requested.State.Phase).IsEqualTo(MatchPhase.AwaitingEffectChoice);
        await Assert.That(requested.State.PendingEffect).IsNotNull();
        await Assert
            .That(requested.State.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.Oche);

        var requirement = requested.State.PendingEffect!.Requirements.Single();
        var wrongChooser = new MatchCommand.ResolveEffectChoice(
            new CommandId("wrong-chooser"),
            requested.State.Id,
            MatchScenario.FirstPlayer,
            requested.State.Revision,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    requirement.Id,
                    FrozenList<CardInstanceId>.Create(new CardInstanceId("defender-bench"))
                )
            )
        );
        var rejected = (CommandOutcome.Rejected)engine.Apply(requested.State, wrongChooser);

        await Assert.That(rejected.Rejection.Code).IsEqualTo(CommandRejectionCode.WrongChooser);
        await Assert.That(rejected.State).IsEqualTo(requested.State);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.SecondPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(requested.State, decision.Action.Command);

        await Assert.That(decision.Action.Kind).IsEqualTo(LegalActionKind.ResolveEffectChoice);
        await Assert.That(resolved.State.PendingEffect).IsNull();
        await Assert
            .That(resolved.State.Card(new CardInstanceId("defender-bench")).Zone)
            .IsEqualTo(CardZone.Oche);
        await Assert
            .That(resolved.State.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.Booth);
    }
}
