using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Blokemon.Web.Client.Pages;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// The two things the browser is asked for while a cue is on screen, and what the table does when
// the asking goes wrong.
//
// How far a card slides depends on the size of the screen, so two of the journeys are measured in
// the browser rather than declared in the stylesheet. That measurement is the least important thing
// on the table and it sat in the most dangerous place: a player hovering a hand card while a card
// was dealt hit an error inside it, and the error came back out through the beat, through the
// command that had just been applied, and took the match down with it - board, hand and the game
// in progress.
//
// So the guarantee here is about what a failure costs. A measurement that fails costs the journey:
// the card is where the beat leaves it, and the beats after it are still played and still measured.
// Nothing below looks for a catch, a class, a sentence or a length of time.
public sealed class MatchCueMotionTests
{
    [Test]
    public async Task ABrowserThatCannotMeasureLosesTheJourneyAndNothingElse()
    {
        var browser = new Browser(fails: true);
        var dealt = MatchTableFixture.Beat(MatchAnimationKindView.Draw, local: true);
        var played = MatchTableFixture.Beat(MatchAnimationKindView.Play, local: true);

        // A card is dealt and the browser fails on it; the card carried out of the hand after it is
        // played anyway, and the failure of the first reaches neither the second nor the caller.
        await Should.NotThrowAsync(async () =>
        {
            await Position(browser, dealt);
            await Position(browser, played);
        });

        // And both were genuinely measured, so a page that quietly stopped measuring altogether
        // cannot pass this by never failing.
        browser.Asked.Count.ShouldBe(2);
    }

    // A skipped presentation stops at its reveals without moving the table underneath them, so the
    // table is still the one the beat before left. The beat being played is therefore the only
    // thing that can be asked what it is carrying: a reveal that inherited the answer from the card
    // carried before it would have the browser measuring a journey nobody is making.
    [Test]
    public async Task ARevealDoesNotInheritTheJourneyOfTheCardCarriedBeforeIt()
    {
        var browser = new Browser(fails: false);
        var beats = MatchTableFixture.CarriedThenRevealed();
        var carrying = beats.First(beat => beat.Cue?.Kind == MatchAnimationKindView.Play);
        var reveal = beats.First(beat => beat.Cue?.Kind == MatchAnimationKindView.Reveal);

        await Position(browser, carrying);
        var carried = browser.Asked.Count;
        await Position(browser, reveal);

        // The card leaving the hand is measured, because it has somewhere to go.
        carried.ShouldBe(1);
        // The reveal that follows it asks for nothing: it is holding a card up, not moving one.
        browser.Asked.Count.ShouldBe(carried);
    }

    private static Task Position(Browser browser, MatchPresentationBeat beat) =>
        Match.PositionCueMotion(browser, default, beat.Cue, beat.Overlay);

    // The browser side of a measurement: what it was asked to measure, and - when this is the
    // browser that cannot - a call that fails the way a real one fails, with the module answering
    // rather than refusing to be called at all.
    private sealed class Browser(bool fails) : IJSObjectReference
    {
        public List<string> Asked { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            Answer<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => Answer<TValue>(identifier);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private ValueTask<TValue> Answer<TValue>(string identifier)
        {
            Asked.Add(identifier);
            return fails
                ? ValueTask.FromException<TValue>(new JSException("Nothing there to measure."))
                : default;
        }
    }
}
