using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class OpeningTests
{
    [Test]
    public async Task OpeningSamplesBeforeShuffleAndStartsWithExplicitPlacements()
    {
        const ulong seed = 0x0C4EUL;
        var expectedRandom = new BlokemonSeededRandom(seed);
        var expectedOpening =
            expectedRandom.NextInt(2) == 0 ? MatchScenario.FirstPlayer : MatchScenario.SecondPlayer;
        var engine = MatchScenario.Engine();
        var state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest(seed)));

        state.OpeningPlayer.ShouldBe(expectedOpening);
        state.Players.All(static player => player.BarChitsRemaining == 6).ShouldBeTrue();
        state.Players.All(player => state.CardsIn(player.Id, CardZone.Mitt).Count() == 7)
            .ShouldBeTrue();

        foreach (var player in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
        {
            var mitt = state.CardsIn(player, CardZone.Mitt).ToArray();
            var command = new MatchCommand.ChooseOpening(
                new CommandId($"opening:{player.Value}"),
                state.Id,
                player,
                state.Revision,
                mitt[0].Id,
                FrozenList<CardInstanceId>.Create(
                    mitt.Skip(1).Take(5).Select(static card => card.Id)
                )
            );
            state = MatchScenario.Applied(engine.Apply(state, command));
        }

        state.Phase.ShouldBe(MatchPhase.Playing);
        state.ActivePlayer.ShouldBe(expectedOpening);
        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche).Count().ShouldBe(1);
        state.CardsIn(MatchScenario.SecondPlayer, CardZone.Oche).Count().ShouldBe(1);
        state.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth).Count().ShouldBe(5);
        state.CardsIn(MatchScenario.SecondPlayer, CardZone.Booth).Count().ShouldBe(5);
        state.Player(expectedOpening).RoundsStarted.ShouldBe(1);
        foreach (var player in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
        {
            var barChits = state.CardsIn(player, CardZone.BarChit).ToArray();
            barChits.Length.ShouldBe(6);
            barChits.All(static card => card.IsFaceDown).ShouldBeTrue();
            barChits.Select(static card => card.Id).Distinct().Count().ShouldBe(6);
        }
    }
}
