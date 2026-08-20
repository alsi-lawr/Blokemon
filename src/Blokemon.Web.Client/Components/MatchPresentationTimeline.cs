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
            var before = frame;
            var overlay = MatchPresentationOverlay.Empty;
            var gone = new List<string>(2);
            var dealing = Dealing(step);
            var stripping = Stripping(step);
            var standing = false;
            var dealt = false;
            var stripped = false;
            for (var index = 0; index < step.Events.Length; index++)
            {
                var cue = step.Events[index];

                // A drawn card has to be in the hand before it can be dealt into it, so a draw
                // takes its step's frame early. The deltas go with the frame they were measured
                // against - and a frame that has caught up needs nothing hidden from it, because
                // it no longer has anybody anywhere they have already left.
                if (cue.Kind == MatchAnimationKindView.Draw && index >= dealing)
                {
                    standing = true;
                    dealt = true;
                    overlay = MatchPresentationOverlay.Empty;
                    gone.Clear();
                }

                if (index == stripping)
                {
                    stripped = true;
                }

                frame = Composed(before, step.Frame, standing, dealt, stripped);

                // Whatever this cue takes out of a place stays taken out. The cue that carries a
                // card off is the only one that shows it going, but the frame behind it still has
                // the card where it was for every cue after that too, so the concealment has to
                // outlive the cue that started it or a second copy comes back.
                Departs(cue, gone);
                overlay = Applied(overlay, cue).Gone(gone.ToArray());
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
                if (Stands(cue))
                {
                    standing = true;
                    before = Handed(before, cue);
                    overlay = MatchPresentationOverlay.Empty;
                    gone.Clear();
                }
            }

            // The table settles on what the command actually did, and the deltas that stood in
            // for it until now are spent.
            frame = step.Frame;
            beats.Add(new(frame, null, MatchPresentationOverlay.Empty));
        }

        return beats;
    }

    // The table one beat is drawn against, put together out of the table this command was given and
    // the one it settles on.
    //
    // Three things on it catch up at three different moments, because three different cues account
    // for them: what is standing on the table and what phase it is in, the hand this command deals
    // to the player, and the strip the opponent's held cards are drawn as. Taking all three at once
    // is what put a whole hand in front of the opponent on the beat the player was being dealt
    // theirs - nothing had said it was coming, and their own deal then played over the top of it.
    private static MatchFrameView Composed(
        MatchFrameView before,
        MatchFrameView settled,
        bool standing,
        bool dealt,
        bool stripped
    )
    {
        var table = standing ? settled : before;
        return table with
        {
            Player = Holding(table.Player, dealt ? settled.Player : before.Player),
            Opponent = Holding(table.Opponent, stripped ? settled.Opponent : before.Opponent),
        };
    }

    // A side of the table holding what it has been dealt so far rather than what it ends up
    // holding, out of the Deck it has that much left in.
    private static MatchSideView Holding(MatchSideView side, MatchSideView held) =>
        side with
        {
            DeckCount = held.DeckCount,
            HandCount = held.HandCount,
            Hand = held.Hand,
        };

    // Whether this cue is the choice that opens a game, which is the one card journey the table
    // stands up early for. Every other card played lands when the command settles, one beat later.
    private static bool Stands(MatchEventCueView cue) =>
        cue.Kind == MatchAnimationKindView.Setup && cue.SourceCardInstanceId is not null;

    // The hand a card has just been carried out of, one card lighter. Only the half that made the
    // choice loses anything: theirs is a count of backs with nothing in it to name, so it is one
    // narrower rather than one card shorter, which is the same thing said the only way their strip
    // can say it.
    private static MatchFrameView Handed(MatchFrameView frame, MatchEventCueView cue) =>
        cue.ActorIsLocalPlayer switch
        {
            true => frame with { Player = Less(frame.Player, cue.SourceCardInstanceId) },
            false => frame with { Opponent = Less(frame.Opponent, cue.SourceCardInstanceId) },
            _ => frame,
        };

    private static MatchSideView Less(MatchSideView side, string? cardInstanceId) =>
        side with
        {
            HandCount = Math.Max(0, side.HandCount - 1),
            Hand = [.. side.Hand.Where(card => card.Id != cardInstanceId)],
        };

    // Which cue of a step deals the opponent the hand they keep, so their strip is empty until it
    // and full from it.
    //
    // Their held cards are drawn as a count of backs with no identity of their own, so nothing a
    // draw of theirs says names the cards it dealt, and nothing the frame says picks out which of
    // their draws it is the hand of. The only one it can be shown to be the hand of is their last:
    // a hand with no Blokemon in it goes back into the Deck and is drawn out of it again, so a
    // strip filling on any earlier draw fills with cards that went back - and one filling on a
    // draw of the player's fills with cards no cue of theirs has dealt at all.
    private static int Stripping(MatchPresentationStepView step) =>
        Array.FindLastIndex(
            step.Events,
            cue => cue.Kind == MatchAnimationKindView.Draw && cue.ActorIsLocalPlayer == false
        );

    // Which cue of a step the frame is the hand of, so that the draws before it leave the table
    // alone.
    //
    // A step normally draws once and the frame it settles on is that draw's own, which is why a
    // draw takes it early. An opening draws several times and keeps only the last: a hand with no
    // Blokemon in it goes back into the Deck, and the frame holds the hand that stayed. Taking the
    // frame for one of the hands that went back deals the kept hand before the deal that keeps it,
    // and the deal then plays over cards already lying in front of the player - which is the same
    // hand appearing from nowhere that a new game opening on the last game's table is.
    //
    // So the frame belongs to the first draw it can show whole, and to every draw after that one. A
    // step whose draws it can show none of keeps it from the first of them, exactly as every draw
    // did before: a card drawn and spent inside the same command is in neither hand by the end of
    // it, so there is nothing to wait for.
    //
    // This is the player's hand and no more. The opponent's is a count of backs with nothing in it
    // to be named, so it has a rule of its own above and answers to their own draws.
    private static int Dealing(MatchPresentationStepView step)
    {
        for (var index = 0; index < step.Events.Length; index++)
        {
            if (
                step.Events[index].Kind == MatchAnimationKindView.Draw
                && Dealt(step.Frame, step.Events[index])
            )
            {
                return index;
            }
        }

        return 0;
    }

    // Whether this frame is the hand this draw dealt - all of it, rather than some of it. A hand
    // that went back is shuffled into the Deck it came from and drawn from again, so the hand that
    // stays routinely shares a card or two with the one that went: a draw the frame agrees with on
    // one card out of seven is still a draw of six cards the frame has never had.
    private static bool Dealt(MatchFrameView frame, MatchEventCueView cue) =>
        cue.TargetCardInstanceIds.Length > 0
        && cue.TargetCardInstanceIds.All(target =>
            Held(frame.Player, target) || Held(frame.Opponent, target)
        );

    private static bool Held(MatchSideView side, string cardInstanceId) =>
        side.Hand.Any(card => card.Id == cardInstanceId);

    // The cards this cue takes out of the place the frame still has them in. A card carried to a
    // place on the table is carried out of the hand by the presentation itself - it is drawn
    // travelling, or held up in the middle of the table - and a Blokemon knocked out is shown
    // leaving the field. Both are cards the table has finished with before the frame agrees, and
    // drawing them where they were is drawing a second one of them.
    private static void Departs(MatchEventCueView cue, List<string> gone)
    {
        if (MatchCueState.CarriesACard(cue) && cue.SourceCardInstanceId is { } played)
        {
            Leaves(gone, played);
            return;
        }

        if (cue.Kind == MatchAnimationKindView.Knockout)
        {
            foreach (var target in cue.TargetCardInstanceIds)
            {
                Leaves(gone, target);
            }
        }
    }

    private static void Leaves(List<string> gone, string cardInstanceId)
    {
        if (!gone.Contains(cardInstanceId, StringComparer.Ordinal))
        {
            gone.Add(cardInstanceId);
        }
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
        if (!MatchCueState.CarriesACard(cue) || cue.SourceCardInstanceId is not { } cardInstanceId)
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
