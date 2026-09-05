namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// What completing a sign-in needs from the host.
type SignInServices =
    { Documents: IStateDocumentStore
      Catalogue: BlokemonCatalogue
      Economy: EconomyRules
      SessionLifetime: TimeSpan }

/// Provider-neutral sign-in completion: the link resolves to an account or the first sign-in
/// creates the account and its profile; a disabled, erased or tenant-excluded account is refused
/// with no session; otherwise a session is issued with the provenance the provider stated.
/// Every step is idempotent, so a replay or a sign-in interrupted part-way converges on one
/// account and one profile.
module SignInCompletion =

    /// The name a first profile gets when the provider offers none that fits.
    [<Literal>]
    let FallbackDisplayName = "Player"

    /// The provider's hint, trimmed and cut to the display-name bound, or the fallback.
    let displayName (hint: string | null) =
        let trimmed =
            match hint with
            | null -> ""
            | text -> text.Trim()

        if trimmed.Length = 0 then
            FallbackDisplayName
        elif trimmed.Length > DisplayName.MaximumLength then
            trimmed.Substring(0, DisplayName.MaximumLength).TrimEnd()
        else
            trimmed

    /// The profile a first sign-in creates replays under the account's own identity, so a
    /// second creation for the same account is the same command and not a second profile.
    let profileCreationCommand (account: AccountId) = Guid.Parse account.Value

    let private resolveAccount
        (documents: IStateDocumentStore)
        (identity: VerifiedIdentity)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountId, SignInFailure>> =
        task {
            let! resolution =
                IdentityLinks.resolve documents identity.Provider identity.Subject cancellationToken

            match resolution with
            | LinkResolution.Linked account -> return DomainResult.Succeeded account
            | LinkResolution.Damaged -> return DomainResult.Failed SignInFailure.Damaged
            | LinkResolution.Unlinked ->
                let account = AccountId.Mint()

                let! created =
                    IdentityLinks.create
                        documents
                        { Provider = identity.Provider
                          Subject = identity.Subject
                          Account = account }
                        now
                        cancellationToken

                match created with
                | DomainResult.Succeeded() -> return DomainResult.Succeeded account
                | DomainResult.Failed IdentityLinkFailure.AlreadyLinked ->
                    // A concurrent first sign-in for the same subject won the link; this one
                    // follows it. The minted id was never written anywhere.
                    let! again =
                        IdentityLinks.resolve
                            documents
                            identity.Provider
                            identity.Subject
                            cancellationToken

                    match again with
                    | LinkResolution.Linked winner -> return DomainResult.Succeeded winner
                    | _ -> return DomainResult.Failed SignInFailure.Conflict
        }

    let private ensureAccount
        (documents: IStateDocumentStore)
        (account: AccountId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountDocument, SignInFailure>> =
        task {
            let read () =
                task {
                    let! stored = documents.Read(accountKey account, cancellationToken)

                    match stored with
                    | null -> return None
                    | document ->
                        let parsed =
                            try
                                Ok(JsonSerializer.Deserialize<AccountDocument>(document.Json, json))
                            with :? JsonException ->
                                Error()

                        match parsed with
                        | Ok(NonNull value) when value.SchemaVersion = accountSchemaVersion ->
                            return Some(Ok value)
                        | _ -> return Some(Error())
                }

            let! existing = read ()

            match existing with
            | Some(Ok document) -> return DomainResult.Succeeded document
            | Some(Error()) -> return DomainResult.Failed SignInFailure.Damaged
            | None ->
                let document = newAccount account now

                let! write =
                    documents.Create(
                        accountKey account,
                        JsonSerializer.Serialize(document, json),
                        cancellationToken
                    )

                match write with
                | :? DocumentWriteResult.Written -> return DomainResult.Succeeded document
                | _ ->
                    let! raced = read ()

                    match raced with
                    | Some(Ok document) -> return DomainResult.Succeeded document
                    | _ -> return DomainResult.Failed SignInFailure.Conflict
        }

    let private ensureProfile
        (services: SignInServices)
        (account: AccountId)
        (tenant: TenantId)
        (hint: string | null)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, SignInFailure>> =
        task {
            let principal = ApplicationPrincipal.Account(account, tenant)

            let application =
                LocalApplicationService(
                    services.Catalogue,
                    services.Documents,
                    principal,
                    LocalMatchService(
                        services.Catalogue,
                        services.Documents,
                        PlayerDocumentKeys.ofPrincipal principal
                    ),
                    services.Economy,
                    ProfileAuthorityPolicy.Preserve
                )

            let! response =
                application.CreateProfile(
                    CreateProfileRequest(profileCreationCommand account, displayName hint),
                    cancellationToken
                )

            if response.Succeeded then
                return DomainResult.Succeeded()
            else
                match response.Error with
                | NonNull error when
                    String.Equals(
                        error.Code,
                        ProfileFailures.ProfileExistsCode,
                        StringComparison.Ordinal
                    )
                    ->
                    return DomainResult.Succeeded()
                | NonNull error -> return DomainResult.Failed(SignInFailure.ProfileRefused error)
                | Null -> return DomainResult.Failed SignInFailure.Conflict
        }

    /// Completes a sign-in for an identity a provider has already verified.
    let complete
        (services: SignInServices)
        (identity: VerifiedIdentity)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<IssuedSession, SignInFailure>> =
        task {
            let documents = services.Documents
            let! resolved = resolveAccount documents identity now cancellationToken

            match resolved with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded account ->
                let! ensured = ensureAccount documents account now cancellationToken

                match ensured with
                | DomainResult.Failed failure -> return DomainResult.Failed failure
                | DomainResult.Succeeded record when record.Status = AccountStatus.Disabled ->
                    return DomainResult.Failed SignInFailure.AccountDisabled
                | DomainResult.Succeeded record when record.Status = AccountStatus.Erased ->
                    return DomainResult.Failed SignInFailure.AccountErased
                | DomainResult.Succeeded _ ->
                    let! approval = Approvals.load documents account tenant cancellationToken

                    match approval with
                    | DomainResult.Failed _ -> return DomainResult.Failed SignInFailure.Damaged
                    | DomainResult.Succeeded(Some loaded) when Approvals.isExcluded loaded.Document ->
                        return DomainResult.Failed SignInFailure.TenantExcluded
                    | DomainResult.Succeeded _ ->
                        let! profile =
                            ensureProfile
                                services
                                account
                                tenant
                                identity.DisplayNameHint
                                cancellationToken

                        match profile with
                        | DomainResult.Failed failure -> return DomainResult.Failed failure
                        | DomainResult.Succeeded() ->
                            let! issued =
                                Sessions.issue
                                    documents
                                    account
                                    tenant
                                    identity.Provenance
                                    now
                                    services.SessionLifetime
                                    cancellationToken

                            return DomainResult.Succeeded issued
        }

    /// Verifies a proof through the named enabled provider, then completes the sign-in.
    let signIn
        (services: SignInServices)
        (registry: IdentityProviderRegistry)
        (provider: IdentityProviderName)
        (proof: string)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<IssuedSession, SignInFailure>> =
        task {
            match registry.Find provider with
            | null ->
                return
                    DomainResult.Failed(
                        SignInFailure.ProviderRefused(
                            ApiError(
                                "provider.unavailable",
                                "That way of signing in is not enabled."
                            )
                        )
                    )
            | implementation ->
                let! verified = implementation.Verify(proof, cancellationToken)

                match verified with
                | DomainResult.Failed failure -> return DomainResult.Failed failure
                | DomainResult.Succeeded identity ->
                    return! complete services identity tenant now cancellationToken
        }
