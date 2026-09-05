namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// One passkey, stored at `credential/{account}/{id}`. The authenticator's own credential id is
/// a field rather than a key segment: it is authenticator-minted and unbounded, so the key stays
/// fixed literals and Blokemon-minted identities, and an account's passkeys are one prefix.
type CredentialDocument =
    {
        SchemaVersion: int
        Id: string
        Account: string
        /// The authenticator's credential id, base64url, as the browser presents it.
        CredentialId: string
        /// The credential's public key as the verifier stored it, base64.
        PublicKey: string
        SignCount: uint32
        /// The provenance of the session that enrolled this passkey.
        Provenance: SessionProvenance
        /// The tenant that session was issued by, recorded for an Issuer enrolment.
        Tenant: string | null
        EnrolledAt: DateTimeOffset
    }

/// A credential as it was read, at the revision a change must be written against.
type LoadedCredential =
    { Revision: int64
      Document: CredentialDocument }

/// Why a credential could not be written.
[<RequireQualifiedAccess>]
type CredentialFailure =
    /// The account already holds this authenticator credential; nothing changed.
    | AlreadyEnrolled
    /// The record changed underneath the write; nothing changed.
    | Conflict

/// Passkeys: enrolled onto an account, found by the account the assertion's user handle names
/// and the credential id it carries, and updated with the sign count each assertion reports.
module Credentials =

    let schemaVersion = 1

    let prefix (account: AccountId) = $"credential/{account}/"

    let key (account: AccountId) (id: string) = $"credential/{account}/{id}"

    let private parse (document: StoredDocument) : CredentialDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<CredentialDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when
            value.SchemaVersion = schemaVersion && Enum.IsDefined value.Provenance
            ->
            Some value
        | _ -> None

    /// Every readable passkey of the account, in key order.
    let forAccount
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<LoadedCredential list> =
        task {
            let! summaries = listing.List(prefix account, cancellationToken)
            let mutable credentials = []

            for summary in summaries do
                let! stored = documents.Read(summary.Key, cancellationToken)

                match stored with
                | null -> ()
                | document ->
                    match parse document with
                    | Some credential ->
                        credentials <-
                            { Revision = document.Revision
                              Document = credential }
                            :: credentials
                    | None -> ()

            return List.rev credentials
        }

    /// Whether the account holds any passkey.
    let anyFor
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! credentials = forAccount documents listing account cancellationToken
            return not (List.isEmpty credentials)
        }

    /// The account's passkey with this authenticator credential id, if it has one.
    let find
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (credentialId: string)
        (cancellationToken: CancellationToken)
        : Task<LoadedCredential option> =
        task {
            let! credentials = forAccount documents listing account cancellationToken

            return
                credentials
                |> List.tryFind (fun loaded ->
                    String.Equals(
                        loaded.Document.CredentialId,
                        credentialId,
                        StringComparison.Ordinal
                    ))
        }

    /// Enrols a verified passkey onto the account, recording the provenance it came from.
    let enrol
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (credentialId: string)
        (publicKey: string)
        (signCount: uint32)
        (provenance: SessionProvenance)
        (tenant: TenantId | null)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<CredentialDocument, CredentialFailure>> =
        task {
            let! existing = find documents listing account credentialId cancellationToken

            match existing with
            | Some _ -> return DomainResult.Failed CredentialFailure.AlreadyEnrolled
            | None ->
                let id = Guid.NewGuid().ToString "D"

                let document =
                    { SchemaVersion = schemaVersion
                      Id = id
                      Account = account.Value
                      CredentialId = credentialId
                      PublicKey = publicKey
                      SignCount = signCount
                      Provenance = provenance
                      Tenant =
                        match tenant with
                        | null -> null
                        | bound -> bound.Value
                      EnrolledAt = now }

                let! write =
                    documents.Create(
                        key account id,
                        JsonSerializer.Serialize(document, json),
                        cancellationToken
                    )

                match write with
                | :? DocumentWriteResult.Written -> return DomainResult.Succeeded document
                | _ -> return DomainResult.Failed CredentialFailure.Conflict
        }

    /// Records the sign count an assertion reported, against the revision the credential was
    /// read at, so two assertions cannot both advance from the same count.
    let recordSignCount
        (documents: IStateDocumentStore)
        (loaded: LoadedCredential)
        (signCount: uint32)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, CredentialFailure>> =
        task {
            let document = loaded.Document

            let! write =
                documents.Update(
                    key
                        (match AccountId.Create document.Account with
                         | DomainResult.Succeeded account -> account
                         | DomainResult.Failed _ ->
                             raise (
                                 InvalidOperationException "A stored credential names no account."
                             ))
                        document.Id,
                    loaded.Revision,
                    JsonSerializer.Serialize({ document with SignCount = signCount }, json),
                    cancellationToken
                )

            match write with
            | :? DocumentWriteResult.Written -> return DomainResult.Succeeded()
            | _ -> return DomainResult.Failed CredentialFailure.Conflict
        }
