using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

public sealed class ServerSessionTests
{
    private static readonly Guid MatchId = Guid.Parse("c6111111-1111-1111-1111-111111111111");

    /// <summary>Every route that requires a session: the eleven application routes minus the state route, BLOKEMON-149's own, BLOKEMON-150's account-bound routes, BLOKEMON-151's continuation, approval and operator admission routes, and BLOKEMON-152's account, operator and owner routes.</summary>
    private static (HttpMethod Method, string Path, object? Body)[] SessionRequiredRoutes() =>
        [
            (HttpMethod.Post, "/api/profile", new CreateProfileRequest(Guid.NewGuid(), "P")),
            (HttpMethod.Post, "/api/packs/open", new OpenPackRequest(Guid.NewGuid())),
            (
                HttpMethod.Post,
                "/api/starter-decks/claim",
                new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom")
            ),
            (
                HttpMethod.Post,
                "/api/decks",
                new SaveDeckRequest(Guid.NewGuid(), null, null, "D", [])
            ),
            (
                HttpMethod.Post,
                "/api/decks/delete",
                new DeleteDeckRequest(Guid.NewGuid(), Guid.NewGuid())
            ),
            (HttpMethod.Post, "/api/matches", new StartMatchRequest(Guid.NewGuid(), MatchId)),
            (
                HttpMethod.Post,
                $"/api/matches/{MatchId:D}/actions",
                new ApplyMatchActionRequest(Guid.NewGuid(), 1, "end-turn", [])
            ),
            (HttpMethod.Post, "/api/matches/abandon", new AbandonSavedMatchRequest(1, "identity")),
            (
                HttpMethod.Post,
                "/api/matches/history/discard",
                new DiscardMatchHistoryRequest(1, "identity")
            ),
            (HttpMethod.Post, "/api/purge", new { }),
            (HttpMethod.Post, "/api/session/signout", new { }),
            (HttpMethod.Post, "/api/session/continue", new { }),
            (HttpMethod.Get, "/api/session/approvals", null),
            (HttpMethod.Post, $"/api/session/approvals/{MatchId:D}", new { }),
            (HttpMethod.Post, "/api/operator/bootstrap", new OperatorBootstrapRequest("x")),
            // The account, operator and owner routes (BLOKEMON-152).
            (HttpMethod.Post, "/api/session/erase", new { }),
            (HttpMethod.Get, "/api/session/roles", null),
            (HttpMethod.Get, "/api/operator/accounts", null),
            (HttpMethod.Get, "/api/operator/tenants", null),
            (HttpMethod.Get, "/api/operator/diagnostics", null),
            (HttpMethod.Post, $"/api/operator/accounts/{MatchId:D}/disable", new { }),
            (HttpMethod.Post, $"/api/operator/accounts/{MatchId:D}/enable", new { }),
            (HttpMethod.Post, $"/api/operator/accounts/{MatchId:D}/erase", new { }),
            (HttpMethod.Post, $"/api/operator/accounts/{MatchId:D}/grant-operator", new { }),
            (
                HttpMethod.Post,
                $"/api/operator/tenants/{MatchId:D}/owner",
                new OwnerAssignmentRequest(MatchId.ToString("D"))
            ),
            (HttpMethod.Get, "/api/owner/tenants", null),
            (HttpMethod.Get, $"/api/owner/{MatchId:D}/approvals", null),
            (HttpMethod.Post, $"/api/owner/{MatchId:D}/accounts/{MatchId:D}/exclude", new { }),
            (HttpMethod.Post, $"/api/owner/{MatchId:D}/accounts/{MatchId:D}/readmit", new { }),
            // The operator's admission routes (BLOKEMON-151).
            (HttpMethod.Post, "/api/operator/tenants", new { slug = "x", label = "x" }),
            (HttpMethod.Post, $"/api/operator/tenants/{MatchId:D}/rotate", new { }),
            (HttpMethod.Post, $"/api/operator/tenants/{MatchId:D}/close", new { }),
            (HttpMethod.Post, $"/api/operator/tenants/{MatchId:D}/revoke", new { }),
            // The first-party routes that act on the session's own account (BLOKEMON-150).
            (HttpMethod.Post, "/api/session/firstparty/enrol/options", new { }),
            (
                HttpMethod.Post,
                "/api/session/firstparty/enrol",
                new PasskeyCeremonyRequest("x", JsonDocument.Parse("{}").RootElement)
            ),
            (HttpMethod.Get, "/api/session/firstparty/credentials", null),
            (HttpMethod.Post, "/api/session/firstparty/recovery-codes", new { }),
        ];

