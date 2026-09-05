namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Why an account's lifecycle could not be changed. Nothing is written for any of these.
[<RequireQualifiedAccess>]
type LifecycleFailure =
    | NotFound
    /// The account is a tombstone; nothing about it changes again.
    | Erased
    /// Only the default tenant has an assigned owner; a channel's owner is derived.
    | NotDefaultTenant
    | TenantNotFound
    | Conflict
    | Damaged

/// The operator's account-wide operations: disable and enable (data kept, sessions revoked,
/// sign-in refused with its typed error), the operator grant, and the default tenant's owner.
/// A tenant owner's exclusion is tenant-scoped state on the approval record (Approvals) and
/// never touches the account.
module AccountLifecycle =

    let toError (failure: LifecycleFailure) : ApiError =
        match failure with
        | LifecycleFailure.NotFound ->
            ApiError("account.not_found", "That account is not on this server.")
        | LifecycleFailure.Erased -> ApiError("account.erased", "This account was erased.")
        | LifecycleFailure.NotDefaultTenant ->
            ApiError(
                "tenant.owner_derived",
                "A channel's owner is its broadcaster; only the main Blokemon's owner is assigned."
            )
        | LifecycleFailure.TenantNotFound ->
            ApiError("tenant.not_found", "That channel is not on this server.")
        | LifecycleFailure.Conflict ->
            ApiError("account.conflict", "The account changed underneath this request. Try again.")
        | LifecycleFailure.Damaged ->
            ApiError("account.damaged", "The account record could not be read. Nothing changed.")

    let private change
        (documents: IStateDocumentStore)
        (account: AccountId)
        (apply: AccountDocument -> AccountDocument)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountDocument, LifecycleFailure>> =
        task {
            let! record = Accounts.load documents account cancellationToken

            match record with
            | AccountRecord.Absent -> return DomainResult.Failed LifecycleFailure.NotFound
            | AccountRecord.Erased _ -> return DomainResult.Failed LifecycleFailure.Erased
            | AccountRecord.Damaged -> return DomainResult.Failed LifecycleFailure.Damaged
            | AccountRecord.Live(revision, document) ->
                let changed = apply document

                if changed = document then
                    return DomainResult.Succeeded document
                else
                    let! write =
                        documents.Update(
                            accountKey account,
                            revision,
                            JsonSerializer.Serialize(changed, json),
                            cancellationToken
                        )

                    match write with
                    | :? DocumentWriteResult.Written -> return DomainResult.Succeeded changed
                    | _ -> return DomainResult.Failed LifecycleFailure.Conflict
        }

    /// Disables the account everywhere: its sessions are revoked and sign-in is refused until
    /// it is enabled again. Its data is untouched. Idempotent.
    let disable
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountDocument, LifecycleFailure>> =
        task {
            let! changed =
                change
                    documents
                    account
                    (fun document ->
                        { document with
                            Status = AccountStatus.Disabled })
                    cancellationToken

            match changed with
            | DomainResult.Succeeded document ->
                let! _ = Sessions.revokeAccount documents listing account cancellationToken
                return DomainResult.Succeeded document
            | failed -> return failed
        }

    /// Enables a disabled account. Idempotent.
    let enable
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountDocument, LifecycleFailure>> =
        change
            documents
            account
            (fun document ->
                { document with
                    Status = AccountStatus.Active })
            cancellationToken

    /// Flags the account operator. Whether the granting session may do so is the host's check
    /// (a FirstParty session of an operator). Idempotent.
    let grantOperator
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountDocument, LifecycleFailure>> =
        change
            documents
            account
            (fun document -> { document with Operator = true })
            cancellationToken

    /// Records the account as the default tenant's owner. A channel tenant's owner is derived
    /// from its broadcaster and is refused here.
    let assignOwner
        (documents: IStateDocumentStore)
        (tenant: TenantId)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<TenantDocument, LifecycleFailure>> =
        task {
            let! record = Accounts.load documents account cancellationToken

            match record with
            | AccountRecord.Absent -> return DomainResult.Failed LifecycleFailure.NotFound
            | AccountRecord.Erased _ -> return DomainResult.Failed LifecycleFailure.Erased
            | AccountRecord.Damaged -> return DomainResult.Failed LifecycleFailure.Damaged
            | AccountRecord.Live _ ->
                let! stored = documents.Read(tenantKey tenant, cancellationToken)

                match stored with
                | null -> return DomainResult.Failed LifecycleFailure.TenantNotFound
                | document ->
                    match Tenants.parse document with
                    | None -> return DomainResult.Failed LifecycleFailure.Damaged
                    | Some current when not (Tenants.isDefault current) ->
                        return DomainResult.Failed LifecycleFailure.NotDefaultTenant
                    | Some current when
                        String.Equals(current.OwnerAccount, account.Value, StringComparison.Ordinal)
                        ->
                        return DomainResult.Succeeded current
                    | Some current ->
                        let assigned =
                            { current with
                                OwnerAccount = account.Value }

                        let! write =
                            documents.Update(
                                tenantKey tenant,
                                document.Revision,
                                JsonSerializer.Serialize(assigned, json),
                                cancellationToken
                            )

                        match write with
                        | :? DocumentWriteResult.Written -> return DomainResult.Succeeded assigned
                        | _ -> return DomainResult.Failed LifecycleFailure.Conflict
        }
