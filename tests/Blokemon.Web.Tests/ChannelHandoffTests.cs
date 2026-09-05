using System.Net.Http.Json;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// The hand-off and its exchange, issuer approval and adoption, exclusion, the continuation,
/// and the erasure relay, over HTTP with the fake channel; every refused case mutates nothing.
/// </summary>
public sealed class ChannelHandoffTests
{
    [Test]
    public async Task AHandoffForANewSubject_CreatesTheAccountProfileAndLinkAndIssuesAnIssuerSessionForTheCodesTenant()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(
            operatorToken,
            "the-regular",
            "The Regular"
        );
        var before = await host.WithStore(store => store.List(""));

        var handoff = await channel.Handoff("1234", "viewer_one", "Viewer One");
        handoff.Succeeded.ShouldBeTrue(handoff.Error?.Message);
        handoff.Value!.ExpiresAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(61));
        var stored = (await host.WithStore(store => store.List("handoff/"))).ShouldHaveSingleItem();
        var record = (await host.WithStore(store => store.Read(stored.Key)))!;
        record.Json.ShouldNotContain(handoff.Value.Code.Split('.')[1]);
        record.Json.ShouldContain("\"kind\":\"Channel\"");

        var signedIn = await host.Exchange(handoff.Value.Code, "the-regular");
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        signedIn.Value!.DisplayName.ShouldBe("Viewer One");
        signedIn.Value.Recovery.ShouldBeFalse();
        var session = await host.SessionOf(signedIn.Value.Token);
        session.Provenance.ShouldBe(SessionProvenance.Issuer);
        session.Tenant.Value.ShouldBe(tenant.Id);
        var keys = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ToList();
        keys.ShouldContain($"account/{session.Account}");
        keys.ShouldContain("link/twitch/1234");
        keys.ShouldContain($"a/{session.Account}/profile");
        keys.ShouldContain($"approval/{session.Account}/{tenant.Id}");
        keys.ShouldNotContain(stored.Key);
        var approval = (
            await host.WithStore(store => store.Read($"approval/{session.Account}/{tenant.Id}"))
        )!;
        JsonSerializer
            .Deserialize<ApprovalDocument>(approval.Json, json)!
            .Status.ShouldBe(ApprovalStatus.Approved);
        // The same viewer again is the same account.
        var again = await host.Exchange(await channel.HandoffCode("1234"), "the-regular");
        (await host.SessionOf(again.Value!.Token)).Account.ShouldBe(session.Account);
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(
            before.Count(static s => s.Key.StartsWith("account/", StringComparison.Ordinal)) + 1
        );
    }

    [Test]
    public async Task ChannelCalls_RefuseEveryBadTokenAndBadHandoffAndMutateNothing()
    {
        await using var host = ChannelHosting.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.HandoffRateLimitPerMinuteKey, "3")
        );
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "strict", "Strict");
        var (closedChannel, closedTenant) = await host.AdmitChannel(
            operatorToken,
            "closed",
            "Closed"
        );
        var (revokedChannel, revokedTenant) = await host.AdmitChannel(
            operatorToken,
            "revoked",
            "Revoked"
        );
        await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{closedTenant.Id}/close"
        );
        await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{revokedTenant.Id}/revoke"
        );
        using var noHeader = new FakeChannel(host, null);
        using var unknown = new FakeChannel(host, $"blkm_{tenant.Id}_not-the-secret");
        using var malformed = new FakeChannel(host, "not-a-token");
        var before = await host.WithStore(store => store.List(""));

        foreach (
            var (name, caller, code) in new (string, FakeChannel, string)[]
            {
                ("no Authorization header", noHeader, "channel.token_required"),
                ("malformed token", malformed, "channel.token_required"),
                ("unknown token", unknown, "channel.token_unknown"),
                ("closed tenant", closedChannel, "tenant.closed"),
                ("revoked tenant", revokedChannel, "tenant.revoked"),
            }
        )
        {
            (await caller.Self()).Error!.Code.ShouldBe(code, $"{name} self");
            (await caller.Handoff("1234")).Error!.Code.ShouldBe(code, $"{name} handoff");
            (await caller.Erasure("1234")).Error!.Code.ShouldBe(code, $"{name} erasure");
            (await caller.Close()).Error!.Code.ShouldBe(code, $"{name} close");
        }
        (await channel.Handoff(null)).Error!.Code.ShouldBe("handoff.subject");
        (await channel.Handoff("  ")).Error!.Code.ShouldBe("handoff.subject");
        (await channel.Handoff("viewer-one")).Error!.Code.ShouldBe("handoff.subject");
        (await channel.Handoff(new string('9', 65))).Error!.Code.ShouldBe("handoff.subject");
        // A body that is not the request's shape never reaches the handler: the host answers
        // a client error rather than the envelope, and nothing is minted.
        (
            await channel.PostRaw("/api/tenant/handoff", "not json")
        ).IsSuccessStatusCode.ShouldBeFalse();
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // The rate limit counts what was minted, not what was refused: three mints, then no.
        for (var mint = 0; mint < 3; mint++)
        {
            (await channel.Handoff("1234")).Succeeded.ShouldBeTrue($"mint {mint}");
        }
        var limited = await channel.Handoff("1234");
        limited.Error!.Code.ShouldBe("handoff.rate_limited");
        (await host.WithStore(store => store.List("handoff/"))).Count.ShouldBe(3);
    }

    [Test]
    public async Task Exchange_RefusesExpiredReusedMalformedWrongKindAndOtherTenantCodesAndMutatesNothing()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "one", "One");
        var (_, other) = await host.AdmitChannel(operatorToken, "two", "Two");
        var subject = (
            (DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded)
                ExternalSubject.Create("4321")
        ).Value;
        var live = await channel.HandoffCode("4321");
        var signedIn = await host.SignIn("continuer", "Continuer");
        using var client = host.Client(signedIn.Token);
        using var continued = await client.PostAsJsonAsync("/api/session/continue", new { });
        var continuation = (
            await continued.Content.ReadFromJsonAsync<ApiResponse<ContinuationView>>()
        )!;
        continuation.Succeeded.ShouldBeTrue(continuation.Error?.Message);
        // Minted last: every mint sweeps what has expired.
        var expired = await host.WithStore(store =>
            HandoffCodes.mint(
                store,
                HandoffBinding.NewChannel(Id(tenant.Id), subject, null),
                DateTimeOffset.UtcNow.AddSeconds(-61),
                default
            )
        );
        var before = await host.WithStore(store => store.List(""));

        (await host.Exchange(expired.Code, "one")).Error!.Code.ShouldBe("handoff.expired");
        (await host.Exchange(null, "one")).Error!.Code.ShouldBe("handoff.refused");
        (await host.Exchange("garbage", "one")).Error!.Code.ShouldBe("handoff.refused");
        (await host.Exchange($"{Guid.NewGuid():D}.secret", "one")).Error!.Code.ShouldBe(
            "handoff.refused"
        );
        (await host.Exchange(live.Split('.')[0] + ".wrong-secret", "one")).Error!.Code.ShouldBe(
            "handoff.refused"
        );
        // The page runs as another tenant, or the root: the code stays unconsumed.
        (await host.Exchange(live, "two")).Error!.Code.ShouldBe("handoff.tenant");
        (await host.Exchange(live, null)).Error!.Code.ShouldBe("handoff.tenant");
        (await host.Exchange(live, "nobody")).Error!.Code.ShouldBe("tenant.not_found");
        // Each exchange refuses the other kind.
        (await host.Exchange(continuation.Value!.Code, "one")).Error!.Code.ShouldBe("handoff.kind");
        (await host.Exchange(live, "one", "/api/session/resume")).Error!.Code.ShouldBe(
            "handoff.kind"
        );
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // Then once, and never again.
        var first = await host.Exchange(live, "one");
        first.Succeeded.ShouldBeTrue(first.Error?.Message);
        (await host.Exchange(live, "one")).Error!.Code.ShouldBe("handoff.refused");
        other.Id.ShouldNotBe(tenant.Id);
    }

    [Test]
    public async Task ACode_ExchangesExactlyOnceUnderConcurrency_AndExpiredCodesAreSweptOnMint()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "racing", "Racing");
        var code = await channel.HandoffCode("5555");

        var races = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => host.Exchange(code, "racing"))
        );
        races.Count(static r => r.Succeeded).ShouldBe(1);
        races
            .Where(static r => !r.Succeeded)
            .ShouldAllBe(static r => r.Error!.Code == "handoff.refused");
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(2);

        var subject = (
            (DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded)
                ExternalSubject.Create("5555")
        ).Value;
        var stale = await host.WithStore(store =>
            HandoffCodes.mint(
                store,
                HandoffBinding.NewChannel(Id(tenant.Id), subject, null),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                default
            )
        );
        (await host.WithStore(store => store.Read(HandoffCodes.key(stale.Id)))).ShouldNotBeNull();
        await channel.HandoffCode("5555");
        (await host.WithStore(store => store.Read(HandoffCodes.key(stale.Id)))).ShouldBeNull();
    }

    [Test]
    public async Task AnExistingAccount_IsPendingUntilApprovedFromAnEligibleSession()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (a, tenantA) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var (b, tenantB) = await host.AdmitChannel(operatorToken, "beta", "Beta");
        var (c, tenantC) = await host.AdmitChannel(operatorToken, "gamma", "Gamma");
        var inA = await host.Exchange(await a.HandoffCode("7777", "Seven"), "alpha");
        inA.Succeeded.ShouldBeTrue(inA.Error?.Message);
        var account = (await host.SessionOf(inA.Value!.Token)).Account;
        var before = await host.WithStore(store => store.List(""));

        // B's hand-off: pending, no session, and the account itself untouched.
        var pending = await host.Exchange(await b.HandoffCode("7777"), "beta");
        pending.Succeeded.ShouldBeFalse();
        pending.Error!.Code.ShouldBe("approval.pending");
        var record = (
            await host.WithStore(store => store.Read(approvalKey(account, Id(tenantB.Id))))
        )!;
        JsonSerializer
            .Deserialize<ApprovalDocument>(record.Json, json)!
            .Status.ShouldBe(ApprovalStatus.Pending);
        (await host.WithStore(store => store.List("session/"))).Count.ShouldBe(
            before.Count(static s => s.Key.StartsWith("session/", StringComparison.Ordinal))
        );

        // The eligible session lists it; ineligible ones are refused.
        using var fromA = host.Client(inA.Value.Token);
        var listed = await fromA.GetFromJsonAsync<ApiResponse<PendingApprovalView[]>>(
            "/api/session/approvals"
        );
        listed!.Value!.Select(static p => p.Slug).ShouldBe(["beta"]);
        var recovery = await host.IssueDirectly(
            account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            SessionProvenance.Recovery
        );
        using (var fromRecovery = host.Client(recovery.Token))
        {
            (await Approve(fromRecovery, tenantB.Id)).Error!.Code.ShouldBe(
                SessionFailures.RecoveryCode
            );
        }
        var inB = await host.IssueDirectly(
            account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            SessionProvenance.Issuer
        );
        // A session B itself would issue (written directly, since B may not sign in yet): B is
        // no live route, so it cannot approve itself or another.
        var issuerOfB = await host.WithStore(store =>
            Sessions.issue(
                store,
                account,
                Id(tenantB.Id),
                SessionProvenance.Issuer,
                DateTimeOffset.UtcNow,
                TimeSpan.FromHours(1),
                default
            )
        );
        using (var fromB = host.Client(issuerOfB.Token))
        {
            (await Approve(fromB, tenantB.Id)).Error!.Code.ShouldBe("approval.route");
        }
        (await Approve(fromA, tenantC.Id)).Error!.Code.ShouldBe("approval.none");
        (await Approve(fromA, Guid.NewGuid().ToString("D"))).Error!.Code.ShouldBe(
            "tenant.not_found"
        );

        // A's Issuer session is a live route: it approves B, and B then signs the account in.
        var approved = await Approve(fromA, tenantB.Id);
        approved.Succeeded.ShouldBeTrue(approved.Error?.Message);
        (
            await fromA.GetFromJsonAsync<ApiResponse<PendingApprovalView[]>>(
                "/api/session/approvals"
            )
        )!.Value!.ShouldBeEmpty();
        var viaB = await host.Exchange(await b.HandoffCode("7777"), "beta");
        viaB.Succeeded.ShouldBeTrue(viaB.Error?.Message);
        (await host.SessionOf(viaB.Value!.Token)).Account.ShouldBe(account);
        (await Approve(fromA, tenantB.Id)).Succeeded.ShouldBeTrue("approving again is idempotent");

        // A closed tenant's session is revoked, so it approves nothing.
        var viaC = await host.Exchange(await c.HandoffCode("7777"), "gamma");
        viaC.Error!.Code.ShouldBe("approval.pending");
        await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{tenantA.Id}/close"
        );
        (await Approve(fromA, tenantC.Id)).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        // And a first-party session approves from anywhere.
        var firstParty = await host.IssueDirectly(
            account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        using var fromFirstParty = host.Client(firstParty.Token);
        (await Approve(fromFirstParty, tenantC.Id)).Succeeded.ShouldBeTrue();
        inB.Token.ShouldNotBeNull();
    }

    [Test]
    public async Task TheCoreIssuer_AdoptsAnAccountWithNoPasskeyAndNoLiveRoute_AndOnlyThat()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (a, tenantA) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var (b, _) = await host.AdmitChannel(operatorToken, "beta", "Beta");
        var core = await host.Admit(operatorToken, "core", "Core", null, "https://bot.example");
        using var coreChannel = new FakeChannel(host, core.Value!.Token);
        var orphanToBe = await host.Exchange(await a.HandoffCode("8001", "Orphan"), "alpha");
        var routed = await host.Exchange(await a.HandoffCode("8002", "Routed"), "alpha");
        var withPasskey = await host.Exchange(await a.HandoffCode("8003", "Keyed"), "alpha");
        var orphan = (await host.SessionOf(orphanToBe.Value!.Token)).Account;
        var keyed = (await host.SessionOf(withPasskey.Value!.Token)).Account;
        await host.WithStore(store =>
            Credentials.enrol(
                store,
                store,
                keyed,
                "cred",
                "key",
                0u,
                SessionProvenance.Issuer,
                Id(tenantA.Id),
                DateTimeOffset.UtcNow,
                default
            )
        );

        // While A is a live route nobody is adopted.
        (await host.Exchange(await coreChannel.HandoffCode("8001"), null)).Error!.Code.ShouldBe(
            "approval.pending"
        );
        // B never adopts, whatever the account's state.
        await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{tenantA.Id}/close"
        );
        (await host.Exchange(await b.HandoffCode("8001"), "beta")).Error!.Code.ShouldBe(
            "approval.pending"
        );
        // The core issuer adopts the orphan, records it, and refuses the keyed account.
        var adopted = await host.Exchange(await coreChannel.HandoffCode("8001"), null);
        adopted.Succeeded.ShouldBeTrue(adopted.Error?.Message);
        var session = await host.SessionOf(adopted.Value!.Token);
        session.Account.ShouldBe(orphan);
        session.Tenant.ShouldBe(await host.DefaultTenantId());
        session.Provenance.ShouldBe(SessionProvenance.Issuer);
        var record = (
            await host.WithStore(store => store.Read(approvalKey(orphan, session.Tenant)))
        )!;
        var document = JsonSerializer.Deserialize<ApprovalDocument>(record.Json, json)!;
        document.Status.ShouldBe(ApprovalStatus.Approved);
        document.AdoptedAt.HasValue.ShouldBeTrue();
        (await host.Exchange(await coreChannel.HandoffCode("8003"), null)).Error!.Code.ShouldBe(
            "approval.pending"
        );
        routed.Value!.Token.ShouldNotBeNull();
    }

    [Test]
    public async Task AnExcludedAccount_IsRefusedByTheChannelsHandoff()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var signedIn = await host.Exchange(await channel.HandoffCode("9001"), "alpha");
        var account = (await host.SessionOf(signedIn.Value!.Token)).Account;
        await host.WithStore(store =>
            Approvals.exclude(store, account, Id(tenant.Id), DateTimeOffset.UtcNow, default)
        );
        var before = await host.WithStore(store => store.List(""));

        var refused = await host.Exchange(await channel.HandoffCode("9001"), "alpha");
        refused.Error!.Code.ShouldBe("tenant.excluded");
        (await host.WithStore(store => store.List("")))
            .Where(static s => !s.Key.StartsWith("handoff/", StringComparison.Ordinal))
            .ShouldBe(
                before.Where(static s => !s.Key.StartsWith("handoff/", StringComparison.Ordinal))
            );
        // A subject with no account cannot be excluded: there is no account to record it on.
        (await host.WithStore(store => store.Read("link/twitch/9999"))).ShouldBeNull();
    }

    [Test]
    public async Task AContinuation_ResumesTheSameAccountTenantAndProvenanceExactlyOnce()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var hosted = await host.Exchange(await channel.HandoffCode("2468", "Hosted"), "alpha");
        using var client = host.Client(hosted.Value!.Token);

        using var response = await client.PostAsJsonAsync("/api/session/continue", new { });
        var continuation = (
            await response.Content.ReadFromJsonAsync<ApiResponse<ContinuationView>>()
        )!;
        continuation.Succeeded.ShouldBeTrue(continuation.Error?.Message);
        continuation.Value!.Path.ShouldBe("/t/alpha/continue");
        continuation.Value.Code.ShouldNotContain(hosted.Value.Token);
        var record = (
            await host.WithStore(store =>
                store.Read(HandoffCodes.key(continuation.Value.Code.Split('.')[0]))
            )
        )!;
        record.Json.ShouldContain("\"kind\":\"Continuation\"");
        record.Json.ShouldNotContain(continuation.Value.Code.Split('.')[1]);

        (
            await host.Exchange(continuation.Value.Code, null, "/api/session/resume")
        ).Error!.Code.ShouldBe("handoff.tenant");
        var resumed = await host.Exchange(continuation.Value.Code, "alpha", "/api/session/resume");
        resumed.Succeeded.ShouldBeTrue(resumed.Error?.Message);
        resumed.Value!.Token.ShouldNotBe(hosted.Value.Token);
        var original = await host.SessionOf(hosted.Value.Token);
        var resumedSession = await host.SessionOf(resumed.Value.Token);
        resumedSession.Account.ShouldBe(original.Account);
        resumedSession.Tenant.ShouldBe(original.Tenant);
        resumedSession.Provenance.ShouldBe(original.Provenance);
        (
            await host.Exchange(continuation.Value.Code, "alpha", "/api/session/resume")
        ).Error!.Code.ShouldBe("handoff.refused");
        tenant.Id.ShouldBe(original.Tenant.Value);
    }

    [Test]
    public async Task TheErasureRelay_RemovesOnlyTheCallingTenantsApprovalAndIsIdempotent()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (a, tenantA) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var (b, tenantB) = await host.AdmitChannel(operatorToken, "beta", "Beta");
        var inA = await host.Exchange(await a.HandoffCode("3690", "Erased"), "alpha");
        var account = (await host.SessionOf(inA.Value!.Token)).Account;
        var firstParty = await host.IssueDirectly(
            account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        (await host.Exchange(await b.HandoffCode("3690"), "beta")).Error!.Code.ShouldBe(
            "approval.pending"
        );
        using (var approver = host.Client(firstParty.Token))
        {
            (await Approve(approver, tenantB.Id)).Succeeded.ShouldBeTrue();
        }
        await host.WithStore(store =>
            Credentials.enrol(
                store,
                store,
                account,
                "cred",
                "key",
                0u,
                SessionProvenance.FirstParty,
                null,
                DateTimeOffset.UtcNow,
                default
            )
        );
        var before = await host.WithStore(store => store.List(""));

        var relayed = await a.Erasure("3690");
        relayed.Succeeded.ShouldBeTrue(relayed.Error?.Message);
        relayed.Value!.Dissociated.ShouldBeTrue();
        (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ShouldBe(
                before
                    .Select(static s => s.Key)
                    .Where(key => key != approvalKey(account, Id(tenantA.Id)))
            );
        (
            await host.WithStore(store => store.Read(approvalKey(account, Id(tenantB.Id))))
        ).ShouldNotBeNull();
        (await host.WithStore(store => store.Read("link/twitch/3690"))).ShouldNotBeNull();

        var again = await a.Erasure("3690");
        again.Value!.Dissociated.ShouldBeFalse();
        var unknown = await a.Erasure("9999");
        unknown.Succeeded.ShouldBeTrue();
        unknown.Value!.Dissociated.ShouldBeFalse();
        (await a.Erasure("not-digits")).Error!.Code.ShouldBe("handoff.subject");
        // A's next hand-off for the subject is pending again; the account lives on.
        (await host.Exchange(await a.HandoffCode("3690"), "alpha")).Error!.Code.ShouldBe(
            "approval.pending"
        );
        (await host.Exchange(await b.HandoffCode("3690"), "beta")).Succeeded.ShouldBeTrue();
    }

    [Test]
    public async Task EveryKeyTheFederationWrites_IsWithinTheBound()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, _) = await host.AdmitChannel(operatorToken, new string('a', 32), "Longest");
        var signedIn = await host.Exchange(
            await channel.HandoffCode(new string('9', 64)),
            new string('a', 32)
        );
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        using var client = host.Client(signedIn.Value!.Token);
        (await client.PostAsJsonAsync("/api/session/continue", new { })).EnsureSuccessStatusCode();

        (await host.WithStore(store => store.List(""))).ShouldAllBe(static s =>
            s.Key.Length <= 160
        );
        (await host.WithStore(store => store.List("link/"))).ShouldContain(static s =>
            s.Key.Length == "link/twitch/".Length + 64
        );
    }

    private static async Task<ApiResponse<ApprovalView>> Approve(HttpClient client, string tenantId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/session/approvals/{tenantId}",
            new { }
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<ApprovalView>>())!;
    }

    private static TenantId Id(string value) =>
        ((DomainResult<TenantId, IdentityValueFailure>.Succeeded)TenantId.Create(value)).Value;
}
