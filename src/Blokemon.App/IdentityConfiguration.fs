namespace Blokemon.App

open System
open System.Globalization
open Blokemon.Product
open Microsoft.Extensions.Configuration

/// One sign-in provider as the deployment configured it. The name is the configuration section's
/// own name, lower-cased; this tier knows no provider by name.
type ProviderSettings =
    {
        Name: IdentityProviderName
        Enabled: bool
        /// An external sign-in page that signs a person in through this provider and returns
        /// top-level with a hand-off; the sign-in page offers it only when it is configured.
        CoreSignInUrl: Uri | null
    }

/// The passkey relying party, required once the first-party provider is enabled.
type PasskeySettings =
    { RelyingPartyId: string
      Origins: string array }

/// The `Blokemon:Identity` contract, validated once at start-up.
///
/// Operator bootstrap redeems `OperatorBootstrapCode` from a FirstParty session only, and only
/// an operator admits channels, so a deployment must enable the first-party provider to obtain
/// its first operator: enabling only a provider whose sessions carry Issuer provenance is a
/// stated dead end. A deployment that enables no provider still starts and serves browser-local
/// play.
type IdentityConfiguration =
    { Providers: ProviderSettings array
      SessionLifetime: TimeSpan
      SessionSweepInterval: TimeSpan
      Passkeys: PasskeySettings option
      OperatorBootstrapCode: string | null
      HandoffRateLimitPerMinute: int }

    /// The providers the deployment enabled, in configuration order.
    member this.EnabledProviders =
        this.Providers
        |> Array.filter (fun provider -> provider.Enabled)
        |> Array.map (fun provider -> provider.Name)

    /// The settings of one provider, or null when the deployment never mentioned it.
    member this.Provider(name: IdentityProviderName) : ProviderSettings | null =
        match this.Providers |> Array.tryFind (fun provider -> provider.Name = name) with
        | Some provider -> provider
        | None -> null

