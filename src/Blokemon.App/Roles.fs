namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.Product

/// The two roles beyond Player, derived on every call from the records and never cached on a
/// session. Operator is the account's flag. Owner authority in a channel tenant needs the
/// session's account to be the one linked to the broadcaster subject the operator recorded at
/// admission, and in addition either an Issuer session that tenant itself issued or a
/// FirstParty session of an account whose approval record for the tenant is Approved; a
/// first-claim of the broadcaster's subject through another channel therefore confers nothing
/// here. The default tenant's owner is the account an operator assigned, holding a FirstParty
/// session or an Issuer session of the default tenant. Nothing a provider supplied at sign-in
/// enters into any of it.
module Roles =

    let isOperator
        (documents: IStateDocumentStore)
        (session: Session)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        Accounts.isOperator documents session.Account cancellationToken

    let private issuedBy (session: Session) (tenant: TenantId) =
        session.Provenance = SessionProvenance.Issuer && session.Tenant = tenant

    let isOwner
        (documents: IStateDocumentStore)
        (linkProvider: IdentityProviderName)
        (session: Session)
        (tenant: TenantDocument)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let tenantId = Tenants.idOf tenant

            if Tenants.isDefault tenant then
                return
                    String.Equals(
                        tenant.OwnerAccount,
                        session.Account.Value,
                        StringComparison.Ordinal
                    )
                    && (session.Provenance = SessionProvenance.FirstParty
                        || issuedBy session tenantId)
            else
                match tenant.BroadcasterSubject with
                | null -> return false
                | subjectText ->
                    match ExternalSubject.Create subjectText with
                    | DomainResult.Failed _ -> return false
                    | DomainResult.Succeeded subject ->
                        let! link =
                            IdentityLinks.resolve documents linkProvider subject cancellationToken

                        match link with
                        | LinkResolution.Linked account when
                            String.Equals(
                                account.Value,
                                session.Account.Value,
                                StringComparison.Ordinal
                            )
                            ->
                            if issuedBy session tenantId then
                                return true
                            elif session.Provenance = SessionProvenance.FirstParty then
                                let! approval =
                                    Approvals.load
                                        documents
                                        session.Account
                                        tenantId
                                        cancellationToken

                                match approval with
                                | DomainResult.Succeeded(Some loaded) ->
                                    return loaded.Document.Status = ApprovalStatus.Approved
                                | _ -> return false
                            else
                                return false
                        | _ -> return false
        }

    /// Every tenant the session holds owner authority in, in key order.
    let ownedTenants
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (linkProvider: IdentityProviderName)
        (session: Session)
        (cancellationToken: CancellationToken)
        : Task<TenantDocument list> =
        task {
            let! tenants = Tenants.all documents listing cancellationToken
            let mutable owned = []

            for tenant in tenants do
                let! owner = isOwner documents linkProvider session tenant cancellationToken

                if owner then
                    owned <- tenant :: owned

            return List.rev owned
        }
