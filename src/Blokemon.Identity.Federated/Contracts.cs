namespace Blokemon.Identity.Federated;

// The channel's own calls. The operator's admission contracts are Blokemon.App.Contracts'
// (TenantAdmissionRequest, AdmittedTenantView, TenantStatusView), shared with the operator page.

/// <summary>What a channel states to hand a viewer off: the Twitch user id, and hints.</summary>
public sealed record HandoffRequest(string? TwitchUserId, string? Login, string? DisplayName);

/// <summary>A hand-off code, once: single-use, bound to the channel and the viewer, sixty seconds.</summary>
public sealed record HandoffCodeView(string Code, DateTimeOffset ExpiresAt);

/// <summary>The channel's relay of a viewer's erasure.</summary>
public sealed record ErasureRequest(string? TwitchUserId);

/// <summary>Whether the relay changed anything; a subject the channel held no approval for is a no-op.</summary>
public sealed record ErasureView(bool Dissociated);
