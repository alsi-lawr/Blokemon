namespace Blokemon.App

open System
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.Product

// The four server-only documents behind tenancy and accounts. Like ProductDocument these are
// plain public records so System.Text.Json's reflection resolver can construct them; they carry
// no behaviour. Every field is stated here and versioned by SchemaVersion; BLOKEMON-151 populates
// the tenant's admission slots and drives its status without redefining the record.

/// The verifier of a tenant's integration token: a hash of the token's secret part and when it
/// was issued. The token itself is never stored.
type IntegrationTokenVerifier =
    { Hash: string
      IssuedAt: DateTimeOffset }

/// One Blokemon edition, stored at `tenant/{id}`. The default tenant is served at `/` and holds
/// the same issuer slots as a channel.
type TenantDocument =
    {
        SchemaVersion: int
        Id: string
        Slug: string
        DisplayLabel: string
        /// The channel's broadcaster as the provider's subject: digits only, bounded, and absent
        /// for the default tenant. Admission populates it.
        BroadcasterSubject: string | null
        /// The exact origin of the host that embeds this tenant; absent for the default tenant.
        /// Admission populates it.
        RegisteredParentOrigin: string | null
        /// Admission populates it; rotation replaces it.
        IntegrationTokenVerifier: IntegrationTokenVerifier | null
        Status: TenantStatus
        /// The account an operator assigned as the default tenant's owner.
        OwnerAccount: string | null
        CreatedAt: DateTimeOffset
    }

/// One person, stored at `account/{id}`. Lifecycle state only: it names no provider.
type AccountDocument =
    { SchemaVersion: int
      Id: string
      Status: AccountStatus
      Operator: bool
      CreatedAt: DateTimeOffset
      ErasedAt: Nullable<DateTimeOffset> }

/// One external identity's route to its account, stored at `link/{provider}/{subject}`: the
/// only key a provider subject ever appears in.
type IdentityLinkDocument =
    { SchemaVersion: int
      Provider: string
      Subject: string
      Account: string
      LinkedAt: DateTimeOffset }

/// Where an approval stands. Exclusion is separate state that dominates either value.
type ApprovalStatus =
    | Pending = 0
    | Approved = 1

/// Whether a tenant may act for an account, stored at `approval/{account}/{tenant}`.
type ApprovalDocument =
    {
        SchemaVersion: int
        Account: string
        Tenant: string
        Status: ApprovalStatus
        ApprovedAt: Nullable<DateTimeOffset>
        /// Set while the tenant's owner has excluded the account; refuses the account in this
        /// tenant whatever Status says.
        ExcludedAt: Nullable<DateTimeOffset>
        /// Set when the core sign-in adopted an account with no other live route.
        AdoptedAt: Nullable<DateTimeOffset>
    }

/// The keys, schema versions and serializer of the tenancy documents.
module internal TenancyDocuments =

    let tenantSchemaVersion = 1
    let accountSchemaVersion = 1
    let linkSchemaVersion = 1
    let approvalSchemaVersion = 1

    let json =
        let options =
            JsonSerializerOptions(
                JsonSerializerDefaults.Web,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            )

        options.Converters.Add(JsonStringEnumConverter())
        options

    let tenantKey (tenant: TenantId) = $"tenant/{tenant}"

    let accountKey (account: AccountId) = $"account/{account}"

    let linkKey (provider: IdentityProviderName) (subject: ExternalSubject) =
        $"link/{provider}/{subject}"

    let approvalKey (account: AccountId) (tenant: TenantId) = $"approval/{account}/{tenant}"

    /// A tenant as admission first records it: active, with every issuer slot empty.
    let newTenant
        (id: TenantId)
        (slug: TenantSlug)
        (displayLabel: string)
        (createdAt: DateTimeOffset)
        : TenantDocument =
        { SchemaVersion = tenantSchemaVersion
          Id = id.Value
          Slug = slug.Value
          DisplayLabel = displayLabel
          BroadcasterSubject = null
          RegisteredParentOrigin = null
          IntegrationTokenVerifier = null
          Status = TenantStatus.Active
          OwnerAccount = null
          CreatedAt = createdAt }

    /// An account as first sign-in creates it.
    let newAccount (id: AccountId) (createdAt: DateTimeOffset) : AccountDocument =
        { SchemaVersion = accountSchemaVersion
          Id = id.Value
          Status = AccountStatus.Active
          Operator = false
          CreatedAt = createdAt
          ErasedAt = Nullable() }
