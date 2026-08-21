using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// What the presentation says about one thing the table is drawing while a cue is on screen.
//
// A card can be several of these at once - the far end of a blow that has just carried it off the
// table is struck and gone together - so they combine. What they are not is the names of the rules
// written for them: the marks a browser matches on are derived from these in one place, and every
// question about which card the presentation has identified as moving is asked and answered here,
// where renaming a rule cannot change the answer.
[Flags]
public enum MatchCueRole
{
    None = 0,

    // The card a cue is acting from, and the card it is acting on. Both come from the cue itself,
    // which names them.
    Source = 1 << 0,
    Target = 1 << 1,

    // The two ends of a blow. The card throwing it keeps this from the declaration through the
    // damage, so the one movement is not cut in half where the cues change; the card taking it
    // carries the other end only while the damage is landing.
    Striking = 1 << 2,
    Struck = 1 << 3,

    // A card the presentation has already carried out of the place the frame still has it in.
    // Nothing about a cue can say this, because it stays true after the cue that did it.
    Gone = 1 << 4,

    // The face-down back being dealt into the opponent's strip, which has no identity of its own
    // there: their held cards are drawn as a count of backs.
    Arriving = 1 << 5,
}

// Which half of the table a cue belongs to. A cue with no actor belongs to neither, and reaches
// neither half's furniture.
public enum MatchCueActor
{
    Unattributed,
    Local,
    Opponent,
}

// The cue the whole table is wearing: what is happening, and who is doing it. Both halves matter -
// a shuffle that forgets whose it is bounces both Decks - so the actor is carried beside the kind
// rather than left to whatever is written downstream of it.
public sealed record MatchTableCue(MatchAnimationKindView Kind, MatchCueActor Actor);

// Where inside the place kept for it a card being played comes to rest. The Active card leans away
// from the middle of the table at its own end; everything else stands in the middle of its place.
public enum MatchLandingPlacement
{
    Top,
    Bottom,
    Centre,
}

// Which of the two piles a card of a shuffling Deck parts into.
public enum MatchRifflePile
{
    Left,
    Right,
}

// The presentation's answer about every card on the table while a cue plays.
//
// Four defects on this presentation shared one shape: a card journey that was described in full
// and had never once played, because the card it was written for was never identified as the one
// moving. None of them could be seen by reading either half alone. They are all answered here, in
// values that say what the presentation thinks is happening rather than what the table will be
// wearing - so a renamed rule, a moved element or a restyled card changes nothing about them.
public static class MatchCueState
{
    // Everything true of a held card: which end of the cue on screen it is, and whether the
    // presentation has already carried it out of the hand. A held card is one end of both journeys
    // between the hand and the table - it is dealt into the hand, and it is played out of it - and
    // both of those journeys were once described for a card the hand never identified.
    public static MatchCueRole HeldCard(
        MatchEventCueView? cue,
        MatchPresentationOverlay overlay,
        string cardInstanceId
    ) => Named(cue, cardInstanceId) | Departed(overlay, cardInstanceId);

    // And everything true of a card standing on the table, which can additionally be one end of a
    // blow. Both the blow and the departure outlive the cue that made them true, so both are asked
    // of the presentation rather than of whatever is on screen at this instant.
    public static MatchCueRole FieldCard(
        MatchEventCueView? cue,
        MatchPresentationOverlay overlay,
        string cardInstanceId
    ) =>
        Named(cue, cardInstanceId)
        | Blow(overlay, cardInstanceId)
        | Departed(overlay, cardInstanceId);

    // Which end of a blow a card is, if it is either. Neither is something a single cue knows - a
    // blow is declared by one and lands on another - so both are asked of the presentation, which
    // worked the whole exchange out before any of it was drawn. Damage nobody swung for names
    // nobody.
    //
    // A card can only be one end of it. One that damages itself is throwing the blow, not taking
    // it: it is already making a movement of its own, and a card cannot be knocked back by the
    // thing it is in the middle of doing.
    public static MatchCueRole Blow(MatchPresentationOverlay overlay, string cardInstanceId)
    {
        if (overlay.StrikingCardInstanceId is not { } striking)
        {
            return MatchCueRole.None;
        }

        return striking == cardInstanceId ? MatchCueRole.Striking
            : overlay.IsStruck(cardInstanceId) ? MatchCueRole.Struck
            : MatchCueRole.None;
    }

