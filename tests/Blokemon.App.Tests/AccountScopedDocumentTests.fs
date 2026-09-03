namespace Blokemon.App.Tests

open System
open System.IO
open Blokemon.App
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private ApplicationFixtures =

    let catalogue =
        lazy
            (BlokemonCatalogue.FromBootstrapJson(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
                )
            ))

    let tenant = TenantId.Mint()

    let accountPrincipal () =
        ApplicationPrincipal.Account(AccountId.Mint(), tenant)

    let keysOf principal =
        PlayerDocumentKeys.ofPrincipal principal

    let application (principal: ApplicationPrincipal) (documents: IStateDocumentStore) =
        LocalApplicationService(
            catalogue.Value,
            documents,
            principal,
            LocalMatchService(catalogue.Value, documents, keysOf principal),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        )

    let browserLocalApplication (documents: IStateDocumentStore) =
        LocalApplicationService(
            catalogue.Value,
            documents,
            LocalMatchService(catalogue.Value, documents),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        )

    let value (response: ApiResponse<'T>) : 'T =
        if response.Succeeded then
            Unchecked.nonNull response.Value
        else
            failwith $"Expected success, received {response.Error}."

    let error (response: ApiResponse<'T>) : ApiError =
        if response.Succeeded then
            failwith "Expected an API failure."
        else
            Unchecked.nonNull response.Error

    let profile (view: ApplicationView) =
        match view.Profile with
        | null -> failwith "Expected a profile."
        | profile -> profile

    /// The store's own bound; the server store declares it and refuses anything longer.
    let keyBound = 160

type AccountScopedDocumentTests() =

    [<Test>]
    member _.``documents of two accounts should be independent``() =
        task {
            let documents = MemoryDocumentStore()
            let alpha = accountPrincipal ()
            let beta = accountPrincipal ()
            let alphaApplication = application alpha documents
            let betaApplication = application beta documents

            let! _ = alphaApplication.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Alpha"))
            let! _ = betaApplication.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Beta"))
            let betaBefore = documents.Snapshot[(keysOf beta).Profile]
            let! opened = alphaApplication.OpenPack(OpenPackRequest(Guid.NewGuid()))
            let! alphaState = alphaApplication.State()
            let! betaState = betaApplication.State()

            (profile (value opened)).DisplayName |> should equal "Alpha"
            (profile (value alphaState)).DisplayName |> should equal "Alpha"
            (value alphaState).LastPack |> should not' (be null)
            (profile (value betaState)).DisplayName |> should equal "Beta"
            (value betaState).LastPack |> should be null
            documents.Snapshot[(keysOf beta).Profile] |> should equal betaBefore

            documents.Keys
            |> should equal (List.sort [ (keysOf alpha).Profile; (keysOf beta).Profile ])
        }

    [<Test>]
    member _.``one profile per account should replay its creation command and refuse a second``() =
        task {
            let documents = MemoryDocumentStore()
            let principal = accountPrincipal ()
            let service = application principal documents
            let command = Guid.NewGuid()

            let! created = service.CreateProfile(CreateProfileRequest(command, "Alpha"))
            let! replayed = service.CreateProfile(CreateProfileRequest(command, "Alpha"))
            let! refused = service.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Other"))

            (profile (value replayed)).Id |> should equal (profile (value created)).Id
            (error refused).Code |> should equal "profile.exists"
            (error refused).Message |> should equal "This account already has a profile."
            documents.Keys |> should equal [ (keysOf principal).Profile ]
        }

    [<Test>]
    member _.``purging an account should delete only that account's three documents``() =
        task {
            let documents = MemoryDocumentStore()
            let store = documents :> IStateDocumentStore
            let alpha = accountPrincipal ()
            let beta = accountPrincipal ()
            let alphaKeys = keysOf alpha
            let betaKeys = keysOf beta
            let alphaApplication = application alpha documents
            let betaApplication = application beta documents
            let! _ = alphaApplication.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Alpha"))
            let! _ = betaApplication.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Beta"))
            let! _ = store.Create(alphaKeys.Match, "{}")
            let! _ = store.Create(alphaKeys.MatchHistory, "{}")
            let! _ = store.Create(betaKeys.Match, "{}")
            let! _ = store.Create("account/alpha", "{}")
            let! _ = store.Create("link/example/alpha-subject", "{}")

            let! purged = alphaApplication.PurgeData()

            (value purged).Profile |> should be null

            documents.Keys
            |> should
                equal
                (List.sort
                    [ betaKeys.Profile
                      betaKeys.Match
                      "account/alpha"
                      "link/example/alpha-subject" ])
        }

    [<Test>]
    member _.``the browser local host should keep its literal keys and no account``() =
        task {
            let documents = MemoryDocumentStore()
            let service = browserLocalApplication documents

            let! refusedTwice =
                task {
                    let! _ = service.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Local"))
                    return! service.CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Again"))
                }

            documents.Keys |> should equal [ "profile" ]

            (error refusedTwice).Message
            |> should equal "This machine already has a local profile."
        }

    [<Test>]
    member _.``an application and a match service for different players should be refused``() =
        let documents = MemoryDocumentStore()

        (fun () ->
            LocalApplicationService(
                catalogue.Value,
                documents,
                accountPrincipal (),
                LocalMatchService(catalogue.Value, documents),
                EconomyRules.Unlimited,
                ProfileAuthorityPolicy.Preserve
            )
            |> ignore)
        |> should throw typeof<ArgumentException>

    [<Test>]
    member _.``no provider subject should appear in an account scoped key``() =
        task {
            let documents = MemoryDocumentStore()
            let principal = accountPrincipal ()
            let subject = "subject-12345"

            let account =
                match principal with
                | ApplicationPrincipal.Account(account, _) -> account
                | ApplicationPrincipal.BrowserLocal -> failwith "Expected an account."

            let link =
                { Provider =
                    match IdentityProviderName.Create "example" with
                    | DomainResult.Succeeded provider -> provider
                    | DomainResult.Failed failure -> failwith $"{failure}"
                  Subject =
                    match ExternalSubject.Create subject with
                    | DomainResult.Succeeded subject -> subject
                    | DomainResult.Failed failure -> failwith $"{failure}"
                  Account = account }

            let! _ =
                IdentityLinks.create
                    documents
                    link
                    DateTimeOffset.UnixEpoch
                    Threading.CancellationToken.None

            let! _ =
                (application principal documents)
                    .CreateProfile(CreateProfileRequest(Guid.NewGuid(), "Alpha"))

            let keysNamingTheSubject =
                documents.Keys |> List.filter (fun key -> key.Contains subject)

            keysNamingTheSubject |> should equal [ $"link/example/{subject}" ]

            documents.Keys
            |> List.filter (fun key -> key.StartsWith "a/")
            |> should equal [ $"a/{account}/profile" ]
        }

    [<Test>]
    member _.``every key this ticket defines should be within the widened bound``() =
        let account = AccountId.Mint()
        let tenant = TenantId.Mint()
        let keys = PlayerDocumentKeys.forAccount account

        let provider =
            match IdentityProviderName.Create(String('p', IdentityProviderName.MaximumLength)) with
            | DomainResult.Succeeded provider -> provider
            | DomainResult.Failed failure -> failwith $"{failure}"

        let subject =
            match ExternalSubject.Create(String('s', ExternalSubject.MaximumLength)) with
            | DomainResult.Succeeded subject -> subject
            | DomainResult.Failed failure -> failwith $"{failure}"

        (approvalKey account tenant).Length |> should equal 82

        for key in
            [ keys.Profile
              keys.Match
              keys.MatchHistory
              tenantKey tenant
              accountKey account
              linkKey provider subject
              approvalKey account tenant ] do
            key.Length |> should be (lessThanOrEqualTo keyBound)
