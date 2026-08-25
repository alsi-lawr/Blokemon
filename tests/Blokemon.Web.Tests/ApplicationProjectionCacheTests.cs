using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Content;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class ApplicationProjectionCacheTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Operations_RebuildOnlyChangedCompleteViewSegments()
    {
        AssertExecutableProjectionMatrix();

        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var application = Local(catalogue, documents);

        var empty = Value(await application.State());
        Snapshot(application).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1, 1));
        await ShouldEqualColdReference(empty, catalogue, documents);

        var repeatedEmpty = Value(await application.State());
        Snapshot(application).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1, 1));
        JsonValue(repeatedEmpty).ShouldBe(JsonValue(empty));

        var beforeCreate = Snapshot(application);
        var created = Value(
            await application.CreateProfile(
                new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Projection Player")
            )
        );
        AssertDelta(beforeCreate, Snapshot(application), 1, 1, 0, 1, 0, 0, 1, 1);
        await ShouldEqualColdReference(created, catalogue, documents);

        await SetProfileId(documents, Guid.Parse("10000000-0000-0000-0000-000000000099"));
        application = Local(catalogue, documents);
        created = Value(await application.State());

        var identical = (await documents.Read("profile"))!;
        (
            await documents.Update("profile", identical.Revision, identical.Json)
        ).ShouldBeOfType<DocumentWriteResult.Written>();
        var beforeIdenticalExternalWrite = Snapshot(application);
        var identicalExternalWrite = Value(await application.State());
        AssertDelta(beforeIdenticalExternalWrite, Snapshot(application), 1, 0, 0, 0, 0, 0, 0, 0);
        created = identicalExternalWrite;

        var beforeClaim = Snapshot(application);
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        AssertDelta(beforeClaim, Snapshot(application), 1, 1, 1, 1, 0, 0, 0, 0);
        await ShouldEqualColdReference(claimed, catalogue, documents);

        var starterDeck = claimed.Decks.Single();
        var savedDeckId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var beforeSave = Snapshot(application);
        var saved = Value(
            await application.SaveDeck(
                new(savedDeckId, null, null, "Projection copy", starterDeck.Entries)
            )
        );
        AssertDelta(beforeSave, Snapshot(application), 1, 0, 1, 0, 0, 0, 0, 0);
        await ShouldEqualColdReference(saved, catalogue, documents);

        var beforeDelete = Snapshot(application);
        var deleted = Value(await application.DeleteDeck(new(Guid.NewGuid(), savedDeckId)));
        AssertDelta(beforeDelete, Snapshot(application), 1, 0, 1, 0, 0, 0, 0, 0);
        await ShouldEqualColdReference(deleted, catalogue, documents);

        var packCommand = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var beforePack = Snapshot(application);
        var opened = Value(await application.OpenPack(new(packCommand)));
        var afterPack = Snapshot(application);
        afterPack.Profile.ShouldBe(beforePack.Profile + 1);
        afterPack.Cards.ShouldBe(beforePack.Cards + 1);
        afterPack.LastPack.ShouldBe(beforePack.LastPack + 1);
        var drawnIds = opened
            .LastPack!.Cards.Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var deckOwnershipChanged = opened
            .Decks.SelectMany(static deck => deck.Entries)
            .Any(entry => drawnIds.Contains(entry.CardId));
        var starterLeaderOwnershipChanged = opened.StarterDecks.Any(starter =>
            drawnIds.Contains(starter.Leader.Id)
        );
        afterPack.Decks.ShouldBe(beforePack.Decks + (deckOwnershipChanged ? 1 : 0));
        afterPack.StarterDecks.ShouldBe(
            beforePack.StarterDecks + (starterLeaderOwnershipChanged ? 1 : 0)
        );
        afterPack.PackPresentation.ShouldBe(beforePack.PackPresentation);
        afterPack.Match.ShouldBe(beforePack.Match);
        afterPack.MatchError.ShouldBe(beforePack.MatchError);
        await ShouldEqualColdReference(opened, catalogue, documents);

        var beforePackRetry = Snapshot(application);
        var packRetry = Value(await application.OpenPack(new(packCommand)));
        Snapshot(application).ShouldBe(beforePackRetry);
        JsonValue(packRetry).ShouldBe(JsonValue(opened));

        var nextStarter = packRetry.StarterDecks.First(starter => starter.Id != "growroom");
        var beforeClaimWithReceipt = Snapshot(application);
        var claimedWithReceipt = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("10000000-0000-0000-0000-000000000008"), nextStarter.Id)
            )
        );
        var receiptOwnershipChanged = packRetry
            .LastPack!.Cards.Zip(claimedWithReceipt.LastPack!.Cards)
            .Any(pair => pair.First.OwnedQuantity != pair.Second.OwnedQuantity);
        AssertDelta(
            beforeClaimWithReceipt,
            Snapshot(application),
            1,
            1,
            1,
            1,
            0,
            receiptOwnershipChanged ? 1 : 0,
            0,
            0
        );
        await ShouldEqualColdReference(claimedWithReceipt, catalogue, documents);

        var beforeFailure = Snapshot(application);
        var failed = await application.SaveDeck(
            new(Guid.NewGuid(), null, null, "Invalid", [new("UNKNOWN-CARD", 60)])
        );
        failed.Succeeded.ShouldBeFalse();
        Snapshot(application).ShouldBe(beforeFailure);

        documents.ConflictNextUpdate = true;
        var conflict = await application.OpenPack(
            new(Guid.Parse("10000000-0000-0000-0000-000000000005"))
        );
        conflict.Succeeded.ShouldBeFalse();
        conflict.Error!.Code.ShouldBe("state.conflict");
        Snapshot(application).ShouldBe(beforeFailure);
        var afterConflict = Value(await application.State());
        Snapshot(application).ShouldBe(beforeFailure);
        JsonValue(afterConflict).ShouldBe(JsonValue(claimedWithReceipt));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            application.State(cancelled.Token)
        );
        Snapshot(application).ShouldBe(beforeFailure);

        var deckForMatch = afterConflict.Decks.Single(deck => deck.Id == starterDeck.Id);
        var beforeStart = Snapshot(application);
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("10000000-0000-0000-0000-000000000006"), deckForMatch.Id)
            )
        ).Application;
        AssertDelta(beforeStart, Snapshot(application), 0, 0, 0, 0, 0, 0, 1, 1);
        await ShouldEqualColdReference(started, catalogue, documents);

        var action = started.Match!.LegalActions.First();
        var beforeAction = Snapshot(application);
        var applied = Value(
            await application.ApplyMatchAction(
                started.Match.Frame.Id,
                RequestFor(started.Match, action)
            )
        ).Application;
        AssertDelta(beforeAction, Snapshot(application), 0, 0, 0, 0, 0, 0, 1, 1);
        await ShouldEqualColdReference(applied, catalogue, documents);

        var other = Local(catalogue, documents);
        var externalAction = applied.Match!.LegalActions.First();
        var externallyApplied = Value(
            await other.ApplyMatchAction(
                applied.Match.Frame.Id,
                RequestFor(applied.Match, externalAction)
            )
        ).Application;
        var beforeExternalMatch = Snapshot(application);
        var observedExternalMatch = Value(await application.State());
        AssertDelta(beforeExternalMatch, Snapshot(application), 0, 0, 0, 0, 0, 0, 1, 1);
        observedExternalMatch.Match!.Frame.Revision.ShouldBe(
            externallyApplied.Match!.Frame.Revision
        );
        await ShouldEqualColdReference(observedExternalMatch, catalogue, documents);

        documents.BlockNextMatchRead();
        var staleTask = application.State();
        await documents.BlockedMatchRead.WaitAsync(TimeSpan.FromSeconds(10));
        var newer = Value(
            await application.OpenPack(new(Guid.Parse("10000000-0000-0000-0000-000000000007")))
        );
        documents.ReleaseBlockedMatchRead();
        var stale = Value(await staleTask);
        stale.Profile!.Revision.ShouldBeLessThan(newer.Profile!.Revision);

        var afterStaleBuilds = Snapshot(application);
        var afterStaleCompletion = Value(await application.State());
        Snapshot(application).ShouldBe(afterStaleBuilds);
        JsonValue(afterStaleCompletion).ShouldBe(JsonValue(newer));
        await ShouldEqualColdReference(afterStaleCompletion, catalogue, documents);

        var beforePurge = Snapshot(application);
        var purged = Value(await application.PurgeData());
        AssertDelta(beforePurge, Snapshot(application), 1, 1, 1, 1, 0, 1, 1, 1);
        purged.Profile.ShouldBeNull();
        purged.Cards.ShouldAllBe(static card => card.OwnedQuantity == 0);
        await ShouldEqualColdReference(purged, catalogue, documents);

        var beforeRepeatedPurgeState = Snapshot(application);
        var repeatedPurgeState = Value(await application.State());
        Snapshot(application).ShouldBe(beforeRepeatedPurgeState);
        JsonValue(repeatedPurgeState).ShouldBe(JsonValue(purged));
    }

    [Test]
    public async Task HistoricalDeckChanges_UpdateOnlyTheAffectedCardUniverse()
    {
        const string historicalId = "UNKNOWN-HISTORICAL-DECK";
        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var setup = Local(catalogue, documents);
        Value(
            await setup.CreateProfile(
                new(Guid.Parse("20000000-0000-0000-0000-000000000001"), "History Player")
            )
        );
        var claimed = Value(
            await setup.ClaimStarterDeck(
                new(Guid.Parse("20000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        var originalEntries = claimed.Decks.Single().Entries;
        await MakeFirstDeckHistorical(documents, historicalId);

        var application = Local(catalogue, documents);
        var historical = Value(await application.State());
        Snapshot(application).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1, 1));
        historical.Cards.ShouldContain(card => card.Id == historicalId);
        await ShouldEqualColdReference(historical, catalogue, documents);

        var historicalDeck = historical.Decks.Single();
        var beforeRevision = Snapshot(application);
        var revised = Value(
            await application.SaveDeck(
                new(
                    Guid.NewGuid(),
                    historicalDeck.Id,
                    historicalDeck.Revision,
                    historicalDeck.Name,
                    originalEntries
                )
            )
        );
        AssertDelta(beforeRevision, Snapshot(application), 1, 1, 1, 0, 0, 0, 0, 0);
        revised.Cards.ShouldNotContain(card => card.Id == historicalId);
        await ShouldEqualColdReference(revised, catalogue, documents);

        await MakeFirstDeckHistorical(documents, historicalId);
        var beforeExternalHistoricalChange = Snapshot(application);
        var externallyChanged = Value(await application.State());
        AssertDelta(beforeExternalHistoricalChange, Snapshot(application), 1, 1, 1, 0, 0, 0, 0, 0);
        externallyChanged.Cards.ShouldContain(card => card.Id == historicalId);
        var beforeDelete = Snapshot(application);
        var deleted = Value(
            await application.DeleteDeck(new(Guid.NewGuid(), externallyChanged.Decks.Single().Id))
        );
        AssertDelta(beforeDelete, Snapshot(application), 1, 1, 1, 0, 0, 0, 0, 0);
        deleted.Cards.ShouldNotContain(card => card.Id == historicalId);
        await ShouldEqualColdReference(deleted, catalogue, documents);
    }

    [Test]
    public async Task CachedViews_EqualColdReconstructionForActiveCompletedAndDamagedMatches()
    {
        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var application = Local(catalogue, documents);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("30000000-0000-0000-0000-000000000001"), "Match Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("30000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        var active = Value(
            await application.StartMatch(
                new(Guid.Parse("30000000-0000-0000-0000-000000000003"), claimed.Decks.Single().Id)
            )
        ).Application;
        await ShouldEqualColdReference(active, catalogue, documents);

        var completed = await CompleteMatch(application, active);
        completed.Match!.Frame.IsComplete.ShouldBeTrue();
        await ShouldEqualColdReference(completed, catalogue, documents);

        var stored = (await documents.Read("match"))!;
        await documents.Update("match", stored.Revision, "{\"schemaVersion\":2}");
        var beforeDamagedMatch = Snapshot(application);
        var damaged = Value(await application.State());
        AssertDelta(beforeDamagedMatch, Snapshot(application), 0, 0, 0, 0, 0, 0, 1, 1);
        damaged.Match.ShouldBeNull();
        damaged.MatchError!.Code.ShouldBe("match.document_corrupt");
        await ShouldEqualColdReference(damaged, catalogue, documents);
    }

    [Test]
    public async Task AuthorityMigrationAndReplacement_ProduceTheColdCompleteView()
    {
        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var preserved = Local(catalogue, documents);
        Value(
            await preserved.CreateProfile(
                new(Guid.Parse("40000000-0000-0000-0000-000000000001"), "Authority Player")
            )
        );
        await SetProfileAuthority(documents, "previous-authority");

        var migrating = Local(catalogue, documents, ProfileAuthorityPolicy.MigrateCompatible);
        var migrated = Value(await migrating.State());
        Snapshot(migrating).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1, 1));
        JsonNode.Parse((await documents.Read("profile"))!.Json)!["profile"]![
            "authorityManifestVersion"
        ]!
            .GetValue<string>()
            .ShouldBe(catalogue.Mechanics.ManifestVersion);
        await ShouldEqualColdReference(migrated, catalogue, documents);

        var replacement = WithManifestVersion(catalogue, "replacement-authority");
        var replacementApplication = Local(replacement, documents);
        var replaced = Value(await replacementApplication.State());
        Snapshot(replacementApplication).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1, 1));
        await ShouldEqualColdReference(replaced, replacement, documents);
        JsonSerializer.Serialize(replaced, Json).ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task PublicResults_IsolateEveryMutableArrayFromProjectionTemplates()
    {
        typeof(MatchServiceResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(static property => property.Name)
            .ShouldBe(["Error", "Presentation", "View"], ignoreOrder: true);
        foreach (var methodName in new[] { "Apply", "Start", "State" })
        {
            typeof(LocalMatchService)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Single(method => method.Name == methodName)
                .ReturnType.ShouldBe(typeof(Task<MatchServiceResult>));
        }

        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var application = Local(catalogue, documents);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("50000000-0000-0000-0000-000000000001"), "Isolation Player")
            )
        );
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("50000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        Value(await application.OpenPack(new(Guid.Parse("50000000-0000-0000-0000-000000000003"))));
        var matchCommand = Guid.Parse("50000000-0000-0000-0000-000000000004");
        var matchDeck = claimed.Decks.Single().Id;
        var mutation = Value(await application.StartMatch(new(matchCommand, matchDeck)));
        var authoritative = Value(await application.State());
        var authoritativeJson = JsonValue(authoritative);
        var beforePoisonedState = Snapshot(application);

        var poisonedPaths = PoisonMutableArrays(mutation, "Mutation");
        poisonedPaths.UnionWith(PoisonMutableArrays(authoritative, "Application"));

        poisonedPaths.ShouldContain(static path => path.EndsWith(".Cards"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Rules"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".EnergyCost"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Decks"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Entries"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Errors"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Warnings"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".StarterDecks"));
        poisonedPaths.ShouldContain(static path => path.Contains(".LastPack.Cards"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Bench"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Hand"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".AttachedEnergy"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".AttachedTools"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".UnderlyingCards"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Conditions"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".LegalActions"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".ChoiceRequirements"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".EligibleCards"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".EligibleTargets"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".EligibleCardTypes"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Attacks"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".RecentEvents"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Steps"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".Events"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".TargetCardInstanceIds"));
        poisonedPaths.ShouldContain(static path => path.EndsWith(".RevealedCards"));

        var afterPoison = Value(await application.State());
        Snapshot(application).ShouldBe(beforePoisonedState);
        JsonValue(afterPoison).ShouldBe(authoritativeJson);
        ReferenceEquals(afterPoison.Cards, authoritative.Cards).ShouldBeFalse();
        ReferenceEquals(afterPoison.Match, authoritative.Match).ShouldBeFalse();

        var storedProfile = (await documents.Read("profile"))!;
        var product = JsonSerializer.Deserialize<ProductDocument>(storedProfile.Json, Json)!;
        var profile = ProductValue(LocalProfile.Restore(product.Profile, catalogue.Mechanics));

        var noMatchDocuments = new ControllableDocumentStore();
        var noMatch = await new LocalMatchService(catalogue, noMatchDocuments).State(
            profile,
            profile.DisplayName.Value
        );
        noMatch.View.ShouldBeNull();
        noMatch.Error.ShouldBeNull();
        noMatch.Presentation.ShouldBeNull();

        var damagedMatchDocuments = new ControllableDocumentStore();
        await damagedMatchDocuments.Create("match", "{broken");
        var damagedMatch = await new LocalMatchService(catalogue, damagedMatchDocuments).State(
            profile,
            profile.DisplayName.Value
        );
        damagedMatch.View.ShouldBeNull();
        damagedMatch.Error!.Code.ShouldBe("match.document_corrupt");
        damagedMatch.Presentation.ShouldBeNull();

        var publicMatches = new LocalMatchService(catalogue, documents);
        var publicResult = await publicMatches.State(profile, profile.DisplayName.Value);
        var publicJson = JsonValue(publicResult.View);
        PoisonMutableArrays(publicResult, "PublicMatch");

        var repeatedPublicResult = await publicMatches.State(profile, profile.DisplayName.Value);
        JsonValue(repeatedPublicResult.View).ShouldBe(publicJson);
        ReferenceEquals(publicResult.View, repeatedPublicResult.View).ShouldBeFalse();

        var publicStarted = await publicMatches.Start(
            profile,
            profile.DisplayName.Value,
            new(matchCommand, matchDeck)
        );
        JsonValue(publicStarted.View).ShouldBe(publicJson);
        PoisonMutableArrays(publicStarted, "PublicStart");
        JsonValue((await publicMatches.State(profile, profile.DisplayName.Value)).View)
            .ShouldBe(publicJson);

        var publicCurrent = await publicMatches.State(profile, profile.DisplayName.Value);
        var publicAction = publicCurrent.View!.LegalActions.First();
        var publicApplied = await publicMatches.Apply(
            profile,
            profile.DisplayName.Value,
            publicCurrent.View.Frame.Id,
            RequestFor(publicCurrent.View, publicAction)
        );
        publicApplied.Error.ShouldBeNull();
        var publicAppliedJson = JsonValue(publicApplied.View);
        PoisonMutableArrays(publicApplied, "PublicApply");
        JsonValue((await publicMatches.State(profile, profile.DisplayName.Value)).View)
            .ShouldBe(publicAppliedJson);
    }

    [Test]
    public async Task CancellationDuringIdentityConstructionSegmentsPublicationAndGateWait_DoesNotPublish()
    {
        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var application = Local(catalogue, documents);
        Value(
            await application.CreateProfile(
                new(Guid.Parse("60000000-0000-0000-0000-000000000001"), "Cancel Player")
            )
        );
        var authoritative = Value(await application.State());

        var sameDocument = (await documents.Read("profile"))!;
        await documents.Update("profile", sameDocument.Revision, sameDocument.Json);
        var beforeIdentityCancellation = Snapshot(application);
        var beforeIdentityBuilds = application.ProjectionIdentityBuildCount;
        using (var cancellation = new CancellationTokenSource())
        {
            application.ProjectionHooks.AfterProfileIdentityConstruction = new Action(
                cancellation.Cancel
            );
            await Should.ThrowAsync<OperationCanceledException>(() =>
                application.State(cancellation.Token)
            );
        }
        application.ProjectionHooks.AfterProfileIdentityConstruction = null;
        Snapshot(application).ShouldBe(beforeIdentityCancellation);
        application.ProjectionIdentityBuildCount.ShouldBe(beforeIdentityBuilds + 1);

        var afterIdentityCancellation = Value(await application.State());
        application.ProjectionIdentityBuildCount.ShouldBe(beforeIdentityBuilds + 2);
        AssertDelta(beforeIdentityCancellation, Snapshot(application), 1, 0, 0, 0, 0, 0, 0, 0);
        await ShouldEqualColdReference(afterIdentityCancellation, catalogue, documents);

        var beforeSegmentCancellation = Snapshot(application);
        var beforeSegmentIdentityBuilds = application.ProjectionIdentityBuildCount;
        using (var cancellation = new CancellationTokenSource())
        {
            application.ProjectionHooks.AfterSegmentConstruction =
                new Action<ApplicationProjectionSegment>(segment =>
                {
                    if (segment == ApplicationProjectionSegment.Cards)
                    {
                        cancellation.Cancel();
                    }
                });
            await Should.ThrowAsync<OperationCanceledException>(() =>
                application.OpenPack(
                    new(Guid.Parse("60000000-0000-0000-0000-000000000002")),
                    cancellation.Token
                )
            );
        }
        application.ProjectionHooks.AfterSegmentConstruction = null;
        AssertDelta(beforeSegmentCancellation, Snapshot(application), 1, 1, 0, 0, 0, 0, 0, 0);
        application.ProjectionIdentityBuildCount.ShouldBe(beforeSegmentIdentityBuilds + 1);

        var beforeSegmentRecovery = Snapshot(application);
        var afterSegmentCancellation = Value(await application.State());
        application.ProjectionIdentityBuildCount.ShouldBe(beforeSegmentIdentityBuilds + 2);
        afterSegmentCancellation.LastPack.ShouldNotBeNull();
        var recoveredCardIds = afterSegmentCancellation
            .LastPack.Cards.Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var recoveredStarterChanged = afterSegmentCancellation.StarterDecks.Any(starter =>
            recoveredCardIds.Contains(starter.Leader.Id)
        );
        AssertDelta(
            beforeSegmentRecovery,
            Snapshot(application),
            1,
            1,
            0,
            recoveredStarterChanged ? 1 : 0,
            0,
            1,
            0,
            0
        );
        await ShouldEqualColdReference(afterSegmentCancellation, catalogue, documents);

        var repeatedDocument = (await documents.Read("profile"))!;
        await documents.Update("profile", repeatedDocument.Revision, repeatedDocument.Json);
        var beforePublicationCancellation = Snapshot(application);
        var beforePublicationIdentityBuilds = application.ProjectionIdentityBuildCount;
        using (var cancellation = new CancellationTokenSource())
        {
            application.ProjectionHooks.BeforeTemplatePublication = new Action(cancellation.Cancel);
            await Should.ThrowAsync<OperationCanceledException>(() =>
                application.State(cancellation.Token)
            );
        }
        application.ProjectionHooks.BeforeTemplatePublication = null;
        AssertDelta(beforePublicationCancellation, Snapshot(application), 1, 0, 0, 0, 0, 0, 0, 0);
        application.ProjectionIdentityBuildCount.ShouldBe(beforePublicationIdentityBuilds + 1);

        var beforePublicationRecovery = Snapshot(application);
        var afterPublicationCancellation = Value(await application.State());
        application.ProjectionIdentityBuildCount.ShouldBe(beforePublicationIdentityBuilds + 2);
        AssertDelta(beforePublicationRecovery, Snapshot(application), 1, 0, 0, 0, 0, 0, 0, 0);
        await ShouldEqualColdReference(afterPublicationCancellation, catalogue, documents);

        var gateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var releaseGate = new ManualResetEventSlim();
        var blockOnce = 0;
        application.ProjectionHooks.BeforeTemplatePublication = new Action(() =>
        {
            if (Interlocked.CompareExchange(ref blockOnce, 1, 0) == 0)
            {
                gateEntered.TrySetResult();
                releaseGate.Wait(TimeSpan.FromSeconds(10));
            }
        });

        var beforeGateIdentityBuilds = application.ProjectionIdentityBuildCount;
        var gateHolder = Task.Run(async () => await application.State());
        await gateEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var gateCancellation = new CancellationTokenSource();
        var gateWaiter = application.State(gateCancellation.Token);
        gateCancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => gateWaiter);
        releaseGate.Set();
        await gateHolder;
        application.ProjectionHooks.BeforeTemplatePublication = null;
        application.ProjectionIdentityBuildCount.ShouldBe(beforeGateIdentityBuilds);

        var afterGateCancellationBuilds = Snapshot(application);
        var afterGateCancellation = Value(await application.State());
        Snapshot(application).ShouldBe(afterGateCancellationBuilds);
        await ShouldEqualColdReference(afterGateCancellation, catalogue, documents);
        JsonValue(afterGateCancellation).ShouldBe(JsonValue(afterPublicationCancellation));
        JsonValue(authoritative).ShouldNotBeNullOrWhiteSpace();
    }

    private static async Task<ApplicationView> CompleteMatch(
        LocalApplicationService application,
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
                current.Match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind == MatchActionKindView.Attack
                )
                ?? current.Match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind == MatchActionKindView.AttachEnergy
                )
                ?? current.Match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind == MatchActionKindView.EndTurn
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

    private static async Task ShouldEqualColdReference(
        ApplicationView actual,
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents
    )
    {
        var expected = Value(await Local(catalogue, documents).State());
        JsonValue(actual).ShouldBe(JsonValue(expected));
    }

    private static string JsonValue<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static HashSet<string> PoisonMutableArrays(object root, string rootPath)
    {
        var arrays = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        void Visit(object? value, string path)
        {
            if (value is null || value is string)
            {
                return;
            }

            var type = value.GetType();
            if (type.IsValueType)
            {
                return;
            }

            if (value is Array array)
            {
                arrays.Add(path);
                for (var index = 0; index < array.Length; index++)
                {
                    Visit(array.GetValue(index), $"{path}[{index}]");
                }

                if (array.Length > 0)
                {
                    var elementType = type.GetElementType()!;
                    array.SetValue(
                        elementType == typeof(string) ? "POISON"
                            : elementType.IsValueType ? Activator.CreateInstance(elementType)
                            : null,
                        0
                    );
                }
                return;
            }

            if (!visited.Add(value))
            {
                return;
            }

            foreach (
                var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(static property =>
                        property.CanRead
                        && property.GetIndexParameters().Length == 0
                        && (
                            property.CanWrite
                            || property.DeclaringType == typeof(MatchServiceResult)
                        )
                    )
            )
            {
                Visit(property.GetValue(value), $"{path}.{property.Name}");
            }
        }

        Visit(root, rootPath);
        return arrays;
    }

    private static ApplicationProjectionDependency Sources(
        params ApplicationProjectionDependency[] dependencies
    ) =>
        dependencies.Aggregate(
            ApplicationProjectionDependency.None,
            static (current, dependency) => current | dependency
        );

    private static void AssertExecutableProjectionMatrix()
    {
        ApplicationProjectionMatrix
            .fields.Select(static row => row.Segment)
            .ShouldBe(Enum.GetValues<ApplicationProjectionSegment>());
        ApplicationProjectionMatrix
            .operations.Select(static row => row.Operation)
            .ShouldBe(Enum.GetValues<ApplicationProjectionOperation>());

        Field(ApplicationProjectionSegment.Profile)
            .ShouldBe(ApplicationProjectionDependency.ProfileSummary);
        Field(ApplicationProjectionSegment.Cards)
            .ShouldBe(
                Sources(
                    ApplicationProjectionDependency.Catalogue,
                    ApplicationProjectionDependency.CardUniverseAndOwnership
                )
            );
        Field(ApplicationProjectionSegment.Decks)
            .ShouldBe(
                Sources(
                    ApplicationProjectionDependency.Catalogue,
                    ApplicationProjectionDependency.SavedDecksAndOwnership
                )
            );
        Field(ApplicationProjectionSegment.StarterDecks)
            .ShouldBe(
                Sources(
                    ApplicationProjectionDependency.Catalogue,
                    ApplicationProjectionDependency.StarterClaimsAndOwnership
                )
            );
        Field(ApplicationProjectionSegment.PackPresentation)
            .ShouldBe(ApplicationProjectionDependency.Catalogue);
        Field(ApplicationProjectionSegment.LastPack)
            .ShouldBe(
                Sources(
                    ApplicationProjectionDependency.Catalogue,
                    ApplicationProjectionDependency.PackHistoryAndOwnership
                )
            );
        var matchDependencies = Sources(
            ApplicationProjectionDependency.Catalogue,
            ApplicationProjectionDependency.MatchProfile,
            ApplicationProjectionDependency.MatchDocument
        );
        Field(ApplicationProjectionSegment.Match).ShouldBe(matchDependencies);
        Field(ApplicationProjectionSegment.MatchError).ShouldBe(matchDependencies);
        Field(ApplicationProjectionSegment.MatchRecovery).ShouldBe(matchDependencies);

        foreach (
            var operation in new[]
            {
                ApplicationProjectionOperation.State,
                ApplicationProjectionOperation.CreateProfile,
                ApplicationProjectionOperation.OpenPack,
                ApplicationProjectionOperation.ClaimStarterDeck,
                ApplicationProjectionOperation.SaveDeck,
                ApplicationProjectionOperation.DeleteDeck,
                ApplicationProjectionOperation.AbandonSavedMatch,
                ApplicationProjectionOperation.DiscardMatchHistory,
            }
        )
        {
            MatchSource(operation).ShouldBe(MatchProjectionSource.LoadSavedMatch);
        }
        MatchSource(ApplicationProjectionOperation.StartMatch)
            .ShouldBe(MatchProjectionSource.UseCommittedMatch);
        MatchSource(ApplicationProjectionOperation.ApplyMatchAction)
            .ShouldBe(MatchProjectionSource.UseCommittedMatch);
        MatchSource(ApplicationProjectionOperation.PurgeData)
            .ShouldBe(MatchProjectionSource.NoMatch);

        static ApplicationProjectionDependency Field(ApplicationProjectionSegment segment) =>
            ApplicationProjectionMatrix.fields.Single(row => row.Segment == segment).Dependencies;

        static MatchProjectionSource MatchSource(ApplicationProjectionOperation operation) =>
            ApplicationProjectionMatrix
                .operations.Single(row => row.Operation == operation)
                .MatchSource;
    }

    private static void AssertDelta(
        BuildCounts before,
        BuildCounts after,
        long profile,
        long cards,
        long decks,
        long starterDecks,
        long packPresentation,
        long lastPack,
        long match,
        long matchError,
        long? matchRecovery = null
    ) =>
        after.ShouldBe(
            new(
                before.Profile + profile,
                before.Cards + cards,
                before.Decks + decks,
                before.StarterDecks + starterDecks,
                before.PackPresentation + packPresentation,
                before.LastPack + lastPack,
                before.Match + match,
                before.MatchError + matchError,
                before.MatchRecovery + (matchRecovery ?? matchError)
            )
        );

    private static BuildCounts Snapshot(LocalApplicationService application)
    {
        var counts = application.ProjectionBuildCounts;
        return new(
            counts.Profile,
            counts.Cards,
            counts.Decks,
            counts.StarterDecks,
            counts.PackPresentation,
            counts.LastPack,
            counts.Match,
            counts.MatchError,
            counts.MatchRecovery
        );
    }

    private static async Task MakeFirstDeckHistorical(
        ControllableDocumentStore documents,
        string historicalId
    )
    {
        var stored = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(stored.Json)!.AsObject();
        var profile = document["profile"]!.AsObject();
        profile["authorityManifestVersion"] = "historical-authority";
        profile["savedDecks"]![0]!["cards"]![0]!["cardId"] = historicalId;
        (
            await documents.Update("profile", stored.Revision, document.ToJsonString())
        ).ShouldBeOfType<DocumentWriteResult.Written>();
    }

    private static async Task SetProfileAuthority(
        ControllableDocumentStore documents,
        string authority
    )
    {
        var stored = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(stored.Json)!.AsObject();
        document["profile"]!["authorityManifestVersion"] = authority;
        (
            await documents.Update("profile", stored.Revision, document.ToJsonString())
        ).ShouldBeOfType<DocumentWriteResult.Written>();
    }

    private static async Task SetProfileId(ControllableDocumentStore documents, Guid profileId)
    {
        var stored = (await documents.Read("profile"))!;
        var document = JsonNode.Parse(stored.Json)!.AsObject();
        document["profile"]!["profileId"] = profileId.ToString("D");
        (
            await documents.Update("profile", stored.Revision, document.ToJsonString())
        ).ShouldBeOfType<DocumentWriteResult.Written>();
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
        var starters = JsonNode
            .Parse(bootstrap["starterDecksJson"]!.GetValue<string>())!
            .AsObject();
        starters["mechanicalManifestVersion"] = manifestVersion;
        bootstrap["starterDecksJson"] = starters.ToJsonString();
        return BlokemonCatalogue.FromBootstrapJson(bootstrap.ToJsonString());
    }

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    private static LocalApplicationService Local(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        ProfileAuthorityPolicy policy = ProfileAuthorityPolicy.Preserve
    ) =>
        new(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents),
            EconomyRules.Unlimited,
            policy
        );

    private static T Value<T>(ApiResponse<T> response)
        where T : class =>
        response.Succeeded && response.Value is not null
            ? response.Value
            : throw new InvalidOperationException(response.Error?.Message);

    private static TValue ProductValue<TValue, TFailure>(DomainResult<TValue, TFailure> result)
        where TValue : notnull
        where TFailure : notnull =>
        result is DomainResult<TValue, TFailure>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("The product fixture transition failed.");

    private sealed class ControllableDocumentStore : IStateDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(
            StringComparer.Ordinal
        );
        private readonly object _lock = new();
        private TaskCompletionSource _blockedMatchRead = NewSignal();
        private TaskCompletionSource _releaseMatchRead = NewSignal();
        private int _blockNextMatchRead;

        public bool ConflictNextUpdate { get; set; }

        public Task BlockedMatchRead => _blockedMatchRead.Task;

        public void BlockNextMatchRead()
        {
            _blockedMatchRead = NewSignal();
            _releaseMatchRead = NewSignal();
            Interlocked.Exchange(ref _blockNextMatchRead, 1);
        }

        public void ReleaseBlockedMatchRead() => _releaseMatchRead.TrySetResult();

        public async Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        )
        {
            if (key == "match" && Interlocked.CompareExchange(ref _blockNextMatchRead, 0, 1) == 1)
            {
                _blockedMatchRead.TrySetResult();
                await _releaseMatchRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            lock (_lock)
            {
                return _documents.GetValueOrDefault(key);
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

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record BuildCounts(
        long Profile,
        long Cards,
        long Decks,
        long StarterDecks,
        long PackPresentation,
        long LastPack,
        long Match,
        long MatchError,
        long MatchRecovery
    );
}
