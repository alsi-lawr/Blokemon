using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Blokemon.App.Contracts;
using Blokemon.Web.Hosting;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Blokemon.Web.Tests;

/// <summary>
/// BLOKEMON-155's serving rules on the host: every client route loads the shell, the framing
/// policy is per tenant on the hosted page and <c>'none'</c> everywhere else, caching is
/// <c>no-store</c> under <c>/t/</c>, revalidated elsewhere and immutable for fingerprinted
/// framework assets, the baseline headers travel with every response, no circuit is offered
/// and no <c>/api</c> route carries antiforgery.
/// </summary>
public sealed partial class HostingHeaderTests
{
    private const string Parent = "https://parent.example";

    [Test]
    public async Task EveryClientRoute_LoadsTheShellOnDirectNavigation_AndNoCircuitIsOffered()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001", Parent);
        using var client = host.Client();

        foreach (
            var path in new[]
            {
                "/",
                "/profile",
                "/signin",
                "/operator",
                "/t/alpha",
                "/t/alpha/continue",
                "/t/nobody",
            }
        )
        {
            using var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            html.ShouldContain("_framework/blazor.web.js", customMessage: path);
            html.ShouldContain("<base href=\"/\">", customMessage: path);
        }

        // The interactive-server render mode is gone with its hub: nothing negotiates where a
        // circuit would be (the POST falls to the not-found page, whose antiforgery answers 400).
        using var negotiate = await client.PostAsync("/_blazor/negotiate", new StringContent(""));
        negotiate.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        (await negotiate.Content.ReadAsStringAsync()).ShouldNotContain("connectionId");
        using var hub = await client.GetAsync("/_blazor");
        hub.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        alpha.Dispose();
    }

    [Test]
    public async Task Framing_NamesTheRegisteredParentOnAnAdmittedTenantsHostedPage_AndIsNoneEverywhereElse()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001", Parent);
        var (beta, _) = await host.AdmitChannel(operatorToken, "beta", "Beta", "1002", null);
        var (gamma, gammaTenant) = await host.AdmitChannel(
            operatorToken,
            "gamma",
            "Gamma",
            "1003",
            Parent
        );
        (
            await host.Operator<TenantStatusView>(
                operatorToken,
                $"/api/operator/tenants/{gammaTenant.Id}/close"
            )
        ).Succeeded.ShouldBeTrue();
        using var client = host.Client();

        (await Headers(client, "/t/alpha")).ContentSecurityPolicy.ShouldBe(
            $"frame-ancestors {Parent}"
        );
        foreach (
            var path in new[]
            {
                "/t/alpha/continue",
                "/t/beta",
                "/t/gamma",
                "/t/nobody",
                "/t/alpha/",
                "/",
                "/profile",
                "/signin",
                "/api/state",
                "/api/tenant/alpha",
                "/healthz",
                "/css/foundation.css",
                "/_framework/blazor.web.js",
                "/no-such-page",
            }
        )
        {
            var headers = await Headers(client, path);
            headers.ContentSecurityPolicy.ShouldBe(HostingHeaders.NoneFraming, path);
            headers.XFrameOptions.ShouldBeNull(path);
        }
        alpha.Dispose();
        beta.Dispose();
        gamma.Dispose();
    }

    [Test]
    public async Task Caching_IsNoStoreUnderTheTenantRoutes_RevalidatedElsewhere_AndImmutableForFingerprintedFrameworkAssets()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001", Parent);
        using var client = host.Client();

        foreach (var path in new[] { "/t/alpha", "/t/alpha/continue", "/t/nobody" })
        {
            (await Headers(client, path)).CacheControl.ShouldBe(HostingHeaders.NoStore, path);
        }

        var shell = await client.GetStringAsync("/");
        var fingerprinted = FingerprintedRuntime().Match(shell);
        fingerprinted.Success.ShouldBeTrue("the shell names a fingerprinted runtime asset");
        foreach (
            var path in new[]
            {
                "/",
                "/profile",
                "/api/state",
                "/healthz",
                "/css/foundation.css",
                "/art/BLK-001-weedman-will-200.webp",
                "/fonts/GilliusADF-Bold.otf",
                "/_framework/blazor.web.js",
                "/no-such-page",
            }
        )
        {
            var cache = (await Headers(client, path)).CacheControl;
            (cache.Contains("no-cache") || cache.Contains("no-store")).ShouldBeTrue(
                $"{path}: {cache}"
            );
        }
        (await Headers(client, "/" + fingerprinted.Value)).CacheControl.ShouldBe(
            HostingHeaders.Immutable
        );
        alpha.Dispose();
    }

    [Test]
    public async Task Baseline_HeadersTravelWithEveryResponse_AndNoApiRouteCarriesAntiforgery()
    {
        await using var host = SessionHost.Create();
        using var client = host.Client();

        foreach (var path in new[] { "/", "/api/state", "/css/foundation.css", "/no-such-page" })
        {
            var headers = await Headers(client, path);
            headers.XContentTypeOptions.ShouldBe("nosniff", path);
            headers.ReferrerPolicy.ShouldBe("no-referrer", path);
        }

        var api = host
            .Factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(static endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true
            )
            .ToList();
        api.Count.ShouldBeGreaterThan(20);
        api.Where(static endpoint =>
                endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation == true
            )
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .ShouldBeEmpty();
        // A tokenless POST reaches the route and gets its typed answer, not a 400.
        using var erase = await client.PostAsJsonAsync("/api/session/erase", new { });
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await erase.Content.ReadFromJsonAsync<ApiResponse<AccountErasedView>>()
        )!.Error!.Code.ShouldBe("session.required");
    }

    [Test]
    public async Task TheWebAssemblyPayload_IsServedCompressed()
    {
        await using var host = SessionHost.Create();
        using var client = host.Client();
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("br, gzip");

        var shell = await client.GetStringAsync("/");
        var runtime = FingerprintedRuntime().Match(shell).Value;
        using var response = await client.GetAsync("/" + runtime);
        response.Content.Headers.ContentEncoding.ShouldContain(static encoding =>
            encoding == "br" || encoding == "gzip"
        );
    }

    private static async Task<ResponseHeaders> Headers(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        string? Of(string name) =>
            response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
            : response.Content.Headers.TryGetValues(name, out var content)
                ? string.Join(",", content)
            : null;
        return new(
            Of("Content-Security-Policy"),
            Of("X-Frame-Options"),
            Of("Cache-Control") ?? "",
            Of("X-Content-Type-Options"),
            Of("Referrer-Policy")
        );
    }

    private sealed record ResponseHeaders(
        string? ContentSecurityPolicy,
        string? XFrameOptions,
        string CacheControl,
        string? XContentTypeOptions,
        string? ReferrerPolicy
    );

    [GeneratedRegex(@"_framework/dotnet\.[a-z0-9]{8,12}\.js")]
    private static partial Regex FingerprintedRuntime();
}
