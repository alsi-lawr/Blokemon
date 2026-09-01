namespace Blokemon.App.Catalogue

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign
open Blokemon.App.Catalogue.CatalogueDocuments

[<Sealed>]
type BlokemonCatalogue
    private
    (
        mechanicsJson: string,
        starterDecksJson: string,
        mechanics: BlokemonRuntimeManifest,
        publicContentVersion: string,
        starterDecks: StarterDeckCatalogue,
        cardStylesheet: string,
        reverseFaceHtml: string,
        packPresentation: PackPresentationView,
        cards: IReadOnlyDictionary<string, CardView>,
        effects: IReadOnlyDictionary<string, CatalogueEffect>
    ) =

    static let bootstrapSchemaVersion = 2

    static let serializerOptions =
        JsonSerializerOptions(
            JsonSerializerDefaults.Web,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        )

    // CardView is an App.Contracts C# record, so F# cannot copy-and-update it (FS0786): the
    // owned-quantity overlay is written as an explicit construction.
    static let withOwnership (card: CardView) (owned: int) =
        CardView(
            card.Id,
            card.Name,
            card.Kind,
            card.Type,
            card.Detail,
            card.FaceHtml,
            card.Rules,
            owned,
            card.FreelyAvailable
        )

    member _.Mechanics = mechanics

    member _.PublicContentVersion = publicContentVersion

    member _.StarterDecks = starterDecks

    member _.CardStylesheet = cardStylesheet

    member _.ReverseFaceHtml = reverseFaceHtml

    member _.PackPresentation = packPresentation

    member _.StarterRegularId =
        mechanics.Collectibles
        |> Array.filter (fun card -> card.Rank = BlokemonRank.Regular)
        |> Array.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
        |> Array.head
        |> _.Id

    member _.Cards: IReadOnlyCollection<CardView> = cards.Values |> Seq.toArray :> _

    member _.Card(id: string) =
        match cards.TryGetValue id with
        | true, card -> card
        | _ -> raise (InvalidDataException($"The authority does not contain card {id}."))

    member _.EffectName(id: string) =
        match effects.TryGetValue id with
        | true, effect -> effect.Name
        | _ -> id

    member _.EffectText(id: string) : string | null =
        match effects.TryGetValue id with
        | true, effect -> effect.Text
        | _ -> null

    member _.StayingPower(id: string) =
        match mechanics.Collectibles |> Array.tryFind (fun card -> card.Id = id) with
        | Some card -> card.StayingPower
        | None -> 0

    member _.CardsWithOwnership(ownership: IReadOnlyDictionary<string, int>) =
        cards.Values
        |> Seq.map (fun card -> withOwnership card (ownership.GetValueOrDefault(card.Id, 0)))
        |> Seq.sortWith (fun left right ->
            match compare left.Kind right.Kind with
            | 0 -> String.CompareOrdinal(left.Id, right.Id)
            | order -> order)
        |> Seq.toArray

    member _.ToBootstrapJson() =
        JsonSerializer.Serialize(
            { SchemaVersion = bootstrapSchemaVersion
              MechanicsJson = mechanicsJson
              StarterDecksJson = starterDecksJson
              PublicContentVersion = publicContentVersion
              CardStylesheet = cardStylesheet
              ReverseFaceHtml = reverseFaceHtml
              PackPresentation = packPresentation
              Cards =
                cards.Values
                |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
                |> Seq.toArray
              Effects =
                effects.Values
                |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.Id, right.Id))
                |> Seq.toArray },
            serializerOptions
        )

    static member FromBootstrapJson(json: string) =
        let bootstrap =
            try
                JsonSerializer.Deserialize<CatalogueBootstrap>(json, serializerOptions)
            with :? JsonException as damaged ->
                raise (InvalidDataException("The browser card catalogue is damaged.", damaged))

        match bootstrap with
        | null ->
            raise (
                InvalidDataException(
                    "The browser card catalogue is not compatible with this version of Blokemon."
                )
            )
        | document ->
            if document.SchemaVersion <> bootstrapSchemaVersion then
                raise (
                    InvalidDataException(
                        "The browser card catalogue is not compatible with this version of Blokemon."
                    )
                )

            BlokemonCatalogue.Create(
                document.MechanicsJson,
                document.StarterDecksJson,
                document.PublicContentVersion,
                document.CardStylesheet,
                document.ReverseFaceHtml,
                document.PackPresentation,
                document.Cards,
                document.Effects
            )

    static member Create
        (
            mechanicsJson: string,
            starterDecksJson: string,
            publicContentVersion: string,
            cardStylesheet: string,
            reverseFaceHtml: string,
            packPresentation: PackPresentationView,
            cards: IEnumerable<CardView>,
            effects: IEnumerable<CatalogueEffect>
        ) =
        let mechanics = BlokemonSetJson.RuntimeManifest mechanicsJson
        let mechanicsValidation = BlokemonSetValidator.ValidateRuntime mechanics

        if not mechanicsValidation.IsValid then
            raise (
                InvalidDataException(
                    $"The mechanical authority is invalid: {mechanicsValidation.Issues[0].Message}"
                )
            )

        if String.IsNullOrWhiteSpace publicContentVersion then
            raise (InvalidDataException("The public card-content version is missing."))

        let starterDecks = StarterDeckCatalogue.LoadJson(starterDecksJson, mechanics)
        let cardMap = Dictionary<string, CardView>(StringComparer.Ordinal)

        for card in cards do
            cardMap.Add(card.Id, card)

        let expectedCardIds = HashSet<string>(StringComparer.Ordinal)

        for card in mechanics.Collectibles do
            expectedCardIds.Add card.Id |> ignore

        for card in mechanics.Kits do
            expectedCardIds.Add card.Id |> ignore

        for card in mechanics.BasicVim do
            expectedCardIds.Add card.Id |> ignore

        if not (expectedCardIds.SetEquals cardMap.Keys) then
            raise (
                InvalidDataException(
                    "The browser card catalogue does not match the mechanical authority."
                )
            )

        if
            String.IsNullOrWhiteSpace cardStylesheet
            || String.IsNullOrWhiteSpace reverseFaceHtml
        then
            raise (InvalidDataException("The browser card presentation is incomplete."))

        let effectMap = Dictionary<string, CatalogueEffect>(StringComparer.Ordinal)

        for effect in effects do
            effectMap.Add(effect.Id, effect)

        BlokemonCatalogue(
            mechanicsJson,
            starterDecksJson,
            mechanics,
            publicContentVersion,
            starterDecks,
            cardStylesheet,
            reverseFaceHtml,
            packPresentation,
            cardMap,
            effectMap
        )
