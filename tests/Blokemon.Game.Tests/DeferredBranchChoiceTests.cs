using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class DeferredBranchChoiceTests
{
    [Test]
    public async Task PendingCoinBranch_PreservesRandomnessReplaysFullStreamAndRejectsRepeatedResolution()
    {
        var engine = MatchScenario.Engine();
        var request = CoinSwitchRequest();
        var started = (MatchStartOutcome.Started)engine.Start(request);
        var allEvents = new List<MatchEvent>(started.Events);
        var state = started.State;

        CommandOutcome.Applied Apply(MatchCommand command)
        {
            var applied = (CommandOutcome.Applied)engine.Apply(state, command);
            state = applied.State;
            allEvents.AddRange(applied.Events);
            return applied;
        }

        foreach (var player in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
        {
            var playerState = state.Player(player);
            if (playerState.MulliganBonusAllowance > 0 && !playerState.MulliganBonusChosen)
            {
                Apply(
                    new MatchCommand.ChooseMulliganBonus(
                        new CommandId($"mulligan:{player.Value}"),
                        state.Id,
                        player,
                        state.Revision,
                        0
                    )
                );
            }
        }

        var attacker = state
            .CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
            .Single(card => card.MechanicalId.Value == "BLK-052");
        var defenders = state
            .CardsIn(MatchScenario.SecondPlayer, CardZone.Mitt)
            .Where(card => card.MechanicalId.Value == "BLK-001")
            .Take(2)
            .ToArray();
        Apply(
            new MatchCommand.ChooseOpening(
                new CommandId("opening:first"),
                state.Id,
                MatchScenario.FirstPlayer,
                state.Revision,
                attacker.Id,
                []
            )
        );
        Apply(
            new MatchCommand.ChooseOpening(
                new CommandId("opening:second"),
                state.Id,
                MatchScenario.SecondPlayer,
                state.Revision,
                defenders[0].Id,
                FrozenList<CardInstanceId>.Create(defenders[1].Id)
            )
        );
        Apply(
            new MatchCommand.EndRound(
                new CommandId("end-opening-round"),
                state.Id,
                MatchScenario.SecondPlayer,
                state.Revision
            )
        );
        var vim = state
            .CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
            .First(card => card.Kind == CardKind.Vim);
        Apply(
            new MatchCommand.AttachVim(
                new CommandId("attach-for-coin-switch"),
                state.Id,
                MatchScenario.FirstPlayer,
                state.Revision,
                vim.Id,
                attacker.Id
            )
        );
        var requested = Apply(
            new MatchCommand.Attack(
                new CommandId("coin-switch"),
                state.Id,
                MatchScenario.FirstPlayer,
                state.Revision,
                attacker.Id,
                new EffectId("BLK-052-B01"),
                []
            )
        );
        var pending = requested.State.PendingEffect!;
        var target = pending.Requirements.Single().EligibleCards.Single();
        var resolve = new MatchCommand.ResolveEffectChoice(
            new CommandId("resolve-coin-switch"),
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
        var wrongChooser = resolve with
        {
            Id = new CommandId("resolve-coin-switch-wrong-chooser"),
            Actor = requested.State.Other(pending.Chooser),
        };
        var eventsBeforeWrongChooser = allEvents.ToArray();

        var rejectedChooser = (CommandOutcome.Rejected)engine.Apply(requested.State, wrongChooser);
        var resolved = Apply(resolve);
        var restarted = (CommandOutcome.Applied)
            MatchScenario.Engine().Apply(requested.State, resolve);
        var finalEvents = allEvents.ToArray();
        var duplicate = (CommandOutcome.Rejected)engine.Apply(resolved.State, resolve);
        var replayed = (ReplayOutcome.Replayed)engine.ReplayEvents(finalEvents);

        await Assert.That(pending.BeerMatResults).IsEquivalentTo([true]);
        await Assert
            .That(resolved.State.Random.ConsumptionIndex)
            .IsEqualTo(requested.State.Random.ConsumptionIndex);
        await Assert.That(replayed.State).IsEqualTo(resolved.State);
        await Assert.That(restarted.State).IsEqualTo(resolved.State);
        await Assert.That(restarted.Events).IsEqualTo(resolved.Events);
        await Assert
            .That(rejectedChooser.Rejection.Code)
            .IsEqualTo(CommandRejectionCode.WrongChooser);
        await Assert.That(ReferenceEquals(rejectedChooser.State, requested.State)).IsTrue();
        await Assert.That(rejectedChooser.State).IsEqualTo(requested.State);
        await Assert
            .That(
                allEvents
                    .Take(eventsBeforeWrongChooser.Length)
                    .SequenceEqual(eventsBeforeWrongChooser)
            )
            .IsTrue();
        await Assert
            .That(duplicate.Rejection.Code)
            .IsEqualTo(CommandRejectionCode.DuplicateCommand);
        await Assert.That(ReferenceEquals(duplicate.State, resolved.State)).IsTrue();
        await Assert.That(duplicate.State).IsEqualTo(resolved.State);
        await Assert.That(allEvents.SequenceEqual(finalEvents)).IsTrue();
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

    private static MatchStartRequest CoinSwitchRequest() =>
        new(
            new MatchId("coin-branch-e2e"),
            new MatchSeed(1),
            FrozenDeckSnapshot.Create(
                MatchScenario.FirstPlayer,
                Enumerable.Repeat("BLK-052", 4).Concat(Enumerable.Repeat("VIM-SOBER", 56))
            ),
            FrozenDeckSnapshot.Create(
                MatchScenario.SecondPlayer,
                Enumerable.Repeat("BLK-001", 4).Concat(Enumerable.Repeat("VIM-SOBER", 56))
            )
        );

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
