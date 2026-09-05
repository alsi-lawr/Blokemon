using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// BLOKEMON-152 over HTTP: the authorization matrix of the lifecycle operations across the
/// three roles, the three provenances, the default tenant against a channel tenant and across
/// tenants; disable, exclusion and erasure with their scope; purge against erase; the listings'
/// and the diagnostics' content; and that no role changes what a profile can do.
/// </summary>
public sealed class LifecycleServerTests
{
    private const string OperatorRequired = "operator.required";
    private const string OwnerRequired = "owner.required";

    [Test]
    public async Task OwnerAuthority_NeedsTheBroadcasterLinkAndTheTenantsOwnIssuerOrAnApprovedFirstPartySession_AndRefusalsMutateNothing()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, alphaTenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var (beta, betaTenant) = await host.AdmitChannel(operatorToken, "beta", "Beta", "2002");
        // Beta hands the broadcaster's subject off first: the link names beta's viewer.
        var claimedViaBeta = await host.Exchange(await beta.HandoffCode("1001"), "beta");
        var claimant = await host.SessionOf(claimedViaBeta.Value!.Token);
        var claimantFirstParty = await host.IssueDirectly(
            claimant.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        var player = await host.Exchange(await alpha.HandoffCode("3003"), "alpha");
        var playerSession = await host.SessionOf(player.Value!.Token);
        var before = await host.WithStore(store => store.List(""));

        // Neither of the claimant's sessions holds authority in alpha: the beta-issued one was
        // not issued by alpha, and the first-party one has no approval for alpha.
        foreach (var token in new[] { claimedViaBeta.Value.Token, claimantFirstParty.Token })
        {
            using var client = host.Client(token);
            (await Roles(client)).OwnedTenants.ShouldBeEmpty();
            (
                await Get<ApprovalSummaryView[]>(client, $"/api/owner/{alphaTenant.Id}/approvals")
            ).Error!.Code.ShouldBe(OwnerRequired);
            (
                await Post<ExclusionView>(
                    client,
                    $"/api/owner/{alphaTenant.Id}/accounts/{playerSession.Account}/exclude"
                )
            ).Error!.Code.ShouldBe(OwnerRequired);
        }
        // A player holds none either, and an owner is not an operator.
        using (var asPlayer = host.Client(player.Value.Token))
        {
            (await Roles(asPlayer)).ShouldSatisfyAllConditions(
                static roles => roles.Operator.ShouldBeFalse(),
                static roles => roles.OwnedTenants.ShouldBeEmpty()
            );
            (
                await Post<ExclusionView>(
                    asPlayer,
                    $"/api/owner/{alphaTenant.Id}/accounts/{claimant.Account}/exclude"
                )
            ).Error!.Code.ShouldBe(OwnerRequired);
            (
                await Post<AccountLifecycleView>(
                    asPlayer,
                    $"/api/operator/accounts/{claimant.Account}/disable"
                )
            ).Error!.Code.ShouldBe(OperatorRequired);
            (
                await Get<AccountSummaryView[]>(asPlayer, "/api/operator/accounts")
            ).Error!.Code.ShouldBe(OperatorRequired);
        }
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // Alpha itself hands the broadcaster off: pending until the person approves from a
        // first-party session; the approved first-party session and alpha's own issuer
        // session then hold authority, beta's still does not.
        (await host.Exchange(await alpha.HandoffCode("1001"), "alpha")).Error!.Code.ShouldBe(
            "approval.pending"
        );
        using (var firstParty = host.Client(claimantFirstParty.Token))
        {
            (await Roles(firstParty)).OwnedTenants.ShouldBeEmpty();
            (
                await Post<ApprovalView>(firstParty, $"/api/session/approvals/{alphaTenant.Id}")
            ).Succeeded.ShouldBeTrue();
            (await Roles(firstParty)).OwnedTenants.Select(static t => t.Slug).ShouldBe(["alpha"]);
            var listed = await Get<ApprovalSummaryView[]>(
                firstParty,
                $"/api/owner/{alphaTenant.Id}/approvals"
            );
            listed
                .Value!.Select(static a => a.AccountId)
                .ShouldBe([claimant.Account.Value, playerSession.Account.Value], ignoreOrder: true);
        }
        var issuedByAlpha = await host.Exchange(await alpha.HandoffCode("1001"), "alpha");
        using (var alphaIssuer = host.Client(issuedByAlpha.Value!.Token))
        {
            (await Roles(alphaIssuer)).OwnedTenants.Select(static t => t.Slug).ShouldBe(["alpha"]);
            (
                await Get<ApprovalSummaryView[]>(
                    alphaIssuer,
                    $"/api/owner/{betaTenant.Id}/approvals"
                )
            ).Error!.Code.ShouldBe(OwnerRequired);
            // An owner cannot disable anyone.
            (
                await Post<AccountLifecycleView>(
                    alphaIssuer,
                    $"/api/operator/accounts/{playerSession.Account}/disable"
                )
            ).Error!.Code.ShouldBe(OperatorRequired);
        }
        using (var betaIssuer = host.Client(claimedViaBeta.Value.Token))
        {
            (await Roles(betaIssuer)).OwnedTenants.ShouldBeEmpty();
        }
        alpha.Dispose();
        beta.Dispose();
    }

