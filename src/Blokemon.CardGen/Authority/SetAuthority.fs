namespace Blokemon.CardGen.Authority

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.IO
open System.Text.Json
open Blokemon.CardGen.Domain
open Blokemon.Core.PublicContent
open Blokemon.Core.SetDesign

/// The complete printed set.
type CardSet =
    {
        /// The collectible fronts.
        Blokemon: ImmutableArray<Card>

        /// The Support fronts.
        Supports: ImmutableArray<Card>

        /// The Basic Energy fronts.
        Energy: ImmutableArray<Card>

        /// The shared reverse.
        Reverse: Card
    }

// The authority documents System.Text.Json reads. They are public because the serialiser binds
// public constructors only, and F# ties a record's constructor accessibility to the record's own;
// grouping them keeps their role as file shapes rather than domain types legible.
module AuthorityDocument =

    /// One collectible's printed physical description, as the profiles authority carries it.
    type ProfileEntry =
        { Subtype: string
          Feet: int
          Inches: int
          Pounds: int }

    /// One collectible's Gen 1 printing, as the printing authority carries it.
    type PrintingRow =
        { Id: string
          Gen1Set: string
          Gen1Rarity: string }

    /// The printing authority.
    type PrintingDocument =
        { Collectibles: ImmutableArray<PrintingRow> }

/// The printed run positions and the holographic collectibles.
type private PrintingIndex =
    { Numbers: ImmutableDictionary<string, CollectorNumber>
      Holo: ImmutableHashSet<string> }

