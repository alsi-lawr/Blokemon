namespace Blokemon.App

open System
open System.Collections.Generic

/// Refuses a client after `limit` failures inside a sliding `window`. Recovery and operator
/// bootstrap share the same terms: five failures per client per fifteen minutes.
[<Sealed>]
type FailureLockout(limit: int, window: TimeSpan) =

    let failures = Dictionary<string, Queue<DateTimeOffset>>(StringComparer.Ordinal)
    let gate = obj ()

    let prune (client: string) (now: DateTimeOffset) =
        match failures.TryGetValue client with
        | true, recorded ->
            while recorded.Count > 0 && recorded.Peek() <= now - window do
                recorded.Dequeue() |> ignore

            if recorded.Count = 0 then
                failures.Remove client |> ignore

            recorded.Count
        | _ -> 0

    /// The terms recovery and operator bootstrap are held to.
    static member OnRecoveryTerms() =
        FailureLockout(5, TimeSpan.FromMinutes 15.0)

    member _.Limit = limit

    member _.Window = window

    member _.IsLockedOut(client: string, now: DateTimeOffset) =
        lock gate (fun () -> prune client now >= limit)

    member _.RecordFailure(client: string, now: DateTimeOffset) =
        lock gate (fun () ->
            prune client now |> ignore

            let recorded =
                match failures.TryGetValue client with
                | true, recorded -> recorded
                | _ ->
                    let created = Queue<DateTimeOffset>()
                    failures[client] <- created
                    created

            recorded.Enqueue now)

    member _.Clear(client: string) =
        lock gate (fun () -> failures.Remove client |> ignore)
