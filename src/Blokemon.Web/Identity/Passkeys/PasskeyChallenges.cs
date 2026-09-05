using System.Collections.Concurrent;
using Blokemon.Product;

namespace Blokemon.Web.Identity.Passkeys;

/// <summary>What a pending ceremony is for, fixed when its options were issued.</summary>
public abstract record CeremonyBinding
{
    private CeremonyBinding() { }

    /// <summary>A registration that creates this account with this display name.</summary>
    public sealed record NewAccount(AccountId Account, string DisplayName) : CeremonyBinding;

    /// <summary>An enrolment onto the account a session names, from that session's provenance.</summary>
    public sealed record Enrolment(AccountId Account, SessionProvenance Provenance, TenantId Tenant)
        : CeremonyBinding;

    /// <summary>A sign-in; the credential names the account.</summary>
    public sealed record Authentication : CeremonyBinding;
}

/// <summary>A ceremony whose options were issued and whose response has not yet arrived.</summary>
public sealed record PendingCeremony(
    CeremonyBinding Binding,
    string OptionsJson,
    DateTimeOffset ExpiresAt
);

/// <summary>
/// The ceremonies in flight, held in memory by challenge. A challenge is taken exactly once, so
/// a response replayed against it is refused, and one not answered within the lifetime is
/// dropped. Nothing here is durable: an unanswered challenge is simply started again.
/// </summary>
public sealed class PasskeyChallenges(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingCeremony> _pending = new(
        StringComparer.Ordinal
    );

    public void Issue(string challenge, CeremonyBinding binding, string optionsJson)
    {
        var now = time.GetUtcNow();
        foreach (var (key, pending) in _pending)
        {
            if (pending.ExpiresAt <= now)
            {
                _pending.TryRemove(key, out _);
            }
        }

        _pending[challenge] = new(binding, optionsJson, now + Lifetime);
    }

    /// <summary>The pending ceremony, removed so it cannot be answered twice; null when unknown or expired.</summary>
    public PendingCeremony? Take(string? challenge) =>
        challenge is { Length: > 0 }
        && _pending.TryRemove(challenge, out var pending)
        && pending.ExpiresAt > time.GetUtcNow()
            ? pending
            : null;
}
