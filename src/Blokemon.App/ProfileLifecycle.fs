namespace Blokemon.App

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Contracts
open Blokemon.App.ProfileFailures
open Blokemon.App.ProfileStore
open Blokemon.Product

/// The profile itself: what this machine draws before it has one, how it is created, and how
/// every saved document on the machine is deleted.
module internal ProfileLifecycle =

    let state (context: ApplicationContext) (cancellationToken: CancellationToken) =
        let loadProfile = loadProfile context
        let toView = toView context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | null ->
                let! view = toView loaded.Profile cancellationToken null
                return succeeded view
            | error -> return failed<ApplicationView> error
        }

    let createProfile
        (context: ApplicationContext)
        (request: CreateProfileRequest)
        (cancellationToken: CancellationToken)
        =
        let catalogue = context.Catalogue
        let documents = context.Documents
        let economy = context.Economy
        let loadProfile = loadProfile context
        let toView = toView context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | NonNull existing ->
                    if existing.Document.CreationCommandId = request.CommandId then
                        let! view = toView existing cancellationToken null
                        return succeeded view
                    else
                        return
                            failed<ApplicationView> (
                                ApiError(
                                    ProfileExistsCode,
                                    match context.Principal with
                                    | ApplicationPrincipal.BrowserLocal ->
                                        "This machine already has a local profile."
                                    | ApplicationPrincipal.Account _ ->
                                        "This account already has a profile."
                                )
                            )
                | Null ->

                    match DisplayName.Create request.DisplayName with
                    | DomainResult.Failed invalidName ->
                        return
                            failed<ApplicationView> (
                                ApiError(
                                    "profile.display_name",
                                    if invalidName = DisplayNameCreationFailure.TooLong then
                                        "The display name must be 32 characters or fewer."
                                    else
                                        "Enter a display name."
                                )
                            )
                    | DomainResult.Succeeded displayName ->

                        let persistedProfileId = Guid.NewGuid()
                        let profileId = required (ProfileId.Create(persistedProfileId.ToString "D"))

                        match
                            LocalProfile.Create(
                                profileId,
                                displayName,
                                catalogue.Mechanics,
                                economy
                            )
                        with
                        | DomainResult.Failed _ ->
                            return
                                failed<ApplicationView> (
                                    ApiError(
                                        "profile.authority",
                                        "The current card set does not contain a starter Blokemon."
                                    )
                                )
                        | DomainResult.Succeeded profile ->

                            let document =
                                { SchemaVersion = productSchemaVersion
                                  CreationCommandId = request.CommandId
                                  Profile = profile.ToSnapshot() }

                            let documentJson = JsonSerializer.Serialize(document, json)

                            let! write =
                                documents.Create(
                                    context.Keys.Profile,
                                    documentJson,
                                    cancellationToken
                                )

                            match write with
                            | :? DocumentWriteResult.Written as written ->
                                let! view =
                                    toView
                                        { Revision = written.Revision
                                          ContentIdentity = DocumentIdentity.ofText documentJson
                                          Document = document
                                          Profile = profile
                                          Ids =
                                            { Profile = persistedProfileId
                                              Decks = Dictionary<DeckId, Guid>()
                                              PackReceipts = Dictionary<PackReceiptId, Guid>() } }
                                        cancellationToken
                                        null

                                return succeeded view
                            | _ -> return failed<ApplicationView> (conflict ())
        }

    let purgeData (context: ApplicationContext) (cancellationToken: CancellationToken) =
        let documents = context.Documents
        let matches = context.Matches
        let toView = toView context

        task {
            do! matches.PurgeSavedMatches cancellationToken
            do! documents.Delete(context.Keys.Profile, cancellationToken)
            let! view = toView null cancellationToken null
            return succeeded view
        }
