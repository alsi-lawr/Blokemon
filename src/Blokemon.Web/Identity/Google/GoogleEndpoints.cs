using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Blokemon.App;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Identity.Google;

/// <summary>
/// The Google sign-in's two anonymous routes. <c>GET /api/session/google/start</c> sends the
/// browser to Google with a state, a nonce and a PKCE challenge held here for the answer;
/// <c>GET /api/session/google/callback</c> takes Google's answer, completes the sign-in through
/// the provider and the ordinary completion path, and lands the browser on the continuation
/// page with a single-use code in the fragment, which the client exchanges for its session as
/// a top-level continuation does. Nothing about the token or the code reaches a URL; every
/// refusal lands on the sign-in page saying only that the sign-in did not complete.
/// </summary>
public static class GoogleEndpoints
{
    public const string FailedRoute = "/signin?reason=external";

    public static IEndpointRouteBuilder MapGoogleSignIn(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(GoogleSignIn.StartRoute, Start);
        endpoints.MapGet(GoogleSignIn.CallbackRoute, Callback);
        return endpoints;
    }

    private static IResult Start(
        string? slug,
        HttpContext context,
        IdentityProviderRegistry registry,
        IdentityConfiguration identity,
        GoogleDiscovery discovery,
        GoogleAuthorizations authorizations
    )
    {
        if (
            !registry.IsEnabled(GoogleSignIn.Name)
            || identity.Provider(GoogleSignIn.Name) is not { ClientId: { } clientId }
        )
        {
            return Results.NotFound();
        }

        var state = Random(24);
        var nonce = Random(24);
        var verifier = Random(32);
        var challenge = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
        );
        // The scheme and host are the forwarded ones behind a known proxy, so the redirect URI
        // is the public one Google was registered with.
        var redirectUri =
            $"{context.Request.Scheme}://{context.Request.Host}{GoogleSignIn.CallbackRoute}";
        authorizations.Issue(
            state,
            nonce,
            verifier,
            string.IsNullOrWhiteSpace(slug) ? null : slug.Trim(),
            redirectUri
        );
        var query = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid profile",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account",
            }
        );
        return Results.Redirect(
            discovery.AuthorizationEndpoint.AbsoluteUri + query.ToUriComponent()
        );
    }

    private static async Task<IResult> Callback(
        string? code,
        string? state,
        string? error,
        StateDocumentStore documents,
        SignInServices services,
        IdentityProviderRegistry registry,
        GoogleAuthorizations authorizations,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        // The state is spent whatever Google answered, so a refusal cannot be retried with the
        // same one; an answer for a state this host never issued is refused before anything.
        var pending = authorizations.Take(state);
        if (
            pending is null
            || error is not null
            || string.IsNullOrEmpty(code)
            || !registry.IsEnabled(GoogleSignIn.Name)
        )
        {
            return Results.Redirect(FailedRoute);
        }

        var tenant = await TenantResolution.Resolve(documents, pending.Slug, cancellationToken);
        if (tenant is null)
        {
            return Results.Redirect(FailedRoute);
        }

        var now = time.GetUtcNow();
        var outcome = await SignInCompletion.signIn(
            services,
            registry,
            GoogleSignIn.Name,
            GoogleProvider.Proof(code, pending),
            TenantResolution.IdOf(tenant),
            now,
            cancellationToken
        );
        if (outcome is not DomainResult<IssuedSession, SignInFailure>.Succeeded issued)
        {
            return Results.Redirect(FailedRoute);
        }

        // The browser holds the session the continuation exchange issues, never this one,
        // whose token would otherwise have nowhere safe to travel.
        await HandoffCodes.sweep(documents, documents, now, cancellationToken);
        var handoff = await HandoffCodes.mint(
            documents,
            HandoffBinding.NewContinuation(issued.Value.Session),
            now,
            cancellationToken
        );
        await Sessions.revoke(documents, issued.Value.Session.Id, cancellationToken);
        return Results.Redirect($"/t/{tenant.Slug}/continue#handoff={handoff.Code}");
    }

    private static string Random(int bytes) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
