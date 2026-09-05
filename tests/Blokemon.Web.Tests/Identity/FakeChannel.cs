using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace Blokemon.Web.Tests.Identity;

/// <summary>
/// A BlokeBot channel as the server sees one: a client holding an integration token that
/// calls the channel routes server-to-server, exactly as the plugin would. It mints the happy
/// path and every refused case the tests ask for; a token of null sends no Authorization
/// header at all. Test assembly only.
/// </summary>
internal sealed class FakeChannel(SessionHost host, string? token) : IDisposable
{
    private readonly HttpClient _client = Client(host, token);

    public string? Token { get; } = token;

    public Task<ApiResponse<TenantDescriptorView>> Self() =>
        Get<TenantDescriptorView>("/api/tenant/self");

    public Task<ApiResponse<HandoffCodeView>> Handoff(
        string? twitchUserId,
        string? login = null,
        string? displayName = null
    ) =>
        Post<HandoffCodeView>(
            "/api/tenant/handoff",
            new HandoffRequest(twitchUserId, login, displayName)
        );

    /// <summary>A hand-off that must succeed: the code, ready to exchange.</summary>
    public async Task<string> HandoffCode(string twitchUserId, string? displayName = null)
    {
        var response = await Handoff(twitchUserId, displayName: displayName);
        response.Succeeded.ShouldBeTrue(response.Error?.Message);
        return response.Value!.Code;
    }

    public Task<ApiResponse<ErasureView>> Erasure(string? twitchUserId) =>
        Post<ErasureView>("/api/tenant/erasure", new ErasureRequest(twitchUserId));

    public Task<ApiResponse<TenantStatusView>> Close() =>
        Post<TenantStatusView>("/api/tenant/close", new { });

    /// <summary>A call whose body is whatever the test says, for the malformed cases.</summary>
    public async Task<HttpResponseMessage> PostRaw(string path, string body)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return await _client.PostAsync(path, content);
    }

    private async Task<ApiResponse<T>> Get<T>(string path) =>
        (await _client.GetFromJsonAsync<ApiResponse<T>>(path))!;

    private async Task<ApiResponse<T>> Post<T>(string path, object body)
    {
        using var content = JsonContent.Create(body, body.GetType());
        using var response = await _client.PostAsync(path, content);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!;
    }

    private static HttpClient Client(SessionHost host, string? token)
    {
        var client = host.Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token
            );
        }

        return client;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>The host as the federation tests need it, and the operator who admits channels.</summary>
internal static class ChannelHosting
{
    public const string BootstrapCode = "the-operator-bootstrap-code";

    /// <summary>A host with the BlokeBot provider enabled and an operator bootstrap code configured.</summary>
    public static SessionHost Create(
        Action<IWebHostBuilder>? configure = null,
        bool kestrel = false,
        int kestrelPort = 0
    ) =>
        SessionHost.Create(
            builder =>
            {
                builder.UseSetting(
                    IdentityConfigurationModule.providerEnabledKey("BlokeBot"),
                    "true"
                );
                builder.UseSetting(
                    IdentityConfigurationModule.OperatorBootstrapCodeKey,
                    BootstrapCode
                );
                configure?.Invoke(builder);
            },
            kestrel: kestrel,
            kestrelPort: kestrelPort
        );

    /// <summary>Signs a first-party operator in through the bootstrap and returns their token.</summary>
    public static async Task<string> OperatorToken(
        this SessionHost host,
        string subject = "operator"
    )
    {
        var signedIn = await host.SignIn(subject, "Operator");
        using var client = host.Client(signedIn.Token);
        using var response = await client.PostAsJsonAsync(
            "/api/operator/bootstrap",
            new OperatorBootstrapRequest(BootstrapCode)
        );
        var envelope = await response.Content.ReadFromJsonAsync<
            ApiResponse<OperatorBootstrapView>
        >();
        if (!envelope!.Succeeded && envelope.Error?.Code != "bootstrap.redeemed")
        {
            throw new InvalidOperationException($"Bootstrap failed: {envelope.Error?.Message}");
        }

        return signedIn.Token;
    }

    public static async Task<ApiResponse<AdmittedTenantView>> Admit(
        this SessionHost host,
        string operatorToken,
        string? slug,
        string? label,
        string? broadcaster = null,
        string? parentOrigin = null
    )
    {
        using var client = host.Client(operatorToken);
        using var response = await client.PostAsJsonAsync(
            "/api/operator/tenants",
            new TenantAdmissionRequest(slug, label, broadcaster, parentOrigin)
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AdmittedTenantView>>())!;
    }

    /// <summary>Admits a channel that must succeed and returns it as a fake channel.</summary>
    public static async Task<(FakeChannel Channel, AdmittedTenantView Tenant)> AdmitChannel(
        this SessionHost host,
        string operatorToken,
        string slug,
        string label,
        string? broadcaster = "1001",
        string? parentOrigin = "https://parent.example"
    )
    {
        var admitted = await host.Admit(operatorToken, slug, label, broadcaster, parentOrigin);
        admitted.Succeeded.ShouldBeTrue(admitted.Error?.Message);
        return (new FakeChannel(host, admitted.Value!.Token), admitted.Value);
    }

    public static async Task<ApiResponse<T>> Operator<T>(
        this SessionHost host,
        string operatorToken,
        string path
    )
    {
        using var client = host.Client(operatorToken);
        using var response = await client.PostAsJsonAsync(path, new { });
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!;
    }

    /// <summary>Exchanges a hand-off code as the page running as the given slug (null: the root).</summary>
    public static async Task<ApiResponse<IssuedSessionView>> Exchange(
        this SessionHost host,
        string? code,
        string? slug,
        string path = "/api/session/blokebot"
    )
    {
        using var client = host.Client();
        using var response = await client.PostAsJsonAsync(
            path,
            new SessionExchangeRequest(code!, slug)
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<IssuedSessionView>>())!;
    }

    public static async Task<Session> SessionOf(this SessionHost host, string token)
    {
        var validation = await host.WithStore(store =>
            Sessions.validate(store, token, DateTimeOffset.UtcNow, default)
        );
        return ((SessionValidation.Valid)validation).Item;
    }
}
