using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Blokemon.CardGen.Domain;

namespace Blokemon.CardGen.Authority;

/// <summary>A lookup of illustrations by card id and symbol key.</summary>
public sealed partial class ArtIndex
{
    private readonly ImmutableDictionary<string, string> _byCardId;
    private readonly ImmutableDictionary<string, string> _bySymbolKey;

    private ArtIndex(
        ImmutableDictionary<string, string> byCardId,
        ImmutableDictionary<string, string> bySymbolKey
    )
    {
        _byCardId = byCardId;
        _bySymbolKey = bySymbolKey;
    }

    /// <summary>Indexes the illustrations in a directory.</summary>
    /// <param name="directory">The directory holding the illustrations.</param>
    /// <returns>The illustration index.</returns>
    public static ArtIndex Scan(string directory)
    {
        var files = Directory
            .EnumerateFiles(directory, "*.svg")
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        var byCardId = files
            .Select(file => (file, match: CardAsset().Match(file)))
            .Where(candidate => candidate.match.Success)
            .ToImmutableDictionary(
                candidate => candidate.match.Groups["id"].Value,
                candidate => candidate.file
            );

        var bySymbolKey = files
            .Select(file => (file, match: SymbolAsset().Match(file)))
            .Where(candidate => candidate.match.Success)
            .ToImmutableDictionary(
                candidate => candidate.match.Groups["key"].Value,
                candidate => candidate.file
            );

        return new ArtIndex(byCardId, bySymbolKey);
    }

    /// <summary>The illustration bound to a card.</summary>
    /// <param name="cardId">The identifier of the card.</param>
    /// <param name="altText">The illustration alternative text.</param>
    /// <returns>The bound illustration.</returns>
    public Artwork For(string cardId, string altText) =>
        _byCardId.TryGetValue(cardId, out var file)
            ? new Artwork(file, altText)
            : throw new InvalidDataException($"No illustration for {cardId}");

    /// <summary>The illustration bound to a symbol.</summary>
    /// <param name="symbolKey">The key of the symbol.</param>
    /// <param name="altText">The illustration alternative text.</param>
    /// <returns>The bound illustration.</returns>
    public Artwork ForSymbol(string symbolKey, string altText) =>
        _bySymbolKey.TryGetValue(symbolKey, out var file)
            ? new Artwork(file, altText)
            : throw new InvalidDataException($"No illustration for symbol {symbolKey}");

    [GeneratedRegex(@"(?<id>(?:BLK|KIT)-\d+)-")]
    private static partial Regex CardAsset();

    [GeneratedRegex(@"(?<key>energy-[a-z]+|card-back)\.svg$")]
    private static partial Regex SymbolAsset();
}
