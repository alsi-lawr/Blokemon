using System.Net.Http.Headers;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.AspNetCore.Http;

namespace Blokemon.Identity.Federated;

/// <summary>
/// What every channel call shares: the integration token from <c>Authorization: Bearer</c>,
/// authenticated against the tenant it names, and the typed refusals for a call with no
/// token, an unknown token, or a closed or revoked tenant. The session middleware ignores the
/// token on these routes, which are anonymous to it; this is their authentication.
/// </summary>
internal static class ChannelCalls
{
    public static readonly ApiError NoToken = new(
        "channel.token_required",
        "A channel call carries its integration token as a bearer token."
    );

    public static readonly ApiError Unknown = new(
        "channel.token_unknown",
        "That integration token is not one this server issued."
    );

    public static readonly ApiError Closed = new("tenant.closed", "That channel has closed.");

    public static readonly ApiError Revoked = new("tenant.revoked", "That channel was revoked.");

    /// <summary>The authenticated tenant, or the typed refusal.</summary>
    public static async Task<DomainResult<TenantDocument, ApiError>> Authenticate(
        HttpContext context,
        IStateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var outcome = await TenantAdmission.authenticate(
            documents,
            BearerToken(context.Request),
            cancellationToken
        );
        return outcome switch
        {
            ChannelAuthentication.Authenticated authenticated => DomainResult<
                TenantDocument,
                ApiError
            >.NewSucceeded(authenticated.Item),
            { IsNoToken: true } => DomainResult<TenantDocument, ApiError>.NewFailed(NoToken),
            { IsClosed: true } => DomainResult<TenantDocument, ApiError>.NewFailed(Closed),
            { IsRevoked: true } => DomainResult<TenantDocument, ApiError>.NewFailed(Revoked),
            _ => DomainResult<TenantDocument, ApiError>.NewFailed(Unknown),
        };
    }

    public static ApiResponse<T> Ok<T>(T value) => new(true, value, null);

    public static ApiResponse<T> Fail<T>(ApiError error) => new(false, default, error);

    private static string? BearerToken(HttpRequest request)
    {
        if (request.Headers.Authorization.Count != 1)
        {
            return null;
        }

        return
            AuthenticationHeaderValue.TryParse(request.Headers.Authorization[0], out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(header.Parameter)
            ? header.Parameter
            : null;
    }
}
