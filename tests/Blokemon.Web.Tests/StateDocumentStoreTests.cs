using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class StateDocumentStoreTests
{
    [Test]
    public async Task ProductFlow_RestartsWithoutDoubleApplyingAPack()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = new LocalApplicationService(
            catalogue,
            store,
            new LocalMatchService(catalogue, store),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
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
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );

        var restartedStore = new StateDocumentStore(database);
        var restarted = new LocalApplicationService(
            catalogue,
            restartedStore,
            new LocalMatchService(catalogue, restartedStore),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        );
        var restored = Value(await restarted.State());

        created.Profile!.DisplayName.ShouldBe("Local Player");
        retried.LastPack!.Id.ShouldBe(opened.LastPack!.Id);
        saved.Decks.Single().IsLegal.ShouldBeTrue();
        restored.LastPack!.Sequence.ShouldBe(1);
        restored.Decks.Single().CardCount.ShouldBe(60);
    }

    [Test]
    public async Task ServerBackedHistoricalProfile_RemainsVisibleAuthorityBoundAndPreserved()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store);
        var profileCommand = Guid.Parse("41111111-1111-1111-1111-111111111111");
        var packCommand = Guid.Parse("42222222-2222-2222-2222-222222222222");
        var deckCommand = Guid.Parse("43333333-3333-3333-3333-333333333333");
        Value(await application.CreateProfile(new(profileCommand, "Local Player")));
        Value(await application.OpenPack(new(packCommand)));
        Value(
            await application.SaveDeck(
                new(
                    deckCommand,
                    null,
                    null,
                    "Historical deck",
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );
        var original = (await store.Read("profile"))!;
        var document = JsonNode.Parse(original.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        profile["authorityManifestVersion"] = "historical-manifest";
        var sampledCards = profile["packReceipts"]![0]!["sampledCollectibleIds"]!.AsArray();
        var replacedCardId = sampledCards[0]!.GetValue<string>();
        const string historicalCollectibleId = "HISTORICAL-COLLECTIBLE";
        const string historicalDeckCardId = "HISTORICAL-DECK-CARD";
        sampledCards[0] = historicalCollectibleId;
        var ownership = profile["collectibleOwnership"]!.AsArray();
        var replacedOwnership = ownership
            .Select(static item => item!.AsObject())
            .Single(item => item["cardId"]!.GetValue<string>() == replacedCardId);
        var remainingQuantity = replacedOwnership["quantity"]!.GetValue<int>() - 1;
        if (remainingQuantity == 0)
        {
            ownership.Remove(replacedOwnership);
        }
        else
        {
            replacedOwnership["quantity"] = remainingQuantity;
        }
        ownership.Add(new JsonObject { ["cardId"] = historicalCollectibleId, ["quantity"] = 1 });
        var deckCards = profile["savedDecks"]![0]!["cards"]!.AsArray();
        deckCards
            .Select(static item => item!.AsObject())
            .Single(item => item["cardId"]!.GetValue<string>() == "VIM-BLAZED")["cardId"] =
            historicalDeckCardId;
        await store.Update("profile", original.Revision, document.ToJsonString());
        var historical = (await store.Read("profile"))!;

        var restarted = Application(catalogue, store);
        var restored = Value(await restarted.State());
        var after = await store.Read("profile");
        var deck = restored.Decks.Single();
        var historicalCollectible = restored.Cards.Single(card =>
            card.Id == historicalCollectibleId
        );
        var historicalDeckCard = restored.Cards.Single(card => card.Id == historicalDeckCardId);

        deck.IsLegal.ShouldBeFalse();
        foreach (var entry in deck.Entries)
        {
            restored.Cards.Count(card => card.Id == entry.CardId).ShouldBe(1);
        }
        historicalCollectible.OwnedQuantity.ShouldBe(1);
        historicalCollectible.FaceHtml.ShouldBe(catalogue.ReverseFaceHtml);
        historicalDeckCard.OwnedQuantity.ShouldBe(0);
        historicalDeckCard.FreelyAvailable.ShouldBeFalse();
        historicalDeckCard.FaceHtml.ShouldBe(catalogue.ReverseFaceHtml);
        restored.LastPack!.Cards.Any(card => card.Id == historicalCollectibleId).ShouldBeTrue();
        restored.Cards.Single(card => card.Id == "BLK-001").Type.ShouldNotBe("Historical");
        after.ShouldBe(historical);

        var revised = Value(
            await restarted.SaveDeck(
                new(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    deck.Id,
                    deck.Revision,
                    "Current deck",
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );
        var persisted = (await store.Read("profile"))!;
        var persistedDocument = JsonNode.Parse(persisted.Json)!.AsObject();
        var persistedProfile = persistedDocument["profile"]!.AsObject();
        var persistedDeck = persistedProfile["savedDecks"]![0]!.AsObject();
        var persistedCardIds = persistedDeck["cards"]!
            .AsArray()
            .Select(static item => item!["cardId"]!.GetValue<string>())
            .ToArray();
        var pack = await restarted.OpenPack(
            new(Guid.Parse("45555555-5555-5555-5555-555555555555"))
        );

        revised.Decks.Single().IsLegal.ShouldBeTrue();
        revised
            .Decks.Single()
            .Entries.Select(static entry => entry.CardId)
            .ShouldBe(["BLK-001", "VIM-BLAZED"], ignoreOrder: true);
        (persistedProfile["authorityManifestVersion"]!.GetValue<string>()).ShouldBe(
            "historical-manifest"
        );
        persistedDeck["revision"]!.GetValue<long>().ShouldBe(deck.Revision + 1);
        persistedCardIds.ShouldBe(["BLK-001", "VIM-BLAZED"], ignoreOrder: true);
        Error(pack).Code.ShouldBe("pack.authority_changed");
        Error(pack)
            .Message.ShouldBe(
                "This saved player cannot use the current card set. No data changed."
            );
        (await store.Read("profile")).ShouldBe(persisted);
    }

    [Test]
    public async Task HistoricalProfile_CurrentDeckSupportsRevisionCasAndStartsMatch()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("61111111-1111-1111-1111-111111111111"), "Local Player")
            )
        );
        var saved = Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("62222222-2222-2222-2222-222222222222"),
                    null,
                    null,
                    "Current deck",
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );
        var original = (await store.Read("profile"))!;
        var document = JsonNode.Parse(original.Json)!.AsObject();
        document["profile"]!["authorityManifestVersion"] = "historical-manifest";
        await store.Update("profile", original.Revision, document.ToJsonString());
        var historical = (await store.Read("profile"))!;
        var restarted = Application(catalogue, store);

        var current = Value(await restarted.State());
        var currentDeck = current.Decks.Single();
        var revised = Value(
            await restarted.SaveDeck(
                new(
                    Guid.Parse("63333333-3333-3333-3333-333333333333"),
                    currentDeck.Id,
                    currentDeck.Revision,
                    "Revised current deck",
                    currentDeck.Entries
                )
            )
        );
        var afterRevision = (await store.Read("profile"))!;
        var stale = await restarted.SaveDeck(
            new(
                Guid.Parse("64444444-4444-4444-4444-444444444444"),
                currentDeck.Id,
                currentDeck.Revision,
                "Stale overwrite",
                currentDeck.Entries
            )
        );
        var started = Value(
            await restarted.StartMatch(
                new(Guid.Parse("65555555-5555-5555-5555-555555555555"), saved.Decks.Single().Id)
            )
        );

        currentDeck.IsLegal.ShouldBeTrue();
        currentDeck.Errors.ShouldBeEmpty();
        revised.Decks.Single().Revision.ShouldBe(currentDeck.Revision + 1);
        Error(stale).Code.ShouldBe("deck.stale");
        (await store.Read("profile")).ShouldBe(afterRevision);
        started.Match.ShouldNotBeNull();
        afterRevision.ShouldNotBe(historical);
    }

    [Test]
    [Arguments("profile")]
    [Arguments("deck")]
    [Arguments("receipt")]
    public async Task PersistedNonGuidWebIdentity_IsTypedAndNonMutating(string identity)
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("51111111-1111-1111-1111-111111111111"), "Local Player")
            )
        );
        Value(await application.OpenPack(new(Guid.Parse("52222222-2222-2222-2222-222222222222"))));
        Value(await application.OpenPack(new(Guid.Parse("52222222-2222-2222-2222-222222222223"))));
        Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("53333333-3333-3333-3333-333333333333"),
                    null,
                    null,
                    "Local deck",
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );
        var original = (await store.Read("profile"))!;
        var document = JsonNode.Parse(original.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        switch (identity)
        {
            case "profile":
                profile["profileId"] = "not-a-guid";
                break;
            case "deck":
                profile["savedDecks"]![0]!["deckId"] = "not-a-guid";
                break;
            case "receipt":
                profile["packReceipts"]![0]!["receiptId"] = "not-a-guid";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(identity));
        }
        await store.Update("profile", original.Revision, document.ToJsonString());
        var invalid = (await store.Read("profile"))!;

        var response = await Application(catalogue, store).State();
        var after = await store.Read("profile");

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe("state.invalid");
        response.Value.ShouldBeNull();
        after.ShouldBe(invalid);
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

        created.ShouldBe(new DocumentWriteResult.Written(1));
        committed.ShouldBe(new DocumentWriteResult.Written(2));
        stale.ShouldBeOfType<DocumentWriteResult.Conflict>();
        stored.ShouldBe(new StoredDocument(2, """{"name":"Committed"}"""));
    }

    [Test]
    public async Task DuplicateCreate_PreservesTheFirstDocument()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);

        var first = await store.Create("profile", """{"name":"First"}""");
        var duplicate = await store.Create("profile", """{"name":"Duplicate"}""");
        var stored = await store.Read("profile");

        first.ShouldBe(new DocumentWriteResult.Written(1));
        duplicate.ShouldBeOfType<DocumentWriteResult.Conflict>();
        stored.ShouldBe(new StoredDocument(1, """{"name":"First"}"""));
    }

    [Test]
    public async Task RevisionCheckedDelete_RejectsDifferentBytesAndIsIdempotentAfterCommit()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);
        const string original = """{"battle":"original"}""";
        await store.Create("match", original);

        var stale = await store.DeleteIfUnchanged("match", 1, """{"battle":"replacement"}""");
        var deleted = await store.DeleteIfUnchanged("match", 1, original);
        var repeated = await store.DeleteIfUnchanged("match", 1, original);

        stale.ShouldBeOfType<DocumentDeleteResult.Conflict>();
        deleted.ShouldBeOfType<DocumentDeleteResult.Deleted>();
        repeated.ShouldBeOfType<DocumentDeleteResult.Missing>();
        (await store.Read("match")).ShouldBeNull();
    }

    [Test]
    public async Task KeyOverTheBound_IsRefusedWithATypedErrorWhileTheBoundItselfIsAccepted()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);
        var atTheBound = new string('k', StateDocument.MaximumKeyLength);
        var overTheBound = atTheBound + "k";

        var accepted = await store.Create(atTheBound, "{}");
        var refused = await Should.ThrowAsync<DocumentStorageException>(() =>
            store.Create(overTheBound, "{}")
        );
        var refusedRead = await Should.ThrowAsync<DocumentStorageException>(() =>
            store.Read(overTheBound)
        );

        accepted.ShouldBe(new DocumentWriteResult.Written(1));
        refused.Failure.ShouldBe(DocumentStorageFailure.Rejected);
        refusedRead.Failure.ShouldBe(DocumentStorageFailure.Rejected);
        (await store.List("k")).Select(static summary => summary.Key).ShouldBe([atTheBound]);
    }

    [Test]
    public async Task EveryDefinedKey_FitsTheBoundAndTheApprovalKeyIs82Characters()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);
        var account = AccountId.Mint();
        var tenant = TenantId.Mint();
        var playerKeys = PlayerDocumentKeysModule.forAccount(account);
        var provider = Value(
            IdentityProviderName.Create(new string('p', IdentityProviderName.MaximumLength))
        );
        var subject = Value(ExternalSubject.Create(new string('s', ExternalSubject.MaximumLength)));
        string[] keys =
        [
            playerKeys.Profile,
            playerKeys.Match,
            playerKeys.MatchHistory,
            TenancyDocuments.tenantKey(tenant),
            TenancyDocuments.accountKey(account),
            TenancyDocuments.linkKey(provider, subject),
            TenancyDocuments.approvalKey(account, tenant),
        ];

        foreach (var key in keys)
        {
            (await store.Create(key, "{}")).ShouldBe(new DocumentWriteResult.Written(1));
        }

        TenancyDocuments.approvalKey(account, tenant).Length.ShouldBe(82);
        keys.ShouldAllBe(key => key.Length <= StateDocument.MaximumKeyLength);
    }

    [Test]
    public async Task Listing_ReturnsKeysRevisionsAndTheDeclaredProjectionsAndNoBody()
    {
        await using var database = await TestDatabase.Create();
        var store = new StateDocumentStore(database);
        var account = AccountId.Mint();
        var tenant = TenantId.Mint();
        var createdAt = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = createdAt.AddHours(8);
        var accountJson = JsonSerializer.Serialize(
            TenancyDocuments.newAccount(account, createdAt),
            TenancyDocuments.json
        );
        var tenantJson = JsonSerializer.Serialize(
            TenancyDocuments.newTenant(
                tenant,
                Value(TenantSlug.Create("the-regular")),
                "The Regular",
                createdAt
            ),
            TenancyDocuments.json
        );
        await store.Create(TenancyDocuments.accountKey(account), accountJson);
        await store.Update(TenancyDocuments.accountKey(account), 1, accountJson);
        await store.Create(TenancyDocuments.tenantKey(tenant), tenantJson);
        await store.Create(
            "session/one",
            $$"""{"expiresAt":"{{expiresAt:O}}","token":"never-listed","status":"never-listed"}"""
        );
        await store.Create("handoff/one", """{"kind":"Channel"}""");
        await store.Create(
            PlayerDocumentKeysModule.forAccount(account).Profile,
            """{"status":"never-projected","createdAt":"2026-09-03T12:00:00+00:00"}"""
        );
        await store.Create("link/example/one", """{"status":"never-projected"}""");
        await store.Create("account/damaged", "not json");

        var accounts = await store.List("account/");
        var sessions = await store.List("session/");
        var everything = await store.List("");

        accounts
            .Select(static summary => summary.Key)
            .ShouldBe([TenancyDocuments.accountKey(account), "account/damaged"]);
        var listedAccount = accounts.Single(summary =>
            summary.Key == TenancyDocuments.accountKey(account)
        );
        listedAccount.Revision.ShouldBe(2);
        listedAccount.Projection.ShouldBe(
            new DocumentProjection.Lifecycle("Active", createdAt, null)
        );
        accounts
            .Single(static summary => summary.Key == "account/damaged")
            .Projection.ShouldBeNull();
        everything
            .Single(summary => summary.Key == TenancyDocuments.tenantKey(tenant))
            .Projection.ShouldBe(new DocumentProjection.Lifecycle("Active", createdAt, null));
        sessions.Single().Projection.ShouldBe(new DocumentProjection.Expiry(expiresAt));
        everything
            .Single(static summary => summary.Key == "handoff/one")
            .Projection.ShouldBe(new DocumentProjection.Expiry(null));
        everything
            .Where(static summary =>
                summary.Key.StartsWith("a/", StringComparison.Ordinal)
                || summary.Key.StartsWith("link/", StringComparison.Ordinal)
            )
            .ShouldAllBe(static summary => summary.Projection == null);
        everything.Count.ShouldBe(7);
        typeof(DocumentSummary).GetProperty("Json").ShouldBeNull();
    }

    [Test]
    public async Task AccountScopedKeysMigration_DeletesTheLegacyRowsOnceAndLeavesEveryOtherRow()
    {
        await using var database = await TestDatabase.CreateAsInitialStateLeftIt();
        var store = new StateDocumentStore(database);
        await store.Create("profile", """{"legacy":"profile"}""");
        await store.Create("match", """{"legacy":"match"}""");
        await store.Create("match-history", """{"legacy":"history"}""");
        await store.Create("a/other/profile", """{"kept":"profile"}""");
        await store.Update("a/other/profile", 1, """{"kept":"profile-2"}""");
        await store.Create("match-migration-backup/sentinel", """{"kept":"backup"}""");
        await store.Create("profiles", """{"kept":"prefix-neighbour"}""");

        await database.Migrate();
        var afterFirst = await store.List("");
        await database.Migrate();
        var afterSecond = await store.List("");
        await store.Create("profile", """{"recreated":"profile"}""");
        await database.Migrate();
        var recreated = await store.Read("profile");
        var widened = await store.Create(new string('w', StateDocument.MaximumKeyLength), "{}");

        afterFirst
            .Select(static summary => (summary.Key, summary.Revision))
            .ShouldBe([
                ("a/other/profile", 2L),
                ("match-migration-backup/sentinel", 1L),
                ("profiles", 1L),
            ]);
        afterSecond.ShouldBe(afterFirst);
        (await store.Read("a/other/profile")).ShouldBe(
            new StoredDocument(2, """{"kept":"profile-2"}""")
        );
        recreated.ShouldBe(new StoredDocument(1, """{"recreated":"profile"}"""));
        widened.ShouldBe(new DocumentWriteResult.Written(1));
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
            await database.Migrate();
            return database;
        }

        /// <summary>
        /// A database as the first migration left it, with that migration recorded as applied:
        /// the schema is built from the migration's own operations, so what the start-up
        /// migration then does to it is what it does to a real legacy database.
        /// </summary>
        public static async Task<TestDatabase> CreateAsInitialStateLeftIt()
        {
            const string initialStateId = "202608140001_InitialState";
            var database = new TestDatabase(
                Path.Combine(AppContext.BaseDirectory, $"state-{Guid.NewGuid():N}.db")
            );
            await using var context = database.CreateDbContext();
            var migrations = context.GetService<IMigrationsAssembly>();
            var initialState = migrations.CreateMigration(
                migrations.Migrations[initialStateId],
                context.Database.ProviderName!
            );
            var commands = context
                .GetService<IMigrationsSqlGenerator>()
                .Generate(initialState.UpOperations, initialState.TargetModel);
            foreach (var command in commands)
            {
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            }
            var history = context.GetService<IHistoryRepository>();
            await context.Database.ExecuteSqlRawAsync(history.GetCreateScript());
            await context.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(initialStateId, ProductInfo.GetVersion()))
            );
            return database;
        }

        /// <summary>The start-up migration, as Program.cs runs it.</summary>
        public async Task Migrate()
        {
            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
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

    private static LocalApplicationService Application(
        BlokemonCatalogue catalogue,
        StateDocumentStore store
    ) =>
        new(
            catalogue,
            store,
            new LocalMatchService(catalogue, store),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        );

    private static ApplicationView Value(ApiResponse<MatchMutationView> response) =>
        Value<MatchMutationView>(response).Application;

    private static T Value<T>(ApiResponse<T> response)
        where T : class
    {
        if (!response.Succeeded || response.Value is null)
        {
            throw new InvalidOperationException(response.Error?.Message);
        }
        return response.Value;
    }

    private static T Value<T, TFailure>(DomainResult<T, TFailure> result) =>
        result.Match(
            static value => value,
            static failure => throw new InvalidOperationException(failure!.ToString())
        );

    private static ApiError Error<T>(ApiResponse<T> response)
    {
        if (response.Succeeded || response.Error is null)
        {
            throw new InvalidOperationException("Expected an API failure.");
        }
        return response.Error;
    }
}
