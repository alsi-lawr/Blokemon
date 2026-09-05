using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Web.Identity;

/// <summary>
/// The application service for the account a request's session names. One is built per
/// request, as the server host always did, now for the session's principal.
/// </summary>
public sealed class ServerApplications(
    BlokemonCatalogue catalogue,
    IStateDocumentStore documents,
    EconomyRules economy,
    CurrentSession current
)
{
    /// <summary>The session's application; the middleware guarantees one on every route that needs it.</summary>
    public LocalApplicationService Current() =>
        current.Session is { } session
            ? For(session)
            : throw new InvalidOperationException(
                "A session-required route ran without a session."
            );

    public LocalApplicationService For(Session session)
    {
        var principal = ApplicationPrincipal.NewAccount(session.Account, session.Tenant);
        return new(
            catalogue,
            documents,
            principal,
            new LocalMatchService(
                catalogue,
                documents,
                PlayerDocumentKeysModule.ofPrincipal(principal)
            ),
            economy,
            ProfileAuthorityPolicy.Preserve
        );
    }
}
