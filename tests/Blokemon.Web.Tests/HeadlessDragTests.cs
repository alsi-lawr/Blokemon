using System.Diagnostics;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless checks that a card is played by dragging it to where it goes (BLOKEMON-173): on
/// a Pixel a finger carries the opening Blokemon into the Active position and a held card onto
/// one of the player's Blokemon, and with a mouse a Blokemon goes to the Bench, a hold that
/// becomes a drag closes its viewer and completes, a drop over nothing springs back, and Escape
/// and a tap on the field put a picked-up card down. Blokemon.Web on Kestrel with no provider,
/// the browser game driven by headless_drag_evidence.py through headless Chrome. Skipped where
/// Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessDragTests/*"</c>.
/// </summary>
public sealed class HeadlessDragTests
{
    [Test]
    [Timeout(600_000)]
    public async Task CardsArePlayedByDraggingThemToTheirTargets_OnAPixelAndWithAMouse(
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
            "headless_drag_evidence.py"
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
        report.ShouldContain("HEADLESS DRAG EVIDENCE COMPLETE");
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
