using System.Collections.Immutable;
using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Catalogue;

/// <summary>The approved packaging objects.</summary>
public static class PackCatalogue
{
    /// <summary>The approved packaging objects.</summary>
    public static ImmutableArray<Pack> All { get; } =
    [
        // Delays are spread across the 5.5s glint cycle so no two wrappers on the sheet flash
        // together, which reads as a synchronised page effect rather than as light on film.
        new Pack(
            PackKey.Booster,
            new PackFormat.Wrapper(WrapperSize.Booster, Resealable: false),
            new PackContents("11 cards", "11 additional game cards", NotForResale: true),
            new GlintDelay(0d)
        ),
        new Pack(
            PackKey.StarterDeck,
            new PackFormat.Carton(),
            new PackContents("60 cards", "60 cards \u00b7 rulebook", NotForResale: false),
            new GlintDelay(0d)
        ),
        new Pack(
            PackKey.OneForTheRoad,
            new PackFormat.Wrapper(WrapperSize.Small, Resealable: false),
            new PackContents("1 card", "1 additional game card", NotForResale: true),
            new GlintDelay(0.8d)
        ),
        new Pack(
            PackKey.RoundOfThree,
            new PackFormat.Wrapper(WrapperSize.Small, Resealable: false),
            new PackContents("3 cards", "3 additional game cards", NotForResale: true),
            new GlintDelay(2.7d)
        ),
        // The premium pull prints gold whatever stock the deployment chose. The colourway is
        // part of the fixed design, not a third stock a deployment can select.
        new Pack(
            PackKey.LockIn,
            new PackFormat.Wrapper(WrapperSize.Small, Resealable: false),
            new PackContents("1 holo", "1 additional game card", NotForResale: true),
            new GlintDelay(1.2d),
            PackMaterial.Gold
        ),
        new Pack(
            PackKey.Session,
            new PackFormat.Wrapper(WrapperSize.Small, Resealable: true),
            new PackContents("3 boosters", "33 additional game cards", NotForResale: true),
            new GlintDelay(3.4d)
        ),
    ];

    /// <summary>One approved packaging object.</summary>
    /// <param name="key">The object to select.</param>
    /// <returns>The object.</returns>
    public static Pack Get(PackKey key) => All.Single(pack => pack.Key == key);
}
