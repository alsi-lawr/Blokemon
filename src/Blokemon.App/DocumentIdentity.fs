namespace Blokemon.App

open System
open System.Security.Cryptography
open System.Text

module internal DocumentIdentity =

    let ofText (value: string) =
        value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
