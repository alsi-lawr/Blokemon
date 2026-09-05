namespace Blokemon.App.Tests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Blokemon.App
open Blokemon.App.ApiResponses
open Blokemon.App.Contracts
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private Scripting =

    let answer<'T> (response: ApiError | null) : Task<ApiResponse<'T>> =
        match response with
        | null -> Task.FromResult(ApiResponse<'T>(true, Unchecked.defaultof<'T>, null))
        | error -> Task.FromResult(failed<'T> error)

/// A server whose every operation answers with one scripted response.
type private ScriptedServer(response: ApiError | null) =

    interface IBlokemonApplication with
        member _.State _ = answer response
        member _.CreateProfile(_, _) = answer response
        member _.OpenPack(_, _) = answer response
        member _.ClaimStarterDeck(_, _) = answer response
        member _.SaveDeck(_, _) = answer response
        member _.DeleteDeck(_, _) = answer response
        member _.StartMatch(_, _) = answer response
        member _.ApplyMatchAction(_, _, _) = answer response
        member _.AbandonSavedMatch(_, _) = answer response
        member _.DiscardMatchHistory(_, _) = answer response
        member _.PurgeData _ = answer response

type private RecordingHost() =
    let reasons = List<ReauthenticationReason>()
    member _.Reasons = List.ofSeq reasons

    interface IReauthenticationHost with
        member _.Reauthenticate(reason, _) =
            reasons.Add reason
            Task.CompletedTask

type ReauthenticationTests() =

    let application (server: IBlokemonApplication) (host: IReauthenticationHost) mode =
        task {
            let documents = MemoryDocumentStore()
            let browser = browserLocalApplication documents

            let application =
                PlayModeApplication(server, browser, documents, { ServerBacked = true }, host)

            let! _ = application.SelectMode mode
            return application
        }

    [<Test>]
    member _.``a server refusal of the session should reach the host as the typed reason and the caller unchanged``
        ()
        =
        task {
            for error, expected in
                [ SessionFailures.expired (), ReauthenticationReason.Expired
                  SessionFailures.required (), ReauthenticationReason.Required ] do
                let host = RecordingHost()
                let! application = application (ScriptedServer error) host PlayMode.ServerBacked

                let! response = application.State()

                response.Succeeded |> should be False
                (Unchecked.nonNull response.Error).Code |> should equal error.Code
                host.Reasons |> should equal [ expected ]
        }

    [<Test>]
    member _.``any other server outcome should leave the host alone``() =
        task {
            let host = RecordingHost()

            let! refusing =
                application
                    (ScriptedServer(ApiError("pack.allowance", "No packs.")))
                    host
                    PlayMode.ServerBacked

            let! succeeding = application (ScriptedServer null) host PlayMode.ServerBacked

            let! _ = refusing.OpenPack(OpenPackRequest(Guid.NewGuid()))
            let! _ = succeeding.State()

            host.Reasons |> should be Empty
        }

    [<Test>]
    member _.``the browser-local path should never ask the host to re-authenticate``() =
        task {
            let host = RecordingHost()

            let! application =
                application (ScriptedServer(SessionFailures.required ())) host PlayMode.BrowserLocal

            let! response = application.State()

            response.Succeeded |> should be True
            host.Reasons |> should be Empty
        }
