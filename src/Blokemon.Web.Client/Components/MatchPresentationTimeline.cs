using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// One moment of a presentation: the frame the table is drawing, the cue on screen over it, and
// what the cues so far have already made true of that frame.
public sealed record MatchPresentationBeat(
    MatchFrameView Frame,
    MatchEventCueView? Cue,
    MatchPresentationOverlay Overlay
);

// The order a presentation happens in, worked out before any of it is played.
//
// A step is everything one command did, and the application can only hand over one state per
// command: the engine applies a command whole and commits a single state at the end of it
// (Blokemon.Game MatchCommit.commit puts CommittedState on the terminal event alone), so there is
// no state between two events to build a frame from, and building one would cost a legal-action
// sweep of both sides. So the frames stay per command, and what happens inside a command is
// carried as deltas against the frame already on screen: damage lands while its own cue plays,
// which is the point of the whole exercise, rather than at the frame change that follows the
// last cue - by which time the turn has usually rotated and the blow reads as belonging to it.
//
// Timing lives in the page, not here: this says only what is on screen and in what order.
public static class MatchPresentationTimeline
{
    public static IReadOnlyList<MatchPresentationBeat> Beats(
        MatchPresentationView presentation,
        MatchFrameView? previousFrame
    )
    {
        var beats = new List<MatchPresentationBeat>();
        var frame = previousFrame ?? presentation.Steps[0].Frame;

        foreach (var step in presentation.Steps)
        {
            var overlay = MatchPresentationOverlay.Empty;
            foreach (var cue in step.Events)
            {
                // A drawn card has to be in the hand before it can be dealt into it, so a draw
                // takes its step's frame early. The deltas go with the frame they were measured
                // against.
                if (cue.Kind == MatchAnimationKindView.Draw)
                {
                    frame = step.Frame;
                    overlay = MatchPresentationOverlay.Empty;
                }

                overlay = Applied(overlay, cue);
                beats.Add(new(frame, cue, overlay.LandingOn(Landing(cue, step.Frame))));
            }

            // The table settles on what the command actually did, and the deltas that stood in
            // for it until now are spent.
            frame = step.Frame;
            beats.Add(new(frame, null, MatchPresentationOverlay.Empty));
        }

        return beats;
    }

    private static MatchPresentationOverlay Applied(
        MatchPresentationOverlay overlay,
        MatchEventCueView cue
    ) =>
        cue.Kind switch
        {
            MatchAnimationKindView.Damage => overlay.WithDamage(
                cue.TargetCardInstanceIds,
                cue.Amount
            ),
            MatchAnimationKindView.Heal => overlay.WithDamage(
                cue.TargetCardInstanceIds,
                -cue.Amount
            ),
            _ => overlay,
        };

    // Where the card a cue is about is standing once the command has been applied. A card that
    // ends up anywhere but the table - a Kit that does its work and is discarded - has no
    // landing, and its cue keeps the presentation it has always had.
    private static MatchLandingSlot? Landing(MatchEventCueView cue, MatchFrameView frame)
    {
        if (
            cue.Kind is not (MatchAnimationKindView.Play or MatchAnimationKindView.Evolve)
            || cue.SourceCardInstanceId is not { } cardInstanceId
        )
        {
            return null;
        }

        return LandingOn(frame.Player, cardInstanceId, opponent: false)
            ?? LandingOn(frame.Opponent, cardInstanceId, opponent: true);
    }

    private static MatchLandingSlot? LandingOn(
        MatchSideView side,
        string cardInstanceId,
        bool opponent
    )
    {
        if (side.Active?.Id == cardInstanceId)
        {
            return new(opponent, MatchLandingKind.Active, 0);
        }

        var bench = Array.FindIndex(side.Bench, card => card.Id == cardInstanceId);
        if (bench >= 0)
        {
            return new(opponent, MatchLandingKind.Bench, bench);
        }

        return side.InPlayKits.Any(card => card.Id == cardInstanceId)
            ? new(opponent, MatchLandingKind.InPlay, 0)
            : null;
    }
}
