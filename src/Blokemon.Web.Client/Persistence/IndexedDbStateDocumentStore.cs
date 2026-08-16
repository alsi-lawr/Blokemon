using Microsoft.JSInterop;

namespace Blokemon.Web.Persistence;

public sealed class IndexedDbStateDocumentStore(IJSRuntime js)
    : IStateDocumentStore,
        IAsyncDisposable
{
    private readonly SemaphoreSlim _moduleLock = new(1, 1);
    private IJSObjectReference? _module;

    public async Task<StoredDocument?> Read(
        string key,
        CancellationToken cancellationToken = default
    ) => await Invoke<StoredDocument?>("read", cancellationToken, key);

    public async Task<DocumentWriteResult> Create(
        string key,
        string json,
        CancellationToken cancellationToken = default
    )
    {
        var revision = await Invoke<long?>("create", cancellationToken, key, json);
        return revision is { } written
            ? new DocumentWriteResult.Written(written)
            : new DocumentWriteResult.Conflict();
    }

    public async Task<DocumentWriteResult> Update(
        string key,
        long expectedRevision,
        string json,
        CancellationToken cancellationToken = default
    )
    {
        var revision = await Invoke<long?>(
            "update",
            cancellationToken,
            key,
            expectedRevision,
            json
        );
        return revision is { } written
            ? new DocumentWriteResult.Written(written)
            : new DocumentWriteResult.Conflict();
    }

    public async Task Delete(string key, CancellationToken cancellationToken = default) =>
        await Invoke<object?>("remove", cancellationToken, key);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
        _moduleLock.Dispose();
    }

    private async Task<T> Invoke<T>(
        string method,
        CancellationToken cancellationToken,
        params object?[] arguments
    )
    {
        try
        {
            var module = await Module(cancellationToken);
            return await module.InvokeAsync<T>(method, cancellationToken, arguments);
        }
        catch (JSException exception)
        {
            throw StorageFailure(exception);
        }
    }

    private async Task<IJSObjectReference> Module(CancellationToken cancellationToken)
    {
        if (_module is not null)
        {
            return _module;
        }

        await _moduleLock.WaitAsync(cancellationToken);
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./browserState.js"
            );
            return _module;
        }
        finally
        {
            _moduleLock.Release();
        }
    }

    private static DocumentStorageException StorageFailure(JSException exception)
    {
        var failure = exception.Message switch
        {
            var message when message.Contains("QuotaExceededError", StringComparison.Ordinal) =>
                DocumentStorageFailure.Full,
            var message
                when message.Contains("NotAllowedError", StringComparison.Ordinal)
                    || message.Contains("SecurityError", StringComparison.Ordinal) =>
                DocumentStorageFailure.Rejected,
            _ => DocumentStorageFailure.Unavailable,
        };
        return new(failure, "Browser storage could not save the game.", exception);
    }
}