    // The backs a draw is dealing into the strip, which are the newest ones in it: the opponent's
    // held cards have no identity to pick them out by, so the only thing that says which of them
    // have just arrived is how many the draw dealt. An ordinary draw deals one and the newest back
    // is it; the deal that opens a game deals a whole hand and every back in the strip is arriving.
    // A strip already showing its full width has nowhere to put another and shows none arriving.
    public static MatchCueRole ArrivingBack(MatchEventCueView? cue, int index, int shown) =>
        cue?.Kind == MatchAnimationKindView.Draw && index >= shown - cue.Amount
            ? MatchCueRole.Arriving
            : MatchCueRole.None;

    // Whether a cue is one that can carry a card somewhere, and names the card it is about: a
    // Blokemon put down, a Kit played out, a promotion, and the Blokemon chosen to open the game.
    //
    // It is not the kind on its own, because one kind says two things. The battle beginning and the
    // Blokemon chosen to stand in the Oche are both Setup, and only the second of them is about a
    // card - so the question is whether the cue names the card it is about, asked here with every
    // other question about a card the presentation says is moving, rather than answered by a list
    // of kinds written out separately wherever a journey is drawn.
    //
    // It is as far as a cue can be asked, and it is not the whole answer. Using the ability printed
    // on a card that is already on the table is the same kind and names the same sort of card, and
    // moves nothing at all. Whether a card the cue names went anywhere is settled against the
    // tables the command sits between, in MatchPresentationTimeline, and the card it says was
    // picked up is what everything downstream of it draws.
    public static bool CarriesACard(MatchEventCueView? cue) =>
        cue?.SourceCardInstanceId is not null
        && cue.Kind
            is MatchAnimationKindView.Play
                or MatchAnimationKindView.Evolve
                or MatchAnimationKindView.Setup;

    // What the whole table is doing, said as the thing happening and the half of it doing so.
    public static MatchTableCue? Table(MatchEventCueView? cue) =>
        cue is null ? null : new(cue.Kind, Actor(cue));

    // Whether a cue reaches one half of the table's own furniture. A cue belongs to the half doing
    // it: only that Deck shuffles, and only that hand is dealt into. This is the whole of that
    // scoping, and dropping it is what one player's shuffle bouncing both Decks looks like.
    public static bool ActingOn(
        MatchEventCueView? cue,
        MatchAnimationKindView kind,
        bool opponent
    ) => cue?.Kind == kind && cue.ActorIsLocalPlayer == !opponent;

    // Whether this place on the table is the one expecting the card being played, and where inside
    // it that card comes to rest.
    public static MatchLandingPlacement? Landing(
        MatchLandingSlot? landing,
        MatchLandingKind kind,
        int index
    )
    {
        if (
            landing is null
            || landing.Kind != kind
            || (kind == MatchLandingKind.Bench && landing.BenchIndex != index)
        )
        {
            return null;
        }

        // Every place a card can be played to is named. A last arm reading "anywhere else lands in
        // the middle" is right about the Booth and about a card put into play, and would go on
        // being right about a fourth place that wanted an end of the table - silently, and only on
        // screen. The suppression below is for the other half of the same warning: a value cast in
        // from outside the names the enum declares, which cannot reach here - the kind is the one
        // the landing itself carries, checked equal above.
#pragma warning disable CS8524
        return kind switch
        {
            MatchLandingKind.Active when landing.Opponent => MatchLandingPlacement.Bottom,
            MatchLandingKind.Active => MatchLandingPlacement.Top,
            MatchLandingKind.Bench => MatchLandingPlacement.Centre,
            MatchLandingKind.InPlay => MatchLandingPlacement.Centre,
        };
#pragma warning restore CS8524
    }

    // Which pile a card of a shuffling Deck belongs to. They alternate, so that dealing them in the
    // order they are written crosses the two sides one card at a time instead of one whole side and
    // then the other.
    public static MatchRifflePile RifflePile(int index) =>
        index % 2 == 0 ? MatchRifflePile.Left : MatchRifflePile.Right;

    private static MatchCueRole Named(MatchEventCueView? cue, string cardInstanceId)
    {
        if (cue is null)
        {
            return MatchCueRole.None;
        }

        var role = MatchCueRole.None;
        if (cue.SourceCardInstanceId == cardInstanceId)
        {
            role |= MatchCueRole.Source;
        }

        if (cue.TargetCardInstanceIds.Contains(cardInstanceId, StringComparer.Ordinal))
        {
            role |= MatchCueRole.Target;
        }

        return role;
    }

    private static MatchCueRole Departed(MatchPresentationOverlay overlay, string cardInstanceId) =>
        overlay.IsGone(cardInstanceId) ? MatchCueRole.Gone : MatchCueRole.None;

    private static MatchCueActor Actor(MatchEventCueView cue) =>
        cue.ActorIsLocalPlayer switch
        {
            true => MatchCueActor.Local,
            false => MatchCueActor.Opponent,
            null => MatchCueActor.Unattributed,
        };
}
