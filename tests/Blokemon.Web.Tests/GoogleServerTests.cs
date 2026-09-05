using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Identity.Google;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;

namespace Blokemon.Web.Tests;

/// <summary>
/// The Google sign-in over HTTP (BLOKEMON-164) against the stub of Google's two endpoints: the
/// redirect out, the callback in, the continuation code the browser is landed with, and the
/// refusals of everything that is not Google's answer to this host's own request.
/// </summary>
public sealed class GoogleServerTests
{
    private static (SessionHost Host, StubGoogle Stub) GoogleHost()
    {
        var stub = new StubGoogle();
        var host = SessionHost.Create(builder => stub.Configure(builder));
        return (host, stub);
    }

    private static HttpClient Plain(SessionHost host) =>
        host.Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

    [Test]
    public async Task Start_SendsTheBrowserToGoogle_AndTheCallbackLandsAContinuationThatSignsIn()
    {
        var (host, stub) = GoogleHost();
        await using var _ = host;
        using var client = Plain(host);

        var start = await client.GetAsync("/api/session/google/start");
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = start.Headers.Location!;
        location.Host.ShouldBe("accounts.google.com");
        var query = QueryHelpers.ParseQuery(location.Query);
        query["client_id"].ToString().ShouldBe(StubGoogle.ClientId);
        query["redirect_uri"].ToString().ShouldBe("http://localhost/api/session/google/callback");
        query["response_type"].ToString().ShouldBe("code");
        query["scope"].ToString().ShouldContain("openid");
        query["code_challenge_method"].ToString().ShouldBe("S256");
        var state = query["state"].ToString();
        var nonce = query["nonce"].ToString();
        state.Length.ShouldBeGreaterThanOrEqualTo(32);
        nonce.Length.ShouldBeGreaterThanOrEqualTo(32);
        stub.LastNonce = nonce;

        var callback = await client.GetAsync(
            $"/api/session/google/callback?code={StubGoogle.Code}&state={state}"
        );
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var landing = callback.Headers.Location!.ToString();
        landing.ShouldStartWith("/t/core/continue#handoff=");
        landing.ShouldNotContain("id_token");
        landing.ShouldNotContain("stub-access-token");
        landing.ShouldNotContain(StubGoogle.Code);
        // The exchange presented the code with the secret and the verifier of the challenge.
        var exchange = stub.TokenRequests.ShouldHaveSingleItem();
        exchange["code"].ShouldBe(StubGoogle.Code);
        exchange["client_id"].ShouldBe(StubGoogle.ClientId);
        exchange["client_secret"].ShouldBe(StubGoogle.ClientSecret);
        exchange["grant_type"].ShouldBe("authorization_code");
        exchange["redirect_uri"].ShouldBe("http://localhost/api/session/google/callback");
        Base64Url
            .EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"])))
            .ShouldBe(query["code_challenge"].ToString());

        var signedIn = await Resume(host, landing);
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        signedIn.Value!.DisplayName.ShouldBe("Googly Player");
        var account = await PasskeyServerTests.AccountOf(host, signedIn.Value.Token);
        var keys = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ToList();
        keys.ShouldContain($"link/google/{stub.Subject}");
        keys.ShouldContain($"account/{account}");
        keys.ShouldContain($"a/{account}/profile");
        // The callback's own session was revoked: the browser holds the continuation's only.
        (await host.WithStore(store => store.List("session/"))).Count.ShouldBe(1);
        var session = await host.WithStore(store =>
            Sessions.validate(store, signedIn.Value.Token, DateTimeOffset.UtcNow, default)
        );
        ((SessionValidation.Valid)session).Item.Provenance.ShouldBe(SessionProvenance.FirstParty);

        // A returning subject reaches the same account with a fresh code.
        var again = await SignInThroughGoogle(host, stub);
        again.ShouldNotBe(landing);
        var returning = await Resume(host, again);
        (await PasskeyServerTests.AccountOf(host, returning.Value!.Token)).ShouldBe(account);
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(1);

        // The descriptor offers the link with the tenant's slug.
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );
        descriptor!.Value!.EnabledProviders.ShouldContain("google");
        descriptor.Value.SignInLinks.ShouldBe([
            new CoreSignInView("Sign in with Google", "api/session/google/start?slug=core"),
        ]);
    }

    [Test]
    public async Task Callback_RefusesWhatIsNotGooglesAnswerToThisHost_AndLandsOnTheSignInPage()
    {
        var (host, stub) = GoogleHost();
        await using var _ = host;
        using var client = Plain(host);
        var before = await host.WithStore(store => store.List(""));

        async Task Refused(string path, string why)
        {
            var response = await client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect, why);
            response.Headers.Location!.ToString().ShouldBe(GoogleEndpoints.FailedRoute, why);
            (await host.WithStore(store => store.List(""))).ShouldBe(before, why);
        }

        // A state this host never issued, and no state at all.
        await Refused(
            $"/api/session/google/callback?code={StubGoogle.Code}&state=never-issued",
            "unknown state"
        );
        await Refused($"/api/session/google/callback?code={StubGoogle.Code}", "no state");

        // Google's own refusal spends the state.
        var declined = await Start(client, stub);
        await Refused(
            $"/api/session/google/callback?error=access_denied&state={declined}",
            "access denied"
        );
        await Refused(
            $"/api/session/google/callback?code={StubGoogle.Code}&state={declined}",
            "the spent state"
        );

        // A token for another nonce, another audience, another issuer, or past its expiry.
        foreach (
            var (arrange, why) in new (Action, string)[]
            {
                (() => stub.NonceOverride = "another-nonce", "wrong nonce"),
                (() => stub.Audience = "another-client", "wrong audience"),
                (() => stub.Issuer = "https://accounts.example", "wrong issuer"),
                (() => stub.Validity = TimeSpan.FromMinutes(-1), "expired"),
                (() => stub.Status = HttpStatusCode.BadRequest, "the exchange refused"),
                (() => stub.Subject = "not a subject!", "a malformed subject"),
            }
        )
        {
            var fresh = new StubGoogle();
            stub.NonceOverride = fresh.NonceOverride;
            stub.Audience = fresh.Audience;
            stub.Issuer = fresh.Issuer;
            stub.Validity = fresh.Validity;
            stub.Status = fresh.Status;
            stub.Subject = fresh.Subject;
            arrange();
            var state = await Start(client, stub);
            await Refused(
                $"/api/session/google/callback?code={StubGoogle.Code}&state={state}",
                why
            );
        }

        // A code answered once is not answered twice: the state went with the first answer.
        stub.NonceOverride = null;
        stub.Audience = StubGoogle.ClientId;
        stub.Issuer = "https://accounts.google.com";
        stub.Validity = TimeSpan.FromMinutes(5);
        stub.Status = HttpStatusCode.OK;
        stub.Subject = "1234567890";
        var good = await Start(client, stub);
        var first = await client.GetAsync(
            $"/api/session/google/callback?code={StubGoogle.Code}&state={good}"
        );
        first.Headers.Location!.ToString().ShouldStartWith("/t/core/continue#handoff=");
        var replay = await client.GetAsync(
            $"/api/session/google/callback?code={StubGoogle.Code}&state={good}"
        );
        replay.Headers.Location!.ToString().ShouldBe(GoogleEndpoints.FailedRoute);
    }

    [Test]
    public async Task Unconfigured_ShowsNothing_AndEnabledWithoutItsClientFailsStartUp()
    {
        await using var host = SessionHost.Create();
        using var client = Plain(host);

        (await client.GetAsync("/api/session/google/start")).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (
            await client.GetAsync(
                $"/api/session/google/callback?code={StubGoogle.Code}&state=whatever"
            )
        )
            .Headers.Location!.ToString()
            .ShouldBe(GoogleEndpoints.FailedRoute);
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );
        descriptor!.Value!.EnabledProviders.ShouldNotContain("google");
        (descriptor.Value.SignInLinks ?? []).ShouldBeEmpty();

        await using var halfConfigured = SessionHost.Create(builder =>
        {
            builder.UseSetting(IdentityConfigurationModule.providerEnabledKey("Google"), "true");
            builder.UseSetting(
                IdentityConfigurationModule.providerClientIdKey("Google"),
                StubGoogle.ClientId
            );
        });
        Should
            .Throw<Exception>(() => halfConfigured.Client())
            .Message.ShouldContain("Blokemon:Identity:Providers:Google:ClientSecret");
        await using var enabledWithoutClient = SessionHost.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.providerEnabledKey("Google"), "true")
        );
        Should
            .Throw<Exception>(() => enabledWithoutClient.Client())
            .Message.ShouldContain("Blokemon:Identity:Providers:Google:ClientId");
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Starts a sign-in and tells the stub the nonce Google would have been given; the state.</summary>
    private static async Task<string> Start(HttpClient client, StubGoogle stub)
    {
        var start = await client.GetAsync("/api/session/google/start");
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        stub.LastNonce = query["nonce"].ToString();
        return query["state"].ToString();
    }

    /// <summary>A whole round: the landing location the callback answered with.</summary>
    private static async Task<string> SignInThroughGoogle(SessionHost host, StubGoogle stub)
    {
        using var client = Plain(host);
        var state = await Start(client, stub);
        var callback = await client.GetAsync(
            $"/api/session/google/callback?code={StubGoogle.Code}&state={state}"
        );
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return callback.Headers.Location!.ToString();
    }

    /// <summary>Exchanges the continuation code from a landing location, as the client does.</summary>
    private static async Task<ApiResponse<IssuedSessionView>> Resume(
        SessionHost host,
        string landing
    )
    {
        var code = landing[(landing.IndexOf("#handoff=", StringComparison.Ordinal) + 9)..];
        using var client = host.Client();
        return await PasskeyServerTests.Post<IssuedSessionView>(
            client,
            "/api/session/resume",
            new SessionExchangeRequest(code)
        );
    }
}
