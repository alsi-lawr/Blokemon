namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// An approval as it was read, at the revision a change must be written against.
type internal LoadedApproval =
    { Revision: int64
      Document: ApprovalDocument }

/// Why an approval could not be read or changed.
[<RequireQualifiedAccess>]
type internal ApprovalFailure =
    /// The record changed underneath the operation; nothing was written.
    | Conflict
    /// The stored record cannot be read; nothing was written.
    | Damaged

/// Whether a tenant may act for an account. Exclusion is the owner's tenant-scoped refusal and
/// dominates the approval status; readmission lifts it and changes nothing else.
module internal Approvals =

    /// The record an exclusion creates when the tenant was never approved for the account.
    let pending (account: AccountId) (tenant: TenantId) : ApprovalDocument =
        { SchemaVersion = approvalSchemaVersion
          Account = account.Value
          Tenant = tenant.Value
          Status = ApprovalStatus.Pending
          ApprovedAt = Nullable()
          ExcludedAt = Nullable()
          AdoptedAt = Nullable() }

    let isExcluded (approval: ApprovalDocument) = approval.ExcludedAt.HasValue

    let excluded (at: DateTimeOffset) (approval: ApprovalDocument) =
        { approval with
            ExcludedAt = Nullable at }

    let readmitted (approval: ApprovalDocument) =
        { approval with
            ExcludedAt = Nullable() }

    /// An approval is a live route into its account only while it is approved, its tenant is
    /// active and the account is not excluded there.
    let isLiveRoute (approval: ApprovalDocument) (tenant: TenantDocument) =
        String.Equals(approval.Tenant, tenant.Id, StringComparison.Ordinal)
        && approval.Status = ApprovalStatus.Approved
        && tenant.Status = TenantStatus.Active
        && not (isExcluded approval)

    let load
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoadedApproval option, ApprovalFailure>> =
        task {
            let! stored = documents.Read(approvalKey account tenant, cancellationToken)

            match stored with
            | null -> return DomainResult.Succeeded None
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<ApprovalDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Ok(NonNull value) when value.SchemaVersion = approvalSchemaVersion ->
                    return
                        DomainResult.Succeeded(
                            Some
                                { Revision = document.Revision
                                  Document = value }
                        )
                | _ -> return DomainResult.Failed ApprovalFailure.Damaged
        }

    let private save
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (existing: LoadedApproval option)
        (document: ApprovalDocument)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApprovalFailure>> =
        task {
            let key = approvalKey account tenant
            let serialized = JsonSerializer.Serialize(document, json)

            let! write =
                match existing with
                | None -> documents.Create(key, serialized, cancellationToken)
                | Some loaded ->
                    documents.Update(key, loaded.Revision, serialized, cancellationToken)

            match write with
            | :? DocumentWriteResult.Written -> return DomainResult.Succeeded()
            | _ -> return DomainResult.Failed ApprovalFailure.Conflict
        }

    /// Refuses the account in the tenant from now on, creating the record when there is none.
    let exclude
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (at: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApprovalFailure>> =
        task {
            let! loaded = load documents account tenant cancellationToken

            match loaded with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded None ->
                return!
                    save
                        documents
                        account
                        tenant
                        None
                        (excluded at (pending account tenant))
                        cancellationToken
            | DomainResult.Succeeded(Some existing) when isExcluded existing.Document ->
                return DomainResult.Succeeded()
            | DomainResult.Succeeded(Some existing) ->
                return!
                    save
                        documents
                        account
                        tenant
                        (Some existing)
                        (excluded at existing.Document)
                        cancellationToken
        }

    /// Lifts an exclusion. The approval's status is untouched; a record that was never excluded
    /// or never existed is left as it is.
    let readmit
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApprovalFailure>> =
        task {
            let! loaded = load documents account tenant cancellationToken

            match loaded with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded None -> return DomainResult.Succeeded()
            | DomainResult.Succeeded(Some existing) when not (isExcluded existing.Document) ->
                return DomainResult.Succeeded()
            | DomainResult.Succeeded(Some existing) ->
                return!
                    save
                        documents
                        account
                        tenant
                        (Some existing)
                        (readmitted existing.Document)
                        cancellationToken
        }
