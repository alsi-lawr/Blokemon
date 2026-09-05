using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// BLOKEMON-154's end-to-end authorization matrix. The session-required <c>/api</c> routes are
/// enumerated from the running application's <see cref="EndpointDataSource"/> and classified by the
/// shipped <see cref="ApiSessionPolicy"/>, so a route a later ticket adds cannot escape: every
/// enumerated route is either anonymous or refuses a caller that holds no valid session. Across the
/// caller classes the ticket names — signed-out, expired, revoked, disabled, excluded (the classes
/// whose session does not validate), and other-account and wrong-tenant (which reach the handler) —
/// every refusal is proven to mutate nothing by a full-store snapshot.
/// </summary>
public sealed partial class MilestoneAuthorizationTests
{
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    private static readonly Guid Placeholder = Guid.Parse("c6111111-1111-1111-1111-111111111111");

    /// <summary>Every <c>/api</c> route the application maps, with its method and a concrete path.</summary>
    private static (string Method, string Path)[] ApiRoutes(SessionHost host) =>
        host
            .Factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(static endpoint =>
                ("/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')).StartsWith(
                    "/api",
                    StringComparison.Ordinal
                )
            )
            .Select(static endpoint =>
                (
                    Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods[0]
                        ?? "GET",
                    Path: RouteParameter()
                        .Replace(
                            "/" + endpoint.RoutePattern.RawText!.TrimStart('/'),
                            Placeholder.ToString("D")
                        )
                )
            )
            .ToArray();

    private static (string Method, string Path)[] SessionRequired(SessionHost host) =>
        ApiRoutes(host)
            .Where(static route => !ApiSessionPolicy.IsAnonymous(route.Method, route.Path))
            .ToArray();

    [Test]
    public async Task EveryApiRoute_IsAnonymousOrRefusesEveryInvalidSessionCaller_MutatingNothing()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();

        // A disabled account (its status makes the session fail validation).
        var disabled = await host.SignIn("disabled-user");
        (
            await host.WithStore(store =>
                AccountLifecycle.disable(store, store, disabled.Session.Account, default)
            )
        ).IsSucceeded.ShouldBeTrue();

