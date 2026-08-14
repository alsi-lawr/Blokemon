using System.Globalization;

namespace Blokemon.CardGen.Domain;

/// <summary>The canonical identifier of a card.</summary>
public readonly record struct CardId
{
    /// <summary>Creates a card identifier.</summary>
    /// <param name="value">The identifier text.</param>
    public CardId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The identifier text.</summary>
    public string Value { get; }

    /// <summary>The identifier text.</summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value;
}

/// <summary>The identifier of a mechanical entry.</summary>
public readonly record struct MechanicalId
{
    /// <summary>Creates a mechanical identifier.</summary>
    /// <param name="value">The identifier text.</param>
    public MechanicalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The identifier text.</summary>
    public string Value { get; }

    /// <summary>The identifier text.</summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value;
}

/// <summary>The printed HP of a collectible.</summary>
public readonly record struct HitPoints
{
    /// <summary>Creates a printed HP amount.</summary>
    /// <param name="value">The HP amount.</param>
    public HitPoints(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
        Value = value;
    }

    /// <summary>The HP amount.</summary>
    public int Value { get; }

    /// <summary>The HP amount as printed.</summary>
    /// <returns>The HP amount.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The printed Damage of an Attack.</summary>
public readonly record struct Damage
{
    /// <summary>Creates a printed Damage amount.</summary>
    /// <param name="value">The Damage amount.</param>
    public Damage(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The Damage amount.</summary>
    public int Value { get; }

    /// <summary>The Damage amount as printed.</summary>
    /// <returns>The Damage amount.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The printed Retreat cost of a collectible.</summary>
public readonly record struct RetreatCost
{
    /// <summary>Creates a printed Retreat cost.</summary>
    /// <param name="value">The number of Energy required.</param>
    public RetreatCost(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number of Energy required.</summary>
    public int Value { get; }
}

/// <summary>The Prize Cards taken when a collectible is Knocked Out.</summary>
public readonly record struct PrizeCards
{
    /// <summary>Creates a Prize Card count.</summary>
    /// <param name="value">The number of Prize Cards.</param>
    public PrizeCards(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
        Value = value;
    }

    /// <summary>The number of Prize Cards.</summary>
    public int Value { get; }

    /// <summary>The Prize Card line printed on the flavour plate.</summary>
    /// <returns>The printed Prize Card line.</returns>
    public string PrintedLabel() =>
        Value == 1 ? "1 Prize Card when Knocked Out" : $"{Value} Prize Cards when Knocked Out";
}

/// <summary>The collector number printed in the imprint row.</summary>
public readonly record struct CollectorNumber
{
    /// <summary>Creates a collector number.</summary>
    /// <param name="value">The position within the printed run.</param>
    /// <param name="total">The size of the printed run.</param>
    public CollectorNumber(int value, int total)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, total);
        Value = value;
        Total = total;
    }

    /// <summary>The position within the printed run.</summary>
    public int Value { get; }

    /// <summary>The size of the printed run.</summary>
    public int Total { get; }

    /// <summary>The collector number as printed.</summary>
    /// <returns>The printed collector number.</returns>
    public string PrintedLabel() =>
        $"{Value.ToString("D3", CultureInfo.InvariantCulture)}/{Total.ToString("D3", CultureInfo.InvariantCulture)}";
}

/// <summary>The whole-object multiplier applied to a canonical card.</summary>
public readonly record struct CardScale
{
    /// <summary>The unscaled card.</summary>
    public static CardScale Canonical { get; } = new(1d);

    /// <summary>Creates a card scale.</summary>
    /// <param name="value">The multiplier.</param>
    public CardScale(double value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0d);
        Value = value;
    }

    /// <summary>The multiplier.</summary>
    public double Value { get; }

    /// <summary>The multiplier as a CSS custom property value.</summary>
    /// <returns>The CSS value.</returns>
    public string ToCssValue() => Value.ToString("0.##########", CultureInfo.InvariantCulture);
}

/// <summary>An illustration bound to a card.</summary>
/// <param name="FileName">The illustration file name.</param>
/// <param name="AltText">The illustration alternative text.</param>
public sealed record Artwork(string FileName, string AltText);

/// <summary>A printed Weakness or Resistance.</summary>
/// <param name="Type">The affected type.</param>
/// <param name="Modifier">The printed modifier.</param>
public sealed record TypeAffinity(BlokemonType Type, string Modifier)
{
    /// <summary>The affinity as printed in the stat row.</summary>
    /// <returns>The printed affinity.</returns>
    public string PrintedValue() => $"{Type} {Modifier}";
}

/// <summary>The immediately previous evolution stage.</summary>
/// <param name="Id">The identifier of the previous card.</param>
/// <param name="Name">The name of the previous card.</param>
/// <param name="Art">The thumbnail shown in the evolution burst.</param>
public sealed record PreviousStage(CardId Id, string Name, Artwork Art);
