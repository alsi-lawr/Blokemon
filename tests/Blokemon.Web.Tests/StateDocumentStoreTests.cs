using Blokemon.Web.Application;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blokemon.Web.Tests;

public sealed class StateDocumentStoreTests
{
    [Test]
    public async Task ProductFlow_RestartsWithoutDoubleApplyingAPack()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogue.Load(Path.Combine(AppContext.BaseDirectory, "content"));
        var store = new StateDocumentStore(database);
        var application = new LocalApplicationService(
            catalogue,
            store,
            new LocalMatchService(catalogue, store)
        );
        var createCommand = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var packCommand = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var deckCommand = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var created = Value(
            await application.CreateProfile(new(createCommand, "  Local Player  "))
        );
        var opened = Value(await application.OpenPack(new(packCommand)));
        var retried = Value(await application.OpenPack(new(packCommand)));
        var saved = Value(
            await application.SaveDeck(
                new(
                    deckCommand,
                    null,
                    null,
                    "The Friday stack",
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
                )
            )
        );

        var restartedStore = new StateDocumentStore(database);
        var restarted = new LocalApplicationService(
            catalogue,
            restartedStore,
            new LocalMatchService(catalogue, restartedStore)
        );
        var restored = Value(await restarted.State());

        await Assert.That(created.Profile!.DisplayName).IsEqualTo("Local Player");
        await Assert.That(opened.Profile!.UnopenedPacks).IsEqualTo(9);
        await Assert.That(retried.Profile!.UnopenedPacks).IsEqualTo(9);
        await Assert.That(retried.LastPack!.Id).IsEqualTo(opened.LastPack!.Id);
        await Assert.That(saved.Decks.Single().IsLegal).IsTrue();
        await Assert.That(restored.Profile!.UnopenedPacks).IsEqualTo(9);
        await Assert.That(restored.LastPack!.Sequence).IsEqualTo(1);
        await Assert.That(restored.Decks.Single().CardCount).IsEqualTo(60);
    }

    [Test]
    public async Task StaleWrite_DoesNotOverwriteCommittedDocument()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);
        var created = await store.Create("profile", """{"name":"First"}""");
        var committed = await store.Update("profile", 1, """{"name":"Committed"}""");

        var stale = await store.Update("profile", 1, """{"name":"Stale"}""");
        var stored = await store.Read("profile");

        await Assert.That(created).IsEqualTo(new DocumentWriteResult.Written(1));
        await Assert.That(committed).IsEqualTo(new DocumentWriteResult.Written(2));
        await Assert.That(stale).IsTypeOf<DocumentWriteResult.Conflict>();
        await Assert.That(stored).IsEqualTo(new StoredDocument(2, """{"name":"Committed"}"""));
    }

    [Test]
    public async Task DuplicateCreate_PreservesTheFirstDocument()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);

        var first = await store.Create("profile", """{"name":"First"}""");
        var duplicate = await store.Create("profile", """{"name":"Duplicate"}""");
        var stored = await store.Read("profile");

        await Assert.That(first).IsEqualTo(new DocumentWriteResult.Written(1));
        await Assert.That(duplicate).IsTypeOf<DocumentWriteResult.Conflict>();
        await Assert.That(stored).IsEqualTo(new StoredDocument(1, """{"name":"First"}"""));
    }

    private sealed class TestDatabase : IDbContextFactory<BlokemonDbContext>, IAsyncDisposable
    {
        private readonly string _path;
        private readonly DbContextOptions<BlokemonDbContext> _options;

        private TestDatabase(string path)
        {
            _path = path;
            _options = new DbContextOptionsBuilder<BlokemonDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public static async Task<TestDatabase> Create()
        {
            var database = new TestDatabase(
                Path.Combine(AppContext.BaseDirectory, $"state-{Guid.NewGuid():N}.db")
            );
            await using var context = database.CreateDbContext();
            await context.Database.MigrateAsync();
            return database;
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

    private static T Value<T>(ApiResponse<T> response)
        where T : class
    {
        if (!response.Succeeded || response.Value is null)
        {
            throw new InvalidOperationException(response.Error?.Message);
        }
        return response.Value;
    }
}
