namespace Blokemon.App

open System
open System.Collections.Concurrent
open Blokemon.App.Contracts

/// One typed sign-in outcome and how often it happened.
type SignInOutcome = { Code: string; Count: int64 }

/// The sign-in diagnostics an operator reads: how many sessions each exchange issued and how
/// many sign-ins each typed reason refused, since the host started. Counts and codes only; no
/// token, code, subject or credential ever enters.
[<Sealed>]
type SignInDiagnostics(since: DateTimeOffset) =

    let counts = ConcurrentDictionary<string, int64>(StringComparer.Ordinal)

    /// The code a sign-in that issued a session is counted under.
    static member IssuedCode = "session.issued"

    member _.Since = since

    member _.Record(code: string) =
        counts.AddOrUpdate(code, 1L, (fun _ count -> count + 1L)) |> ignore

    /// Counts an exchange's answer: a session issued, or the typed reason it was refused.
    member this.Observe(response: ApiResponse<'T>) =
        if response.Succeeded then
            this.Record SignInDiagnostics.IssuedCode
        else
            match response.Error with
            | null -> ()
            | error -> this.Record error.Code

        response

    /// Every outcome seen, most frequent first, then by code.
    member _.Outcomes: SignInOutcome list =
        counts
        |> Seq.map (fun pair -> { Code = pair.Key; Count = pair.Value })
        |> Seq.sortBy (fun outcome -> -outcome.Count, outcome.Code)
        |> List.ofSeq
