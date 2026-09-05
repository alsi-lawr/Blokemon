namespace Blokemon.Identity.Federated;

/// <summary>What an operator states to admit a channel, or the default tenant's core issuer.</summary>
public sealed record TenantAdmissionRequest(
    string? Slug,
    string? Label,
    string? BroadcasterTwitchUserId,
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

/// <summary>What a channel states to hand a viewer off: the Twitch user id, and hints.</summary>
public sealed record HandoffRequest(string? TwitchUserId, string? Login, string? DisplayName);

/// <summary>A hand-off code, once: single-use, bound to the channel and the viewer, sixty seconds.</summary>
public sealed record HandoffCodeView(string Code, DateTimeOffset ExpiresAt);

/// <summary>The channel's relay of a viewer's erasure.</summary>
public sealed record ErasureRequest(string? TwitchUserId);

/// <summary>Whether the relay changed anything; a subject the channel held no approval for is a no-op.</summary>
public sealed record ErasureView(bool Dissociated);
