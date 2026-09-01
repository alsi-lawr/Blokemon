using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// The counter and fan are two readings of the Energy attached to a Blokemon. Where the faces land
// is a matter for the eye; which cards are in the fan is not.
public sealed class MatchAttachedCardTests
{
    // The counter exists because the fan alone could not be counted, so the number it shows and the
    // faces the fan draws have to be two readings of one fact. They would drift silently: a pill fed
    // from anywhere other than the cards actually hanging off the Blokemon still reads like a
    // number, and reads wrong at exactly the moment a player is working out what an attack costs.
    [Test]
    public void TheCounterAndTheFanBothReadTheEnergyAttachedToTheCard()
    {
        var host = Bloke("C1-001", energy: ["fire", "psychic", "water"], tools: []);

        var counted = MatchAttachedCards.EnergyCount(host);
        var fan = MatchAttachedCards.Energy(host);

        counted.ShouldBe(host.AttachedEnergy.Length);
        fan.Count.ShouldBe(counted);
        fan.OrderBy(face => face.Depth).Select(face => face.Card).ShouldBe(host.AttachedEnergy);
    }

    private static MatchCardInstanceView Bloke(string id, string[] energy, string[] tools) =>
        new(
            id,
            Card(id),
            "You",
            "Field",
            0,
            60,
            [.. energy.Select(Card)],
            [.. tools.Select(Card)],
            [],
            []
        );

    private static CardView Card(string id) =>
        new(id, id, CardKindView.Energy, string.Empty, string.Empty, string.Empty, [], 0, false);
}
