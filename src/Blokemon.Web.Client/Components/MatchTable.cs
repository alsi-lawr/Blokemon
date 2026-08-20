using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// What the table itself draws, and therefore what a question can be asked of. The page glows
// these cards and routes taps to them, so anything asked about one of them is asked of the
// table rather than of a surface put in front of it.
public static class MatchTable
{
    public static IEnumerable<MatchCardInstanceView> Shown(MatchFrameView frame) =>
        Shown(frame.Player).Concat(Shown(frame.Opponent));

    public static IEnumerable<MatchCardInstanceView> Shown(MatchSideView side) =>
        (side.Active is { } active ? new[] { active }.Concat(side.Bench) : side.Bench)
            .Concat(side.Hand)
            .Concat(side.InPlayKits);

    // The card a held press asks to be shown. A press names either a card the table is drawing or
    // one of the cards attached to it, and the two cannot shadow one another: a card standing on
    // the table answers to its own instance wherever it sits in the search, and an attached card
    // only ever answers to the name its own host's fan gave it.
    public static CardView? Pressed(IEnumerable<MatchCardInstanceView> cards, string pressed)
    {
        CardView? attached = null;
        foreach (var card in cards)
        {
            if (string.Equals(card.Id, pressed, StringComparison.Ordinal))
            {
                return card.Card;
            }

            attached ??= MatchAttachedCards.Find(card, pressed);
        }

        return attached;
    }

    // Whether a step of a question is asked of the table: any card it would accept as an answer
    // is standing on the table or held in hand, glowing, waiting to be tapped. A question asked
    // of the table cannot be printed over it, because what it is asking for is underneath.
    public static bool AsksTheTable(MatchFrameView frame, MatchChoiceRequirementView requirement)
    {
        var shown = Shown(frame).Select(static card => card.Id).ToHashSet(StringComparer.Ordinal);
        return requirement
            .EligibleCards.Concat(requirement.EligibleTargets)
            .Any(card => shown.Contains(card.Id));
    }
}
