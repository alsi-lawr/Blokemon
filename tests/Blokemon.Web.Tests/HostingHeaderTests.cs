using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Blokemon.App.Contracts;
using Blokemon.Web.Hosting;
using Blokemon.Web.Persistence;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
    public async Task AStoreFailureOnATenantRoute_IsAnsweredThroughTheErrorPath_AndIsNotFramable()
    {
        var contexts = new FailingContextFactory();
        await using var host = ChannelHosting.Create(builder =>
            builder.ConfigureServices(contexts.Wrap)
        );
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001", Parent);
        using var client = host.Client();
        (await Headers(client, "/t/alpha")).ContentSecurityPolicy.ShouldBe(
            $"frame-ancestors {Parent}"
        );

        // The store fails while the hosted page's tenant is resolved: the application's own
        // error handling answers, with the framing and baseline headers on that answer.
        contexts.Fail = true;
        using var failed = await client.GetAsync("/t/alpha");
        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        failed.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await failed.Content.ReadAsStringAsync()).ShouldNotBeEmpty();
        failed.Headers.GetValues("Content-Security-Policy").ShouldBe([HostingHeaders.NoneFraming]);
        failed.Headers.Contains("X-Frame-Options").ShouldBeFalse();
        failed.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);

        // A recovered store frames the hosted page for its parent again.
        contexts.Fail = false;
        (await Headers(client, "/t/alpha")).ContentSecurityPolicy.ShouldBe(
            $"frame-ancestors {Parent}"
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

    /// <summary>
    /// The host's own context factory until told to fail, then a store whose every use throws,
    /// as a database that has gone away would.
    /// </summary>
    private sealed class FailingContextFactory : IDbContextFactory<BlokemonDbContext>
    {
        private IDbContextFactory<BlokemonDbContext>? _inner;

        public volatile bool Fail;

        public void Wrap(IServiceCollection services)
        {
            var registered = services.Single(static descriptor =>
                descriptor.ServiceType == typeof(IDbContextFactory<BlokemonDbContext>)
            );
            services.Remove(registered);
            services.AddSingleton<IDbContextFactory<BlokemonDbContext>>(provider =>
            {
                _inner =
                    (IDbContextFactory<BlokemonDbContext>)(
                        registered.ImplementationInstance
                        ?? registered.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(
                            provider,
                            registered.ImplementationType!
                        )
                    );
                return this;
            });
        }

        public BlokemonDbContext CreateDbContext() =>
            Fail
                ? throw new IOException("The database is unreachable.")
                : _inner!.CreateDbContext();

        public Task<BlokemonDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) =>
            Fail
                ? throw new IOException("The database is unreachable.")
                : _inner!.CreateDbContextAsync(cancellationToken);
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
