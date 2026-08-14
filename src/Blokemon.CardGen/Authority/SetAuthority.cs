using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Blokemon.CardGen.Domain;
using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;

namespace Blokemon.CardGen.Authority;

/// <summary>Reads the set authorities into cards.</summary>
public static class SetAuthority
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the set authorities into the printed set.</summary>
    /// <param name="publicContentPath">The path to the public content authority.</param>
    /// <param name="mechanicsPath">The path to the mechanics authority.</param>
    /// <param name="printingManifestPath">The path to the printing authority.</param>
    /// <param name="artDirectory">The directory holding the illustrations.</param>
    /// <returns>The printed set.</returns>
    public static CardSet Load(
        string publicContentPath,
        string mechanicsPath,
        string printingManifestPath,
        string artDirectory
    )
    {
        var content = BlokemonPublicContentJson.Manifest(File.ReadAllText(publicContentPath));
        var mechanics = BlokemonSetJson.RuntimeManifest(File.ReadAllText(mechanicsPath));

        var label = mechanics.ApprovedMechanicalDisplayMap.ToDictionary(
            entry => entry.MechanicalType,
            entry => Enum.Parse<BlokemonType>(entry.ApprovedLabel.ToString())
        );
        var art = ArtIndex.Scan(artDirectory);
        var mechanical = mechanics.Collectibles.ToDictionary(collectible => collectible.Id);
        var names = content.Collectibles.ToDictionary(card => card.Id, card => card.ApprovedName);
        var supportNames = content.Supports.ToDictionary(
            support => support.Id,
            support => support.Name
        );
        var category = content.Terminology.ToDictionary(term => term.Id, term => term.Singular);
        var profiles = LoadProfiles();
        var printing = LoadPrinting(printingManifestPath);
        var supportNumbers = Number(
            content
                .Supports.OrderBy(support => support.Name, StringComparer.Ordinal)
                .Select(support => support.Id)
        );
        var energyNumbers = Number(
            content
                .BasicEnergy.OrderBy(energy => energy.Id, StringComparer.Ordinal)
                .Select(energy => energy.Id)
        );

        return new CardSet(
            [
                .. content.Collectibles.Select(card =>
                    ToBlokemon(
                        card,
                        mechanical[card.Id],
                        label,
                        art,
                        names,
                        supportNames,
                        profiles[card.Id],
                        printing.Numbers[card.Id],
                        printing.Holo.Contains(card.Id)
                    )
                ),
            ],
            [
                .. content.Supports.Select(support =>
                    ToSupport(
                        support,
                        category[support.CategoryTermId],
                        art,
                        supportNumbers[support.Id]
                    )
                ),
            ],
            [
                .. content.BasicEnergy.Select(energy =>
                    ToEnergy(energy, art, energyNumbers[energy.Id])
                ),
            ],
            ToReverse(art)
        );
    }

    private static IReadOnlyDictionary<string, CollectorNumber> Number(IEnumerable<string> ordered)
    {
        var run = ordered.ToImmutableArray();

        return run.Select((id, index) => (id, number: new CollectorNumber(index + 1, run.Length)))
            .ToDictionary(entry => entry.id, entry => entry.number);
    }

    private static PrintingIndex LoadPrinting(string printingManifestPath)
    {
        var printing =
            JsonSerializer.Deserialize<PrintingDocument>(
                File.ReadAllBytes(printingManifestPath),
                _json
            )
            ?? throw new InvalidDataException(
                $"Unreadable printing authority at {printingManifestPath}"
            );
        var total = printing.Collectibles.Length;

        return new PrintingIndex(
            printing.Collectibles.ToDictionary(
                row => row.Id,
                row => new CollectorNumber(Position(row.Id), total)
            ),
            printing
                .Collectibles.Where(IsHolo)
                .Select(row => row.Id)
                .ToHashSet(StringComparer.Ordinal)
        );
    }

    // Base Set's sixteen holo rares, plus Mew, whose only Gen 1 printing was a holo promo.
    private static bool IsHolo(PrintingRow row) =>
        row.Gen1Rarity is "Promo" || (row.Gen1Set is "Base Set" && row.Gen1Rarity is "Rare Holo");

    private static int Position(string runtimeId) =>
        int.Parse(runtimeId.AsSpan(runtimeId.LastIndexOf('-') + 1), CultureInfo.InvariantCulture);

    private static Rarity ToRarity(BlokemonProductBucket productBucket, bool holo, CardId id)
    {
        var printed = Enum.Parse<Rarity>(productBucket.ToString());

        if (!holo)
        {
            return printed;
        }

        // Holo sits above Rare, so a holo card must not sit in a lesser bucket.
        return printed is Rarity.Rare
            ? Rarity.RareHolo
            : throw new InvalidDataException($"{id} is holo but its product bucket is {printed}");
    }

    private static ICard ToBlokemon(
        BlokemonPublicCollectible published,
        BlokemonCollectible mechanical,
        IReadOnlyDictionary<BlokemonMechanicalType, BlokemonType> label,
        ArtIndex art,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> supportNames,
        CardProfile profile,
        CollectorNumber number,
        bool holo
    )
    {
        var type = Enum.Parse<BlokemonType>(published.ApprovedType.ToString());
        var stage = ToStage(mechanical.Rank);
        var previous = ToPrevious(mechanical.PromotesFromId, art, names, supportNames);
        var prizes = new PrizeCards(mechanical.BarChitsWhenSentHome);
        ImmutableArray<CardRegion> lineage = previous is null
            ? []
            : [new CardRegion.Lineage(previous)];

        return Themed(
            type,
            new CardId(published.Id),
            [
                new CardRegion.PrintedField(),
                .. lineage,
                new CardRegion.Nameplate(
                    stage.ClassificationLabel(previous is not null),
                    published.ApprovedName
                ),
                new CardRegion.Vitality(new HitPoints(mechanical.StayingPower), type),
                new CardRegion.Illustration(
                    art.For(published.Id, published.Illustration.AltIntent),
                    IllustrationPlacement.Framed
                ),
                new CardRegion.IdentityStrip(profile.PrintedIdentity()),
                new CardRegion.Mechanics(ToEntries(published, mechanical, label)),
                new CardRegion.Affinities(
                    ToAffinity(mechanical.SoftSpots, label),
                    ToAffinity(mechanical.StubbornStreaks, label),
                    new RetreatCost(mechanical.TaxiFare)
                ),
                new CardRegion.Colophon(
                    published.FlavourText,
                    $"{published.Id} · {prizes.PrintedLabel()}",
                    ToRarity(mechanical.ProductBucket, holo, new CardId(published.Id)),
                    number
                ),
            ]
        );
    }

    private static IReadOnlyDictionary<string, CardProfile> LoadProfiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "blokemon-profiles.json");
        var entries =
            JsonSerializer.Deserialize<ImmutableDictionary<string, ProfileEntry>>(
                File.ReadAllBytes(path),
                _json
            ) ?? throw new InvalidDataException($"Unreadable profiles at {path}");

        return entries.ToDictionary(
            entry => entry.Key,
            entry => new CardProfile(
                entry.Value.Subtype,
                new Stature(entry.Value.Feet, entry.Value.Inches, entry.Value.Pounds)
            )
        );
    }

    private static ICard ToSupport(
        BlokemonPublicSupport published,
        string category,
        ArtIndex art,
        CollectorNumber number
    ) =>
        new Card<SupportTheme>(
            new CardId(published.Id),
            [
                new CardRegion.PrintedField(),
                new CardRegion.Nameplate(category, published.Name),
                new CardRegion.Illustration(
                    art.For(published.Id, $"{published.Name} Support illustration."),
                    IllustrationPlacement.Framed
                ),
                new CardRegion.IdentityStrip($"{category} · {published.Id}"),
                new CardRegion.Mechanics([
                    .. published.Effects.Select(effect =>
                        (CardEntry)
                            new CardEntry.Rule(
                                new MechanicalId(effect.MechanicalId),
                                effect.Name,
                                effect.EffectText ?? string.Empty
                            )
                    ),
                ]),
                new CardRegion.Colophon(
                    $"{category} card. Played from hand, then discarded unless its own text says otherwise.",
                    published.Id,
                    Rarity.Uncommon,
                    number
                ),
            ]
        );

    private static ICard ToEnergy(
        BlokemonPublicBasicEnergy published,
        ArtIndex art,
        CollectorNumber number
    )
    {
        var type = Enum.Parse<BlokemonType>(published.Id.Split('-')[1], ignoreCase: true);

        return Themed(
            type,
            new CardId(published.Id),
            [
                new CardRegion.PrintedField(),
                new CardRegion.Vitality(null, type),
                new CardRegion.Nameplate(null, "Energy"),
                new CardRegion.Illustration(
                    art.ForSymbol(published.SymbolKey, $"{published.Name} Basic Energy field."),
                    IllustrationPlacement.Field
                ),
                new CardRegion.Denomination(type),
                new CardRegion.Colophon(null, null, null, number),
            ]
        );
    }

    private static ICard ToReverse(ArtIndex art) =>
        new Card<UnprintedTheme>(
            new CardId("REVERSE"),
            [
                new CardRegion.Illustration(
                    art.ForSymbol("card-back", "Blokemon card reverse."),
                    IllustrationPlacement.FullBleed
                ),
            ]
        );

    private static ICard Themed(BlokemonType type, CardId id, ImmutableArray<CardRegion> regions) =>
        type switch
        {
            BlokemonType.Blazed => new Card<BlazedTheme>(id, regions),
            BlokemonType.Beer => new Card<BeerTheme>(id, regions),
            BlokemonType.Curry => new Card<CurryTheme>(id, regions),
            BlokemonType.Dodgy => new Card<DodgyTheme>(id, regions),
            BlokemonType.Geeked => new Card<GeekedTheme>(id, regions),
            BlokemonType.Lairy => new Card<LairyTheme>(id, regions),
            BlokemonType.Legend => new Card<LegendTheme>(id, regions),
            BlokemonType.Local => new Card<LocalTheme>(id, regions),
            BlokemonType.Roadie => new Card<RoadieTheme>(id, regions),
            BlokemonType.Sober => new Card<SoberTheme>(id, regions),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    private static ImmutableArray<CardEntry> ToEntries(
        BlokemonPublicCollectible published,
        BlokemonCollectible mechanical,
        IReadOnlyDictionary<BlokemonMechanicalType, BlokemonType> label
    )
    {
        var costs = mechanical.Attacks.ToDictionary(attack => attack.MechanicalId);

        return
        [
            .. published.Abilities.Select(ability =>
                (CardEntry)
                    new CardEntry.Ability(
                        new MechanicalId(ability.MechanicalId),
                        ability.Name,
                        ability.EffectText ?? string.Empty
                    )
            ),
            .. published.Attacks.Select(attack =>
                (CardEntry)
                    new CardEntry.Attack(
                        new MechanicalId(attack.MechanicalId),
                        attack.Name,
                        [.. costs[attack.MechanicalId].VimCost.Select(vim => label[vim])],
                        new Damage(costs[attack.MechanicalId].PrintedDamage),
                        attack.EffectText
                    )
            ),
            .. published.Rules.Select(rule =>
                (CardEntry)
                    new CardEntry.Rule(
                        new MechanicalId(rule.MechanicalId),
                        rule.Name,
                        rule.EffectText ?? string.Empty
                    )
            ),
        ];
    }

    private static PreviousStage? ToPrevious(
        string? promotesFromId,
        ArtIndex art,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> supportNames
    )
    {
        if (promotesFromId is null)
        {
            return null;
        }

        // A few collectibles promote from a Support that is played as a stand-in Basic.
        var name =
            names.GetValueOrDefault(promotesFromId)
            ?? supportNames.GetValueOrDefault(promotesFromId)
            ?? throw new InvalidDataException($"Unknown previous stage {promotesFromId}");

        return new PreviousStage(
            new CardId(promotesFromId),
            name,
            art.For(promotesFromId, $"Previous stage {name} thumbnail.")
        );
    }

    private static TypeAffinity? ToAffinity(
        BlokemonMechanicalTypeModifier[] affinities,
        IReadOnlyDictionary<BlokemonMechanicalType, BlokemonType> label
    ) =>
        affinities.Length == 0
            ? null
            : new TypeAffinity(label[affinities[0].MechanicalType], affinities[0].Modifier);

    private static Stage ToStage(BlokemonRank rank) =>
        rank switch
        {
            BlokemonRank.Regular => Stage.Basic,
            BlokemonRank.Seasoned => Stage.StageOne,
            BlokemonRank.Landlord => Stage.StageTwo,
            _ => throw new ArgumentOutOfRangeException(nameof(rank)),
        };

    private sealed record ProfileEntry(string Subtype, int Feet, int Inches, int Pounds);

    private sealed record PrintingDocument(ImmutableArray<PrintingRow> Collectibles);

    private sealed record PrintingRow(string Id, string Gen1Set, string Gen1Rarity);

    private sealed record PrintingIndex(
        IReadOnlyDictionary<string, CollectorNumber> Numbers,
        IReadOnlySet<string> Holo
    );
}

/// <summary>The complete printed set.</summary>
/// <param name="Blokemon">The collectible fronts.</param>
/// <param name="Supports">The Support fronts.</param>
/// <param name="Energy">The Basic Energy fronts.</param>
/// <param name="Reverse">The shared reverse.</param>
public sealed record CardSet(
    ImmutableArray<ICard> Blokemon,
    ImmutableArray<ICard> Supports,
    ImmutableArray<ICard> Energy,
    ICard Reverse
);
