using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// The two things a player does with a card attached to a Blokemon: count it, and read it.
//
// Counting it was impossible because the fan drew every Energy on top of the one before it, and
// reading it was impossible because an attached card had no press of its own. Both are answered by
// the same fan: the pill counts what the fan draws, and a press names one of the faces the fan drew
// so the viewer opens on that card rather than on the card it is attached to.
//
// Nothing here reads a stylesheet, a class, an element or a length of time. Where the faces land is
// a matter for the eye; which cards are in the fan and which one a press answers with is not.
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

        // Alex's ruling on the stack: each Energy after the first sits behind the one before it. The
        // fan is therefore handed over back to front, so the face nearest its host is the last one
        // drawn and the one lying on top of the rest. Reverse it and the fan reads the same in a
        // diff while stacking the wrong way on the table.
        fan.Select(face => face.Depth).ShouldBe([2, 1, 0]);
    }

    // Which card a held press opens. A press names either a card the table is drawing or a face
    // hanging off one, and a name of one kind must never be answered by a card of the other: before
    // this, a press anywhere on the assembly opened the host, which is the defect. The host still
    // answering its own name is the half a fix can silently take away.
    [Test]
    public void APressOnAnAttachedCardOpensThatCardRatherThanTheOneItIsAttachedTo()
    {
        var attacker = Bloke("C1-001", energy: ["fire", "water"], tools: ["belt"]);
        var benched = Bloke("C1-002", energy: ["grass"], tools: []);
        var table = new[] { attacker, benched };

        var energy = MatchAttachedCards.Energy(attacker).Single(face => face.Depth == 1);
        var tool = MatchAttachedCards.Tools(attacker).Single();
        var theirs = MatchAttachedCards.Energy(benched).Single();

        // The face pressed is the card opened, and the second Energy in the fan is not the first.
        MatchTable.Pressed(table, energy.ViewKey).ShouldBe(attacker.AttachedEnergy[1]);
        MatchTable.Pressed(table, tool.ViewKey).ShouldBe(attacker.AttachedTools[0]);

        // A press on the Blokemon itself still opens the Blokemon.
        MatchTable.Pressed(table, attacker.Id).ShouldBe(attacker.Card);
        MatchTable.Pressed(table, benched.Id).ShouldBe(benched.Card);

        // And no card answers for another card's fan, nor for a name belonging to nothing.
        MatchTable.Pressed(table, theirs.ViewKey).ShouldBe(benched.AttachedEnergy[0]);
        MatchTable.Pressed([attacker], theirs.ViewKey).ShouldBeNull();
        MatchTable.Pressed(table, "C1-003").ShouldBeNull();
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
        new(id, id, CardKindView.BasicVim, string.Empty, string.Empty, string.Empty, [], 0, false);
}
