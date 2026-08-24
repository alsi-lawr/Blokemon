using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Application;

public interface IApplicationStateReader
{
    Task<ApiResponse<ApplicationView>> State(CancellationToken cancellationToken = default);
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

internal sealed class ApplicationCapabilities(IBlokemonApplication application)
    : IApplicationStateReader,
        IDeckOperations,
        IStarterDeckOperations,
        IMatchOperations,
        IPackOperations,
        IProfileOperations
{
    public Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    ) => application.State(cancellationToken);

    public Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    ) => application.SaveDeck(request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    ) => application.DeleteDeck(request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    ) => application.ClaimStarterDeck(request, cancellationToken);

    public Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    ) => application.StartMatch(request, cancellationToken);

    public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    ) => application.ApplyMatchAction(matchId, request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    ) => application.OpenPack(request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) => application.CreateProfile(request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    ) => application.PurgeData(cancellationToken);
}
