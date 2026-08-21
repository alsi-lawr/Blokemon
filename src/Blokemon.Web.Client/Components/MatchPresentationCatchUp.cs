using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// How much of the table a beat is allowed to have caught up with yet.
//
// A command hands over one settled table and the cues inside it play one at a time against the
// table that was already there, so every beat is drawn somewhere between the two. Three things on
// it arrive at three different moments, because three different cues account for them, and most of
// what is here is working out which cue each of them waits for.
//
// The hardest of those questions is which draw a hand is really the hand of. An opening deals
// several and keeps only the last, so it has to be asked of each half of the table separately - and
// the answer also settles which cues belong to a go that went back into the Deck, which is why that
// lives here too rather than beside the beat it marks.
internal static class MatchPresentationCatchUp
{
    // The table one beat is drawn against, put together out of the table this command was given and
    // the one it settles on.
    //
    // Three things on it catch up at three different moments, because three different cues account
    // for them: what is standing on the table and what phase it is in, the hand this command deals
    // to the player, and the strip the opponent's held cards are drawn as. Taking all three at once
    // is what put a whole hand in front of the opponent on the beat the player was being dealt
    // theirs - nothing had said it was coming, and their own deal then played over the top of it.
    internal static MatchFrameView Composed(
        MatchFrameView before,
        MatchFrameView settled,
        bool standing,
        bool dealt,
        bool stripped,
        string[] undealt
    )
    {
        var table = standing ? settled : before;
        var held = Without(settled, undealt);
        return table with
        {
            Player = Holding(table.Player, dealt ? held.Player : before.Player),
            Opponent = Holding(table.Opponent, stripped ? held.Opponent : before.Opponent),
        };
    }

    // The settled hand less the cards a later draw of the same step has still to deal, so no beat
    // shows a card before the draw that brings it. A step that draws once has nothing to withhold
    // and is untouched; a step that draws twice was showing the second draw's card from the first
    // draw onwards, and the second draw then took it out of the hand to deal it again.
    private static MatchFrameView Without(MatchFrameView frame, string[] undealt) =>
        undealt.Length == 0
            ? frame
            : frame with
            {
                Player = Withheld(frame.Player, undealt),
                Opponent = Withheld(frame.Opponent, undealt),
            };

    // Only what this side is actually holding comes off it, so a draw of the other player's leaves
    // this one alone. A withheld card has not left the Deck yet either.
    private static MatchSideView Withheld(MatchSideView side, string[] undealt)
    {
        var kept = side.Hand.Where(card => !undealt.Contains(card.Id)).ToArray();
        var removed = side.Hand.Length - kept.Length;
        return removed == 0
            ? side
            : side with
            {
                DeckCount = side.DeckCount + removed,
                HandCount = Math.Max(0, side.HandCount - removed),
                Hand = kept,
            };
    }

    // The cards a draw later in this step has still to deal.
    //
    // The extra draw is the step that needs it: it ends the setup and starts the turn that follows,
    // so the command deals the bonus card AND that turn's first card, and only draws counted here
    // are ones the frame keeps - an opening's returned hands are dealt by draws the settled frame
    // never had and must not be withheld from it.
    internal static string[] Undealt(MatchPresentationStepView step, int index)
    {
        var pending = new List<string>(2);
        for (var later = index + 1; later < step.Events.Length; later++)
        {
            var cue = step.Events[later];
            if (cue.Kind == MatchAnimationKindView.Draw && Dealt(step.Frame, cue))
            {
                pending.AddRange(cue.TargetCardInstanceIds);
            }
        }

        return [.. pending];
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
    internal static bool Stands(MatchEventCueView cue) =>
        cue.Kind == MatchAnimationKindView.Setup && cue.SourceCardInstanceId is not null;

    // The hand a card has just been carried out of, one card lighter. Only the half that made the
    // choice loses anything: theirs is a count of backs with nothing in it to name, so it is one
    // narrower rather than one card shorter, which is the same thing said the only way their strip
    // can say it.
    internal static MatchFrameView Handed(MatchFrameView frame, MatchEventCueView cue) =>
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
    internal static int Stripping(MatchPresentationStepView step) =>
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
    // So the frame belongs to the first draw it can show whole, and to every draw after that one.
    // A step it can show none of says so, rather than naming the first cue: none and the first of
    // them are different answers and the table treats them differently, even though the hand has
    // nothing to wait for in either case.
    //
    // This is the player's hand and no more. The opponent's is a count of backs with nothing in it
    // to be named, so it has a rule of its own above and answers to their own draws.
    internal static int? Dealing(MatchPresentationStepView step)
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

        return null;
    }

    // Which cues of a step are a go at an opening hand that went back into the Deck.
    //
    // An opening hand has to have a Regular in it, and one that has not goes back: the mitt returns
    // to the Deck, the Deck is shuffled, and another seven come out, for as long as it takes. Two
    // openings in five take at least one more go and every one of them genuinely happened, so every
    // one of them is played. What tells them apart from the go that stays is which draw the frame is
    // the hand of, which is already worked out for each half of the table separately just above:
    // every draw of a side before that one is a hand that went back.
    //
    // So is the shuffle that dealt it, which is the shuffle nearest before it on the same half of
    // the table - a mulligan shuffles only the Deck the cards went back into, so a shuffle belongs
    // to its own side's next draw and to nobody else's. Walking back from the end is what makes that
    // the one question asked: each side remembers what became of the draw it has just passed.
    internal static bool[] Returned(MatchPresentationStepView step, int dealing, int stripping)
    {
        var returned = new bool[step.Events.Length];
        var again = new bool[2];
        for (var index = step.Events.Length - 1; index >= 0; index--)
        {
            var cue = step.Events[index];
            if (cue.ActorIsLocalPlayer is not { } acting)
            {
                continue;
            }

            var side = acting ? 0 : 1;
            if (cue.Kind == MatchAnimationKindView.Draw)
            {
                again[side] = index < (acting ? dealing : stripping);
            }
            else if (cue.Kind != MatchAnimationKindView.Shuffle)
            {
                continue;
            }

            returned[index] = again[side];
        }

        return returned;
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
}
