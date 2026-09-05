using Blokemon.App;
using Microsoft.AspNetCore.HttpOverrides;

namespace Blokemon.Web.Hosting;

/// <summary>
/// Applies the forwarded client address and scheme only when the connection comes from one of
/// the configured known proxies (BLOKEMON-D-045). With no proxy configured nothing is added
/// and every caller is its connection's address; a request from an unlisted address keeps its
/// connection address whatever headers it carries. Everything that reads
/// <c>Connection.RemoteIpAddress</c> afterwards, the lock-outs through
/// <see cref="Identity.ClientLockouts.ClientOf"/> first among them, sees the resolved address.
/// </summary>
public static class ForwardedClients
{
    public static IApplicationBuilder UseForwardedClients(
        this IApplicationBuilder app,
        HostingConfiguration hosting
    )
    {
        if (!hosting.TrustsForwardedHeaders)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        // The defaults trust the loopback; only the configured list is trusted here.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in hosting.KnownProxies)
        {
            options.KnownIPNetworks.Add(network);
        }

        return app.UseForwardedHeaders(options);
    }
}