    [Test]
    public async Task Operators_ActAnywhereButGrantOnlyFromAFirstPartySession_AndListingsCarryIdentifiersStatusAndTimestampsOnly()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, alphaTenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var player = await host.Exchange(await alpha.HandoffCode("3003", "Viewer Three"), "alpha");
        var playerSession = await host.SessionOf(player.Value!.Token);
        var operatorSession = await host.SessionOf(operatorToken);
        var operatorAsIssuer = await host.IssueDirectly(
            operatorSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            SessionProvenance.Issuer
        );
        using var asOperator = host.Client(operatorToken);
        using var asIssuerOperator = host.Client(operatorAsIssuer.Token);
        var defaultTenant = await host.DefaultTenantId();

        // Listings: the ids, status and timestamps and nothing else; no name, token or secret.
        var accounts = await asOperator.GetAsync("/api/operator/accounts");
        var accountsJson = await accounts.Content.ReadAsStringAsync();
        var tenants = await asOperator.GetAsync("/api/operator/tenants");
        var tenantsJson = await tenants.Content.ReadAsStringAsync();
        Keys(accountsJson).ShouldBe(["createdAt", "erasedAt", "id", "status"]);
        Keys(tenantsJson).ShouldBe(["createdAt", "id", "status"]);
        foreach (var json in new[] { accountsJson, tenantsJson })
        {
            json.ShouldNotContain("Viewer Three");
            json.ShouldNotContain("Operator\"");
            json.ShouldNotContain("blkm_");
            json.ShouldNotContain("3003");
        }
        var listedAccounts = JsonSerializer
            .Deserialize<ApiResponse<AccountSummaryView[]>>(accountsJson, Json)!
            .Value!;
        listedAccounts
            .Select(static a => a.Id)
            .ShouldBe(
                [operatorSession.Account.Value, playerSession.Account.Value],
                ignoreOrder: true
            );
        listedAccounts.ShouldAllBe(static a => a.Status == "Active" && a.CreatedAt != null);
        JsonSerializer
            .Deserialize<ApiResponse<TenantSummaryView[]>>(tenantsJson, Json)!
            .Value!.Select(static t => t.Id)
            .ShouldBe([defaultTenant.Value, alphaTenant.Id], ignoreOrder: true);

        // Grant: refused from the operator's Issuer session, granted from the first-party one.
        (
            await Post<AccountLifecycleView>(
                asIssuerOperator,
                $"/api/operator/accounts/{playerSession.Account}/grant-operator"
            )
        ).Error!.Code.ShouldBe("operator.provenance");
        (
            await host.WithStore(store => store.Read(accountKey(playerSession.Account)))
        )!.Json.ShouldContain("\"operator\":false");
        var granted = await Post<AccountLifecycleView>(
            asOperator,
            $"/api/operator/accounts/{playerSession.Account}/grant-operator"
        );
        granted.Value.ShouldBe(new(playerSession.Account.Value, "Active", true));
        using (var asNewOperator = host.Client(player.Value.Token))
        {
            (await Roles(asNewOperator)).Operator.ShouldBeTrue();
        }

