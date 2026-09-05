namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.Product

/// Recovery with a one-time code: the code is consumed, every session of the account it names
/// is revoked, and a Recovery session is issued that can do one thing, enrol a replacement
/// passkey. A disabled or erased account consumes nothing and gets no session.
module PasskeyRecovery =

    let toError (failure: RecoveryFailure) : ApiError =
        match failure with
        | RecoveryFailure.Refused ->
            ApiError("recovery.refused", "That code is not one of yours, or was used already.")
        | RecoveryFailure.Conflict ->
            ApiError("recovery.conflict", "Recovery changed underneath this request. Try again.")
        | RecoveryFailure.Damaged ->
            ApiError("recovery.damaged", "A recovery record could not be read. Nothing changed.")

    /// The refusal a locked-out client receives before any code is looked at.
    let locked () =
        ApiError("recovery.locked", "Too many attempts. Try again in fifteen minutes.")

    let recover
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (tenant: TenantId)
        (presented: string | null)
        (now: DateTimeOffset)
        (lifetime: TimeSpan)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<IssuedSession, RecoveryFailure>> =
        task {
            let! located = RecoveryCodes.locate documents listing presented cancellationToken

            match located with
            | None -> return DomainResult.Failed RecoveryFailure.Refused
            | Some(key, set, index) ->
                match AccountId.Create set.Document.Account with
                | DomainResult.Failed _ -> return DomainResult.Failed RecoveryFailure.Damaged
                | DomainResult.Succeeded account ->
                    let! active = Accounts.isActive documents account cancellationToken

                    if not active then
                        return DomainResult.Failed RecoveryFailure.Refused
                    else
                        let! consumed =
                            RecoveryCodes.consumeFrom documents key set index now cancellationToken

                        match consumed with
                        | DomainResult.Failed failure -> return DomainResult.Failed failure
                        | DomainResult.Succeeded() ->
                            let! _ =
                                Sessions.revokeAccount documents listing account cancellationToken

                            let! issued =
                                Sessions.issue
                                    documents
                                    account
                                    tenant
                                    SessionProvenance.Recovery
                                    now
                                    lifetime
                                    cancellationToken

                            return DomainResult.Succeeded issued
        }
