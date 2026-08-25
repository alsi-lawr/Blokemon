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
    [Arguments(false, 1, "sv151-candidate.12")]
    [Arguments(true, 1, "sv151-candidate.12")]
    [Arguments(false, 1, "sv151-candidate.14")]
    [Arguments(true, 1, "sv151-candidate.14")]
    [Arguments(false, 2, "sv151-candidate.14")]
    [Arguments(true, 2, "sv151-candidate.14")]
    [Arguments(false, 2, "sv151-candidate.15")]
    [Arguments(true, 2, "sv151-candidate.15")]
    [Arguments(false, 2, "sv151-candidate.16")]
    [Arguments(true, 2, "sv151-candidate.16")]
    public async Task SupportedActiveMatchVersion_MigratesThroughBackupAndColdReplayForEachProvider(
        bool sqlite,
        int sourceSchema,
        string sourceAuthority
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var sourceJson = MatchAtVersion(sourceSchema, sourceAuthority);
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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity(
                "match",
                sourceSchema,
                sourceAuthority,
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
    [Arguments(false, 1, "sv151-candidate.12")]
    [Arguments(true, 1, "sv151-candidate.12")]
    [Arguments(false, 1, "sv151-candidate.14")]
    [Arguments(true, 1, "sv151-candidate.14")]
    [Arguments(false, 2, "sv151-candidate.14")]
    [Arguments(true, 2, "sv151-candidate.14")]
    [Arguments(false, 2, "sv151-candidate.15")]
    [Arguments(true, 2, "sv151-candidate.15")]
    [Arguments(false, 2, "sv151-candidate.16")]
    [Arguments(true, 2, "sv151-candidate.16")]
    public async Task SupportedHistoryVersion_MigratesAllMatchesBeforeReplacementForEachProvider(
        bool sqlite,
        int sourceSchema,
        string sourceAuthority
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
        var catalogue = Catalogue();
        var profile = Profile(catalogue, "history-profile.json");
        var activeJson = Fixture("schema-one-completed-match.json");
        var historyJson = HistoryAtVersion(sourceSchema, sourceAuthority);
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
            MigrationIdentity(
                "match-history",
                sourceSchema,
                sourceAuthority,
                catalogue.Mechanics.ManifestVersion
            )
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IncompatiblePreD3History_LeavesTheWholeHistoryAndBackupSpaceUntouched(
        bool sqlite
    )
    {
        await using var fixture = await DocumentStoreFixture.Create(sqlite);
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

        completed.Error!.Code.ShouldBe("match.history_authority_changed");
        completed.View!.Frame.IsComplete.ShouldBeTrue();
        rejected.Error!.Code.ShouldBe("match.history_authority_changed");
        (await fixture.Store.Read("match-history")).ShouldBe(historySource);
        (await fixture.Store.Read(BackupKey("match-history", historySource))).ShouldBeNull();
        (await fixture.Store.Read("match"))!.Json.ShouldContain(
            "30000000-0000-0000-0000-000000000001",
            Case.Sensitive
        );
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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity("match", 1, "sv151-candidate.14", catalogue.Mechanics.ManifestVersion)
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
        AssertBackup(
            backup,
            "match",
            source,
            MigrationIdentity("match", 1, "sv151-candidate.14", catalogue.Mechanics.ManifestVersion)
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
        await fixture.Store.Create("match", Fixture("schema-one-completed-match.json"));
        await fixture.Store.Create(
            "match-history",
            HistoryAtVersion(sourceSchema, sourceAuthority)
        );
        var historySource = (await fixture.Store.Read("match-history"))!;
        var service = new LocalMatchService(catalogue, fixture.Store);
        var completed = await service.State(profile, profile.DisplayName.Value);

        var rejected = await service.Start(
            profile,
            profile.DisplayName.Value,
            new(Guid.Parse("30000000-0000-0000-0000-000000000005"), _firstDeck)
        );

        completed.Error!.Code.ShouldBe("match.history_version");
        completed.View!.Frame.IsComplete.ShouldBeTrue();
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
        await inner.Create("match", Fixture("schema-one-active-match.json"));
        var source = (await inner.Read("match"))!;
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
            MigrationIdentity("match", 1, "sv151-candidate.14", catalogue.Mechanics.ManifestVersion)
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
        await fixture.Store.Create("match", Fixture("schema-one-active-match.json"));
        var source = (await fixture.Store.Read("match"))!;
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
        await fixture.Store.Create("match-history", "history-sentinel");
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
        await fixture.Store.Create("match-history", "history-sentinel");
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
        await fixture.Store.Create("match", Fixture("schema-one-completed-match.json"));
        await fixture.Store.Create("match-history", HistoryAtVersion(2, "arbitrary-authority"));
        await fixture.Store.Create("match-migration-backup/sentinel", "backup-sentinel");
        var documents = new RecordingDeleteStore(fixture.Store);
        var application = Application(catalogue, documents);
        var gated = Value(await application.State());
        var recovery = gated.MatchRecovery!;
        var active = (await fixture.Store.Read("match"))!;
        var activeBackup = (
            await fixture.Store.Read(
                BackupKey("match", new(1, Fixture("schema-one-completed-match.json")))
            )
        )!;
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
        (
            await fixture.Store.Read(
                BackupKey("match", new(1, Fixture("schema-one-completed-match.json")))
            )
        ).ShouldBe(activeBackup);
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

    private static string HistoryAtVersion(int schema, string authority)
    {
        var document = JsonNode
            .Parse(
                Fixture(
                    schema == 1 ? "schema-one-match-history.json" : "schema-two-match-history.json"
                )
            )!
            .AsObject();
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
