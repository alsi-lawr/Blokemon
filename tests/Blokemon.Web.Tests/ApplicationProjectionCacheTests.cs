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
        var catalogue = Catalogue();
        var documents = new ControllableDocumentStore();
        var application = Local(catalogue, documents);

        var empty = Value(await application.State());
        Snapshot(application).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1));
        await ShouldEqualColdReference(empty, catalogue, documents);

        var repeatedEmpty = Value(await application.State());
        AssertSameSegments(empty, repeatedEmpty);
        Snapshot(application).ShouldBe(new(1, 1, 1, 1, 1, 1, 1, 1));

        var created = Value(
            await application.CreateProfile(
                new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Projection Player")
            )
        );
        created.Decks.ShouldBeSameAs(empty.Decks);
        created.PackPresentation.ShouldBeSameAs(empty.PackPresentation);
        created.LastPack.ShouldBeSameAs(empty.LastPack);
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
        identicalExternalWrite.Cards.ShouldBeSameAs(created.Cards);
        identicalExternalWrite.Decks.ShouldBeSameAs(created.Decks);
        identicalExternalWrite.StarterDecks.ShouldBeSameAs(created.StarterDecks);
        created = identicalExternalWrite;

        var beforeClaim = Snapshot(application);
        var claimed = Value(
            await application.ClaimStarterDeck(
                new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        AssertDelta(beforeClaim, Snapshot(application), 1, 1, 1, 1, 0, 0, 0, 0);
        claimed.PackPresentation.ShouldBeSameAs(created.PackPresentation);
        claimed.LastPack.ShouldBeSameAs(created.LastPack);
        claimed.Match.ShouldBeSameAs(created.Match);
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
        saved.Cards.ShouldBeSameAs(claimed.Cards);
        saved.StarterDecks.ShouldBeSameAs(claimed.StarterDecks);
        saved.LastPack.ShouldBeSameAs(claimed.LastPack);
        await ShouldEqualColdReference(saved, catalogue, documents);

        var beforeDelete = Snapshot(application);
        var deleted = Value(await application.DeleteDeck(new(Guid.NewGuid(), savedDeckId)));
        AssertDelta(beforeDelete, Snapshot(application), 1, 0, 1, 0, 0, 0, 0, 0);
        deleted.Cards.ShouldBeSameAs(saved.Cards);
        deleted.StarterDecks.ShouldBeSameAs(saved.StarterDecks);
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
        AssertSameSegments(opened, packRetry);
        Snapshot(application).ShouldBe(beforePackRetry);

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
        AssertSameSegments(packRetry, afterConflict);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            application.State(cancelled.Token)
        );
        Snapshot(application).ShouldBe(beforeFailure);

        var deckForMatch = afterConflict.Decks.Single();
        var beforeStart = Snapshot(application);
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("10000000-0000-0000-0000-000000000006"), deckForMatch.Id)
            )
        ).Application;
        AssertDelta(beforeStart, Snapshot(application), 0, 0, 0, 0, 0, 0, 1, 1);
        AssertProfileSegmentsSame(afterConflict, started);
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
        AssertProfileSegmentsSame(started, applied);
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
        AssertProfileSegmentsSame(applied, observedExternalMatch);
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

        var afterStaleCompletion = Value(await application.State());
        afterStaleCompletion.Profile.ShouldBeSameAs(newer.Profile);
        afterStaleCompletion.Cards.ShouldBeSameAs(newer.Cards);
        afterStaleCompletion.LastPack.ShouldBeSameAs(newer.LastPack);
        await ShouldEqualColdReference(afterStaleCompletion, catalogue, documents);

        var beforePurge = Snapshot(application);
        var purged = Value(await application.PurgeData());
        AssertDelta(beforePurge, Snapshot(application), 1, 1, 1, 1, 0, 1, 1, 1);
        purged.Profile.ShouldBeNull();
        purged.Cards.ShouldAllBe(static card => card.OwnedQuantity == 0);
        purged.PackPresentation.ShouldBeSameAs(afterStaleCompletion.PackPresentation);
        await ShouldEqualColdReference(purged, catalogue, documents);

        var repeatedPurgeState = Value(await application.State());
        AssertSameSegments(purged, repeatedPurgeState);
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
        revised.StarterDecks.ShouldBeSameAs(historical.StarterDecks);
        await ShouldEqualColdReference(revised, catalogue, documents);

        await MakeFirstDeckHistorical(documents, historicalId);
        var externallyChanged = Value(await application.State());
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
        var damaged = Value(await application.State());
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
        JsonNode.Parse((await documents.Read("profile"))!.Json)!["profile"]![
            "authorityManifestVersion"
        ]!
            .GetValue<string>()
            .ShouldBe(catalogue.Mechanics.ManifestVersion);
        await ShouldEqualColdReference(migrated, catalogue, documents);

        var replacement = WithManifestVersion(catalogue, "replacement-authority");
        var replaced = Value(await Local(replacement, documents).State());
        await ShouldEqualColdReference(replaced, replacement, documents);
        JsonSerializer.Serialize(replaced, Json).ShouldNotBeNullOrWhiteSpace();
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
        JsonSerializer.Serialize(actual, Json).ShouldBe(JsonSerializer.Serialize(expected, Json));
    }

    private static void AssertProfileSegmentsSame(ApplicationView before, ApplicationView after)
    {
        after.Profile.ShouldBeSameAs(before.Profile);
        after.Cards.ShouldBeSameAs(before.Cards);
        after.Decks.ShouldBeSameAs(before.Decks);
        after.StarterDecks.ShouldBeSameAs(before.StarterDecks);
        after.PackPresentation.ShouldBeSameAs(before.PackPresentation);
        after.LastPack.ShouldBeSameAs(before.LastPack);
    }

    private static void AssertSameSegments(ApplicationView before, ApplicationView after)
    {
        AssertProfileSegmentsSame(before, after);
        after.Match.ShouldBeSameAs(before.Match);
        after.MatchError.ShouldBeSameAs(before.MatchError);
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
        long matchError
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
                before.MatchError + matchError
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
            counts.MatchError
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
        long MatchError
    );
}
