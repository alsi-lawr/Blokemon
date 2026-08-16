using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class ResignTests
{
    [Test]
    public async Task Resign_IsLegalForBothPlayersInEveryLivePhase()
    {
        var engine = MatchScenario.Engine();
        foreach (var live in LivePhases())
        {
            foreach (var actor in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
            {
                engine
                    .GetLegalActions(live.State, actor)
                    .Count(static action => action.Kind == LegalActionKind.Resign)
                    .ShouldBe(1, $"{live.Name} / {actor.Value}");
            }
        }
    }

    [Test]
    public async Task Resign_CompletesTheMatchForTheOpponentAndClearsPendingRequirements()
    {
        var engine = MatchScenario.Engine();
        foreach (var live in LivePhases())
        {
            foreach (var actor in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
            {
                var applied = (CommandOutcome.Applied)
                    engine.Apply(live.State, ResignCommand(live.State, actor));
                var opponent = live.State.Other(actor);

                applied.State.Winner.ShouldBe(opponent, $"{live.Name} / {actor.Value}");
                applied.State.Phase.ShouldBe(MatchPhase.Complete, live.Name);
                applied.State.PendingEffect.ShouldBeNull(live.Name);
                applied.State.PendingKnockout.ShouldBeNull(live.Name);
                applied.State.PendingBarChits.ShouldBeEmpty(live.Name);
                applied.State.ReplacementPlayer.ShouldBeNull(live.Name);
                applied.State.PendingRoundEnd.ShouldBeFalse(live.Name);
                applied.State.SuddenDeathCount.ShouldBe(live.State.SuddenDeathCount, live.Name);
                applied
                    .Events.Count(matchEvent =>
                        matchEvent.Kind == MatchEventKind.MatchWon && matchEvent.Actor == opponent
                    )
                    .ShouldBe(1, $"{live.Name} / {actor.Value}");
            }
        }
    }

    [Test]
    public async Task Resigning_LeavesNoFurtherLegalActionOrAcceptedCommand()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-003", "BLK-001", ["VIM-BLAZED"], 5);
        var resigned = MatchScenario.Applied(
            engine.Apply(state, ResignCommand(state, MatchScenario.FirstPlayer))
        );

        var endRound = new MatchCommand.EndRound(
            new CommandId("after-resign"),
            resigned.Id,
            MatchScenario.SecondPlayer,
            resigned.Revision
        );
        var rejected = (CommandOutcome.Rejected)engine.Apply(resigned, endRound);

        rejected.Rejection.Code.ShouldBe(CommandRejectionCode.MatchComplete);
        rejected.State.ShouldBe(resigned);
        engine.GetLegalActions(resigned, MatchScenario.FirstPlayer).ShouldBeEmpty();
        engine.GetLegalActions(resigned, MatchScenario.SecondPlayer).ShouldBeEmpty();
    }

    [Test]
    public async Task Resigning_ACompletedMatch_IsRejectedLikeAnyOtherCommand()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-003", "BLK-001", ["VIM-BLAZED"], 5);
        var resigned = MatchScenario.Applied(
            engine.Apply(state, ResignCommand(state, MatchScenario.FirstPlayer))
        );

        var rejected = (CommandOutcome.Rejected)
            engine.Apply(resigned, ResignCommand(resigned, MatchScenario.SecondPlayer));

        rejected.Rejection.Code.ShouldBe(CommandRejectionCode.MatchComplete);
        rejected.State.ShouldBe(resigned);
        rejected.State.Winner.ShouldBe(MatchScenario.SecondPlayer);
    }

    [Test]
    public async Task DeterministicCpu_NeverResignsAndKeepsAnActionInEveryLivePhase()
    {
        var engine = MatchScenario.Engine();
        var cpu = new DeterministicCpu();
        foreach (var live in LivePhases())
        {
            foreach (var actor in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
            {
                var decision = cpu.Choose(engine, live.State, actor);
                var available = engine
                    .GetLegalActions(live.State, actor)
                    .Where(static action => action.Kind != LegalActionKind.Resign)
                    .ToArray();

                decision
                    .Match(
                        static selected => selected.Action.Kind == LegalActionKind.Resign,
                        static _ => false
                    )
                    .ShouldBeFalse($"{live.Name} / {actor.Value}");
                decision
                    .Match(static _ => true, static _ => false)
                    .ShouldBe(available.Length > 0, $"{live.Name} / {actor.Value}");
            }
        }
    }

    private static MatchCommand.Resign ResignCommand(MatchState state, PlayerId actor) =>
        new(new CommandId($"resign:{actor.Value}"), state.Id, actor, state.Revision);

    private static LivePhase[] LivePhases() =>
        [
            new("own turn", MatchScenario.BattleState("BLK-003", "BLK-001", ["VIM-BLAZED"], 5)),
            new("sudden death", SuddenDeathState()),
            new("pending effect choice", PendingEffectChoiceState()),
            new("pending knockout trigger", PendingKnockoutTriggerState()),
            new("pending bar chit trigger", PendingBarChitTriggerState()),
            new("pending replacement", PendingReplacementState()),
        ];

    private static MatchState SuddenDeathState()
    {
        var state = MatchScenario.BattleState("BLK-003", "BLK-001", ["VIM-BLAZED"], 11);
        return state with
        {
            SuddenDeathCount = 1,
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(static player => player with { BarChitsRemaining = 1 })
            ),
        };
    }

    private static MatchState PendingEffectChoiceState()
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
                initial.Cards.Append(bench).OrderBy(static card => card.Id)
            ),
        };

        var requested = MatchScenario.Applied(
            engine.Apply(initial, MatchScenario.AttackCommand(initial, "BLK-012-B01"))
        );
        requested.Phase.ShouldBe(MatchPhase.AwaitingEffectChoice);
        requested.PendingEffect.ShouldNotBeNull();
        return requested;
    }

    private static MatchState PendingKnockoutTriggerState()
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
                    .Cards.Select(static card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(
                                    new CardInstanceId("movable-vim")
                                ),
                            }
                            : card
                    )
                    .Append(triggerSource)
                    .Append(movableVim)
                    .Append(prize)
                    .OrderBy(static card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(static player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 1,
                        }
                        : player
                )
            ),
        };

        var attacked = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"))
        );
        attacked.Phase.ShouldBe(MatchPhase.AwaitingTriggerChoice);
        attacked.PendingKnockout.ShouldNotBeNull();
        return attacked;
    }

    private static MatchState PendingBarChitTriggerState()
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
                    .OrderBy(static card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(static player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 2,
                        }
                        : player
                )
            ),
        };

        var attacked = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"))
        );
        attacked.Phase.ShouldBe(MatchPhase.AwaitingTriggerChoice);
        attacked.PendingBarChits.ShouldNotBeEmpty();
        return attacked;
    }

    private static MatchState PendingReplacementState()
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
                    .Cards.Append(firstPrize)
                    .Append(secondPrize)
                    .Append(defenderBench)
                    .OrderBy(static card => card.Id)
            ),
        };

        var attacked = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-026-B01"))
        );
        attacked.Phase.ShouldBe(MatchPhase.AwaitingReplacement);
        attacked.ReplacementPlayer.ShouldBe(MatchScenario.SecondPlayer);
        return attacked;
    }

    private sealed record LivePhase(string Name, MatchState State);
}
