namespace Blokemon.App

open System
open System.Collections.Generic
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Product

// The persisted profile document carries no JsonRequired annotation, so it stays a plain F#
// record: BLOKEMON-066 proved this exact shape round-trips byte-identically and defaults every
// absent member.
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
type ProductDocument =
    { SchemaVersion: int
      CreationCommandId: Guid
      Profile: LocalProfileSnapshot }

type internal WebLocalIds =
    { Profile: Guid
      Decks: IReadOnlyDictionary<DeckId, Guid>
      PackReceipts: IReadOnlyDictionary<PackReceiptId, Guid> }

    static member TryCreate(profile: LocalProfile) : WebLocalIds | null =
        match Guid.TryParse profile.Id.Value with
        | false, _ -> null
        | true, profileId ->
            let decks = Dictionary<DeckId, Guid>()
            let packReceipts = Dictionary<PackReceiptId, Guid>()

            let deckIdsParsed =
                profile.SavedDecks.Keys
                |> Seq.forall (fun deckId ->
                    match Guid.TryParse deckId.Value with
                    | true, parsed ->
                        decks.Add(deckId, parsed)
                        true
                    | _ -> false)

            let receiptIdsParsed =
                deckIdsParsed
                && profile.PackReceipts.Keys
                   |> Seq.forall (fun receiptId ->
                       match Guid.TryParse receiptId.Value with
                       | true, parsed ->
                           packReceipts.Add(receiptId, parsed)
                           true
                       | _ -> false)

            if receiptIdsParsed then
                { Profile = profileId
                  Decks = decks
                  PackReceipts = packReceipts }
            else
                null

type internal LoadedProfile =
    { Revision: int64
      Document: ProductDocument
      Profile: LocalProfile
      Ids: WebLocalIds }

type internal ProfileLoad =
    { Profile: LoadedProfile | null
      Error: ApiError | null }

// The dependencies one service instance holds.
type internal ApplicationContext =
    { Catalogue: BlokemonCatalogue
      Documents: IStateDocumentStore
      Matches: LocalMatchService
      Economy: EconomyRules }
