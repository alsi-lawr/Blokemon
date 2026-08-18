namespace Blokemon.App

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.ProfileFailures
open Blokemon.App.ProfileStore
open Blokemon.Core.SetDesign
open Blokemon.Product

/// Opening a pack and claiming a starter deck. Both are idempotent by command id: a repeated
/// request reports the saved outcome instead of granting twice.
module internal PackAndStarterOperations =

    let packSeed (profileId: ProfileId) (commandId: CommandId) =
        let bytes =
            SHA256.HashData(Encoding.UTF8.GetBytes $"{profileId.Value}:{commandId.Value}")

        BitConverter.ToUInt64 bytes

    let starterDefinition (starter: StarterDeck) =
        StarterDeckDefinition(
            required (StarterDeckId.Create starter.Id),
            required (DeckId.Create(starter.SavedDeckId.ToString "D")),
            required (DeckName.Create starter.Name),
            starter.Entries
            |> Seq.map (fun entry ->
                { CardId = required (CardId.Create entry.CardId)
                  Quantity = entry.Quantity })
        )

    let openPack
        (context: ApplicationContext)
        (request: OpenPackRequest)
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
                            ApiError(
                                "profile.required",
                                "Create a local profile before opening a pack."
                            )
                        )
                | current ->

                    let commandId = required (CommandId.Create(request.CommandId.ToString "D"))
                    let receiptId = required (PackReceiptId.Create(request.CommandId.ToString "D"))

                    let transition =
                        current.Profile.OpenPack(
                            commandId,
                            receiptId,
                            catalogue.Mechanics,
                            BlokemonSeededRandom(packSeed current.Profile.Id commandId)
                        )

                    match transition with
                    | DomainResult.Failed reason ->
                        return failed<ApplicationView> (packFailure reason)
                    | DomainResult.Succeeded opened ->
                        if opened.Disposition = PackOpenDisposition.AlreadyOpened then
                            let! view = toView current cancellationToken null
                            return succeeded view
                        else
                            let updated =
                                { current with
                                    Profile = opened.Profile
                                    Document =
                                        { current.Document with
                                            Profile = opened.Profile.ToSnapshot() } }

                            return! save updated cancellationToken
        }

    let claimStarterDeck
        (context: ApplicationContext)
        (request: ClaimStarterDeckRequest)
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
                            ApiError(
                                "profile.required",
                                "Create a player before you choose a starter deck."
                            )
                        )
                | current ->

                    if request.CommandId = Guid.Empty then
                        return
                            failed<ApplicationView> (
                                ApiError("starter.command_id", "Choose the starter deck again.")
                            )
                    else

                        match catalogue.StarterDecks.Find request.StarterDeckId with
                        | null ->
                            return
                                failed<ApplicationView> (
                                    ApiError(
                                        "starter.not_found",
                                        "Choose one of the available starter decks."
                                    )
                                )
                        | selected ->

                            let commandId =
                                required (CommandId.Create(request.CommandId.ToString "D"))

                            let definition = starterDefinition selected

                            match
                                current.Profile.ClaimStarterDeck(
                                    commandId,
                                    definition,
                                    catalogue.Mechanics
                                )
                            with
                            | DomainResult.Failed reason ->
                                return failed<ApplicationView> (starterFailure reason)
                            | DomainResult.Succeeded(StarterDeckClaimOutcome.AlreadyClaimed _) ->
                                let! view = toView current cancellationToken null
                                return succeeded view
                            | DomainResult.Succeeded(StarterDeckClaimOutcome.Claimed(claimed, _)) ->
                                let updated =
                                    { current with
                                        Profile = claimed
                                        Document =
                                            { current.Document with
                                                Profile = claimed.ToSnapshot() } }

                                return! save updated cancellationToken
        }
