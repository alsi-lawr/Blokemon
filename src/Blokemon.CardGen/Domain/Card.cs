using System.Collections.Immutable;

namespace Blokemon.CardGen.Domain;

/// <summary>One card face.</summary>
public interface ICard
{
    /// <summary>The canonical identifier of the card.</summary>
    CardId Id { get; }

    /// <summary>The regions printed on the face, in print order.</summary>
    ImmutableArray<CardRegion> Regions { get; }

    /// <summary>The stylesheet token of the card's atmosphere, absent on unprinted stock.</summary>
    string? ThemeToken { get; }

    /// <summary>The type printed on the card, absent when the card has no type disc.</summary>
    BlokemonType? PrintedType { get; }

    /// <summary>The name printed on the card, falling back to its identifier.</summary>
    string DisplayName { get; }
}

/// <summary>One card face printed in a given atmosphere.</summary>
/// <typeparam name="TTheme">The atmosphere the card prints in.</typeparam>
/// <param name="Id">The canonical identifier of the card.</param>
/// <param name="Regions">The regions printed on the face, in print order.</param>
public sealed record Card<TTheme>(CardId Id, ImmutableArray<CardRegion> Regions) : ICard
    where TTheme : ICardTheme
{
    /// <inheritdoc/>
    public string? ThemeToken => TTheme.Token;

    /// <inheritdoc/>
    public BlokemonType? PrintedType =>
        Regions
            .OfType<CardRegion.Vitality>()
            .Select(vitality => (BlokemonType?)vitality.Type)
            .FirstOrDefault();

    /// <inheritdoc/>
    public string DisplayName =>
        Regions
            .OfType<CardRegion.Nameplate>()
            .Select(nameplate => nameplate.Name)
            .DefaultIfEmpty(Id.Value)
            .First();
}
