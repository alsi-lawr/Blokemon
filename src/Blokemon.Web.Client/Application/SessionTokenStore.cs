using System.Net.Http.Headers;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// The bearer token the client currently holds, in memory. The HTTP handler reads it on every
/// API call; the session holder is the only writer.
/// </summary>
public sealed class SessionTokenStore
{
    private volatile string? _token;

    public string? Token
    {
        get => _token;
        set => _token = value;
    }
}

/// <summary>Puts the held session on every API call as an <c>Authorization: Bearer</c> header.</summary>
public sealed class SessionAuthorizationHandler(SessionTokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (tokens.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
