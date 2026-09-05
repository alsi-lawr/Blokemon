namespace Blokemon.App.Contracts;

// The operator's admission of tenants, as the operator page and the federation's routes share
// it. Provider-neutral: the broadcaster is the channel's subject at whichever provider the
// federation links under.

/// <summary>What an operator states to admit a channel, or the default tenant's core issuer.</summary>
public sealed record TenantAdmissionRequest(
    string? Slug,
    string? Label,
    string? BroadcasterSubject,
    string? ParentOrigin
);

/// <summary>
/// A tenant as admission or rotation leaves it, with its integration token: the one time the
/// token exists in clear.
/// </summary>
public sealed record AdmittedTenantView(
    string Id,
    string Slug,
    string Label,
    string Status,
    string Token,
    DateTimeOffset IssuedAt
);

/// <summary>A tenant's lifecycle state after an operator's or the channel's own closure or revocation.</summary>
public sealed record TenantStatusView(string Id, string Slug, string Status);
