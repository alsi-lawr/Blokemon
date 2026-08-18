namespace Blokemon.App

open System
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.App.Contracts
open Blokemon.Product

/// The profile document's key and options, and every typed failure the application tier returns
/// for a rejected product transition.
module internal ProfileFailures =

    let profileKey = "profile"

    // Version 3 dropped the starter claim's deck snapshot. Older documents fail the version
    // check in LoadProfile and take the damaged-document recovery path; there is no migration.
    let productSchemaVersion = 3

    let json =
        JsonSerializerOptions(
            JsonSerializerDefaults.Web,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        )

    let conflict () =
        ApiError("state.conflict", "The saved data changed. Select the action again.")

    let invalidStateError () =
        ApiError("state.invalid", "The saved player data is damaged. No data changed.")

    let invalidState () : ProfileLoad =
        { Profile = null
          Error = invalidStateError () }

    let required (result: DomainResult<'TValue, TextValueFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed _ -> raise (UnreachableException())

    let deckIssue (issue: DeckValidationIssue) =
        match issue with
        | DeckValidationIssue.QuantityMustBePositive(cardId, _) ->
            $"{cardId.Value} must have a positive quantity."
        | DeckValidationIssue.WrongCardCount(actual, requiredCount) ->
            $"The deck has {actual} cards. It must have {requiredCount} cards."
        | DeckValidationIssue.UnknownCard cardId ->
            $"{cardId.Value} is not in the current card set."
        | DeckValidationIssue.MechanicalCopyLimitExceeded(cardId, actual, allowed) ->
            $"{cardId.Value} has {actual} copies. The limit is {allowed}."
        | DeckValidationIssue.RegularCollectibleRequired ->
            "The deck needs at least one Regular Blokemon."
        | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
            $"{cardId.Value} requests {requested} copies, but only {owned} are owned."
        | DeckValidationIssue.CatalogueCardNotFree cardId ->
            $"{cardId.Value} is not freely available."

    let deckFailure (reason: DeckSaveFailure) =
        match reason with
        | DeckSaveFailure.AlreadyExists _ -> ApiError("deck.exists", "That deck already exists.")
        | DeckSaveFailure.NotFound _ ->
            ApiError("deck.not_found", "The saved deck no longer exists.")
        | DeckSaveFailure.StaleRevision _ ->
            ApiError("deck.stale", "The saved deck changed. Reload the page.")
        | DeckSaveFailure.InvalidDeck issues ->
            ApiError("deck.invalid", String.Join(" ", issues |> Seq.map deckIssue))
        | DeckSaveFailure.RevisionExhausted _ ->
            ApiError("deck.revision", "The saved deck changed. Reload the page.")

    let starterFailure (reason: StarterDeckClaimFailure) =
        match reason with
        | StarterDeckClaimFailure.CommandConflict _ ->
            ApiError(
                "starter.command_conflict",
                "This request conflicts with a saved choice. Choose the starter deck again."
            )
        | StarterDeckClaimFailure.AllowanceExhausted _ ->
            ApiError(
                "starter.already_claimed",
                "This player already opened its Starter Deck. This game allows one."
            )
        | StarterDeckClaimFailure.InvalidDeck issues ->
            ApiError("starter.invalid", String.Join(" ", issues |> Seq.map deckIssue))

    let packFailure (reason: PackOpenFailure) =
        match reason with
        | PackOpenFailure.ReceiptIdAlreadyUsed ->
            ApiError("pack.receipt", "This pack was already opened.")
        | PackOpenFailure.ElevenCardPackUnavailable ->
            ApiError("pack.authority", "The current card set cannot supply an 11-card pack.")
        | PackOpenFailure.AuthorityVersionMismatch ->
            ApiError(
                "pack.authority_changed",
                "The card set changed. Reload the page before you open a pack."
            )
        | PackOpenFailure.PackAllowanceExhausted ->
            ApiError("pack.allowance", "You have opened every pack this player is allowed.")
        | _ -> raise (ArgumentOutOfRangeException(nameof reason))
