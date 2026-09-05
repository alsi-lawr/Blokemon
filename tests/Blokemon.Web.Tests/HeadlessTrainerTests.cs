using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless checks of Trainers in packs and in the collection and deck builder (BLOKEMON-156,
/// BLOKEMON-157): Blokemon.Web on Kestrel with no provider, the browser game driven by
/// headless_trainer_evidence.py through headless Chrome.
/// Skipped where Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessTrainerTests/*"</c>.
/// </summary>
public sealed class HeadlessTrainerTests
{
    [Test]
    [Timeout(600_000)]
    public async Task OpenedPacks_ShowTheirTrainersThroughTheCardFace_AndOwnershipGovernsThem(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        await using var host = SessionHost.Create(withProvider: false, kestrel: true);
        host.Factory.StartServer();
        var origin = Origin(host);

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_trainer_evidence.py"
        );
        var start = new ProcessStartInfo(python, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        start.Environment["BLOKEMON_ORIGIN"] = origin;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var report = $"{await output}\n{await errors}";
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS TRAINER EVIDENCE COMPLETE");
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
}
