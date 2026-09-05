using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Web.Identity;

namespace Blokemon.Web.Api;

/// <summary>
/// The server's application routes, acting for the account the request's session names. The
/// session middleware refuses every one of these but <c>GET /api/state</c> without a session;
/// the state route answers a signed-out caller with the signed-out view.
/// </summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        var api = endpoints.MapGroup("/api");
        api.MapGet(
            "/state",
            static (
                CurrentSession current,
                ServerApplications applications,
                BlokemonCatalogue catalogue,
                CancellationToken cancellationToken
            ) =>
                current.Session is { } session
                    ? applications.For(session).State(cancellationToken)
                    : Task.FromResult(
                        new ApiResponse<ApplicationView>(true, SignedOutView.build(catalogue), null)
                    )
        );
        api.MapPost(
            "/profile",
            static (
                CreateProfileRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().CreateProfile(request, cancellationToken)
        );
        api.MapPost(
            "/packs/open",
            static (
                OpenPackRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().OpenPack(request, cancellationToken)
        );
        api.MapPost(
            "/starter-decks/claim",
            static (
                ClaimStarterDeckRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().ClaimStarterDeck(request, cancellationToken)
        );
        api.MapPost(
            "/decks",
            static (
                SaveDeckRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().SaveDeck(request, cancellationToken)
        );
        api.MapPost(
            "/decks/delete",
            static (
                DeleteDeckRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().DeleteDeck(request, cancellationToken)
        );
        api.MapPost(
            "/matches",
            static (
                StartMatchRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().StartMatch(request, cancellationToken)
        );
        api.MapPost(
            "/matches/{matchId:guid}/actions",
            static (
                Guid matchId,
                ApplyMatchActionRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().ApplyMatchAction(matchId, request, cancellationToken)
        );
        api.MapPost(
            "/matches/abandon",
            static (
                AbandonSavedMatchRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().AbandonSavedMatch(request, cancellationToken)
        );
        api.MapPost(
            "/matches/history/discard",
            static (
                DiscardMatchHistoryRequest request,
                ServerApplications applications,
                CancellationToken cancellationToken
            ) => applications.Current().DiscardMatchHistory(request, cancellationToken)
        );
        api.MapPost(
            "/purge",
            static (ServerApplications applications, CancellationToken cancellationToken) =>
                applications.Current().PurgeData(cancellationToken)
        );
        return endpoints;
    }
}
