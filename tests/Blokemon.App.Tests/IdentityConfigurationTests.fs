namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type IdentityConfigurationTests() =

    let providerName (name: string) =
        match IdentityProviderName.Create name with
        | DomainResult.Succeeded name -> name
        | DomainResult.Failed failure -> failwith $"{failure}"

    let failsNaming (key: string) (settings: (string * string) list) =
        let thrown =
            try
                identityConfiguration settings |> ignore
                None
            with :? InvalidOperationException as raised ->
                Some raised.Message

        match thrown with
        | Some message when message.Contains(key, StringComparison.Ordinal) -> ()
        | Some message -> failwith $"The failure for {key} did not name it: {message}"
        | None -> failwith $"Configuration with a bad {key} was accepted."

    [<Test>]
    member _.``no identity configuration should resolve to defaults with no provider enabled``() =
        let resolved = identityConfiguration []

        resolved.Providers |> should be Empty
        resolved.EnabledProviders |> should be Empty
        resolved.SessionLifetime |> should equal Sessions.DefaultLifetime

        resolved.SessionSweepInterval
        |> should equal IdentityConfiguration.DefaultSweepInterval

        resolved.Passkeys |> should equal None
        resolved.OperatorBootstrapCode |> should be Null
        resolved.HandoffRateLimitPerMinute |> should equal 60

    [<Test>]
    member _.``each invalid value should fail fast naming its key``() =
        failsNaming
            IdentityConfiguration.SessionLifetimeKey
            [ IdentityConfiguration.SessionLifetimeKey, "soon" ]

        failsNaming
            IdentityConfiguration.SessionLifetimeKey
            [ IdentityConfiguration.SessionLifetimeKey, "25:00:00" ]

        failsNaming
            IdentityConfiguration.SessionLifetimeKey
            [ IdentityConfiguration.SessionLifetimeKey, "00:00:00" ]

        failsNaming
            IdentityConfiguration.SessionSweepIntervalKey
            [ IdentityConfiguration.SessionSweepIntervalKey, "-01:00:00" ]

        failsNaming
            (IdentityConfiguration.providerEnabledKey "example")
            [ IdentityConfiguration.providerEnabledKey "example", "maybe" ]

        failsNaming
            (IdentityConfiguration.providerCoreSignInUrlKey "example")
            [ IdentityConfiguration.providerCoreSignInUrlKey "example", "not a url" ]

        failsNaming
            (IdentityConfiguration.providerCoreSignInUrlKey "example")
            [ IdentityConfiguration.providerCoreSignInUrlKey "example", "ftp://files.example" ]

        failsNaming
            $"{IdentityConfiguration.ProvidersKey}:bad name"
            [ IdentityConfiguration.providerEnabledKey "bad name", "true" ]

        failsNaming
            IdentityConfiguration.PasskeysOriginsKey
            [ $"{IdentityConfiguration.PasskeysOriginsKey}:0", "nope" ]

        failsNaming
            IdentityConfiguration.OperatorBootstrapCodeKey
            [ IdentityConfiguration.OperatorBootstrapCodeKey, "short" ]

        failsNaming
            IdentityConfiguration.HandoffRateLimitPerMinuteKey
            [ IdentityConfiguration.HandoffRateLimitPerMinuteKey, "0" ]

        failsNaming
            IdentityConfiguration.HandoffRateLimitPerMinuteKey
            [ IdentityConfiguration.HandoffRateLimitPerMinuteKey, "sixty" ]

    [<Test>]
    member _.``enabling the first party provider should require the passkey relying party and origins``
        ()
        =
        failsNaming
            IdentityConfiguration.PasskeysRelyingPartyIdKey
            [ IdentityConfiguration.providerEnabledKey "firstparty", "true" ]

        failsNaming
            IdentityConfiguration.PasskeysOriginsKey
            [ IdentityConfiguration.providerEnabledKey "firstparty", "true"
              IdentityConfiguration.PasskeysRelyingPartyIdKey, "blokemon.monster" ]

        let resolved =
            identityConfiguration
                [ IdentityConfiguration.providerEnabledKey "FirstParty", "true"
                  IdentityConfiguration.PasskeysRelyingPartyIdKey, "blokemon.monster"
                  $"{IdentityConfiguration.PasskeysOriginsKey}:0", "https://blokemon.monster/"
                  $"{IdentityConfiguration.PasskeysOriginsKey}:1", "http://localhost:5080" ]

        resolved.EnabledProviders
        |> should equal [| IdentityConfiguration.FirstPartyProvider |]

        match resolved.Passkeys with
        | Some passkeys ->
            passkeys.RelyingPartyId |> should equal "blokemon.monster"

            passkeys.Origins
            |> should equal [| "https://blokemon.monster"; "http://localhost:5080" |]
        | None -> failwith "Expected passkey settings."

    [<Test>]
    member _.``a provider section should be named in lower case and keep its optional core sign in url``
        ()
        =
        let resolved =
            identityConfiguration
                [ IdentityConfiguration.providerEnabledKey "Example", "false"
                  IdentityConfiguration.providerCoreSignInUrlKey "Example",
                  "https://core.example/signin" ]

        resolved.EnabledProviders |> should be Empty
        let example = resolved.Provider(providerName "example")
        example |> should not' (be Null)

        (Unchecked.nonNull example).CoreSignInUrl
        |> should equal (Uri "https://core.example/signin")

        resolved.Provider(providerName "other") |> should be Null

    [<Test>]
    member _.``the bootstrap code should be accepted at the minimum length and the lifetime at its bound``
        ()
        =
        let resolved =
            identityConfiguration
                [ IdentityConfiguration.OperatorBootstrapCodeKey,
                  String('k', IdentityConfiguration.MinimumBootstrapCodeLength)
                  IdentityConfiguration.SessionLifetimeKey, "1.00:00:00"
                  IdentityConfiguration.HandoffRateLimitPerMinuteKey, "5" ]

        resolved.OperatorBootstrapCode
        |> should equal (String('k', IdentityConfiguration.MinimumBootstrapCodeLength))

        resolved.SessionLifetime |> should equal Sessions.MaximumLifetime
        resolved.HandoffRateLimitPerMinute |> should equal 5

    [<Test>]
    member _.``the registry should list enabled providers the host ships and refuse one it does not``
        ()
        =
        let shipped = ScriptedIdentityProvider("example", SessionProvenance.Issuer)

        let enabledOnly =
            identityConfiguration [ IdentityConfiguration.providerEnabledKey "example", "true" ]

        let registry = IdentityProviderRegistry(enabledOnly, [ shipped ])
        registry.Enabled |> should equal [| shipped.Name |]
        registry.IsEnabled shipped.Name |> should be True
        registry.Find shipped.Name |> should not' (be Null)

        let none = IdentityProviderRegistry(identityConfiguration [], [ shipped ])
        none.Enabled |> should be Empty
        none.Find shipped.Name |> should be Null

        let thrown =
            try
                IdentityProviderRegistry(enabledOnly, []) |> ignore
                None
            with :? InvalidOperationException as raised ->
                Some raised.Message

        match thrown with
        | Some message ->
            message
            |> should haveSubstring (IdentityConfiguration.providerEnabledKey "example")
        | None -> failwith "An enabled provider with no implementation was accepted."
