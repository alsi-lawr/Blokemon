namespace Blokemon.CardGen.Domain;

/// <summary>The approved public type labels.</summary>
public enum BlokemonType
{
    Blazed,
    Beer,
    Curry,
    Dodgy,
    Geeked,
    Lairy,
    Legend,
    Local,
    Roadie,
    Sober,
}

/// <summary>The evolution stage of a collectible.</summary>
public enum Stage
{
    Basic,
    StageOne,
    StageTwo,
}

/// <summary>The printed rarity of a card.</summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    RareHolo,
}

/// <summary>The category of a Support card.</summary>
public enum SupportCategory
{
    Item,
    Tool,
    Supporter,
    Stadium,
}

/// <summary>The printed forms of the card vocabulary.</summary>
public static class CardVocabulary
{
    /// <summary>The classification label printed in the card header.</summary>
    /// <param name="stage">The stage to label.</param>
    /// <param name="isEvolved">Whether the card carries an evolution burst.</param>
    /// <returns>The classification label.</returns>
    public static string ClassificationLabel(this Stage stage, bool isEvolved) =>
        (stage, isEvolved) switch
        {
            (_, true) => stage.PrintedLabel().ToUpperInvariant(),
            _ => $"{stage.PrintedLabel()} Blokemon",
        };

    /// <summary>The printed name of a stage.</summary>
    /// <param name="stage">The stage to name.</param>
    /// <returns>The stage label.</returns>
    public static string PrintedLabel(this Stage stage) =>
        stage switch
        {
            Stage.Basic => "Basic",
            Stage.StageOne => "Stage 1",
            Stage.StageTwo => "Stage 2",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };

    /// <summary>The two-letter code for a type.</summary>
    /// <param name="type">The type to code.</param>
    /// <returns>The type code.</returns>
    public static string TypeCode(this BlokemonType type) =>
        type.ToString()[..2].ToUpperInvariant();

    /// <summary>The mark printed in the rarity imprint.</summary>
    /// <param name="rarity">The rarity to mark.</param>
    /// <returns>The rarity mark.</returns>
    public static string RarityMark(this Rarity rarity) =>
        rarity switch
        {
            Rarity.Common => "●",
            Rarity.Uncommon => "◆",
            Rarity.Rare or Rarity.RareHolo => "★",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };

    /// <summary>The printed name of a rarity.</summary>
    /// <param name="rarity">The rarity to name.</param>
    /// <returns>The rarity label.</returns>
    public static string PrintedLabel(this Rarity rarity) =>
        rarity switch
        {
            Rarity.Common => "Common",
            Rarity.Uncommon => "Uncommon",
            Rarity.Rare => "Rare",
            Rarity.RareHolo => "Rare Holo",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };

    /// <summary>Whether a rarity prints with a holographic field.</summary>
    /// <param name="rarity">The rarity to test.</param>
    /// <returns>Whether the rarity is holographic.</returns>
    public static bool IsHolo(this Rarity rarity) => rarity is Rarity.RareHolo;
}
