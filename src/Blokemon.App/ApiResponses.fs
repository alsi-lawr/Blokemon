namespace Blokemon.App

open System
open Blokemon.App.Contracts

/// The two ApiResponse shapes the application tier returns.
module internal ApiResponses =

    let succeeded value = ApiResponse(true, value, null)

    let failed<'T> (error: ApiError) =
        ApiResponse<'T>(false, Unchecked.defaultof<'T>, error)

/// System.Text.Json can put a null into a member a C# contract declares non-nullable, and F#
/// types those non-null, so the damaged-document guards go through ReferenceEquals. Dropping them
/// would turn a graceful typed rejection into a NullReferenceException the JsonException handlers
/// never see.
module internal DamagedDocument =

    let isMissing (value: obj) = Object.ReferenceEquals(value, null)

    let orEmpty (values: 'T array) : 'T array =
        if Object.ReferenceEquals(values, null) then
            Array.empty
        else
            values
