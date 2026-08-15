using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class DeferredBranchChoiceTests
{
    [Test]
    public async Task PendingCoinBranch_ReplaysExactlyAfterEngineRestart()
    {
        var initial = CoinSwitchState();
        var firstEngine = MatchScenario.Engine();
        var requested = (CommandOutcome.Applied)
            firstEngine.Apply(initial, MatchScenario.AttackCommand(initial, "BLK-052-B01"));
        var pending = requested.State.PendingEffect!;
        var target = pending.Requirements.Single().EligibleCards.Single();
        var resolve = new MatchCommand.ResolveEffectChoice(
            new CommandId("resolve-after-restart"),
            requested.State.Id,
            pending.Chooser,
            requested.State.Revision,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    pending.Requirements.Single().Id,
                    FrozenList<CardInstanceId>.Create(target)
                )
            )
        );

        var original = (CommandOutcome.Applied)firstEngine.Apply(requested.State, resolve);
        var restarted = (CommandOutcome.Applied)
            MatchScenario.Engine().Apply(requested.State, resolve);

        await Assert.That(requested.State.Random.ConsumptionIndex).IsEqualTo(1);
        await Assert.That(pending.BeerMatResults).IsEquivalentTo([true]);
        await Assert.That(restarted.State).IsEqualTo(original.State);
        await Assert.That(restarted.Events).IsEqualTo(original.Events);
    }

    [Test]
    public async Task BasicVimAttachmentAttack_SkipsSecondaryEffectWithoutABench()
    {
        var state = MatchScenario.BattleState("BLK-123", "BLK-001", ["VIM-BLAZED"], 503);
        var discardedVim = MatchScenario.Card(
            "discarded-vim",
            "VIM-BLAZED",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(discardedVim).OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-123-B01")
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        await Assert.That(action.ChoiceRequirements.Count).IsEqualTo(0);
        await Assert.That(applied.Card(new CardInstanceId("defender")).Damage).IsEqualTo(20);
        await Assert.That(applied.Card(discardedVim.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    [Test]
    public async Task CoinAttachmentKit_SkipsHeadsBranchWithoutABenchOrTarget()
    {
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], SeedForBadge());
        var kit = MatchScenario.Card(
            "coin-kit",
            "KIT-008",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var discardedVim = MatchScenario.Card(
            "discarded-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(kit).Append(discardedVim).OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.PlayKit
                && candidate.Command is MatchCommand.PlayKit play
                && play.Kit == kit.Id
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        await Assert.That(action.ChoiceRequirements.Count).IsEqualTo(0);
        await Assert.That(applied.PendingEffect).IsNull();
        await Assert.That(applied.Card(kit.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
        await Assert.That(applied.Card(discardedVim.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    [Test]
    public async Task CoinAttachmentKit_RequestsTargetOnlyAfterHeads()
    {
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], SeedForBadge());
        var bench = MatchScenario.Card(
            "own-bench",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var kit = MatchScenario.Card(
            "coin-kit",
            "KIT-008",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var discardedVim = MatchScenario.Card(
            "discarded-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Append(bench)
                    .Append(kit)
                    .Append(discardedVim)
                    .OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.PlayKit
                && candidate.Command is MatchCommand.PlayKit play
                && play.Kit == kit.Id
            );
        var requested = (CommandOutcome.Applied)engine.Apply(state, action.Command);
        var cpu = new DeterministicCpu();
        var choice = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.FirstPlayer);

        var resolved = MatchScenario.Applied(engine.Apply(requested.State, choice.Action.Command));

        await Assert.That(action.ChoiceRequirements.Count).IsEqualTo(0);
        await Assert.That(requested.State.PendingEffect).IsNotNull();
        await Assert
            .That(requested.State.PendingEffect!.Requirements.Single().EligibleTargets)
            .IsEquivalentTo([bench.Id]);
        await Assert.That(resolved.Card(discardedVim.Id).AttachedTo).IsEqualTo(bench.Id);
        await Assert.That(resolved.Card(kit.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    private static MatchState CoinSwitchState()
    {
        var state = MatchScenario.BattleState("BLK-052", "BLK-001", ["VIM-SOBER"], SeedForBadge());
        var booth = MatchScenario.Card(
            "other-booth",
            "BLK-002",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        return state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(booth).OrderBy(static card => card.Id)
            ),
        };
    }

    private static ulong SeedForBadge()
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            var random = new BlokemonSeededRandom(seed);
            if (random.NextInt(2) == 1)
            {
                return seed;
            }
        }

        return 0;
    }
}
