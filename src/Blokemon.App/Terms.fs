namespace Blokemon.App

open System
open Blokemon.App.Contracts

/// The terms of service a person agrees to before an account is created for them. The version
/// is the date on the Terms of Service page the client shows at `/terms`; an account records
/// the version it was created under and when. A sign-in that would create an account without
/// the current version is refused before anything is stored.
module Terms =

    /// The current terms: the date shown on the Terms of Service page.
    [<Literal>]
    let Version = "2026-09-05"

    /// Whether the version presented is the current one.
    let accepted (version: string | null) =
        match version with
        | null -> false
        | text -> String.Equals(text.Trim(), Version, StringComparison.Ordinal)

    let required =
        ApiError(
            "terms.required",
            "Agree to the Terms of Service and read the Privacy Notice to create an account."
        )
