using System.Net.Http.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace Blokemon.Web.Tests;

/// <summary>
/// BLOKEMON-D-045 on the host: a forwarded client address is applied only when the connection
/// is one of the configured known proxies, the two lock-outs key on the address that results,
/// and an invalid <c>Blokemon:Hosting:KnownProxies</c> value fails start-up naming its key. The
/// hosts run on Kestrel so that every request has a real connection address, the loopback.
/// </summary>
public sealed class ForwardedClientTests
{
    private const string ForwardedFor = "X-Forwarded-For";

    [Test]
    public async Task WithNoKnownProxy_TheForwardedHeaderIsIgnored_AndEveryCallerIsTheConnection()
    {
        await using var host = ChannelHosting.Create(kestrel: true);
        host.Factory.StartServer();
        var guesser = await host.SignIn("guesser");

        // Five failures dressed as five forwarded clients all land on the loopback.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Bootstrap(host, guesser.Token, $"203.0.113.{attempt}")).Error!.Code.ShouldBe(
                "bootstrap.refused"
            );
        }
        (await Bootstrap(host, guesser.Token, "203.0.113.99")).Error!.Code.ShouldBe(
            "bootstrap.locked"
        );
        (await Bootstrap(host, guesser.Token, null)).Error!.Code.ShouldBe("bootstrap.locked");
    }

    [Test]
    public async Task WithTheConnectionAsAKnownProxy_TwoForwardedClientsHoldSeparateLockouts()
    {
        var port = FreePort();
        var origin = $"http://localhost:{port}";
        await using var host = ChannelHosting.Create(
            builder =>
            {
                builder.UseSetting($"{HostingConfigurationModule.KnownProxiesKey}:0", "127.0.0.1");
                builder.UseSetting($"{HostingConfigurationModule.KnownProxiesKey}:1", "::1");
                builder.UseSetting(
                    IdentityConfigurationModule.providerEnabledKey("FirstParty"),
                    "true"
                );
                builder.UseSetting(
                    IdentityConfigurationModule.PasskeysRelyingPartyIdKey,
                    "localhost"
                );
                builder.UseSetting($"{IdentityConfigurationModule.PasskeysOriginsKey}:0", origin);
            },
            kestrel: true,
            kestrelPort: port
        );
        host.Factory.StartServer();
        var guesser = await host.SignIn("guesser");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Bootstrap(host, guesser.Token, "203.0.113.7")).Error!.Code.ShouldBe(
                "bootstrap.refused"
            );
        }
        (await Bootstrap(host, guesser.Token, "203.0.113.7")).Error!.Code.ShouldBe(
            "bootstrap.locked"
        );
        // The other client behind the same proxy, and the proxy's own address, are untouched.
        (await Bootstrap(host, guesser.Token, "203.0.113.8")).Error!.Code.ShouldBe(
            "bootstrap.refused"
        );
        (await Bootstrap(host, guesser.Token, null)).Error!.Code.ShouldBe("bootstrap.refused");

        // The recovery lock-out keys on the same resolved address.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Recover(host, "203.0.113.7")).Error!.Code.ShouldBe("recovery.refused");
        }
        (await Recover(host, "203.0.113.7")).Error!.Code.ShouldBe("recovery.locked");
        (await Recover(host, "203.0.113.8")).Error!.Code.ShouldBe("recovery.refused");
    }

    [Test]
    public async Task WithAKnownProxyThatIsNotTheConnection_TheForwardedHeaderIsIgnored()
    {
        await using var host = ChannelHosting.Create(
            builder =>
                builder.UseSetting($"{HostingConfigurationModule.KnownProxiesKey}:0", "10.0.0.0/8"),
            kestrel: true
        );
        host.Factory.StartServer();
        var guesser = await host.SignIn("guesser");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await Bootstrap(host, guesser.Token, $"203.0.113.{attempt}")).Error!.Code.ShouldBe(
                "bootstrap.refused"
            );
        }
        (await Bootstrap(host, guesser.Token, "203.0.113.99")).Error!.Code.ShouldBe(
            "bootstrap.locked"
        );
    }

    [Test]
    [Arguments("not-an-address")]
    [Arguments("12")]
    [Arguments("10.0.0.0/33")]
    [Arguments("")]
    public async Task AnInvalidKnownProxy_FailsStartUpNamingTheKey(string value)
    {
        await using var host = SessionHost.Create(builder =>
            builder.UseSetting($"{HostingConfigurationModule.KnownProxiesKey}:0", value)
        );

        var failure = Should.Throw<InvalidOperationException>(() => host.Client());

        failure.Message.ShouldContain($"{HostingConfigurationModule.KnownProxiesKey}:0");
    }

    private static async Task<ApiResponse<OperatorBootstrapView>> Bootstrap(
        SessionHost host,
        string token,
        string? forwardedFor
    )
    {
        using var client = host.Client(token);
        if (forwardedFor is not null)
        {
            client.DefaultRequestHeaders.Add(ForwardedFor, forwardedFor);
        }

        using var response = await client.PostAsJsonAsync(
            "/api/operator/bootstrap",
            new OperatorBootstrapRequest("not-the-code")
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OperatorBootstrapView>>())!;
    }

    private static async Task<ApiResponse<IssuedSessionView>> Recover(
        SessionHost host,
        string forwardedFor
    )
    {
        using var client = host.Client();
        client.DefaultRequestHeaders.Add(ForwardedFor, forwardedFor);
        using var response = await client.PostAsJsonAsync(
            "/api/session/firstparty/recover",
            new RecoveryRequest("not-a-code", Tenants.DefaultSlug.Value)
        );
        return (await response.Content.ReadFromJsonAsync<ApiResponse<IssuedSessionView>>())!;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
