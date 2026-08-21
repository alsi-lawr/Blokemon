using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// A card's journey across the table while a command is being played: whether the presentation has
// picked it up at all, where it has therefore stopped being drawn, and where it is heading.
//
// The three belong together because each answers the next. Nothing can be concealed where it was
// standing until it is known to have left, and nowhere can be expecting it until it is known to be
// going somewhere - and both of those are settled by comparing the two tables the command sits
// between, rather than by anything the cue announcing it says about itself.
internal static class MatchPresentationJourneys
{
    // The card a cue picks up, if it picks one up.
    //
    // The cue cannot say on its own. A card played out of the hand and a card whose printed ability
    // is being used are announced the same way and both name the card they are about, because both
    // are one thing a player does with one card - and only the first of them goes anywhere. A
    // Local stays in the pub slot and goes on offering its trade; a Blokemon uses its party trick
    // from the Oche it is already standing in. Told as a card being played, all of them were
    // carried out of the place they were standing in and hidden there for the rest of the command,
    // so the card whose whole point is that it stays vanished the moment it was used.
    //
    // So the two tables the command sits between are asked instead, and they answer by
    // construction: a card standing in the same place before the command and after it has been
    // nowhere, whatever the cue is called. A card that is not on the table to begin with is always
    // picked up - it comes out of a hand, and out of the opponent's there is nothing in to name -
    // and where it is going is a separate question the landing answers.
    internal static string? Carrying(
        MatchEventCueView cue,
        MatchFrameView given,
        MatchFrameView settled
    )
    {
        if (!MatchCueState.CarriesACard(cue) || cue.SourceCardInstanceId is not { } source)
        {
            return null;
        }

        var stood = Placed(given, source);
        return stood is not null && stood == Placed(settled, source) ? null : source;
    }

    // The cards this cue takes out of the place the frame still has them in. A card the
    // presentation has picked up is carried out of where it was by the presentation itself - it is
    // drawn travelling, or held up in the middle of the table - and a Blokemon knocked out is shown
    // leaving the field. Both are cards the table has finished with before the frame agrees, and
    // drawing them where they were is drawing a second one of them. A card nothing picked up has
    // left nowhere and is concealed nowhere.
    internal static void Departs(MatchEventCueView cue, string? carried, List<string> gone)
    {
        if (carried is not null)
        {
            Leaves(gone, carried);
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

    // Where the card the presentation has picked up is standing once the command has been applied.
    // A card that ends up anywhere but the table - a Kit that does its work and is discarded - has
    // no landing, and its cue keeps the presentation it has always had.
    internal static MatchLandingSlot? Landing(string? carried, MatchFrameView frame) =>
        carried is null ? null : Placed(frame, carried);

    // Where on the table a card is standing, if it is standing on it at all. A hand is not a place
    // on the table: a card being held is somewhere the table cannot point at, and the opponent's
    // hand has nothing in it to point at either.
    private static MatchLandingSlot? Placed(MatchFrameView frame, string cardInstanceId) =>
        Placed(frame.Player, cardInstanceId, opponent: false)
        ?? Placed(frame.Opponent, cardInstanceId, opponent: true);

    private static MatchLandingSlot? Placed(
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
