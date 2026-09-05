using System.Net.Http.Json;
using System.Text.Json;
using Blokemon.App.Contracts;

namespace Blokemon.App.Client;

/// <summary>
/// Reads the ordinary response envelope from an HTTP call. Anything that is not an envelope,
/// including a route that does not exist, is the typed <c>unavailable</c> outcome.
/// </summary>
internal static class ApiEnvelopeTransport
{
    public static async Task<ApiResponse<T>> Get<T>(
        HttpClient http,
        string path,
        ApiError unavailable,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await http.GetFromJsonAsync<ApiResponse<T>>(path, cancellationToken)
                ?? Unavailable<T>(unavailable);
        }
        catch (HttpRequestException)
        {
            return Unavailable<T>(unavailable);
        }
        catch (JsonException)
        {
            return Unavailable<T>(unavailable);
        }
        catch (NotSupportedException)
        {
            return Unavailable<T>(unavailable);
        }
    }

    public static async Task<ApiResponse<TResponse>> Post<TRequest, TResponse>(
        HttpClient http,
        string path,
        TRequest request,
        ApiError unavailable,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, request, cancellationToken);
            return await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>(
                    cancellationToken
                ) ?? Unavailable<TResponse>(unavailable);
        }
        catch (HttpRequestException)
        {
            return Unavailable<TResponse>(unavailable);
        }
        catch (JsonException)
        {
            return Unavailable<TResponse>(unavailable);
        }
        catch (NotSupportedException)
        {
            return Unavailable<TResponse>(unavailable);
        }
    }

    private static ApiResponse<T> Unavailable<T>(ApiError error) => new(false, default, error);
}
