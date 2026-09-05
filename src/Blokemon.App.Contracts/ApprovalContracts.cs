namespace Blokemon.App.Contracts;

/// <summary>A channel waiting for the signed-in person's approval to sign them in there.</summary>
public sealed record PendingApprovalView(string TenantId, string Slug, string Label);

public sealed record ApprovalView(DateTimeOffset ApprovedAt);

/// <summary>
/// A continuation the client opens in a top-level window: the single-use code, the path it is
/// exchanged at (the tenant's continuation route) and when the code expires. The session token
/// itself never travels this way.
/// </summary>
public sealed record ContinuationView(string Code, string Path, DateTimeOffset ExpiresAt);
