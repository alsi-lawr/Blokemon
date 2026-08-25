namespace Blokemon.App

open System.Threading
open Blokemon.App.ApiResponses
open Blokemon.App.ApplicationViewAssembly
open Blokemon.App.Contracts
open Blokemon.App.ProfileStore

module internal ApplicationMatchRecoveryOperations =

    let private recover
        (context: ApplicationContext)
        operation
        (cancellationToken: CancellationToken)
        =
        task {
            let! loaded = loadProfile context cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->
                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before recovering saved battles."
                            )
                        )
                | current ->
                    let! recovered = operation current.Profile cancellationToken

                    match recovered with
                    | Error error -> return failed<ApplicationView> error
                    | Ok() ->
                        let! view = toView context current cancellationToken null
                        return succeeded view
        }

    let abandonSavedMatch
        (context: ApplicationContext)
        (request: AbandonSavedMatchRequest)
        (cancellationToken: CancellationToken)
        =
        recover
            context
            (fun profile token -> context.Matches.AbandonSavedMatch(profile, request, token))
            cancellationToken

    let discardMatchHistory
        (context: ApplicationContext)
        (request: DiscardMatchHistoryRequest)
        (cancellationToken: CancellationToken)
        =
        recover
            context
            (fun profile token -> context.Matches.DiscardMatchHistory(profile, request, token))
            cancellationToken
