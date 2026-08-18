namespace Blokemon.App

open System
open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Contracts
open Blokemon.App.ProfileStore
open Blokemon.Product

/// The two battle operations the application tier owns: both load the profile, hand the request
/// to the match service, and return the one view the client redraws from.
module internal ApplicationMatchOperations =

    let startMatch
        (context: ApplicationContext)
        (request: StartMatchRequest)
        (cancellationToken: CancellationToken)
        =
        let matches = context.Matches
        let loadProfile = loadProfile context
        let toView = toView context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<MatchMutationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<MatchMutationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before starting a match."
                            )
                        )
                | current ->

                    let! played =
                        matches.Start(
                            current.Profile,
                            current.Profile.DisplayName.Value,
                            request,
                            cancellationToken
                        )

                    match played.Error with
                    | NonNull error -> return failed<MatchMutationView> error
                    | Null ->
                        let! view = toView current cancellationToken played
                        return succeeded (MatchMutationView(view, played.Presentation))
        }

    let applyMatchAction
        (context: ApplicationContext)
        (matchId: Guid)
        (request: ApplyMatchActionRequest)
        (cancellationToken: CancellationToken)
        =
        let matches = context.Matches
        let loadProfile = loadProfile context
        let toView = toView context

        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<MatchMutationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<MatchMutationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before playing a match."
                            )
                        )
                | current ->

                    let! played =
                        matches.Apply(
                            current.Profile,
                            current.Profile.DisplayName.Value,
                            matchId,
                            request,
                            cancellationToken
                        )

                    match played.Error with
                    | NonNull error -> return failed<MatchMutationView> error
                    | Null ->
                        let! view = toView current cancellationToken played
                        return succeeded (MatchMutationView(view, played.Presentation))
        }
