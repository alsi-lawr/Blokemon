using System.Net.Http.Json;
using System.Text.Json;
using Blokemon.App.Contracts;

namespace Blokemon.App;

public sealed class BlokemonApiClient(HttpClient http) : IBlokemonApplication
{
    public Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    ) => Get<ApplicationView>("api/state", cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) => Post<CreateProfileRequest, ApplicationView>("api/profile", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    ) => Post<OpenPackRequest, ApplicationView>("api/packs/open", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<ClaimStarterDeckRequest, ApplicationView>(
            "api/starter-decks/claim",
            request,
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Post<SaveDeckRequest, ApplicationView>("api/decks", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Post<DeleteDeckRequest, ApplicationView>("api/decks/delete", request, cancellationToken);

    public Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    ) => Post<StartMatchRequest, MatchMutationView>("api/matches", request, cancellationToken);

    public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<ApplyMatchActionRequest, MatchMutationView>(
            $"api/matches/{matchId:D}/actions",
            request,
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    ) => Post<object, ApplicationView>("api/purge", new(), cancellationToken);

    private async Task<ApiResponse<T>> Get<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetFromJsonAsync<ApiResponse<T>>(path, cancellationToken)
                ?? Unavailable<T>();
        }
        catch (HttpRequestException)
        {
            return Unavailable<T>();
        }
        catch (JsonException)
        {
            return Unavailable<T>();
        }
        catch (NotSupportedException)
        {
            return Unavailable<T>();
        }
    }

    private async Task<ApiResponse<TResponse>> Post<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, request, cancellationToken);
            return await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>(
                    cancellationToken
                ) ?? Unavailable<TResponse>();
        }
        catch (HttpRequestException)
        {
            return Unavailable<TResponse>();
        }
        catch (JsonException)
        {
            return Unavailable<TResponse>();
        }
        catch (NotSupportedException)
        {
            return Unavailable<TResponse>();
        }
    }

    private static ApiResponse<T> Unavailable<T>() =>
        new(false, default, new("unavailable", "The local game service is unavailable."));
}
