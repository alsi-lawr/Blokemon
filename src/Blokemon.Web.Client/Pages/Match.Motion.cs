using System.Globalization;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Pages;

// ---- What the table does while a cue is on screen ---------------------------------------
//
// The motion itself is declared in the stylesheet against the marks the presenters put on the
// cards a cue is about. Two of the movements cross the table - a card leaving the Deck for a
// hand, a card leaving a hand for the place it ends up standing in - and neither distance can be
// written down in a stylesheet, because both depend on where the two ends happen to be on this
// screen at this size. So those are measured in the browser and handed back as custom properties
// on the elements the renderer already owns; the browser never draws anything the page did not
// render, and never holds any state of its own.
public partial class Match
{
    // What the beat being played needs measured, measured. Everything it is decided from is handed
    // in, because the one thing it must not be decided from is the table: a skipped presentation
    // stops moving the table underneath its reveals, so a beat that asked the table what it was
    // carrying would be answered about the beat before it.
    //
    // A measurement that fails costs the journey and nothing else. The distance is only how far a
    // card slides on this screen; without it the card is where the beat leaves it, which is where
    // it belongs, and the beat, the presentation and the game go on exactly as they would have.
    public static async Task PositionCueMotion(
        IJSObjectReference? presentationModule,
        ElementReference battleScreen,
        MatchEventCueView? cue,
        MatchPresentationOverlay overlay
    )
    {
        if (cue is null || presentationModule is null)
        {
            return;
        }

        var measure = cue.Kind switch
        {
            MatchAnimationKindView.Draw => "positionDrawCards",
            // The blow is measured once, on the cue that throws it. The cue that lands it measures
            // nothing: by then the card is part way across and no longer where it started.
            MatchAnimationKindView.Attack => "positionAttack",
            _ => overlay.CarriedCardInstanceId is null ? null : "positionPlayCard",
        };
        if (measure is null)
        {
            return;
        }

        try
        {
            await presentationModule.InvokeVoidAsync(measure, battleScreen);
        }
        catch (JSException)
        {
            // The card arrives without travelling.
        }
    }

    private async Task ToggleFullscreen()
    {
        if (_presentationModule is null)
        {
            return;
        }

        try
        {
            await _presentationModule.InvokeAsync<bool>("toggleFullscreen", _battleScreen);
        }
        catch (JSException)
        {
            _operationError = "Full screen is not available in this browser.";
        }
    }

    // The face of the card the presentation has picked up. It is looked for on the table this beat
    // is drawn against, then on the one still painted, and last of all on the table the battle has
    // arrived at: a card taken out of a hand nobody can see - the Blokemon the opponent chooses to
    // open with - is on none of the tables it has left, and the only one that has it is the one it
    // is on its way to.
    private CardView? PresentationCard(MatchFrameView frame, MatchPresentationOverlay overlay)
    {
        if (overlay.CarriedCardInstanceId is not { } cardInstanceId)
        {
            return null;
        }

        return Face(frame, cardInstanceId)
            ?? (_presentedFrame is null ? null : Face(_presentedFrame, cardInstanceId))
            ?? (_view?.Match is null ? null : Face(_view.Match.Frame, cardInstanceId));
    }

    private static CardView? Face(MatchFrameView frame, string cardInstanceId) =>
        AllVisibleCards(frame).FirstOrDefault(card => card.Id == cardInstanceId)?.Card;

    // A blow is thrown by the cue that declares it and lands on the cue that damages, and the
    // engine is free to put others between the two - tossing a beer mat to find out whether the
    // attack happens at all. The movement carries across them, so the only thing that has to give
    // is when it starts: the declaration waits out everything the table will spend on what comes
    // between, and the strike then lands exactly as the damage does. Nothing between the two is
    // shortened for it, and with nothing between it the wait is nothing.
    private static int LeadToBlow(IReadOnlyList<MatchPresentationBeat> beats, int declaration)
    {
        var striking = beats[declaration].Cue?.SourceCardInstanceId;
        var lead = 0;
        for (var index = declaration + 1; index < beats.Count; index++)
        {
            var cue = beats[index].Cue;
            if (cue is null || beats[index].Overlay.StrikingCardInstanceId != striking)
            {
                return 0;
            }

            if (cue.Kind == MatchAnimationKindView.Damage)
            {
                return lead;
            }

            lead += BeatDuration(beats[index]);
        }

        return 0;
    }

    // The two things the table is told about the beat it is playing: how long the card throwing a
    // blow waits before it throws it, and the share of itself this beat's motion is played at.
    private string MotionStyle() =>
        $"--blow-lead: {_blowLead.ToString(CultureInfo.InvariantCulture)}ms; "
        + $"--cue-pace: {_pace.ToString(CultureInfo.InvariantCulture)}";

    private string? AnimationClass() => MatchCueMarking.Table(_activeCue);

