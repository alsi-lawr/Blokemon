using Blokemon.App.Contracts;

namespace Blokemon.App.Client;

/// <summary>
/// The session and tenant routes: the descriptor a client learns its tenant from, the
/// exchanges that turn a single-use code into a session, and sign-out. The hand-off exchange
/// route comes from the descriptor, so this client names no provider. An exchange route that
/// is not on this server answers with the typed <c>unavailable</c> outcome.
/// </summary>
public sealed class SessionApiClient(HttpClient http)
{
    /// <summary>The route that exchanges a continuation code opened in a top-level window.</summary>
    public const string ContinuationExchangePath = "api/session/resume";

    private static readonly ApiError UnavailableError = new(
        "unavailable",
        "Signing in this way is not available on this server."
    );

    public Task<ApiResponse<TenantDescriptorView>> Descriptor(
        string slug,
        CancellationToken cancellationToken = default
    ) =>
        ApiEnvelopeTransport.Get<TenantDescriptorView>(
            http,
            $"api/tenant/{Uri.EscapeDataString(slug)}",
            UnavailableError,
            cancellationToken
        );

    /// <summary>Exchanges a code at the given route for the tenant the page runs as (null: the default).</summary>
    public Task<ApiResponse<IssuedSessionView>> Exchange(
        string path,
        string code,
        string? slug = null,
        CancellationToken cancellationToken = default
    ) =>
        ApiEnvelopeTransport.Post<SessionExchangeRequest, IssuedSessionView>(
            http,
            path,
            new(code, slug),
            UnavailableError,
            cancellationToken
        );

    public Task<ApiResponse<SignOutView>> SignOut(CancellationToken cancellationToken = default) =>
        ApiEnvelopeTransport.Post<object, SignOutView>(
            http,
            "api/session/signout",
            new(),
            UnavailableError,
            cancellationToken
        );
}
