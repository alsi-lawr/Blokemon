using Blokemon.Game;
using Shouldly;

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

        requested.State.Phase.ShouldBe(MatchPhase.AwaitingEffectChoice);
        requirement.Chooser.ShouldBe(MatchScenario.SecondPlayer);
        requirement.Minimum.ShouldBe(2);
        resolved.Card(first.Id).Zone.ShouldBe(CardZone.EmptiesTray);
        resolved.Card(second.Id).Zone.ShouldBe(CardZone.EmptiesTray);
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

        requested.State.Phase.ShouldBe(MatchPhase.AwaitingEffectChoice);
        requested.State.PendingEffect.ShouldNotBeNull();
        requested.State.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Oche);

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

        rejected.Rejection.Code.ShouldBe(CommandRejectionCode.WrongChooser);
        rejected.State.ShouldBe(requested.State);

        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.SecondPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(requested.State, decision.Action.Command);

        decision.Action.Kind.ShouldBe(LegalActionKind.ResolveEffectChoice);
        resolved.State.PendingEffect.ShouldBeNull();
        resolved.State.Card(new CardInstanceId("defender-bench")).Zone.ShouldBe(CardZone.Oche);
        resolved.State.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Booth);
    }
}
