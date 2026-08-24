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
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private Task<IAsyncDisposable>? _invalidationSubscription;
    private Task<ApiResponse<ApplicationView>>? _hydration;
    private ApiResponse<ApplicationView>? _current;
    private long _epoch;
    private volatile bool _disposed;

    public async Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    )
    {
        await EnsureInvalidationSubscription().WaitAsync(cancellationToken);

        Task<ApiResponse<ApplicationView>> state;
        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_current is not null)
            {
                return _current;
            }

            _hydration ??= Hydrate(_epoch);
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
    ) => Mutate(token => application.SaveDeck(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Mutate(token => application.DeleteDeck(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Mutate(token => application.ClaimStarterDeck(request, token), cancellationToken);

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
    ) => Mutate(token => application.OpenPack(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) => Mutate(token => application.CreateProfile(request, token), cancellationToken);

    public async Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    )
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var response = await application.PurgeData(cancellationToken);
            if (response.Succeeded)
            {
                await Invalidate();
            }
            return response;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

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

    public async Task<ApiResponse<PlayModeState>> SelectMode(
        PlayMode mode,
        CancellationToken cancellationToken = default
    )
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var before = await modes.Mode(cancellationToken);
            var response = await modes.SelectMode(mode, cancellationToken);
            if (response.Succeeded && response.Value?.Selected != before.Selected)
            {
                await Invalidate();
            }
            return response;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<IAsyncDisposable>? subscription;
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
            subscription = _invalidationSubscription;
        }
        finally
        {
            _snapshotGate.Release();
        }

        if (subscription is not null)
        {
            await (await subscription).DisposeAsync();
        }
    }

    private async Task<IAsyncDisposable> EnsureInvalidationSubscription()
    {
        Task<IAsyncDisposable> subscription;
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
            subscription = _invalidationSubscription ??= documentInvalidations.Subscribe(
                ExternalDocumentInvalidated
            );
        }
        finally
        {
            _snapshotGate.Release();
        }
        return await subscription;
    }

    private async Task<ApiResponse<ApplicationView>> Hydrate(long epoch)
    {
        var response = await application.State(CancellationToken.None);
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
            replacement = await State(CancellationToken.None);
        }
        return replacement ?? response;
    }

    private async Task<ApiResponse<ApplicationView>> Mutate(
        Func<CancellationToken, Task<ApiResponse<ApplicationView>>> operation,
        CancellationToken cancellationToken
    )
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var response = await operation(cancellationToken);
            if (response.Succeeded && response.Value is not null)
            {
                await Publish(response.Value);
            }
            return response;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ApiResponse<MatchMutationView>> MutateMatch(
        Func<CancellationToken, Task<ApiResponse<MatchMutationView>>> operation,
        CancellationToken cancellationToken
    )
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var response = await operation(cancellationToken);
            if (response.Succeeded && response.Value is not null)
            {
                await Publish(response.Value.Application);
            }
            return response;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task Publish(ApplicationView view)
    {
        await _snapshotGate.WaitAsync(CancellationToken.None);
        try
        {
            ThrowIfDisposed();
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
