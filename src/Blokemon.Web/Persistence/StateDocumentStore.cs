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
}
