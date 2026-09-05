namespace Blokemon.App

open System
open System.Collections.Generic

/// Allows at most `limit` events per key inside a sliding `window`, counting the events it
/// allowed. Unlike a lock-out, a refusal records nothing: the caller is told no and the window
/// keeps draining.
[<Sealed>]
type RateLimiter(limit: int, window: TimeSpan) =

    let events = Dictionary<string, Queue<DateTimeOffset>>(StringComparer.Ordinal)
    let gate = obj ()

    member _.Limit = limit

    member _.Window = window

    /// Whether one more event is allowed for the key at `now`; when it is, it is counted.
    member _.Allow(key: string, now: DateTimeOffset) : bool =
        lock gate (fun () ->
            let queue =
                match events.TryGetValue key with
                | true, existing -> existing
                | _ ->
                    let created = Queue<DateTimeOffset>()
                    events[key] <- created
                    created

            while queue.Count > 0 && queue.Peek() <= now - window do
                queue.Dequeue() |> ignore

            if queue.Count < limit then
                queue.Enqueue now
                true
            else
                false)

    /// The per-minute limit a deployment configured for hand-off minting.
    static member PerMinute(limit: int) =
        RateLimiter(limit, TimeSpan.FromMinutes 1.0)
