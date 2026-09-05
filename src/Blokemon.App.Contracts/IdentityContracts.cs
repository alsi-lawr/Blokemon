namespace Blokemon.App.Contracts;

/// <summary>
/// An external sign-in page the sign-in page offers as a top-level navigation. The label comes
/// from the server with the URL, so the client names no provider.
/// </summary>
public sealed record CoreSignInView(string Label, string Url);

/// <summary>
/// What an unauthenticated client may learn about a tenant: its identity, label, the providers
/// the server enables, the exact origin of the page that may embed it, the core sign-in when
/// one is configured, and the route at which a hand-off code for this tenant is exchanged. The
/// server names that route so the client names no provider. Nothing else about the tenant
/// leaves the server this way.
/// </summary>
public sealed record TenantDescriptorView(
    string Id,
    string Slug,
    string Label,
    string[] EnabledProviders,
    string? RegisteredParentOrigin,
    CoreSignInView? CoreSignIn,
    string HandoffExchangePath
);

/// <summary>A single-use code presented to an exchange endpoint.</summary>
public sealed record SessionExchangeRequest(string Code);

/// <summary>
/// A session as the client receives it, once: the bearer token, when it expires, and the
/// display name of the profile it acts for.
/// </summary>
public sealed record IssuedSessionView(string Token, DateTimeOffset ExpiresAt, string DisplayName);

public sealed record SignOutView(DateTimeOffset RevokedAt);

public sealed record OperatorBootstrapRequest(string Code);

public sealed record OperatorBootstrapView(DateTimeOffset RedeemedAt);
