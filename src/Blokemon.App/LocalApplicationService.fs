namespace Blokemon.App

open System
open System.Runtime.InteropServices
open System.Threading
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Product

[<Sealed>]
type LocalApplicationService
    (
        catalogue: BlokemonCatalogue,
        documents: IStateDocumentStore,
        principal: ApplicationPrincipal,
        matches: LocalMatchService,
        economy: EconomyRules,
        profileAuthorityPolicy: ProfileAuthorityPolicy
    ) =

    let keys = PlayerDocumentKeys.ofPrincipal principal

    do
        if matches.Keys <> keys then
            raise (
                ArgumentException(
                    "The match service must act on the same player's documents.",
                    nameof matches
                )
            )

    let projectionHooks = ApplicationProjectionHooks()

    let projections =
        ApplicationProjectionCache(
            ApplicationProjectionIdentity.catalogue catalogue,
            projectionHooks
        )

    let mutable projectionGeneration = 0L

    let context operation : ApplicationContext =
        { Catalogue = catalogue
          Documents = documents
          Principal = principal
          Keys = keys
          Matches = matches
          Economy = economy
          ProfileAuthorityPolicy = profileAuthorityPolicy
          Projections = projections
          ProjectionRequest =
            { Generation = Interlocked.Increment(&projectionGeneration)
              Operation = operation } }

    /// The browser-local host's service: anonymous, on the literal keys it has always used.
    new
        (
            catalogue: BlokemonCatalogue,
            documents: IStateDocumentStore,
            matches: LocalMatchService,
            economy: EconomyRules,
            profileAuthorityPolicy: ProfileAuthorityPolicy
        ) =
        LocalApplicationService(
            catalogue,
            documents,
            ApplicationPrincipal.BrowserLocal,
            matches,
            economy,
            profileAuthorityPolicy
        )

    member internal _.ProjectionBuildCounts = projections.BuildCounts

    member internal _.ProjectionHooks = projectionHooks

    member internal _.ProjectionIdentityBuildCount = projections.ProfileIdentityBuildCount

    /// Everything the client draws: the profile, its cards, decks and the saved battle.
    member _.State([<Optional>] cancellationToken: CancellationToken) =
        ProfileLifecycle.state (context ApplicationProjectionOperation.State) cancellationToken

    /// Creates this machine's local profile.
    member _.CreateProfile
        (request: CreateProfileRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        ProfileLifecycle.createProfile
            (context ApplicationProjectionOperation.CreateProfile)
            request
            cancellationToken

    /// Opens one pack for this profile.
    member _.OpenPack(request: OpenPackRequest, [<Optional>] cancellationToken: CancellationToken) =
        PackAndStarterOperations.openPack
            (context ApplicationProjectionOperation.OpenPack)
            request
            cancellationToken

    /// Claims one of the catalogue's starter decks.
    member _.ClaimStarterDeck
        (request: ClaimStarterDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        PackAndStarterOperations.claimStarterDeck
            (context ApplicationProjectionOperation.ClaimStarterDeck)
            request
            cancellationToken

    /// Saves a new deck, or revises a saved one.
    member _.SaveDeck(request: SaveDeckRequest, [<Optional>] cancellationToken: CancellationToken) =
        DeckOperations.saveDeck
            (context ApplicationProjectionOperation.SaveDeck)
            request
            cancellationToken

    /// Deletes a saved deck.
    member _.DeleteDeck
        (request: DeleteDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        DeckOperations.deleteDeck
            (context ApplicationProjectionOperation.DeleteDeck)
            request
            cancellationToken

    /// Starts a battle for this profile.
    member _.StartMatch
        (request: StartMatchRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        ApplicationMatchOperations.startMatch
            (context ApplicationProjectionOperation.StartMatch)
            request
            cancellationToken

    /// Applies one move to the saved battle.
    member _.ApplyMatchAction
        (
            matchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        ApplicationMatchOperations.applyMatchAction
            (context ApplicationProjectionOperation.ApplyMatchAction)
            matchId
            request
            cancellationToken

    member _.AbandonSavedMatch
        (request: AbandonSavedMatchRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        ApplicationMatchRecoveryOperations.abandonSavedMatch
            (context ApplicationProjectionOperation.AbandonSavedMatch)
            request
            cancellationToken

    member _.DiscardMatchHistory
        (request: DiscardMatchHistoryRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        ApplicationMatchRecoveryOperations.discardMatchHistory
            (context ApplicationProjectionOperation.DiscardMatchHistory)
            request
            cancellationToken

    /// Deletes every saved document this machine holds.
    member _.PurgeData([<Optional>] cancellationToken: CancellationToken) =
        ProfileLifecycle.purgeData
            (context ApplicationProjectionOperation.PurgeData)
            cancellationToken

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
