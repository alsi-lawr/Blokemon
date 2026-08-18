namespace Blokemon.App

open System.Collections.Generic
open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Contracts
open Blokemon.App.ProfileFailures
open Blokemon.App.ProfileStore
open Blokemon.Product

/// Saving, revising and deleting a deck. A save that asks for exactly what is already stored is
/// answered from the saved deck rather than written again.
module internal DeckOperations =

    let sameDeck (existing: SavedDeck) (name: DeckName) (selections: DeckCardSelection seq) =
        let requested =
            selections
            |> Seq.groupBy _.CardId
            |> Seq.map (fun (cardId, rows) -> cardId, rows |> Seq.sumBy _.Quantity)
            |> dict

        existing.Name = name
        && existing.Cards.Count = requested.Count
        && existing.Cards
           |> Seq.forall (fun entry ->
               let requestedQuantity =
                   match requested.TryGetValue entry.Key with
                   | true, quantity -> quantity
                   | _ -> 0

               requestedQuantity = entry.Value)

    let saveDeck
        (context: ApplicationContext)
        (request: SaveDeckRequest)
        (cancellationToken: CancellationToken)
        =
        let catalogue = context.Catalogue
        let loadProfile = loadProfile context
        let toView = toView context
        let save = save context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError("profile.required", "Create a player before you save a deck.")
                        )
                | current ->

                    match DeckName.Create request.Name with
                    | DomainResult.Failed _ ->
                        return failed<ApplicationView> (ApiError("deck.name", "Enter a deck name."))
                    | DomainResult.Succeeded name ->

                        let deckId =
                            required (
                                DeckId.Create(
                                    (if request.DeckId.HasValue then
                                         request.DeckId.Value
                                     else
                                         request.CommandId)
                                        .ToString
                                        "D"
                                )
                            )

                        let selections = List<DeckCardSelection>(request.Entries.Length)
                        let mutable unknownCard = false

                        for entry in request.Entries do
                            if not unknownCard then
                                match CardId.Create entry.CardId with
                                | DomainResult.Failed _ -> unknownCard <- true
                                | DomainResult.Succeeded cardId ->
                                    selections.Add
                                        { CardId = cardId
                                          Quantity = entry.Quantity }

                        if unknownCard then
                            return
                                failed<ApplicationView> (
                                    ApiError("deck.card_id", "The deck contains an unknown card.")
                                )
                        else

                            let alreadySaved =
                                match current.Profile.SavedDecks.TryGetValue deckId with
                                | true, existing -> sameDeck existing name selections
                                | _ -> false

                            if alreadySaved then
                                let! view = toView current cancellationToken null
                                return succeeded view
                            else

                                let transition =
                                    if not request.DeckId.HasValue then
                                        Ok(
                                            current.Profile.CreateDeck(
                                                deckId,
                                                name,
                                                selections,
                                                catalogue.Mechanics
                                            )
                                        )
                                    elif not request.ExpectedRevision.HasValue then
                                        Error(
                                            ApiError(
                                                "deck.revision",
                                                "The saved deck changed. Reload the page."
                                            )
                                        )
                                    else
                                        match
                                            DeckRevision.Create request.ExpectedRevision.Value
                                        with
                                        | DomainResult.Failed _ ->
                                            Error(
                                                ApiError(
                                                    "deck.revision",
                                                    "The saved deck changed. Reload the page."
                                                )
                                            )
                                        | DomainResult.Succeeded revision ->
                                            Ok(
                                                current.Profile.ReviseDeck(
                                                    deckId,
                                                    revision,
                                                    name,
                                                    selections,
                                                    catalogue.Mechanics
                                                )
                                            )

                                match transition with
                                | Error error -> return failed<ApplicationView> error
                                | Ok(DomainResult.Failed reason) ->
                                    return failed<ApplicationView> (deckFailure reason)
                                | Ok(DomainResult.Succeeded saved) ->
                                    let updated =
                                        { current with
                                            Profile = saved.Profile
                                            Document =
                                                { current.Document with
                                                    Profile = saved.Profile.ToSnapshot() } }

                                    return! save updated cancellationToken
        }

    let deleteDeck
        (context: ApplicationContext)
        (request: DeleteDeckRequest)
        (cancellationToken: CancellationToken)
        =
        let loadProfile = loadProfile context
        let save = save context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError(
                                "profile.required",
                                "Create a player before you delete a deck."
                            )
                        )
                | current ->

                    let deckId = required (DeckId.Create(request.DeckId.ToString "D"))

                    match current.Profile.DeleteDeck deckId with
                    | DomainResult.Failed _ ->
                        return
                            failed<ApplicationView> (
                                ApiError("deck.not_found", "The saved deck no longer exists.")
                            )
                    | DomainResult.Succeeded deleted ->
                        let updated =
                            { current with
                                Profile = deleted.Profile
                                Document =
                                    { current.Document with
                                        Profile = deleted.Profile.ToSnapshot() } }

                        return! save updated cancellationToken
        }
