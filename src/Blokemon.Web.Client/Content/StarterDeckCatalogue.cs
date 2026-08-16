using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.Core.SetDesign;

namespace Blokemon.Web.Content;

public sealed record StarterDeckEntry(string CardId, int Quantity);

public sealed record StarterDeck(
    string Id,
    Guid SavedDeckId,
    string Name,
    string Type,
    string Role,
    string Description,
    string LeaderCardId,
    IReadOnlyList<StarterDeckEntry> Entries
)
{
    public int CardCount => Entries.Sum(static entry => entry.Quantity);

    public string[] ExpandedCardIds =>
        Entries
            .OrderBy(static entry => entry.CardId, StringComparer.Ordinal)
            .SelectMany(static entry => Enumerable.Repeat(entry.CardId, entry.Quantity))
            .ToArray();
}

public sealed class StarterDeckCatalogue
{
    private const int _schemaVersion = 1;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IReadOnlyDictionary<string, StarterDeck> _decks;

    private StarterDeckCatalogue(string version, IReadOnlyDictionary<string, StarterDeck> decks)
    {
        Version = version;
        _decks = decks;
    }

    public string Version { get; }

    public IReadOnlyCollection<StarterDeck> Decks => [.. _decks.Values];

    public StarterDeck Deck(string id) =>
        _decks.TryGetValue(id, out var deck)
            ? deck
            : throw new InvalidDataException($"The starter authority does not contain deck {id}.");

    public StarterDeck? Find(string id) => _decks.GetValueOrDefault(id);

    public StarterDeck OpponentFor(string? selectedStarterId) =>
        selectedStarterId switch
        {
            "growroom" => Deck("brick-lane-heat"),
            "brick-lane-heat" => Deck("early-shift"),
            "early-shift" => Deck("growroom"),
            _ => Deck("brick-lane-heat"),
        };

    public static StarterDeckCatalogue LoadJson(string json, BlokemonRuntimeManifest mechanics)
    {
        var document = JsonSerializer.Deserialize<StarterDeckDocument>(json, _json);
        if (document is null)
        {
            throw Invalid("The document is empty.");
        }
        if (document.SchemaVersion != _schemaVersion)
        {
            throw Invalid($"Schema version {document.SchemaVersion} is not supported.");
        }
        if (
            !string.Equals(
                document.MechanicalManifestVersion,
                mechanics.ManifestVersion,
                StringComparison.Ordinal
            )
        )
        {
            throw Invalid("The mechanical manifest version does not match the current authority.");
        }
        if (string.IsNullOrWhiteSpace(document.StarterDeckVersion))
        {
            throw Invalid("The starter deck version is required.");
        }
        if (document.Decks is not { Length: 3 })
        {
            throw Invalid("Exactly three starter decks are required.");
        }

        var knownCards = mechanics
            .Collectibles.Select(card => new KnownCard(
                card.Id,
                KnownCardKind.Blokemon,
                card.StackCopyLimit,
                card.Rank == BlokemonRank.Regular,
                card.PromotesFromId,
                card.Attacks
            ))
            .Concat(
                mechanics.Kits.Select(card => new KnownCard(
                    card.Id,
                    KnownCardKind.Trainer,
                    card.StackCopyLimit,
                    false,
                    null,
                    []
                ))
            )
            .Concat(
                mechanics.BasicVim.Select(card => new KnownCard(
                    card.Id,
                    KnownCardKind.Energy,
                    card.StackCopyLimit,
                    false,
                    null,
                    []
                ))
            )
            .ToDictionary(static card => card.Id, StringComparer.Ordinal);
        var energyTypes = mechanics.BasicVim.ToDictionary(
            static card => card.Id,
            static card => card.MechanicalType,
            StringComparer.Ordinal
        );
        var decks = new Dictionary<string, StarterDeck>(StringComparer.Ordinal);
        var savedDeckIds = new HashSet<Guid>();

        foreach (var source in document.Decks)
        {
            var deck = Validate(source, knownCards, energyTypes);
            if (!decks.TryAdd(deck.Id, deck))
            {
                throw Invalid($"Starter deck ID {deck.Id} is duplicated.");
            }
            if (!savedDeckIds.Add(deck.SavedDeckId))
            {
                throw Invalid($"Saved deck ID {deck.SavedDeckId:D} is duplicated.");
            }
        }

        return new(document.StarterDeckVersion, decks);
    }

