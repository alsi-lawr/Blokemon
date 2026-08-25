namespace Blokemon.App.Contracts;

public sealed record StoredDocument(long Revision, string Json);

public abstract record DocumentWriteResult
{
    private DocumentWriteResult() { }

    public sealed record Written(long Revision) : DocumentWriteResult;

    public sealed record Conflict : DocumentWriteResult;
}

public abstract record DocumentDeleteResult
{
    private DocumentDeleteResult() { }

    public sealed record Deleted : DocumentDeleteResult;

    public sealed record Missing : DocumentDeleteResult;

    public sealed record Conflict : DocumentDeleteResult;
}

public interface IStateDocumentStore
{
    Task<StoredDocument?> Read(string key, CancellationToken cancellationToken = default);

    Task<DocumentWriteResult> Create(
        string key,
        string json,
        CancellationToken cancellationToken = default
    );

    Task<DocumentWriteResult> Update(
        string key,
        long expectedRevision,
        string json,
        CancellationToken cancellationToken = default
    );

    Task Delete(string key, CancellationToken cancellationToken = default);

    Task<DocumentDeleteResult> DeleteIfUnchanged(
        string key,
        long expectedRevision,
        string expectedJson,
        CancellationToken cancellationToken = default
    );
}

public enum DocumentStorageFailure
{
    Unavailable,
    Full,
    Rejected,
}

public sealed class DocumentStorageException(
    DocumentStorageFailure failure,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public DocumentStorageFailure Failure { get; } = failure;
}
