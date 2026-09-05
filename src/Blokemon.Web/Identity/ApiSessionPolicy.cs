using Blokemon.Identity.Federated;
using Microsoft.AspNetCore.Routing.Template;

namespace Blokemon.Web.Identity;

/// <summary>
/// The routes under <c>/api</c> that need no session, enumerated exactly. Everything else under
/// the prefix requires a session, whether or not a route exists there yet, so a route a later
/// ticket adds is protected before it is written. <c>/healthz</c> lies outside the prefix.
/// </summary>
public static class ApiSessionPolicy
{
    /// <summary>Method and route template of every anonymous route.</summary>
    public static readonly IReadOnlyList<(string Method, string Template)> Anonymous =
    [
        // The signed-out view.
        ("GET", "/api/state"),
        // The tenant descriptor.
        ("GET", "/api/tenant/{slug}"),
        // The first-party sign-in ceremonies (BLOKEMON-150): each ceremony is its options and
        // its response.
        ("POST", "/api/session/firstparty/register/options"),
        ("POST", "/api/session/firstparty/register"),
        ("POST", "/api/session/firstparty/authenticate/options"),
        ("POST", "/api/session/firstparty/authenticate"),
        ("POST", "/api/session/firstparty/recover"),
        // The hand-off and continuation exchanges.
        ("POST", HandoffExchange.Route),
        ("POST", "/api/session/resume"),
        // The channel endpoints, authenticated by integration token.
        ("GET", "/api/tenant/self"),
        ("POST", "/api/tenant/handoff"),
        ("POST", "/api/tenant/erasure"),
        ("POST", "/api/tenant/close"),
    ];

    /// <summary>
    /// The one operation a <c>Recovery</c> session may perform: enrolling the replacement
    /// passkey, which answers with the new code set. Every other route refuses it.
    /// </summary>
    public static readonly IReadOnlyList<(string Method, string Template)> RecoveryPermitted =
    [
        ("POST", "/api/session/firstparty/enrol/options"),
        ("POST", "/api/session/firstparty/enrol"),
    ];

    private static readonly (string Method, TemplateMatcher Matcher)[] AnonymousMatchers = Matchers(
        Anonymous
    );

    private static readonly (string Method, TemplateMatcher Matcher)[] RecoveryMatchers = Matchers(
        RecoveryPermitted
    );

    public static bool IsAnonymous(string method, PathString path) =>
        Matches(AnonymousMatchers, method, path);

    public static bool IsRecoveryPermitted(string method, PathString path) =>
        Matches(RecoveryMatchers, method, path);

    private static (string Method, TemplateMatcher Matcher)[] Matchers(
        IEnumerable<(string Method, string Template)> routes
    ) =>
        routes
            .Select(route =>
                (route.Method, new TemplateMatcher(TemplateParser.Parse(route.Template), []))
            )
            .ToArray();

    private static bool Matches(
        (string Method, TemplateMatcher Matcher)[] matchers,
        string method,
        PathString path
    )
    {
        foreach (var (routeMethod, matcher) in matchers)
        {
            if (
                string.Equals(routeMethod, method, StringComparison.OrdinalIgnoreCase)
                && matcher.TryMatch(path, [])
            )
            {
                return true;
            }
        }

        return false;
    }
}
