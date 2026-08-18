namespace Blokemon.Web.Client.Components;

// Which cards glow on this frame, which of those are already chosen, whether the Bench is a
// destination, and what counter each carries. The match page derives it from the stage every
// render and hands it down; the presenters only ask it questions.
public sealed record MatchAuraView(
    string[] Cards,
    string[] Selected,
    bool Bench,
    IReadOnlyDictionary<string, int> Counters
)
{
    public bool IsAura(string cardInstanceId) =>
        Cards.Contains(cardInstanceId, StringComparer.Ordinal);

    public bool IsSelected(string cardInstanceId) =>
        Selected.Contains(cardInstanceId, StringComparer.Ordinal);

    public int? Counter(string cardInstanceId) =>
        Counters.TryGetValue(cardInstanceId, out var counter) && counter > 0 ? counter : null;

    // A card that can be chosen is a toggle; a card that cannot is not, and says nothing.
    public string? Pressed(string cardInstanceId) =>
        IsSelected(cardInstanceId) ? "true"
        : IsAura(cardInstanceId) ? "false"
        : null;
}
