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
            EconomyRules.Unlimited
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
            new LocalMatchService(catalogue, restartedStore),
            EconomyRules.Unlimited
        );
        var restored = Value(await restarted.State());

        created.Profile!.DisplayName.ShouldBe("Local Player");
        retried.LastPack!.Id.ShouldBe(opened.LastPack!.Id);
        saved.Decks.Single().IsLegal.ShouldBeTrue();
        restored.LastPack!.Sequence.ShouldBe(1);
        restored.Decks.Single().CardCount.ShouldBe(60);
    }

    [Test]
    public async Task UnavailableHistoricalCards_MigrateRemainVisibleAndSurviveASecondRestart()
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
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
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
            .Single(item => item["cardId"]!.GetValue<string>() == "VIM-DODGY")["cardId"] =
            historicalDeckCardId;
        await store.Update("profile", original.Revision, document.ToJsonString());
        var historical = (await store.Read("profile"))!;

        var restarted = Application(catalogue, store);
        var restored = Value(await restarted.State());
        var migrated = (await store.Read("profile"))!;
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
        migrated.Revision.ShouldBe(historical.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedMigratedProfile(historical, catalogue)
            )
            .ShouldBeTrue();

        var secondRestart = Application(catalogue, store);
        Value(await secondRestart.State());
        (await store.Read("profile")).ShouldBe(migrated);

        var revised = Value(
            await secondRestart.SaveDeck(
                new(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    deck.Id,
                    deck.Revision,
                    "Current deck",
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
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
        var newPackCommand = Guid.Parse("45555555-5555-5555-5555-555555555555");
        var pack = Value(await secondRestart.OpenPack(new(newPackCommand)));
        var afterPack = (await store.Read("profile"))!;
        var retried = Value(await secondRestart.OpenPack(new(newPackCommand)));

        revised.Decks.Single().IsLegal.ShouldBeTrue();
        revised
            .Decks.Single()
            .Entries.Select(static entry => entry.CardId)
            .ShouldBe(["BLK-001", "VIM-DODGY"], ignoreOrder: true);
        (persistedProfile["authorityManifestVersion"]!.GetValue<string>()).ShouldBe(
            catalogue.Mechanics.ManifestVersion
        );
        persistedProfile["historicalAuthorityManifestVersions"]!
            .AsArray()
            .Select(static version => version!.GetValue<string>())
            .ShouldBe(["historical-manifest"]);
        persistedDeck["revision"]!.GetValue<long>().ShouldBe(deck.Revision + 1);
        persistedCardIds.ShouldBe(["BLK-001", "VIM-DODGY"], ignoreOrder: true);
        pack.LastPack!.Sequence.ShouldBe(2);
        retried.LastPack!.Id.ShouldBe(pack.LastPack.Id);
        retried.LastPack.Sequence.ShouldBe(pack.LastPack.Sequence);
        retried
            .LastPack.Cards.Select(static card => card.Id)
            .ShouldBe(pack.LastPack.Cards.Select(static card => card.Id));
        (await store.Read("profile")).ShouldBe(afterPack);
        retried.Cards.Single(card => card.Id == historicalCollectibleId).OwnedQuantity.ShouldBe(1);
    }

    [Test]
    public async Task CurrentCardProfile_MigratesExactSnapshotBeforeFurtherOperations()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store, Classic(3));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("61111111-1111-1111-1111-111111111111"), "Local Player")
            )
        );
        Value(await application.OpenPack(new(Guid.Parse("61222222-2222-2222-2222-222222222222"))));
        Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("61333333-3333-3333-3333-333333333333"), "growroom")
            )
        );
        Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("61444444-4444-4444-4444-444444444444"),
                    null,
                    null,
                    "Current deck",
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
                )
            )
        );
        var original = (await store.Read("profile"))!;
        var historicalJson = JsonNode.Parse(original.Json)!.AsObject();
        historicalJson["profile"]!["authorityManifestVersion"] = "historical-manifest";
        await store.Update("profile", original.Revision, historicalJson.ToJsonString());
        var historical = (await store.Read("profile"))!;

        var restarted = Application(catalogue, store);
        var current = Value(await restarted.State());
        var migrated = (await store.Read("profile"))!;

        current.Profile!.DisplayName.ShouldBe("Local Player");
        current.Profile.StarterDeckId.ShouldBe("growroom");
        current.Profile.RemainingPacks.ShouldBe(2);
        current.Profile.StarterClaimUsed.ShouldBe(true);
        current.LastPack!.Sequence.ShouldBe(1);
        current.Decks.Length.ShouldBe(2);
        migrated.Revision.ShouldBe(historical.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedMigratedProfile(historical, catalogue)
            )
            .ShouldBeTrue();

        var secondRestart = Application(catalogue, store);
        Value(await secondRestart.State());
        (await store.Read("profile")).ShouldBe(migrated);

        var command = Guid.Parse("61555555-5555-5555-5555-555555555555");
        var opened = Value(await secondRestart.OpenPack(new(command)));
        var afterOpen = (await store.Read("profile"))!;
        var retried = Value(await secondRestart.OpenPack(new(command)));

        opened.LastPack!.Sequence.ShouldBe(2);
        opened.Profile!.RemainingPacks.ShouldBe(1);
        opened.Profile.StarterClaimUsed.ShouldBe(true);
        retried.LastPack!.Id.ShouldBe(opened.LastPack.Id);
        retried.LastPack.Sequence.ShouldBe(opened.LastPack.Sequence);
        retried
            .LastPack.Cards.Select(static card => card.Id)
            .ShouldBe(opened.LastPack.Cards.Select(static card => card.Id));
        (await store.Read("profile")).ShouldBe(afterOpen);
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
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
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
    public async Task ProfileMigrationConflict_ReturnsTypedFailureAndPreservesHistoricalDocument()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("71111111-1111-1111-1111-111111111111"), "Local Player")
            )
        );
        var original = (await store.Read("profile"))!;
        var document = JsonNode.Parse(original.Json)!.AsObject();
        document["profile"]!["authorityManifestVersion"] = "historical-manifest";
        await store.Update("profile", original.Revision, document.ToJsonString());
        var historical = (await store.Read("profile"))!;

        var response = await Application(catalogue, new ConflictOnUpdateStore(store)).State();

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe("state.conflict");
        response.Value.ShouldBeNull();
        (await store.Read("profile")).ShouldBe(historical);
    }

    [Test]
    public async Task ProfileMigration_DoesNotBypassSavedMatchAuthorityGate()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var store = new StateDocumentStore(database);
        var application = Application(catalogue, store);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("81111111-1111-1111-1111-111111111111"), "Local Player")
            )
        );
        var saved = Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("82222222-2222-2222-2222-222222222222"),
                    null,
                    null,
                    "Current deck",
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
                )
            )
        );
        Value(
            await application.StartMatch(
                new(Guid.Parse("83333333-3333-3333-3333-333333333333"), saved.Decks.Single().Id)
            )
        );

        var profileBefore = (await store.Read("profile"))!;
        var historicalProfile = JsonNode.Parse(profileBefore.Json)!.AsObject();
        historicalProfile["profile"]!["authorityManifestVersion"] = "historical-manifest";
        await store.Update("profile", profileBefore.Revision, historicalProfile.ToJsonString());
        var historical = (await store.Read("profile"))!;

        var matchBefore = (await store.Read("match"))!;
        var incompatibleMatch = JsonNode.Parse(matchBefore.Json)!.AsObject();
        incompatibleMatch["authorityVersion"] = "historical-match-authority";
        await store.Update("match", matchBefore.Revision, incompatibleMatch.ToJsonString());
        var preservedMatch = (await store.Read("match"))!;

        var state = Value(await Application(catalogue, store).State());
        var migrated = (await store.Read("profile"))!;

        state.Match.ShouldBeNull();
        state.MatchError!.Code.ShouldBe("match.authority_changed");
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedMigratedProfile(historical, catalogue)
            )
            .ShouldBeTrue();
        (await store.Read("match")).ShouldBe(preservedMatch);
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
                    [new("BLK-001", 1), new("VIM-DODGY", 59)]
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

    private static LocalApplicationService Application(
        BlokemonCatalogue catalogue,
        IStateDocumentStore store,
        EconomyRules? economy = null
    ) =>
        new(
            catalogue,
            store,
            new LocalMatchService(catalogue, store),
            economy ?? EconomyRules.Unlimited
        );

    private static EconomyRules Classic(int packAllowance) =>
        EconomyRules
            .Classic(packAllowance)
            .Match(
                static rules => rules,
                static failure =>
                    throw new InvalidOperationException($"Expected classic rules, got {failure}.")
            );

    private static JsonObject ExpectedMigratedProfile(
        StoredDocument historical,
        BlokemonCatalogue catalogue
    )
    {
        var expected = JsonNode.Parse(historical.Json)!.AsObject();
        var profile = expected["profile"]!.AsObject();
        var historicalVersion = profile["authorityManifestVersion"]!.GetValue<string>();
        profile["authorityManifestVersion"] = catalogue.Mechanics.ManifestVersion;
        profile["historicalAuthorityManifestVersions"] = new JsonArray(historicalVersion);

        var currentCardIds = catalogue
            .Cards.Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var unavailableCardIds = new HashSet<string>(StringComparer.Ordinal);

        void AddIfUnavailable(JsonNode? cardId)
        {
            var value = cardId!.GetValue<string>();
            if (!currentCardIds.Contains(value))
            {
                unavailableCardIds.Add(value);
            }
        }

        foreach (var cardId in profile["unavailableHistoricalCardIds"]!.AsArray())
        {
            unavailableCardIds.Add(cardId!.GetValue<string>());
        }
        AddIfUnavailable(profile["guaranteedRegularCollectibleId"]);
        foreach (var ownership in profile["collectibleOwnership"]!.AsArray())
        {
            AddIfUnavailable(ownership!["cardId"]);
        }
        foreach (var receipt in profile["packReceipts"]!.AsArray())
        {
            foreach (var cardId in receipt!["sampledCollectibleIds"]!.AsArray())
            {
                AddIfUnavailable(cardId);
            }
        }
        foreach (var deck in profile["savedDecks"]!.AsArray())
        {
            foreach (var card in deck!["cards"]!.AsArray())
            {
                AddIfUnavailable(card!["cardId"]);
            }
        }
        foreach (var claim in profile["starterDeckClaims"]!.AsArray())
        {
            foreach (var grant in claim!["collectibleGrants"]!.AsArray())
            {
                AddIfUnavailable(grant!["cardId"]);
            }
        }

        var persistedUnavailableCardIds = new JsonArray();
        foreach (var cardId in unavailableCardIds.Order(StringComparer.Ordinal))
        {
            persistedUnavailableCardIds.Add(cardId);
        }
        profile["unavailableHistoricalCardIds"] = persistedUnavailableCardIds;
        return expected;
    }

    private sealed class ConflictOnUpdateStore(IStateDocumentStore inner) : IStateDocumentStore
    {
        public Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        ) => inner.Read(key, cancellationToken);

        public Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        ) => inner.Create(key, json, cancellationToken);

        public Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());

        public Task Delete(string key, CancellationToken cancellationToken = default) =>
            inner.Delete(key, cancellationToken);
    }

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

    private static ApiError Error<T>(ApiResponse<T> response)
    {
        if (response.Succeeded || response.Error is null)
        {
            throw new InvalidOperationException("Expected an API failure.");
        }
        return response.Error;
    }
}