        // An account excluded from the channel whose Issuer session it holds.
        var (alpha, alphaTenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var excludedExchange = await host.Exchange(await alpha.HandoffCode("5001"), "alpha");
        var excludedSession = await host.SessionOf(excludedExchange.Value!.Token);
        var alphaId = TenantIdOf(alphaTenant.Id);
        (
            await host.WithStore(store =>
                Exclusions.exclude(
                    store,
                    store,
                    excludedSession.Account,
                    alphaId,
                    DateTimeOffset.UtcNow,
                    default
                )
            )
        ).IsSucceeded.ShouldBeTrue();

        var live = await host.SignIn("expiring");
        var expired = await host.IssueDirectly(
            live.Session.Account,
            DateTimeOffset.UtcNow.AddHours(-9),
            TimeSpan.FromHours(8)
        );
        var revoked = await host.SignIn("revoked-user");
        using (var revoking = host.Client(revoked.Token))
        {
            (
                await revoking.PostAsJsonAsync("/api/session/signout", new { })
            ).EnsureSuccessStatusCode();
        }

        var routes = SessionRequired(host);
        routes.Length.ShouldBeGreaterThan(20);

        (string Name, string? Token, string ExpectedCode)[] callers =
        [
            ("signed out", null, SessionFailures.RequiredCode),
            ("garbage token", "not.a-token", SessionFailures.RequiredCode),
            ("expired session", expired.Token, SessionFailures.ExpiredCode),
            ("revoked session", revoked.Token, SessionFailures.RequiredCode),
            ("disabled account", disabled.Token, SessionFailures.RequiredCode),
            (
                "excluded from its tenant",
                excludedExchange.Value!.Token,
                SessionFailures.RequiredCode
            ),
        ];

        var before = await host.WithStore(store => store.List(""));
        foreach (var (name, token, expectedCode) in callers)
        {
            using var client = host.Client(token);
            foreach (var (method, path) in routes)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                request.Headers.Add("Origin", "https://elsewhere.example");
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    request.Content = JsonContent.Create(new { });
                }

                using var response = await client.SendAsync(request);
                var envelope = await response.Content.ReadFromJsonAsync<
                    ApiResponse<JsonElement?>
                >();

                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{name} {method} {path}");
                response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse(path);
                envelope!.Succeeded.ShouldBeFalse($"{name} {method} {path}");
                envelope.Error!.Code.ShouldBe(expectedCode, $"{name} {method} {path}");
            }
        }

        (await host.WithStore(store => store.List(""))).ShouldBe(before);
        alpha.Dispose();
    }

    [Test]
    public async Task OtherAccountAndWrongTenantCallers_AreRefusedByTheAuthorityRoutes_MutatingNothing()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, alphaTenant) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var (beta, betaTenant) = await host.AdmitChannel(operatorToken, "beta", "Beta", "2002");

        // A plain player (another account, a valid first-party session) and an Issuer of beta
        // (a session for the wrong tenant) each hold no authority over alpha or the operator.
        var player = await host.SignIn("plain-player");
        var betaIssuer = await host.Exchange(await beta.HandoffCode("2002"), "beta");
        var otherAccount = (await host.SessionOf(betaIssuer.Value!.Token)).Account;

        (string Name, string Token)[] callers =
        [
            ("other account (player)", player.Token),
            ("wrong tenant (beta issuer)", betaIssuer.Value!.Token),
        ];

        (string Method, string Path, string ExpectedCode)[] authorityRoutes =
        [
            ("GET", "/api/operator/accounts", "operator.required"),
            ("GET", "/api/operator/tenants", "operator.required"),
            ("GET", "/api/operator/diagnostics", "operator.required"),
            ($"POST", $"/api/operator/accounts/{otherAccount}/disable", "operator.required"),
            ("POST", $"/api/operator/accounts/{otherAccount}/erase", "operator.required"),
            ("POST", $"/api/operator/accounts/{otherAccount}/grant-operator", "operator.required"),
            ("POST", $"/api/operator/tenants/{alphaTenant.Id}/close", "operator.required"),
            ("GET", $"/api/owner/{alphaTenant.Id}/approvals", "owner.required"),
            (
                "POST",
                $"/api/owner/{alphaTenant.Id}/accounts/{otherAccount}/exclude",
                "owner.required"
            ),
        ];

        // A code minted before the snapshot so the refused exchanges below can be shown to leave
        // it intact; minting is itself a legitimate mutation and is not what is under test here.
        var code = await alpha.HandoffCode("3003");

        var before = await host.WithStore(store => store.List(""));
        foreach (var (name, token) in callers)
        {
            using var client = host.Client(token);
            foreach (var (method, path, expectedCode) in authorityRoutes)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    request.Content = JsonContent.Create(new { });
                }

                using var response = await client.SendAsync(request);
                var envelope = await response.Content.ReadFromJsonAsync<
                    ApiResponse<JsonElement?>
                >();
                envelope!.Succeeded.ShouldBeFalse($"{name} {method} {path}");
                envelope.Error!.Code.ShouldBe(expectedCode, $"{name} {method} {path}");
            }
        }

        // Wrong-tenant at the hand-off and continuation exchanges: the code minted above for
        // alpha, presented as beta, and the same hand-off code presented at the continuation route.
        (await host.Exchange(code, "beta")).Error!.Code.ShouldBe("handoff.tenant");
        (await host.Exchange(code, "beta", "/api/session/resume")).Error!.Code.ShouldBe(
            "handoff.kind"
        );

        (await host.WithStore(store => store.List(""))).ShouldBe(before);
        _ = betaTenant;
        alpha.Dispose();
        beta.Dispose();
    }

    private static TenantId TenantIdOf(string id) =>
        TenantId.Create(id) is DomainResult<TenantId, IdentityValueFailure>.Succeeded parsed
            ? parsed.Value
            : throw new InvalidOperationException("A stored tenant id is malformed.");
}
