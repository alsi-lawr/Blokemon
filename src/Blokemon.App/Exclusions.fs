namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.Product

/// The tenant owner's one sanction: exclusion is tenant-scoped state on the approval record,
/// created when there was none, and it ends the account's sessions in that tenant. Nothing
/// else about the account changes; readmission clears the exclusion and nothing else.
module Exclusions =

    let private toError (failure: ApprovalFailure) : ApiError =
        match failure with
        | ApprovalFailure.Conflict ->
            ApiError(
                "approval.conflict",
                "The approval changed underneath this request. Try again."
            )
        | ApprovalFailure.Damaged ->
            ApiError("approval.damaged", "An approval record could not be read. Nothing changed.")

    let private accountExists
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<Result<unit, ApiError>> =
        task {
            let! record = Accounts.load documents account cancellationToken

            match record with
            | AccountRecord.Live _ -> return Ok()
            | _ ->
                return Error(ApiError("account.not_found", "That account is not on this server."))
        }

    let exclude
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (account: AccountId)
        (tenant: TenantId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApiError>> =
        task {
            let! exists = accountExists documents account cancellationToken

            match exists with
            | Error error -> return DomainResult.Failed error
            | Ok() ->
                let! excluded = Approvals.exclude documents account tenant now cancellationToken

                match excluded with
                | DomainResult.Failed failure -> return DomainResult.Failed(toError failure)
                | DomainResult.Succeeded() ->
                    let! _ =
                        Sessions.revokeAccountInTenant
                            documents
                            listing
                            account
                            tenant
                            cancellationToken

                    return DomainResult.Succeeded()
        }

    let readmit
        (documents: IStateDocumentStore)
        (account: AccountId)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, ApiError>> =
        task {
            let! exists = accountExists documents account cancellationToken

            match exists with
            | Error error -> return DomainResult.Failed error
            | Ok() ->
                let! readmitted = Approvals.readmit documents account tenant cancellationToken

                match readmitted with
                | DomainResult.Failed failure -> return DomainResult.Failed(toError failure)
                | DomainResult.Succeeded() -> return DomainResult.Succeeded()
        }
