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

public interface IMatchRecoveryOperations
{
    Task<ApiResponse<ApplicationView>> AbandonSavedMatch(
        AbandonSavedMatchRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> DiscardMatchHistory(
        DiscardMatchHistoryRequest request,
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

internal sealed class BackgroundOperationQueue : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;

    public CancellationToken Lifetime => _lifetime.Token;

    public async Task<T> Run<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken callerCancellation
    )
    {
        await _gate.WaitAsync(callerCancellation);
        if (_disposed)
        {
            _gate.Release();
            throw new ObjectDisposedException(nameof(BackgroundOperationQueue));
        }

        var completion = Complete(operation);
        _ = Observe(completion);
        return await completion.WaitAsync(callerCancellation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
    }

    private async Task<T> Complete<T>(Func<CancellationToken, Task<T>> operation)
    {
        try
        {
            return await operation(_lifetime.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task Observe(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
            // Detached work is still observed after its caller leaves or the scope is disposed.
        }
    }
}