        // The default tenant's owner is assigned; a channel's is derived and refused.
        var assigned = await asOperator.PostAsJsonAsync(
            $"/api/operator/tenants/{defaultTenant}/owner",
            new OwnerAssignmentRequest(playerSession.Account.Value)
        );
        (await assigned.Content.ReadFromJsonAsync<ApiResponse<TenantOwnerView>>())!.Value.ShouldBe(
            new(defaultTenant.Value, playerSession.Account.Value)
        );
        var onChannel = await asOperator.PostAsJsonAsync(
            $"/api/operator/tenants/{alphaTenant.Id}/owner",
            new OwnerAssignmentRequest(playerSession.Account.Value)
        );
        (
            await onChannel.Content.ReadFromJsonAsync<ApiResponse<TenantOwnerView>>()
        )!.Error!.Code.ShouldBe("tenant.owner_derived");
        // The assignment holds for the account's first-party session; the alpha channel's
        // session of the same account confers nothing on the default tenant.
        using (var asPlayer = host.Client(player.Value.Token))
        {
            (await Roles(asPlayer)).OwnedTenants.ShouldBeEmpty();
        }
        var playerFirstParty = await host.IssueDirectly(
            playerSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        using (var asPlayerFirstParty = host.Client(playerFirstParty.Token))
        {
            (await Roles(asPlayerFirstParty))
                .OwnedTenants.Select(static t => t.Slug)
                .ShouldBe(["core"]);
        }

        // Diagnostics: counts and typed reasons, and nothing of what was presented.
        (await host.Exchange("not-a-code", "alpha")).Succeeded.ShouldBeFalse();
        var diagnostics = await asOperator.GetAsync("/api/operator/diagnostics");
        var diagnosticsJson = await diagnostics.Content.ReadAsStringAsync();
        var view = JsonSerializer
            .Deserialize<ApiResponse<SignInDiagnosticsView>>(diagnosticsJson, Json)!
            .Value!;
        view.Outcomes.ShouldContain(static o => o.Code == "session.issued" && o.Count >= 1);
        view.Outcomes.ShouldContain(static o => o.Code == "handoff.refused" && o.Count >= 1);
        Keys(diagnosticsJson).ShouldBe(["code", "count", "outcomes", "since"]);
        diagnosticsJson.ShouldNotContain("not-a-code");
        diagnosticsJson.ShouldNotContain(player.Value.Token);
        diagnosticsJson.ShouldNotContain("3003");
        alpha.Dispose();
    }

    [Test]
    public async Task Disabling_RefusesEverySessionAndSignInUntilEnabled_AndKeepsTheData()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var player = await host.Exchange(await alpha.HandoffCode("3003", "Viewer Three"), "alpha");
        var playerSession = await host.SessionOf(player.Value!.Token);
        var firstParty = await host.IssueDirectly(
            playerSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        using var asOperator = host.Client(operatorToken);
        var before = await host.WithStore(store => store.List(""));

        var disabled = await Post<AccountLifecycleView>(
            asOperator,
            $"/api/operator/accounts/{playerSession.Account}/disable"
        );
        disabled.Value.ShouldBe(new(playerSession.Account.Value, "Disabled", false));
        foreach (var token in new[] { player.Value.Token, firstParty.Token })
        {
            using var client = host.Client(token);
            (
                await Get<PendingApprovalView[]>(client, "/api/session/approvals")
            ).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        }
        (await host.Exchange(await alpha.HandoffCode("3003"), "alpha")).Error!.Code.ShouldBe(
            "account.disabled"
        );
        var after = await host.WithStore(store => store.List(""));
        after
            .Where(static s => !s.Key.StartsWith("session/") && !s.Key.StartsWith("handoff/"))
            .Select(static s => s.Key)
            .ShouldBe(
                before
                    .Where(static s =>
                        !s.Key.StartsWith("session/") && !s.Key.StartsWith("handoff/")
                    )
                    .Select(static s => s.Key)
            );
        after
            .Where(static s => s.Key.StartsWith("session/"))
            .Select(static s => s.Key)
            .ShouldBe([$"session/{(await host.SessionOf(operatorToken)).Id}"]);

        var enabled = await Post<AccountLifecycleView>(
            asOperator,
            $"/api/operator/accounts/{playerSession.Account}/enable"
        );
        enabled.Value.ShouldBe(new(playerSession.Account.Value, "Active", false));
        var back = await host.Exchange(await alpha.HandoffCode("3003"), "alpha");
        back.Value!.DisplayName.ShouldBe("Viewer Three");
        (await host.SessionOf(back.Value.Token)).Account.ShouldBe(playerSession.Account);
        alpha.Dispose();
    }

