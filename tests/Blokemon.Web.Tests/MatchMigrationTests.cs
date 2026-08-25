using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class MatchMigrationTests
{
    private static readonly Guid _firstDeck = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SchemaOneActiveMatch_MigratesThroughBackupAndColdReplayForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var sourceJson = Fixture("schema-one-active-match.json");
        await fixture.Store.Create("match", sourceJson);
        var source = (await fixture.Store.Read("match"))!;
        var profile = Profile(catalogue);

        var restored = await new LocalMatchService(catalogue, fixture.Store).State(
            profile,
            profile.DisplayName.Value
        );
        var migrated = (await fixture.Store.Read("match"))!;
        var backup = (await fixture.Store.Read(BackupKey("match", source)))!;
        var repeated = await new LocalMatchService(catalogue, fixture.Store).State(
            profile,
            profile.DisplayName.Value
        );

        restored.Error.ShouldBeNull();
        restored.View.ShouldNotBeNull();
        migrated.Revision.ShouldBe(source.Revision + 1);
        var migratedJson = JsonNode.Parse(migrated.Json)!.AsObject();
        migratedJson["schemaVersion"]!.GetValue<int>().ShouldBe(2);
        migratedJson["authorityVersion"]!
            .GetValue<string>()
            .ShouldBe(catalogue.Mechanics.ManifestVersion);
        AssertBackup(backup, "match", source, "match-schema-1-to-2+match-authority-to-checked-out");
        repeated.Error.ShouldBeNull();
        JsonSerializer.Serialize(repeated.View).ShouldBe(JsonSerializer.Serialize(restored.View));
        (await fixture.Store.Read("match")).ShouldBe(migrated);
        (await fixture.Store.Read(BackupKey("match", source))).ShouldBe(backup);
        (await fixture.Store.Read("profile")).ShouldBeNull();
    }

    [Test]
    public async Task SchemaOneHistory_MigratesAllMatchesBeforeACompletedMatchIsReplaced()
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite: true);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        var activeJson = Fixture("schema-one-completed-match.json");
        var historyJson = Fixture("schema-one-match-history.json");
        await fixture.Store.Create("match", activeJson);
        await fixture.Store.Create("match-history", historyJson);
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);
        var completed = await service.State(profile, profile.DisplayName.Value);

        var started = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000003"), _firstDeck)
        );
        var history = (await fixture.Store.Read("match-history"))!;
        var backup = (await fixture.Store.Read(BackupKey("match-history", historySource)))!;

        completed.Error.ShouldBeNull();
        completed.View!.Frame.IsComplete.ShouldBeTrue();
        started.Error.ShouldBeNull();
        started.View!.Frame.Id.ShouldBe(Guid.Parse("30000000-0000-0000-0000-000000000003"));
        history.Revision.ShouldBe(historySource.Revision + 1);
        var migratedJson = JsonNode.Parse(history.Json)!.AsObject();
        migratedJson["schemaVersion"]!.GetValue<int>().ShouldBe(2);
        migratedJson["authorityVersion"]!
            .GetValue<string>()
            .ShouldBe(catalogue.Mechanics.ManifestVersion);
        migratedJson["matches"]!.AsArray().ShouldHaveSingleItem();
        AssertBackup(
            backup,
            "match-history",
            historySource,
            "match-history-schema-1-to-2+match-history-authority-to-checked-out"
        );
    }

    [Test]
    public async Task IncompatiblePreD3History_LeavesTheWholeHistoryAndBackupSpaceUntouched()
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite: false);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        await fixture.Store.Create("match", Fixture("schema-one-completed-match.json"));
        await fixture.Store.Create("match-history", Fixture("pre-d3-match-history.json"));
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);
        var completed = await service.State(profile, profile.DisplayName.Value);

        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000004"), _firstDeck)
        );

        completed.Error.ShouldBeNull();
        rejected.Error!.Code.ShouldBe("match.history_authority_changed");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
        (await fixture.Store.Read("match"))!.Json.ShouldContain(
            "30000000-0000-0000-0000-000000000001",
            Case.Sensitive
        );
    }

    [Test]
    public async Task IncompatiblePreD3ActiveMatch_IsTypedAndPreservedWithoutABackup()
    {
        var documents = new MemoryDocumentStore();
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "pre-d3-profile.json");
        await documents.Create("match", Fixture("pre-d3-active-match.json"));
        var source = (await documents.Read("match"))!;

        var restored = await new LocalMatchService(catalogue, documents).State(
            profile,
            profile.DisplayName.Value
        );

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("match.authority_changed");
        (await documents.Read("match")).ShouldBe(source);
        (await documents.Read(BackupKey("match", source))).ShouldBeNull();
    }

    [Test]
    public async Task ConcurrentMigrationAttempts_ConvergeOnOneCandidateAndOneBackup()
    {
        var inner = new MemoryDocumentStore();
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        await inner.Create("match", Fixture("schema-one-active-match.json"));
        var source = (await inner.Read("match"))!;
        var documents = new ConcurrentMatchUpdateStore(inner, participants: 2);
        var first = new LocalMatchService(catalogue, documents);
        var second = new LocalMatchService(catalogue, documents);

        var results = await Task.WhenAll(
            first.State(profile, profile.DisplayName.Value),
            second.State(profile, profile.DisplayName.Value)
        );
        var migrated = (await inner.Read("match"))!;
        var backup = (await inner.Read(BackupKey("match", source)))!;

        foreach (var result in results)
        {
            result.Error.ShouldBeNull();
            result.View.ShouldNotBeNull();
        }
        JsonSerializer
            .Serialize(results[0].View)
            .ShouldBe(JsonSerializer.Serialize(results[1].View));
        documents.MatchUpdateAttempts.ShouldBe(2);
        documents.ExpectedRevisions.ShouldAllBe(revision => revision == source.Revision);
        migrated.Revision.ShouldBe(source.Revision + 1);
        AssertBackup(backup, "match", source, "match-schema-1-to-2+match-authority-to-checked-out");
    }

    [Test]
    public async Task CancellationAfterBackupCommit_PreservesTheSourceAndRecoverableBackup()
    {
        var inner = new MemoryDocumentStore();
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        await inner.Create("match", Fixture("schema-one-active-match.json"));
        var source = (await inner.Read("match"))!;
        using var cancellation = new CancellationTokenSource();
        var documents = new CancelAfterBackupStore(inner, cancellation);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new LocalMatchService(catalogue, documents).State(
                profile,
                profile.DisplayName.Value,
                cancellation.Token
            )
        );
        var backup = (await inner.Read(BackupKey("match", source)))!;

        (await inner.Read("match")).ShouldBe(source);
        AssertBackup(backup, "match", source, "match-schema-1-to-2+match-authority-to-checked-out");

        var retried = await new LocalMatchService(catalogue, inner).State(
            profile,
            profile.DisplayName.Value
        );
        retried.Error.ShouldBeNull();
        (await inner.Read(BackupKey("match", source))).ShouldBe(backup);
    }

    [Test]
    public async Task CancellationReportedAfterPrimaryCommit_ReconcilesTheExactCommittedCandidate()
    {
        var inner = new MemoryDocumentStore();
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        await inner.Create("match", Fixture("schema-one-active-match.json"));
        var source = (await inner.Read("match"))!;
        using var cancellation = new CancellationTokenSource();
        var documents = new CommitMatchThenCancelStore(inner, cancellation);

        var restored = await new LocalMatchService(catalogue, documents).State(
            profile,
            profile.DisplayName.Value,
            cancellation.Token
        );
        var migrated = (await inner.Read("match"))!;

        restored.Error.ShouldBeNull();
        restored.View.ShouldNotBeNull();
        documents.MatchUpdateAttempts.ShouldBe(1);
        migrated.Revision.ShouldBe(source.Revision + 1);
        (await inner.Read(BackupKey("match", source))).ShouldNotBeNull();
    }

    private static void AssertBackup(
        StoredDocument backup,
        string sourceKey,
        StoredDocument source,
        string migration
    )
    {
        backup.Revision.ShouldBe(1);
        var document = JsonNode.Parse(backup.Json)!.AsObject();
        document["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        document["sourceKey"]!.GetValue<string>().ShouldBe(sourceKey);
        document["sourceRevision"]!.GetValue<long>().ShouldBe(source.Revision);
        document["sourceJson"]!.GetValue<string>().ShouldBe(source.Json);
        document["migration"]!.GetValue<string>().ShouldBe(migration);
    }

    private static string BackupKey(string key, StoredDocument source) =>
        $"match-migration-backup/{key}/{source.Revision}/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Json)))}";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "match-migrations", name));

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    private static LocalProfile Profile(
        BlokemonCatalogue catalogue,
        string fixture = "historical-profile.json"
    )
    {
        var document = JsonNode.Parse(Fixture(fixture))!.AsObject();
        document["profile"]!["authorityManifestVersion"] = catalogue.Mechanics.ManifestVersion;
        var product = JsonSerializer.Deserialize<ProductDocument>(
            document.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        return ProductValue(LocalProfile.Restore(product.Profile, catalogue.Mechanics));
    }

    private static TValue ProductValue<TValue, TFailure>(DomainResult<TValue, TFailure> result)
        where TValue : notnull
        where TFailure : notnull =>
        result is DomainResult<TValue, TFailure>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("The historical profile is not valid now.");

    private sealed class MemoryDocumentStore : IStateDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(
            StringComparer.Ordinal
        );
        private readonly object _lock = new();

        public Task<StoredDocument?> Read(string key, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_documents.GetValueOrDefault(key));
            }
        }

        public Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                if (_documents.ContainsKey(key))
                {
                    return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
                }
                _documents.Add(key, new(1, json));
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(1));
            }
        }

        public Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                if (
                    !_documents.TryGetValue(key, out var current)
                    || current.Revision != expectedRevision
                )
                {
                    return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
                }
                var revision = expectedRevision + 1;
                _documents[key] = new(revision, json);
                return Task.FromResult<DocumentWriteResult>(
                    new DocumentWriteResult.Written(revision)
                );
            }
        }

        public Task Delete(string key, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _documents.Remove(key);
                return Task.CompletedTask;
            }
        }
    }

    private abstract class DelegatingDocumentStore(IStateDocumentStore inner) : IStateDocumentStore
    {
        public virtual Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        ) => inner.Read(key, cancellationToken);

        public virtual Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        ) => inner.Create(key, json, cancellationToken);

        public virtual Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        ) => inner.Update(key, expectedRevision, json, cancellationToken);

        public virtual Task Delete(string key, CancellationToken cancellationToken = default) =>
            inner.Delete(key, cancellationToken);
    }

    private sealed class ConcurrentMatchUpdateStore(IStateDocumentStore inner, int participants)
        : DelegatingDocumentStore(inner)
    {
        private readonly TaskCompletionSource _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly object _lock = new();
        private readonly List<long> _expectedRevisions = [];
        private int _attempts;

        public int MatchUpdateAttempts => Volatile.Read(ref _attempts);

        public long[] ExpectedRevisions
        {
            get
            {
                lock (_lock)
                {
                    return [.. _expectedRevisions];
                }
            }
        }

        public override async Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (key != "match")
            {
                return await base.Update(key, expectedRevision, json, cancellationToken);
            }
            lock (_lock)
            {
                _expectedRevisions.Add(expectedRevision);
            }
            if (Interlocked.Increment(ref _attempts) == participants)
            {
                _ready.SetResult();
            }
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return await base.Update(key, expectedRevision, json, cancellationToken);
        }
    }

    private sealed class CancelAfterBackupStore(
        IStateDocumentStore inner,
        CancellationTokenSource cancellation
    ) : DelegatingDocumentStore(inner)
    {
        public override async Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            var result = await base.Create(key, json, cancellationToken);
            if (key.StartsWith("match-migration-backup/", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return result;
        }
    }

    private sealed class CommitMatchThenCancelStore(
        IStateDocumentStore inner,
        CancellationTokenSource cancellation
    ) : DelegatingDocumentStore(inner)
    {
        private int _matchUpdateAttempts;

        public int MatchUpdateAttempts => Volatile.Read(ref _matchUpdateAttempts);

        public override async Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            var result = await base.Update(key, expectedRevision, json, cancellationToken);
            if (key == "match" && Interlocked.Increment(ref _matchUpdateAttempts) == 1)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return result;
        }
    }

    private sealed class DocumentStoreFixture(
        IStateDocumentStore store,
        SqliteContexts? contexts = null
    ) : IAsyncDisposable
    {
        public IStateDocumentStore Store { get; } = store;

        public static async Task<DocumentStoreFixture> Create(bool sqlite)
        {
            if (!sqlite)
            {
                return new(new MemoryDocumentStore());
            }
            var contexts = new SqliteContexts();
            await using (var database = contexts.CreateDbContext())
            {
                await database.Database.MigrateAsync();
            }
            return new(new StateDocumentStore(contexts), contexts);
        }

        public async ValueTask DisposeAsync()
        {
            if (contexts is not null)
            {
                await contexts.DisposeAsync();
            }
        }
    }

    private sealed class SqliteContexts : IDbContextFactory<BlokemonDbContext>, IAsyncDisposable
    {
        private readonly string _path = Path.Combine(
            AppContext.BaseDirectory,
            $"match-migration-{Guid.NewGuid():N}.db"
        );
        private readonly DbContextOptions<BlokemonDbContext> _options;

        public SqliteContexts()
        {
            _options = new DbContextOptionsBuilder<BlokemonDbContext>()
                .UseSqlite($"Data Source={_path}")
                .Options;
        }

        public BlokemonDbContext CreateDbContext() => new(_options);

        public Task<BlokemonDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = _path + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
