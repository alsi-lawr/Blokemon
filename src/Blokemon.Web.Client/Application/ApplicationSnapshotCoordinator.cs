using Blokemon.App;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Application;

internal sealed class ApplicationSnapshotCoordinator(
    IBlokemonApplication application,
    PlayModeApplication modes,
    IApplicationDocumentInvalidations documentInvalidations
)
    : IApplicationStateReader,
        IApplicationStateRefresher,
        IDeckOperations,
        IStarterDeckOperations,
        IMatchOperations,
        IPackOperations,
        IProfileOperations,
        IPlayModeOperations,
        IAsyncDisposable
{
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly BackgroundOperationQueue _operations = new();
    private readonly ApplicationDocumentInvalidationSession _documentInvalidations = new(
        documentInvalidations
    );
    private Task<ApiResponse<ApplicationView>>? _hydration;
    private ApiResponse<ApplicationView>? _current;
    private long _epoch;
    private volatile bool _disposed;

    public async Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    )
    {
        var subscription = _documentInvalidations.Ensure(ExternalDocumentInvalidated);
        _ = BackgroundOperationQueue.Observe(subscription);
        await subscription.WaitAsync(cancellationToken);

        Task<ApiResponse<ApplicationView>> state;
        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_current is not null)
            {
                return _current;
            }

            if (_hydration is null)
            {
                _hydration = Hydrate(_epoch);
                _ = BackgroundOperationQueue.Observe(_hydration);
            }
            state = _hydration;
        }
        finally
        {
            _snapshotGate.Release();
        }

        try
        {
            return await state.WaitAsync(cancellationToken);
        }
        finally
        {
            if (state.IsCompleted)
            {
                await _snapshotGate.WaitAsync(CancellationToken.None);
                try
                {
                    if (ReferenceEquals(_hydration, state))
                    {
                        _hydration = null;
                    }
                }
                finally
                {
                    _snapshotGate.Release();
                }
            }
        }
    }

    public async Task<ApiResponse<ApplicationView>> Refresh(
        CancellationToken cancellationToken = default
    )
    {
        await Invalidate();
        return await State(cancellationToken);
    }

    public Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    ) => MutateApplication(token => application.SaveDeck(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    ) => MutateApplication(token => application.DeleteDeck(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    ) =>
        MutateApplication(token => application.ClaimStarterDeck(request, token), cancellationToken);

    public Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    ) => MutateMatch(token => application.StartMatch(request, token), cancellationToken);

    public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    ) =>
        MutateMatch(
            token => application.ApplyMatchAction(matchId, request, token),
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    ) => MutateApplication(token => application.OpenPack(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) => MutateApplication(token => application.CreateProfile(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    ) =>
        Mutate(
            token => application.PurgeData(token),
            (response, _) => response.Succeeded ? Invalidate() : Task.CompletedTask,
            cancellationToken
        );

    public async Task<PlayModeState> Mode(CancellationToken cancellationToken = default)
    {
        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
        }
        finally
        {
            _snapshotGate.Release();
        }
        return await modes.Mode(cancellationToken);
    }

    public Task<ApiResponse<PlayModeState>> SelectMode(
        PlayMode mode,
        CancellationToken cancellationToken = default
    ) =>
        _operations.Run(
            async token =>
            {
                ThrowIfDisposed();
                var before = await modes.Mode(token);
                var response = await modes.SelectMode(mode, token);
                if (response.Succeeded && response.Value?.Selected != before.Selected)
                {
                    await Invalidate();
                }
                return response;
            },
            cancellationToken
        );

    public async ValueTask DisposeAsync()
    {
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _epoch++;
            _current = null;
            _hydration = null;
        }
        finally
        {
            _snapshotGate.Release();
        }

        _operations.Dispose();
        await _documentInvalidations.DisposeAsync();
    }

    private Task<ApiResponse<ApplicationView>> MutateApplication(
        Func<CancellationToken, Task<ApiResponse<ApplicationView>>> operation,
        CancellationToken cancellationToken
    ) =>
        Mutate(
            operation,
            (response, epoch) =>
                response.Succeeded ? Publish(response.Value, epoch) : Task.CompletedTask,
            cancellationToken
        );

    private Task<ApiResponse<MatchMutationView>> MutateMatch(
        Func<CancellationToken, Task<ApiResponse<MatchMutationView>>> operation,
        CancellationToken cancellationToken
    ) =>
        Mutate(
            operation,
            (response, epoch) =>
                response.Succeeded
                    ? Publish(response.Value?.Application, epoch)
                    : Task.CompletedTask,
            cancellationToken
        );

    private Task<ApiResponse<T>> Mutate<T>(
        Func<CancellationToken, Task<ApiResponse<T>>> operation,
        Func<ApiResponse<T>, long, Task> settle,
        CancellationToken cancellationToken
    ) =>
        _operations.Run(
            async token =>
            {
                ThrowIfDisposed();
                var epoch = await CurrentEpoch();
                var response = await operation(token);
                await settle(response, epoch);
                return response;
            },
            cancellationToken
        );

    private async Task<ApiResponse<ApplicationView>> Hydrate(long epoch)
    {
        var response = await application.State(_operations.Lifetime);
        ApiResponse<ApplicationView>? replacement = null;
        var retry = false;
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            if (_epoch != epoch)
            {
                replacement = _current;
                retry = replacement is null;
            }
            else if (response.Succeeded && response.Value is not null)
            {
                _current = response;
            }
        }
        finally
        {
            _snapshotGate.Release();
        }

        if (retry)
        {
            replacement = await State(_operations.Lifetime);
        }
        return replacement ?? response;
    }

    private async Task<long> CurrentEpoch()
    {
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            return _epoch;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private async Task Publish(ApplicationView? view, long epoch)
    {
        if (view is null)
        {
            return;
        }

        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_epoch != epoch)
            {
                _current = null;
                _hydration = null;
                return;
            }

            _epoch++;
            _current = new(true, view, null);
            _hydration = null;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private Task ExternalDocumentInvalidated(string _) => Invalidate();

    private async Task Invalidate()
    {
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed)
            {
                return;
            }

            _epoch++;
            _current = null;
            _hydration = null;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
