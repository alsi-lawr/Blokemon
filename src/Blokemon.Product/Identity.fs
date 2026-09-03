namespace Blokemon.Product

open System
open System.Collections.Immutable

/// Why a Blokemon-minted identity was rejected.
type IdentityValueFailure =
    | Required = 0
    | Malformed = 1

/// Every identity Blokemon mints is a GUID in its canonical form, so a document key composed
/// from one is fixed literals and hexadecimal only; a provider's subject never stands in for one.
module internal MintedIdentity =

    let canonical (value: Guid) = value.ToString "D"

    let create
        (value: string | null)
        (make: string -> 'TValue)
        : DomainResult<'TValue, IdentityValueFailure> =
        match value with
        | null -> DomainResult.Failed IdentityValueFailure.Required
        | text when String.IsNullOrWhiteSpace text ->
            DomainResult.Failed IdentityValueFailure.Required
        | text ->
            match Guid.TryParseExact(text, "D") with
            | true, parsed -> DomainResult.Succeeded(make (canonical parsed))
            | _ -> DomainResult.Failed IdentityValueFailure.Malformed

/// Identifies one Blokemon edition: the default tenant, or one admitted channel.
type TenantId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: TenantId, right: TenantId) = left.Equals(right)

    static member op_Inequality(left: TenantId, right: TenantId) = not (left.Equals(right))

    static member Create(value: string | null) : DomainResult<TenantId, IdentityValueFailure> =
        MintedIdentity.create value (fun valid -> { value = valid })

    /// A fresh identity, minted by Blokemon and nowhere else.
    static member Mint() : TenantId =
        { value = MintedIdentity.canonical (Guid.NewGuid()) }

/// Identifies one person across every tenant.
type AccountId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: AccountId, right: AccountId) = left.Equals(right)

    static member op_Inequality(left: AccountId, right: AccountId) = not (left.Equals(right))

    static member Create(value: string | null) : DomainResult<AccountId, IdentityValueFailure> =
        MintedIdentity.create value (fun valid -> { value = valid })

    /// A fresh identity, minted by Blokemon and nowhere else.
    static member Mint() : AccountId =
        { value = MintedIdentity.canonical (Guid.NewGuid()) }

/// Whether an account may act at all. Erased keeps the identity as a tombstone so it is never
/// reissued.
type AccountStatus =
    | Active = 0
    | Disabled = 1
    | Erased = 2

/// Whether a tenant admits play. Closing or revoking a tenant leaves its accounts untouched.
type TenantStatus =
    | Active = 0
    | Closed = 1
    | Revoked = 2

/// Why a provider name or a provider's subject was rejected.
type ExternalIdentityFailure =
    | Required = 0
    | TooLong = 1
    | Malformed = 2

/// Bounded ASCII text, or a typed failure. Each identity below states its own alphabet.
module internal BoundedAsciiText =

    let create
        (value: string | null)
        (maximumLength: int)
        (permitted: char -> bool)
        (make: string -> 'TValue)
        : DomainResult<'TValue, ExternalIdentityFailure> =
        match value with
        | null -> DomainResult.Failed ExternalIdentityFailure.Required
        | "" -> DomainResult.Failed ExternalIdentityFailure.Required
        | text when text.Length > maximumLength ->
            DomainResult.Failed ExternalIdentityFailure.TooLong
        | text when text |> Seq.forall permitted -> DomainResult.Succeeded(make text)
        | _ -> DomainResult.Failed ExternalIdentityFailure.Malformed

    let lowerAlphanumeric (character: char) =
        (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')

    let alphanumeric (character: char) =
        lowerAlphanumeric character || (character >= 'A' && character <= 'Z')

/// The name of a sign-in provider as it is recorded in an identity link: lower-case letters and
/// digits, at most 32 characters.
type IdentityProviderName =
    private
        { value: string }

    /// The longest provider name a link records.
    static member MaximumLength = 32

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: IdentityProviderName, right: IdentityProviderName) =
        left.Equals(right)

    static member op_Inequality(left: IdentityProviderName, right: IdentityProviderName) =
        not (left.Equals(right))

    static member Create
        (value: string | null)
        : DomainResult<IdentityProviderName, ExternalIdentityFailure> =
        BoundedAsciiText.create
            value
            IdentityProviderName.MaximumLength
            BoundedAsciiText.lowerAlphanumeric
            (fun valid -> { value = valid })

/// The identity a provider asserts for a person: letters, digits, dot, hyphen and underscore, at
/// most 64 characters. It is a lookup key, never an account's or a profile's identity.
type ExternalSubject =
    private
        { value: string }

    /// The longest subject a link records.
    static member MaximumLength = 64

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: ExternalSubject, right: ExternalSubject) = left.Equals(right)

    static member op_Inequality(left: ExternalSubject, right: ExternalSubject) =
        not (left.Equals(right))

    static member Create
        (value: string | null)
        : DomainResult<ExternalSubject, ExternalIdentityFailure> =
        BoundedAsciiText.create
            value
            ExternalSubject.MaximumLength
            (fun character ->
                BoundedAsciiText.alphanumeric character
                || character = '.'
                || character = '-'
                || character = '_')
            (fun valid -> { value = valid })

/// One external identity and the one account it signs in to. The pair is unique; the account is
/// the canonical identity.
type ExternalIdentityLink =
    { Provider: IdentityProviderName
      Subject: ExternalSubject
      Account: AccountId }

/// Why a tenant slug was rejected.
type TenantSlugFailure =
    | Required = 0
    | TooLong = 1
    | Malformed = 2
    | Reserved = 3

/// The segment that names a tenant under `/t/{slug}` and `/api/tenant/{slug}`: lower-case letters
/// and digits separated by single hyphens, at most 32 characters, and never a reserved segment.
type TenantSlug =
    private
        { value: string }

    /// The longest slug a tenant may take.
    static member MaximumLength = 32

    /// Every literal segment routed under `/api/tenant/` and `/t/`, which no slug may be.
    static member Reserved =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "self",
            "close",
            "continue",
            "handoff",
            "erasure"
        )

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: TenantSlug, right: TenantSlug) = left.Equals(right)

    static member op_Inequality(left: TenantSlug, right: TenantSlug) = not (left.Equals(right))

    static member Create(value: string | null) : DomainResult<TenantSlug, TenantSlugFailure> =
        let wellFormed (text: string) =
            text
            |> Seq.forall (fun character ->
                BoundedAsciiText.lowerAlphanumeric character || character = '-')
            && not (text.StartsWith '-')
            && not (text.EndsWith '-')
            && not (text.Contains "--")

        match value with
        | null -> DomainResult.Failed TenantSlugFailure.Required
        | "" -> DomainResult.Failed TenantSlugFailure.Required
        | text when text.Length > TenantSlug.MaximumLength ->
            DomainResult.Failed TenantSlugFailure.TooLong
        | text when not (wellFormed text) -> DomainResult.Failed TenantSlugFailure.Malformed
        | text when TenantSlug.Reserved.Contains text ->
            DomainResult.Failed TenantSlugFailure.Reserved
        | text -> DomainResult.Succeeded { value = text }
