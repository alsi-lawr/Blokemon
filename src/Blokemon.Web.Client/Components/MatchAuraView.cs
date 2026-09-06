namespace Blokemon.Web.Client.Components;

// The places on the table that are not cards and can still be where a card goes. Each is a
// fixed part of the table that the physical game would put the card on: the Bench positions, the
// empty Active position, the place a Kit in play stands in, and the Empties Tray.
[Flags]
public enum MatchTargetPlaces
{
    None = 0,
    Bench = 1,
    Active = 2,
    InPlay = 4,
    Empties = 8,
}

// Which cards glow on this frame, which of those are already chosen, where the chosen card can
// go, which cards a drag may pick up, whether the Deck is a place that can be tapped, whether the
// rest of the table is dimmed behind a card that has been picked up, and what counter each card
// carries. The match page derives it from the stage every render and hands it down; the
// presenters only ask it questions.
public sealed record MatchAuraView(
    string[] Cards,
    string[] Selected,
    string[] Targets,
    string[] Draggable,
    MatchTargetPlaces Places,
    bool Deck,
    bool Focused,
    IReadOnlyDictionary<string, int> Counters
)
{
    public static MatchAuraView Rest(IReadOnlyDictionary<string, int> counters) =>
        new([], [], [], [], MatchTargetPlaces.None, false, false, counters);

    public bool IsAura(string cardInstanceId) =>
        Cards.Contains(cardInstanceId, StringComparer.Ordinal);

    public bool IsSelected(string cardInstanceId) =>
        Selected.Contains(cardInstanceId, StringComparer.Ordinal);

    // A card the picked-up card can be put on. It glows with the target corona rather than the
    // playable one, so it reads as somewhere to put a card rather than a card to play.
    public bool IsTarget(string cardInstanceId) =>
        Targets.Contains(cardInstanceId, StringComparer.Ordinal);

    public bool IsDraggable(string cardInstanceId) =>
        Draggable.Contains(cardInstanceId, StringComparer.Ordinal);

    public bool Bench => Places.HasFlag(MatchTargetPlaces.Bench);

    public bool Active => Places.HasFlag(MatchTargetPlaces.Active);

    public bool InPlay => Places.HasFlag(MatchTargetPlaces.InPlay);

    public bool Empties => Places.HasFlag(MatchTargetPlaces.Empties);

    // Whether anything at all is lit, which is whether the table is asking the player something.
    public bool Any =>
        Cards.Length > 0
        || Selected.Length > 0
        || Targets.Length > 0
        || Places != MatchTargetPlaces.None
        || Deck;

    public int? Counter(string cardInstanceId) =>
        Counters.TryGetValue(cardInstanceId, out var counter) && counter > 0 ? counter : null;

    // A card that can be chosen is a toggle; a card that cannot is not, and says nothing.
    public string? Pressed(string cardInstanceId) =>
        IsSelected(cardInstanceId) ? "true"
        : IsAura(cardInstanceId) || IsTarget(cardInstanceId) ? "false"
        : null;
}
