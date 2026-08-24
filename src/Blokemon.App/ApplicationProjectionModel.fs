namespace Blokemon.App

open System
open System.Threading
open Blokemon.App.Contracts

[<Flags>]
type internal ApplicationProjectionDependency =
    | None = 0
    | Catalogue = 1
    | ProfileSummary = 2
    | CardUniverseAndOwnership = 4
    | SavedDecksAndOwnership = 8
    | StarterClaimsAndOwnership = 16
    | PackHistoryAndOwnership = 32
    | MatchProfile = 64
    | MatchDocument = 128

type internal ApplicationProjectionSegment =
    | Profile = 0
    | Cards = 1
    | Decks = 2
    | StarterDecks = 3
    | PackPresentation = 4
    | LastPack = 5
    | Match = 6
    | MatchError = 7

type internal ApplicationProjectionOperation =
    | State = 0
    | CreateProfile = 1
    | OpenPack = 2
    | ClaimStarterDeck = 3
    | SaveDeck = 4
    | DeleteDeck = 5
    | StartMatch = 6
    | ApplyMatchAction = 7
    | PurgeData = 8

type internal MatchProjectionSource =
    | LoadSavedMatch = 0
    | UseCommittedMatch = 1
    | NoMatch = 2

type internal ApplicationProjectionFieldRow =
    { Segment: ApplicationProjectionSegment
      Dependencies: ApplicationProjectionDependency }

type internal ApplicationProjectionOperationRow =
    { Operation: ApplicationProjectionOperation
      OwnedChanges: ApplicationProjectionDependency
      MatchSource: MatchProjectionSource }

module internal ApplicationProjectionMatrix =

    let fields =
        [| { Segment = ApplicationProjectionSegment.Profile
             Dependencies = ApplicationProjectionDependency.ProfileSummary }
           { Segment = ApplicationProjectionSegment.Cards
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership }
           { Segment = ApplicationProjectionSegment.Decks
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership }
           { Segment = ApplicationProjectionSegment.StarterDecks
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership }
           { Segment = ApplicationProjectionSegment.PackPresentation
             Dependencies = ApplicationProjectionDependency.Catalogue }
           { Segment = ApplicationProjectionSegment.LastPack
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.PackHistoryAndOwnership }
           { Segment = ApplicationProjectionSegment.Match
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument }
           { Segment = ApplicationProjectionSegment.MatchError
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument } |]

    let operations =
        [| { Operation = ApplicationProjectionOperation.State
             OwnedChanges = ApplicationProjectionDependency.None
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.CreateProfile
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership
               ||| ApplicationProjectionDependency.MatchProfile
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.OpenPack
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership
               ||| ApplicationProjectionDependency.PackHistoryAndOwnership
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.ClaimStarterDeck
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership
               ||| ApplicationProjectionDependency.PackHistoryAndOwnership
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.SaveDeck
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.DeleteDeck
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.StartMatch
             OwnedChanges = ApplicationProjectionDependency.MatchDocument
             MatchSource = MatchProjectionSource.UseCommittedMatch }
           { Operation = ApplicationProjectionOperation.ApplyMatchAction
             OwnedChanges = ApplicationProjectionDependency.MatchDocument
             MatchSource = MatchProjectionSource.UseCommittedMatch }
           { Operation = ApplicationProjectionOperation.PurgeData
             OwnedChanges =
               ApplicationProjectionDependency.ProfileSummary
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership
               ||| ApplicationProjectionDependency.PackHistoryAndOwnership
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument
             MatchSource = MatchProjectionSource.NoMatch } |]

    let dependencies (segment: ApplicationProjectionSegment) = fields[int segment].Dependencies

    let operation (value: ApplicationProjectionOperation) = operations[int value]

type internal ApplicationProjectionRequest =
    { Generation: int64
      Operation: ApplicationProjectionOperation }

type internal ProfileProjectionIdentities =
    { Summary: string
      Cards: string
      Decks: string
      StarterDecks: string
      LastPack: string
      MatchProfile: string }

type internal ApplicationProjectionKeys =
    { Catalogue: string
      ProfileSummary: string
      Cards: string
      Decks: string
      StarterDecks: string
      LastPack: string
      MatchProfile: string
      MatchDocument: string }

type internal ApplicationProjectionBuilders =
    { Profile: unit -> ProfileView | null
      Cards: unit -> CardView array
      Decks: unit -> DeckView array
      StarterDecks: unit -> StarterDeckView array
      PackPresentation: unit -> PackPresentationView
      LastPack: unit -> PackReceiptView | null
      Match: unit -> MatchView | null
      MatchError: unit -> ApiError | null }

[<Sealed>]
type internal ApplicationProjectionBuildCounts
    (
        profile: int64,
        cards: int64,
        decks: int64,
        starterDecks: int64,
        packPresentation: int64,
        lastPack: int64,
        matchView: int64,
        matchError: int64
    ) =

    member _.Profile = profile
    member _.Cards = cards
    member _.Decks = decks
    member _.StarterDecks = starterDecks
    member _.PackPresentation = packPresentation
    member _.LastPack = lastPack
    member _.Match = matchView
    member _.MatchError = matchError

