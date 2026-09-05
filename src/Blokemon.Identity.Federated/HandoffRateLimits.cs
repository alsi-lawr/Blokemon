using Blokemon.App;

namespace Blokemon.Identity.Federated;

/// <summary>
/// The per-token hand-off rate limit: <c>Blokemon:Identity:Handoff:RateLimitPerMinute</c>
/// mints per minute, keyed by the token (the tenant and the moment its verifier was issued,
/// so a rotated token starts its own count).
/// </summary>
public sealed class HandoffRateLimits(IdentityConfiguration identity)
{
    private readonly RateLimiter _limiter = RateLimiter.PerMinute(
        identity.HandoffRateLimitPerMinute
    );

    public bool Allow(TenantDocument tenant, DateTimeOffset now) =>
        _limiter.Allow(
            $"{tenant.Id}:{tenant.IntegrationTokenVerifier?.IssuedAt.UtcTicks ?? 0}",
            now
        );
}
