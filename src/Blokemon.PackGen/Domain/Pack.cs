using System.Globalization;

namespace Blokemon.PackGen.Domain;

/// <summary>The identity of an approved packaging object.</summary>
public enum PackKey
{
    /// <summary>The eleven-card booster.</summary>
    Booster,

    /// <summary>The sixty-card starter deck carton.</summary>
    StarterDeck,

    /// <summary>The single-card channel pack.</summary>
    OneForTheRoad,

    /// <summary>The three-card channel pack.</summary>
    RoundOfThree,

    /// <summary>The guaranteed-holo channel pack.</summary>
    LockIn,

    /// <summary>The resealable pouch of three boosters.</summary>
    Session,
}

/// <summary>Printed properties of pack identities.</summary>
public static class PackKeys
{
    /// <summary>The file-safe name of a pack identity.</summary>
    /// <param name="key">The identity to name.</param>
    /// <returns>The name.</returns>
    public static string Slug(this PackKey key) =>
        string.Concat(
            key.ToString()
                .Select(
                    (letter, index) =>
                        char.IsUpper(letter) && index > 0
                            ? $"-{char.ToLowerInvariant(letter)}"
                            : char.ToLowerInvariant(letter).ToString()
                )
        );
}

/// <summary>The contents printed on a pack foot.</summary>
/// <param name="Count">The contents line printed large.</param>
/// <param name="Declaration">The declaration printed small above the copyright.</param>
/// <param name="NotForResale">Whether the copyright line carries a not-for-resale notice.</param>
public readonly record struct PackContents(string Count, string Declaration, bool NotForResale)
{
    /// <summary>The copyright line printed under the declaration.</summary>
    /// <param name="noun">The configured product noun.</param>
    /// <returns>The copyright line.</returns>
    public string Copyright(string noun) =>
        NotForResale ? $"not for resale \u00a9 {noun}" : $"\u00a9 {noun}";
}

/// <summary>The offset a pack enters the shared glint cycle at.</summary>
public readonly record struct GlintDelay
{
    private const double _cycleSeconds = 5.5d;

    /// <summary>The offset a pack enters the shared glint cycle at.</summary>
    /// <param name="seconds">The offset in seconds.</param>
    public GlintDelay(double seconds)
    {
        // Offsets beyond the cycle length are not wrong so much as unreadable: two packs an
        // exact cycle apart glint together while their declared delays look unrelated.
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seconds, _cycleSeconds);
        Seconds = seconds;
    }

    /// <summary>The offset in seconds.</summary>
    public double Seconds { get; }

    /// <summary>The offset as a CSS duration.</summary>
    /// <returns>The duration.</returns>
    public string ToCssValue() => $"{Seconds.ToString("0.##", CultureInfo.InvariantCulture)}s";
}

/// <summary>An approved packaging object.</summary>
/// <param name="Key">The identity its configured name is looked up by.</param>
/// <param name="Format">The construction it is built as.</param>
/// <param name="Contents">The contents printed on its foot.</param>
/// <param name="Glint">The offset it enters the shared glint cycle at.</param>
/// <param name="FixedMaterial">
/// The material it always prints on, absent when it follows the configured stock.
/// </param>
public sealed record Pack(
    PackKey Key,
    PackFormat Format,
    PackContents Contents,
    GlintDelay Glint,
    PackMaterial? FixedMaterial = null
)
{
    /// <summary>The material this pack prints on under a stock.</summary>
    /// <param name="stock">The configured stock.</param>
    /// <returns>The material.</returns>
    public PackMaterial MaterialUnder(PackStock stock) => FixedMaterial ?? stock.AsMaterial();
}
