namespace Blokemon.CardGen.Domain;

/// <summary>The printed physical description of a collectible.</summary>
/// <param name="Feet">The whole feet of the length.</param>
/// <param name="Inches">The remaining inches of the length.</param>
/// <param name="Pounds">The weight in pounds.</param>
public sealed record Stature(int Feet, int Inches, int Pounds)
{
    /// <summary>The length as printed on the identity strip.</summary>
    /// <returns>The printed length.</returns>
    public string PrintedLength() => Inches == 0 ? $"{Feet}'" : $"{Feet}' {Inches}\"";

    /// <summary>The weight as printed on the identity strip.</summary>
    /// <returns>The printed weight.</returns>
    public string PrintedWeight() => $"{Pounds} lbs.";
}

/// <summary>The species epithet and stature printed on the identity strip.</summary>
/// <param name="Subtype">The epithet naming the collectible's species.</param>
/// <param name="Stature">The printed length and weight.</param>
public sealed record CardProfile(string Subtype, Stature Stature)
{
    /// <summary>The identity strip line.</summary>
    /// <returns>The printed identity.</returns>
    public string PrintedIdentity() =>
        $"{Subtype} Blokemon. Length: {Stature.PrintedLength()}, Weight: {Stature.PrintedWeight()}";
}