    [Test]
    public async Task EverySessionRequiredRoute_RefusesNoExpiredRevokedAndCookieOnlyCallersAndMutatesNothing()
    {
        await using var host = SessionHost.Create();
        var signedIn = await host.SignIn("matrix", "Matrix Player");
        var revoked = await host.SignIn("revoked", "Revoked Player");
        var expired = await host.IssueDirectly(
            signedIn.Session.Account,
            DateTimeOffset.UtcNow.AddHours(-9),
            TimeSpan.FromHours(8)
        );
        using var revoking = host.Client(revoked.Token);
        (await revoking.PostAsJsonAsync("/api/session/signout", new { })).EnsureSuccessStatusCode();
        var before = await host.WithStore(store => store.List(""));
        (string Name, string? Token, string? Cookie, string ExpectedCode)[] callers =
        [
            ("no session", null, null, SessionFailures.RequiredCode),
            ("expired session", expired.Token, null, SessionFailures.ExpiredCode),
            ("revoked session", revoked.Token, null, SessionFailures.RequiredCode),
            ("garbage token", "not.a-token", null, SessionFailures.RequiredCode),
            ("cookie only", null, $"session={signedIn.Token}", SessionFailures.RequiredCode),
        ];

        foreach (var (name, token, cookie, expectedCode) in callers)
        {
            using var client = host.Client(token);
            foreach (var (method, path, body) in SessionRequiredRoutes())
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Add("Origin", "https://elsewhere.example");
                if (cookie is not null)
                {
                    request.Headers.Add("Cookie", cookie);
                }
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, body.GetType());
                }

