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
    private const string CompatibleLegacyAuthority = "sv151-candidate.17";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompatibleLegacyActiveMatch_MigratesThroughBackupAndColdReplayForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, fixture.Store, profile);

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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity(
                "match",
                2,
                CompatibleLegacyAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );
        repeated.Error.ShouldBeNull();
        JsonSerializer.Serialize(repeated.View).ShouldBe(JsonSerializer.Serialize(restored.View));
        (await fixture.Store.Read("match")).ShouldBe(migrated);
        (await fixture.Store.Read(BackupKey("match", source))).ShouldBe(backup);
        (await fixture.Store.Read("profile")).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompatibleLegacyHistory_MigratesBeforeReplacementForEachProvider(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        var historySource = await CreateCompatibleLegacyHistory(catalogue, fixture.Store, profile);
        var service = new LocalMatchService(catalogue, fixture.Store);

        var started = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000003"), _firstDeck)
        );
        var history = (await fixture.Store.Read("match-history"))!;
        var backup = (await fixture.Store.Read(BackupKey("match-history", historySource)))!;

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
            MigrationIdentity(
                "match-history",
                2,
                CompatibleLegacyAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StructurallyInvalidPreD3History_IsCorruptAndPreservedWithoutABackup(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        await fixture.Store.Create("match-history", Fixture("pre-d3-match-history.json"));
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);
        var restored = await service.State(profile, profile.DisplayName.Value);

        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000004"), _firstDeck)
        );

        restored.Error!.Code.ShouldBe("match.history_corrupt");
        restored.View.ShouldBeNull();
        rejected.Error!.Code.ShouldBe("match.history_corrupt");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StructurallyInvalidUnpaidRingRoadHistory_IsCorruptAndPreservedForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        await fixture.Store.Create(
            "match-history",
            HistoryFixtureAtVersion(
                "schema-two-unpaid-ring-road-match-history.json",
                2,
                "sv151-candidate.17"
            )
        );
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);

        var restored = await service.State(profile, profile.DisplayName.Value);
        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000006"), _firstDeck)
        );

        restored.Error!.Code.ShouldBe("match.history_corrupt");
        restored.View.ShouldBeNull();
        rejected.Error!.Code.ShouldBe("match.history_corrupt");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LegacyVimDodgyHistory_IsAuthorityChangedAndPreservedForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        await fixture.Store.Create("match-history", HistoryAtVersion(2, CompatibleLegacyAuthority));
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);

        var restored = await service.State(profile, profile.DisplayName.Value);
        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000007"), _firstDeck)
        );

        restored.Error!.Code.ShouldBe("match.history_authority_changed");
        restored.View.ShouldBeNull();
        rejected.Error!.Code.ShouldBe("match.history_authority_changed");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IncompatiblePreD3ActiveMatch_IsTypedAndPreservedWithoutABackup(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var documents = fixture.Store;
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "pre-d3-profile.json");
        await documents.Create("match", Fixture("pre-d3-active-match.json"));
        var source = (await documents.Read("match"))!;

        var restored = await new LocalMatchService(catalogue, documents).StateProjection(
            profile,
            profile.DisplayName.Value,
            CancellationToken.None
        );

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("match.authority_changed");
        restored.Recovery!.Kind.ShouldBe(
            MatchRecoveryKindView.ActiveMatchIncompatibleWithCurrentRules
        );
        (await documents.Read("match")).ShouldBe(source);
        (await documents.Read(BackupKey("match", source))).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentMigrationAttempts_ConvergeOnOneCandidateAndOneBackup(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var inner = fixture.Store;
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, inner, profile);
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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity(
                "match",
                2,
                CompatibleLegacyAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAfterBackupCommit_PreservesTheSourceAndRecoverableBackup(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var inner = fixture.Store;
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, inner, profile);
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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity(
                "match",
                2,
                CompatibleLegacyAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );

        var retried = await new LocalMatchService(catalogue, inner).State(
            profile,
            profile.DisplayName.Value
        );
        retried.Error.ShouldBeNull();
        (await inner.Read(BackupKey("match", source))).ShouldBe(backup);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationReportedAfterPrimaryCommit_ReconcilesTheExactCommittedCandidate(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var inner = fixture.Store;
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, inner, profile);
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

    [Test]
    [Arguments(false, 2, "sv151-candidate.12")]
    [Arguments(true, 2, "sv151-candidate.12")]
    [Arguments(false, 1, "sv151-candidate.15")]
    [Arguments(true, 1, "sv151-candidate.15")]
    [Arguments(false, 2, "arbitrary-authority")]
    [Arguments(true, 2, "arbitrary-authority")]
    public async Task UnsupportedActiveMatchPair_PreservesPrimaryAndBackupSpace(
        bool sqlite,
        int sourceSchema,
        string sourceAuthority
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        await fixture.Store.Create("match", MatchAtVersion(sourceSchema, sourceAuthority));
        var source = (await fixture.Store.Read("match"))!;

        var restored = await new LocalMatchService(catalogue, fixture.Store).State(
            profile,
            profile.DisplayName.Value
        );

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("match.document_version");
        (await fixture.Store.Read("match")).ShouldBe(source);
        (await fixture.Store.Read(BackupKey("match", source))).ShouldBeNull();
    }

    [Test]
    [Arguments(false, 2, "sv151-candidate.12")]
    [Arguments(true, 2, "sv151-candidate.12")]
    [Arguments(false, 1, "sv151-candidate.15")]
    [Arguments(true, 1, "sv151-candidate.15")]
    [Arguments(false, 2, "arbitrary-authority")]
    [Arguments(true, 2, "arbitrary-authority")]
    public async Task UnsupportedHistoryPair_PreservesPrimaryAndBackupSpace(
        bool sqlite,
        int sourceSchema,
        string sourceAuthority
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(sourceSchema, sourceAuthority)
        );
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);
        var restored = await service.State(profile, profile.DisplayName.Value);

        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000005"), _firstDeck)
        );

        restored.Error!.Code.ShouldBe("match.history_version");
        restored.View.ShouldBeNull();
        rejected.Error!.Code.ShouldBe("match.history_version");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CorruptSupportedMatch_IsTypedAndPreservedWithoutABackup(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var corrupt = JsonNode.Parse(MatchAtVersion(2, "sv151-candidate.16"))!.AsObject();
        corrupt.Remove("commands");
        await fixture.Store.Create("match", corrupt.ToJsonString());
        var source = (await fixture.Store.Read("match"))!;

        var service = new LocalMatchService(catalogue, fixture.Store);
        var restored = await service.State(profile, profile.DisplayName.Value);
        var projection = await service.StateProjection(
            profile,
            profile.DisplayName.Value,
            CancellationToken.None
        );

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("match.document_corrupt");
        projection.Recovery.ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBe(source);
        (await fixture.Store.Read(BackupKey("match", source))).ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DivergentPrimaryConflict_PreservesTheExternalDocumentAndRollbackBackup(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var inner = fixture.Store;
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, inner, profile);
        var externalJson = source.Json + " ";
        var documents = new DivergentMatchUpdateStore(inner, externalJson);

        var restored = await new LocalMatchService(catalogue, documents).StateProjection(
            profile,
            profile.DisplayName.Value,
            CancellationToken.None
        );
        var primary = (await inner.Read("match"))!;
        var backup = (await inner.Read(BackupKey("match", source)))!;

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("state.conflict");
        restored.Recovery.ShouldBeNull();
        primary.Revision.ShouldBe(source.Revision + 1);
        primary.Json.ShouldBe(externalJson);
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity(
                "match",
                2,
                CompatibleLegacyAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConflictingBackup_IsNeverReplacedAndLeavesThePrimaryRollbackReadable(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue);
        var source = await CreateCompatibleLegacyActiveMatch(catalogue, fixture.Store, profile);
        const string conflictingBackup = "{\"occupied\":true}";
        await fixture.Store.Create(BackupKey("match", source), conflictingBackup);
        var backup = (await fixture.Store.Read(BackupKey("match", source)))!;

        var restored = await new LocalMatchService(catalogue, fixture.Store).State(
            profile,
            profile.DisplayName.Value
        );

        restored.View.ShouldBeNull();
        restored.Error!.Code.ShouldBe("state.conflict");
        (await fixture.Store.Read("match")).ShouldBe(source);
        (await fixture.Store.Read(BackupKey("match", source))).ShouldBe(backup);
        backup.Json.ShouldBe(conflictingBackup);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AbandonUnsupportedActiveMatch_DeletesOnlyTheConfirmedPrimaryForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(2, catalogue.Mechanics.ManifestVersion)
        );
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var source = (await fixture.Store.Read("match"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var history = (await fixture.Store.Read("match-history"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);

        var gated = Value(await application.State());
        var recovery = gated.MatchRecovery!;
        var abandoned = Value(
            await application.AbandonSavedMatch(new(recovery.Revision, recovery.ContentIdentity))
        );
        var repeated = Value(
            await application.AbandonSavedMatch(new(recovery.Revision, recovery.ContentIdentity))
        );

        recovery.Kind.ShouldBe(MatchRecoveryKindView.ActiveMatchUnsupportedVersion);
        recovery.Revision.ShouldBe(source.Revision);
        abandoned.Match.ShouldBeNull();
        abandoned.MatchError.ShouldBeNull();
        abandoned.MatchRecovery.ShouldBeNull();
        repeated.MatchRecovery.ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBeNull();
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBe(["match"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaleActiveMatchConfirmation_PreservesReplacementAndEveryOtherDocument(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(2, catalogue.Mechanics.ManifestVersion)
        );
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var recovery = Value(await application.State()).MatchRecovery!;
        var source = (await fixture.Store.Read("match"))!;
        await fixture.Store.Delete("match");
        await fixture.Store.Create("match", source.Json + " ");
        var replacement = (await fixture.Store.Read("match"))!;
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;

        var stale = await application.AbandonSavedMatch(
            new(recovery.Revision, recovery.ContentIdentity)
        );

        stale.Succeeded.ShouldBeFalse();
        stale.Error!.Code.ShouldBe("match.recovery_stale");
        (await fixture.Store.Read("match")).ShouldBe(replacement);
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentActiveRecoveryRequests_ConvergeWithoutTouchingOtherKeys(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-history", "history-sentinel");
        var first = Application(catalogue, fixture.Store);
        var second = Application(catalogue, fixture.Store);
        var recovery = Value(await first.State()).MatchRecovery!;
        var request = new AbandonSavedMatchRequest(recovery.Revision, recovery.ContentIdentity);
        var profile = (await fixture.Store.Read("profile"))!;
        var history = (await fixture.Store.Read("match-history"))!;

        var responses = await Task.WhenAll(
            first.AbandonSavedMatch(request),
            second.AbandonSavedMatch(request)
        );

        responses.ShouldAllBe(static response => response.Succeeded);
        responses.ShouldAllBe(static response => response.Value!.MatchRecovery == null);
        (await fixture.Store.Read("match")).ShouldBeNull();
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-history")).ShouldBe(history);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledActiveRecovery_PreservesTheGateAndEveryDocument(bool sqlite)
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-history", "history-sentinel");
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var recovery = Value(await application.State()).MatchRecovery!;
        var source = (await fixture.Store.Read("match"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var history = (await fixture.Store.Read("match-history"))!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            application.AbandonSavedMatch(
                new(recovery.Revision, recovery.ContentIdentity),
                cancellation.Token
            )
        );

        (await fixture.Store.Read("match")).ShouldBe(source);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        Value(await application.State()).MatchRecovery.ShouldBe(recovery);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DiscardUnsupportedHistory_AloneRemovesTheTypedStartGateForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "history-profile.json")
        );
        var currentProfile = Profile(catalogue, "history-profile.json");
        var active = await CreateCurrentCompletedMatch(catalogue, fixture.Store, currentProfile);
        await fixture.Store.Create("match-history", HistoryAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var gated = Value(await application.State());
        var recovery = gated.MatchRecovery!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;

        var blockedStart = Value(
            await application.StartMatch(
                new(Guid.Parse("30000000-0000-0000-0000-000000000008"), _firstDeck)
            )
        );

        blockedStart.Outcome.ShouldBe(MatchMutationOutcomeView.RecoveryRequired);
        blockedStart.Application.MatchRecovery.ShouldBe(recovery);
        (await fixture.Store.Read("match")).ShouldBe(active);

        var discarded = Value(
            await application.DiscardMatchHistory(new(recovery.Revision, recovery.ContentIdentity))
        );
        var repeated = Value(
            await application.DiscardMatchHistory(new(recovery.Revision, recovery.ContentIdentity))
        );

        discarded.Match.ShouldNotBeNull();
        discarded.MatchError.ShouldBeNull();
        discarded.MatchRecovery.ShouldBeNull();
        repeated.MatchRecovery.ShouldBeNull();
        (await fixture.Store.Read("match-history")).ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBe(active);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBe(["match-history"]);

        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("30000000-0000-0000-0000-000000000009"), _firstDeck)
            )
        );
        started.Outcome.ShouldBe(MatchMutationOutcomeView.Applied);
        started.Application.Match!.Frame.Id.ShouldBe(
            Guid.Parse("30000000-0000-0000-0000-000000000009")
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AbandonUnsupportedActiveMatch_RevealsTheIndependentHistoryGateForEachProvider(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-history", HistoryAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var activeRecovery = Value(await application.State()).MatchRecovery!;

        var abandoned = Value(
            await application.AbandonSavedMatch(
                new(activeRecovery.Revision, activeRecovery.ContentIdentity)
            )
        );
        var reloaded = Value(await Application(catalogue, documents).State());

        activeRecovery.Kind.ShouldBe(MatchRecoveryKindView.ActiveMatchUnsupportedVersion);
        abandoned.Match.ShouldBeNull();
        abandoned.MatchError!.Code.ShouldBe("match.history_version");
        abandoned.MatchRecovery!.Kind.ShouldBe(
            MatchRecoveryKindView.MatchHistoryUnsupportedVersion
        );
        reloaded.MatchRecovery.ShouldBe(abandoned.MatchRecovery);
        (await fixture.Store.Read("match")).ShouldBeNull();
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBe(["match"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnsupportedHistoryWithoutActiveMatch_GatesReloadAndStartUntilDiscardSucceeds(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "history-profile.json")
        );
        await fixture.Store.Create("match-history", HistoryAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var initial = Value(await application.State());
        var recovery = initial.MatchRecovery!;

        var reloaded = Value(await Application(catalogue, documents).State());
        var blockedStart = Value(
            await application.StartMatch(
                new(Guid.Parse("30000000-0000-0000-0000-00000000000a"), _firstDeck)
            )
        );
        var blockedMatch = await fixture.Store.Read("match");

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() =>
                application.DiscardMatchHistory(
                    new(recovery.Revision, recovery.ContentIdentity),
                    cancellation.Token
                )
            );
        }

        var failedDocuments = new RecoveryDeleteFaultStore(
            documents,
            "match-history",
            RecoveryDeleteFault.FailBeforeCommit
        );
        await Should.ThrowAsync<DocumentStorageException>(() =>
            Application(catalogue, failedDocuments)
                .DiscardMatchHistory(new(recovery.Revision, recovery.ContentIdentity))
        );
        var retained = Value(await Application(catalogue, documents).State());
        var retainedHistory = await fixture.Store.Read("match-history");
        var retainedProfile = await fixture.Store.Read("profile");
        var retainedBackup = await fixture.Store.Read("match-migration-backup/sentinel");

        var discarded = Value(
            await application.DiscardMatchHistory(new(recovery.Revision, recovery.ContentIdentity))
        );
        var started = Value(
            await application.StartMatch(
                new(Guid.Parse("30000000-0000-0000-0000-00000000000b"), _firstDeck)
            )
        );

        initial.Match.ShouldBeNull();
        initial.MatchError!.Code.ShouldBe("match.history_version");
        recovery.Kind.ShouldBe(MatchRecoveryKindView.MatchHistoryUnsupportedVersion);
        reloaded.MatchRecovery.ShouldBe(recovery);
        blockedStart.Outcome.ShouldBe(MatchMutationOutcomeView.RecoveryRequired);
        blockedStart.Application.MatchRecovery.ShouldBe(recovery);
        blockedMatch.ShouldBeNull();
        retained.MatchRecovery.ShouldBe(recovery);
        retainedHistory.ShouldBe(history);
        retainedProfile.ShouldBe(profile);
        retainedBackup.ShouldBe(backup);
        discarded.Match.ShouldBeNull();
        discarded.MatchError.ShouldBeNull();
        discarded.MatchRecovery.ShouldBeNull();
        started.Outcome.ShouldBe(MatchMutationOutcomeView.Applied);
        (await fixture.Store.Read("match-history")).ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldNotBeNull();
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        failedDocuments.DeleteAttempts.ShouldBe(1);
        documents.UnconditionalDeletes.ShouldBeEmpty();
        documents.CheckedDeletes.ShouldBe(["match-history"]);
        history.Revision.ShouldBe(recovery.Revision);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RecoveryDeleteFaultBeforeCommit_PreservesEveryDocumentAndSurfacesTheFault(
        bool sqlite,
        bool cancellation
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(2, catalogue.Mechanics.ManifestVersion)
        );
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var source = (await fixture.Store.Read("match"))!;
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var recovery = Value(await Application(catalogue, fixture.Store).State()).MatchRecovery!;
        using var cancellationSource = new CancellationTokenSource();
        var fault = cancellation
            ? RecoveryDeleteFault.CancelBeforeCommit
            : RecoveryDeleteFault.FailBeforeCommit;
        var documents = new RecoveryDeleteFaultStore(
            fixture.Store,
            "match",
            fault,
            cancellationSource
        );
        var operation = Application(catalogue, documents)
            .AbandonSavedMatch(
                new(recovery.Revision, recovery.ContentIdentity),
                cancellationSource.Token
            );

        if (cancellation)
        {
            await Should.ThrowAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            await Should.ThrowAsync<DocumentStorageException>(() => operation);
        }

        (await fixture.Store.Read("match")).ShouldBe(source);
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        Value(await Application(catalogue, fixture.Store).State()).MatchRecovery.ShouldBe(recovery);
        documents.DeleteAttempts.ShouldBe(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RecoveryDeleteFaultAfterCommit_ReconcilesSuccessAndBuildsTheCompleteView(
        bool sqlite,
        bool cancellation
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(2, catalogue.Mechanics.ManifestVersion)
        );
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var recovery = Value(await Application(catalogue, fixture.Store).State()).MatchRecovery!;
        using var cancellationSource = new CancellationTokenSource();
        var fault = cancellation
            ? RecoveryDeleteFault.CancelAfterCommit
            : RecoveryDeleteFault.FailAfterCommit;
        var documents = new RecoveryDeleteFaultStore(
            fixture.Store,
            "match",
            fault,
            cancellationSource
        );

        var recovered = Value(
            await Application(catalogue, documents)
                .AbandonSavedMatch(
                    new(recovery.Revision, recovery.ContentIdentity),
                    cancellationSource.Token
                )
        );

        recovered.Match.ShouldBeNull();
        recovered.MatchError.ShouldBeNull();
        recovered.MatchRecovery.ShouldBeNull();
        (await fixture.Store.Read("match")).ShouldBeNull();
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        documents.DeleteAttempts.ShouldBe(1);
        cancellationSource.IsCancellationRequested.ShouldBe(cancellation);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecoveryDeleteFaultWithReplacement_ReturnsConflictWithoutTouchingAnyOtherKey(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        await fixture.Store.Create(
            "profile",
            CurrentProfileDocument(catalogue, "pre-d3-profile.json")
        );
        await fixture.Store.Create("match", MatchAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(2, catalogue.Mechanics.ManifestVersion)
        );
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var history = (await fixture.Store.Read("match-history"))!;
        var profile = (await fixture.Store.Read("profile"))!;
        var backup = (await fixture.Store.Read("match-migration-backup/sentinel"))!;
        var recovery = Value(await Application(catalogue, fixture.Store).State()).MatchRecovery!;
        var replacementJson = MatchAtVersion(2, "replacement-authority");
        var documents = new RecoveryDeleteFaultStore(
            fixture.Store,
            "match",
            RecoveryDeleteFault.ReplaceThenFail,
            replacementJson: replacementJson
        );

        var conflict = await Application(catalogue, documents)
            .AbandonSavedMatch(new(recovery.Revision, recovery.ContentIdentity));

        conflict.Succeeded.ShouldBeFalse();
        conflict.Error!.Code.ShouldBe("match.recovery_stale");
        (await fixture.Store.Read("match"))!.Json.ShouldBe(replacementJson);
        (await fixture.Store.Read("match-history")).ShouldBe(history);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
        (await fixture.Store.Read("match-migration-backup/sentinel")).ShouldBe(backup);
        documents.DeleteAttempts.ShouldBe(1);
    }

    private static async Task<StoredDocument> CreateCompatibleLegacyActiveMatch(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        LocalProfile profile
    )
    {
        var current = await CreateCurrentActiveMatch(catalogue, documents, profile);
        var legacyJson = DocumentAtVersion(current.Json, 2, CompatibleLegacyAuthority);
        await documents.Delete("match");
        (await documents.Create("match", legacyJson)).ShouldBeOfType<DocumentWriteResult.Written>();
        return (await documents.Read("match"))!;
    }

    private static async Task<StoredDocument> CreateCompatibleLegacyHistory(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        LocalProfile profile
    )
    {
        var completed = await CreateCurrentCompletedMatch(catalogue, documents, profile);
        var archived = JsonNode.Parse(completed.Json)!.AsObject();
        archived["authorityVersion"] = CompatibleLegacyAuthority;
        var history = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["authorityVersion"] = CompatibleLegacyAuthority,
            ["matches"] = new JsonArray(archived),
        };
        await documents.Delete("match");
        (
            await documents.Create("match-history", history.ToJsonString())
        ).ShouldBeOfType<DocumentWriteResult.Written>();
        return (await documents.Read("match-history"))!;
    }

    private static async Task<StoredDocument> CreateCurrentCompletedMatch(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        LocalProfile profile
    )
    {
        var service = new LocalMatchService(catalogue, documents);
        var current = await CreateCurrentActiveMatch(catalogue, documents, profile, service);
        var started = await service.State(profile, profile.DisplayName.Value);
        started.Error.ShouldBeNull();
        var match = started.View!;
        var resign = match.LegalActions.Single(static action =>
            action.Kind == MatchActionKindView.Resign
        );
        var completed = await service.Apply(
            profile,
            profile.DisplayName.Value,
            match.Frame.Id,
            new(
                Guid.Parse("30000000-0000-0000-0000-000000000102"),
                match.Frame.Revision,
                resign.Id,
                []
            )
        );

        completed.Error.ShouldBeNull();
        completed.View!.Frame.IsComplete.ShouldBeTrue();
        var stored = (await documents.Read("match"))!;
        stored.Revision.ShouldBe(current.Revision + 1);
        return stored;
    }

    private static Task<StoredDocument> CreateCurrentActiveMatch(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        LocalProfile profile
    ) =>
        CreateCurrentActiveMatch(
            catalogue,
            documents,
            profile,
            new LocalMatchService(catalogue, documents)
        );

    private static async Task<StoredDocument> CreateCurrentActiveMatch(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents,
        LocalProfile profile,
        LocalMatchService service
    )
    {
        var started = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000101"), _firstDeck)
        );
        started.Error.ShouldBeNull();
        started.View.ShouldNotBeNull();
        return (await documents.Read("match"))!;
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

    private static string MatchAtVersion(int schema, string authority) =>
        DocumentAtVersion(
            Fixture(schema == 1 ? "schema-one-active-match.json" : "schema-two-active-match.json"),
            schema,
            authority
        );

    private static string HistoryAtVersion(int schema, string authority) =>
        HistoryFixtureAtVersion(
            schema == 1 ? "schema-one-match-history.json" : "schema-two-match-history.json",
            schema,
            authority
        );

    private static string HistoryFixtureAtVersion(string fixture, int schema, string authority)
    {
        var document = JsonNode.Parse(Fixture(fixture))!.AsObject();
        document["schemaVersion"] = schema;
        document["authorityVersion"] = authority;
        foreach (var archived in document["matches"]!.AsArray())
        {
            archived!["schemaVersion"] = schema;
            archived["authorityVersion"] = authority;
        }
        return document.ToJsonString();
    }

    private static string DocumentAtVersion(string json, int schema, string authority)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        document["schemaVersion"] = schema;
        document["authorityVersion"] = authority;
        return document.ToJsonString();
    }

    private static string MigrationIdentity(
        string key,
        int sourceSchema,
        string sourceAuthority,
        string targetAuthority
    )
    {
        var transitions = new List<string>();
        if (sourceSchema == 1)
        {
            transitions.Add($"{key}-schema-1-{sourceAuthority}-to-2-{sourceAuthority}");
        }
        if (!StringComparer.Ordinal.Equals(sourceAuthority, targetAuthority))
        {
            transitions.Add($"{key}-authority-2-{sourceAuthority}-to-2-{targetAuthority}");
        }
        return string.Join('+', transitions);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "match-migrations", name));

    private static string CurrentProfileDocument(BlokemonCatalogue catalogue, string fixture)
    {
        var document = JsonNode.Parse(Fixture(fixture))!.AsObject();
        document["profile"]!["authorityManifestVersion"] = catalogue.Mechanics.ManifestVersion;
        MakeCurrentDeckLegal(document);
        return document.ToJsonString();
    }

    private static LocalApplicationService Application(
        BlokemonCatalogue catalogue,
        IStateDocumentStore documents
    ) =>
        new(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        );

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    private static LocalProfile Profile(
        BlokemonCatalogue catalogue,
        string fixture = "historical-profile.json"
    )
    {
        var document = JsonNode.Parse(Fixture(fixture))!.AsObject();
        document["profile"]!["authorityManifestVersion"] = catalogue.Mechanics.ManifestVersion;
        MakeCurrentDeckLegal(document);
        var product = JsonSerializer.Deserialize<ProductDocument>(
            document.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        return ProductValue(LocalProfile.Restore(product.Profile, catalogue.Mechanics));
    }

    private static void MakeCurrentDeckLegal(JsonObject document)
    {
        foreach (var deck in document["profile"]!["savedDecks"]!.AsArray())
        {
            var cards = deck!["cards"]!.AsArray();
            var doubleColorless = cards
                .Select(static card => card!.AsObject())
                .SingleOrDefault(static card => card["cardId"]!.GetValue<string>() == "VIM-DODGY");
            if (doubleColorless is null)
            {
                continue;
            }

            var quantity = doubleColorless["quantity"]!.GetValue<int>();
            if (quantity <= 4)
            {
                continue;
            }

            doubleColorless["quantity"] = 4;
            cards.Add(new JsonObject { ["cardId"] = "VIM-BLAZED", ["quantity"] = quantity - 4 });
        }
    }

    private static TValue ProductValue<TValue, TFailure>(DomainResult<TValue, TFailure> result)
        where TValue : notnull
        where TFailure : notnull =>
        result is DomainResult<TValue, TFailure>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("The historical profile is not valid now.");

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

    private sealed class RecordingDeleteStore(IStateDocumentStore inner)
        : DelegatingDocumentStore(inner)
    {
        public List<string> UnconditionalDeletes { get; } = [];

        public List<string> CheckedDeletes { get; } = [];

        public override async Task Delete(string key, CancellationToken cancellationToken = default)
        {
            UnconditionalDeletes.Add(key);
            await base.Delete(key, cancellationToken);
        }

        public override async Task<DocumentDeleteResult> DeleteIfUnchanged(
            string key,
            long expectedRevision,
            string expectedJson,
            CancellationToken cancellationToken = default
        )
        {
            CheckedDeletes.Add(key);
            return await base.DeleteIfUnchanged(
                key,
                expectedRevision,
                expectedJson,
                cancellationToken
            );
        }
    }

    private enum RecoveryDeleteFault
    {
        CancelBeforeCommit,
        FailBeforeCommit,
        CancelAfterCommit,
        FailAfterCommit,
        ReplaceThenFail,
    }

    private sealed class RecoveryDeleteFaultStore(
        IStateDocumentStore inner,
        string targetKey,
        RecoveryDeleteFault fault,
        CancellationTokenSource? cancellation = null,
        string? replacementJson = null
    ) : DelegatingDocumentStore(inner)
    {
        private int _deleteAttempts;

        public int DeleteAttempts => Volatile.Read(ref _deleteAttempts);

        public override async Task<DocumentDeleteResult> DeleteIfUnchanged(
            string key,
            long expectedRevision,
            string expectedJson,
            CancellationToken cancellationToken = default
        )
        {
            if (key != targetKey)
            {
                return await base.DeleteIfUnchanged(
                    key,
                    expectedRevision,
                    expectedJson,
                    cancellationToken
                );
            }

            Interlocked.Increment(ref _deleteAttempts);

            if (fault == RecoveryDeleteFault.CancelBeforeCommit)
            {
                cancellation!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (fault == RecoveryDeleteFault.FailBeforeCommit)
            {
                throw Failure();
            }

            var result = await base.DeleteIfUnchanged(
                key,
                expectedRevision,
                expectedJson,
                cancellationToken
            );

            if (fault == RecoveryDeleteFault.ReplaceThenFail)
            {
                (
                    await base.Create(key, replacementJson!, CancellationToken.None)
                ).ShouldBeOfType<DocumentWriteResult.Written>();
                throw Failure();
            }
            if (fault == RecoveryDeleteFault.CancelAfterCommit)
            {
                cancellation!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (fault == RecoveryDeleteFault.FailAfterCommit)
            {
                throw Failure();
            }

            return result;
        }

        private static DocumentStorageException Failure() =>
            new(DocumentStorageFailure.Unavailable, "Simulated recovery delete failure.");
    }

    private sealed class DivergentMatchUpdateStore(IStateDocumentStore inner, string externalJson)
        : DelegatingDocumentStore(inner)
    {
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

            var external = await base.Update(
                key,
                expectedRevision,
                externalJson,
                cancellationToken
            );
            external.ShouldBeOfType<DocumentWriteResult.Written>();
            return await base.Update(key, expectedRevision, json, cancellationToken);
        }
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
