using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;
using Blokemon.Web.Client.Api;

namespace Blokemon.Web.Content;

public sealed class BlokemonCatalogue
{
    private readonly IReadOnlyDictionary<string, CardView> _cards;
    private readonly IReadOnlyDictionary<string, BlokemonPublicEffect> _effects;

    private BlokemonCatalogue(
        BlokemonRuntimeManifest mechanics,
        BlokemonPublicContentManifest publicContent,
        IReadOnlyDictionary<string, CardView> cards,
        IReadOnlyDictionary<string, BlokemonPublicEffect> effects
    )
    {
        Mechanics = mechanics;
        PublicContent = publicContent;
        _cards = cards;
        _effects = effects;
    }

    public BlokemonRuntimeManifest Mechanics { get; }

    public BlokemonPublicContentManifest PublicContent { get; }

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
        _effects.TryGetValue(id, out var effect) ? effect.EffectText : null;

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

    public static BlokemonCatalogue Load(string contentRoot)
    {
        var authorityRoot = Path.Combine(contentRoot, "authorities");
        var mechanics = BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(Path.Combine(authorityRoot, "mechanics.json"))
        );
        var publicContent = BlokemonPublicContentJson.Manifest(
            File.ReadAllText(Path.Combine(authorityRoot, "public-content.json"))
        );
        var mechanicsValidation = BlokemonSetValidator.ValidateRuntime(mechanics);
        if (!mechanicsValidation.IsValid)
        {
            throw new InvalidDataException(
                $"The mechanical authority is invalid: {mechanicsValidation.Issues[0].Message}"
            );
        }
        var publicValidation = BlokemonPublicContentValidator.ValidateDocument(
            publicContent,
            mechanics
        );
        if (!publicValidation.IsValid)
        {
            throw new InvalidDataException(
                $"The public-content authority is invalid: {publicValidation.Issues[0].Message}"
            );
        }

        var publicCollectibles = publicContent.Collectibles.ToDictionary(static card => card.Id);
        var publicKits = publicContent.Supports.ToDictionary(static card => card.Id);
        var publicVim = publicContent.BasicEnergy.ToDictionary(static card =>
            $"VIM-{card.Id["ENERGY-".Length..]}"
        );
        var effects = publicContent
            .Collectibles.SelectMany(static card =>
                card.Abilities.Concat(card.Attacks).Concat(card.Rules)
            )
            .Concat(publicContent.Supports.SelectMany(static card => card.Effects))
            .ToDictionary(static effect => effect.MechanicalId, StringComparer.Ordinal);
        var art = ScanArt(Path.Combine(contentRoot, "art"));
        var cards = new Dictionary<string, CardView>(StringComparer.Ordinal);

        foreach (var mechanical in mechanics.Collectibles)
        {
            var presentation = publicCollectibles[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.ApprovedName,
                    CardKindView.Blokemon,
                    presentation.ApprovedType.ToString(),
                    $"{mechanical.ProductBucket} · {mechanical.Rank}",
                    $"/art/{art[mechanical.Id]}",
                    0,
                    false
                )
            );
        }
        foreach (var mechanical in mechanics.Kits)
        {
            var presentation = publicKits[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.Kit,
                    "Kit",
                    mechanical.Kind.ToString(),
                    $"/art/{art[mechanical.Id]}",
                    0,
                    mechanical.FreelyAvailable
                )
            );
        }
        foreach (var mechanical in mechanics.BasicVim)
        {
            var presentation = publicVim[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.BasicVim,
                    mechanical.MechanicalType.ToString(),
                    "Basic Vim",
                    $"/art/{presentation.SymbolKey}.svg",
                    0,
                    mechanical.FreelyAvailable
                )
            );
        }

        return new(mechanics, publicContent, cards, effects);
    }

    private static IReadOnlyDictionary<string, string> ScanArt(string artRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(artRoot, "*.svg"))
        {
            var fileName = Path.GetFileName(path);
            var id =
                fileName.StartsWith("BLK-", StringComparison.Ordinal) ? fileName[.."BLK-000".Length]
                : fileName.StartsWith("KIT-", StringComparison.Ordinal)
                    ? fileName[.."KIT-000".Length]
                : null;
            if (id is not null)
            {
                result.Add(id, fileName);
            }
        }
        return result;
    }
}
