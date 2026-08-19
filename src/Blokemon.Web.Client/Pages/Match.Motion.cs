using System.Globalization;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
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
    private async Task PositionCueMotion(MatchEventCueView? cue)
    {
        if (cue is null || _presentationModule is null)
        {
            return;
        }

        var measure = cue.Kind switch
        {
            MatchAnimationKindView.Draw => "positionDrawCards",
            MatchAnimationKindView.Play or MatchAnimationKindView.Evolve => "positionPlayCard",
            // The blow is measured once, on the cue that throws it. The cue that lands it measures
            // nothing: by then the card is part way across and no longer where it started.
            MatchAnimationKindView.Attack => "positionAttack",
            _ => null,
        };
        if (measure is not null)
        {
            await _presentationModule.InvokeVoidAsync(measure, _battleScreen);
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

    private CardView? PresentationCard(MatchFrameView frame, MatchEventCueView cue)
    {
        if (!IsCardPresentationCue(cue.Kind))
        {
            return null;
        }

        var cardInstanceId = cue.SourceCardInstanceId ?? cue.TargetCardInstanceIds.FirstOrDefault();
        if (cardInstanceId is null)
        {
            return null;
        }

        return AllVisibleCards(frame).FirstOrDefault(card => card.Id == cardInstanceId)?.Card
            ?? (
                _presentedFrame is null
                    ? null
                    : AllVisibleCards(_presentedFrame)
                        .FirstOrDefault(card => card.Id == cardInstanceId)
                        ?.Card
            );
    }

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

            lead += BeatDuration(cue);
        }

        return 0;
    }

    private string BlowLeadStyle() =>
        $"--blow-lead: {_blowLead.ToString(CultureInfo.InvariantCulture)}ms";

    private string? AnimationClass() =>
        _activeCue is null
            ? null
            : $"cue-{_activeCue.Kind.ToString().ToLowerInvariant()}{ActorCueClass(_activeCue)}";

    private static string ActorCueClass(MatchEventCueView cue) =>
        cue.ActorIsLocalPlayer switch
        {
            true => " cue-actor-local",
            false => " cue-actor-opponent",
            null => string.Empty,
        };

    private static bool IsCardPresentationCue(MatchAnimationKindView kind) =>
        kind is MatchAnimationKindView.Play or MatchAnimationKindView.Evolve;

    // A card only travels when there is somewhere on the table for it to travel to: one that
    // does its work and is discarded keeps the presentation it has always had.
    private bool CardTravels() =>
        _activeCue is not null
        && IsCardPresentationCue(_activeCue.Kind)
        && _presentationCard is not null
        && _overlay.Landing is not null;

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
    private static int BeatDuration(MatchEventCueView? cue) =>
        cue?.Kind switch
        {
            MatchAnimationKindView.Setup => 900,
            MatchAnimationKindView.Shuffle => 1200,
            MatchAnimationKindView.Draw => 900,
            MatchAnimationKindView.Play or MatchAnimationKindView.Evolve => 1000,
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