type private CachedApplicationProjection =
    { Keys: ApplicationProjectionKeys
      View: ApplicationView }

[<Sealed>]
type internal ApplicationProjectionCache(catalogueIdentity: string) =
    let gate = new SemaphoreSlim(1, 1)
    let identityLock = obj ()
    let counts = Array.zeroCreate<int64> 8
    let mutable cached: CachedApplicationProjection option = None
    let mutable publishedGeneration = Int64.MinValue

    let mutable profileIdentities: (int64 * string * ProfileProjectionIdentities) option =
        None

    let mutable profileIdentityGeneration = Int64.MinValue

    let uses dependency value = value &&& dependency = dependency

    let sameSources segment (left: ApplicationProjectionKeys) (right: ApplicationProjectionKeys) =
        let dependencies = ApplicationProjectionMatrix.dependencies segment

        (not (uses ApplicationProjectionDependency.Catalogue dependencies)
         || String.Equals(left.Catalogue, right.Catalogue, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.ProfileSummary dependencies)
            || String.Equals(left.ProfileSummary, right.ProfileSummary, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.CardUniverseAndOwnership dependencies)
            || String.Equals(left.Cards, right.Cards, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.SavedDecksAndOwnership dependencies)
            || String.Equals(left.Decks, right.Decks, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.StarterClaimsAndOwnership dependencies)
            || String.Equals(left.StarterDecks, right.StarterDecks, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.PackHistoryAndOwnership dependencies)
            || String.Equals(left.LastPack, right.LastPack, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.MatchProfile dependencies)
            || String.Equals(left.MatchProfile, right.MatchProfile, StringComparison.Ordinal))
        && (not (uses ApplicationProjectionDependency.MatchDocument dependencies)
            || String.Equals(left.MatchDocument, right.MatchDocument, StringComparison.Ordinal))

    member _.CatalogueIdentity = catalogueIdentity

    member _.ProfileIdentities
        (
            request: ApplicationProjectionRequest,
            revision: int64,
            contentIdentity: string,
            build: unit -> ProfileProjectionIdentities
        ) =
        lock identityLock (fun () ->
            match profileIdentities with
            | Some(cachedRevision, identity, identities) when
                cachedRevision = revision
                && String.Equals(identity, contentIdentity, StringComparison.Ordinal)
                ->
                identities
            | _ ->
                let identities = build ()

                if request.Generation >= profileIdentityGeneration then
                    profileIdentities <- Some(revision, contentIdentity, identities)
                    profileIdentityGeneration <- request.Generation

                identities)

    member _.BuildCounts =
        ApplicationProjectionBuildCounts(
            Volatile.Read(&counts[int ApplicationProjectionSegment.Profile]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.Cards]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.Decks]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.StarterDecks]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.PackPresentation]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.LastPack]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.Match]),
            Volatile.Read(&counts[int ApplicationProjectionSegment.MatchError])
        )

    member _.Assemble
        (
            request: ApplicationProjectionRequest,
            keys: ApplicationProjectionKeys,
            builders: ApplicationProjectionBuilders,
            cancellationToken: CancellationToken
        ) =
        task {
            do! gate.WaitAsync cancellationToken

            try
                cancellationToken.ThrowIfCancellationRequested()

                let previous = cached

                let select segment previousValue build =
                    match previous with
                    | Some existing when sameSources segment existing.Keys keys -> previousValue ()
                    | _ ->
                        Interlocked.Increment(&counts[int segment]) |> ignore
                        build ()

                let view =
                    ApplicationView(
                        select
                            ApplicationProjectionSegment.Profile
                            (fun () -> previous.Value.View.Profile)
                            builders.Profile,
                        select
                            ApplicationProjectionSegment.Cards
                            (fun () -> previous.Value.View.Cards)
                            builders.Cards,
                        select
                            ApplicationProjectionSegment.Decks
                            (fun () -> previous.Value.View.Decks)
                            builders.Decks,
                        select
                            ApplicationProjectionSegment.StarterDecks
                            (fun () -> previous.Value.View.StarterDecks)
                            builders.StarterDecks,
                        select
                            ApplicationProjectionSegment.PackPresentation
                            (fun () -> previous.Value.View.PackPresentation)
                            builders.PackPresentation,
                        select
                            ApplicationProjectionSegment.LastPack
                            (fun () -> previous.Value.View.LastPack)
                            builders.LastPack,
                        select
                            ApplicationProjectionSegment.Match
                            (fun () -> previous.Value.View.Match)
                            builders.Match,
                        select
                            ApplicationProjectionSegment.MatchError
                            (fun () -> previous.Value.View.MatchError)
                            builders.MatchError
                    )

                if request.Generation >= publishedGeneration then
                    cached <- Some { Keys = keys; View = view }
                    publishedGeneration <- request.Generation

                return view
            finally
                gate.Release() |> ignore
        }
