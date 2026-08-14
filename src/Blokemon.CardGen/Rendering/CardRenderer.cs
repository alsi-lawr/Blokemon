using Blokemon.CardGen.Domain;

namespace Blokemon.CardGen.Rendering;

/// <summary>Prints a card's regions as the shared card markup.</summary>
public static class CardRenderer
{
    private const string _layout = "gym-challenge-750x1050";

    /// <summary>Prints a card.</summary>
    /// <param name="card">The card to print.</param>
    /// <param name="art">The illustrations to embed.</param>
    /// <returns>The card markup.</returns>
    public static string Render(ICard card, Illustrations art)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(art);

        var regions = string.Concat(card.Regions.Select(region => Region(region, art)));

        return $"""<article class="blokemon-gym-card" data-layout="{_layout}"{Theme(card.ThemeToken)}{Foil(card)} data-canonical-id="{Esc(card.Id.Value)}" aria-label="{Esc(AccessibleName(card))}">{regions}</article>""";
    }

    private static string Theme(string? token) =>
        token is null ? string.Empty : $" data-card-type=\"{token}\"";

    private static string Foil(ICard card) =>
        card
            .Regions.OfType<CardRegion.Colophon>()
            .Any(colophon => colophon.Rarity?.IsHolo() is true)
            ? " data-holo=\"true\""
            : string.Empty;

    private static string AccessibleName(ICard card) =>
        card.Regions.OfType<CardRegion.Vitality>().FirstOrDefault()
            is { Points: { } points } vitality
            ? $"{card.DisplayName}, {points} HP, {vitality.Type} Blokemon"
            : card.DisplayName;

    private static string Region(CardRegion region, Illustrations art) =>
        region.Match(
            PrintedField,
            Nameplate,
            Vitality,
            lineage => Lineage(lineage, art),
            illustration => Illustration(illustration, art),
            Denomination,
            IdentityStrip,
            Mechanics,
            Affinities,
            Colophon
        );

    private static string PrintedField(CardRegion.PrintedField _) =>
        """<div class="printed-inner-field" data-region="inner-field" aria-hidden="true"></div>""";

    private static string Nameplate(CardRegion.Nameplate nameplate) =>
        $"""{Classification(nameplate.Classification)}<h2 class="card-name" style="--blokemon-name-len:{nameplate.Name.Length}">{Esc(nameplate.Name)}</h2>""";

    private static string Classification(string? classification) =>
        classification is null
            ? string.Empty
            : $"""<span class="classification">{Esc(classification)}</span>""";

    private static string Vitality(CardRegion.Vitality vitality) =>
        $"""{HitPoints(vitality.Points)}<span class="main-type-icon" data-energy="{vitality.Type}" aria-label="{vitality.Type} type">{TypeGlyphs.Reference(vitality.Type)}</span>""";

    private static string HitPoints(HitPoints? points) =>
        points is null
            ? string.Empty
            : $"""<div class="hp-cluster"><strong>{points}</strong><small>HP</small></div>""";

    private static string Lineage(CardRegion.Lineage lineage, Illustrations art) =>
        $"""<div class="evolution-panel" data-region="evolution-panel">{art.Image(lineage.Previous.Art, "previous-art")}</div><div class="evolves-strip">Evolves from {Esc(lineage.Previous.Name)}</div>""";

    private static string Illustration(CardRegion.Illustration illustration, Illustrations art) =>
        $"""{Frame(illustration.Placement)}<figure class="art-viewport" data-region="art-viewport" data-placement="{Placement(illustration.Placement)}">{art.Image(illustration.Art, null)}</figure>""";

    private static string Frame(IllustrationPlacement placement) =>
        placement is IllustrationPlacement.Framed
            ? """<div class="art-frame" data-region="art-frame" aria-hidden="true"></div>"""
            : string.Empty;

    private static string Placement(IllustrationPlacement placement) =>
        placement switch
        {
            IllustrationPlacement.Framed => "framed",
            IllustrationPlacement.Field => "field",
            IllustrationPlacement.FullBleed => "full-bleed",
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };

    private static string Denomination(CardRegion.Denomination denomination) =>
        $"""<span class="denomination" data-region="denomination" data-energy="{denomination.Type}" aria-label="{denomination.Type} Energy">{TypeGlyphs.Reference(denomination.Type)}</span>""";

    private static string IdentityStrip(CardRegion.IdentityStrip strip) =>
        $"""<div class="metadata-strip" data-region="metadata-strip"><span>{Esc(strip.Identity)}</span></div>""";

    private static string Mechanics(CardRegion.Mechanics mechanics) =>
        $"""<div class="rules-flow" data-region="rules-flow">{string.Concat(mechanics.Entries.Select(entry => entry.Match(Note, Attack, Note)))}</div><div class="footer-rule" aria-hidden="true"></div>""";

    private static string Affinities(CardRegion.Affinities affinities) =>
        $"""<div class="stat-row" data-region="stat-row"><div class="footer-stat is-weakness"><b>weakness</b>{StatValue(affinities.Weakness)}</div><div class="footer-stat is-resistance"><b>resistance</b>{StatValue(affinities.Resistance)}</div><div class="footer-stat is-retreat"><b>retreat cost</b>{RetreatValue(affinities.Retreat)}</div></div>""";

    private static string Colophon(CardRegion.Colophon colophon) =>
        $"""{Flavour(colophon.Flavour, colophon.Index)}<div class="legal-row" data-region="legal-row">{CollectorMark(colophon.Number)}{RarityImprint(colophon.Rarity)}</div>""";

    private static string Flavour(string? flavour, string? index) =>
        flavour is null
            ? string.Empty
            : $"""<div class="flavour-box" data-region="flavour-box"><p class="flavour">{Esc(flavour)}{Index(index)}</p></div>""";

    private static string Index(string? index) =>
        index is null ? string.Empty : $""" <span class="card-index">{Esc(index)}</span>""";

    private static string CollectorMark(CollectorNumber? number) =>
        number is { } printed
            ? $"""<span class="collector-number">{printed.PrintedLabel()}</span>"""
            : string.Empty;

    private static string RarityImprint(Rarity? rarity) =>
        rarity is { } printed
            ? $"""<span class="rarity-label">{printed.PrintedLabel()}</span><span class="rarity-mark" aria-label="{printed.PrintedLabel()} rarity">{printed.RarityMark()}</span>"""
            : string.Empty;

    private static string Attack(CardEntry.Attack attack) =>
        $"""<section class="rule-entry" data-mechanical-id="{Esc(attack.Id.Value)}" data-entry-kind="Attack"><div class="entry-energy">{string.Concat(attack.EnergyCost.Select(Pip))}</div><p class="entry-copy{NameOnly(attack.EffectText)}"><b class="entry-name">{Esc(attack.Name)}</b>{Esc(attack.EffectText ?? string.Empty)}</p><strong class="entry-damage" aria-label="{attack.Damage} Damage">{attack.Damage}</strong></section>""";

    private static string Note(CardEntry entry)
    {
        var (kind, effect) = entry switch
        {
            CardEntry.Ability ability => ("Ability", ability.EffectText),
            CardEntry.Rule rule => ("Rule", rule.EffectText),
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };

        return $"""<section class="rule-entry is-note" data-mechanical-id="{Esc(entry.Id.Value)}" data-entry-kind="{kind}"><p class="entry-copy"><span class="entry-lead">{kind}:</span><b class="entry-name">{Esc(entry.Name)}</b>{Esc(effect)}</p></section>""";
    }

    private static string NameOnly(string? effectText) =>
        string.IsNullOrWhiteSpace(effectText) ? " is-name-only" : string.Empty;

    private static string Pip(BlokemonType type) =>
        $"""<span class="energy-pip" data-energy="{type}" aria-label="{type} Energy">{TypeGlyphs.Reference(type)}</span>""";

    private static string StatValue(TypeAffinity? affinity) =>
        affinity is null
            ? """<span class="stat-value">&#8212;</span>"""
            : $"""<span class="stat-value"><span class="footer-icon" data-energy="{affinity.Type}" aria-hidden="true">{TypeGlyphs.Reference(affinity.Type)}</span>{Esc(affinity.PrintedValue())}</span>""";

    private static string RetreatValue(RetreatCost retreat) =>
        retreat.Value is 0
            ? """<span class="stat-value">&#8212;</span>"""
            : $"""<span class="stat-value">{string.Concat(Enumerable.Repeat(RetreatPip(), retreat.Value))}</span>""";

    private static string RetreatPip() =>
        $"""<span class="footer-icon" data-energy="Local" aria-label="Local Energy">{TypeGlyphs.Reference(BlokemonType.Local)}</span>""";

    // The card is delivered as XML, so every entity is numeric and every void element is closed.
    private static string Esc(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
}
