using Blokemon.App;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Application;

public interface IApplicationStateReader
{
    Task<ApiResponse<ApplicationView>> State(CancellationToken cancellationToken = default);
}

public interface IApplicationStateRefresher
{
    Task<ApiResponse<ApplicationView>> Refresh(CancellationToken cancellationToken = default);
}

public interface IDeckOperations
{
    Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    );
}

public interface IStarterDeckOperations
{
    Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    );
}

public interface IMatchOperations
{
    Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    );
}

public interface IPackOperations
{
    Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    );
}

public interface IProfileOperations
{
    Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> PurgeData(CancellationToken cancellationToken = default);
}

public interface IPlayModeOperations
{
    Task<PlayModeState> Mode(CancellationToken cancellationToken = default);

    Task<ApiResponse<PlayModeState>> SelectMode(
        PlayMode mode,
        CancellationToken cancellationToken = default
    );
}