/// Reads and validates the `Blokemon:Identity` contract; an invalid value fails start-up with
/// a message naming the key, like the economy settings.
module IdentityConfiguration =

    [<Literal>]
    let ProvidersKey = "Blokemon:Identity:Providers"

    [<Literal>]
    let SessionLifetimeKey = "Blokemon:Identity:Session:Lifetime"

    [<Literal>]
    let SessionSweepIntervalKey = "Blokemon:Identity:Session:SweepInterval"

    [<Literal>]
    let PasskeysRelyingPartyIdKey = "Blokemon:Identity:Passkeys:RelyingPartyId"

    [<Literal>]
    let PasskeysOriginsKey = "Blokemon:Identity:Passkeys:Origins"

    [<Literal>]
    let OperatorBootstrapCodeKey = "Blokemon:Identity:OperatorBootstrapCode"

    [<Literal>]
    let HandoffRateLimitPerMinuteKey = "Blokemon:Identity:Handoff:RateLimitPerMinute"

    /// The provider whose sessions carry FirstParty provenance and whose enabling requires the
    /// passkey relying party.
    let FirstPartyProvider =
        match IdentityProviderName.Create "firstparty" with
        | DomainResult.Succeeded name -> name
        | DomainResult.Failed _ -> failwith "The first-party provider name is well formed."

    /// The shortest operator bootstrap code a deployment may configure.
    let MinimumBootstrapCodeLength = 16

    /// The sweep interval a deployment gets when it states none.
    let DefaultSweepInterval = TimeSpan.FromHours 1.0

    let providerEnabledKey (section: string) = $"{ProvidersKey}:{section}:Enabled"

    let providerCoreSignInUrlKey (section: string) =
        $"{ProvidersKey}:{section}:CoreSignInUrl"

    let private invalid (message: string) =
        raise (InvalidOperationException message)

    let private timeSpan (configuration: IConfiguration) (key: string) (fallback: TimeSpan) =
        match configuration[key] with
        | null -> fallback
        | text when String.IsNullOrWhiteSpace text -> fallback
        | text ->
            match TimeSpan.TryParse(text, CultureInfo.InvariantCulture) with
            | true, parsed when parsed > TimeSpan.Zero -> parsed
            | _ -> invalid $"{key} must be a positive duration such as 08:00:00."

    let private absoluteHttpUri (key: string) (text: string) =
        match Uri.TryCreate(text, UriKind.Absolute) with
        | true, NonNull uri when uri.Scheme = Uri.UriSchemeHttps || uri.Scheme = Uri.UriSchemeHttp ->
            uri
        | _ -> invalid $"{key} must be an absolute http or https URL."

    let private provider (section: IConfigurationSection) =
        let name =
            match IdentityProviderName.Create(section.Key.ToLowerInvariant()) with
            | DomainResult.Succeeded name -> name
            | DomainResult.Failed _ ->
                invalid (
                    $"{ProvidersKey}:{section.Key} must be named with letters and digits only, "
                    + $"at most {IdentityProviderName.MaximumLength} characters."
                )

        let enabledKey = providerEnabledKey section.Key

        let enabled =
            match section["Enabled"] with
            | null -> false
            | text when String.IsNullOrWhiteSpace text -> false
            | text ->
                match Boolean.TryParse text with
                | true, value -> value
                | _ -> invalid $"{enabledKey} must be true or false."

        let coreSignInUrl: Uri | null =
            match section["CoreSignInUrl"] with
            | null -> null
            | text when String.IsNullOrWhiteSpace text -> null
            | text -> absoluteHttpUri (providerCoreSignInUrlKey section.Key) text

        { Name = name
          Enabled = enabled
          CoreSignInUrl = coreSignInUrl }

    let private passkeys (configuration: IConfiguration) (firstPartyEnabled: bool) =
        let relyingPartyId = configuration[PasskeysRelyingPartyIdKey]

        let origins =
            configuration.GetSection(PasskeysOriginsKey).GetChildren()
            |> Seq.map (fun child ->
                match child.Value with
                | null -> invalid $"{PasskeysOriginsKey} entries must be absolute origins."
                | text ->
                    (absoluteHttpUri PasskeysOriginsKey text).GetLeftPart UriPartial.Authority)
            |> Seq.toArray

        let configured =
            not (String.IsNullOrWhiteSpace relyingPartyId) || origins.Length > 0

        if firstPartyEnabled || configured then
            if String.IsNullOrWhiteSpace relyingPartyId then
                invalid
                    $"{PasskeysRelyingPartyIdKey} is required when the first-party provider is enabled."

            if origins.Length = 0 then
                invalid
                    $"{PasskeysOriginsKey} needs at least one origin when the first-party provider is enabled."

            Some
                { RelyingPartyId = (Unchecked.nonNull relyingPartyId).Trim()
                  Origins = origins }
        else
            None

    let Resolve (configuration: IConfiguration) : IdentityConfiguration =
        ArgumentNullException.ThrowIfNull(configuration, nameof configuration)

        let providers =
            configuration.GetSection(ProvidersKey).GetChildren()
            |> Seq.map provider
            |> Seq.toArray

        let duplicated =
            providers
            |> Array.countBy (fun provider -> provider.Name)
            |> Array.tryFind (fun (_, count) -> count > 1)

        match duplicated with
        | Some(name, _) -> invalid $"{ProvidersKey} names the provider {name} more than once."
        | None -> ()

        let lifetime = timeSpan configuration SessionLifetimeKey Sessions.DefaultLifetime

        if lifetime > Sessions.MaximumLifetime then
            invalid $"{SessionLifetimeKey} must be at most {Sessions.MaximumLifetime}."

        let sweepInterval =
            timeSpan configuration SessionSweepIntervalKey DefaultSweepInterval

        let firstPartyEnabled =
            providers
            |> Array.exists (fun provider -> provider.Enabled && provider.Name = FirstPartyProvider)

        let bootstrapCode: string | null =
            match configuration[OperatorBootstrapCodeKey] with
            | null -> null
            | "" -> null
            | text when text.Length < MinimumBootstrapCodeLength ->
                invalid
                    $"{OperatorBootstrapCodeKey} must be at least {MinimumBootstrapCodeLength} characters."
            | text -> text

        let rateLimit =
            match configuration[HandoffRateLimitPerMinuteKey] with
            | null -> 60
            | text when String.IsNullOrWhiteSpace text -> 60
            | text ->
                match Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) with
                | true, parsed when parsed >= 1 -> parsed
                | _ ->
                    invalid $"{HandoffRateLimitPerMinuteKey} must be a whole number of at least 1."

        { Providers = providers
          SessionLifetime = lifetime
          SessionSweepInterval = sweepInterval
          Passkeys = passkeys configuration firstPartyEnabled
          OperatorBootstrapCode = bootstrapCode
          HandoffRateLimitPerMinute = rateLimit }
