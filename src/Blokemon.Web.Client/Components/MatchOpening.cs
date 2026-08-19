using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// The table a new game begins from.
//
// Every other command is presented against the frame already on screen, which is the whole
// mechanism by which a move is seen happening rather than having happened. A match start has no
// such frame. What came before it is the game before, so presenting the new one over it opens
// every battle on the last battle's hand, board and Prize rack. And the frame the start settles on
// is no use as a starting point either: it is the table with both opening hands already dealt, so
// the deal it is meant to explain would have nothing left to do.
//
// So the table a game begins from is worked out from the table it begins on: the same two players,
// the same Decks, holding nothing and standing nothing out, with the cards about to be dealt still
// in the Decks they are about to come from. Nothing here is invented and nothing is asked of the
// application - every value is one the frame the start settles on already carries - which is why a
// new game needs no new word on the wire to open on an empty table.
//
// The Prize rack is deliberately not emptied. It is the furniture this game is played on rather
// than something the opening presentation deals: no cue announces the Bar Chits being set aside,
// so counting them up would be movement with nothing to explain it. It can carry nothing of the
// game before, because it is this game's own.
public static class MatchOpening
{
    public static MatchFrameView? EmptyTable(MatchPresentationView? presentation) =>
        presentation is { Steps.Length: > 0 } opening ? Cleared(opening.Steps[0].Frame) : null;

    private static MatchFrameView Cleared(MatchFrameView started) =>
        started with
        {
            Player = Cleared(started.Player),
            Opponent = Cleared(started.Opponent),
        };

    private static MatchSideView Cleared(MatchSideView side) =>
        side with
        {
            DeckCount = side.DeckCount + side.HandCount,
            HandCount = 0,
            Active = null,
            Bench = [],
            Hand = [],
            InPlayKits = [],
        };
}
