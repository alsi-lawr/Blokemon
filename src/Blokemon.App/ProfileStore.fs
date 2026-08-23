namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Contracts
open Blokemon.App.ProfileFailures
open Blokemon.Product

/// Reading the profile document back under its schema gate, and writing a changed profile at the
/// revision it was read at.
module internal ProfileStore =

    let loadProfile (context: ApplicationContext) (cancellationToken: CancellationToken) =
        let catalogue = context.Catalogue
        let documents = context.Documents

        task {
            let! stored = documents.Read(profileKey, cancellationToken)

            match stored with
            | null -> return { Profile = null; Error = null }
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<ProductDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Error() -> return invalidState ()
                | Ok Null -> return invalidState ()
                | Ok(NonNull value) ->
                    if value.SchemaVersion <> productSchemaVersion then
                        return invalidState ()
                    else
                        match LocalProfile.Restore(value.Profile, catalogue.Mechanics) with
                        | DomainResult.Failed _ -> return invalidState ()
                        | DomainResult.Succeeded restored ->
                            match WebLocalIds.TryCreate restored with
                            | null -> return invalidState ()
                            | ids ->
                                if
                                    String.Equals(
                                        restored.BoundAuthorityManifestVersion,
                                        catalogue.Mechanics.ManifestVersion,
                                        StringComparison.Ordinal
                                    )
                                then
                                    return
                                        { Profile =
                                            { Revision = document.Revision
                                              Document = value
                                              Profile = restored
                                              Ids = ids }
                                          Error = null }
                                else
                                    let migrated = restored.MigrateAuthority catalogue.Mechanics

                                    let migratedDocument =
                                        { value with
                                            Profile = migrated.ToSnapshot() }

                                    let! write =
                                        documents.Update(
                                            profileKey,
                                            document.Revision,
                                            JsonSerializer.Serialize(migratedDocument, json),
                                            cancellationToken
                                        )

                                    match write with
                                    | :? DocumentWriteResult.Written as written ->
                                        return
                                            { Profile =
                                                { Revision = written.Revision
                                                  Document = migratedDocument
                                                  Profile = migrated
                                                  Ids = ids }
                                              Error = null }
                                    | _ -> return { Profile = null; Error = conflict () }
        }

    let save
        (context: ApplicationContext)
        (loaded: LoadedProfile)
        (cancellationToken: CancellationToken)
        =
        let documents = context.Documents
        let toView = toView context

        task {
            match WebLocalIds.TryCreate loaded.Profile with
            | null -> return failed<ApplicationView> (invalidStateError ())
            | ids ->
                let! write =
                    documents.Update(
                        profileKey,
                        loaded.Revision,
                        JsonSerializer.Serialize(loaded.Document, json),
                        cancellationToken
                    )

                match write with
                | :? DocumentWriteResult.Written as written ->
                    let! view =
                        toView
                            { loaded with
                                Revision = written.Revision
                                Ids = ids }
                            cancellationToken
                            null

                    return succeeded view
                | _ -> return failed<ApplicationView> (conflict ())
        }