                using var response = await client.SendAsync(request);
                var envelope = await response.Content.ReadFromJsonAsync<
                    ApiResponse<JsonElement?>
                >();

                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{name} {path}");
                response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse(path);
                envelope!.Succeeded.ShouldBeFalse($"{name} {path}");
                envelope.Value.ShouldBeNull($"{name} {path}");
                envelope.Error!.Code.ShouldBe(expectedCode, $"{name} {path}");
            }
        }

        (await host.WithStore(store => store.List(""))).ShouldBe(before);
    }

    [Test]
    public async Task AnonymousRoutes_AnswerWithoutASessionAndIgnoreAStaleOne()
    {
        await using var host = SessionHost.Create();
        var signedIn = await host.SignIn("anonymous-check", "Anonymous Check");
        using (var signedInClient = host.Client(signedIn.Token))
        {
            (
                await signedInClient.PostAsJsonAsync(
                    "/api/profile",
                    new CreateProfileRequest(Guid.NewGuid(), "Anonymous Check")
                )
            ).EnsureSuccessStatusCode();
        }
        var expired = await host.IssueDirectly(
            signedIn.Session.Account,
            DateTimeOffset.UtcNow.AddDays(-1),
            TimeSpan.FromHours(1)
        );

        foreach (var token in new[] { null, expired.Token, "stale.token" })
        {
            using var client = host.Client(token);
            using var health = await client.GetAsync("/healthz");
            var state = await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state");
            var tenant = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
                $"/api/tenant/{Tenants.DefaultSlug.Value}"
            );

            health.StatusCode.ShouldBe(HttpStatusCode.OK);
            state!.Succeeded.ShouldBeTrue();
            state.Value!.Profile.ShouldBeNull();
            state.Value.Cards.ShouldNotBeEmpty();
            state.Value.Cards.ShouldAllBe(card => card.OwnedQuantity == 0);
            state.Value.Decks.ShouldBeEmpty();
            state.Value.StarterDecks.ShouldNotBeEmpty();
            state.Value.Match.ShouldBeNull();
            tenant!.Succeeded.ShouldBeTrue();
        }

        using var genuine = host.Client(signedIn.Token);
        var own = await genuine.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state");
        own!.Value!.Profile!.DisplayName.ShouldBe("Anonymous Check");
    }

    [Test]
    public async Task TenantDescriptor_CarriesOnlyItsFieldsAndIsTypedNotFoundForAnUnknownSlug()
    {
        await using var host = SessionHost.Create();
        var tenant = await host.DefaultTenant();
        using var client = host.Client();

        var descriptor = JsonNode
            .Parse(await client.GetStringAsync($"/api/tenant/{Tenants.DefaultSlug.Value}"))!
            .AsObject();
        var value = descriptor["value"]!.AsObject();

        descriptor["succeeded"]!.GetValue<bool>().ShouldBeTrue();
        value
            .Select(static field => field.Key)
            .Order(StringComparer.Ordinal)
            .ShouldBe([
                "coreSignIn",
                "enabledProviders",
                "handoffExchangePath",
                "id",
                "label",
                "registeredParentOrigin",
                "slug",
            ]);
        value["id"]!.GetValue<string>().ShouldBe(tenant.Id);
        value["slug"]!.GetValue<string>().ShouldBe(Tenants.DefaultSlug.Value);
        value["label"]!.GetValue<string>().ShouldBe(Tenants.DefaultLabel);
        value["enabledProviders"]!
            .AsArray()
            .Select(static item => item!.GetValue<string>())
            .ShouldBe([TestIdentityProvider.ProviderName]);
        value["registeredParentOrigin"].ShouldBeNull();
        value["coreSignIn"].ShouldBeNull();
        // The host names the hand-off exchange route (BLOKEMON-151's) so the client need not.
        value["handoffExchangePath"]!.GetValue<string>().ShouldBe("api/session/blokebot");

        foreach (var slug in new[] { "nobody", "Not-A-Slug", "a--b" })
        {
            var missing = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
                $"/api/tenant/{slug}"
            );
            missing!.Succeeded.ShouldBeFalse(slug);
            missing.Value.ShouldBeNull(slug);
            missing.Error!.Code.ShouldBe("tenant.not_found", slug);
        }

        // The reserved segment is the channel's own descriptor route, which wants a token.
        var self = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            "/api/tenant/self"
        );
        self!.Succeeded.ShouldBeFalse();
        self.Value.ShouldBeNull();
        self.Error!.Code.ShouldBe("channel.token_required");
    }

    [Test]
    public async Task PublishedHost_StartsWithNoProviderAndAnEmptyRegistry()
    {
        await using var host = SessionHost.Create(withProvider: false);
        using var client = host.Client();

        var registry = host.Factory.Services.GetRequiredService<IdentityProviderRegistry>();
        var identity = host.Factory.Services.GetRequiredService<IdentityConfiguration>();
        using var health = await client.GetAsync("/healthz");
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );

        registry.Enabled.ShouldBeEmpty();
        identity.EnabledProviders.ShouldBeEmpty();
        identity.SessionLifetime.ShouldBe(TimeSpan.FromHours(8));
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        descriptor!.Value!.EnabledProviders.ShouldBeEmpty();
        descriptor.Value.CoreSignIn.ShouldBeNull();
        // The federation's provider is the only implementation a published host ships (the
        // first-party one exists only with a relying party configured); the registry lists it
        // only when the deployment enables it, and the test double is never here.
        host.Factory.Services.GetServices<IIdentityProvider>()
            .ShouldAllBe(static provider =>
                provider is Blokemon.Identity.Federated.BlokeBotProvider
            );
    }

    // The two names below are the host's own (Blokemon.Web owns them); this test project is
    // exempted from the D-029 scan for them and says so in the evidence.
    [Test]
    public async Task CoreSignInUrl_AppearsInTheDescriptorExactlyWhenConfigured()
    {
        await using var host = SessionHost.Create(builder =>
            builder.UseSetting(
                "Blokemon:Identity:Providers:BlokeBot:CoreSignInUrl",
                "https://core.example/blokemon/signin"
            )
        );
        using var client = host.Client();

        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );

        descriptor!.Value!.CoreSignIn.ShouldBe(
            new CoreSignInView("Sign in with Twitch", "https://core.example/blokemon/signin")
        );
        descriptor.Value.EnabledProviders.ShouldBe([TestIdentityProvider.ProviderName]);
    }

    [Test]
    public async Task InvalidIdentityConfiguration_FailsStartUpNamingTheKey()
    {
        (string Key, string Value)[] invalid =
        [
            (IdentityConfigurationModule.SessionLifetimeKey, "25:00:00"),
            (IdentityConfigurationModule.OperatorBootstrapCodeKey, "short"),
            (IdentityConfigurationModule.providerEnabledKey("ghost"), "true"),
            (IdentityConfigurationModule.HandoffRateLimitPerMinuteKey, "0"),
        ];

        foreach (var (key, value) in invalid)
        {
            await using var host = SessionHost.Create(builder => builder.UseSetting(key, value));
            Exception? thrown = null;
            try
            {
                using var client = host.Client();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            thrown.ShouldNotBeNull($"{key}={value} was accepted");
            thrown.ToString().ShouldContain(key);
        }
    }

    [Test]
    public async Task SessionLifetime_IsTheConfiguredOneAndAbsolute()
    {
        await using var host = SessionHost.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.SessionLifetimeKey, "00:30:00")
        );
        var issued = await host.SignIn("bounded");
        using var client = host.Client(issued.Token);

        var stored = await host.WithStore(store => store.Read($"session/{issued.Session.Id}"));
        var document = JsonNode.Parse(stored!.Json)!.AsObject();
        var issuedAt = document["issuedAt"]!.GetValue<DateTimeOffset>();
        var expiresAt = document["expiresAt"]!.GetValue<DateTimeOffset>();
        (expiresAt - issuedAt).ShouldBe(TimeSpan.FromMinutes(30));
        issued.Session.ExpiresAt.ShouldBe(expiresAt);

        // Using the session is not renewing it.
        (
            await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state")
        )!.Succeeded.ShouldBeTrue();
        (
            await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(Guid.NewGuid(), "Bounded")
            )
        ).EnsureSuccessStatusCode();
        var after = await host.WithStore(store => store.Read($"session/{issued.Session.Id}"));
        after.ShouldBe(stored);
        document.Select(static field => field.Key).ShouldNotContain("token");
        document.Select(static field => field.Key).ShouldNotContain("secret");
    }

    [Test]
    public async Task SignOut_RevokesTheDocumentServerSideAndTheTokenIsRefusedThereafter()
    {
        await using var host = SessionHost.Create();
        var issued = await host.SignIn("leaving", "Leaving Player");
        using var client = host.Client(issued.Token);
        (
            await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state")
        )!.Value!.Profile!.DisplayName.ShouldBe("Leaving Player");

        using var signedOut = await client.PostAsJsonAsync("/api/session/signout", new { });
        var receipt = await signedOut.Content.ReadFromJsonAsync<ApiResponse<SignOutView>>();
        var mutation = await client.PostAsJsonAsync(
            "/api/packs/open",
            new OpenPackRequest(Guid.NewGuid())
        );
        var refused = await mutation.Content.ReadFromJsonAsync<ApiResponse<ApplicationView>>();
        var state = await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state");
        var again = await client.PostAsJsonAsync("/api/session/signout", new { });
        var againEnvelope = await again.Content.ReadFromJsonAsync<ApiResponse<SignOutView>>();

        receipt!.Succeeded.ShouldBeTrue();
        refused!.Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        state!.Value!.Profile.ShouldBeNull();
        againEnvelope!.Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        (await host.WithStore(store => store.List("session/"))).ShouldBeEmpty();
    }

    [Test]
    public async Task DisablingOrErasingTheAccount_RevokesItsSession()
    {
        foreach (var status in new[] { AccountStatus.Disabled, AccountStatus.Erased })
        {
            await using var host = SessionHost.Create();
            var issued = await host.SignIn($"lifecycle-{status}");
            await host.WithStore(async store =>
            {
                var key = accountKey(issued.Session.Account);
                var stored = (await store.Read(key))!;
                var account = JsonNode.Parse(stored.Json)!.AsObject();
                account["status"] = status.ToString();
                (
                    await store.Update(key, stored.Revision, account.ToJsonString())
                ).ShouldBeOfType<DocumentWriteResult.Written>();
            });
            using var client = host.Client(issued.Token);

            var response = await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(Guid.NewGuid(), "Gone")
            );
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ApplicationView>>();

            envelope!.Error!.Code.ShouldBe(SessionFailures.RequiredCode, status.ToString());
            (
                await host.WithStore(store => store.Read($"session/{issued.Session.Id}"))
            ).ShouldBeNull();
        }
    }

    [Test]
    public async Task Sweep_RemovesExpiredSessionDocumentsAndLeavesLiveOnes()
    {
        await using var host = SessionHost.Create();
        var live = await host.SignIn("sweep-live");
        var expired = await host.IssueDirectly(
            live.Session.Account,
            DateTimeOffset.UtcNow.AddHours(-10),
            TimeSpan.FromHours(8)
        );
        var justExpired = await host.IssueDirectly(
            live.Session.Account,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            TimeSpan.FromMinutes(1)
        );

        var removed = await host.WithStore(store =>
            SessionSweep.run(store, store, DateTimeOffset.UtcNow, CancellationToken.None)
        );
        var remaining = await host.WithStore(store => store.List("session/"));
        var again = await host.WithStore(store =>
            SessionSweep.run(store, store, DateTimeOffset.UtcNow, CancellationToken.None)
        );

        removed.ShouldBe(2);
        again.ShouldBe(0);
        remaining.Select(static summary => summary.Key).ShouldBe([$"session/{live.Session.Id}"]);
        _ = expired;
        _ = justExpired;
        host.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .ShouldContain(static service => service is SessionSweepService);
    }

    [Test]
    public async Task OtherAccount_SeesOnlyItsOwnDocuments()
    {
        await using var host = SessionHost.Create();
        var alpha = await host.SignIn("alpha", "Alpha");
        var beta = await host.SignIn("beta", "Beta");
        using var alphaClient = host.Client(alpha.Token);
        using var betaClient = host.Client(beta.Token);

        var alphaState = await alphaClient.GetFromJsonAsync<ApiResponse<ApplicationView>>(
            "/api/state"
        );
        (
            await alphaClient.PostAsJsonAsync(
                "/api/starter-decks/claim",
                new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom")
            )
        ).EnsureSuccessStatusCode();
        var betaState = await betaClient.GetFromJsonAsync<ApiResponse<ApplicationView>>(
            "/api/state"
        );
        (await betaClient.PostAsJsonAsync("/api/purge", new { })).EnsureSuccessStatusCode();
        var alphaAfter = await alphaClient.GetFromJsonAsync<ApiResponse<ApplicationView>>(
            "/api/state"
        );

        alphaState!.Value!.Profile!.DisplayName.ShouldBe("Alpha");
        betaState!.Value!.Profile!.DisplayName.ShouldBe("Beta");
        betaState.Value.Decks.ShouldBeEmpty();
        alphaAfter!.Value!.Profile!.DisplayName.ShouldBe("Alpha");
        alphaAfter.Value.Decks.ShouldHaveSingleItem();
        var keys = (await host.WithStore(store => store.List("a/"))).Select(static s => s.Key);
        keys.ShouldBe([$"a/{alpha.Session.Account}/profile"]);
    }

    [Test]
    public async Task OperatorBootstrap_RedeemsOnceFromAFirstPartySessionAndLocksOutAfterFiveFailures()
    {
        const string code = "the-operator-bootstrap-code";
        await using var host = SessionHost.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.OperatorBootstrapCodeKey, code)
        );
        var issuer = await host.SignIn("issuer-op", provenance: SessionProvenance.Issuer);
        var firstParty = await host.SignIn("first-op");
        using var issuerClient = host.Client(issuer.Token);
        using var client = host.Client(firstParty.Token);

        var provenance = await Bootstrap(issuerClient, code);
        provenance.Error!.Code.ShouldBe("bootstrap.provenance");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Bootstrap(client, $"wrong-{attempt}")).Error!.Code.ShouldBe("bootstrap.refused");
        }
        var locked = await Bootstrap(client, code);
        locked.Error!.Code.ShouldBe("bootstrap.locked");
        (await host.WithStore(store => store.Read(OperatorBootstrap.Key))).ShouldBeNull();

        await using var fresh = SessionHost.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.OperatorBootstrapCodeKey, code)
        );
        var operatorToBe = await fresh.SignIn("operator");
        using var freshClient = fresh.Client(operatorToBe.Token);
        var redeemed = await Bootstrap(freshClient, code);
        var second = await Bootstrap(freshClient, code);
        var account = await fresh.WithStore(store =>
            store.Read(accountKey(operatorToBe.Session.Account))
        );
        var record = await fresh.WithStore(store => store.Read(OperatorBootstrap.Key));

        redeemed.Succeeded.ShouldBeTrue();
        second.Error!.Code.ShouldBe("bootstrap.redeemed");
        JsonNode.Parse(account!.Json)!["operator"]!.GetValue<bool>().ShouldBeTrue();
        JsonNode.Parse(record!.Json)!["account"]!
            .GetValue<string>()
            .ShouldBe(operatorToBe.Session.Account.Value);
    }

    [Test]
    public async Task OperatorBootstrap_IsUnavailableWhenNoCodeIsConfigured()
    {
        await using var host = SessionHost.Create();
        var issued = await host.SignIn("no-code");
        using var client = host.Client(issued.Token);

        (await Bootstrap(client, "anything-at-all-really")).Error!.Code.ShouldBe(
            "bootstrap.unavailable"
        );
    }

    private static async Task<ApiResponse<OperatorBootstrapView>> Bootstrap(
        HttpClient client,
        string code
    )
    {
        using var response = await client.PostAsJsonAsync(
            "/api/operator/bootstrap",
            new OperatorBootstrapRequest(code)
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OperatorBootstrapView>>())!;
    }
}
