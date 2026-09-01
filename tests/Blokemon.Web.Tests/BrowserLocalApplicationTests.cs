using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class BrowserLocalApplicationTests
{
    [Test]
    public async Task BrowserAndServerModes_KeepSeparateProfilesAndOnlyServerModeUsesHttp()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var serverProfileId = Guid.Parse("91111111-1111-1111-1111-111111111111");
        var server = new ServerHandler(
            new(
                new(serverProfileId, "Server Player", 7, "growroom"),
                [],
                [],
                [],
                catalogue.PackPresentation,
                null,
                null,
                null
            )
        );
        var application = Application(catalogue, documents, server);

        Value(await application.SelectMode(PlayMode.BrowserLocal));
        var browserProfile = Value(
            await application.CreateProfile(
                new(Guid.Parse("92222222-2222-2222-2222-222222222222"), "Browser Player")
            )
        );

        server.Requests.ShouldBeEmpty();

        Value(await application.SelectMode(PlayMode.ServerBacked));
        var serverState = Value(await application.State());

        server.Requests.ShouldBe(["GET api/state"]);
        serverState.Profile!.Id.ShouldBe(serverProfileId);
        serverState.Profile.DisplayName.ShouldBe("Server Player");

        Value(await application.SelectMode(PlayMode.BrowserLocal));
        var restoredBrowserState = Value(await application.State());

        restoredBrowserState.Profile!.Id.ShouldBe(browserProfile.Profile!.Id);
        restoredBrowserState.Profile.DisplayName.ShouldBe("Browser Player");
        server.Requests.ShouldBe(["GET api/state"]);
    }

    [Test]
    public async Task ServerMode_ForwardsTheTwoRecoveryCapabilitiesToTheirSeparateRoutes()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var returned = new ApplicationView(
            null,
            [],
            [],
            [],
            catalogue.PackPresentation,
            null,
            null,
            null
        );
        var server = new ServerHandler(returned);
        var application = Application(catalogue, documents, server);
        Value(await application.SelectMode(PlayMode.ServerBacked));

        Value(await application.AbandonSavedMatch(new(3, "active-identity")));
        Value(await application.DiscardMatchHistory(new(4, "history-identity")));

        server.Requests.ShouldBe(["POST api/matches/abandon", "POST api/matches/history/discard"]);
    }

    [Test]
    public async Task BrowserDocumentWrites_RejectDuplicateAndStaleChangesWithoutOverwriting()
    {
        var documents = new MemoryDocumentStore();

        var created = await documents.Create("profile", "first");
        var duplicate = await documents.Create("profile", "duplicate");
        var updated = await documents.Update("profile", 1, "committed");
        var stale = await documents.Update("profile", 1, "stale");

        created.ShouldBe(new DocumentWriteResult.Written(1));
        duplicate.ShouldBeOfType<DocumentWriteResult.Conflict>();
        updated.ShouldBe(new DocumentWriteResult.Written(2));
        stale.ShouldBeOfType<DocumentWriteResult.Conflict>();
        (await documents.Read("profile")).ShouldBe(new StoredDocument(2, "committed"));
    }

    [Test]
    public async Task BrowserRevisionCheckedDelete_RejectsDifferentBytesAndIsIdempotentAfterCommit()
    {
        var documents = new MemoryDocumentStore();
        await documents.Create("match", "original");

        var stale = await documents.DeleteIfUnchanged("match", 1, "replacement");
        var deleted = await documents.DeleteIfUnchanged("match", 1, "original");
        var repeated = await documents.DeleteIfUnchanged("match", 1, "original");

        stale.ShouldBeOfType<DocumentDeleteResult.Conflict>();
        deleted.ShouldBeOfType<DocumentDeleteResult.Deleted>();
        repeated.ShouldBeOfType<DocumentDeleteResult.Missing>();
        (await documents.Read("match")).ShouldBeNull();
    }

    [Test]
    [Arguments(DocumentStorageFailure.Unavailable, "storage.unavailable")]
    [Arguments(DocumentStorageFailure.Full, "storage.full")]
    [Arguments(DocumentStorageFailure.Rejected, "storage.rejected")]
    public async Task BrowserStorageFailure_ReportsPlainFailureAndPreservesTheLastProfileDocument(
        DocumentStorageFailure failure,
        string errorCode
    )
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(catalogue, documents, new ServerHandler(null));
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("93333333-3333-3333-3333-333333333333"), "Browser Player")
            )
        );
        var before = await documents.Read("profile");
        documents.FailNextWrite = failure;

        var response = await application.OpenPack(
            new(Guid.Parse("94444444-4444-4444-4444-444444444444"))
        );

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe(errorCode);
        response.Error.Message.ShouldContain("last saved game is unchanged", Case.Sensitive);
        (await documents.Read("profile")).ShouldBe(before);
    }

    [Test]
    public async Task BrowserRecoveryStorageFailure_PreservesEveryDocumentAndTheTypedGate()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(catalogue, documents, new ServerHandler(null));
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("94511111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("94522222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        Value(
            await application.StartMatch(
                new(Guid.Parse("94533333-3333-3333-3333-333333333333"), claimed.Decks.Single().Id)
            )
        );
        var current = (await documents.Read("match"))!;
        var incompatible = JsonNode.Parse(current.Json)!.AsObject();
        incompatible["authorityVersion"] = "arbitrary-authority";
        await documents.Update("match", current.Revision, incompatible.ToJsonString());
        var source = (await documents.Read("match"))!;
        var profile = (await documents.Read("profile"))!;
        var recovery = Value(await application.State()).MatchRecovery!;
        documents.FailNextWrite = DocumentStorageFailure.Full;

        var failed = await application.AbandonSavedMatch(
            new(recovery.Revision, recovery.ContentIdentity)
        );

        failed.Succeeded.ShouldBeFalse();
        failed.Error!.Code.ShouldBe("storage.full");
        (await documents.Read("match")).ShouldBe(source);
        (await documents.Read("profile")).ShouldBe(profile);
        Value(await application.State()).MatchRecovery.ShouldBe(recovery);
    }

    [Test]
    public async Task BrowserCompatibleAuthorityMigration_PreservesProfileAndReplaysWithoutAnotherWrite()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server, economy: Classic(3));
        var historical = await CreateHistoricalProfile(application, documents);
        var historicalProfileId = Guid.Parse(
            JsonNode.Parse(historical.Json)!["profile"]!["profileId"]!.GetValue<string>()
        );

        var restarted = Application(catalogue, documents, server, economy: Classic(3));
        var restored = Value(await restarted.State());
        var migrated = (await documents.Read("profile"))!;

        restored.Profile!.DisplayName.ShouldBe("Browser Player");
        restored.Profile.Id.ShouldBe(historicalProfileId);
        restored.Profile.RemainingPacks.ShouldBe(2);
        restored.Profile.StarterClaimUsed.ShouldBe(true);
        restored.LastPack!.Sequence.ShouldBe(1);
        restored.Decks.Length.ShouldBe(2);
        restored.StarterDecks.Single(static starter => starter.IsClaimed).Id.ShouldBe("growroom");
        migrated.Revision.ShouldBe(historical.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedVersionOnlyProfile(historical, catalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();

        var secondRestart = Application(catalogue, documents, server, economy: Classic(3));
        Value(await secondRestart.State());
        (await documents.Read("profile")).ShouldBe(migrated);

        var ownershipBeforeOpen = Ownership(restored);
        var command = Guid.Parse("c1555555-5555-5555-5555-555555555555");
        var opened = Value(await secondRestart.OpenPack(new(command)));
        var afterOpen = (await documents.Read("profile"))!;
        var retried = Value(await secondRestart.OpenPack(new(command)));

        opened.LastPack!.Sequence.ShouldBe(2);
        opened.Profile!.RemainingPacks.ShouldBe(1);
        Ownership(opened).Values.Sum().ShouldBe(ownershipBeforeOpen.Values.Sum() + 11);
        retried.LastPack!.Id.ShouldBe(opened.LastPack.Id);
        retried.LastPack.Sequence.ShouldBe(opened.LastPack.Sequence);
        retried
            .LastPack.Cards.Select(static card => card.Id)
            .ShouldBe(opened.LastPack.Cards.Select(static card => card.Id));
        Ownership(retried).ShouldBe(Ownership(opened));
        afterOpen.Revision.ShouldBe(migrated.Revision + 1);
        (await documents.Read("profile")).ShouldBe(afterOpen);
        server.Requests.ShouldBeEmpty();
    }

    [Test]
    [Arguments("ownership", true)]
    [Arguments("receipt", true)]
    [Arguments("deck", true)]
    [Arguments("guaranteed-starter", true)]
    [Arguments("starter-claim", false)]
    public async Task BrowserIncompatibleHistoricalCard_PreservesExactDocumentAndPriorProjection(
        string location,
        bool projectable
    )
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(catalogue, documents, new ServerHandler(null));
        var historical = await CreateHistoricalProfile(
            application,
            documents,
            profile => AddUnknownHistoricalCard(profile, location)
        );

        var restarted = Application(catalogue, documents, new ServerHandler(null));
        var state = await restarted.State();

        if (projectable)
        {
            var restored = Value(state);
            restored.Cards.ShouldContain(card => card.Id == UnknownCardId(location));

            var pack = await restarted.OpenPack(
                new(Guid.Parse("c1666666-6666-6666-6666-666666666666"))
            );
            pack.Succeeded.ShouldBeFalse();
            pack.Error!.Code.ShouldBe("pack.authority_changed");
            pack.Error.Message.ShouldBe(
                "This saved player cannot use the current card set. No data changed."
            );
        }
        else
        {
            state.Succeeded.ShouldBeFalse();
            state.Error!.Code.ShouldBe("state.invalid");
        }

        (await documents.Read("profile")).ShouldBe(historical);
    }

    [Test]
    public async Task CurrentAuthorityUnknownCard_RemainsRejectedWithoutAWhitelist()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(catalogue, documents, new ServerHandler(null));
        var current = await PopulateBrowserProfile(application, documents);
        var damagedJson = JsonNode.Parse(current.Json)!.AsObject();
        damagedJson["profile"]!["collectibleOwnership"]!
            .AsArray()
            .Add(new JsonObject { ["cardId"] = "UNKNOWN-CURRENT-OWNERSHIP", ["quantity"] = 0 });
        await documents.Update("profile", current.Revision, damagedJson.ToJsonString());
        var damaged = (await documents.Read("profile"))!;

        var response = await Application(catalogue, documents, new ServerHandler(null)).State();

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe("state.invalid");
        (await documents.Read("profile")).ShouldBe(damaged);
    }

    [Test]
    public async Task TwoCompatibleAuthorityChanges_MigrateByVersionOnlyEachTime()
    {
        var originalCatalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(originalCatalogue, documents, new ServerHandler(null));
        var original = await PopulateBrowserProfile(application, documents);
        var firstCatalogue = WithManifestVersion(originalCatalogue, "compatible-manifest-one");

        Value(await Application(firstCatalogue, documents, new ServerHandler(null)).State());
        var first = (await documents.Read("profile"))!;
        var secondCatalogue = WithManifestVersion(firstCatalogue, "compatible-manifest-two");

        Value(await Application(secondCatalogue, documents, new ServerHandler(null)).State());
        var second = (await documents.Read("profile"))!;

        first.Revision.ShouldBe(original.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(first.Json),
                ExpectedVersionOnlyProfile(original, firstCatalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();
        second.Revision.ShouldBe(first.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(second.Json),
                ExpectedVersionOnlyProfile(first, secondCatalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();

        Value(await Application(secondCatalogue, documents, new ServerHandler(null)).State());
        (await documents.Read("profile")).ShouldBe(second);
    }

    [Test]
    public async Task BrowserAuthorityMigration_ConflictPreservesHistoricalDocument()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var historical = await CreateHistoricalProfile(
            Application(catalogue, documents, new ServerHandler(null)),
            documents
        );
        documents.ConflictNextUpdate = true;

        var response = await Application(catalogue, documents, new ServerHandler(null)).State();

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe("state.conflict");
        (await documents.Read("profile")).ShouldBe(historical);
    }

    [Test]
    public async Task BrowserAuthorityMigration_TypedStorageFailurePreservesHistoricalDocument()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var historical = await CreateHistoricalProfile(
            Application(catalogue, documents, new ServerHandler(null)),
            documents
        );
        documents.FailNextWrite = DocumentStorageFailure.Full;

        var response = await Application(catalogue, documents, new ServerHandler(null)).State();

        response.Succeeded.ShouldBeFalse();
        response.Error!.Code.ShouldBe("storage.full");
        response.Error.Message.ShouldContain("last saved game is unchanged", Case.Sensitive);
        (await documents.Read("profile")).ShouldBe(historical);
    }

    [Test]
    public async Task BrowserAuthorityMigration_CancellationBeforePersistencePreservesHistoricalDocument()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var historical = await CreateHistoricalProfile(
            Application(catalogue, documents, new ServerHandler(null)),
            documents
        );
        using var cancellation = new CancellationTokenSource();
        documents.AfterProfileRead = cancellation.Cancel;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Application(catalogue, documents, new ServerHandler(null)).State(cancellation.Token)
        );

        (await documents.Read("profile")).ShouldBe(historical);
    }

    [Test]
    public async Task BrowserAuthorityMigration_CommittedThenCancelledRecoversAndReplaysPersistedPack()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var historical = await CreateHistoricalProfile(
            Application(catalogue, documents, new ServerHandler(null)),
            documents
        );
        using var cancellation = new CancellationTokenSource();
        var cancellingDocuments = new CommitThenCancelDocumentStore(documents, cancellation);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            Application(catalogue, cancellingDocuments, new ServerHandler(null))
                .State(cancellation.Token)
        );

        exception.CancellationToken.ShouldBe(cancellation.Token);
        cancellation.IsCancellationRequested.ShouldBeTrue();
        var migrated = (await documents.Read("profile"))!;
        migrated.Revision.ShouldBe(historical.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedVersionOnlyProfile(historical, catalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();

        var restarted = Application(catalogue, cancellingDocuments, new ServerHandler(null));
        var restored = Value(await restarted.State());
        var persistedPackCommand = Guid.Parse("c1222222-2222-2222-2222-222222222222");

        cancellingDocuments.ProfileUpdateAttempts.ShouldBe(1);
        restored.LastPack!.Id.ShouldBe(persistedPackCommand);
        (await documents.Read("profile")).ShouldBe(migrated);

        var replayed = Value(await restarted.OpenPack(new(persistedPackCommand)));

        replayed.LastPack!.Id.ShouldBe(persistedPackCommand);
        replayed.LastPack.Sequence.ShouldBe(restored.LastPack.Sequence);
        replayed
            .LastPack.Cards.Select(static card => card.Id)
            .ShouldBe(restored.LastPack.Cards.Select(static card => card.Id));
        replayed.Profile!.RemainingPacks.ShouldBe(restored.Profile!.RemainingPacks);
        Ownership(replayed).ShouldBe(Ownership(restored));
        cancellingDocuments.ProfileUpdateAttempts.ShouldBe(1);
        (await documents.Read("profile")).ShouldBe(migrated);
    }

    [Test]
    public async Task BrowserAuthorityMigration_ConcurrentClientsUseCasAndReturnOneConflict()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var historical = await CreateHistoricalProfile(
            Application(catalogue, documents, new ServerHandler(null)),
            documents
        );
        var racingDocuments = new ConcurrentMigrationDocumentStore(documents, 2);
        var first = Application(catalogue, racingDocuments, new ServerHandler(null));
        var second = Application(catalogue, racingDocuments, new ServerHandler(null));

        var responses = await Task.WhenAll(first.State(), second.State());

        responses.Count(static response => response.Succeeded).ShouldBe(1);
        Value(responses.Single(static response => response.Succeeded)).Profile.ShouldNotBeNull();
        var conflict = responses.Single(static response => !response.Succeeded);
        conflict.Error!.Code.ShouldBe("state.conflict");
        racingDocuments.MigrationUpdateAttempts.ShouldBe(2);
        racingDocuments.MigrationExpectedRevisions.ShouldBe([
            historical.Revision,
            historical.Revision,
        ]);
        var migrated = (await documents.Read("profile"))!;
        migrated.Revision.ShouldBe(historical.Revision + 1);
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedVersionOnlyProfile(historical, catalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();

        Value(await Application(catalogue, racingDocuments, new ServerHandler(null)).State());
        racingDocuments.MigrationUpdateAttempts.ShouldBe(2);
        (await documents.Read("profile")).ShouldBe(migrated);
    }

    [Test]
    public async Task BrowserAuthorityMigration_RebindsARealVersionAndRejectsAnUnknownOne()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Application(catalogue, documents, new ServerHandler(null));
        await PopulateBrowserProfile(application, documents);
        var current = Value(await application.State());
        Value(
            await application.StartMatch(
                new(Guid.Parse("c1777777-7777-7777-7777-777777777777"), current.Decks[0].Id)
            )
        );
        var historical = await SetProfileAuthorityVersion(documents, "historical-profile-manifest");
        var match = (await documents.Read("match"))!;
        var mismatchedMatch = JsonNode.Parse(match.Json)!.AsObject();
        mismatchedMatch["authorityVersion"] = "sv151-candidate.16";
        await documents.Update("match", match.Revision, mismatchedMatch.ToJsonString());
        var preservedMatch = (await documents.Read("match"))!;

        var state = Value(await Application(catalogue, documents, new ServerHandler(null)).State());
        var migrated = (await documents.Read("profile"))!;
        var migratedMatch = (await documents.Read("match"))!;

        state.Match.ShouldNotBeNull();
        state.MatchError.ShouldBeNull();
        JsonNode
            .DeepEquals(
                JsonNode.Parse(migrated.Json),
                ExpectedVersionOnlyProfile(historical, catalogue.Mechanics.ManifestVersion)
            )
            .ShouldBeTrue();
        migratedMatch.Revision.ShouldBe(preservedMatch.Revision + 1);
        JsonNode.Parse(migratedMatch.Json)!["authorityVersion"]!
            .GetValue<string>()
            .ShouldBe(catalogue.Mechanics.ManifestVersion);

        var unknownJson = JsonNode.Parse(migratedMatch.Json)!.AsObject();
        unknownJson["authorityVersion"] = "arbitrary-authority";
        await documents.Update("match", migratedMatch.Revision, unknownJson.ToJsonString());
        var unknown = (await documents.Read("match"))!;

        var rejected = Value(
            await Application(catalogue, documents, new ServerHandler(null)).State()
        );

        rejected.Match.ShouldBeNull();
        rejected.MatchError!.Code.ShouldBe("match.document_version");
        (await documents.Read("match")).ShouldBe(unknown);
    }

    [Test]
    public async Task IncompatibleBrowserSettings_AreReportedWithoutBeingReplaced()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        await documents.Create("settings", """{"schemaVersion":1,"mode":99}""");
        var before = await documents.Read("settings");
        var application = Application(catalogue, documents, new ServerHandler(null));

        var mode = await application.Mode();
        var selection = await application.SelectMode(PlayMode.BrowserLocal);

        mode.Selected.ShouldBeNull();
        mode.BrowserStorageError.ShouldNotBeNull()
            .ShouldContain("damaged or incompatible", Case.Sensitive);
        selection.Succeeded.ShouldBeFalse();
        (await documents.Read("settings")).ShouldBe(before);
    }

    [Test]
    public async Task StandaloneBuild_RejectsAnUnavailableServerModeWithoutCallingItsApi()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        await documents.Create("settings", """{"schemaVersion":1,"mode":0}""");
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server, serverBackedAvailable: false);

        var mode = await application.Mode();
        var serverSelection = await application.SelectMode(PlayMode.ServerBacked);
        var browserSelection = await application.SelectMode(PlayMode.BrowserLocal);
        var browserState = await application.State();

        mode.Selected.ShouldBeNull();
        mode.ServerBackedAvailable.ShouldBeFalse();
        serverSelection.Succeeded.ShouldBeFalse();
        serverSelection.Error!.Code.ShouldBe("mode.unavailable");
        browserSelection.Succeeded.ShouldBeTrue();
        browserState.Succeeded.ShouldBeTrue();
        server.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task BrowserJourney_RestartsFromProfileThroughSavedDeckAndReplayedMatch()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server);
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("95111111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("95222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var opened = Value(
            await application.OpenPack(new(Guid.Parse("95333333-3333-3333-3333-333333333333")))
        );
        var starter = claimed.Decks.Single();
        var saved = Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("95444444-4444-4444-4444-444444444444"),
                    starter.Id,
                    starter.Revision,
                    "Browser starter",
                    starter.Entries
                )
            )
        );
        var deck = saved.Decks.Single();
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("95555555-5555-5555-5555-555555555555"), deck.Id)
            )
        );
        var action = started.Application.Match!.LegalActions.First();
        var applied = Value(
            await application.ApplyMatchAction(
                started.Application.Match.Frame.Id,
                RequestFor(started.Application.Match, action)
            )
        );

        var restarted = Application(catalogue, documents, server);
        var mode = await restarted.Mode();
        var restored = Value(await restarted.State());

        mode.Selected.ShouldBe(PlayMode.BrowserLocal);
        mode.StorageLocation.ShouldBe("Saved in this browser");
        restored.Profile!.DisplayName.ShouldBe("Browser Player");
        restored.Profile.StarterDeckId.ShouldBe("growroom");
        restored.LastPack!.Id.ShouldBe(opened.LastPack!.Id);
        restored.Decks.Single().Name.ShouldBe("Browser starter");
        JsonSerializer
            .Serialize(restored.Match)
            .ShouldBe(JsonSerializer.Serialize(applied.Application.Match));
        restored.MatchError.ShouldBeNull();
        server.Requests.ShouldBeEmpty();
        (await documents.Read("settings")).ShouldNotBeNull();
        (await documents.Read("profile")).ShouldNotBeNull();
        (await documents.Read("match")).ShouldNotBeNull();
    }

    [Test]
    public async Task BrowserJourney_ArchivesACompletedBattleBeforeStartingTheNextOne()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server);
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("97111111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("97222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var first = Value(
            await application.StartMatch(
                new(Guid.Parse("97333333-3333-3333-3333-333333333333"), claimed.Decks.Single().Id)
            )
        ).Application;
        var completed = await CompleteMatch(application, first);
        var firstMatchId = completed.Match!.Frame.Id;

        var second = Value(
            await application.StartMatch(
                new(Guid.Parse("97444444-4444-4444-4444-444444444444"), claimed.Decks.Single().Id)
            )
        ).Application;
        var history = await documents.Read("match-history");
        var restarted = Application(catalogue, documents, server);
        var restored = Value(await restarted.State());

        completed.Match.Frame.IsComplete.ShouldBeTrue();
        second.Match!.Frame.Id.ShouldNotBe(firstMatchId);
        history.ShouldNotBeNull();
        history!.Json.ShouldContain(firstMatchId.ToString("D"), Case.Sensitive);
        restored.Match!.Frame.Id.ShouldBe(second.Match.Frame.Id);
        server.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task BrowserJourney_ResignsTheSavedBattleAndReloadsItWithTheRecordedWinner()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server);
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("96111111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("96222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("96333333-3333-3333-3333-333333333333"), claimed.Decks.Single().Id)
            )
        ).Application;
        var match = started.Match!;
        var resign = match.LegalActions.Single(static action =>
            action.Kind == MatchActionKindView.Resign
        );

        var resigned = Value(
            await application.ApplyMatchAction(match.Frame.Id, RequestFor(match, resign))
        ).Application;
        var stored = await documents.Read("match");
        var restored = Value(await Application(catalogue, documents, server).State());

        resigned.Match!.Frame.IsComplete.ShouldBeTrue();
        resigned.Match.Frame.Winner.ShouldBe("The Regular");
        stored!.Json.ShouldContain("\"$command\":\"resign\"", Case.Sensitive);
        restored.Match!.Frame.IsComplete.ShouldBeTrue();
        restored.Match.Frame.Winner.ShouldBe("The Regular");
        restored.MatchError.ShouldBeNull();
        JsonSerializer.Serialize(restored.Match).ShouldBe(JsonSerializer.Serialize(resigned.Match));
        server.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task BrowserJourney_RejectsAMismatchedDuplicateHistoryEntryWithoutReplacingTheMatch()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var server = new ServerHandler(null);
        var application = Application(catalogue, documents, server);
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("98111111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("98222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("98333333-3333-3333-3333-333333333333"), claimed.Decks.Single().Id)
            )
        ).Application;
        await CompleteMatch(application, started);
        var activeBefore = (await documents.Read("match"))!;
        var archived = JsonNode.Parse(activeBefore.Json)!.AsObject();
        archived["commands"] = new JsonArray();
        archived["clientCommands"] = new JsonArray();
        var history = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["authorityVersion"] = catalogue.Mechanics.ManifestVersion,
            ["matches"] = new JsonArray(archived),
        };
        await documents.Create("match-history", history.ToJsonString());

        var replacement = await application.StartMatch(
            new(Guid.Parse("98444444-4444-4444-4444-444444444444"), claimed.Decks.Single().Id)
        );
        var activeAfter = await documents.Read("match");

        replacement.Succeeded.ShouldBeFalse();
        replacement.Error!.Code.ShouldBe("match.history_corrupt");
        activeAfter.ShouldBe(activeBefore);
        server.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task DeckBuilder_CreatesAnExtraDeckUnderItsCommandIdAndRevisesEachDeckSeparately()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, EconomyRules.Unlimited);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("b1111111-1111-1111-1111-111111111111"), "Deck Builder")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("b1222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var starter = claimed.Decks.Single();
        var createCommandId = Guid.Parse("b1333333-3333-3333-3333-333333333333");
        var create = new SaveDeckRequest(
            createCommandId,
            null,
            null,
            "Second deck",
            starter.Entries
        );

        var created = Value(await application.SaveDeck(create));
        var retried = Value(await application.SaveDeck(create));
        var createdDeck = created.Decks.Single(deck => deck.Id != starter.Id);
        var revised = Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("b1444444-4444-4444-4444-444444444444"),
                    createdDeck.Id,
                    createdDeck.Revision,
                    "Renamed second deck",
                    createdDeck.Entries
                )
            )
        );
        var restored = Value(await Local(catalogue, documents, EconomyRules.Unlimited).State());

        created.Decks.Length.ShouldBe(2);
        createdDeck.Id.ShouldBe(createCommandId);
        createdDeck.Revision.ShouldBe(starter.Revision);
        createdDeck.IsLegal.ShouldBeTrue();
        retried
            .Decks.Select(static deck => deck.Id)
            .ShouldBe(created.Decks.Select(static deck => deck.Id));
        revised
            .Decks.Single(deck => deck.Id == createdDeck.Id)
            .Revision.ShouldBe(createdDeck.Revision + 1);
        revised
            .Decks.Single(deck => deck.Id == createdDeck.Id)
            .Name.ShouldBe("Renamed second deck");
        revised.Decks.Single(deck => deck.Id == starter.Id).Revision.ShouldBe(starter.Revision);
        restored
            .Decks.Select(static deck => deck.Name)
            .ShouldBe([starter.Name, "Renamed second deck"], ignoreOrder: true);
    }

    [Test]
    public async Task NewDeck_FailsTheSameLegalityAndOwnershipChecksAsARevisionWithoutSavingIt()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, EconomyRules.Unlimited);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("b2111111-1111-1111-1111-111111111111"), "Deck Builder")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("b2222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var starter = claimed.Decks.Single();
        var kinds = claimed.Cards.ToDictionary(
            static card => card.Id,
            static card => card.Kind,
            StringComparer.Ordinal
        );
        var energy = starter.Entries.First(entry => kinds[entry.CardId] == CardKindView.Energy);
        var unowned = claimed.Cards.First(card =>
            card.Kind == CardKindView.Blokemon
            && card.OwnedQuantity == 0
            && starter.Entries.All(entry => entry.CardId != card.Id)
        );
        var borrowed = starter
            .Entries.Select(entry =>
                entry.CardId == energy.CardId ? entry with { Quantity = entry.Quantity - 2 } : entry
            )
            .Append(new DeckEntryView(unowned.Id, 2))
            .ToArray();
        var incomplete = starter.Entries.Take(3).ToArray();

        var createdIncomplete = await application.SaveDeck(
            new(
                Guid.Parse("b2333333-3333-3333-3333-333333333333"),
                null,
                null,
                "Short deck",
                incomplete
            )
        );
        var revisedIncomplete = await application.SaveDeck(
            new(
                Guid.Parse("b2444444-4444-4444-4444-444444444444"),
                starter.Id,
                starter.Revision,
                starter.Name,
                incomplete
            )
        );
        var createdBorrowed = await application.SaveDeck(
            new(
                Guid.Parse("b2555555-5555-5555-5555-555555555555"),
                null,
                null,
                "Borrowed deck",
                borrowed
            )
        );
        // A new deck starts as an empty draft, so saving before it is filled fails the same way.
        var createdEmpty = await application.SaveDeck(
            new(Guid.Parse("b2666666-6666-6666-6666-666666666666"), null, null, "New deck", [])
        );
        var state = Value(await application.State());

        createdIncomplete.Succeeded.ShouldBeFalse();
        createdIncomplete.Error!.Code.ShouldBe("deck.invalid");
        revisedIncomplete.Succeeded.ShouldBeFalse();
        revisedIncomplete.Error!.Code.ShouldBe(createdIncomplete.Error.Code);
        revisedIncomplete.Error.Message.ShouldBe(createdIncomplete.Error.Message);
        createdBorrowed.Succeeded.ShouldBeFalse();
        createdBorrowed.Error!.Code.ShouldBe("deck.invalid");
        createdBorrowed.Error.Message.ShouldBe(
            $"{unowned.Id} requests 2 copies, but only 0 are owned."
        );
        createdEmpty.Succeeded.ShouldBeFalse();
        createdEmpty.Error!.Code.ShouldBe("deck.invalid");
        createdEmpty.Error.Message.ShouldBe(
            "The deck has 0 cards. It must have 60 cards. The deck needs at least one Regular Blokemon."
        );
        state.Decks.Single().Id.ShouldBe(starter.Id);
        state.Decks.Single().Revision.ShouldBe(starter.Revision);
    }

    [Test]
    public async Task DeckBuilder_DeletesDecksWithoutChangingOwnedCardsAndKeepsThemDeleted()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, EconomyRules.Unlimited);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("b3111111-1111-1111-1111-111111111111"), "Deck Builder")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("b3222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var starter = claimed.Decks.Single();
        var withSecond = Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("b3333333-3333-3333-3333-333333333333"),
                    null,
                    null,
                    "Second deck",
                    starter.Entries
                )
            )
        );
        var ownershipBefore = Ownership(withSecond);
        // The match froze its own copy of the deck, so deleting that deck must not disturb it.
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("b3777777-7777-7777-7777-777777777777"), starter.Id)
            )
        ).Application;

        var deletedStarter = Value(
            await application.DeleteDeck(
                new(Guid.Parse("b3444444-4444-4444-4444-444444444444"), starter.Id)
            )
        );
        var retried = await application.DeleteDeck(
            new(Guid.Parse("b3555555-5555-5555-5555-555555555555"), starter.Id)
        );
        var restored = Value(await Local(catalogue, documents, EconomyRules.Unlimited).State());
        var deletedLast = Value(
            await application.DeleteDeck(
                new(Guid.Parse("b3666666-6666-6666-6666-666666666666"), restored.Decks.Single().Id)
            )
        );

        deletedStarter.Decks.Single().Name.ShouldBe("Second deck");
        Ownership(deletedStarter).ShouldBe(ownershipBefore);
        deletedStarter.Match!.Frame.Id.ShouldBe(started.Match!.Frame.Id);
        restored.Match!.Frame.Id.ShouldBe(started.Match.Frame.Id);
        deletedStarter.Profile!.StarterDeckId.ShouldBe("growroom");
        deletedStarter.StarterDecks.Count(static starter => starter.IsClaimed).ShouldBe(1);
        retried.Succeeded.ShouldBeFalse();
        retried.Error!.Code.ShouldBe("deck.not_found");
        restored.Decks.Single().Name.ShouldBe("Second deck");
        deletedLast.Decks.ShouldBeEmpty();
        Ownership(deletedLast).ShouldBe(ownershipBefore);
        Value(await Local(catalogue, documents, EconomyRules.Unlimited).State())
            .Decks.ShouldBeEmpty();
    }

    [Test]
    public async Task ProfileDocumentFromTheSupersededSchema_IsReportedWithoutBeingReplaced()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, EconomyRules.Unlimited);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("b4111111-1111-1111-1111-111111111111"), "Legacy Player")
            )
        );
        Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("b4222222-2222-2222-2222-222222222222"), "growroom")
            )
        );
        var stored = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(stored.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        // Schema 2 recorded the claimed starter's deck inside the claim itself.
        document["schemaVersion"] = 2;
        var claim = profile["starterDeckClaims"]![0]!.AsObject();
        claim["deck"] = profile["savedDecks"]![0]!.DeepClone();
        await documents.Update("profile", stored.Revision, document.ToJsonString());
        var legacy = (await documents.Read("profile"))!;

        var restarted = Local(catalogue, documents, EconomyRules.Unlimited);
        var state = await restarted.State();
        var opened = await restarted.OpenPack(
            new(Guid.Parse("b4333333-3333-3333-3333-333333333333"))
        );

        state.Succeeded.ShouldBeFalse();
        state.Error!.Code.ShouldBe("state.invalid");
        opened.Succeeded.ShouldBeFalse();
        opened.Error!.Code.ShouldBe("state.invalid");
        (await documents.Read("profile")).ShouldBe(legacy);
    }

    [Test]
    public async Task ServerApi_StillPersistsAProfileInItsOwnSqliteDatabase()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"server-regression-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
            });
            using var client = factory.CreateClient();
            var commandId = Guid.Parse("96666666-6666-6666-6666-666666666666");

            var createdResponse = await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(commandId, "Server Player")
            );
            var created = await createdResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();
            var restored = await client.GetFromJsonAsync<ApiResponse<ApplicationView>>(
                "/api/state"
            );

            createdResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            created!.Succeeded.ShouldBeTrue();
            restored!.Succeeded.ShouldBeTrue();
            restored.Value!.Profile!.Id.ShouldBe(created.Value!.Profile!.Id);
            File.Exists(Path.Combine(dataDirectory, "blokemon.db")).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task ServerApi_DeletesASavedDeckThroughItsOwnEndpoint()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"server-deck-delete-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
            });
            using var client = factory.CreateClient();
            await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(
                    Guid.Parse("b5111111-1111-1111-1111-111111111111"),
                    "Server Player"
                )
            );
            var claimedResponse = await client.PostAsJsonAsync(
                "/api/starter-decks/claim",
                new ClaimStarterDeckRequest(
                    Guid.Parse("b5222222-2222-2222-2222-222222222222"),
                    "growroom"
                )
            );
            var claimed = await claimedResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();
            var starter = claimed!.Value!.Decks.Single();

            var deletedResponse = await client.PostAsJsonAsync(
                "/api/decks/delete",
                new DeleteDeckRequest(
                    Guid.Parse("b5333333-3333-3333-3333-333333333333"),
                    starter.Id
                )
            );
            var deleted = await deletedResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();
            var restored = await client.GetFromJsonAsync<ApiResponse<ApplicationView>>(
                "/api/state"
            );

            deletedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            deleted!.Succeeded.ShouldBeTrue();
            deleted.Value!.Decks.ShouldBeEmpty();
            Ownership(deleted.Value).ShouldBe(Ownership(claimed.Value));
            restored!.Value!.Decks.ShouldBeEmpty();
            restored.Value.Profile!.StarterDeckId.ShouldBe("growroom");
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task UnlimitedEconomy_KeepsUncappedPacksAndStartersWithoutAllowanceSignals()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, EconomyRules.Unlimited);
        var created = Value(
            await application.CreateProfile(
                new(Guid.Parse("a1111111-1111-1111-1111-111111111111"), "Unlimited Player")
            )
        );

        var firstPack = Value(
            await application.OpenPack(new(Guid.Parse("a1222222-2222-2222-2222-222222222222")))
        );
        var secondPack = Value(
            await application.OpenPack(new(Guid.Parse("a1333333-3333-3333-3333-333333333333")))
        );
        Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("a1444444-4444-4444-4444-444444444444"), "growroom")
            )
        );
        var secondClaim = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("a1555555-5555-5555-5555-555555555555"), "early-shift")
            )
        );

        created.Profile!.RemainingPacks.ShouldBeNull();
        created.Profile.StarterClaimUsed.ShouldBeNull();
        firstPack.Profile!.RemainingPacks.ShouldBeNull();
        secondPack.LastPack!.Sequence.ShouldBe(2);
        secondClaim.Profile!.StarterClaimUsed.ShouldBeNull();
        secondClaim.StarterDecks.Count(static starter => starter.IsClaimed).ShouldBe(2);
    }

    [Test]
    public async Task ClassicEconomy_CapsPacksAndStarterClaimsWithVisibleRemainingAllowances()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var application = Local(catalogue, documents, Classic(2));
        var created = Value(
            await application.CreateProfile(
                new(Guid.Parse("a2111111-1111-1111-1111-111111111111"), "Classic Player")
            )
        );

        var firstPack = Value(
            await application.OpenPack(new(Guid.Parse("a2222222-2222-2222-2222-222222222222")))
        );
        var secondPack = Value(
            await application.OpenPack(new(Guid.Parse("a2333333-3333-3333-3333-333333333333")))
        );
        var exhausted = await application.OpenPack(
            new(Guid.Parse("a2444444-4444-4444-4444-444444444444"))
        );
        var retriedSecondPack = Value(
            await application.OpenPack(new(Guid.Parse("a2333333-3333-3333-3333-333333333333")))
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("a2555555-5555-5555-5555-555555555555"), "growroom")
            )
        );
        var secondClaim = await application.ClaimStarterDeck(
            new(Guid.Parse("a2666666-6666-6666-6666-666666666666"), "early-shift")
        );
        var retriedClaim = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("a2555555-5555-5555-5555-555555555555"), "growroom")
            )
        );

        created.Profile!.RemainingPacks.ShouldBe(2);
        created.Profile.StarterClaimUsed.ShouldBe(false);
        firstPack.Profile!.RemainingPacks.ShouldBe(1);
        secondPack.Profile!.RemainingPacks.ShouldBe(0);
        exhausted.Succeeded.ShouldBeFalse();
        exhausted.Error!.Code.ShouldBe("pack.allowance");
        retriedSecondPack.LastPack!.Id.ShouldBe(secondPack.LastPack!.Id);
        retriedSecondPack.Profile!.RemainingPacks.ShouldBe(0);
        claimed.Profile!.StarterClaimUsed.ShouldBe(true);
        secondClaim.Succeeded.ShouldBeFalse();
        secondClaim.Error!.Code.ShouldBe("starter.already_claimed");
        retriedClaim.StarterDecks.Count(static starter => starter.IsClaimed).ShouldBe(1);
        retriedClaim.Profile!.StarterDeckId.ShouldBe("growroom");
    }

    [Test]
    public async Task ClassicProfile_KeepsItsInheritedModeWhenTheConfiguredModeChanges()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var classic = Local(catalogue, documents, Classic(1));
        Value(
            await classic.CreateProfile(
                new(Guid.Parse("a3111111-1111-1111-1111-111111111111"), "Classic Player")
            )
        );
        Value(await classic.OpenPack(new(Guid.Parse("a3222222-2222-2222-2222-222222222222"))));

        var unlimited = Local(catalogue, documents, EconomyRules.Unlimited);
        var restored = Value(await unlimited.State());
        var blocked = await unlimited.OpenPack(
            new(Guid.Parse("a3333333-3333-3333-3333-333333333333"))
        );

        restored.Profile!.RemainingPacks.ShouldBe(0);
        restored.Profile.StarterClaimUsed.ShouldBe(false);
        restored.LastPack!.Sequence.ShouldBe(1);
        blocked.Succeeded.ShouldBeFalse();
        blocked.Error!.Code.ShouldBe("pack.allowance");
    }

    [Test]
    public async Task ProfileDocumentWithoutEconomyFields_RestoresAsUnlimitedOnTheCurrentSchema()
    {
        var catalogue = Catalogue();
        var documents = new MemoryDocumentStore();
        var classic = Local(catalogue, documents, Classic(1));
        Value(
            await classic.CreateProfile(
                new(Guid.Parse("a4111111-1111-1111-1111-111111111111"), "Legacy Player")
            )
        );
        Value(await classic.OpenPack(new(Guid.Parse("a4222222-2222-2222-2222-222222222222"))));
        var stored = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(stored.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        var persistedMode = profile["economy"]!.GetValue<int>();
        var persistedAllowance = profile["economyPackAllowance"]!.GetValue<int>();
        profile.Remove("economy");
        profile.Remove("economyPackAllowance");
        await documents.Update("profile", stored.Revision, document.ToJsonString());

        var unlimited = Local(catalogue, documents, EconomyRules.Unlimited);
        var restored = Value(await unlimited.State());
        var opened = Value(
            await unlimited.OpenPack(new(Guid.Parse("a4333333-3333-3333-3333-333333333333")))
        );

        document["schemaVersion"]!.GetValue<int>().ShouldBe(3);
        persistedMode.ShouldBe((int)EconomyMode.ClassicScarcity);
        persistedAllowance.ShouldBe(1);
        restored.Profile!.RemainingPacks.ShouldBeNull();
        restored.Profile.StarterClaimUsed.ShouldBeNull();
        opened.LastPack!.Sequence.ShouldBe(2);
        opened.Profile!.RemainingPacks.ShouldBeNull();
    }

    [Test]
    public void EconomyConfiguration_DefaultsToUnlimitedAndReadsTheClassicSettings()
    {
        var unlimited = EconomyConfiguration.Resolve(Configuration([]));
        var explicitUnlimited = EconomyConfiguration.Resolve(
            Configuration([new(EconomyConfiguration.ModeKey, "Unlimited")])
        );
        var classicDefault = EconomyConfiguration.Resolve(
            Configuration([new(EconomyConfiguration.ModeKey, "ClassicScarcity")])
        );
        var classicConfigured = EconomyConfiguration.Resolve(
            Configuration([
                new(EconomyConfiguration.ModeKey, "ClassicScarcity"),
                new(EconomyConfiguration.PackAllowanceKey, "3"),
            ])
        );

        unlimited.ShouldBe(EconomyRules.Unlimited);
        unlimited.PackAllowance.ShouldBeNull();
        explicitUnlimited.ShouldBe(EconomyRules.Unlimited);
        classicDefault.Mode.ShouldBe(EconomyMode.ClassicScarcity);
        classicDefault.PackAllowance.ShouldBe(EconomyRules.DefaultClassicPackAllowance);
        classicDefault.StarterDeckClaimAllowance.ShouldBe(1);
        classicConfigured.PackAllowance.ShouldBe(3);
        Should.Throw<InvalidOperationException>(() =>
            EconomyConfiguration.Resolve(Configuration([new(EconomyConfiguration.ModeKey, "Free")]))
        );
        Should.Throw<InvalidOperationException>(() =>
            EconomyConfiguration.Resolve(
                Configuration([
                    new(EconomyConfiguration.ModeKey, "ClassicScarcity"),
                    new(EconomyConfiguration.PackAllowanceKey, "-1"),
                ])
            )
        );
    }

    [Test]
    public void BrowserComposition_TakesTheEconomyResolvedFromTheBrowserConfiguration()
    {
        var services = new ServiceCollection()
            .AddBlokemonClient(
                new HttpClient { BaseAddress = new Uri("https://browser.invalid/") },
                Catalogue(),
                new PlayModeAvailability(serverBacked: false),
                EconomyConfiguration.Resolve(
                    Configuration([
                        new(EconomyConfiguration.ModeKey, "ClassicScarcity"),
                        new(EconomyConfiguration.PackAllowanceKey, "4"),
                    ])
                )
            )
            .BuildServiceProvider();

        var economy = services.GetRequiredService<EconomyRules>();

        economy.Mode.ShouldBe(EconomyMode.ClassicScarcity);
        economy.PackAllowance.ShouldBe(4);
        economy.StarterDeckClaimAllowance.ShouldBe(1);
    }

    [Test]
    public async Task BrowserBuild_ShipsUnlimitedSettingsAndAClassicEnvironmentOverlay()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"browser-settings-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
            });
            using var client = factory.CreateClient();

            var shipped = await client.GetStringAsync("appsettings.json");
            var classicOverlay = await client.GetStringAsync("appsettings.Classic.json");
            var shippedEconomy = EconomyConfiguration.Resolve(BrowserConfiguration(shipped));
            var classicEconomy = EconomyConfiguration.Resolve(
                BrowserConfiguration(shipped, classicOverlay)
            );

            shippedEconomy.ShouldBe(EconomyRules.Unlimited);
            classicEconomy.Mode.ShouldBe(EconomyMode.ClassicScarcity);
            classicEconomy.PackAllowance.ShouldBe(10);
            classicEconomy.StarterDeckClaimAllowance.ShouldBe(1);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task ServerApi_AppliesTheClassicEconomyConfiguredInItsSettings()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"classic-economy-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
                builder.UseSetting(EconomyConfiguration.ModeKey, "ClassicScarcity");
                builder.UseSetting(EconomyConfiguration.PackAllowanceKey, "1");
            });
            using var client = factory.CreateClient();

            var createdResponse = await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(
                    Guid.Parse("a5111111-1111-1111-1111-111111111111"),
                    "Classic Server Player"
                )
            );
            var created = await createdResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();
            var openedResponse = await client.PostAsJsonAsync(
                "/api/packs/open",
                new OpenPackRequest(Guid.Parse("a5222222-2222-2222-2222-222222222222"))
            );
            var opened = await openedResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();
            var exhaustedResponse = await client.PostAsJsonAsync(
                "/api/packs/open",
                new OpenPackRequest(Guid.Parse("a5333333-3333-3333-3333-333333333333"))
            );
            var exhausted = await exhaustedResponse.Content.ReadFromJsonAsync<
                ApiResponse<ApplicationView>
            >();

            created!.Value!.Profile!.RemainingPacks.ShouldBe(1);
            created.Value.Profile.StarterClaimUsed.ShouldBe(false);
            opened!.Value!.Profile!.RemainingPacks.ShouldBe(0);
            exhausted!.Succeeded.ShouldBeFalse();
            exhausted.Error!.Code.ShouldBe("pack.allowance");
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task ServerRecoveryEndpoint_UsesTheSameTypedIdentityGateAndKeyIsolation()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"match-recovery-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Blokemon:DataDirectory", dataDirectory);
            });
            using var client = factory.CreateClient();
            var created = await client.PostAsJsonAsync(
                "/api/profile",
                new CreateProfileRequest(Guid.NewGuid(), "Server Player")
            );
            created.EnsureSuccessStatusCode();
            var claimedResponse = await client.PostAsJsonAsync(
                "/api/starter-decks/claim",
                new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom")
            );
            var claimed = (
                await claimedResponse.Content.ReadFromJsonAsync<ApiResponse<ApplicationView>>()
            )!.Value!;
            var startResponse = await client.PostAsJsonAsync(
                "/api/matches",
                new StartMatchRequest(Guid.NewGuid(), claimed.Decks.Single().Id)
            );
            startResponse.EnsureSuccessStatusCode();

            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<StateDocumentStore>();
            var current = (await store.Read("match"))!;
            var incompatible = JsonNode.Parse(current.Json)!.AsObject();
            incompatible["authorityVersion"] = "arbitrary-authority";
            await store.Update("match", current.Revision, incompatible.ToJsonString());
            await store.Create("match-history", "history-sentinel");
            await store.Create("match-migration-backup/sentinel", "backup-sentinel");
            var profile = (await store.Read("profile"))!;
            var history = (await store.Read("match-history"))!;
            var backup = (await store.Read("match-migration-backup/sentinel"))!;

            var gated = (
                await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state")
            )!.Value!;
            var recovery = gated.MatchRecovery!;
            var abandonedResponse = await client.PostAsJsonAsync(
                "/api/matches/abandon",
                new AbandonSavedMatchRequest(recovery.Revision, recovery.ContentIdentity)
            );
            var abandoned = (
                await abandonedResponse.Content.ReadFromJsonAsync<ApiResponse<ApplicationView>>()
            )!;

            recovery.Kind.ShouldBe(MatchRecoveryKindView.ActiveMatchUnsupportedVersion);
            abandoned.Succeeded.ShouldBeTrue();
            abandoned.Value!.MatchRecovery.ShouldBeNull();
            (await store.Read("match")).ShouldBeNull();
            (await store.Read("profile")).ShouldBe(profile);
            (await store.Read("match-history")).ShouldBe(history);
            (await store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    private static IConfiguration Configuration(KeyValuePair<string, string?>[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    // The browser host layers appsettings.json and appsettings.<environment>.json the same way.
    private static IConfiguration BrowserConfiguration(params string[] documents)
    {
        var builder = new ConfigurationBuilder();
        foreach (var document in documents)
        {
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(document)));
        }

        return builder.Build();
    }

    private static Dictionary<string, int> Ownership(ApplicationView view) =>
        view
            .Cards.Where(static card => card.OwnedQuantity > 0)
            .ToDictionary(
                static card => card.Id,
                static card => card.OwnedQuantity,
                StringComparer.Ordinal
            );

    private static async Task<StoredDocument> PopulateBrowserProfile(
        PlayModeApplication application,
        MemoryDocumentStore documents
    )
    {
        Value(await application.SelectMode(PlayMode.BrowserLocal));
        Value(
            await application.CreateProfile(
                new(Guid.Parse("c1111111-1111-1111-1111-111111111111"), "Browser Player")
            )
        );
        Value(await application.OpenPack(new(Guid.Parse("c1222222-2222-2222-2222-222222222222"))));
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("c1333333-3333-3333-3333-333333333333"), "growroom")
            )
        );
        var starter = claimed.Decks.Single();
        Value(
            await application.SaveDeck(
                new(
                    Guid.Parse("c1444444-4444-4444-4444-444444444444"),
                    null,
                    null,
                    "Browser copy",
                    starter.Entries
                )
            )
        );

        return (await documents.Read("profile"))!;
    }

    private static async Task<StoredDocument> CreateHistoricalProfile(
        PlayModeApplication application,
        MemoryDocumentStore documents,
        Action<JsonObject>? mutate = null
    )
    {
        var current = await PopulateBrowserProfile(application, documents);
        var document = JsonNode.Parse(current.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        profile["authorityManifestVersion"] = "historical-manifest";
        mutate?.Invoke(profile);
        await documents.Update("profile", current.Revision, document.ToJsonString());
        return (await documents.Read("profile"))!;
    }

    private static async Task<StoredDocument> SetProfileAuthorityVersion(
        MemoryDocumentStore documents,
        string manifestVersion
    )
    {
        var current = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(current.Json)!.AsObject();
        document["profile"]!["authorityManifestVersion"] = manifestVersion;
        await documents.Update("profile", current.Revision, document.ToJsonString());
        return (await documents.Read("profile"))!;
    }

    private static string UnknownCardId(string location) =>
        $"UNKNOWN-HISTORICAL-{location.ToUpperInvariant()}";

    private static void AddUnknownHistoricalCard(JsonObject profile, string location)
    {
        var cardId = UnknownCardId(location);

        switch (location)
        {
            case "ownership":
                profile["collectibleOwnership"]!
                    .AsArray()
                    .Add(new JsonObject { ["cardId"] = cardId, ["quantity"] = 0 });
                break;
            case "receipt":
            {
                var sampled = profile["packReceipts"]![0]!["sampledCollectibleIds"]!.AsArray();
                var replaced = sampled[0]!.GetValue<string>();
                sampled[0] = cardId;
                MoveOwnership(profile, replaced, cardId, 1);
                break;
            }
            case "deck":
                profile["savedDecks"]![0]!["cards"]![0]!["cardId"] = cardId;
                break;
            case "guaranteed-starter":
            {
                var replaced = profile["guaranteedRegularCollectibleId"]!.GetValue<string>();
                profile["guaranteedRegularCollectibleId"] = cardId;
                MoveOwnership(profile, replaced, cardId, 1);
                break;
            }
            case "starter-claim":
            {
                var grant = profile["starterDeckClaims"]![0]!["collectibleGrants"]![0]!.AsObject();
                var replaced = grant["cardId"]!.GetValue<string>();
                var quantity = grant["quantity"]!.GetValue<int>();
                grant["cardId"] = cardId;
                MoveOwnership(profile, replaced, cardId, quantity);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(location));
        }
    }

    private static void MoveOwnership(
        JsonObject profile,
        string replacedCardId,
        string replacementCardId,
        int quantity
    )
    {
        var ownership = profile["collectibleOwnership"]!.AsArray();
        var replaced = ownership
            .Select(static item => item!.AsObject())
            .Single(item => item["cardId"]!.GetValue<string>() == replacedCardId);
        var remaining = replaced["quantity"]!.GetValue<int>() - quantity;

        if (remaining == 0)
        {
            ownership.Remove(replaced);
        }
        else
        {
            replaced["quantity"] = remaining;
        }

        ownership.Add(new JsonObject { ["cardId"] = replacementCardId, ["quantity"] = quantity });
    }

    private static JsonObject ExpectedVersionOnlyProfile(
        StoredDocument historical,
        string manifestVersion
    )
    {
        var expected = JsonNode.Parse(historical.Json)!.AsObject();
        expected["profile"]!["authorityManifestVersion"] = manifestVersion;
        return expected;
    }

    private static BlokemonCatalogue WithManifestVersion(
        BlokemonCatalogue catalogue,
        string manifestVersion
    )
    {
        var bootstrap = JsonNode.Parse(catalogue.ToBootstrapJson())!.AsObject();
        var mechanics = JsonNode.Parse(bootstrap["mechanicsJson"]!.GetValue<string>())!.AsObject();
        mechanics["manifestVersion"] = manifestVersion;
        bootstrap["mechanicsJson"] = mechanics.ToJsonString();

        var starterDecks = JsonNode
            .Parse(bootstrap["starterDecksJson"]!.GetValue<string>())!
            .AsObject();
        starterDecks["mechanicalManifestVersion"] = manifestVersion;
        bootstrap["starterDecksJson"] = starterDecks.ToJsonString();
        return BlokemonCatalogue.FromBootstrapJson(bootstrap.ToJsonString());
    }

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    private static PlayModeApplication Application(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        ServerHandler handler,
        bool serverBackedAvailable = true,
        EconomyRules? economy = null
    )
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.invalid/") };
        return new(
            new BlokemonApiClient(http),
            Local(catalogue, documents, economy ?? EconomyRules.Unlimited),
            documents,
            new PlayModeAvailability(serverBackedAvailable)
        );
    }

    private static LocalApplicationService Local(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        EconomyRules economy
    ) =>
        new(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents),
            economy,
            ProfileAuthorityPolicy.MigrateCompatible
        );

    private static EconomyRules Classic(int packAllowance) =>
        EconomyRules
            .Classic(packAllowance)
            .Match(
                static rules => rules,
                static failure =>
                    throw new InvalidOperationException($"Expected classic rules, got {failure}.")
            );

    private static ApplyMatchActionRequest RequestFor(MatchView match, MatchActionView action) =>
        new(
            Guid.NewGuid(),
            match.Frame.Revision,
            action.Id,
            action
                .ChoiceRequirements.Where(static requirement => requirement.Chooser.IsLocalPlayer)
                .Where(requirement =>
                    requirement.DependsOnOptional is null
                    || action
                        .ChoiceRequirements.Single(parent =>
                            parent.Id == requirement.DependsOnOptional
                        )
                        .Kind != MatchChoiceKindView.Optional
                )
                .Select(SelectionFor)
                .ToArray()
        );

    private static async Task<ApplicationView> CompleteMatch(
        PlayModeApplication application,
        ApplicationView initial
    )
    {
        var current = initial;
        for (var count = 0; count < 256; count++)
        {
            if (current.Match!.Frame.IsComplete)
            {
                return current;
            }

            var action =
                current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.Attack
                )
                ?? current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.AttachEnergy
                )
                ?? current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.EndTurn
                )
                ?? current.Match.LegalActions.First();
            current = Value(
                await application.ApplyMatchAction(
                    current.Match.Frame.Id,
                    RequestFor(current.Match, action)
                )
            ).Application;
        }

        throw new InvalidOperationException("The match did not complete inside the test bound.");
    }

    private static MatchChoiceSelectionRequest SelectionFor(
        MatchChoiceRequirementView requirement
    ) =>
        requirement.Kind switch
        {
            MatchChoiceKindView.Optional => EmptySelection(requirement) with { Accepted = false },
            MatchChoiceKindView.Amount => EmptySelection(requirement) with
            {
                Amount = requirement.Minimum,
            },
            MatchChoiceKindView.Cards => EmptySelection(requirement) with
            {
                CardInstanceIds = requirement
                    .EligibleCards.Take(requirement.Minimum)
                    .Select(static card => card.Id)
                    .ToArray(),
            },
            MatchChoiceKindView.MechanicalType => EmptySelection(requirement) with
            {
                MechanicalType = requirement.EligibleMechanicalTypes[0].Value,
            },
            MatchChoiceKindView.Attack => EmptySelection(requirement) with
            {
                EffectId = requirement.EligibleEffects[0].Id,
            },
            MatchChoiceKindView.Distribution => EmptySelection(requirement) with
            {
                Distribution = [new(requirement.EligibleCards[0].Id, requirement.Maximum)],
            },
            MatchChoiceKindView.Attachments => EmptySelection(requirement) with
            {
                Attachments = requirement
                    .EligibleCards.Take(requirement.Minimum)
                    .Select(card => new MatchAttachmentRequest(
                        card.Id,
                        requirement.EligibleTargets[0].Id
                    ))
                    .ToArray(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(requirement)),
        };

    private static MatchChoiceSelectionRequest EmptySelection(
        MatchChoiceRequirementView requirement
    ) => new(requirement.Id, requirement.Kind, null, null, [], null, null, [], []);

    private static T Value<T>(ApiResponse<T> response)
        where T : class =>
        response.Succeeded && response.Value is not null
            ? response.Value
            : throw new InvalidOperationException(response.Error?.Message);

    private sealed class MemoryDocumentStore : IStateDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(
            StringComparer.Ordinal
        );
        private readonly object _lock = new();

        public DocumentStorageFailure? FailNextWrite { get; set; }

        public bool ConflictNextUpdate { get; set; }

        public Action? AfterProfileRead { get; set; }

        public Task<StoredDocument?> Read(string key, CancellationToken cancellationToken = default)
        {
            StoredDocument? document;
            lock (_lock)
            {
                document = _documents.GetValueOrDefault(key);
            }

            if (key == "profile")
            {
                AfterProfileRead?.Invoke();
            }

            return Task.FromResult(document);
        }

        public Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                ThrowIfWriteFails();
                if (_documents.ContainsKey(key))
                {
                    return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
                }
                _documents.Add(key, new(1, json));
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(1));
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

        public Task<DocumentDeleteResult> DeleteIfUnchanged(
            string key,
            long expectedRevision,
            string expectedJson,
            CancellationToken cancellationToken = default
        )
        {
            lock (_lock)
            {
                ThrowIfWriteFails();
                if (!_documents.TryGetValue(key, out var current))
                {
                    return Task.FromResult<DocumentDeleteResult>(
                        new DocumentDeleteResult.Missing()
                    );
                }
                if (current.Revision != expectedRevision || current.Json != expectedJson)
                {
                    return Task.FromResult<DocumentDeleteResult>(
                        new DocumentDeleteResult.Conflict()
                    );
                }
                _documents.Remove(key);
                return Task.FromResult<DocumentDeleteResult>(new DocumentDeleteResult.Deleted());
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
                ThrowIfWriteFails();
                if (ConflictNextUpdate)
                {
                    ConflictNextUpdate = false;
                    return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
                }
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

        private void ThrowIfWriteFails()
        {
            if (FailNextWrite is not { } failure)
            {
                return;
            }
            FailNextWrite = null;
            throw new DocumentStorageException(failure, "Simulated browser storage failure.");
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

        public virtual Task<DocumentDeleteResult> DeleteIfUnchanged(
            string key,
            long expectedRevision,
            string expectedJson,
            CancellationToken cancellationToken = default
        ) => inner.DeleteIfUnchanged(key, expectedRevision, expectedJson, cancellationToken);
    }

    private sealed class CommitThenCancelDocumentStore(
        IStateDocumentStore inner,
        CancellationTokenSource cancellation
    ) : DelegatingDocumentStore(inner)
    {
        private int _profileUpdateAttempts;

        public int ProfileUpdateAttempts => Volatile.Read(ref _profileUpdateAttempts);

        public override async Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (key != "profile")
            {
                return await base.Update(key, expectedRevision, json, cancellationToken);
            }

            var attempt = Interlocked.Increment(ref _profileUpdateAttempts);
            var result = await base.Update(key, expectedRevision, json, cancellationToken);

            if (attempt == 1 && result is DocumentWriteResult.Written)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }
    }

    private sealed class ConcurrentMigrationDocumentStore(
        IStateDocumentStore inner,
        int participants
    ) : DelegatingDocumentStore(inner)
    {
        private readonly object _lock = new();
        private readonly List<long> _migrationExpectedRevisions = [];
        private readonly TaskCompletionSource _migrationUpdatesReady = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _migrationUpdateAttempts;

        public int MigrationUpdateAttempts => Volatile.Read(ref _migrationUpdateAttempts);

        public long[] MigrationExpectedRevisions
        {
            get
            {
                lock (_lock)
                {
                    return [.. _migrationExpectedRevisions];
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
            if (key != "profile")
            {
                return await base.Update(key, expectedRevision, json, cancellationToken);
            }

            lock (_lock)
            {
                _migrationExpectedRevisions.Add(expectedRevision);
            }

            if (Interlocked.Increment(ref _migrationUpdateAttempts) == participants)
            {
                _migrationUpdatesReady.SetResult();
            }

            await _migrationUpdatesReady.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken
            );
            return await base.Update(key, expectedRevision, json, cancellationToken);
        }
    }

    private sealed class ServerHandler(ApplicationView? state) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}");
            if (state is null)
            {
                throw new InvalidOperationException("The browser-local journey called the server.");
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new ApiResponse<ApplicationView>(true, state, null)
                    ),
                }
            );
        }
    }
}
