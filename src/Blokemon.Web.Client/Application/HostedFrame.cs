using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// The hosted-mode receiver: adopts what the page retained while the application loaded,
/// listens on from the moment it is attached, keeps what arrives before the tenant's registered
/// parent origin is known, and once bound accepts a hand-off only from exactly that origin and
/// posts to the parent only with it. The origin logic lives in signIn.js; this is its handle.
/// </summary>
public sealed class HostedFrame(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _receiver;
    private DotNetObjectReference<HostedFrame>? _reference;
    private Func<string, Task>? _deliver;

    /// <summary>True once bound to a tenant with a registered parent origin.</summary>
    public bool IsBound { get; private set; }

    /// <summary>Starts listening; hand-off codes accepted later are handed to <paramref name="deliver"/>.</summary>
    public async Task Attach(
        Func<string, Task> deliver,
        CancellationToken cancellationToken = default
    )
    {
        _deliver = deliver;
        if (_receiver is not null)
        {
            return;
        }

        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./signIn.js"
        );
        _reference ??= DotNetObjectReference.Create(this);
        _receiver = await _module.InvokeAsync<IJSObjectReference>(
            "attachReceiver",
            cancellationToken,
            _reference
        );
    }

    /// <summary>
    /// Binds the receiver to the tenant's registered parent origin: retained messages are
    /// validated against it or discarded, and readiness is signalled to the parent. A tenant
    /// with no registered origin binds to nothing and the receiver accepts nothing.
    /// </summary>
    public async Task Bind(
        string? registeredParentOrigin,
        CancellationToken cancellationToken = default
    )
    {
        if (_receiver is null)
        {
            throw new InvalidOperationException("The receiver is not attached.");
        }

        IsBound = await _receiver.InvokeAsync<bool>(
            "bind",
            cancellationToken,
            registeredParentOrigin
        );
    }

    /// <summary>Posts a typed message to the parent with the exact registered origin, if bound.</summary>
    public async Task<bool> Post(string type, CancellationToken cancellationToken = default) =>
        _receiver is not null && await _receiver.InvokeAsync<bool>("post", cancellationToken, type);

    [JSInvokable]
    public Task ReceiveHandoff(string code) => _deliver?.Invoke(code) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_receiver is not null)
            {
                await _receiver.InvokeVoidAsync("detach");
                await _receiver.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The browser already released the module.
        }

        _reference?.Dispose();
    }
}
