using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

internal interface IApplicationDocumentInvalidations
{
    Task<IAsyncDisposable> Subscribe(Func<string, Task> invalidated);
}

internal sealed class BrowserDocumentInvalidations(IJSRuntime js)
    : IApplicationDocumentInvalidations
{
    public async Task<IAsyncDisposable> Subscribe(Func<string, Task> invalidated)
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
            return EmptyAsyncDisposable.Instance;
        }
        catch (JSDisconnectedException)
        {
            await Release(module, reference);
            return EmptyAsyncDisposable.Instance;
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
            await module.InvokeVoidAsync("unsubscribeInvalidation", id);
            await module.DisposeAsync();
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        finally
        {
            receiver.Dispose();
        }
    }
}

internal sealed class EmptyAsyncDisposable : IAsyncDisposable
{
    public static EmptyAsyncDisposable Instance { get; } = new();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
