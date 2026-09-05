namespace Blokemon.App

open System
open Blokemon.App.Contracts

/// Why the host must obtain a new session before an operation can be retried.
type ReauthenticationReason =
    /// No session was presented, or the one presented is unknown or revoked.
    | Required = 0
    /// The session presented has passed its absolute expiry.
    | Expired = 1

/// The typed refusals a server route returns when no valid session names the account it acts
/// for, and the one reading of them the client's re-authentication seam needs.
module SessionFailures =

    [<Literal>]
    let RequiredCode = "session.required"

    [<Literal>]
    let ExpiredCode = "session.expired"

    /// A Recovery session presented anywhere but the replacement enrolment.
    [<Literal>]
    let RecoveryCode = "session.recovery"

    let required () =
        ApiError(RequiredCode, "Sign in to play on this server.")

    let expired () =
        ApiError(ExpiredCode, "Your sign-in has ended. Sign in again to keep playing.")

    let recovery () =
        ApiError(RecoveryCode, "Finish recovery by adding a new passkey, then sign in with it.")

    /// The re-authentication reason an error carries, or none when it is any other error.
    let reauthentication (error: ApiError | null) : Nullable<ReauthenticationReason> =
        match error with
        | null -> Nullable()
        | error when String.Equals(error.Code, RequiredCode, StringComparison.Ordinal) ->
            Nullable ReauthenticationReason.Required
        | error when String.Equals(error.Code, ExpiredCode, StringComparison.Ordinal) ->
            Nullable ReauthenticationReason.Expired
        | _ -> Nullable()
