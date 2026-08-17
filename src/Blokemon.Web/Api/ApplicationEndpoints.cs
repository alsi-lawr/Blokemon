using Blokemon.App;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Api;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        var api = endpoints.MapGroup("/api");
        api.MapGet(
            "/state",
            (LocalApplicationService application, CancellationToken cancellationToken) =>
                application.State(cancellationToken)
        );
        api.MapPost(
            "/profile",
            (
                CreateProfileRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.CreateProfile(request, cancellationToken)
        );
        api.MapPost(
            "/packs/open",
            (
                OpenPackRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.OpenPack(request, cancellationToken)
        );
        api.MapPost(
            "/starter-decks/claim",
            (
                ClaimStarterDeckRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.ClaimStarterDeck(request, cancellationToken)
        );
        api.MapPost(
            "/decks",
            (
                SaveDeckRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.SaveDeck(request, cancellationToken)
        );
        api.MapPost(
            "/decks/delete",
            (
                DeleteDeckRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.DeleteDeck(request, cancellationToken)
        );
        api.MapPost(
            "/matches",
            (
                StartMatchRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.StartMatch(request, cancellationToken)
        );
        api.MapPost(
            "/matches/{matchId:guid}/actions",
            (
                Guid matchId,
                ApplyMatchActionRequest request,
                LocalApplicationService application,
                CancellationToken cancellationToken
            ) => application.ApplyMatchAction(matchId, request, cancellationToken)
        );
        api.MapPost(
            "/purge",
            (LocalApplicationService application, CancellationToken cancellationToken) =>
                application.PurgeData(cancellationToken)
        );
        return endpoints;
    }
}
