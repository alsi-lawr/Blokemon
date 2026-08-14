using System.Collections.Immutable;

namespace Blokemon.CardGen.Domain;

/// <summary>A printed region of a card face.</summary>
public abstract record CardRegion
{
    private CardRegion() { }

    /// <summary>Applies the function matching this region.</summary>
    /// <typeparam name="TResult">The result of the applied function.</typeparam>
    /// <param name="printedField">Applied to the printed field.</param>
    /// <param name="nameplate">Applied to the nameplate.</param>
    /// <param name="vitality">Applied to the vitality cluster.</param>
    /// <param name="lineage">Applied to the lineage burst.</param>
    /// <param name="illustration">Applied to the illustration.</param>
    /// <param name="denomination">Applied to the denomination.</param>
    /// <param name="identityStrip">Applied to the identity strip.</param>
    /// <param name="mechanics">Applied to the mechanics region.</param>
    /// <param name="affinities">Applied to the affinities row.</param>
    /// <param name="colophon">Applied to the colophon.</param>
    /// <returns>The result of the applied function.</returns>
    public abstract TResult Match<TResult>(
        Func<PrintedField, TResult> printedField,
        Func<Nameplate, TResult> nameplate,
        Func<Vitality, TResult> vitality,
        Func<Lineage, TResult> lineage,
        Func<Illustration, TResult> illustration,
        Func<Denomination, TResult> denomination,
        Func<IdentityStrip, TResult> identityStrip,
        Func<Mechanics, TResult> mechanics,
        Func<Affinities, TResult> affinities,
        Func<Colophon, TResult> colophon
    );

    /// <summary>The coloured stock the face prints onto.</summary>
    public sealed record PrintedField : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => printedField(this);
    }

    /// <summary>The classification label and card name.</summary>
    /// <param name="Classification">The label printed above the name, absent when the card has none.</param>
    /// <param name="Name">The printed card name.</param>
    public sealed record Nameplate(string? Classification, string Name) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => nameplate(this);
    }

    /// <summary>The HP plate and main type disc.</summary>
    /// <param name="Points">The printed HP, absent when the card has none.</param>
    /// <param name="Type">The printed type.</param>
    public sealed record Vitality(HitPoints? Points, BlokemonType Type) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => vitality(this);
    }

    /// <summary>The evolution burst and Evolves-from strip.</summary>
    /// <param name="Previous">The immediately previous stage.</param>
    public sealed record Lineage(PreviousStage Previous) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => lineage(this);
    }

    /// <summary>The bound illustration.</summary>
    /// <param name="Art">The artwork printed in the region.</param>
    /// <param name="Placement">Where the illustration prints.</param>
    public sealed record Illustration(Artwork Art, IllustrationPlacement Placement) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => illustration(this);
    }

    /// <summary>The printed denomination symbol.</summary>
    /// <param name="Type">The denominated type.</param>
    public sealed record Denomination(BlokemonType Type) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => denomination(this);
    }

    /// <summary>The identity strip under the illustration.</summary>
    /// <param name="Identity">The printed identity line.</param>
    public sealed record IdentityStrip(string Identity) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => identityStrip(this);
    }

    /// <summary>The flowing mechanics region.</summary>
    /// <param name="Entries">The entries printed in the region, in print order.</param>
    public sealed record Mechanics(ImmutableArray<CardEntry> Entries) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => mechanics(this);
    }

    /// <summary>The printed Weakness, Resistance and Retreat row.</summary>
    /// <param name="Weakness">The printed Weakness, absent when the card has none.</param>
    /// <param name="Resistance">The printed Resistance, absent when the card has none.</param>
    /// <param name="Retreat">The printed Retreat cost.</param>
    public sealed record Affinities(
        TypeAffinity? Weakness,
        TypeAffinity? Resistance,
        RetreatCost Retreat
    ) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => affinities(this);
    }

    /// <summary>The flavour plate and imprint row.</summary>
    /// <param name="Flavour">The printed flavour line, absent when the card has none.</param>
    /// <param name="Index">The printed index line, absent when the card has none.</param>
    /// <param name="Rarity">The printed rarity, absent when the card has none.</param>
    /// <param name="Number">The printed collector number, absent when the card has none.</param>
    public sealed record Colophon(
        string? Flavour,
        string? Index,
        Rarity? Rarity,
        CollectorNumber? Number
    ) : CardRegion
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<PrintedField, TResult> printedField,
            Func<Nameplate, TResult> nameplate,
            Func<Vitality, TResult> vitality,
            Func<Lineage, TResult> lineage,
            Func<Illustration, TResult> illustration,
            Func<Denomination, TResult> denomination,
            Func<IdentityStrip, TResult> identityStrip,
            Func<Mechanics, TResult> mechanics,
            Func<Affinities, TResult> affinities,
            Func<Colophon, TResult> colophon
        ) => colophon(this);
    }
}

/// <summary>Where an illustration prints on a card face.</summary>
public enum IllustrationPlacement
{
    /// <summary>Inside the printed art frame.</summary>
    Framed,

    /// <summary>Across the printed field, behind the card furniture.</summary>
    Field,

    /// <summary>Across the whole card, covering the border and field.</summary>
    FullBleed,
}
