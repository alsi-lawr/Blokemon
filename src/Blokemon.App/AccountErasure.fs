namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Why an account was not erased. Nothing is written for any of these.
[<RequireQualifiedAccess>]
type ErasureRefusal =
    /// An Issuer session of a channel tenant: a channel can never erase an account (D-043).
    | ChannelSession
    | NotFound
    | Conflict
    | Damaged

/// What an erasure left: when the account was erased, and whether this call found it erased
/// already (a repeated erase is a terminal no-op).
type Erasure =
    { ErasedAt: DateTimeOffset
      Repeated: bool }

/// Erasure: the profile documents, links, approvals, credentials, recovery codes and sessions
/// of the account are deleted and `account/{id}` becomes a tombstone holding only the id and
/// erased-at, so the id is never reissued. Distinct from purge, which deletes the three profile
/// documents and leaves the account, its credentials and its links in place.
module AccountErasure =

    let toError (refusal: ErasureRefusal) : ApiError =
        match refusal with
        | ErasureRefusal.ChannelSession ->
            ApiError(
                "erase.provenance",
                "Erase from the main Blokemon: sign in there with your passkey or through its sign-in."
            )
        | ErasureRefusal.NotFound ->
            ApiError("account.not_found", "That account is not on this server.")
        | ErasureRefusal.Conflict ->
            ApiError("account.conflict", "The account changed underneath this request. Try again.")
        | ErasureRefusal.Damaged ->
            ApiError("account.damaged", "The account record could not be read. Nothing changed.")

    /// Whether the session may erase its own account: a FirstParty session, or an Issuer
    /// session of the default tenant (the core sign-in). A channel tenant's Issuer session may
    /// not; a Recovery session never reaches here.
    let maySelfErase
        (documents: IStateDocumentStore)
        (session: Session)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            match session.Provenance with
            | SessionProvenance.FirstParty -> return true
            | SessionProvenance.Issuer ->
                let! tenant = Tenants.read documents session.Tenant cancellationToken

                match tenant with
                | Some tenant -> return Tenants.isDefault tenant
                | None -> return false
            | _ -> return false
        }

    let private deleteUnder
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (prefix: string)
        (cancellationToken: CancellationToken)
        : Task =
        task {
            let! summaries = listing.List(prefix, cancellationToken)

            for summary in summaries do
                do! documents.Delete(summary.Key, cancellationToken)
        }

    /// The prefix of the saved battle's migration backups, which scope by the battle's key.
    let backupPrefix (keys: PlayerDocumentKeys) = $"match-migration-backup/{keys.Match}/"

    let erase
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<Erasure, ErasureRefusal>> =
        task {
            let! record = Accounts.load documents account cancellationToken

            match record with
            | AccountRecord.Absent -> return DomainResult.Failed ErasureRefusal.NotFound
            | AccountRecord.Damaged -> return DomainResult.Failed ErasureRefusal.Damaged
            | AccountRecord.Erased tombstone ->
                return
                    DomainResult.Succeeded
                        { ErasedAt = tombstone.ErasedAt
                          Repeated = true }
            | AccountRecord.Live(revision, _) ->
                // Everything else first and the tombstone last, so an erase interrupted part-way
                // is finished by the next one rather than sealed with data left behind.
                let! _ = Sessions.revokeAccount documents listing account cancellationToken
                do! deleteUnder documents listing (Credentials.prefix account) cancellationToken
                do! documents.Delete(RecoveryCodes.key account, cancellationToken)
                do! deleteUnder documents listing $"approval/{account}/" cancellationToken
                let! links = IdentityLinks.keysFor documents listing account cancellationToken

                for key in links do
                    do! documents.Delete(key, cancellationToken)

                let keys = PlayerDocumentKeys.forAccount account
                do! documents.Delete(keys.Profile, cancellationToken)
                do! documents.Delete(keys.Match, cancellationToken)
                do! documents.Delete(keys.MatchHistory, cancellationToken)
                do! deleteUnder documents listing (backupPrefix keys) cancellationToken

                let! tombstoned =
                    documents.Update(
                        accountKey account,
                        revision,
                        JsonSerializer.Serialize(Accounts.tombstone account now, json),
                        cancellationToken
                    )

                match tombstoned with
                | :? DocumentWriteResult.Written ->
                    return DomainResult.Succeeded { ErasedAt = now; Repeated = false }
                | _ -> return DomainResult.Failed ErasureRefusal.Conflict
        }
