namespace Blokemon.App

open System
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.ApplicationViewIsolation
open Blokemon.App.MatchFailures
open Blokemon.App.MatchMigration
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Product
open Blokemon.Game
open Blokemon.Cpu

[<Sealed>]
type LocalMatchService(catalogue: BlokemonCatalogue, documents: IStateDocumentStore) =

    let context: MatchContext =
        { Catalogue = catalogue
          Documents = documents
          Engine = MatchEngine(catalogue.Mechanics)
          Cpu = DeterministicCpu()
          Cached = null }

    /// The saved battle as this player sees it.
    member internal _.StateProjection
        (profile: LocalProfile, displayName: string, cancellationToken: CancellationToken)
        =
        task {
            let! loaded = load context profile cancellationToken

            if not (isNull (box loaded.Error)) then
                return
                    { View = null
                      Error = loaded.Error
                      Recovery =
                        match loaded.Recovery with
                        | None -> null
                        | Some requirement -> recoveryView requirement
                      Presentation = null
                      DocumentIdentity =
                        match loaded.Recovery with
                        | None -> noDocumentProjection
                        | Some requirement ->
                            { Revision = Nullable requirement.Stored.Revision
                              ContentIdentity = DocumentIdentity.ofText requirement.Stored.Json } }
            else
                let view =
                    match loaded.Match with
                    | null -> null
                    | value -> toView context value displayName

                let identity =
                    match loaded.Match with
                    | null -> noDocumentProjection
                    | value -> documentProjection value

                let! history =
                    match loaded.Match with
                    | Null -> historyRecovery context profile cancellationToken
                    | NonNull value when value.State.Phase = MatchPhase.Complete ->
                        historyRecovery context profile cancellationToken
                    | _ -> Task.FromResult(Ok None)

                match history with
                | Error error ->
                    return
                        { View = view
                          Error = error
                          Recovery = null
                          Presentation = null
                          DocumentIdentity = identity }
                | Ok(Some requirement) ->
                    return
                        { View = view
                          Error = recoveryError requirement
                          Recovery = recoveryView requirement
                          Presentation = null
                          DocumentIdentity = identity }
                | Ok None ->
                    return
                        { View = view
                          Error = null
                          Recovery = null
                          Presentation = null
                          DocumentIdentity = identity }
        }

    /// The saved battle as this player sees it.
    member this.State
        (
            profile: LocalProfile,
            displayName: string,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! projection = this.StateProjection(profile, displayName, cancellationToken)
            return matchResult projection
        }

    /// Starts a battle against the computer.
    member internal _.StartProjection
        (
            profile: LocalProfile,
            displayName: string,
            request: StartMatchRequest,
            cancellationToken: CancellationToken
        ) =
        MatchStartFlow.start context profile displayName request cancellationToken

    /// Starts a battle against the computer.
    member this.Start
        (
            profile: LocalProfile,
            displayName: string,
            request: StartMatchRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! projection = this.StartProjection(profile, displayName, request, cancellationToken)

            return matchResult projection
        }

    /// Applies one player move, then lets the computer answer.
    member internal _.ApplyProjection
        (
            profile: LocalProfile,
            displayName: string,
            routeMatchId: Guid,
            request: ApplyMatchActionRequest,
            cancellationToken: CancellationToken
        ) =
        MatchActionFlow.apply context profile displayName routeMatchId request cancellationToken

    /// Applies one player move, then lets the computer answer.
    member this.Apply
        (
            profile: LocalProfile,
            displayName: string,
            routeMatchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! projection =
                this.ApplyProjection(profile, displayName, routeMatchId, request, cancellationToken)

            return matchResult projection
        }

    member internal _.AbandonSavedMatch
        (
            profile: LocalProfile,
            request: AbandonSavedMatchRequest,
            cancellationToken: CancellationToken
        ) =
        MatchRecovery.abandonSavedMatch context profile request cancellationToken

    member internal _.DiscardMatchHistory
        (
            profile: LocalProfile,
            request: DiscardMatchHistoryRequest,
            cancellationToken: CancellationToken
        ) =
        MatchRecovery.discardMatchHistory context profile request cancellationToken

    /// Deletes the saved battle and its history.
    member _.PurgeSavedMatches([<Optional>] cancellationToken: CancellationToken) =
        task {
            do! documents.Delete(matchKey, cancellationToken)
            do! documents.Delete(matchHistoryKey, cancellationToken)
            context.Cached <- null
        }
        :> Task
