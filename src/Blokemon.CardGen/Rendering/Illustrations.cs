using System.Collections.Immutable;
using Blokemon.CardGen.Domain;

namespace Blokemon.CardGen.Rendering;

/// <summary>The illustrations a card carries inside itself.</summary>
public sealed class Illustrations
{
    private readonly ImmutableDictionary<string, string> _encoded;

    private Illustrations(ImmutableDictionary<string, string> encoded) => _encoded = encoded;

    /// <summary>Reads every illustration in a directory.</summary>
    /// <param name="directory">The directory holding the illustrations.</param>
    /// <returns>The illustrations.</returns>
    public static Illustrations Load(string directory) =>
        new(
            Directory
                .EnumerateFiles(directory, "*.svg")
                .ToImmutableDictionary(
                    file => Path.GetFileName(file)!,
                    file => Convert.ToBase64String(File.ReadAllBytes(file)),
                    StringComparer.Ordinal
                )
        );

    /// <summary>Places an illustration as an embedded image.</summary>
    /// <param name="artwork">The artwork to place.</param>
    /// <param name="className">The class the image carries, absent when it needs none.</param>
    /// <returns>The image markup.</returns>
    public string Image(Artwork artwork, string? className)
    {
        ArgumentNullException.ThrowIfNull(artwork);

        if (!_encoded.TryGetValue(artwork.FileName, out var data))
        {
            throw new InvalidDataException($"No illustration for {artwork.FileName}");
        }

        // Carried as data rather than referenced, so a card is one file with nothing beside it.
        var mark = className is null ? string.Empty : $" class=\"{className}\"";
        var alt = artwork
            .AltText.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

        return $"""<img{mark} src="data:image/svg+xml;base64,{data}" alt="{alt}"/>""";
    }
}
