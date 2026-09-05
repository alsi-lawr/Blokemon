namespace Blokemon.App.Tests

open System
open System.Text.Json
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

type AccountLifecycleTests() =

    let provider = "example"

    let signIn documents subject tenant =
        SignInCompletion.complete
            (services documents)
            (identity provider subject "Player" SessionProvenance.FirstParty)
            tenant
            now
            Unchecked.defaultof<_>

    let validate documents token =
        Sessions.validate documents token (now.AddMinutes 1.0) Unchecked.defaultof<_>

    let snapshotWithout (documents: MemoryDocumentStore) (prefix: string) =
        documents.Snapshot
        |> Map.filter (fun key _ -> not (key.StartsWith(prefix, StringComparison.Ordinal)))

    let create (documents: MemoryDocumentStore) (key: string) (value: 'T) =
        task {
            let! _ =
                (documents :> IStateDocumentStore)
                    .Create(key, JsonSerializer.Serialize(value, json))

            return ()
        }

    let session account tenant provenance : Session =
        { Id = Guid.NewGuid().ToString "D"
          Account = account
          Tenant = tenant
          Provenance = provenance
          ExpiresAt = now + lifetime }

    [<Test>]
    member _.``disabling should revoke the account's sessions keep its data and refuse sign-in until enabled``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! issued = signIn documents "person" tenant
            let issued = succeeded issued
            let account = issued.Session.Account
            let before = snapshotWithout documents "session/"

            let! disabled =
                AccountLifecycle.disable documents documents account Unchecked.defaultof<_>

            let sessionsAfterDisable = keysUnder documents "session/"
            let! presented = validate documents issued.Token
            let! refused = signIn documents "person" tenant
            let! again = AccountLifecycle.disable documents documents account Unchecked.defaultof<_>
            let! enabled = AccountLifecycle.enable documents account Unchecked.defaultof<_>
            let! signedInAgain = signIn documents "person" tenant

            (succeeded disabled).Status |> should equal AccountStatus.Disabled
            presented |> should equal SessionValidation.Required
            sessionsAfterDisable |> should equal List.empty<string>
            failed refused |> should equal SignInFailure.AccountDisabled
            (succeeded again).Status |> should equal AccountStatus.Disabled
            (succeeded enabled).Status |> should equal AccountStatus.Active
            (succeeded signedInAgain).Session.Account |> should equal account
            // The account record went Disabled and back; nothing else of the account's moved.
            snapshotWithout documents "session/"
            |> Map.remove (accountKey account)
            |> should equal (before |> Map.remove (accountKey account))
        }

    [<Test>]
    member _.``erasing should leave only the tombstone and repeat as a terminal no-op``() =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! issued = signIn documents "person" tenant
            let issued = succeeded issued
            let account = issued.Session.Account
            let! other = signIn documents "someone-else" tenant
            let other = succeeded other
            let keys = PlayerDocumentKeys.forAccount account
            do! create documents (Credentials.key account "cred") {| id = "cred" |}
            do! create documents (RecoveryCodes.key account) {| codes = [] |}
            do! create documents (approvalKey account tenant) (Approvals.pending account tenant)
            do! create documents $"{AccountErasure.backupPrefix keys}1/abc" {| backup = true |}

            let! _ =
                OperatorBootstrap.redeem
                    documents
                    "the-bootstrap-code!"
                    issued.Session
                    "the-bootstrap-code!"
                    now
                    Unchecked.defaultof<_>
            // The account's own keys name it, except its link, whose key names the subject.
            let own (key: string) =
                key.Contains account.Value || key = $"link/{provider}/person"

            let others =
                snapshotWithout documents "session/" |> Map.filter (fun key _ -> not (own key))

            let! erased =
                AccountErasure.erase documents documents account now Unchecked.defaultof<_>

            let! presented = validate documents issued.Token
            let! stillOther = validate documents other.Token

            let! repeated =
                AccountErasure.erase
                    documents
                    documents
                    account
                    (now.AddHours 1.0)
                    Unchecked.defaultof<_>

            let! record = Accounts.load documents account Unchecked.defaultof<_>
            let remaining = documents.Keys |> List.filter own

            let othersAfter =
                snapshotWithout documents "session/" |> Map.filter (fun key _ -> not (own key))

            let! resigned = signIn documents "person" tenant

            let! asErased =
                SignInCompletion.completeAs
                    (services documents)
                    (identity provider "person-again" "Player" SessionProvenance.FirstParty)
                    account
                    tenant
                    now
                    Unchecked.defaultof<_>

            succeeded erased |> should equal { ErasedAt = now; Repeated = false }
            presented |> should equal SessionValidation.Required
            stillOther.IsValid |> should be True
            succeeded repeated |> should equal { ErasedAt = now; Repeated = true }

            record
            |> should equal (AccountRecord.Erased { Id = account.Value; ErasedAt = now })
            // The tombstone is exactly its two fields.
            let! stored = (documents :> IStateDocumentStore).Read(accountKey account)

            let properties =
                JsonDocument.Parse((Unchecked.nonNull stored).Json).RootElement.EnumerateObject()
                |> Seq.map (fun property -> property.Name)
                |> List.ofSeq

            properties |> should equal [ "id"; "erasedAt" ]
            // Nothing of the account remains but the tombstone, and nothing of anyone else moved.
            remaining |> should equal [ accountKey account ]
            othersAfter |> should equal others
            // The subject signs in again as a new account; the erased id is never reissued.
            (succeeded resigned).Session.Account |> should not' (equal account)
            failed asErased |> should equal SignInFailure.AccountErased
        }

    [<Test>]
    member _.``purging should delete only the three profile documents where erasing removes everything``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! issued = signIn documents "person" tenant
            let account = (succeeded issued).Session.Account
            let keys = PlayerDocumentKeys.forAccount account
            do! create documents (Credentials.key account "cred") {| id = "cred" |}
            do! create documents keys.Match {| saved = true |}
            let principal = ApplicationPrincipal.Account(account, tenant)

            let application =
                LocalApplicationService(
                    catalogue.Value,
                    documents,
                    principal,
                    LocalMatchService(catalogue.Value, documents, keys),
                    EconomyRules.Unlimited,
                    ProfileAuthorityPolicy.Preserve
                )

            let! purged = application.PurgeData(Unchecked.defaultof<_>)

            let afterPurge =
                documents.Keys |> List.filter (fun key -> not (key.StartsWith "session/"))

            let! _ = AccountErasure.erase documents documents account now Unchecked.defaultof<_>

            let afterErase =
                documents.Keys |> List.filter (fun key -> not (key.StartsWith "session/"))

            purged.Succeeded |> should be True

            afterPurge
            |> should
                equal
                (List.sort
                    [ accountKey account
                      Credentials.key account "cred"
                      $"link/{provider}/person" ])

            afterErase |> should equal [ accountKey account ]
        }

    [<Test>]
    member _.``self erasure should be permitted from a first party or default issuer session and refused from a channel issuer or recovery session``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let! core = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>
            let channel = TenantId.Mint()

            do!
                create
                    documents
                    (tenantKey channel)
                    (newTenant
                        channel
                        (succeeded (TenantSlug.Create "the-regular"))
                        "The Regular"
                        now)

            let account = AccountId.Mint()

            let may provenance tenant =
                AccountErasure.maySelfErase
                    documents
                    (session account tenant provenance)
                    Unchecked.defaultof<_>

            let! firstParty = may SessionProvenance.FirstParty channel
            let! coreIssuer = may SessionProvenance.Issuer (Tenants.idOf core)
            let! channelIssuer = may SessionProvenance.Issuer channel
            let! recovery = may SessionProvenance.Recovery (Tenants.idOf core)

            firstParty |> should be True
            coreIssuer |> should be True
            channelIssuer |> should be False
            recovery |> should be False
        }

    [<Test>]
    member _.``granting operator should set only the flag and refuse a tombstone``() =
        task {
            let documents = MemoryDocumentStore()
            let tenant = TenantId.Mint()
            let! issued = signIn documents "person" tenant
            let account = (succeeded issued).Session.Account
            let! before = readAccount documents account

            let! granted = AccountLifecycle.grantOperator documents account Unchecked.defaultof<_>
            let! isOperator = Accounts.isOperator documents account Unchecked.defaultof<_>
            let! _ = AccountErasure.erase documents documents account now Unchecked.defaultof<_>

            let! afterErase =
                AccountLifecycle.grantOperator documents account Unchecked.defaultof<_>

            let! enableErased = AccountLifecycle.enable documents account Unchecked.defaultof<_>

            let! unknown =
                AccountLifecycle.disable
                    documents
                    documents
                    (AccountId.Mint())
                    Unchecked.defaultof<_>

            succeeded granted |> should equal { before with Operator = true }
            isOperator |> should be True
            failed afterErase |> should equal LifecycleFailure.Erased
            failed enableErased |> should equal LifecycleFailure.Erased
            failed unknown |> should equal LifecycleFailure.NotFound
        }
