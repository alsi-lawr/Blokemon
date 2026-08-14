namespace Blokemon.CardGen.Domain;

/// <summary>A scoped print atmosphere.</summary>
public interface ICardTheme
{
    /// <summary>The stylesheet token of the atmosphere, absent on unprinted stock.</summary>
    static abstract string? Token { get; }
}

/// <summary>The Blazed print atmosphere.</summary>
public readonly struct BlazedTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Blazed);
}

/// <summary>The Beer print atmosphere.</summary>
public readonly struct BeerTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Beer);
}

/// <summary>The Curry print atmosphere.</summary>
public readonly struct CurryTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Curry);
}

/// <summary>The Dodgy print atmosphere.</summary>
public readonly struct DodgyTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Dodgy);
}

/// <summary>The Geeked print atmosphere.</summary>
public readonly struct GeekedTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Geeked);
}

/// <summary>The Lairy print atmosphere.</summary>
public readonly struct LairyTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Lairy);
}

/// <summary>The Legend print atmosphere.</summary>
public readonly struct LegendTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Legend);
}

/// <summary>The Local print atmosphere.</summary>
public readonly struct LocalTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Local);
}

/// <summary>The Roadie print atmosphere.</summary>
public readonly struct RoadieTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Roadie);
}

/// <summary>The Sober print atmosphere.</summary>
public readonly struct SoberTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => nameof(BlokemonType.Sober);
}

/// <summary>The Support print atmosphere.</summary>
public readonly struct SupportTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => "Support";
}

/// <summary>The absence of a print atmosphere.</summary>
public readonly struct UnprintedTheme : ICardTheme
{
    /// <inheritdoc/>
    public static string? Token => null;
}