    [Test]
    public async Task Exclusion_RefusesTheTenantsSessionsAndHandoffsOnly_WithOrWithoutAPriorRecord_AndReadmissionTouchesNothingElse()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, alphaTenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var (beta, betaTenant) = await host.AdmitChannel(operatorToken, "beta", "Beta", "2002");
        var owner = await host.Exchange(await alpha.HandoffCode("1001", "Alpha Owner"), "alpha");
        var player = await host.Exchange(await alpha.HandoffCode("3003", "Viewer Three"), "alpha");
        var playerSession = await host.SessionOf(player.Value!.Token);
        var playerFirstParty = await host.IssueDirectly(
            playerSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        var playerAlphaFirstParty = await host.WithStore(store =>
            Sessions.issue(
                store,
                playerSession.Account,
                Tenants.idOf(
                    JsonSerializer.Deserialize<TenantDocument>(
                        store.Read($"tenant/{alphaTenant.Id}").Result!.Json,
                        json
                    )!
                ),
                SessionProvenance.FirstParty,
                DateTimeOffset.UtcNow,
                TimeSpan.FromHours(1),
                CancellationToken.None
            )
        );
        // The player is approved for beta as well.
        (await host.Exchange(await beta.HandoffCode("3003"), "beta")).Error!.Code.ShouldBe(
            "approval.pending"
        );
        using (var approving = host.Client(playerFirstParty.Token))
        {
            (
                await Post<ApprovalView>(approving, $"/api/session/approvals/{betaTenant.Id}")
            ).Succeeded.ShouldBeTrue();
        }
        var viaBeta = await host.Exchange(await beta.HandoffCode("3003"), "beta");
        var never = await host.SignIn("never-through-alpha", "Never");
        var onlyBeta = await host.Exchange(await beta.HandoffCode("4004", "Only Beta"), "beta");
        using var asOwner = host.Client(owner.Value!.Token);
        var before = await host.WithStore(store => store.List(""));

        // Exclude the player (an Approved record exists) and the never-seen account (none).
        (
            await Post<ExclusionView>(
                asOwner,
                $"/api/owner/{alphaTenant.Id}/accounts/{playerSession.Account}/exclude"
            )
        ).Value.ShouldBe(new(playerSession.Account.Value, true));
        (
            await Post<ExclusionView>(
                asOwner,
                $"/api/owner/{alphaTenant.Id}/accounts/{never.Session.Account}/exclude"
            )
        ).Value.ShouldBe(new(never.Session.Account.Value, true));

        // Alpha's sessions for the player are refused, whoever issued them; the default
        // tenant's and beta's still answer; alpha's hand-off is refused, beta's is not.
        foreach (var token in new[] { player.Value.Token, playerAlphaFirstParty.Token })
        {
            using var client = host.Client(token);
            (
                await Get<PendingApprovalView[]>(client, "/api/session/approvals")
            ).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        }
        foreach (var token in new[] { playerFirstParty.Token, viaBeta.Value!.Token, never.Token })
        {
            using var client = host.Client(token);
            (
                await Get<PendingApprovalView[]>(client, "/api/session/approvals")
            ).Succeeded.ShouldBeTrue();
        }
        (await host.Exchange(await alpha.HandoffCode("3003"), "alpha")).Error!.Code.ShouldBe(
            "tenant.excluded"
        );
        (await host.Exchange(await beta.HandoffCode("3003"), "beta")).Succeeded.ShouldBeTrue();
        var playerApproval = await Approval(host, playerSession.Account, alphaTenant.Id);
        playerApproval.Status.ShouldBe(ApprovalStatus.Approved);
        playerApproval.ExcludedAt.ShouldNotBeNull();
        var neverApproval = await Approval(host, never.Session.Account, alphaTenant.Id);
        neverApproval.Status.ShouldBe(ApprovalStatus.Pending);
        neverApproval.ExcludedAt.ShouldNotBeNull();

        // The owner's listing: alpha's records only, as account ids, status and timestamps.
        var listed = await Get<ApprovalSummaryView[]>(
            asOwner,
            $"/api/owner/{alphaTenant.Id}/approvals"
        );
        listed
            .Value!.Select(static a => a.AccountId)
            .ShouldBe(
                [
                    (await host.SessionOf(owner.Value.Token)).Account.Value,
                    playerSession.Account.Value,
                    never.Session.Account.Value,
                ],
                ignoreOrder: true
            );
        var onlyBetaAccount = (await host.SessionOf(onlyBeta.Value!.Token)).Account.Value;
        listed.Value!.ShouldNotContain(a => a.AccountId == onlyBetaAccount);
        listed
            .Value!.Single(a => a.AccountId == playerSession.Account.Value)
            .ShouldSatisfyAllConditions(
                static a => a.Status.ShouldBe("Approved"),
                static a => a.ExcludedAt.ShouldNotBeNull(),
                static a => a.ApprovedAt.ShouldNotBeNull()
            );

        // Readmission clears the exclusion and nothing else; beyond the two approval records
        // and the sessions, every document is exactly as before.
        (
            await Post<ExclusionView>(
                asOwner,
                $"/api/owner/{alphaTenant.Id}/accounts/{playerSession.Account}/readmit"
            )
        ).Value.ShouldBe(new(playerSession.Account.Value, false));
        (
            await Post<ExclusionView>(
                asOwner,
                $"/api/owner/{alphaTenant.Id}/accounts/{never.Session.Account}/readmit"
            )
        ).Value.ShouldBe(new(never.Session.Account.Value, false));
        (await Approval(host, playerSession.Account, alphaTenant.Id)).ShouldBe(
            Approvals.readmitted(playerApproval)
        );
        (await Approval(host, never.Session.Account, alphaTenant.Id)).ShouldBe(
            Approvals.readmitted(neverApproval)
        );
        (await host.Exchange(await alpha.HandoffCode("3003"), "alpha")).Succeeded.ShouldBeTrue();
        var after = await host.WithStore(store => store.List(""));
        static bool Stable(DocumentSummary s) =>
            !s.Key.StartsWith("session/")
            && !s.Key.StartsWith("handoff/")
            && !s.Key.StartsWith("approval/");
        after.Where(Stable).ShouldBe(before.Where(Stable));
        alpha.Dispose();
        beta.Dispose();
    }

