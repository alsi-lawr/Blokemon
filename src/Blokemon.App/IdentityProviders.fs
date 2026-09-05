namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.Product

/// What a provider asserts once it has verified a person: who they are to that provider, a hint
/// for the display name of a first profile, and the provenance a session from it carries.
type VerifiedIdentity =
    { Provider: IdentityProviderName
      Subject: ExternalSubject
      DisplayNameHint: string | null
      Provenance: SessionProvenance }

/// Why a sign-in produced no session. Nothing is written for any of these except that a
/// first sign-in interrupted part-way is completed on the next.
[<RequireQualifiedAccess>]
type SignInFailure =
    /// The provider's own typed refusal of the proof it was given.
    | ProviderRefused of ApiError
    | AccountDisabled
    | AccountErased
    /// The tenant's owner has excluded this account there.
    | TenantExcluded
    /// A record the sign-in depends on cannot be read.
    | Damaged
    /// A record changed underneath the sign-in.
    | Conflict
    /// The first profile could not be created; the typed profile error says why.
    | ProfileRefused of ApiError

module SignInFailures =

    let toError (failure: SignInFailure) : ApiError =
        match failure with
        | SignInFailure.ProviderRefused error -> error
        | SignInFailure.AccountDisabled -> ApiError("account.disabled", "This account is disabled.")
        | SignInFailure.AccountErased -> ApiError("account.erased", "This account was erased.")
        | SignInFailure.TenantExcluded ->
            ApiError("tenant.excluded", "This channel has excluded your account.")
        | SignInFailure.Damaged ->
            ApiError("signin.damaged", "A sign-in record could not be read. Nothing changed.")
        | SignInFailure.Conflict ->
            ApiError("signin.conflict", "Sign-in changed underneath this request. Try again.")
        | SignInFailure.ProfileRefused error -> error

/// One way of establishing who a person is. The host registers the implementations it ships;
/// the deployment enables them by name. This tier names none of them.
type IIdentityProvider =
    /// The name recorded in identity links for subjects this provider asserts.
    abstract Name: IdentityProviderName

    /// Verifies this provider's own proof of a person and asserts who they are.
    abstract Verify:
        proof: string * cancellationToken: CancellationToken ->
            Task<DomainResult<VerifiedIdentity, SignInFailure>>

/// The providers a deployment enabled, each backed by an implementation the host registered. A
/// provider enabled without an implementation is a start-up failure naming the key; an
/// implementation the deployment never enabled is not listed.
[<Sealed>]
type IdentityProviderRegistry
    (configuration: IdentityConfiguration, implementations: IIdentityProvider seq) =

    let implementations = implementations |> Seq.toArray

    let enabled =
        configuration.EnabledProviders
        |> Array.map (fun name ->
            match implementations |> Array.tryFind (fun provider -> provider.Name = name) with
            | Some provider -> provider
            | None ->
                raise (
                    InvalidOperationException(
                        $"{IdentityConfiguration.providerEnabledKey name.Value} is true but this "
                        + "host ships no such provider."
                    )
                ))

    /// The names of the enabled providers, in configuration order.
    member _.Enabled: IdentityProviderName array =
        enabled |> Array.map (fun provider -> provider.Name)

    member _.IsEnabled(name: IdentityProviderName) =
        enabled |> Array.exists (fun provider -> provider.Name = name)

    /// The enabled provider of that name, or null.
    member _.Find(name: IdentityProviderName) : IIdentityProvider | null =
        match enabled |> Array.tryFind (fun provider -> provider.Name = name) with
        | Some provider -> provider
        | None -> null
