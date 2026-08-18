namespace Blokemon.App.Catalogue

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.Core.SetDesign

[<Sealed>]
type StarterDeckCatalogue private (version: string, decks: IReadOnlyDictionary<string, StarterDeck>)
    =

    static let schemaVersion = 1

    static let serializerOptions =
        JsonSerializerOptions(
            JsonSerializerDefaults.Web,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        )

    static let invalid (message: string) =
        InvalidDataException($"The starter-deck authority is invalid: {message}")

    // System.Text.Json can put a null into a required array member and F# types these non-null,
    // so the damaged-document checks go through ReferenceEquals rather than becoming a
    // NullReferenceException on the recovery path.
    static let isMissing (value: obj) = Object.ReferenceEquals(value, null)

    static let validate
        (source: CatalogueDocuments.StarterDeckSource)
        (knownCards: IReadOnlyDictionary<string, CatalogueDocuments.KnownCard>)
        (energyTypes: IReadOnlyDictionary<string, BlokemonMechanicalType>)
        : StarterDeck =
        if
            String.IsNullOrWhiteSpace source.Id
            || source.SavedDeckId = Guid.Empty
            || String.IsNullOrWhiteSpace source.Name
            || String.IsNullOrWhiteSpace source.Type
            || String.IsNullOrWhiteSpace source.Role
            || String.IsNullOrWhiteSpace source.Description
            || String.IsNullOrWhiteSpace source.LeaderCardId
        then
            raise (invalid "Every starter deck requires complete presentation metadata.")

        if isMissing source.Entries || source.Entries.Length = 0 then
            raise (invalid $"Starter deck {source.Id} has no cards.")

        let entries = List<StarterDeckEntry>(source.Entries.Length)
        let seen = HashSet<string>(StringComparer.Ordinal)

        for entry in source.Entries do
            let known =
                if String.IsNullOrWhiteSpace entry.CardId then
                    None
                else
                    match knownCards.TryGetValue entry.CardId with
                    | true, card -> Some card
                    | _ -> None

            match known with
            | None ->
                raise (invalid $"Starter deck {source.Id} contains unknown card {entry.CardId}.")
            | Some card ->
                if not (seen.Add entry.CardId) then
                    raise (invalid $"Starter deck {source.Id} repeats entry {entry.CardId}.")

                if entry.Quantity <= 0 || entry.Quantity > card.CopyLimit then
                    raise (
                        invalid
                            $"Starter deck {source.Id} has an invalid quantity for {entry.CardId}."
                    )

                entries.Add(
                    { CardId = entry.CardId
                      Quantity = entry.Quantity }
                )

        if entries |> Seq.sumBy (fun entry -> entry.Quantity) <> 60 then
            raise (invalid $"Starter deck {source.Id} must contain exactly 60 cards.")

        let energyCount =
            entries
            |> Seq.filter (fun entry ->
                knownCards[entry.CardId].Kind = CatalogueDocuments.KnownCardKind.Energy)
            |> Seq.sumBy (fun entry -> entry.Quantity)

        if energyCount <> 15 then
            raise (invalid $"Starter deck {source.Id} must contain exactly 15 Basic Energy.")

        let hasRegular =
            entries
            |> Seq.exists (fun entry ->
                let card = knownCards[entry.CardId]
                card.Kind = CatalogueDocuments.KnownCardKind.Blokemon && card.IsRegular)

        if not hasRegular then
            raise (invalid $"Starter deck {source.Id} needs a Regular Blokemon.")

        let leaderIsValid =
            match knownCards.TryGetValue source.LeaderCardId with
            | true, leader ->
                leader.Kind = CatalogueDocuments.KnownCardKind.Blokemon
                && seen.Contains source.LeaderCardId
            | _ -> false

        if not leaderIsValid then
            raise (invalid $"Starter deck {source.Id} has an invalid leader.")

        for entry in entries do
            match knownCards[entry.CardId].PromotesFromId with
            | null -> ()
            | parent ->
                if not (seen.Contains parent) then
                    raise (
                        invalid
                            $"Starter deck {source.Id} contains {entry.CardId} without {parent}."
                    )

        let availableEnergy =
            entries
            |> Seq.filter (fun entry -> energyTypes.ContainsKey entry.CardId)
            |> Seq.map (fun entry -> energyTypes[entry.CardId])
            |> HashSet

        let hasPayableAttack =
            entries
            |> Seq.map (fun entry -> knownCards[entry.CardId])
            |> Seq.filter (fun card -> card.Kind = CatalogueDocuments.KnownCardKind.Blokemon)
            |> Seq.collect (fun card -> card.Attacks)
            |> Seq.exists (fun attack ->
                attack.VimCost
                |> Array.forall (fun cost ->
                    cost = BlokemonMechanicalType.Colorless || availableEnergy.Contains cost))

        if not hasPayableAttack then
            raise (invalid $"Starter deck {source.Id} cannot pay for any attack.")

        { Id = source.Id
          SavedDeckId = source.SavedDeckId
          Name = source.Name
          Type = source.Type
          Role = source.Role
          Description = source.Description
          LeaderCardId = source.LeaderCardId
          Entries = entries }

    member _.Version = version

    member _.Decks: IReadOnlyCollection<StarterDeck> = decks.Values |> Seq.toArray :> _

    member _.Deck(id: string) =
        match decks.TryGetValue id with
        | true, deck -> deck
        | _ -> raise (InvalidDataException($"The starter authority does not contain deck {id}."))

    member _.Find(id: string) : StarterDeck | null =
        match decks.TryGetValue id with
        | true, deck -> deck
        | _ -> null

    member this.OpponentFor(selectedStarterId: string | null) =
        match selectedStarterId with
        | "growroom" -> this.Deck "brick-lane-heat"
        | "brick-lane-heat" -> this.Deck "early-shift"
        | "early-shift" -> this.Deck "growroom"
        | _ -> this.Deck "brick-lane-heat"

    static member LoadJson(json: string, mechanics: BlokemonRuntimeManifest) =
        let document =
            match
                JsonSerializer.Deserialize<CatalogueDocuments.StarterDeckDocument>(
                    json,
                    serializerOptions
                )
            with
            | null -> raise (invalid "The document is empty.")
            | value -> value

        if document.SchemaVersion <> schemaVersion then
            raise (invalid $"Schema version {document.SchemaVersion} is not supported.")

        if
            not (
                String.Equals(
                    document.MechanicalManifestVersion,
                    mechanics.ManifestVersion,
                    StringComparison.Ordinal
                )
            )
        then
            raise (invalid "The mechanical manifest version does not match the current authority.")

        if String.IsNullOrWhiteSpace document.StarterDeckVersion then
            raise (invalid "The starter deck version is required.")

        if isMissing document.Decks || document.Decks.Length <> 3 then
            raise (invalid "Exactly three starter decks are required.")

        let knownCards =
            Dictionary<string, CatalogueDocuments.KnownCard>(StringComparer.Ordinal)

        for card in mechanics.Collectibles do
            knownCards.Add(
                card.Id,
                { Id = card.Id
                  Kind = CatalogueDocuments.KnownCardKind.Blokemon
                  CopyLimit = card.StackCopyLimit
                  IsRegular = card.Rank = BlokemonRank.Regular
                  PromotesFromId = card.PromotesFromId
                  Attacks = card.Attacks }
            )

        for card in mechanics.Kits do
            knownCards.Add(
                card.Id,
                { Id = card.Id
                  Kind = CatalogueDocuments.KnownCardKind.Trainer
                  CopyLimit = card.StackCopyLimit
                  IsRegular = false
                  PromotesFromId = null
                  Attacks = Array.empty }
            )

        for card in mechanics.BasicVim do
            knownCards.Add(
                card.Id,
                { Id = card.Id
                  Kind = CatalogueDocuments.KnownCardKind.Energy
                  CopyLimit = card.StackCopyLimit
                  IsRegular = false
                  PromotesFromId = null
                  Attacks = Array.empty }
            )

        let energyTypes = Dictionary<string, BlokemonMechanicalType>(StringComparer.Ordinal)

        for card in mechanics.BasicVim do
            energyTypes.Add(card.Id, card.MechanicalType)

        let decks = Dictionary<string, StarterDeck>(StringComparer.Ordinal)
        let savedDeckIds = HashSet<Guid>()

        for source in document.Decks do
            let deck = validate source knownCards energyTypes

            if not (decks.TryAdd(deck.Id, deck)) then
                raise (invalid $"Starter deck ID {deck.Id} is duplicated.")

            if not (savedDeckIds.Add deck.SavedDeckId) then
                raise (invalid $"Saved deck ID {deck.SavedDeckId:D} is duplicated.")

        StarterDeckCatalogue(document.StarterDeckVersion, decks)
