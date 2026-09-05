namespace Blokemon.App.Tests

open System
open Blokemon.App
open FsUnit
open TUnit.Core

type RateLimiterTests() =

    [<Test>]
    member _.``a key should get its limit inside the window and more once the window slides``() =
        let limiter = RateLimiter.PerMinute 3

        [ for _ in 1..3 -> limiter.Allow("a", now) ]
        |> should equal [ true; true; true ]

        limiter.Allow("a", now.AddSeconds 30.0) |> should be False
        limiter.Allow("b", now.AddSeconds 30.0) |> should be True
        // The window slid past every earlier event: the key has its full limit again.
        [ for _ in 1..3 -> limiter.Allow("a", now.AddSeconds 61.0) ]
        |> should equal [ true; true; true ]

        limiter.Allow("a", now.AddSeconds 61.0) |> should be False
