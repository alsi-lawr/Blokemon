namespace Blokemon.App

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// One signed-in presence, stored at `session/{id}`. The bearer token is never stored: the
/// document holds a hash of the token's secret part and is found by the id the token carries.
type SessionDocument =
    { SchemaVersion: int
      Id: string
      Account: string
      Tenant: string
      Provenance: SessionProvenance
      SecretHash: string
      IssuedAt: DateTimeOffset
      ExpiresAt: DateTimeOffset }

/// What a validated bearer token stands for.
type Session =
    { Id: string
      Account: AccountId
      Tenant: TenantId
      Provenance: SessionProvenance
      ExpiresAt: DateTimeOffset }

/// A session as it was just issued: the record and the one token that reaches it. The token
/// leaves the server exactly once, in the response that issued it.
type IssuedSession = { Session: Session; Token: string }

/// What presenting a bearer token established.
[<RequireQualifiedAccess>]
type SessionValidation =
    | Valid of Session
    /// The token is absent, malformed, unknown, revoked, or its account can no longer act.
    | Required
    /// The session existed and has passed its absolute expiry.
    | Expired

/// Sessions: issued for an account in a tenant with a stated provenance and an absolute expiry,
/// validated on every request, revoked by deleting their document. Nothing extends a session.
module Sessions =

    /// The lifetime a deployment gets when it states none.
    let DefaultLifetime = TimeSpan.FromHours 8.0

    /// The longest lifetime a deployment may configure.
    let MaximumLifetime = TimeSpan.FromHours 24.0

    let schemaVersion = 1

    let key (id: string) = $"session/{id}"

    let private hash (secret: string) =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes secret))

    let private secretsMatch (stored: string) (presented: string) =
        let storedBytes = Encoding.UTF8.GetBytes stored
        let presentedBytes = Encoding.UTF8.GetBytes(hash presented)

        CryptographicOperations.FixedTimeEquals(
            ReadOnlySpan<byte>(storedBytes),
            ReadOnlySpan<byte>(presentedBytes)
        )

    /// Splits a bearer token into the session id that finds the document and the secret the
    /// document's hash is checked against. Anything that is not `{guid}.{secret}` is no token.
    let private parse (token: string | null) =
        match token with
        | null -> None
        | text ->
            match text.Split('.', 2) with
            | [| id; secret |] when secret.Length > 0 ->
                match Guid.TryParseExact(id, "D") with
                | true, parsed when id = parsed.ToString "D" -> Some(id, secret)
                | _ -> None
            | _ -> None

    let private readDocument
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
                        Ok(JsonSerializer.Deserialize<SessionDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Ok(NonNull value) when
                    value.SchemaVersion = schemaVersion
                    && Enum.IsDefined value.Provenance
                    && String.Equals(value.Id, id, StringComparison.Ordinal)
                    ->
                    return Some value
                | _ -> return None
        }

    let private toSession (document: SessionDocument) =
        match AccountId.Create document.Account, TenantId.Create document.Tenant with
        | DomainResult.Succeeded account, DomainResult.Succeeded tenant ->
            Some
                { Id = document.Id
                  Account = account
                  Tenant = tenant
                  Provenance = document.Provenance
                  ExpiresAt = document.ExpiresAt }
        | _ -> None

    /// Issues a session that expires exactly `lifetime` after `now`. The lifetime is the
    /// deployment's configured one, already bounded.
    let issue
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (provenance: SessionProvenance)
        (now: DateTimeOffset)
        (lifetime: TimeSpan)
        (cancellationToken: CancellationToken)
        : Task<IssuedSession> =
        task {
            let id = Guid.NewGuid().ToString "D"
            let secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes 32)

            let document =
                { SchemaVersion = schemaVersion
                  Id = id
                  Account = account.Value
                  Tenant = tenant.Value
                  Provenance = provenance
                  SecretHash = hash secret
                  IssuedAt = now
                  ExpiresAt = now + lifetime }

            let! write =
                documents.Create(
                    key id,
                    JsonSerializer.Serialize(document, json),
                    cancellationToken
                )

            match write with
            | :? DocumentWriteResult.Written ->
                return
                    { Session =
                        { Id = id
                          Account = account
                          Tenant = tenant
                          Provenance = provenance
                          ExpiresAt = document.ExpiresAt }
                      Token = $"{id}.{secret}" }
            | _ -> return raise (InvalidOperationException "A freshly minted session id collided.")
        }

    /// Revokes a session; a session already gone is left gone.
    let revoke
        (documents: IStateDocumentStore)
        (id: string)
        (cancellationToken: CancellationToken)
        : Task =
        documents.Delete(key id, cancellationToken)

    /// Reads the account a session names, so a session whose account was disabled or erased
    /// stops acting the moment that happened rather than when a sweep notices.
    let private accountMayAct
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        =
        task {
            let! stored = documents.Read(accountKey account, cancellationToken)

            match stored with
            | null -> return false
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<AccountDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Ok(NonNull value) when value.SchemaVersion = accountSchemaVersion ->
                    return value.Status = AccountStatus.Active
                | _ -> return false
        }

    /// What a presented bearer token establishes at `now`. A session whose account can no
    /// longer act is revoked here and then refused.
    let validate
        (documents: IStateDocumentStore)
        (token: string | null)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<SessionValidation> =
        task {
            match parse token with
            | None -> return SessionValidation.Required
            | Some(id, secret) ->
                let! document = readDocument documents id cancellationToken

                match document with
                | None -> return SessionValidation.Required
                | Some stored when not (secretsMatch stored.SecretHash secret) ->
                    return SessionValidation.Required
                | Some stored when stored.ExpiresAt <= now -> return SessionValidation.Expired
                | Some stored ->
                    match toSession stored with
                    | None -> return SessionValidation.Required
                    | Some session ->
                        let! mayAct = accountMayAct documents session.Account cancellationToken

                        if mayAct then
                            return SessionValidation.Valid session
                        else
                            do! revoke documents id cancellationToken
                            return SessionValidation.Required
        }
