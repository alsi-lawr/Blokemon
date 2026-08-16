using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.Web.Application;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        mode.BrowserStorageError
            .ShouldNotBeNull()
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
        JsonSerializer.Serialize(restored.Match)
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
            ["schemaVersion"] = 1,
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

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content"));

    private static PlayModeApplication Application(
        BlokemonCatalogue catalogue,
        MemoryDocumentStore documents,
        ServerHandler handler,
        bool serverBackedAvailable = true
    )
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.invalid/") };
        var local = new LocalApplicationService(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents)
        );
        return new(
            new BlokemonApiClient(http),
            local,
            documents,
            new PlayModeAvailability(serverBackedAvailable)
        );
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
