using Blokemon.Web.Client.Components;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace Blokemon.Web.Tests;

// A card is read by holding it, and what ends that reading depends on what is doing the holding. A
// finger sits on top of the card it raises, so the moment it lifts is the first moment the card can
// be read at all: taking the card away on that lift would mean the player never sees it. A mouse
// covers nothing, so it reads while it holds and puts the card down by letting go.
//
// This is the difference between a gesture that works on a phone and one that only appears to, and
// nothing about it shows up in a rendered frame - both cases put a card on screen.
public sealed class CardHoldTests
{
    [Test]
    public async Task ACardRaisedByAFingerStaysUpWhenTheFingerLifts()
    {
        var hold = new CardHold();
        await Raised(hold, "touch");

        hold.Release().ShouldBeFalse();
    }

    [Test]
    public async Task ACardRaisedByAMouseGoesBackDownWhenTheButtonIsLetGo()
    {
        var hold = new CardHold();
        await Raised(hold, "mouse");

        hold.Release().ShouldBeTrue();
    }

    [Test]
    public async Task APressLetGoOfBeforeItBecomesAHoldHasNoCardToPutDown()
    {
        var hold = new CardHold();
        hold.Down(new PointerEventArgs { PointerType = "touch" }, () => Task.CompletedTask);

        hold.Release().ShouldBeFalse();
        hold.Viewing.ShouldBeFalse();
    }

    // Presses the card and waits for the press to become a hold, so the test asserts about a card
    // that is actually up rather than about a press that has not finished being one yet.
    private static async Task Raised(CardHold hold, string pointerType)
    {
        hold.Down(new PointerEventArgs { PointerType = pointerType }, () => Task.CompletedTask);

        for (var attempt = 0; attempt < 200 && !hold.Viewing; attempt++)
        {
            await Task.Delay(10);
        }

        hold.Viewing.ShouldBeTrue();
    }
}