    private static StarterDeck Validate(
        StarterDeckSource source,
        IReadOnlyDictionary<string, KnownCard> knownCards,
        IReadOnlyDictionary<string, BlokemonMechanicalType> energyTypes
    )
    {
        if (
            string.IsNullOrWhiteSpace(source.Id)
            || source.SavedDeckId == Guid.Empty
            || string.IsNullOrWhiteSpace(source.Name)
            || string.IsNullOrWhiteSpace(source.Type)
            || string.IsNullOrWhiteSpace(source.Role)
            || string.IsNullOrWhiteSpace(source.Description)
            || string.IsNullOrWhiteSpace(source.LeaderCardId)
        )
        {
            throw Invalid("Every starter deck requires complete presentation metadata.");
        }
        if (source.Entries is null || source.Entries.Length == 0)
        {
            throw Invalid($"Starter deck {source.Id} has no cards.");
        }

        var entries = new List<StarterDeckEntry>(source.Entries.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in source.Entries)
        {
            if (
                string.IsNullOrWhiteSpace(entry.CardId)
                || !knownCards.TryGetValue(entry.CardId, out var known)
            )
            {
                throw Invalid($"Starter deck {source.Id} contains unknown card {entry.CardId}.");
            }
            if (!seen.Add(entry.CardId))
            {
                throw Invalid($"Starter deck {source.Id} repeats entry {entry.CardId}.");
            }
            if (entry.Quantity <= 0 || entry.Quantity > known.CopyLimit)
            {
                throw Invalid(
                    $"Starter deck {source.Id} has an invalid quantity for {entry.CardId}."
                );
            }
            entries.Add(new(entry.CardId, entry.Quantity));
        }

        if (entries.Sum(static entry => entry.Quantity) != 60)
        {
            throw Invalid($"Starter deck {source.Id} must contain exactly 60 cards.");
        }
        if (
            entries
                .Where(entry => knownCards[entry.CardId].Kind == KnownCardKind.Energy)
                .Sum(static entry => entry.Quantity) != 15
        )
        {
            throw Invalid($"Starter deck {source.Id} must contain exactly 15 Basic Energy.");
        }
        if (
            !entries.Any(entry =>
                knownCards[entry.CardId] is { Kind: KnownCardKind.Blokemon, IsRegular: true }
            )
        )
        {
            throw Invalid($"Starter deck {source.Id} needs a Regular Blokemon.");
        }
        if (
            !knownCards.TryGetValue(source.LeaderCardId, out var leader)
            || leader.Kind != KnownCardKind.Blokemon
            || !seen.Contains(source.LeaderCardId)
        )
        {
            throw Invalid($"Starter deck {source.Id} has an invalid leader.");
        }

        foreach (var entry in entries)
        {
            var parent = knownCards[entry.CardId].PromotesFromId;
            if (parent is not null && !seen.Contains(parent))
            {
                throw Invalid(
                    $"Starter deck {source.Id} contains {entry.CardId} without {parent}."
                );
            }
        }

        var availableEnergy = entries
            .Where(entry => energyTypes.ContainsKey(entry.CardId))
            .Select(entry => energyTypes[entry.CardId])
            .ToHashSet();
        var hasPayableAttack = entries
            .Select(entry => knownCards[entry.CardId])
            .Where(static card => card.Kind == KnownCardKind.Blokemon)
            .SelectMany(static card => card.Attacks)
            .Any(attack =>
                attack.VimCost.All(cost =>
                    cost == BlokemonMechanicalType.Colorless || availableEnergy.Contains(cost)
                )
            );
        if (!hasPayableAttack)
        {
            throw Invalid($"Starter deck {source.Id} cannot pay for any attack.");
        }

        return new(
            source.Id,
            source.SavedDeckId,
            source.Name,
            source.Type,
            source.Role,
            source.Description,
            source.LeaderCardId,
            entries
        );
    }

    private static InvalidDataException Invalid(string message) =>
        new($"The starter-deck authority is invalid: {message}");

    private enum KnownCardKind
    {
        Blokemon,
        Trainer,
        Energy,
    }

    private sealed record KnownCard(
        string Id,
        KnownCardKind Kind,
        int CopyLimit,
        bool IsRegular,
        string? PromotesFromId,
        BlokemonAttack[] Attacks
    );

    private sealed record StarterDeckDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string StarterDeckVersion,
        [property: JsonRequired] string MechanicalManifestVersion,
        [property: JsonRequired] StarterDeckSource[] Decks
    );

    private sealed record StarterDeckSource(
        [property: JsonRequired] string Id,
        [property: JsonRequired] Guid SavedDeckId,
        [property: JsonRequired] string Name,
        [property: JsonRequired] string Type,
        [property: JsonRequired] string Role,
        [property: JsonRequired] string Description,
        [property: JsonRequired] string LeaderCardId,
        [property: JsonRequired] StarterDeckEntrySource[] Entries
    );

    private sealed record StarterDeckEntrySource(
        [property: JsonRequired] string CardId,
        [property: JsonRequired] int Quantity
    );
}
