using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.App.Contracts;
using Blokemon.Core.SetDesign;

namespace Blokemon.App.Catalogue;

public sealed record CatalogueEffect(string Id, string Name, string? Text);

public sealed class BlokemonCatalogue
{
    private const int _bootstrapSchemaVersion = 2;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IReadOnlyDictionary<string, CardView> _cards;
    private readonly IReadOnlyDictionary<string, CatalogueEffect> _effects;
    private readonly string _mechanicsJson;
    private readonly string _starterDecksJson;

    private BlokemonCatalogue(
        string mechanicsJson,
        string starterDecksJson,
        BlokemonRuntimeManifest mechanics,
        string publicContentVersion,
        StarterDeckCatalogue starterDecks,
        string cardStylesheet,
        string reverseFaceHtml,
        PackPresentationView packPresentation,
        IReadOnlyDictionary<string, CardView> cards,
        IReadOnlyDictionary<string, CatalogueEffect> effects
    )
    {
        _mechanicsJson = mechanicsJson;
        _starterDecksJson = starterDecksJson;
        Mechanics = mechanics;
        PublicContentVersion = publicContentVersion;
        StarterDecks = starterDecks;
        CardStylesheet = cardStylesheet;
        ReverseFaceHtml = reverseFaceHtml;
        PackPresentation = packPresentation;
        _cards = cards;
        _effects = effects;
    }

    public BlokemonRuntimeManifest Mechanics { get; }

    public string PublicContentVersion { get; }

    public StarterDeckCatalogue StarterDecks { get; }

    public string CardStylesheet { get; }

    public string ReverseFaceHtml { get; }

    public PackPresentationView PackPresentation { get; }

    public string StarterRegularId =>
        Mechanics
            .Collectibles.Where(static card => card.Rank == BlokemonRank.Regular)
            .MinBy(static card => card.Id, StringComparer.Ordinal)!
            .Id;

    public IReadOnlyCollection<CardView> Cards => [.. _cards.Values];

    public CardView Card(string id) =>
        _cards.TryGetValue(id, out var card)
            ? card
            : throw new InvalidDataException($"The authority does not contain card {id}.");

    public string EffectName(string id) =>
        _effects.TryGetValue(id, out var effect) ? effect.Name : id;

    public string? EffectText(string id) =>
        _effects.TryGetValue(id, out var effect) ? effect.Text : null;

    public int StayingPower(string id) =>
        Mechanics.Collectibles.FirstOrDefault(card => card.Id == id)?.StayingPower
        ?? (
            Mechanics.BaseRules.FossilKits.KitIds.Contains(id, StringComparer.Ordinal)
                ? Mechanics.BaseRules.FossilKits.PlayAsRegularLocalStayingPower
                : 0
        );

    public CardView[] CardsWithOwnership(IReadOnlyDictionary<string, int> ownership) =>
        _cards
            .Values.Select(card =>
                card with
                {
                    OwnedQuantity = ownership.GetValueOrDefault(card.Id),
                }
            )
            .OrderBy(static card => card.Kind)
            .ThenBy(static card => card.Id, StringComparer.Ordinal)
            .ToArray();

    public string ToBootstrapJson() =>
        JsonSerializer.Serialize(
            new CatalogueBootstrap(
                _bootstrapSchemaVersion,
                _mechanicsJson,
                _starterDecksJson,
                PublicContentVersion,
                CardStylesheet,
                ReverseFaceHtml,
                PackPresentation,
                [.. _cards.Values.OrderBy(static card => card.Id, StringComparer.Ordinal)],
                [.. _effects.Values.OrderBy(static effect => effect.Id, StringComparer.Ordinal)]
            ),
            _json
        );

    public static BlokemonCatalogue FromBootstrapJson(string json)
    {
        CatalogueBootstrap? bootstrap;
        try
        {
            bootstrap = JsonSerializer.Deserialize<CatalogueBootstrap>(json, _json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The browser card catalogue is damaged.", exception);
        }

        if (bootstrap is null || bootstrap.SchemaVersion != _bootstrapSchemaVersion)
        {
            throw new InvalidDataException(
                "The browser card catalogue is not compatible with this version of Blokemon."
            );
        }

        return Create(
            bootstrap.MechanicsJson,
            bootstrap.StarterDecksJson,
            bootstrap.PublicContentVersion,
            bootstrap.CardStylesheet,
            bootstrap.ReverseFaceHtml,
            bootstrap.PackPresentation,
            bootstrap.Cards,
            bootstrap.Effects
        );
    }

    public static BlokemonCatalogue Create(
        string mechanicsJson,
        string starterDecksJson,
        string publicContentVersion,
        string cardStylesheet,
        string reverseFaceHtml,
        PackPresentationView packPresentation,
        IEnumerable<CardView> cards,
        IEnumerable<CatalogueEffect> effects
    )
    {
        var mechanics = BlokemonSetJson.RuntimeManifest(mechanicsJson);
        var mechanicsValidation = BlokemonSetValidator.ValidateRuntime(mechanics);
        if (!mechanicsValidation.IsValid)
        {
            throw new InvalidDataException(
                $"The mechanical authority is invalid: {mechanicsValidation.Issues[0].Message}"
            );
        }

        if (string.IsNullOrWhiteSpace(publicContentVersion))
        {
            throw new InvalidDataException("The public card-content version is missing.");
        }

        var starterDecks = StarterDeckCatalogue.LoadJson(starterDecksJson, mechanics);
        var cardMap = cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);
        var expectedCardIds = mechanics
            .Collectibles.Select(static card => card.Id)
            .Concat(mechanics.Kits.Select(static card => card.Id))
            .Concat(mechanics.BasicVim.Select(static card => card.Id))
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedCardIds.SetEquals(cardMap.Keys))
        {
            throw new InvalidDataException(
                "The browser card catalogue does not match the mechanical authority."
            );
        }

        if (string.IsNullOrWhiteSpace(cardStylesheet) || string.IsNullOrWhiteSpace(reverseFaceHtml))
        {
            throw new InvalidDataException("The browser card presentation is incomplete.");
        }

        var effectMap = effects.ToDictionary(static effect => effect.Id, StringComparer.Ordinal);
        return new(
            mechanicsJson,
            starterDecksJson,
            mechanics,
            publicContentVersion,
            starterDecks,
            cardStylesheet,
            reverseFaceHtml,
            packPresentation,
            cardMap,
            effectMap
        );
    }

    private sealed record CatalogueBootstrap(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string MechanicsJson,
        [property: JsonRequired] string StarterDecksJson,
        [property: JsonRequired] string PublicContentVersion,
        [property: JsonRequired] string CardStylesheet,
        [property: JsonRequired] string ReverseFaceHtml,
        [property: JsonRequired] PackPresentationView PackPresentation,
        [property: JsonRequired] CardView[] Cards,
        [property: JsonRequired] CatalogueEffect[] Effects
    );
}
