using System.Text.RegularExpressions;
using Blokemon.App;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Persistence;
using Microsoft.Net.Http.Headers;

namespace Blokemon.Web.Hosting;

/// <summary>
/// The response headers the site is served with, decided per request before the response
/// starts. Framing: <c>frame-ancestors</c> names exactly the registered parent origin of an
/// admitted tenant on its hosted page <c>/t/{slug}</c>, and is <c>'none'</c> on the tenant's
/// continuation window, on an unknown slug and on every other response, so no page but the
/// hosted one can be framed and the hosted one only by its registered embedder. Caching: every
/// response under <c>/t/</c> is <c>no-store</c>, so the per-tenant header can never be reused
/// for another tenant by any cache; every other response that has no caching decision of its
/// own is <c>no-cache</c>, matching the static site's revalidation rule; a fingerprinted
/// framework asset is immutable, matching its content-addressed name. The two baseline headers
/// the neighbouring site sends and the client is indifferent to come with every response.
/// </summary>
public sealed partial class HostingHeaders(RequestDelegate next)
{
    public const string NoneFraming = "frame-ancestors 'none'";

    public const string NoStore = "no-store";

    public const string NoCache = "no-cache";

    public const string Immutable = "public, max-age=31536000, immutable";

    private static readonly PathString TenantPrefix = new("/t");

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var framing = await FramingOf(context, path);
        var hosted = path.StartsWithSegments(TenantPrefix);
        var fingerprinted = FingerprintedFrameworkAsset().IsMatch(path.Value ?? string.Empty);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.ContentSecurityPolicy] = framing;
            headers.Remove(HeaderNames.XFrameOptions);
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            if (hosted)
            {
                headers[HeaderNames.CacheControl] = NoStore;
                headers.Remove(HeaderNames.Pragma);
            }
            else if (fingerprinted)
            {
                headers[HeaderNames.CacheControl] = Immutable;
            }
            else if (!headers.ContainsKey(HeaderNames.CacheControl))
            {
                headers[HeaderNames.CacheControl] = NoCache;
            }

            return Task.CompletedTask;
        });
        await next(context);
    }

    /// <summary>
    /// The hosted page of an admitted tenant with a registered parent origin is framable by
    /// that origin and nothing else is framable at all. The slug is the second segment of
    /// exactly two, so <c>/t/{slug}/continue</c> never resolves a tenant.
    /// </summary>
    private static async Task<string> FramingOf(HttpContext context, PathString path)
    {
        if (
            !path.StartsWithSegments(TenantPrefix, out var remaining)
            || remaining.Value is not { Length: > 1 } slugPath
            || slugPath.IndexOf('/', 1) >= 0
        )
        {
            return NoneFraming;
        }

        var documents = context.RequestServices.GetRequiredService<StateDocumentStore>();
        var tenant = await TenantResolution.Resolve(
            documents,
            slugPath[1..],
            context.RequestAborted
        );
        return tenant is { Status: TenantStatus.Active, RegisteredParentOrigin: { } origin }
            ? $"frame-ancestors {origin}"
            : NoneFraming;
    }

    // The static site's rule: a framework file whose name carries a content hash. The entry
    // points dotnet.js and blazor.web.js carry none and stay revalidated.
    [GeneratedRegex(@"^/_framework/.+\.[a-z0-9]{8,12}\.(wasm|js|dat|pdb)$")]
    private static partial Regex FingerprintedFrameworkAsset();
}

public static class HostingHeadersExtensions
{
    public static IApplicationBuilder UseHostingHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<HostingHeaders>();
}
