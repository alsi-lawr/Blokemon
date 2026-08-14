using Blokemon.Core.SetDesign;
using Blokemon.Game;

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

        await Assert.That(state.OpeningPlayer).IsEqualTo(expectedOpening);
        await Assert
            .That(state.Players.All(static player => player.BarChitsRemaining == 6))
            .IsTrue();
        await Assert
            .That(state.Players.All(player => state.CardsIn(player.Id, CardZone.Mitt).Count() == 7))
            .IsTrue();

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

        await Assert.That(state.Phase).IsEqualTo(MatchPhase.Playing);
        await Assert.That(state.ActivePlayer).IsEqualTo(expectedOpening);
        await Assert
            .That(state.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche).Count())
            .IsEqualTo(1);
        await Assert
            .That(state.CardsIn(MatchScenario.SecondPlayer, CardZone.Oche).Count())
            .IsEqualTo(1);
        await Assert
            .That(state.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth).Count())
            .IsEqualTo(5);
        await Assert
            .That(state.CardsIn(MatchScenario.SecondPlayer, CardZone.Booth).Count())
            .IsEqualTo(5);
        await Assert.That(state.Player(expectedOpening).RoundsStarted).IsEqualTo(1);
        foreach (var player in new[] { MatchScenario.FirstPlayer, MatchScenario.SecondPlayer })
        {
            var barChits = state.CardsIn(player, CardZone.BarChit).ToArray();
            await Assert.That(barChits.Length).IsEqualTo(6);
            await Assert.That(barChits.All(static card => card.IsFaceDown)).IsTrue();
            await Assert
                .That(barChits.Select(static card => card.Id).Distinct().Count())
                .IsEqualTo(6);
        }
    }
}
