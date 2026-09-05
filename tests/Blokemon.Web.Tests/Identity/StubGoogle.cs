using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using Blokemon.App;
using Blokemon.Web.Identity.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blokemon.Web.Tests.Identity;

/// <summary>
/// Google's two endpoints as a test host stands them in: the token endpoint as the named HTTP
/// client's handler, answering a code with an id token for one subject, and the authorization
/// endpoint as a route on the host itself that sends the browser straight back with a code. The
/// nonce the authorization request carried is what the id token repeats, as Google's does.
/// </summary>
internal sealed class StubGoogle : HttpMessageHandler
{
    public const string ClientId = "stub-client.apps.googleusercontent.com";
    public const string ClientSecret = "stub-client-secret";
    public const string Code = "stub-authorization-code";
    public const string AuthorizePath = "/stub-google/authorize";

    public string Subject { get; set; } = "1234567890";

    public string? DisplayName { get; set; } = "Googly Player";

    public string Issuer { get; set; } = "https://accounts.google.com";

    public string Audience { get; set; } = ClientId;

    /// <summary>The nonce the id token carries; null repeats the one the authorization request sent.</summary>
    public string? NonceOverride { get; set; }

    public string? LastNonce { get; set; }

    public string? LastState { get; set; }

    public TimeSpan Validity { get; set; } = TimeSpan.FromMinutes(5);

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public List<Dictionary<string, string>> TokenRequests { get; } = [];

    /// <summary>The host's settings and services for the provider against this stub.</summary>
    public void Configure(IWebHostBuilder builder, string? authorizeOrigin = null)
    {
        builder.UseSetting(IdentityConfigurationModule.providerEnabledKey("Google"), "true");
        builder.UseSetting(IdentityConfigurationModule.providerClientIdKey("Google"), ClientId);
        builder.UseSetting(
            IdentityConfigurationModule.providerClientSecretKey("Google"),
            ClientSecret
        );
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(this);
            services.RemoveAll<GoogleDiscovery>();
            services.AddSingleton(
                new GoogleDiscovery(
                    authorizeOrigin is null
                        ? GoogleDiscovery.Google.AuthorizationEndpoint
                        : new Uri($"{authorizeOrigin}{AuthorizePath}"),
                    new Uri("https://stub.google.test/token"),
                    GoogleDiscovery.Google.Issuers
                )
            );
            services
                .AddHttpClient(GoogleSignIn.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => this);
            services.AddTransient<IStartupFilter, Authorize>();
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var form = await request.Content!.ReadAsStringAsync(cancellationToken);
        var fields = form.Split('&')
            .Select(static pair => pair.Split('=', 2))
            .ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : ""),
                StringComparer.Ordinal
            );
        TokenRequests.Add(fields);
        if (Status != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(
                    "{\"error\":\"invalid_grant\"}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }

        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = Subject,
            ["nonce"] = NonceOverride ?? LastNonce,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + Validity).ToUnixTimeSeconds(),
        };
        if (DisplayName is not null)
        {
            payload["name"] = DisplayName;
        }

        var token = string.Join(
            ".",
            Segment("{\"alg\":\"RS256\",\"kid\":\"stub\",\"typ\":\"JWT\"}"),
            Segment(JsonSerializer.Serialize(payload)),
            Segment("stub-signature")
        );
        var answer = JsonSerializer.Serialize(
            new
            {
                access_token = "stub-access-token",
                id_token = token,
                token_type = "Bearer",
                expires_in = 3600,
            }
        );
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(answer, Encoding.UTF8, "application/json"),
        };
    }

    private static string Segment(string text) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    /// <summary>The authorization endpoint on the host: remembers the nonce and state, answers with the code.</summary>
    private sealed class Authorize(StubGoogle stub) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Map(
                    AuthorizePath,
                    branch =>
                        branch.Run(context =>
                        {
                            var query = context.Request.Query;
                            stub.LastNonce = query["nonce"];
                            stub.LastState = query["state"];
                            var redirect = query["redirect_uri"].ToString();
                            context.Response.Redirect(
                                $"{redirect}?code={Uri.EscapeDataString(Code)}&state={Uri.EscapeDataString(query["state"].ToString())}"
                            );
                            return Task.CompletedTask;
                        })
                );
                next(app);
            };
    }
}
