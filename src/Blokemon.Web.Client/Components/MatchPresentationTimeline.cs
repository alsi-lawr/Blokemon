using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// One moment of a presentation: the frame the table is drawing, the cue on screen over it, what the
// cues so far have already made true of that frame, and whether this is part of a go at an opening
// hand that went back into the Deck rather than the go that kept its hand.
//
// The last of those is not a length and does not decide one. It is the distinction the page needs
// in order to decide one, and it is made here because here is the only place that knows it: a cue
// on its own cannot tell a shuffle that is about to be undone from the one that is not.
public sealed record MatchPresentationBeat(
    MatchFrameView Frame,
    MatchEventCueView? Cue,
    MatchPresentationOverlay Overlay,
    bool WentBack
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
//
// The walk is what is left here. The three questions it asks along the way each carry enough
// reasoning to be their own subject and are answered next door: how much of the settled table this
// beat may show yet, in MatchPresentationCatchUp; whether a card has gone anywhere and where, in
// MatchPresentationJourneys; and what a blow has done and who it was aimed at, in
// MatchPresentationDamage.
public static class MatchPresentationTimeline
{
    public static IReadOnlyList<MatchPresentationBeat> Beats(
        MatchPresentationView presentation,
        MatchFrameView? previousFrame
    )
    {
        var beats = new List<MatchPresentationBeat>();
        var frame = previousFrame ?? presentation.Steps[0].Frame;

        foreach (var step in presentation.Steps.Select(Resolved))
        {
            // The table this command was given, kept apart from the running one: whether a card
            // went anywhere is a question about the two ends of the command, and the running table
            // has already had cards stood up on it by the cues in between.
            var given = frame;
            var before = given;
            var overlay = MatchPresentationOverlay.Empty;
            var gone = new List<string>(2);
            var dealing = MatchPresentationCatchUp.Dealing(step);
            // A step whose draws the frame can show none of still has nothing for the hand to wait
            // for, so the hand catches up at the first of them exactly as it always did.
            var handed = dealing ?? 0;
            var stripping = MatchPresentationCatchUp.Stripping(step);
            var returned = MatchPresentationCatchUp.Returned(step, handed, stripping);
            var standing = false;
            var dealt = false;
            var stripped = false;
            for (var index = 0; index < step.Events.Length; index++)
            {
                var cue = step.Events[index];

                // A drawn card has to be in the hand before it can be dealt into it, so a draw
                // takes its step's frame early.
                //
                // The TABLE only catches up at a draw the frame is actually the hand of. A draw it
                // is not - every draw the opponent makes, because their held cards have no identity
                // to be recognised by, and any card drawn and spent inside its own command - would
                // otherwise stand the whole command up at the draw: the card played three cues
                // later is already lying on the Bench while it is still being dealt, and the cue
                // that plays it then takes it back off to carry it there.
                //
                // The deltas go with the frame they were measured against, so they are only spent
                // when the table has caught up: a frame that has needs nothing hidden from it,
                // because it no longer has anybody anywhere they have already left, and one that
                // has not still does.
                if (cue.Kind == MatchAnimationKindView.Draw && index >= handed)
                {
                    dealt = true;
                    if (dealing is not null)
                    {
                        standing = true;
                        overlay = MatchPresentationOverlay.Empty;
                        gone.Clear();
                    }
                }

                if (index == stripping)
                {
                    stripped = true;
                }

                frame = MatchPresentationCatchUp.Composed(
                    before,
                    step.Frame,
                    standing,
                    dealt,
                    stripped,
                    MatchPresentationCatchUp.Undealt(step, index)
                );

                // Whatever this cue takes out of a place stays taken out. The cue that carries a
                // card off is the only one that shows it going, but the frame behind it still has
                // the card where it was for every cue after that too, so the concealment has to
                // outlive the cue that started it or a second copy comes back.
                var carried = MatchPresentationJourneys.Carrying(cue, given, step.Frame);
                MatchPresentationJourneys.Departs(cue, carried, gone);
                overlay = MatchPresentationDamage.Applied(overlay, cue).Gone(gone.ToArray());
                overlay = cue.Kind switch
                {
                    // A declaration throws a blow, and what it is aimed at is whatever it is
                    // about to damage - which is knowable here, because the whole step is laid
                    // out before any of it is drawn.
                    MatchAnimationKindView.Attack => overlay.Blow(
                        cue.SourceCardInstanceId,
                        MatchPresentationDamage.Struck(step.Events, cue.SourceCardInstanceId)
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
                beats.Add(
                    new(
                        frame,
                        cue,
                        overlay.Carrying(
                            carried,
                            MatchPresentationJourneys.Landing(carried, step.Frame)
                        ),
                        returned[index]
                    )
                );

                // The choice that opens a game is the one command whose step goes on to tell
                // something else: the turn that choice starts, and that turn's first draw, are
                // told inside the same command. So the table stands the chosen Blokemon up the
                // moment it has been carried there - it is standing in the Oche for the turn that
                // follows rather than nowhere at all until the draw - and the phase changes with
                // it, after the motion rather than before it. The hand that turn draws is still the
                // draw's own to deal, so the hand otherwise stays as it was - less the card that
                // has just been carried out of it, which is standing in the Oche now and cannot
                // also still be held. Nothing conceals it any more: the concealment ends when the
                // table catches up, and the table has, so the hand it left has to have lost it.
                if (MatchPresentationCatchUp.Stands(cue))
                {
                    standing = true;
                    before = MatchPresentationCatchUp.Handed(before, cue);
                    overlay = MatchPresentationOverlay.Empty;
                    gone.Clear();
                }
            }

            // The table settles on what the command actually did, and the deltas that stood in
            // for it until now are spent.
            frame = step.Frame;
            beats.Add(new(frame, null, MatchPresentationOverlay.Empty, false));
        }

        return beats;
    }

    // A step with its attack announced once the tosses that decide it have landed.
    //
    // An attack that tosses for its damage is declared, tosses, and then has done however much it
    // has done - and the engine says so in that order. The announcement carries the damage, so told
    // in the order it arrives it says how much damage the attack did before the first beer mat is
    // in the air, and the tosses that follow are a formality about a number already on screen.
    // Nothing is invented and nothing is dropped: the declaration is held back past its own tosses
    // and lands where the answer does, which is also where the blow lands.
    private static MatchPresentationStepView Resolved(MatchPresentationStepView step) =>
        step with
        {
            Events = Announced(step.Events),
        };

    private static MatchEventCueView[] Announced(MatchEventCueView[] cues)
    {
        var order = new List<MatchEventCueView>(cues.Length);
        for (var index = 0; index < cues.Length; index++)
        {
            if (cues[index].Kind != MatchAnimationKindView.Attack)
            {
                order.Add(cues[index]);
                continue;
            }

            var declaration = cues[index];
            while (index + 1 < cues.Length && cues[index + 1].Kind == MatchAnimationKindView.Coin)
            {
                order.Add(cues[++index]);
            }

            order.Add(declaration);
        }

        return [.. order];
    }
}
