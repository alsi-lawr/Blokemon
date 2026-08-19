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
                overlay = cue.Kind switch
                {
                    // A declaration throws a blow, and what it is aimed at is whatever it is
                    // about to damage - which is knowable here, because the whole step is laid
                    // out before any of it is drawn.
                    MatchAnimationKindView.Attack => overlay.Blow(
                        cue.SourceCardInstanceId,
                        Struck(step.Events, cue.SourceCardInstanceId)
                    ),
                    // The blow landing. Damage from anyone but the card that swung is not this
                    // blow landing and ends it: nobody is mid-movement any more.
                    MatchAnimationKindView.Damage
                        when overlay.StrikingCardInstanceId != cue.SourceCardInstanceId =>
                        overlay.Blow(null, null),
                    // Everything else that can happen between the two - the beer mat tossed to
                    // find out whether the attack happens at all - happens while the blow is in
                    // the air, and leaves it alone. Clearing here took the movement off the card
                    // half way through it.
                    _ => overlay,
                };
                beats.Add(new(frame, cue, overlay.LandingOn(Landing(cue, step.Frame))));
            }

            // The table settles on what the command actually did, and the deltas that stood in
            // for it until now are spent.
            frame = step.Frame;
            beats.Add(new(frame, null, MatchPresentationOverlay.Empty));
        }

        return beats;
    }

    // What a declared blow goes on to damage, which is what it should be aimed at: an attack that
    // reaches past the card standing opposite and hits the Bench is aimed at the Bench. Every
    // card the same swing damages is collected, so a blow that catches several is aimed between
    // them rather than at whichever of them the engine happened to name first.
    //
    // A card that damages itself is left out. It is throwing the blow, and a card cannot both
    // throw one and be knocked back by it: the movement it is already making is the cause of what
    // is happening to it. An attack whose only damage is to itself - a fumble - is therefore
    // aimed at nothing, and turns where it stands rather than crossing the table at nobody.
    private static IReadOnlyList<string> Struck(
        MatchEventCueView[] cues,
        string? strikingCardInstanceId
    )
    {
        var struck = new List<string>(2);
        foreach (var cue in cues)
        {
            if (
                cue.Kind != MatchAnimationKindView.Damage
                || cue.SourceCardInstanceId != strikingCardInstanceId
            )
            {
                continue;
            }

            foreach (var target in cue.TargetCardInstanceIds)
            {
                if (
                    target != strikingCardInstanceId
                    && !struck.Contains(target, StringComparer.Ordinal)
                )
                {
                    struck.Add(target);
                }
            }
        }

        return struck;
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
