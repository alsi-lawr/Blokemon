using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// Where on the table a card being played is about to come to rest. The travelling card is aimed
// at it and it takes the landing, so the place a card ends up is a thing the page knows before
// the card gets there rather than something the table discovers when the frame changes.
public enum MatchLandingKind
{
    Active,
    Bench,
    InPlay,
}

public sealed record MatchLandingSlot(bool Opponent, MatchLandingKind Kind, int BenchIndex);

// What a cue has already made true while the frame behind it is still the one from before the
// command. The engine hands the application one state per command, so a change a cue announces -
// damage landing on a card, the place a played card is heading for - has no frame of its own to
// live in: it is carried here as a delta against the frame on screen and asked for by the
// presenters as they draw. Nothing here is ever poked into the page after a render, because the
// next render would paint over it; every frame change drops the deltas, because a delta only
// means anything against the frame it was measured from.
public sealed record MatchPresentationOverlay(
    IReadOnlyDictionary<string, int> DamageDeltas,
    MatchLandingSlot? Landing
)
{
    public static readonly MatchPresentationOverlay Empty = new(
        new Dictionary<string, int>(StringComparer.Ordinal),
        null
    );

    // The damage the table shows for a card: what the frame has, plus whatever the cues since
    // that frame have already announced.
    public int Damage(MatchCardInstanceView instance) =>
        Math.Max(
            0,
            instance.Damage + (DamageDeltas.TryGetValue(instance.Id, out var delta) ? delta : 0)
        );

    public MatchPresentationOverlay WithDamage(IEnumerable<string> cardInstanceIds, int amount)
    {
        if (amount == 0)
        {
            return this;
        }

        var deltas = new Dictionary<string, int>(DamageDeltas, StringComparer.Ordinal);
        foreach (var cardInstanceId in cardInstanceIds)
        {
            deltas[cardInstanceId] =
                (deltas.TryGetValue(cardInstanceId, out var delta) ? delta : 0) + amount;
        }

        return this with
        {
            DamageDeltas = deltas,
        };
    }

    public MatchPresentationOverlay LandingOn(MatchLandingSlot? landing) =>
        this with
        {
            Landing = landing,
        };

    // The landing only belongs to the side it is on: the other half of the table is asked the
    // same question every render and always hears no.
    public MatchLandingSlot? LandingFor(bool opponent) =>
        Landing is { } landing && landing.Opponent == opponent ? landing : null;
}
