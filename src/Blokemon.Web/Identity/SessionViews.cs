using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Web.Identity;

/// <summary>
/// A freshly issued session as the client receives it, named by the profile it acts for; an
/// account with no profile yet has no name, and the client asks for one.
/// </summary>
internal static class SessionViews
{
    public static async Task<IssuedSessionView> Describe(
        IssuedSession issued,
        ServerApplications applications,
        CancellationToken cancellationToken
    )
    {
        var state = await applications.For(issued.Session).State(cancellationToken);
        return new(
            issued.Token,
            issued.Session.ExpiresAt,
            state.Value?.Profile?.DisplayName,
            issued.Session.Provenance == SessionProvenance.Recovery
        );
    }
}
