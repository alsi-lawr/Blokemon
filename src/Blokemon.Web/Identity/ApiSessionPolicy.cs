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
        // The first-party sign-in ceremonies (BLOKEMON-150).
        ("POST", "/api/session/firstparty/register"),
        ("POST", "/api/session/firstparty/authenticate"),
        ("POST", "/api/session/firstparty/recover"),
        // The hand-off and continuation exchanges (BLOKEMON-151).
        ("POST", HandoffExchange.Route),
        ("POST", "/api/session/resume"),
        // The channel endpoints, authenticated by integration token (BLOKEMON-151).
        ("GET", "/api/tenant/self"),
        ("POST", "/api/tenant/handoff"),
        ("POST", "/api/tenant/erasure"),
        ("POST", "/api/tenant/close"),
    ];

    private static readonly (string Method, TemplateMatcher Matcher)[] Matchers = Anonymous
        .Select(route =>
            (route.Method, new TemplateMatcher(TemplateParser.Parse(route.Template), []))
        )
        .ToArray();

    public static bool IsAnonymous(string method, PathString path)
    {
        foreach (var (routeMethod, matcher) in Matchers)
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
