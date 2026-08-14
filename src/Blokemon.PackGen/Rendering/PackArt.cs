using Blokemon.PackGen.Domain;

namespace Blokemon.PackGen.Rendering;

/// <summary>The drawn artwork of a pack.</summary>
public static class PackArt
{
    /// <summary>Draws a pack under a profile.</summary>
    /// <param name="pack">The pack to draw.</param>
    /// <param name="profile">The profile printing it.</param>
    /// <returns>The document.</returns>
    public static string Draw(Pack pack, PackProfile profile)
    {
        ArgumentNullException.ThrowIfNull(pack);

        return pack.Format.Match(
            wrapper => WrapperArt.Draw(pack, profile, wrapper),
            _ => CartonArt.Draw(pack, profile)
        );
    }
}
