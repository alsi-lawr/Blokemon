namespace Blokemon.App.Tests

open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type IntegrationTokenTests() =

    [<Test>]
    member _.``a minted token should name its tenant carry 256 bits and be recognised only by its own verifier``
        ()
        =
        let tenant = TenantId.Mint()
        let token, verifier = IntegrationTokens.mint tenant now
        let other, otherVerifier = IntegrationTokens.mint tenant now

        token |> should startWith $"blkm_{tenant}_"
        let parsedTenant, secret = Option.get (IntegrationTokens.parse token)
        parsedTenant |> should equal tenant
        secret.Length |> should be (greaterThanOrEqualTo 43)
        verifier.Hash.Contains secret |> should be False
        verifier.IssuedAt |> should equal now
        IntegrationTokens.matches verifier secret |> should be True
        IntegrationTokens.matches otherVerifier secret |> should be False

        IntegrationTokens.matches verifier (snd (Option.get (IntegrationTokens.parse other)))
        |> should be False

        IntegrationTokens.matches null secret |> should be False

    [<Test>]
    member _.``anything not shaped like a token should not parse``() =
        for (text: string | null) in
            ([ null
               ""
               "blkm_"
               "blkm_not-a-guid_secret"
               $"blkm_{TenantId.Mint()}_"
               $"blkm_{TenantId.Mint()}"
               $"{TenantId.Mint()}_secret" ]
            : (string | null) list) do
            IntegrationTokens.parse text |> should equal None