    // A card only travels when there is somewhere on the table for it to travel to: one that
    // does its work and is discarded keeps the presentation it has always had.
    private bool CardTravels() => _presentationCard is not null && _overlay.Landing is not null;

    // How long the stylesheet takes to carry a card out of a hand, across the table and into the
    // place it ends up standing in.
    private const int CardJourney = 1000;

    // The share of itself a go at an opening hand is played at when that hand went back into the
    // Deck.
    //
    // A hand with no Regular in it goes back and another is dealt, which two openings in five do at
    // least once, and a go that went back is the same riffle and the same deal as the go that keeps
    // its hand. At full length each of them is over two seconds of the same sentence said again
    // before the player may touch anything, four times over in the opening this was reported from.
    // At a quarter of itself a go that went back is still unmistakably a Deck being shuffled and a
    // hand being dealt, which is the whole point of playing it at all: a mulligan happened, and it
    // is worth seeing rather than worth waiting through.
    //
    // The same share is handed to the stylesheet as the pace to play the runs at, so the beat and
    // the run inside it are shortened by exactly the same amount and stay as long as each other. A
    // shortened beat over a full-length run would not be a quick shuffle - it would be a shuffle cut
    // off before it finished, which is a different thing to have seen.
    private const double WentBackPace = 0.25;

    private static double Pace(MatchPresentationBeat beat) => beat.WentBack ? WentBackPace : 1;

    // How long a beat is held with nothing playing on it.
    //
    // There is no run for the beat to stay as long as here, so the only question left is how long a
    // surface that is simply there has to be there in order to be read. The moving presentation
    // answers a version of it already: it holds each of these surfaces at its own full strength for
    // between about 0.26s - the veil over a card being played - and 0.51s, whose turn it is. A
    // still surface needs less of that than a moving one, because it is legible from its first
    // frame rather than once it has finished arriving, so the dwell sits in the middle of that band
    // and comfortably above the quarter second a short line takes to read. It is short enough that
    // a long presentation stays shorter than the same one at full length, which is what a player
    // who asked for less movement should get rather than more waiting.
    private const int StillBeat = 350;

    // How long a beat is held: what the cue on it takes, at the pace this beat is played at, or a
    // fixed dwell where there is no motion to take any time at all.
    //
    // The pace does not reach the still case, and that is the point of it rather than an oversight.
    // A beat is played at a share of itself so that the run inside it can be shortened by exactly
    // the same share and the two stay as long as each other; with the run suppressed rather than
    // shortened there is nothing left to stay as long as, and a quarter of a floor on legibility is
    // simply illegible. One number, for every beat, for the same reason: it is a floor, and a floor
    // is not a thing to take a share of.
    private int BeatHeldFor(MatchPresentationBeat beat) =>
        _reducedMotion ? StillBeat : BeatDuration(beat);

    private static int BeatDuration(MatchPresentationBeat beat) =>
        (int)(CueDuration(beat) * Pace(beat));

    // Each beat is held for as long as the stylesheet takes to play it, so the words and the
    // motion finish together, and the pause after a step lets the table settle before the next
    // command speaks. Changing one of these without the keyframe it belongs to desynchronises
    // them; nothing may depend on the numbers themselves.
    //
    // The two halves of a blow are the one place where a beat is held to a length the keyframe
    // decides rather than the other way round. A blow makes contact 47% of the way through
    // itself and the damage it caused becomes true when its own cue begins, so the declaration
    // is held for exactly the part of the lunge before contact: the card arrives as the number
    // turns over. What is left of the lunge, and the recoil that starts at contact, both have to
    // finish inside the beat that follows, so that one is the longer of the two.
    //
    // A card the presentation has picked up makes one journey of one length whichever kind of cue
    // picked it up, so how long that beat is held is asked of what the beat is doing rather than of
    // what its cue is called. Setup is why: the battle beginning and the Blokemon chosen to stand
    // in the Oche are the same kind and only the second one travels, so held to the length of the
    // first it was cut off short of the Oche and the phase changed over the top of it.
    private static int CueDuration(MatchPresentationBeat beat) =>
        beat.Overlay.CarriedCardInstanceId is not null
            ? CardJourney
            : beat.Cue?.Kind switch
            {
                MatchAnimationKindView.Setup => 900,
                MatchAnimationKindView.Shuffle => 1200,
                MatchAnimationKindView.Draw => 900,
                MatchAnimationKindView.Play or MatchAnimationKindView.Evolve => CardJourney,
                MatchAnimationKindView.Attack => 658,
                MatchAnimationKindView.Damage => 840,
                MatchAnimationKindView.Knockout => 900,
                MatchAnimationKindView.Turn => 950,
                MatchAnimationKindView.Coin => 1400,
                MatchAnimationKindView.Victory => 1100,
                null => 140,
                _ => 520,
            };
}
