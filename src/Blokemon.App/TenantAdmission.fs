namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Why a tenant was not admitted, rotated, closed or revoked. Nothing is written for any of
/// these.
[<RequireQualifiedAccess>]
type AdmissionFailure =
    | SlugInvalid of TenantSlugFailure
    /// Another tenant holds the slug.
    | SlugTaken
    | LabelInvalid
    | SubjectInvalid
    | OriginInvalid
    | NotFound
    /// A revoked tenant is not re-admitted, rotated or closed.
    | Revoked
    | Conflict
    | Damaged

/// A tenant as admission or rotation leaves it, with the one token that reaches it.
type AdmittedTenant =
    { Tenant: TenantDocument
      Token: string }

/// What presenting an integration token established.
[<RequireQualifiedAccess>]
type ChannelAuthentication =
    | Authenticated of TenantDocument
    /// No token, or nothing shaped like one.
    | NoToken
    /// No tenant, or not this tenant's token.
    | Unknown
    | Closed
    | Revoked

/// Admits, rotates, closes, revokes and authenticates tenants by their integration token. The
/// tenant record is BLOKEMON-148's; admission fills its issuer slots and drives its status.
module TenantAdmission =

    /// The longest display label a tenant may take.
    let LabelMaximumLength = 64

    let toError (failure: AdmissionFailure) : ApiError =
        match failure with
        | AdmissionFailure.SlugInvalid TenantSlugFailure.Reserved ->
            ApiError("tenant.slug", "That slug is reserved.")
        | AdmissionFailure.SlugInvalid _ ->
            ApiError(
                "tenant.slug",
                "A slug is lower-case letters and digits separated by single hyphens, at most 32 characters."
            )
        | AdmissionFailure.SlugTaken ->
            ApiError("tenant.slug_taken", "That slug is already a tenant's.")
        | AdmissionFailure.LabelInvalid ->
            ApiError("tenant.label", $"A label is 1 to {LabelMaximumLength} characters.")
        | AdmissionFailure.SubjectInvalid ->
            ApiError("tenant.subject", "The broadcaster's subject is not well formed.")
        | AdmissionFailure.OriginInvalid ->
            ApiError("tenant.origin", "The parent origin must be an absolute http or https origin.")
        | AdmissionFailure.NotFound ->
            ApiError("tenant.not_found", "That channel is not on this server.")
        | AdmissionFailure.Revoked -> ApiError("tenant.revoked", "That channel was revoked.")
        | AdmissionFailure.Conflict ->
            ApiError("tenant.conflict", "The channel changed underneath this request. Try again.")
        | AdmissionFailure.Damaged ->
            ApiError("tenant.damaged", "The channel record could not be read. Nothing changed.")

    let private label (text: string | null) =
        match text with
        | null -> None
        | value ->
            let trimmed = value.Trim()

            if trimmed.Length >= 1 && trimmed.Length <= LabelMaximumLength then
                Some trimmed
            else
                None

    /// An absolute http or https origin with nothing after the authority, or None.
    let private origin (text: string | null) : Result<string | null, unit> =
        match text with
        | null -> Ok null
        | value when String.IsNullOrWhiteSpace value -> Ok null
        | value ->
            match Uri.TryCreate(value.Trim(), UriKind.Absolute) with
            | true, NonNull uri when
                (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps)
                && uri.GetLeftPart UriPartial.Authority = value.Trim().TrimEnd('/')
                ->
                Ok(uri.GetLeftPart UriPartial.Authority)
            | _ -> Error()

    let private subject (text: string | null) : Result<string | null, unit> =
        match text with
        | null -> Ok null
        | value when String.IsNullOrWhiteSpace value -> Ok null
        | value ->
            match ExternalSubject.Create(value.Trim()) with
            | DomainResult.Succeeded valid -> Ok valid.Value
            | DomainResult.Failed _ -> Error()

    let private write
        (documents: IStateDocumentStore)
        (loaded: LoadedTenant)
        (document: TenantDocument)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<TenantDocument, AdmissionFailure>> =
        task {
            let! result =
                documents.Update(
                    tenantKey (Tenants.idOf document),
                    loaded.Revision,
                    JsonSerializer.Serialize(document, json),
                    cancellationToken
                )

            match result with
            | :? DocumentWriteResult.Written -> return DomainResult.Succeeded document
            | _ -> return DomainResult.Failed AdmissionFailure.Conflict
        }

    let private loadOrFail
        (documents: IStateDocumentStore)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoadedTenant, AdmissionFailure>> =
        task {
            let! stored = documents.Read(tenantKey tenant, cancellationToken)

            match stored with
            | null -> return DomainResult.Failed AdmissionFailure.NotFound
            | document ->
                match Tenants.parse document with
                | Some value ->
                    let loaded: LoadedTenant =
                        { Revision = document.Revision
                          Document = value }

                    return DomainResult.Succeeded loaded
                | None -> return DomainResult.Failed AdmissionFailure.Damaged
        }

    /// Admits a channel under a fresh slug, or mints the default tenant's token when the slug
    /// is the default one: the record gains its broadcaster, parent origin and verifier, and
    /// the token is returned once.
    let admit
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (slugText: string | null)
        (labelText: string | null)
        (broadcasterSubject: string | null)
        (parentOrigin: string | null)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AdmittedTenant, AdmissionFailure>> =
        task {
            match
                TenantSlug.Create slugText,
                label labelText,
                subject broadcasterSubject,
                origin parentOrigin
            with
            | DomainResult.Failed failure, _, _, _ ->
                return DomainResult.Failed(AdmissionFailure.SlugInvalid failure)
            | _, None, _, _ -> return DomainResult.Failed AdmissionFailure.LabelInvalid
            | _, _, Error(), _ -> return DomainResult.Failed AdmissionFailure.SubjectInvalid
            | _, _, _, Error() -> return DomainResult.Failed AdmissionFailure.OriginInvalid
            | DomainResult.Succeeded slug, Some label, Ok broadcaster, Ok parent ->
                if slug = Tenants.DefaultSlug then
                    // The default tenant exists already; its core issuer is admitted by filling
                    // the same slots, and its label stays the product's.
                    let! existing = Tenants.ensureDefault documents listing now cancellationToken
                    let! loaded = loadOrFail documents (Tenants.idOf existing) cancellationToken

                    match loaded with
                    | DomainResult.Failed failure -> return DomainResult.Failed failure
                    | DomainResult.Succeeded loaded when
                        loaded.Document.Status = TenantStatus.Revoked
                        ->
                        return DomainResult.Failed AdmissionFailure.Revoked
                    | DomainResult.Succeeded loaded ->
                        let token, verifier = IntegrationTokens.mint (Tenants.idOf existing) now

                        let! written =
                            write
                                documents
                                loaded
                                { loaded.Document with
                                    BroadcasterSubject = broadcaster
                                    RegisteredParentOrigin = parent
                                    IntegrationTokenVerifier = verifier
                                    Status = TenantStatus.Active }
                                cancellationToken

                        match written with
                        | DomainResult.Succeeded document ->
                            return DomainResult.Succeeded { Tenant = document; Token = token }
                        | DomainResult.Failed failure -> return DomainResult.Failed failure
                else
                    let! taken = Tenants.findBySlug documents listing slug cancellationToken

                    match taken with
                    | Some _ -> return DomainResult.Failed AdmissionFailure.SlugTaken
                    | None ->
                        let id = TenantId.Mint()
                        let token, verifier = IntegrationTokens.mint id now

                        let document =
                            { newTenant id slug label now with
                                BroadcasterSubject = broadcaster
                                RegisteredParentOrigin = parent
                                IntegrationTokenVerifier = verifier }

                        let! created =
                            documents.Create(
                                tenantKey id,
                                JsonSerializer.Serialize(document, json),
                                cancellationToken
                            )

                        match created with
                        | :? DocumentWriteResult.Written ->
                            return DomainResult.Succeeded { Tenant = document; Token = token }
                        | _ -> return DomainResult.Failed AdmissionFailure.Conflict
        }

    /// Mints a replacement token: the previous one is invalid from here on. On a closed tenant
    /// this is re-admission, which makes it active again. A revoked tenant is refused.
    let rotate
        (documents: IStateDocumentStore)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AdmittedTenant, AdmissionFailure>> =
        task {
            let! loaded = loadOrFail documents tenant cancellationToken

            match loaded with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded loaded when loaded.Document.Status = TenantStatus.Revoked ->
                return DomainResult.Failed AdmissionFailure.Revoked
            | DomainResult.Succeeded loaded ->
                let token, verifier = IntegrationTokens.mint tenant now

                let! written =
                    write
                        documents
                        loaded
                        { loaded.Document with
                            IntegrationTokenVerifier = verifier
                            Status = TenantStatus.Active }
                        cancellationToken

                match written with
                | DomainResult.Succeeded document ->
                    return DomainResult.Succeeded { Tenant = document; Token = token }
                | DomainResult.Failed failure -> return DomainResult.Failed failure
        }

    let private end'
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (tenant: TenantId)
        (status: TenantStatus)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<TenantDocument, AdmissionFailure>> =
        task {
            let! loaded = loadOrFail documents tenant cancellationToken

            match loaded with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded loaded when loaded.Document.Status = status ->
                // Already there; the sessions were revoked when it got there.
                return DomainResult.Succeeded loaded.Document
            | DomainResult.Succeeded loaded when
                loaded.Document.Status = TenantStatus.Revoked && status = TenantStatus.Closed
                ->
                return DomainResult.Failed AdmissionFailure.Revoked
            | DomainResult.Succeeded loaded ->
                let! written =
                    write
                        documents
                        loaded
                        { loaded.Document with
                            IntegrationTokenVerifier = null
                            Status = status }
                        cancellationToken

                match written with
                | DomainResult.Succeeded document ->
                    let! _ = Sessions.revokeTenant documents listing tenant cancellationToken
                    return DomainResult.Succeeded document
                | DomainResult.Failed failure -> return DomainResult.Failed failure
        }

    /// Closes the tenant: its token is invalid, its sessions are revoked, its hand-offs are
    /// refused; its accounts, links, approvals, credentials and profiles are untouched.
    /// Idempotent.
    let close documents listing tenant cancellationToken =
        end' documents listing tenant TenantStatus.Closed cancellationToken

    /// Closure's hostile form: the same effects, and no re-admission.
    let revoke documents listing tenant cancellationToken =
        end' documents listing tenant TenantStatus.Revoked cancellationToken

    /// What a presented integration token establishes.
    let authenticate
        (documents: IStateDocumentStore)
        (token: string | null)
        (cancellationToken: CancellationToken)
        : Task<ChannelAuthentication> =
        task {
            match IntegrationTokens.parse token with
            | None -> return ChannelAuthentication.NoToken
            | Some(tenant, secret) ->
                let! found = Tenants.read documents tenant cancellationToken

                match found with
                | None -> return ChannelAuthentication.Unknown
                | Some document ->
                    match document.Status with
                    | TenantStatus.Closed -> return ChannelAuthentication.Closed
                    | TenantStatus.Revoked -> return ChannelAuthentication.Revoked
                    | _ ->
                        if IntegrationTokens.matches document.IntegrationTokenVerifier secret then
                            return ChannelAuthentication.Authenticated document
                        else
                            return ChannelAuthentication.Unknown
        }
