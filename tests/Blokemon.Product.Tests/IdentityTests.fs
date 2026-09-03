namespace Blokemon.Product.Tests

open System
open Blokemon.Product
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private IdentityFixtures =

    let succeeded (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed error -> failwith $"Expected success, received {error}."

    let failed (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Failed error -> error
        | DomainResult.Succeeded value -> failwith $"Expected failure, received {value}."

type IdentityTests() =

    [<Test>]
    member _.``minting an account id should yield a canonical guid that recreates equal``() =
        let minted = AccountId.Mint()

        Guid.TryParseExact(minted.Value, "D") |> fst |> should be True
        minted.Value |> should equal (minted.Value.ToLowerInvariant())
        succeeded (AccountId.Create minted.Value) |> should equal minted
        AccountId.Mint() |> should not' (equal minted)

    [<Test>]
    member _.``creating a tenant id from blank text should fail as required``() =
        failed (TenantId.Create null) |> should equal IdentityValueFailure.Required
        failed (TenantId.Create "   ") |> should equal IdentityValueFailure.Required

    [<Test>]
    member _.``creating a tenant id from text that is not a guid should fail as malformed``() =
        failed (TenantId.Create "twelve") |> should equal IdentityValueFailure.Malformed

        failed (TenantId.Create "{6f9619ff-8b86-d011-b42d-00c04fc964ff}")
        |> should equal IdentityValueFailure.Malformed

    [<Test>]
    member _.``an upper case guid should canonicalise to lower case``() =
        let account = succeeded (AccountId.Create "6F9619FF-8B86-D011-B42D-00C04FC964FF")

        account.Value |> should equal "6f9619ff-8b86-d011-b42d-00c04fc964ff"

    [<Test>]
    member _.``a provider name should be lower case letters and digits within its bound``() =
        (succeeded (IdentityProviderName.Create "firstparty")).Value
        |> should equal "firstparty"

        failed (IdentityProviderName.Create "FirstParty")
        |> should equal ExternalIdentityFailure.Malformed

        failed (IdentityProviderName.Create "")
        |> should equal ExternalIdentityFailure.Required

        failed (IdentityProviderName.Create(String('a', IdentityProviderName.MaximumLength + 1)))
        |> should equal ExternalIdentityFailure.TooLong

    [<Test>]
    member _.``a subject containing a path separator should be refused as malformed``() =
        failed (ExternalSubject.Create "12345/profile")
        |> should equal ExternalIdentityFailure.Malformed

        failed (ExternalSubject.Create "a b")
        |> should equal ExternalIdentityFailure.Malformed

    [<Test>]
    member _.``a subject over the bound should be refused as too long``() =
        (succeeded (ExternalSubject.Create(String('7', ExternalSubject.MaximumLength))))
            .Value.Length
        |> should equal ExternalSubject.MaximumLength

        failed (ExternalSubject.Create(String('7', ExternalSubject.MaximumLength + 1)))
        |> should equal ExternalIdentityFailure.TooLong

    [<Test>]
    member _.``a subject made of digits, letters, dots, hyphens and underscores should be accepted``
        ()
        =
        (succeeded (ExternalSubject.Create "user_1.two-3")).Value
        |> should equal "user_1.two-3"

    [<Test>]
    member _.``every reserved slug should be refused as reserved``() =
        for reserved in TenantSlug.Reserved do
            failed (TenantSlug.Create reserved) |> should equal TenantSlugFailure.Reserved

        TenantSlug.Reserved |> should contain "self"
        TenantSlug.Reserved |> should contain "close"
        TenantSlug.Reserved |> should contain "continue"

    [<Test>]
    member _.``a slug over thirty two characters should be refused as too long``() =
        (succeeded (TenantSlug.Create(String('z', TenantSlug.MaximumLength)))).Value.Length
        |> should equal 32

        failed (TenantSlug.Create(String('z', TenantSlug.MaximumLength + 1)))
        |> should equal TenantSlugFailure.TooLong

    [<Test>]
    member _.``a slug that is not lower case letters and digits with single hyphens should be refused``
        ()
        =
        for malformed in
            [ "Channel"
              "-lead"
              "trail-"
              "dou--ble"
              "with space"
              "slash/ed"
              "ünïcode" ] do
            failed (TenantSlug.Create malformed) |> should equal TenantSlugFailure.Malformed

        failed (TenantSlug.Create "") |> should equal TenantSlugFailure.Required

    [<Test>]
    member _.``a well formed slug should be accepted as written``() =
        (succeeded (TenantSlug.Create "the-regular-7")).Value
        |> should equal "the-regular-7"
