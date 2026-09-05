namespace Blokemon.App

open System
open System.Buffers.Text
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// What a code was minted for. The kind is part of the stored record and not inferable from
/// the code, so each exchange refuses the other kind.
type HandoffKind =
    /// A channel handing off one of its viewers, bound to the tenant and the subject.
    | Channel = 0
    /// A session continuing in a top-level window, bound to its account, tenant and provenance.
    | Continuation = 1

/// A single-use code as it is stored at `handoff/{id}`: its hash, kind, binding and expiry.
type HandoffDocument =
    { SchemaVersion: int
      Id: string
      Kind: HandoffKind
      SecretHash: string
      Tenant: string
      Subject: string | null
      DisplayNameHint: string | null
      Account: string | null
      Provenance: Nullable<SessionProvenance>
      IssuedAt: DateTimeOffset
      ExpiresAt: DateTimeOffset }

/// What a code is bound to when it is minted.
[<RequireQualifiedAccess>]
type HandoffBinding =
    | Channel of tenant: TenantId * subject: ExternalSubject * displayNameHint: (string | null)
    | Continuation of session: Session

/// A code as it was just minted: the one time it exists in clear.
type IssuedHandoff =
    { Id: string
      Code: string
      ExpiresAt: DateTimeOffset }

/// Why a code was not consumed. Nothing is written for any of these.
[<RequireQualifiedAccess>]
type HandoffFailure =
    /// Malformed, unknown, or consumed already.
    | Refused
    /// Known but past its lifetime.
    | Expired
    /// A code of the other kind.
    | WrongKind
    /// Bound to a tenant other than the one the page runs as.
    | OtherTenant

/// Hand-off and continuation codes: at least 128 bits from the cryptographic generator, held
/// as hashes, sixty seconds long, consumed exactly once by deleting the record against the
/// revision it was read at.
module HandoffCodes =

    let schemaVersion = 1

    let Lifetime = TimeSpan.FromSeconds 60.0

    /// The entropy of a code's secret part, in bytes.
    let SecretBytes = 24

    let key (id: string) = $"handoff/{id}"

    let private hash (secret: string) =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes secret))

    let private secretsMatch (stored: string) (presented: string) =
        CryptographicOperations.FixedTimeEquals(
            ReadOnlySpan<byte>(Encoding.UTF8.GetBytes stored),
            ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(hash presented))
        )

    /// `{guid}.{secret}`: the id finds the record, the secret proves the code.
    let private parse (code: string | null) =
        match code with
        | null -> None
        | text ->
            match text.Split('.', 2) with
            | [| id; secret |] when secret.Length > 0 ->
                match Guid.TryParseExact(id, "D") with
                | true, parsed when id = parsed.ToString "D" -> Some(id, secret)
                | _ -> None
            | _ -> None

    let private read
        (documents: IStateDocumentStore)
        (id: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let! stored = documents.Read(key id, cancellationToken)

            match stored with
            | null -> return None
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<HandoffDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Ok(NonNull value) when
                    value.SchemaVersion = schemaVersion
                    && String.Equals(value.Id, id, StringComparison.Ordinal)
                    ->
                    return Some(document, value)
                | _ -> return None
        }

    /// Mints a code for the binding, expiring `Lifetime` after `now`.
    let mint
        (documents: IStateDocumentStore)
        (binding: HandoffBinding)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<IssuedHandoff> =
        task {
            let id = Guid.NewGuid().ToString "D"
            let secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes SecretBytes)
            let expiresAt = now + Lifetime

            let document =
                match binding with
                | HandoffBinding.Channel(tenant, subject, hint) ->
                    { SchemaVersion = schemaVersion
                      Id = id
                      Kind = HandoffKind.Channel
                      SecretHash = hash secret
                      Tenant = tenant.Value
                      Subject = subject.Value
                      DisplayNameHint = hint
                      Account = null
                      Provenance = Nullable()
                      IssuedAt = now
                      ExpiresAt = expiresAt }
                | HandoffBinding.Continuation session ->
                    { SchemaVersion = schemaVersion
                      Id = id
                      Kind = HandoffKind.Continuation
                      SecretHash = hash secret
                      Tenant = session.Tenant.Value
                      Subject = null
                      DisplayNameHint = null
                      Account = session.Account.Value
                      Provenance = Nullable session.Provenance
                      IssuedAt = now
                      ExpiresAt = expiresAt }

            let! write =
                documents.Create(
                    key id,
                    JsonSerializer.Serialize(document, json),
                    cancellationToken
                )

            match write with
            | :? DocumentWriteResult.Written ->
                return
                    { Id = id
                      Code = $"{id}.{secret}"
                      ExpiresAt = expiresAt }
            | _ -> return raise (InvalidOperationException "A freshly minted code id collided.")
        }

    /// Consumes the code if it is of the expected kind, bound to the expected tenant and still
    /// live. The record is deleted against the revision it was read at, so two presentations
    /// succeed at most once; a refused presentation writes nothing.
    let consume
        (documents: IStateDocumentStore)
        (code: string | null)
        (expected: HandoffKind)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<HandoffDocument, HandoffFailure>> =
        task {
            match parse code with
            | None -> return DomainResult.Failed HandoffFailure.Refused
            | Some(id, secret) ->
                let! found = read documents id cancellationToken

                match found with
                | None -> return DomainResult.Failed HandoffFailure.Refused
                | Some(stored, document) ->
                    if not (secretsMatch document.SecretHash secret) then
                        return DomainResult.Failed HandoffFailure.Refused
                    elif document.Kind <> expected then
                        return DomainResult.Failed HandoffFailure.WrongKind
                    elif
                        not (String.Equals(document.Tenant, tenant.Value, StringComparison.Ordinal))
                    then
                        return DomainResult.Failed HandoffFailure.OtherTenant
                    elif document.ExpiresAt <= now then
                        return DomainResult.Failed HandoffFailure.Expired
                    else
                        let! deleted =
                            documents.DeleteIfUnchanged(
                                key id,
                                stored.Revision,
                                stored.Json,
                                cancellationToken
                            )

                        match deleted with
                        | :? DocumentDeleteResult.Deleted -> return DomainResult.Succeeded document
                        | _ -> return DomainResult.Failed HandoffFailure.Refused
        }

    /// Removes every code past its expiry and returns how many; the listing's expiry
    /// projection decides without reading a body.
    let sweep
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<int> =
        task {
            let! codes = listing.List("handoff/", cancellationToken)
            let mutable removed = 0

            for summary in codes do
                match summary.Projection with
                | :? DocumentProjection.Expiry as expiry when
                    expiry.ExpiresAt.HasValue && expiry.ExpiresAt.Value <= now
                    ->
                    do! documents.Delete(summary.Key, cancellationToken)
                    removed <- removed + 1
                | _ -> ()

            return removed
        }

    let toError (failure: HandoffFailure) : ApiError =
        match failure with
        | HandoffFailure.Refused ->
            ApiError("handoff.refused", "That sign-in code is not valid or was used already.")
        | HandoffFailure.Expired -> ApiError("handoff.expired", "That sign-in code has expired.")
        | HandoffFailure.WrongKind ->
            ApiError("handoff.kind", "That code is not for this kind of sign-in.")
        | HandoffFailure.OtherTenant ->
            ApiError("handoff.tenant", "That sign-in code is for another channel.")
