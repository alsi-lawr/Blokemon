using System.Collections.Concurrent;

namespace Blokemon.Web.Identity.Google;

/// <summary>
/// One authorization sent to Google and not yet answered: the nonce its id token must carry,
/// the PKCE verifier its code exchange presents, the tenant the page ran as, and the redirect
/// URI the exchange must repeat.
/// </summary>
public sealed record PendingAuthorization(
    string Nonce,
    string CodeVerifier,
    string? Slug,
    string RedirectUri,
    DateTimeOffset ExpiresAt
);

/// <summary>
/// The authorizations in flight, held in memory by state. A state is taken exactly once, so a
/// callback replayed against it is refused, and one not answered within the lifetime is
/// dropped. Nothing here is durable: an unanswered authorization is simply started again.
/// </summary>
public sealed class GoogleAuthorizations(TimeProvider time)
{
    /// <summary>
    /// Long enough for a person to pick an account and pass a second factor on a phone; the
    /// authorization code itself is single-use and short-lived at Google's end.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new(
        StringComparer.Ordinal
    );

    public PendingAuthorization Issue(
        string state,
        string nonce,
        string codeVerifier,
        string? slug,
        string redirectUri
    )
    {
        var now = time.GetUtcNow();
        foreach (var (key, pending) in _pending)
        {
            if (pending.ExpiresAt <= now)
            {
                _pending.TryRemove(key, out _);
            }
        }

        var issued = new PendingAuthorization(
            nonce,
            codeVerifier,
            slug,
            redirectUri,
            now + Lifetime
        );
        _pending[state] = issued;
        return issued;
    }

    /// <summary>The pending authorization, removed so it cannot be answered twice; null when unknown or expired.</summary>
    public PendingAuthorization? Take(string? state) =>
        state is { Length: > 0 }
        && _pending.TryRemove(state, out var pending)
        && pending.ExpiresAt > time.GetUtcNow()
            ? pending
            : null;
}
