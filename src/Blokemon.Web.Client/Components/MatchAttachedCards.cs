using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// Which fan a face hangs in. The two hang off opposite sides of their host and are read for
// different reasons, so the name a press answers to says which fan it landed in as well as how far
// into it.
public enum MatchAttachedCardKind
{
    Energy,
    Tool,
}

// One attached card as the table draws it. Depth is how far back in its fan the face sits: the card
// attached first is nearest its host and lies over the rest, and each card attached after it sits
// one step further out and one step further behind.
public sealed record MatchAttachedCardView(string ViewKey, CardView Card, int Depth);

// The cards hanging off a card in play, and the names a press on one of them answers to.
//
// An attached card has no instance of its own to be named by: it reaches the table as a card face
// belonging to its host rather than as a card standing on the table. So the name it is pressed by
// is made here, out of the host it hangs off and the place it holds in that host's fan, and it is
// made the same way whether the fan is being drawn or a press is being answered.
public static class MatchAttachedCards
{
    // The Energy fan back to front, so the face nearest the host is the last one drawn and
    // therefore the one lying over the rest of the fan.
    public static IReadOnlyList<MatchAttachedCardView> Energy(MatchCardInstanceView host) =>
        [.. Fan(host, MatchAttachedCardKind.Energy, host.AttachedEnergy).Reverse()];

    // Tools are drawn in the order they were attached, which is how they have always been drawn:
    // they hang in front of their host on the other side and have never stacked.
    public static IReadOnlyList<MatchAttachedCardView> Tools(MatchCardInstanceView host) =>
        Fan(host, MatchAttachedCardKind.Tool, host.AttachedTools);

    // What the counter says. It is asked of the same card the fan is drawn from, so the number in
    // the pill and the faces in the fan are two readings of one fact rather than two facts.
    public static int EnergyCount(MatchCardInstanceView host) => host.AttachedEnergy.Length;

    // Both fans as one list, which is what a host offers to be read: the faces are drawn in two
    // places on the card, but every one of them is a card hanging off this one and the player
    // reaching for them is reaching for the same thing either time.
    public static IReadOnlyList<MatchAttachedCardView> All(MatchCardInstanceView host) =>
        [.. Energy(host), .. Tools(host)];

    // The card a name belongs to, or nothing when the name is not one of this host's own.
    public static CardView? Find(MatchCardInstanceView host, string viewKey) =>
        Found(host, MatchAttachedCardKind.Energy, host.AttachedEnergy, viewKey)
        ?? Found(host, MatchAttachedCardKind.Tool, host.AttachedTools, viewKey);

    private static MatchAttachedCardView[] Fan(
        MatchCardInstanceView host,
        MatchAttachedCardKind kind,
        CardView[] attached
    ) =>
        [
            .. attached.Select(
                (card, depth) => new MatchAttachedCardView(Key(host.Id, kind, depth), card, depth)
            ),
        ];

    private static CardView? Found(
        MatchCardInstanceView host,
        MatchAttachedCardKind kind,
        CardView[] attached,
        string viewKey
    )
    {
        for (var depth = 0; depth < attached.Length; depth++)
        {
            if (string.Equals(Key(host.Id, kind, depth), viewKey, StringComparison.Ordinal))
            {
                return attached[depth];
            }
        }

        return null;
    }

    // A card standing on the table is named by the match as C1-014, so a name written around the
    // fan its face hangs in cannot be mistaken for one of them however the two are looked up.
    private static string Key(string hostCardInstanceId, MatchAttachedCardKind kind, int depth) =>
        $"{hostCardInstanceId}#{kind}#{depth}";
}
