namespace Blokemon.PackGen.Domain;

/// <summary>The stock finish a deployment prints its packaging on.</summary>
public enum PackStock
{
    /// <summary>Metallised film.</summary>
    Gloss,

    /// <summary>Uncoated board.</summary>
    Kraft,
}

/// <summary>The material a pack is printed on.</summary>
public enum PackMaterial
{
    /// <summary>Metallised film.</summary>
    Gloss,

    /// <summary>Uncoated board.</summary>
    Kraft,

    /// <summary>The premium gold colourway.</summary>
    Gold,
}

/// <summary>Printed properties of stocks and materials.</summary>
public static class PackStocks
{
    /// <summary>The material a stock prints as.</summary>
    /// <param name="stock">The configured stock.</param>
    /// <returns>The matching material.</returns>
    public static PackMaterial AsMaterial(this PackStock stock) =>
        stock switch
        {
            PackStock.Gloss => PackMaterial.Gloss,
            PackStock.Kraft => PackMaterial.Kraft,
            _ => throw new ArgumentOutOfRangeException(nameof(stock)),
        };

    /// <summary>The style token a material prints under.</summary>
    /// <param name="material">The material to print.</param>
    /// <returns>The token.</returns>
    public static string Token(this PackMaterial material) =>
        material switch
        {
            PackMaterial.Gloss => "m-gloss",
            PackMaterial.Kraft => "m-kraft",
            PackMaterial.Gold => "m-gold",
            _ => throw new ArgumentOutOfRangeException(nameof(material)),
        };

    /// <summary>Whether a material carries a visible fibre grain.</summary>
    /// <param name="material">The material to print.</param>
    /// <returns>True when the material is uncoated board.</returns>
    public static bool HasFibre(this PackMaterial material) => material is PackMaterial.Kraft;

    /// <summary>The label a stock is described by.</summary>
    /// <param name="stock">The configured stock.</param>
    /// <returns>The label.</returns>
    public static string PrintedLabel(this PackStock stock) =>
        stock switch
        {
            PackStock.Gloss => "Gloss foil",
            PackStock.Kraft => "Kraft board",
            _ => throw new ArgumentOutOfRangeException(nameof(stock)),
        };
}
