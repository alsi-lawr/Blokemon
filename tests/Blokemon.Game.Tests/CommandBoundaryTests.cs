using Blokemon.Game;
using Shouldly;

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

        duplicate.Rejection.Code.ShouldBe(CommandRejectionCode.DuplicateCommand);
        ReferenceEquals(duplicate.State, accepted.State).ShouldBeTrue();
        duplicate.State.ShouldBe(accepted.State);
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

        duplicate.Rejection.Code.ShouldBe(CommandRejectionCode.DuplicateCommand);
        duplicate.State.ShouldBe(accepted.State);
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

        rejected.Rejection.Code.ShouldBe(CommandRejectionCode.StaleRevision);
        rejected.State.ShouldBe(accepted.State);
    }
}
