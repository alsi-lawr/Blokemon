using System.Diagnostics;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Content;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless check of the computer's own runtime. A battle is saved here with the computer to
/// act and its next move decided in this process; the page's worker, booted from the site's own
/// framework files by the site's own module, must decide the same move by the same rules while
/// the page's thread runs no long task. Blokemon.Web on Kestrel with no provider, driven by
/// headless_computer_evidence.py through headless Chrome. Skipped where Chrome or Python is not
/// installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessComputerTests/*"</c>.
/// </summary>
public sealed class HeadlessComputerTests
{
    [Test]
    [Timeout(600_000)]
    public async Task TheWorkerDecidesWhatThePolicyDecides_WhileThePageStaysResponsive(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var documents = new MemoryDocumentStore();
        var (json, expected) = await SavedBattleWithTheComputerToAct(catalogue, documents);

        await using var host = SessionHost.Create(withProvider: false, kestrel: true);
        host.Factory.StartServer();
        var origin = Origin(host);
        var battle = Path.Combine(Path.GetTempPath(), $"blokemon-computer-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(battle, json, cancellationToken);

        try
        {
            var script = Path.Combine(
                RepositoryRoot(),
                "tests",
                "Blokemon.Web.Tests",
                "headless_computer_evidence.py"
            );
            var start = new ProcessStartInfo(python, script)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepositoryRoot(),
            };
            start.Environment["BLOKEMON_ORIGIN"] = origin;
            start.Environment["BLOKEMON_BATTLE"] = battle;
            start.Environment["BLOKEMON_EXPECTED"] = expected;
            start.Environment["BLOKEMON_AUTHORITY"] = catalogue.Mechanics.ManifestVersion;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var report = $"{await output}\n{await errors}";
            process.ExitCode.ShouldBe(0, report);
            report.ShouldContain("HEADLESS COMPUTER EVIDENCE COMPLETE");
        }
        finally
        {
            File.Delete(battle);
        }
    }

    // A battle at Hard, played by the browser game's own application service until the computer
    // has the turn, and the candidate the policy chooses for it in this process: what the page's
    // worker is handed, and what it must answer.
    private static async Task<(string Json, string Expected)> SavedBattleWithTheComputerToAct(
        BlokemonCatalogue catalogue,
        MemoryDocumentStore documents
    )
    {
        var application = new LocalApplicationService(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.MigrateCompatible
        );
        Value(await application.CreateProfile(new(Guid.NewGuid(), "Worker Player")));
        var claimed = Value(await application.ClaimStarterDeck(new(Guid.NewGuid(), "growroom")));
        var match = Value(
            await application.StartMatch(
                new(Guid.NewGuid(), claimed.Decks.Single().Id, CpuDifficultyView.Hard)
            )
        ).Application.Match!;

        for (var moves = 0; moves < 32; moves++)
        {
            if (match.Frame.IsComplete)
            {
                throw new InvalidOperationException("The battle ended before the computer's turn.");
            }

            if (match.Frame.Opponent.HasTurn)
            {
                var stored = (await documents.Read("match"))!;
                var expected = new ComputerTurn(catalogue).Decide(stored.Json);
                expected.ShouldNotBeNull();
                return (stored.Json, expected);
            }

            // The turn is given up as soon as it can be, and until then a move is made that
            // asks nothing: the deal decides which moves are offered, and one that needs a
            // choice answered would be refused with the empty answer given here.
            var action =
                match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.EndTurn && action.DisabledReason is null
                )
                ?? match.LegalActions.First(static action =>
                    action.Kind != MatchActionKindView.Resign
                    && action.DisabledReason is null
                    && action.ChoiceRequirements.All(static requirement => requirement.Minimum == 0)
                );
            match = Value(
                await application.ApplyMatchAction(
                    match.Frame.Id,
                    new(Guid.NewGuid(), match.Frame.Revision, action.Id, [])
                )
            ).Application.Match!;
        }

        throw new InvalidOperationException("The computer never had the turn.");
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

    private static string Origin(SessionHost host)
    {
        using var client = host.Factory.CreateClient();
        return client.BaseAddress!.ToString().TrimEnd('/');
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string? Find(params string[] candidates)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
            Path.PathSeparator
        );
        foreach (var candidate in candidates)
        {
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private sealed class MemoryDocumentStore : IStateDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(
            StringComparer.Ordinal
        );

        public Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_documents.GetValueOrDefault(key));

        public Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (_documents.ContainsKey(key))
            {
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
            }

            _documents[key] = new(1, json);
            return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(1));
        }

        public Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (
                _documents.GetValueOrDefault(key) is not { } current
                || current.Revision != expectedRevision
            )
            {
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
            }

            _documents[key] = new(current.Revision + 1, json);
            return Task.FromResult<DocumentWriteResult>(
                new DocumentWriteResult.Written(current.Revision + 1)
            );
        }

        public Task Delete(string key, CancellationToken cancellationToken = default)
        {
            _documents.Remove(key);
            return Task.CompletedTask;
        }

        public Task<DocumentDeleteResult> DeleteIfUnchanged(
            string key,
            long expectedRevision,
            string expectedJson,
            CancellationToken cancellationToken = default
        )
        {
            if (_documents.GetValueOrDefault(key) is not { } current)
            {
                return Task.FromResult<DocumentDeleteResult>(new DocumentDeleteResult.Missing());
            }

            if (current.Revision != expectedRevision || current.Json != expectedJson)
            {
                return Task.FromResult<DocumentDeleteResult>(new DocumentDeleteResult.Conflict());
            }

            _documents.Remove(key);
            return Task.FromResult<DocumentDeleteResult>(new DocumentDeleteResult.Deleted());
        }
    }
}
