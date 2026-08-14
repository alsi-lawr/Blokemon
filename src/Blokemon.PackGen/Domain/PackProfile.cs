using System.Collections.Immutable;

namespace Blokemon.PackGen.Domain;

/// <summary>The configurable surface of a packaging deployment.</summary>
public sealed record PackProfile
{
    // BLOKEMON-016 fixes this surface at three points. The packaging design itself carries no
    // override seam, so anything not named here is deliberately absent rather than ignored.
    private readonly ImmutableDictionary<PackKey, string> _names;

    /// <summary>The configurable surface of a packaging deployment.</summary>
    /// <param name="noun">The product noun printed as the wordmark.</param>
    /// <param name="stock">The stock every pack without a fixed material follows.</param>
    /// <param name="names">The printed name of every pack.</param>
    public PackProfile(string noun, PackStock stock, IReadOnlyDictionary<PackKey, string> names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noun);
        ArgumentNullException.ThrowIfNull(names);

        var absent = Enum.GetValues<PackKey>()
            .Where(key => !names.TryGetValue(key, out var name) || string.IsNullOrWhiteSpace(name))
            .ToImmutableArray();

        if (absent.Length is not 0)
        {
            throw new ArgumentException(
                $"every pack needs a printed name; missing {string.Join(", ", absent)}",
                nameof(names)
            );
        }

        Noun = noun;
        Stock = stock;
        _names = names.ToImmutableDictionary();
    }

    /// <summary>The product noun printed as the wordmark.</summary>
    public string Noun { get; }

    /// <summary>The stock every pack without a fixed material follows.</summary>
    public PackStock Stock { get; }

    /// <summary>The default Blokemon profile.</summary>
    /// <param name="stock">The stock to print on.</param>
    /// <returns>The profile.</returns>
    public static PackProfile Blokemon(PackStock stock) =>
        new(
            "Blokemon",
            stock,
            new Dictionary<PackKey, string>
            {
                [PackKey.Booster] = "The Oche",
                [PackKey.StarterDeck] = "Starter Deck",
                [PackKey.OneForTheRoad] = "One for the Road",
                [PackKey.RoundOfThree] = "Round",
                [PackKey.LockIn] = "Lock-In",
                [PackKey.Session] = "Session",
            }
        );

    /// <summary>The printed name of a pack.</summary>
    /// <param name="key">The pack to name.</param>
    /// <returns>The printed name.</returns>
    public string Name(PackKey key) => _names[key];

    /// <summary>The same profile printed on another stock.</summary>
    /// <param name="stock">The stock to print on.</param>
    /// <returns>The profile.</returns>
    public PackProfile On(PackStock stock) => new(Noun, stock, _names);
}
