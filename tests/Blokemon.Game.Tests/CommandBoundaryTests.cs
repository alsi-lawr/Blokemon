using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class CommandBoundaryTests
{
    [Test]
    public async Task ReusingAnAcceptedCommand_IsRejectedWithoutChangingState()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()));
        var command = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .First(action => action.Kind == LegalActionKind.ChooseOpening)
            .Command;
        var accepted = (CommandOutcome.Applied)engine.Apply(state, command);

        var duplicate = (CommandOutcome.Rejected)engine.Apply(accepted.State, command);

        await Assert
            .That(duplicate.Rejection.Code)
            .IsEqualTo(CommandRejectionCode.DuplicateCommand);
        await Assert.That(ReferenceEquals(duplicate.State, accepted.State)).IsTrue();
        await Assert.That(duplicate.State).IsEqualTo(accepted.State);
    }

    [Test]
    public async Task ReusingACommandIdAtTheCurrentRevision_IsRejectedWithoutChangingState()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()));
        var command = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .First(action => action.Kind == LegalActionKind.ChooseOpening)
            .Command;
        var accepted = (CommandOutcome.Applied)engine.Apply(state, command);
        var repeated = command with { ExpectedRevision = accepted.State.Revision };

        var duplicate = (CommandOutcome.Rejected)engine.Apply(accepted.State, repeated);

        await Assert
            .That(duplicate.Rejection.Code)
            .IsEqualTo(CommandRejectionCode.DuplicateCommand);
        await Assert.That(duplicate.State).IsEqualTo(accepted.State);
    }

    [Test]
    public async Task StaleUniqueCommand_IsRejectedWithoutChangingState()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()));
        var command = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .First(action => action.Kind == LegalActionKind.ChooseOpening)
            .Command;
        var accepted = (CommandOutcome.Applied)engine.Apply(state, command);
        var stale = command with { Id = new CommandId("unique-stale-command") };

        var rejected = (CommandOutcome.Rejected)engine.Apply(accepted.State, stale);

        await Assert.That(rejected.Rejection.Code).IsEqualTo(CommandRejectionCode.StaleRevision);
        await Assert.That(rejected.State).IsEqualTo(accepted.State);
    }
}
