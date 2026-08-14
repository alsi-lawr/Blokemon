using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class DeterminismAndReplayTests
{
    [Test]
    public async Task IdenticalSeedAndCommands_ReproduceEventsAndState()
    {
        var engine = MatchScenario.Engine();
        var request = MatchScenario.StartRequest();
        var firstStart = (MatchStartOutcome.Started)engine.Start(request);
        var repeatedStart = (MatchStartOutcome.Started)engine.Start(request);

        await Assert.That(repeatedStart.State).IsEqualTo(firstStart.State);
        await Assert.That(repeatedStart.Events).IsEqualTo(firstStart.Events);

        var commands = new List<MatchCommand>();
        var allEvents = new List<MatchEvent>(firstStart.Events);
        var state = firstStart.State;
        foreach (var player in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
        {
            var command = engine
                .GetLegalActions(state, player)
                .First(action => action.Kind == LegalActionKind.ChooseOpening)
                .Command;
            commands.Add(command);
            var applied = (CommandOutcome.Applied)engine.Apply(state, command);
            state = applied.State;
            allEvents.AddRange(applied.Events);
        }

        var endRound = new MatchCommand.EndRound(
            new CommandId("end-round"),
            state.Id,
            state.ActivePlayer,
            state.Revision
        );
        commands.Add(endRound);
        var ended = (CommandOutcome.Applied)engine.Apply(state, endRound);
        state = ended.State;
        allEvents.AddRange(ended.Events);

        var eventReplay = (ReplayOutcome.Replayed)engine.ReplayEvents(allEvents);
        var commandReplay = (ReplayOutcome.Replayed)engine.ReplayCommands(request, commands);

        await Assert.That(eventReplay.State).IsEqualTo(state);
        await Assert.That(commandReplay.State).IsEqualTo(state);
    }

    [Test]
    public async Task CpuPolicy_SelectsTheSameStableLegalAction()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()));
        var cpu = new DeterministicCpu();

        var first = (CpuDecision.Selected)cpu.Choose(engine, state, MatchScenario.FirstPlayer);
        var repeated = (CpuDecision.Selected)cpu.Choose(engine, state, MatchScenario.FirstPlayer);

        await Assert.That(repeated.Action).IsEqualTo(first.Action);
    }

    [Test]
    public async Task EventReplay_ReexecutesRecordedInputAndRejectsAForgedCommit()
    {
        var engine = MatchScenario.Engine();
        var started = (MatchStartOutcome.Started)engine.Start(MatchScenario.StartRequest());
        var events = started.Events.ToArray();
        events[^1] = events[^1] with { CommittedState = started.State with { RoundNumber = 99 } };

        var replay = (ReplayOutcome.Rejected)engine.ReplayEvents(events);

        await Assert.That(replay.Issue.Code).IsEqualTo(ReplayIssueCode.StateMismatch);
    }
}
