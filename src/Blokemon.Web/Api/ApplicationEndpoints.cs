using Blokemon.App;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Api;

/// <summary>
/// The server's application routes. Server documents are keyed by account, and until
/// BLOKEMON-149 introduces sessions no request names one, so every route answers with the typed
/// <c>session.required</c> refusal in the ordinary response envelope and touches nothing.
/// </summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        var api = endpoints.MapGroup("/api");
        api.MapGet("/state", static () => SessionRequired<ApplicationView>());
        api.MapPost("/profile", static () => SessionRequired<ApplicationView>());
        api.MapPost("/packs/open", static () => SessionRequired<ApplicationView>());
        api.MapPost("/starter-decks/claim", static () => SessionRequired<ApplicationView>());
        api.MapPost("/decks", static () => SessionRequired<ApplicationView>());
        api.MapPost("/decks/delete", static () => SessionRequired<ApplicationView>());
        api.MapPost("/matches", static () => SessionRequired<MatchMutationView>());
        api.MapPost(
            "/matches/{matchId:guid}/actions",
            static () => SessionRequired<MatchMutationView>()
        );
        api.MapPost("/matches/abandon", static () => SessionRequired<ApplicationView>());
        api.MapPost("/matches/history/discard", static () => SessionRequired<ApplicationView>());
        api.MapPost("/purge", static () => SessionRequired<ApplicationView>());
        return endpoints;
    }

    private static ApiResponse<T> SessionRequired<T>()
        where T : class => new(false, null, SessionFailures.required());
}
