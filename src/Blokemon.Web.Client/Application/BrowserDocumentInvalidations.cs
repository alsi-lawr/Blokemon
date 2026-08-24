using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

internal interface IApplicationDocumentInvalidations
{
    Task<IAsyncDisposable?> Subscribe(Func<string, Task> invalidated);
}

internal sealed class ApplicationDocumentInvalidationSession(
    IApplicationDocumentInvalidations invalidations
) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _initialization;
    private IAsyncDisposable? _subscription;
    private bool _disposed;

    public async Task Ensure(Func<string, Task> invalidated)
    {
        Task initialization;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscription is not null)
            {
                return;
            }

            initialization = _initialization ??= Initialize(invalidated);
        }
        finally
        {
            _gate.Release();
        }
        await initialization;
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable? subscription;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscription = _subscription;
            _subscription = null;
        }
        finally
        {
            _gate.Release();
        }

        if (subscription is not null)
        {
            await subscription.DisposeAsync();
        }
    }

    private async Task Initialize(Func<string, Task> invalidated)
    {
        IAsyncDisposable? subscription = null;
        try
        {
            subscription = await invalidations.Subscribe(invalidated);
        }
        catch
        {
            // Cross-tab notification is optional; document CAS remains authoritative.
        }

        IAsyncDisposable? dispose = null;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            _initialization = null;
            if (_disposed)
            {
                dispose = subscription;
            }
            else
            {
                _subscription = subscription;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (dispose is not null)
        {
            try
            {
                await dispose.DisposeAsync();
            }
            catch
            {
                // The scope is already gone; disposal is best-effort at this optional boundary.
            }
        }
    }
}

internal sealed class BrowserDocumentInvalidations(IJSRuntime js)
    : IApplicationDocumentInvalidations
{
    public async Task<IAsyncDisposable?> Subscribe(Func<string, Task> invalidated)
    {
        IJSObjectReference? module = null;
        DotNetObjectReference<BrowserDocumentInvalidationReceiver>? reference = null;
        try
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", "./browserState.js");
            var receiver = new BrowserDocumentInvalidationReceiver(invalidated);
            reference = DotNetObjectReference.Create(receiver);
            var id = await module.InvokeAsync<long>("subscribeInvalidation", reference);
            return new BrowserDocumentInvalidationSubscription(module, reference, id);
        }
        catch (JSException)
        {
            await Release(module, reference);
            return null;
        }
        catch (JSDisconnectedException)
        {
            await Release(module, reference);
            return null;
        }
    }

    private static async Task Release(
        IJSObjectReference? module,
        DotNetObjectReference<BrowserDocumentInvalidationReceiver>? receiver
    )
    {
        receiver?.Dispose();
        if (module is not null)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (JSException) { }
            catch (JSDisconnectedException) { }
        }
    }
}

internal sealed class BrowserDocumentInvalidationReceiver(Func<string, Task> invalidated)
{
    [JSInvokable]
    public async Task Invalidated(string key) => await invalidated(key);
}

internal sealed class BrowserDocumentInvalidationSubscription(
    IJSObjectReference module,
    DotNetObjectReference<BrowserDocumentInvalidationReceiver> receiver,
    long id
) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            await IgnoreJsFailure(() => module.InvokeVoidAsync("unsubscribeInvalidation", id));
            await IgnoreJsFailure(module.DisposeAsync);
        }
        finally
        {
            receiver.Dispose();
        }
    }

    private static async Task IgnoreJsFailure(Func<ValueTask> release)
    {
        try
        {
            await release();
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }
}
