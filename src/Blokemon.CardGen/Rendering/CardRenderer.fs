namespace Blokemon.CardGen.Rendering

open System
open Blokemon.CardGen.Domain

/// Prints a card's regions as the shared card markup.
module CardRenderer =

    let private layout = "gym-challenge-750x1050"

    // The card is delivered as XML, so every entity is numeric and every void element is closed.
    let private esc (value: string) =
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal)

    let private theme token =
        match token with
        | None -> ""
        | Some token -> $" data-card-type=\"{token}\""

    let private foil (card: Card) =
        let holo =
            card.Regions
            |> Seq.exists (function
                | CardRegion.Colophon(rarity = Some rarity) -> Rarity.isHolo rarity
                | _ -> false)

        if holo then " data-holo=\"true\"" else ""

    let private vitalityOf (card: Card) =
        card.Regions
        |> Seq.tryPick (function
            | CardRegion.Vitality(points = points; printedType = printedType) ->
                Some(points, printedType)
            | _ -> None)

    let private accessibleName (card: Card) =
        match vitalityOf card with
        | Some(Some points, printedType) ->
            $"{card.DisplayName}, {points} HP, {printedType} Blokemon"
        | _ -> card.DisplayName

    let private classification classification =
        match classification with
        | None -> ""
        | Some label -> $"""<span class="classification">{esc label}</span>"""

    let private nameplate classificationLabel (name: string) =
        $"""{classification classificationLabel}<h2 class="card-name" style="--blokemon-name-len:{name.Length}">{esc name}</h2>"""

    let private hitPoints points =
        match points with
        | None -> ""
        | Some points ->
            $"""<div class="hp-cluster"><strong>{points}</strong><small>HP</small></div>"""

    let private vitality points printedType =
        $"""{hitPoints points}<span class="main-type-icon" data-energy="{printedType}" aria-label="{printedType} type">{TypeGlyphs.reference printedType}</span>"""

    let private lineage (previous: PreviousStage) art =
        let thumbnail =
            IllustrationRendering.image previous.Art IllustrationRole.PreviousStage art

        $"""<div class="evolution-panel" data-region="evolution-panel">{thumbnail}</div><div class="evolves-strip">Evolves from {esc previous.Name}</div>"""

    let private frame placement =
        match placement with
        | IllustrationPlacement.Framed ->
            """<div class="art-frame" data-region="art-frame" aria-hidden="true"></div>"""
        | _ -> ""

    let private placementToken placement =
        match placement with
        | IllustrationPlacement.Framed -> "framed"
        | IllustrationPlacement.Field -> "field"
        | IllustrationPlacement.FullBleed -> "full-bleed"

    let private illustration artwork placement art =
        let drawn = IllustrationRendering.image artwork IllustrationRole.Primary art

        $"""{frame placement}<figure class="art-viewport" data-region="art-viewport" data-placement="{placementToken placement}">{drawn}</figure>"""

    let private denomination printedType =
        $"""<span class="denomination" data-region="denomination" data-energy="{printedType}" aria-label="{printedType} Energy">{TypeGlyphs.reference printedType}</span>"""

    let private identityStrip identity =
        $"""<div class="metadata-strip" data-region="metadata-strip"><span>{esc identity}</span></div>"""

    let private pip (printedType: BlokemonType) =
        $"""<span class="energy-pip" data-energy="{printedType}" aria-label="{printedType} Energy">{TypeGlyphs.reference printedType}</span>"""

    let private nameOnly effectText =
        match effectText with
        | Some text when not (String.IsNullOrWhiteSpace text) -> ""
        | _ -> " is-name-only"

    let private attackEntry (entry: CardEntry) energyCost damage effectText =
        let energy = energyCost |> Seq.map pip |> String.concat ""
        let text = effectText |> Option.defaultValue ""

        $"""<section class="rule-entry" data-mechanical-id="{esc entry.Id.Value}" data-entry-kind="Attack"><div class="entry-energy">{energy}</div><p class="entry-copy{nameOnly effectText}"><b class="entry-name">{esc entry.Name}</b>{esc text}</p><strong class="entry-damage" aria-label="{damage} Damage">{damage}</strong></section>"""

    let private noteEntry (entry: CardEntry) =
        let kind, effect =
            match entry with
            | CardEntry.PokemonPower(effectText = effectText) -> "Blokemon Power", effectText
            | CardEntry.Rule(effectText = effectText) -> "Rule", effectText
            | _ -> raise (ArgumentOutOfRangeException(nameof entry))

        $"""<section class="rule-entry is-note" data-mechanical-id="{esc entry.Id.Value}" data-entry-kind="{kind}"><p class="entry-copy"><span class="entry-lead">{kind}:</span><b class="entry-name">{esc entry.Name}</b>{esc effect}</p></section>"""

    let private entryMarkup entry =
        match entry with
        | CardEntry.Attack(energyCost = energyCost; damage = damage; effectText = effectText) ->
            attackEntry entry energyCost damage effectText
        | CardEntry.PokemonPower _
        | CardEntry.Rule _ -> noteEntry entry

    let private mechanics entries =
        let flow = entries |> Seq.map entryMarkup |> String.concat ""

        $"""<div class="rules-flow" data-region="rules-flow">{flow}</div><div class="footer-rule" aria-hidden="true"></div>"""

    let private statValue affinity =
        match affinity with
        | None -> """<span class="stat-value">&#8212;</span>"""
        | Some(affinity: TypeAffinity) ->
            $"""<span class="stat-value"><span class="footer-icon" data-energy="{affinity.Type}" aria-hidden="true">{TypeGlyphs.reference affinity.Type}</span>{esc (affinity.PrintedValue())}</span>"""

    let private retreatPip () =
        $"""<span class="footer-icon" data-energy="Local" aria-label="Local Energy">{TypeGlyphs.reference BlokemonType.Local}</span>"""

    let private retreatValue (retreat: RetreatCost) =
        if retreat.Value = 0 then
            """<span class="stat-value">&#8212;</span>"""
        else
            let pips = String.replicate retreat.Value (retreatPip ())
            $"""<span class="stat-value">{pips}</span>"""

    let private affinities weakness resistance retreat =
        $"""<div class="stat-row" data-region="stat-row"><div class="footer-stat is-weakness"><b>weakness</b>{statValue weakness}</div><div class="footer-stat is-resistance"><b>resistance</b>{statValue resistance}</div><div class="footer-stat is-retreat"><b>retreat cost</b>{retreatValue retreat}</div></div>"""

    let private index index =
        match index with
        | None -> ""
        | Some index -> $""" <span class="card-index">{esc index}</span>"""

    let private flavour flavourText indexLine =
        match flavourText with
        | None -> ""
        | Some flavourText ->
            $"""<div class="flavour-box" data-region="flavour-box"><p class="flavour">{esc flavourText}{index indexLine}</p></div>"""

    let private collectorMark number =
        match number with
        | None -> ""
        | Some(number: CollectorNumber) ->
            $"""<span class="collector-number">{number.PrintedLabel()}</span>"""

    let private rarityImprint rarity =
        match rarity with
        | None -> ""
        | Some rarity ->
            let printed = Rarity.printedLabel rarity

            $"""<span class="rarity-label">{printed}</span><span class="rarity-mark" aria-label="{printed} rarity">{Rarity.mark rarity}</span>"""

    let private colophon flavourText indexLine rarity number =
        $"""{flavour flavourText indexLine}<div class="legal-row" data-region="legal-row">{collectorMark number}{rarityImprint rarity}</div>"""

    let private printedField () =
        """<div class="printed-inner-field" data-region="inner-field" aria-hidden="true"></div>"""

    let private region art region =
        match region with
        | CardRegion.PrintedField -> printedField ()
        | CardRegion.Nameplate(classification = classification; name = name) ->
            nameplate classification name
        | CardRegion.Vitality(points = points; printedType = printedType) ->
            vitality points printedType
        | CardRegion.Lineage(previous = previous) -> lineage previous art
        | CardRegion.Illustration(art = artwork; placement = placement) ->
            illustration artwork placement art
        | CardRegion.Denomination(printedType = printedType) -> denomination printedType
        | CardRegion.IdentityStrip(identity = identity) -> identityStrip identity
        | CardRegion.Mechanics(entries = entries) -> mechanics entries
        | CardRegion.Affinities(weakness = weakness; resistance = resistance; retreat = retreat) ->
            affinities weakness resistance retreat
        | CardRegion.Colophon(
            flavour = flavourText; index = indexLine; rarity = rarity; number = number) ->
            colophon flavourText indexLine rarity number

    /// Prints a card.
    let render (card: Card) art =
        let regions = card.Regions |> Seq.map (region art) |> String.concat ""

        $"""<article class="blokemon-gym-card" data-layout="{layout}"{theme card.ThemeToken}{foil card} data-canonical-id="{esc card.Id.Value}" aria-label="{esc (accessibleName card)}">{regions}</article>"""
