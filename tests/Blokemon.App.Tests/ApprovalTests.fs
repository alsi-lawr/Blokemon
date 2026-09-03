namespace Blokemon.App.Tests

open System
open System.Text.Json
open System.Threading
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private ApprovalFixtures =

    let succeeded (result: DomainResult<'TSuccess, 'TFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed error -> failwith $"Expected success, received {error}."

    let at = DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)

    let slug =
        match TenantSlug.Create "the-regular" with
        | DomainResult.Succeeded slug -> slug
        | DomainResult.Failed failure -> failwith $"{failure}"

    let loaded (documents: MemoryDocumentStore) account tenant =
        task {
            let! result = Approvals.load documents account tenant CancellationToken.None

            match succeeded result with
            | Some approval -> return approval.Document
            | None -> return failwith "Expected an approval record."
        }

    let seed (documents: MemoryDocumentStore) (approval: ApprovalDocument) =
        task {
            let store = documents :> IStateDocumentStore

            let! _ =
                store.Create(
                    $"approval/{approval.Account}/{approval.Tenant}",
                    JsonSerializer.Serialize(approval, json)
                )

            return ()
        }

type ApprovalTests() =

    [<Test>]
    member _.``excluding an account with no approval record should create one``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let tenant = TenantId.Mint()

            let! excluded = Approvals.exclude documents account tenant at CancellationToken.None
            let! approval = loaded documents account tenant

            succeeded excluded |> should equal ()
            approval.Status |> should equal ApprovalStatus.Pending
            approval.ExcludedAt |> should equal (Nullable at)
            approval.SchemaVersion |> should equal approvalSchemaVersion
            documents.Keys |> should equal [ $"approval/{account}/{tenant}" ]
        }

    [<Test>]
    member _.``an exclusion should dominate an approved status and readmission should clear it only``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let tenant = TenantId.Mint()

            do!
                seed
                    documents
                    { Approvals.pending account tenant with
                        Status = ApprovalStatus.Approved
                        ApprovedAt = Nullable at }

            let! _ =
                Approvals.exclude documents account tenant (at.AddHours 1.0) CancellationToken.None

            let! excluded = loaded documents account tenant
            let! _ = Approvals.readmit documents account tenant CancellationToken.None
            let! readmitted = loaded documents account tenant

            Approvals.isExcluded excluded |> should be True
            excluded.Status |> should equal ApprovalStatus.Approved
            Approvals.isExcluded readmitted |> should be False
            readmitted.Status |> should equal ApprovalStatus.Approved
            readmitted.ApprovedAt |> should equal (Nullable at)
        }

    [<Test>]
    member _.``readmitting a pending exclusion should leave it pending``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let tenant = TenantId.Mint()
            let! _ = Approvals.exclude documents account tenant at CancellationToken.None

            let! _ = Approvals.readmit documents account tenant CancellationToken.None
            let! readmitted = loaded documents account tenant

            Approvals.isExcluded readmitted |> should be False
            readmitted.Status |> should equal ApprovalStatus.Pending
        }

    [<Test>]
    member _.``an approval should be a live route only while approved with an active tenant and no exclusion``
        ()
        =
        let account = AccountId.Mint()
        let tenantId = TenantId.Mint()
        let tenant = newTenant tenantId slug "The Regular" at

        let approved =
            { Approvals.pending account tenantId with
                Status = ApprovalStatus.Approved }

        let cases =
            [ approved, tenant, true
              Approvals.pending account tenantId, tenant, false
              Approvals.excluded at approved, tenant, false
              approved,
              { tenant with
                  Status = TenantStatus.Closed },
              false
              approved,
              { tenant with
                  Status = TenantStatus.Revoked },
              false
              approved, newTenant (TenantId.Mint()) slug "Another" at, false ]

        for approval, tenant, expected in cases do
            Approvals.isLiveRoute approval tenant |> should equal expected

    [<Test>]
    member _.``a new tenant should be active with empty issuer slots under a schema version``() =
        let tenant = newTenant (TenantId.Mint()) slug "The Regular" at

        let roundTripped =
            Unchecked.nonNull (
                JsonSerializer.Deserialize<TenantDocument>(
                    JsonSerializer.Serialize(tenant, json),
                    json
                )
            )

        tenant.Status |> should equal TenantStatus.Active
        tenant.BroadcasterSubject |> should be null
        tenant.RegisteredParentOrigin |> should be null
        tenant.IntegrationTokenVerifier |> should be null
        tenant.OwnerAccount |> should be null
        tenant.SchemaVersion |> should equal tenantSchemaVersion
        roundTripped.Slug |> should equal "the-regular"
        roundTripped.Status |> should equal TenantStatus.Active
