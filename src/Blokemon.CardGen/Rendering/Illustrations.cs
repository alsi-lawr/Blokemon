using System.Collections.Immutable;
using Blokemon.CardGen.Domain;

namespace Blokemon.CardGen.Rendering;

/// <summary>A trusted source of rendered card illustrations.</summary>
public abstract class IllustrationRendering
{
    private protected IllustrationRendering() { }

    /// <summary>Loads illustrations that travel inside the rendered card.</summary>
    /// <param name="directory">The directory holding the illustrations.</param>
    /// <returns>The embedded illustration rendering.</returns>
    public static IllustrationRendering Embedded(string directory) =>
        new EmbeddedIllustrationRendering(
            Directory
                .EnumerateFiles(directory, "*.svg")
                .ToImmutableDictionary(
                    file => Path.GetFileName(file)!,
                    file => Convert.ToBase64String(File.ReadAllBytes(file)),
                    StringComparer.Ordinal
                )
        );

    /// <summary>Loads illustrations referenced from the same-origin art directory.</summary>
    /// <param name="directory">The directory whose known illustration names may be referenced.</param>
    /// <returns>The referenced illustration rendering.</returns>
    public static IllustrationRendering Referenced(string directory) =>
        new ReferencedIllustrationRendering(
            Directory
                .EnumerateFiles(directory, "*.svg")
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToImmutableHashSet(StringComparer.Ordinal)
        );

    internal abstract string Image(Artwork artwork, IllustrationRole role);

    private static string Class(IllustrationRole role) =>
        role switch
        {
            IllustrationRole.Primary => string.Empty,
            IllustrationRole.PreviousStage => " class=\"previous-art\"",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static string Esc(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private sealed class EmbeddedIllustrationRendering(ImmutableDictionary<string, string> encoded)
        : IllustrationRendering
    {
        internal override string Image(Artwork artwork, IllustrationRole role)
        {
            ArgumentNullException.ThrowIfNull(artwork);

            if (!encoded.TryGetValue(artwork.FileName, out var data))
            {
                throw new InvalidDataException($"No illustration for {artwork.FileName}");
            }

            return $"""<img{Class(role)} src="data:image/svg+xml;base64,{data}" alt="{Esc(artwork.AltText)}"/>""";
        }
    }

    private sealed class ReferencedIllustrationRendering(ImmutableHashSet<string> knownFiles)
        : IllustrationRendering
    {
        internal override string Image(Artwork artwork, IllustrationRole role)
        {
            ArgumentNullException.ThrowIfNull(artwork);

            if (!knownFiles.Contains(artwork.FileName))
            {
                throw new InvalidDataException($"No illustration for {artwork.FileName}");
            }

            return $"""<img{Class(role)} src="/art/{Esc(artwork.FileName)}" alt="{Esc(artwork.AltText)}"/>""";
        }
    }
}

internal enum IllustrationRole
{
    Primary,
    PreviousStage,
}
