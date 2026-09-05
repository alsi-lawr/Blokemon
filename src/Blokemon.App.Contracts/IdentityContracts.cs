namespace Blokemon.App.Contracts;

/// <summary>
/// An external sign-in page the sign-in page offers as a top-level navigation. The label comes
/// from the server with the URL, so the client names no provider.
/// </summary>
public sealed record CoreSignInView(string Label, string Url);

/// <summary>
/// What an unauthenticated client may learn about a tenant: its identity, label, the providers
/// the server enables, the exact origin of the page that may embed it, the core sign-in when
/// one is configured, the route at which a hand-off code for this tenant is exchanged, and
/// whether the first-party provider offers passkey ceremonies beside the simple login. The
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
    string HandoffExchangePath,
    bool Passkeys = false
);

/// <summary>
/// A single-use code presented to an exchange endpoint, with the slug of the tenant the page
/// is running as (null at the root, the default tenant) so the exchange can refuse a code
/// bound to another tenant.
/// </summary>
public sealed record SessionExchangeRequest(string Code, string? Slug = null);

/// <summary>
/// A session as the client receives it, once: the bearer token, when it expires, the display
/// name of the profile it acts for, and whether it is a recovery session that can only enrol a
/// replacement passkey.
/// </summary>
public sealed record IssuedSessionView(
    string Token,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    bool Recovery = false
);

public sealed record SignOutView(DateTimeOffset RevokedAt);

public sealed record OperatorBootstrapRequest(string Code);

public sealed record OperatorBootstrapView(DateTimeOffset RedeemedAt);
