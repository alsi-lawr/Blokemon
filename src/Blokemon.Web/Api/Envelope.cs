using Blokemon.App.Contracts;

namespace Blokemon.Web.Api;

/// <summary>The two shapes of the ordinary response envelope, as the identity routes answer.</summary>
internal static class Envelope
{
    public static ApiResponse<T> Ok<T>(T value) => new(true, value, null);

    public static ApiResponse<T> Fail<T>(ApiError error) => new(false, default, error);
}
