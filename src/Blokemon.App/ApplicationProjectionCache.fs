namespace Blokemon.App

open System
open System.Threading
open Blokemon.App.Contracts

[<Sealed>]
type internal ApplicationProjectionCache
    (catalogueIdentity: string, hooks: ApplicationProjectionHooks) =

    let gate = new SemaphoreSlim(1, 1)
    let identityLock = obj ()
    let counts = Array.zeroCreate<int64> 8
    let mutable cached: CachedApplicationProjection option = None
    let mutable publishedGeneration = Int64.MinValue

    let mutable profileIdentities: (int64 * string * ProfileProjectionIdentities) option =
        None

    let mutable profileIdentityBuilds = 0L

    let changed (left: string) (right: string) =
        not (String.Equals(left, right, StringComparison.Ordinal))

    let observedChanges (left: ApplicationProjectionKeys) (right: ApplicationProjectionKeys) =
        let mutable changes = ApplicationProjectionDependency.None

        let observe dependency leftIdentity rightIdentity =
            if changed leftIdentity rightIdentity then
                changes <- changes ||| dependency

        observe ApplicationProjectionDependency.Catalogue left.Catalogue right.Catalogue

        observe
            ApplicationProjectionDependency.ProfileSummary
            left.ProfileSummary
            right.ProfileSummary

        observe ApplicationProjectionDependency.CardUniverseAndOwnership left.Cards right.Cards

        observe ApplicationProjectionDependency.SavedDecksAndOwnership left.Decks right.Decks

        observe
            ApplicationProjectionDependency.StarterClaimsAndOwnership
            left.StarterDecks
            right.StarterDecks

        observe ApplicationProjectionDependency.PackHistoryAndOwnership left.LastPack right.LastPack

        observe ApplicationProjectionDependency.MatchProfile left.MatchProfile right.MatchProfile

        observe ApplicationProjectionDependency.MatchDocument left.MatchDocument right.MatchDocument

        changes

    let invalidatedDependencies previous keys =
        match previous with
        | None -> ApplicationProjectionDependency.All
        | Some current -> observedChanges current.Keys keys

    let invalidates segment changedDependencies =
        let fieldDependencies = ApplicationProjectionMatrix.dependencies segment

        fieldDependencies &&& changedDependencies
        <> ApplicationProjectionDependency.None

    let invoke (callback: Action | null) =
        match callback with
        | null -> ()
        | value -> value.Invoke()

    member _.CatalogueIdentity = catalogueIdentity

    member _.Hooks = hooks

    member _.ProfileIdentityBuildCount = Volatile.Read(&profileIdentityBuilds)

    member _.ProfileIdentities
        (
            revision: int64,
            contentIdentity: string,
            build: unit -> ProfileProjectionIdentities,
            cancellationToken: CancellationToken
        ) =
        cancellationToken.ThrowIfCancellationRequested()

        lock identityLock (fun () ->
            cancellationToken.ThrowIfCancellationRequested()

            match profileIdentities with
            | Some(cachedRevision, identity, identities) when
                cachedRevision = revision
                && String.Equals(identity, contentIdentity, StringComparison.Ordinal)
                ->
                { Identities = identities
                  Publication = RetainProfileProjectionIdentities }
            | _ ->
                let identities = build ()
                Interlocked.Increment(&profileIdentityBuilds) |> ignore
                invoke hooks.AfterProfileIdentityConstruction
                cancellationToken.ThrowIfCancellationRequested()

                { Identities = identities
                  Publication =
                    ReplaceProfileProjectionIdentities(revision, contentIdentity, identities) })

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
            identityPublication: ProfileProjectionIdentityPublication,
            cancellationToken: CancellationToken
        ) =
        task {
            do! gate.WaitAsync cancellationToken

            try
                invoke hooks.AfterGateAcquired
                cancellationToken.ThrowIfCancellationRequested()

                let previous = cached
                let invalidated = invalidatedDependencies previous keys

                let select segment previousValue build =
                    match previous with
                    | Some _ when not (invalidates segment invalidated) -> previousValue ()
                    | _ ->
                        Interlocked.Increment(&counts[int segment]) |> ignore
                        let value = build ()

                        match hooks.AfterSegmentConstruction with
                        | null -> ()
                        | callback -> callback.Invoke segment

                        cancellationToken.ThrowIfCancellationRequested()
                        value

                let template =
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

                invoke hooks.AfterTemplateConstruction
                cancellationToken.ThrowIfCancellationRequested()
                invoke hooks.BeforeTemplatePublication
                cancellationToken.ThrowIfCancellationRequested()

                if request.Generation >= publishedGeneration then
                    lock identityLock (fun () ->
                        cancellationToken.ThrowIfCancellationRequested()

                        match identityPublication with
                        | RetainProfileProjectionIdentities -> ()
                        | ReplaceProfileProjectionIdentities(revision, identity, identities) ->
                            profileIdentities <- Some(revision, identity, identities)
                        | ClearProfileProjectionIdentities -> profileIdentities <- None

                        cached <- Some { Keys = keys; View = template }
                        publishedGeneration <- request.Generation)

                return ApplicationViewIsolation.application template
            finally
                gate.Release() |> ignore
        }