/// Reads the set authorities into cards.
module SetAuthority =

    let private json = JsonSerializerOptions(JsonSerializerDefaults.Web)

    /// Indexes pairs, rejecting the duplicated key the authorities forbid.
    let private indexed (pairs: ('key * 'value) seq) =
        pairs
        |> Seq.map (fun (key, value) -> KeyValuePair(key, value))
        |> ImmutableDictionary.CreateRange

    let private tryFind (index: ImmutableDictionary<string, 'value>) key =
        match index.TryGetValue key with
        | true, value -> Some value
        | _ -> None

    let private number (ordered: string seq) =
        let run = ImmutableArray.CreateRange ordered

        run
        |> Seq.mapi (fun index id -> id, CollectorNumber.create (index + 1) run.Length)
        |> indexed

    // Base Set's sixteen holo rares, plus Mew, whose only Gen 1 printing was a holo promo.
    let private isHolo (row: AuthorityDocument.PrintingRow) =
        row.Gen1Rarity = "Promo"
        || (row.Gen1Set = "Base Set" && row.Gen1Rarity = "Rare Holo")

    let private position (runtimeId: string) =
        let printed = runtimeId.Substring(runtimeId.LastIndexOf('-') + 1)
        Int32.Parse(printed, CultureInfo.InvariantCulture)

    let private loadPrinting (printingManifestPath: string) =
        let printing =
            JsonSerializer.Deserialize<AuthorityDocument.PrintingDocument>(
                File.ReadAllBytes printingManifestPath,
                json
            )
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                raise (
                    InvalidDataException $"Unreadable printing authority at {printingManifestPath}"
                ))

        let total = printing.Collectibles.Length

        let holo = printing.Collectibles |> Seq.filter isHolo |> Seq.map (fun row -> row.Id)

        { Numbers =
            printing.Collectibles
            |> Seq.map (fun row -> row.Id, CollectorNumber.create (position row.Id) total)
            |> indexed
          Holo = ImmutableHashSet.CreateRange(StringComparer.Ordinal, holo) }

    let private loadProfiles () =
        let path =
            Path.Combine(AppContext.BaseDirectory, "Content", "blokemon-profiles.json")

        let entries =
            JsonSerializer.Deserialize<ImmutableDictionary<string, AuthorityDocument.ProfileEntry>>(
                File.ReadAllBytes path,
                json
            )
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                raise (InvalidDataException $"Unreadable profiles at {path}"))

        entries
        |> Seq.map (fun entry ->
            entry.Key,
            { Subtype = entry.Value.Subtype
              Stature =
                { Feet = entry.Value.Feet
                  Inches = entry.Value.Inches
                  Pounds = entry.Value.Pounds } })
        |> indexed

    let private toRarity (productBucket: BlokemonProductBucket) holo (id: CardId) =
        let printed = Enum.Parse<Rarity>(productBucket.ToString())

        if not holo then
            printed
        // Holo sits above Rare, so a holo card must not sit in a lesser bucket.
        elif printed = Rarity.Rare then
            Rarity.RareHolo
        else
            raise (InvalidDataException $"{id} is holo but its product bucket is {printed}")

    let private toStage rank =
        match rank with
        | BlokemonRank.Regular -> Stage.Basic
        | BlokemonRank.Seasoned -> Stage.StageOne
        | BlokemonRank.Landlord -> Stage.StageTwo
        | _ -> raise (ArgumentOutOfRangeException(nameof rank))

    let private toAffinity
        (affinities: BlokemonMechanicalTypeModifier[])
        (label: ImmutableDictionary<BlokemonMechanicalType, BlokemonType>)
        =
        if affinities.Length = 0 then
            None
        else
            Some
                { Type = label[affinities[0].MechanicalType]
                  Modifier = affinities[0].Modifier }

    let private toPrevious promotesFromId (art: ArtIndex) names supportNames =
        match promotesFromId with
        | None -> None
        | Some promotesFromId ->
            // A few collectibles promote from a Support that is played as a stand-in Basic.
            let name =
                tryFind names promotesFromId
                |> Option.orElseWith (fun () -> tryFind supportNames promotesFromId)
                |> Option.defaultWith (fun () ->
                    raise (InvalidDataException $"Unknown previous stage {promotesFromId}"))

            Some
                { Id = CardId.create promotesFromId
                  Name = name
                  Art = art.For(promotesFromId, $"Previous stage {name} thumbnail.") }

    let private effectText text =
        Option.ofObj text |> Option.defaultValue ""

    let private toEntries
        (published: BlokemonPublicCollectible)
        (mechanical: BlokemonCollectible)
        (label: ImmutableDictionary<BlokemonMechanicalType, BlokemonType>)
        =
        let costs =
            mechanical.Attacks
            |> Seq.map (fun attack -> attack.MechanicalId, attack)
            |> indexed

        let abilities =
            [ for ability in published.Abilities ->
                  CardEntry.ability
                      (MechanicalId.create ability.MechanicalId)
                      ability.Name
                      (effectText ability.EffectText) ]

        let attacks =
            [ for attack in published.Attacks ->
                  let cost = costs[attack.MechanicalId]
                  let energy = cost.VimCost |> Seq.map (fun vim -> label[vim])

                  CardEntry.attack
                      (MechanicalId.create attack.MechanicalId)
                      attack.Name
                      (ImmutableArray.CreateRange energy)
                      (Damage.create cost.PrintedDamage)
                      (Option.ofObj attack.EffectText) ]

        let rules =
            [ for rule in published.Rules ->
                  CardEntry.rule
                      (MechanicalId.create rule.MechanicalId)
                      rule.Name
                      (effectText rule.EffectText) ]

        ImmutableArray.CreateRange(abilities @ attacks @ rules)

    /// A card printed in the atmosphere its type carries.
    let private themed (printedType: BlokemonType) id regions =
        { Id = id
          Regions = ImmutableArray.CreateRange(regions: CardRegion list)
          ThemeToken = Some(printedType.ToString()) }

    let private toBlokemon
        (published: BlokemonPublicCollectible)
        (mechanical: BlokemonCollectible)
        label
        (art: ArtIndex)
        names
        supportNames
        (profile: CardProfile)
        number
        holo
        =
        let printedType = Enum.Parse<BlokemonType>(published.ApprovedType.ToString())
        let stage = toStage mechanical.Rank

        let previous =
            toPrevious (Option.ofObj mechanical.PromotesFromId) art names supportNames

        let prizes = PrizeCards.create mechanical.BarChitsWhenSentHome

        let lineage =
            match previous with
            | None -> []
            | Some previous -> [ CardRegion.Lineage previous ]

        let face =
            [ CardRegion.Nameplate(
                  Some(Stage.classificationLabel stage (Option.isSome previous)),
                  published.ApprovedName
              )
              CardRegion.Vitality(Some(HitPoints.create mechanical.StayingPower), printedType)
              CardRegion.Illustration(
                  art.For(published.Id, published.Illustration.AltIntent),
                  IllustrationPlacement.Framed
              )
              CardRegion.IdentityStrip(profile.PrintedIdentity())
              CardRegion.Mechanics(toEntries published mechanical label)
              CardRegion.Affinities(
                  toAffinity mechanical.SoftSpots label,
                  toAffinity mechanical.StubbornStreaks label,
                  RetreatCost.create mechanical.TaxiFare
              )
              CardRegion.Colophon(
                  Some published.FlavourText,
                  Some $"{published.Id} · {prizes.PrintedLabel()}",
                  Some(toRarity mechanical.ProductBucket holo (CardId.create published.Id)),
                  Some number
              ) ]

        themed
            printedType
            (CardId.create published.Id)
            ([ CardRegion.PrintedField ] @ lineage @ face)

    let private toSupport
        (published: BlokemonPublicSupport)
        (category: string)
        (art: ArtIndex)
        number
        =
        let effects =
            [ for effect in published.Effects ->
                  CardEntry.rule
                      (MechanicalId.create effect.MechanicalId)
                      effect.Name
                      (effectText effect.EffectText) ]

        { Id = CardId.create published.Id
          ThemeToken = Some "Support"
          Regions =
            ImmutableArray.CreateRange
                [ CardRegion.PrintedField
                  CardRegion.Nameplate(Some category, published.Name)
                  CardRegion.Illustration(
                      art.For(published.Id, $"{published.Name} Support illustration."),
                      IllustrationPlacement.Framed
                  )
                  CardRegion.IdentityStrip $"{category} · {published.Id}"
                  CardRegion.Mechanics(ImmutableArray.CreateRange effects)
                  CardRegion.Colophon(
                      Some
                          $"{category} card. Played from hand, then discarded unless its own text says otherwise.",
                      Some published.Id,
                      Some Rarity.Uncommon,
                      Some number
                  ) ] }

    let private toEnergy (published: BlokemonPublicBasicEnergy) (art: ArtIndex) number =
        let printedType =
            Enum.Parse<BlokemonType>(published.Id.Split('-')[1], ignoreCase = true)

        themed
            printedType
            (CardId.create published.Id)
            [ CardRegion.PrintedField
              CardRegion.Vitality(None, printedType)
              CardRegion.Nameplate(None, "Energy")
              CardRegion.Illustration(
                  art.ForSymbol(published.SymbolKey, $"{published.Name} Basic Energy field."),
                  IllustrationPlacement.Field
              )
              CardRegion.Denomination printedType
              CardRegion.Colophon(None, None, None, Some number) ]

    let private toReverse (art: ArtIndex) =
        { Id = CardId.create "REVERSE"
          ThemeToken = None
          Regions =
            ImmutableArray.CreateRange
                [ CardRegion.Illustration(
                      art.ForSymbol("card-back", "Blokemon card reverse."),
                      IllustrationPlacement.FullBleed
                  ) ] }

    /// Reads the set authorities into the printed set.
    let Load
        (publicContentPath: string)
        (mechanicsPath: string)
        (printingManifestPath: string)
        (artDirectory: string)
        =
        let content = BlokemonPublicContentJson.Manifest(File.ReadAllText publicContentPath)
        let mechanics = BlokemonSetJson.RuntimeManifest(File.ReadAllText mechanicsPath)

        let label =
            mechanics.ApprovedMechanicalDisplayMap
            |> Seq.map (fun entry ->
                entry.MechanicalType, Enum.Parse<BlokemonType>(entry.ApprovedLabel.ToString()))
            |> indexed

        let art = ArtIndex.Scan artDirectory

        let mechanical =
            mechanics.Collectibles
            |> Seq.map (fun collectible -> collectible.Id, collectible)
            |> indexed

        let names =
            content.Collectibles
            |> Seq.map (fun card -> card.Id, card.ApprovedName)
            |> indexed

        let supportNames =
            content.Supports |> Seq.map (fun support -> support.Id, support.Name) |> indexed

        let category =
            content.Terminology |> Seq.map (fun term -> term.Id, term.Singular) |> indexed

        let profiles = loadProfiles ()
        let printing = loadPrinting printingManifestPath

        let supportNumbers =
            content.Supports
            |> List.ofArray
            |> List.sortWith (fun left right -> String.CompareOrdinal(left.Name, right.Name))
            |> List.map (fun support -> support.Id)
            |> number

        let energyNumbers =
            content.BasicEnergy
            |> List.ofArray
            |> List.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
            |> List.map (fun energy -> energy.Id)
            |> number

        { Blokemon =
            ImmutableArray.CreateRange
                [ for card in content.Collectibles ->
                      toBlokemon
                          card
                          mechanical[card.Id]
                          label
                          art
                          names
                          supportNames
                          profiles[card.Id]
                          printing.Numbers[card.Id]
                          (printing.Holo.Contains card.Id) ]
          Supports =
            ImmutableArray.CreateRange
                [ for support in content.Supports ->
                      toSupport
                          support
                          category[support.CategoryTermId]
                          art
                          supportNumbers[support.Id] ]
          Energy =
            ImmutableArray.CreateRange
                [ for energy in content.BasicEnergy -> toEnergy energy art energyNumbers[energy.Id] ]
          Reverse = toReverse art }
