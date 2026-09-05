namespace Blokemon.App

open System
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.ApiResponses

/// Where a player's game is saved.
type PlayMode =
    | ServerBacked = 0
    | BrowserLocal = 1

/// Which save locations this build offers.
type PlayModeAvailability =
    { ServerBacked: bool }

    // The C# sealed record this replaces carried compiler-generated structural operators, and an
    // F# record emits none: a C# `==` against it would silently fall back to reference equality.
    static member op_Equality(left: PlayModeAvailability, right: PlayModeAvailability) =
        left.Equals right

    static member op_Inequality(left: PlayModeAvailability, right: PlayModeAvailability) =
        not (left.Equals right)

/// Where this player's game is saved, and what went wrong if the browser could not say.
type PlayModeState =
    { Selected: Nullable<PlayMode>
      StorageLocation: string | null
      BrowserStorageError: string | null
      ServerBackedAvailable: bool }

    static member op_Equality(left: PlayModeState, right: PlayModeState) = left.Equals right

    static member op_Inequality(left: PlayModeState, right: PlayModeState) = not (left.Equals right)

/// The saved save-location choice. [<CLIMutable>] is what lets the fields carry
/// [<property: JsonRequired>]; see .agent-workspace/068/probe-and-censuses.md leg (a0).
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
[<CLIMutable>]
type BrowserSettings =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      Mode: PlayMode }

/// The host's way of obtaining a new session when the server refuses the one it holds. Which
/// mechanism does that is the host's own: this tier names no provider and no parent page.
type IReauthenticationHost =
    abstract Reauthenticate:
        reason: ReauthenticationReason * cancellationToken: CancellationToken -> Task

module ReauthenticationHost =

    /// A host with nothing to do; the typed error still reaches the caller.
    let none =
        { new IReauthenticationHost with
            member _.Reauthenticate(_, _) = Task.CompletedTask }

