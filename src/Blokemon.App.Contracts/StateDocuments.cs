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

/// One entry of a key-prefix listing: the key, its revision and the declared summary of its
/// type, never the document body.
public sealed record DocumentSummary(string Key, long Revision, DocumentProjection? Projection);

/// The named fields a listing may surface for a document type. Types without a declared
/// projection are listed with none.
public abstract record DocumentProjection
{
    private DocumentProjection() { }

    /// For account and tenant documents.
    public sealed record Lifecycle(
        string? Status,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? ErasedAt
    ) : DocumentProjection;

    /// For hand-off and session documents.
    public sealed record Expiry(DateTimeOffset? ExpiresAt) : DocumentProjection;
}
