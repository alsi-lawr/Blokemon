namespace Blokemon.App

open System
open System.Text
open Blokemon.App.Catalogue

module internal ApplicationProjectionIdentity =

    let catalogue (value: BlokemonCatalogue) =
        value.ToBootstrapJson() |> DocumentIdentity.ofText

    let content (write: StringBuilder -> unit) =
        let value = StringBuilder()
        write value
        value.ToString() |> DocumentIdentity.ofText

    let appendString (target: StringBuilder) (value: string | null) =
        match value with
        | null -> target.Append("-1:") |> ignore
        | text -> target.Append(text.Length).Append(':').Append(text) |> ignore

    let appendInt64 (target: StringBuilder) (value: int64) =
        target.Append(value).Append(';') |> ignore

    let appendInt (target: StringBuilder) (value: int) =
        target.Append(value).Append(';') |> ignore

    let combine (values: string seq) =
        content (fun target -> values |> Seq.iter (appendString target))

    let matchDocument (result: MatchProjectionResult) =
        content (fun target ->
            if result.DocumentIdentity.Revision.HasValue then
                appendInt64 target result.DocumentIdentity.Revision.Value
            else
                appendString target null

            appendString target result.DocumentIdentity.ContentIdentity

            match result.Error with
            | null -> appendString target null
            | error ->
                appendString target error.Code
                appendString target error.Message)
