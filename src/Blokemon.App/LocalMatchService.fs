namespace Blokemon.App

open System
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.MatchFailures
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Product
open Blokemon.Game

[<Sealed>]
type LocalMatchService(catalogue: BlokemonCatalogue, documents: IStateDocumentStore) =

    let context: MatchContext =
        { Catalogue = catalogue
          Documents = documents
          Engine = MatchEngine(catalogue.Mechanics)
          Cpu = DeterministicCpu()
          Cached = null }

    /// The saved battle as this player sees it.
    member _.State
        (
            profile: LocalProfile,
            displayName: string,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! loaded = load context profile cancellationToken

            if not (isNull (box loaded.Error)) then
                return
                    { View = null
                      Error = loaded.Error
                      Presentation = null }
            else
                return
                    { View =
                        match loaded.Match with
                        | null -> null
                        | value -> toView context value displayName
                      Error = null
                      Presentation = null }
        }

    /// Starts a battle against the computer.
    member _.Start
        (
            profile: LocalProfile,
            displayName: string,
            request: StartMatchRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        MatchStartFlow.start context profile displayName request cancellationToken

    /// Applies one player move, then lets the computer answer.
    member _.Apply
        (
            profile: LocalProfile,
            displayName: string,
            routeMatchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        MatchActionFlow.apply context profile displayName routeMatchId request cancellationToken

    /// Deletes the saved battle and its history.
    member _.PurgeSavedMatches([<Optional>] cancellationToken: CancellationToken) =
        task {
            do! documents.Delete(matchKey, cancellationToken)
            do! documents.Delete(matchHistoryKey, cancellationToken)
            context.Cached <- null
        }
        :> Task
