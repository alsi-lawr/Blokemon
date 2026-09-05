using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Web.Identity.Google;

/// <summary>
/// The <c>google</c> provider's names (BLOKEMON-164, D-046): its label, its two routes and the
/// named HTTP client its token exchange uses. The client learns the link from the tenant
/// descriptor and carries none of these.
/// </summary>
public static class GoogleSignIn
{
    public const string ProviderName = "google";

    public const string Label = "Sign in with Google";

    /// <summary>The start route relative to the client's base address, as the descriptor states it.</summary>
    public const string StartPath = "api/session/google/start";

    public const string StartRoute = "/" + StartPath;

    public const string CallbackRoute = "/api/session/google/callback";

    public const string HttpClientName = "google";

    public static readonly IdentityProviderName Name = IdentityProviderName.Create(ProviderName)
        is DomainResult<IdentityProviderName, ExternalIdentityFailure>.Succeeded parsed
        ? parsed.Value
        : throw new InvalidOperationException("The provider name is well formed.");

    /// <summary>The sign-in link the descriptor carries when the deployment enables the provider.</summary>
    public static CoreSignInView[] Links(IdentityProviderRegistry registry, string slug) =>
        registry.IsEnabled(Name)
            ? [new CoreSignInView(Label, $"{StartPath}?slug={Uri.EscapeDataString(slug)}")]
            : [];
}

/// <summary>
/// Where Google's OpenID Connect endpoints are and which issuers its id tokens name. A test
/// host points the endpoints at its own stubs.
/// </summary>
public sealed record GoogleDiscovery(
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    IReadOnlyList<string> Issuers
)
{
    public static readonly GoogleDiscovery Google = new(
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        ["https://accounts.google.com", "accounts.google.com"]
    );
}

public static class GoogleFailures
{
    public static readonly ApiError Unavailable = new(
        "provider.unavailable",
        "That way of signing in is not enabled."
    );

    public static readonly ApiError State = new(
        "google.state",
        "That Google sign-in has expired or was already used. Start again."
    );

    public static readonly ApiError Unreachable = new(
        "google.unreachable",
        "Google could not be reached. Try again."
    );

    public static readonly ApiError Exchange = new(
        "google.exchange",
        "Google did not accept that sign-in."
    );

    public static readonly ApiError Token = new(
        "google.token",
        "Google's answer was not for this sign-in."
    );
}
