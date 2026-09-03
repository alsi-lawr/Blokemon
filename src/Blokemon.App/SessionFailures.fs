namespace Blokemon.App

open Blokemon.App.Contracts

/// The typed refusal every server route returns while no session names the account it acts for.
module SessionFailures =

    let required () =
        ApiError("session.required", "Sign in to play on this server.")
