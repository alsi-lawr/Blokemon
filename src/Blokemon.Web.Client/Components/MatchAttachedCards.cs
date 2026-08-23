using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// One attached card as the table draws it. Depth is how far back in its fan the face sits: the card
// attached first is nearest its host and lies over the rest, and each card attached after it sits
// one step further out and one step further behind.
public sealed record MatchAttachedCardView(CardView Card, int Depth);

// The cards hanging off a card in play, arranged into the fans the table draws.
public static class MatchAttachedCards
{
    // The Energy fan back to front, so the face nearest the host is the last one drawn and
    // therefore the one lying over the rest of the fan.
    public static IReadOnlyList<MatchAttachedCardView> Energy(MatchCardInstanceView host) =>
        [.. Fan(host.AttachedEnergy).Reverse()];

    // Tools are drawn in the order they were attached, which is how they have always been drawn:
    // they hang in front of their host on the other side and have never stacked.
    public static IReadOnlyList<MatchAttachedCardView> Tools(MatchCardInstanceView host) =>
        Fan(host.AttachedTools);

    // What the counter says. It is asked of the same card the fan is drawn from, so the number in
    // the pill and the faces in the fan are two readings of one fact rather than two facts.
    public static int EnergyCount(MatchCardInstanceView host) => host.AttachedEnergy.Length;

    // Both fans as one list, which is what a host offers to be read: the faces are drawn in two
    // places on the card, but every one of them is a card hanging off this one and the player
    // reaching for them is reaching for the same thing either time.
    public static IReadOnlyList<MatchAttachedCardView> All(MatchCardInstanceView host) =>
        [.. Energy(host), .. Tools(host)];

    private static MatchAttachedCardView[] Fan(CardView[] attached) =>
        [.. attached.Select((card, depth) => new MatchAttachedCardView(card, depth))];
}
