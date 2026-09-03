using Blokemon.App.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Blokemon.Web.Persistence;

public sealed class StateDocumentStore(IDbContextFactory<BlokemonDbContext> contexts)
    : IStateDocumentStore
{
    public async Task<StoredDocument?> Read(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        RefuseOverlongKey(key);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context
            .StateDocuments.AsNoTracking()
            .Where(row => row.Key == key)
            .Select(static row => new StoredDocument(row.Revision, row.Json))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<DocumentWriteResult> Create(
        string key,
        string json,
        CancellationToken cancellationToken = default
    )
    {
        RefuseOverlongKey(key);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO StateDocuments (Key, Revision, Json)
            VALUES ({key}, {1}, {json})
            """,
            cancellationToken
        );
        return rows == 1 ? new DocumentWriteResult.Written(1) : new DocumentWriteResult.Conflict();
    }

    public async Task<DocumentWriteResult> Update(
        string key,
        long expectedRevision,
        string json,
        CancellationToken cancellationToken = default
    )
    {
        RefuseOverlongKey(key);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .StateDocuments.Where(row => row.Key == key && row.Revision == expectedRevision)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(static row => row.Revision, expectedRevision + 1)
                        .SetProperty(static row => row.Json, json),
                cancellationToken
            );
        return rows == 1
            ? new DocumentWriteResult.Written(expectedRevision + 1)
            : new DocumentWriteResult.Conflict();
    }

    public async Task Delete(string key, CancellationToken cancellationToken = default)
    {
        RefuseOverlongKey(key);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context
            .StateDocuments.Where(row => row.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<DocumentDeleteResult> DeleteIfUnchanged(
        string key,
        long expectedRevision,
        string expectedJson,
        CancellationToken cancellationToken = default
    )
    {
        RefuseOverlongKey(key);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .StateDocuments.Where(row =>
                row.Key == key && row.Revision == expectedRevision && row.Json == expectedJson
            )
            .ExecuteDeleteAsync(cancellationToken);
        if (rows == 1)
        {
            return new DocumentDeleteResult.Deleted();
        }

        var exists = await context.StateDocuments.AnyAsync(
            row => row.Key == key,
            cancellationToken
        );
        return exists ? new DocumentDeleteResult.Conflict() : new DocumentDeleteResult.Missing();
    }

    /// <summary>
    /// Every document whose key starts with <paramref name="prefix"/>, in key order: the key,
    /// its revision and the summary declared for its type. No document body leaves the store.
    /// </summary>
    public async Task<IReadOnlyList<DocumentSummary>> List(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        RefuseOverlongKey(prefix);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .StateDocuments.AsNoTracking()
            .Where(row => row.Key.StartsWith(prefix))
            .OrderBy(static row => row.Key)
            .Select(static row => new
            {
                row.Key,
                row.Revision,
                row.Json,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new DocumentSummary(
                row.Key,
                row.Revision,
                DocumentSummaryProjection.Project(row.Key, row.Json)
            ))
            .ToArray();
    }

    private static void RefuseOverlongKey(string key)
    {
        if (key.Length > StateDocument.MaximumKeyLength)
        {
            throw new DocumentStorageException(
                DocumentStorageFailure.Rejected,
                $"A document key must be at most {StateDocument.MaximumKeyLength} characters."
            );
        }
    }
}