[<Sealed>]
type PlayModeApplication
    (
        server: IBlokemonApplication,
        browser: IBlokemonApplication,
        browserDocuments: IStateDocumentStore,
        availability: PlayModeAvailability,
        reauthentication: IReauthenticationHost
    ) =

    static let settingsKey = "settings"
    static let settingsSchemaVersion = 1

    static let json =
        JsonSerializerOptions(
            JsonSerializerDefaults.Web,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        )

    static let storageCode (failure: DocumentStorageFailure) =
        match failure with
        | DocumentStorageFailure.Full -> "storage.full"
        | DocumentStorageFailure.Rejected -> "storage.rejected"
        | _ -> "storage.unavailable"

    static let storageMessage (failure: DocumentStorageFailure) =
        match failure with
        | DocumentStorageFailure.Full ->
            "This browser is out of storage. Your last saved game is unchanged."
        | DocumentStorageFailure.Rejected ->
            "This browser refused access to its storage. Your last saved game is unchanged."
        | _ -> "Browser storage is unavailable. Your last saved game is unchanged."

    let stateLock = new SemaphoreSlim(1, 1)
    let mutable loaded = false
    let mutable settingsRevision = Nullable<int64>()
    let mutable selected = Nullable<PlayMode>()
    let mutable browserStorageError: string | null = null

    let current () =
        { Selected = selected
          StorageLocation =
            if not selected.HasValue then
                null
            else
                match selected.Value with
                | PlayMode.BrowserLocal -> "Saved in this browser"
                | PlayMode.ServerBacked -> "Saved on this server"
                | _ -> null
          BrowserStorageError = browserStorageError
          ServerBackedAvailable = availability.ServerBacked }

    let readSettings (stored: StoredDocument) =
        let settings =
            try
                JsonSerializer.Deserialize<BrowserSettings>(stored.Json, json)
            with :? JsonException ->
                null

        match settings with
        | null ->
            browserStorageError <-
                "The saved browser settings are damaged or incompatible. No data changed."
        | value when value.SchemaVersion <> settingsSchemaVersion || not (Enum.IsDefined value.Mode) ->
            browserStorageError <-
                "The saved browser settings are damaged or incompatible. No data changed."
        | value ->
            settingsRevision <- Nullable stored.Revision

            if value.Mode <> PlayMode.ServerBacked || availability.ServerBacked then
                selected <- Nullable value.Mode

    let ensureLoaded (cancellationToken: CancellationToken) =
        task {
            if not loaded then
                do! stateLock.WaitAsync cancellationToken

                try
                    if not loaded then
                        try
                            let! stored = browserDocuments.Read(settingsKey, cancellationToken)

                            match stored with
                            | null -> ()
                            | document -> readSettings document
                        with :? DocumentStorageException as storage ->
                            browserStorageError <- storageMessage storage.Failure

                        loaded <- true
                finally
                    stateLock.Release() |> ignore
        }

    let invoke
        (operation: IBlokemonApplication -> CancellationToken -> Task<ApiResponse<'T>>)
        (cancellationToken: CancellationToken)
        =
        task {
            do! ensureLoaded cancellationToken

            if not selected.HasValue then
                return
                    failed<'T> (
                        ApiError(
                            "mode.required",
                            "Choose where to save your game on the Home page."
                        )
                    )
            else
                try
                    if selected.Value = PlayMode.BrowserLocal then
                        return! operation browser cancellationToken
                    else
                        let! response = operation server cancellationToken

                        // The server's typed refusal of the session becomes the host's cue to
                        // obtain a new one; the refusal itself still reaches the caller.
                        let reason =
                            if response.Succeeded then
                                Nullable()
                            else
                                SessionFailures.reauthentication response.Error

                        if reason.HasValue then
                            do! reauthentication.Reauthenticate(reason.Value, cancellationToken)

                        return response
                with :? DocumentStorageException as storage ->
                    return
                        failed<'T> (
                            ApiError(storageCode storage.Failure, storageMessage storage.Failure)
                        )
        }

    /// A play-mode application whose host has no way to re-authenticate: the browser-local
    /// hosts and the tests that drive the mode choice.
    new
        (
            server: IBlokemonApplication,
            browser: IBlokemonApplication,
            browserDocuments: IStateDocumentStore,
            availability: PlayModeAvailability
        ) =
        PlayModeApplication(
            server,
            browser,
            browserDocuments,
            availability,
            ReauthenticationHost.none
        )

    /// Where this player's game is saved.
    member _.Mode([<Optional>] cancellationToken: CancellationToken) =
        task {
            do! ensureLoaded cancellationToken
            return current ()
        }

    /// Chooses where this player's game is saved.
    member _.SelectMode(mode: PlayMode, [<Optional>] cancellationToken: CancellationToken) =
        task {
            do! ensureLoaded cancellationToken

            if mode = PlayMode.ServerBacked && not availability.ServerBacked then
                return
                    failed<PlayModeState> (
                        ApiError("mode.unavailable", "This build saves games in this browser only.")
                    )
            elif selected.HasValue && selected.Value = mode then
                return succeeded (current ())
            else

                match browserStorageError with
                | NonNull storageError ->
                    if mode = PlayMode.ServerBacked && not settingsRevision.HasValue then
                        selected <- Nullable mode
                        return succeeded (current ())
                    else
                        return failed<PlayModeState> (ApiError("storage.unavailable", storageError))
                | Null ->
                    let settings =
                        JsonSerializer.Serialize(
                            { SchemaVersion = settingsSchemaVersion
                              Mode = mode },
                            json
                        )

                    try
                        let! write =
                            if settingsRevision.HasValue then
                                browserDocuments.Update(
                                    settingsKey,
                                    settingsRevision.Value,
                                    settings,
                                    cancellationToken
                                )
                            else
                                browserDocuments.Create(settingsKey, settings, cancellationToken)

                        match write with
                        | :? DocumentWriteResult.Written as written ->
                            settingsRevision <- Nullable written.Revision
                            selected <- Nullable mode
                            return succeeded (current ())
                        | _ ->
                            return
                                failed<PlayModeState> (
                                    ApiError(
                                        "settings.changed",
                                        "Your save choice changed in another tab. Reload and choose again."
                                    )
                                )
                    with :? DocumentStorageException as storage ->
                        let message = storageMessage storage.Failure

                        if mode = PlayMode.ServerBacked && not settingsRevision.HasValue then
                            browserStorageError <- message
                            selected <- Nullable mode
                            return succeeded (current ())
                        else
                            return
                                failed<PlayModeState> (
                                    ApiError(storageCode storage.Failure, message)
                                )
        }

    // The eleven IBlokemonApplication members are duplicated as concrete members because both
    // hosts inject and call this type through its concrete form.
    member _.State([<Optional>] cancellationToken: CancellationToken) =
        invoke (fun application token -> application.State token) cancellationToken

    member _.CreateProfile
        (request: CreateProfileRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke
            (fun application token -> application.CreateProfile(request, token))
            cancellationToken

    member _.OpenPack(request: OpenPackRequest, [<Optional>] cancellationToken: CancellationToken) =
        invoke (fun application token -> application.OpenPack(request, token)) cancellationToken

    member _.ClaimStarterDeck
        (request: ClaimStarterDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke
            (fun application token -> application.ClaimStarterDeck(request, token))
            cancellationToken

    member _.SaveDeck(request: SaveDeckRequest, [<Optional>] cancellationToken: CancellationToken) =
        invoke (fun application token -> application.SaveDeck(request, token)) cancellationToken

    member _.DeleteDeck
        (request: DeleteDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke (fun application token -> application.DeleteDeck(request, token)) cancellationToken

    member _.StartMatch
        (request: StartMatchRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke (fun application token -> application.StartMatch(request, token)) cancellationToken

    member _.ApplyMatchAction
        (
            matchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        invoke
            (fun application token -> application.ApplyMatchAction(matchId, request, token))
            cancellationToken

    member _.AbandonSavedMatch
        (request: AbandonSavedMatchRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke
            (fun application token -> application.AbandonSavedMatch(request, token))
            cancellationToken

    member _.DiscardMatchHistory
        (request: DiscardMatchHistoryRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        invoke
            (fun application token -> application.DiscardMatchHistory(request, token))
            cancellationToken

    member _.PurgeData([<Optional>] cancellationToken: CancellationToken) =
        invoke (fun application token -> application.PurgeData token) cancellationToken

    interface IBlokemonApplication with
        member this.State cancellationToken = this.State cancellationToken

        member this.CreateProfile(request, cancellationToken) =
            this.CreateProfile(request, cancellationToken)

        member this.OpenPack(request, cancellationToken) =
            this.OpenPack(request, cancellationToken)

        member this.ClaimStarterDeck(request, cancellationToken) =
            this.ClaimStarterDeck(request, cancellationToken)

        member this.SaveDeck(request, cancellationToken) =
            this.SaveDeck(request, cancellationToken)

        member this.DeleteDeck(request, cancellationToken) =
            this.DeleteDeck(request, cancellationToken)

        member this.StartMatch(request, cancellationToken) =
            this.StartMatch(request, cancellationToken)

        member this.ApplyMatchAction(matchId, request, cancellationToken) =
            this.ApplyMatchAction(matchId, request, cancellationToken)

        member this.AbandonSavedMatch(request, cancellationToken) =
            this.AbandonSavedMatch(request, cancellationToken)

        member this.DiscardMatchHistory(request, cancellationToken) =
            this.DiscardMatchHistory(request, cancellationToken)

        member this.PurgeData cancellationToken = this.PurgeData cancellationToken
