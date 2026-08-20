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
    MatchLandingSlot? Landing,
    // The card part way through throwing a blow. A blow is declared by one cue and lands on
    // another, with anything the engine does in between - a beer mat tossed to find out whether
    // it connects at all - happening while it is in the air, so which card is throwing it is a
    // thing that outlives a single cue: this is what carries it across, and what tells damage
    // that came out of an attack from damage a card simply did to another one.
    string? StrikingCardInstanceId = null,
    // And what it is aimed at, which is whatever the blow actually damages rather than whoever
    // happens to be standing opposite. Known from the declaration, because the whole exchange is
    // worked out before any of it is drawn, so the blow can be aimed before it is thrown.
    IReadOnlyList<string>? StruckCardInstanceIds = null,
    // Every card the presentation has already shown leaving somewhere. A card is carried out of a
    // hand, or knocked off the table, long before the frame behind it catches up - the engine
    // commits one state per command - so between the two there is a card that the frame still has
    // in a place the table has already emptied. It is named here for as long as that lasts, and
    // the presenters do not draw it there. Without it the concealment belongs to the cue that did
    // the carrying and evaporates on the next one, leaving a second copy of a card on the table.
    IReadOnlyList<string>? GoneCardInstanceIds = null,
    // The card the presentation has picked up, if it has picked one up. No cue can say: playing a
    // card out of the hand and using the ability printed on a card already standing on the table
    // are one kind between them, and both name the card they are about. Which of them actually
    // moves anything is settled against the two tables the command sits between, and the answer is
    // carried here for everything that draws a card being carried.
    string? CarriedCardInstanceId = null
)
{
    public static readonly MatchPresentationOverlay Empty = new(
        new Dictionary<string, int>(StringComparer.Ordinal),
        null
    );

    public bool IsGone(string cardInstanceId) =>
        GoneCardInstanceIds?.Contains(cardInstanceId, StringComparer.Ordinal) == true;

    public MatchPresentationOverlay Gone(IReadOnlyList<string> cardInstanceIds) =>
        this with
        {
            GoneCardInstanceIds = cardInstanceIds,
        };

    public bool IsStruck(string cardInstanceId) =>
        StruckCardInstanceIds?.Contains(cardInstanceId, StringComparer.Ordinal) == true;

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

    public MatchPresentationOverlay Blow(string? striking, IReadOnlyList<string>? struck) =>
        this with
        {
            StrikingCardInstanceId = striking,
            StruckCardInstanceIds = striking is null ? null : struck,
        };

    // The card this beat has picked up and the place it is being carried to. They are set together
    // because they are the same fact told twice: nothing is aimed anywhere unless something is
    // being carried, and a card that goes nowhere is not picked up in the first place.
    public MatchPresentationOverlay Carrying(string? cardInstanceId, MatchLandingSlot? landing) =>
        this with
        {
            CarriedCardInstanceId = cardInstanceId,
            Landing = landing,
        };

    // The landing only belongs to the side it is on: the other half of the table is asked the
    // same question every render and always hears no.
    public MatchLandingSlot? LandingFor(bool opponent) =>
        Landing is { } landing && landing.Opponent == opponent ? landing : null;
}