    [Test]
    public async Task SelfErasure_IsPermittedFromFirstPartyAndDefaultIssuerSessions_RefusedFromChannelAndRecoverySessions_AndLeavesTheTombstone()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var (core, _) = await host.AdmitChannel(operatorToken, "core", "Blokemon", "9009");
        var viaAlpha = await host.Exchange(
            await alpha.HandoffCode("3003", "Viewer Three"),
            "alpha"
        );
        var person = await host.SessionOf(viaAlpha.Value!.Token);
        var firstParty = await host.IssueDirectly(
            person.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        var recovery = await host.IssueDirectly(
            person.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            SessionProvenance.Recovery
        );
        await host.WithStore(store =>
            store.Create(Credentials.key(person.Account, "cred"), "{\"id\":\"cred\"}")
        );
        await host.WithStore(store => store.Create(RecoveryCodes.key(person.Account), "{}"));
        var before = await host.WithStore(store => store.List(""));

        // A channel's Issuer session and a Recovery session are refused, mutating nothing.
        using (var asChannelSession = host.Client(viaAlpha.Value.Token))
        {
            (
                await Post<AccountErasedView>(asChannelSession, "/api/session/erase")
            ).Error!.Code.ShouldBe("erase.provenance");
        }
        using (var asRecovery = host.Client(recovery.Token))
        {
            (await Post<AccountErasedView>(asRecovery, "/api/session/erase")).Error!.Code.ShouldBe(
                SessionFailures.RecoveryCode
            );
        }
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // The first-party session erases: only the tombstone remains of the account.
        using (var asFirstParty = host.Client(firstParty.Token))
        {
            var erased = await Post<AccountErasedView>(asFirstParty, "/api/session/erase");
            erased.Value!.Repeated.ShouldBeFalse();
            (
                await Post<AccountErasedView>(asFirstParty, "/api/session/erase")
            ).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
            var remaining = (await host.WithStore(store => store.List("")))
                .Select(static s => s.Key)
                .Where(key => key.Contains(person.Account.Value) || key == "link/twitch/3003")
                .ToList();
            remaining.ShouldBe([accountKey(person.Account)]);
            var tombstone = JsonNode
                .Parse(
                    (await host.WithStore(store => store.Read(accountKey(person.Account))))!.Json
                )!
                .AsObject();
            tombstone.Select(static p => p.Key).ShouldBe(["id", "erasedAt"]);
            tombstone["id"]!.GetValue<string>().ShouldBe(person.Account.Value);
            tombstone["erasedAt"]!.GetValue<DateTimeOffset>().ShouldBe(erased.Value.ErasedAt);
            // The listing shows the erasure through the projection.
            (await host.WithStore(store => store.List(accountKey(person.Account))))
                .Single()
                .Projection.ShouldBe(
                    new DocumentProjection.Lifecycle(null, null, erased.Value.ErasedAt)
                );
            // A repeated erase on behalf is the terminal no-op; the id is never reissued.
            using var asOperator = host.Client(operatorToken);
            var again = await Post<AccountErasedView>(
                asOperator,
                $"/api/operator/accounts/{person.Account}/erase"
            );
            again.Value.ShouldBe(new(erased.Value.ErasedAt, true));
            var reborn = await host.Exchange(
                await alpha.HandoffCode("3003", "Viewer Three"),
                "alpha"
            );
            (await host.SessionOf(reborn.Value!.Token)).Account.ShouldNotBe(person.Account);
        }

        // The default tenant's Issuer session (the core sign-in) may erase.
        var viaCore = await host.Exchange(await core.HandoffCode("5005", "Core Person"), null);
        var corePerson = await host.SessionOf(viaCore.Value!.Token);
        using (var asCoreIssuer = host.Client(viaCore.Value.Token))
        {
            (
                await Post<AccountErasedView>(asCoreIssuer, "/api/session/erase")
            ).Value!.Repeated.ShouldBeFalse();
        }
        (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .Where(key => key.Contains(corePerson.Account.Value) || key == "link/twitch/5005")
            .ShouldBe([accountKey(corePerson.Account)]);
        alpha.Dispose();
        core.Dispose();
    }

    [Test]
    public async Task Purge_DeletesOnlyTheProfileDocumentsWhereEraseRemovesEverything()
    {
        await using var host = SessionHost.Create();
        var signedIn = await host.SignIn("purger", "Purger");
        var account = signedIn.Session.Account;
        await host.WithStore(store =>
            store.Create(Credentials.key(account, "cred"), "{\"id\":\"cred\"}")
        );
        using var client = host.Client(signedIn.Token);
        (
            await client.PostAsJsonAsync(
                "/api/starter-decks/claim",
                new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom")
            )
        ).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/purge", new { })).EnsureSuccessStatusCode();
        var afterPurge = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .Where(static key => !key.StartsWith("session/") && !key.StartsWith("tenant/"))
            .ToList();

        (await Post<AccountErasedView>(client, "/api/session/erase")).Succeeded.ShouldBeTrue();
        var afterErase = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .Where(static key => !key.StartsWith("session/") && !key.StartsWith("tenant/"))
            .ToList();

        afterPurge.ShouldBe(
            [accountKey(account), Credentials.key(account, "cred"), "link/test/purger"],
            ignoreOrder: true
        );
        afterErase.ShouldBe([accountKey(account)]);
    }

    [Test]
    public async Task NoRole_ChangesWhatAProfileCanDo()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var owner = await host.Exchange(await alpha.HandoffCode("1001", "Same Name"), "alpha");
        var player = await host.Exchange(await alpha.HandoffCode("3003", "Same Name"), "alpha");
        var operatorAccount = (await host.SessionOf(operatorToken)).Account;
        var ownerAccount = (await host.SessionOf(owner.Value!.Token)).Account;
        var playerAccount = (await host.SessionOf(player.Value!.Token)).Account;

        var documents = new List<string>();
        var views = new List<(int? Packs, int Cards, int Decks)>();
        foreach (
            var (token, account) in new[]
            {
                (operatorToken, operatorAccount),
                (owner.Value.Token, ownerAccount),
                (player.Value.Token, playerAccount),
            }
        )
        {
            using var client = host.Client(token);
            (
                await client.PostAsJsonAsync(
                    "/api/starter-decks/claim",
                    new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom")
                )
            ).EnsureSuccessStatusCode();
            var opened = await client.PostAsJsonAsync(
                "/api/packs/open",
                new OpenPackRequest(Guid.NewGuid())
            );
            var state = (await opened.Content.ReadFromJsonAsync<ApiResponse<ApplicationView>>())!;
            state.Succeeded.ShouldBeTrue(state.Error?.Message);
            views.Add(
                (
                    state.Value!.Profile!.RemainingPacks,
                    state.Value.Cards.Sum(static card => card.OwnedQuantity),
                    state.Value.Decks.Length
                )
            );
            var profile = JsonNode
                .Parse((await host.WithStore(store => store.Read($"a/{account}/profile")))!.Json)!
                .AsObject();
            // Identity and the pack's random draw differ per account; capability does not.
            profile.Remove("creationCommandId");
            var inner = profile["profile"]!.AsObject();
            inner.Remove("profileId");
            inner.Remove("displayName");
            inner.Remove("collectibleOwnership");
            inner.Remove("packReceipts");
            inner.Remove("savedDecks");
            inner.Remove("starterDeckClaims");
            documents.Add(profile.ToJsonString());
        }

        documents.Distinct().ShouldHaveSingleItem();
        views.Select(static v => (v.Packs, v.Decks)).Distinct().ShouldHaveSingleItem();
        // Sixty starter cards plus one pack for each of them.
        views.Select(static v => v.Cards).Distinct().ShouldHaveSingleItem();
        alpha.Dispose();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<SessionRolesView> Roles(HttpClient client) =>
        (await Get<SessionRolesView>(client, "/api/session/roles")).Value!;

    private static async Task<ApiResponse<T>> Get<T>(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<ApiResponse<T>>(path))!;

    private static async Task<ApiResponse<T>> Post<T>(HttpClient client, string path)
    {
        using var response = await client.PostAsJsonAsync(path, new { });
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!;
    }

    private static async Task<ApprovalDocument> Approval(
        SessionHost host,
        AccountId account,
        string tenantId
    ) =>
        JsonSerializer.Deserialize<ApprovalDocument>(
            (await host.WithStore(store => store.Read($"approval/{account}/{tenantId}")))!.Json,
            json
        )!;

    /// <summary>Every property name inside the envelope's value, sorted, once.</summary>
    private static string[] Keys(string envelope)
    {
        var value = JsonNode.Parse(envelope)!["value"]!;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Walk(value, names);
        return [.. names];

        static void Walk(JsonNode? node, SortedSet<string> names)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, child) in obj)
                    {
                        names.Add(name);
                        Walk(child, names);
                    }
                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        Walk(child, names);
                    }
                    break;
            }
        }
    }
}
