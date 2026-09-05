using System.Diagnostics;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless checks of the match table on a phone: the table fits the screen it has rather
/// than scrolling, and a card held in full screen raises its viewer over the table. Blokemon.Web on
/// Kestrel with no provider, the browser game driven by headless_table_evidence.py through
/// headless Chrome. Skipped where Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessTableTests/*"</c>.
/// </summary>
public sealed class HeadlessTableTests
{
    [Test]
    [Timeout(600_000)]
    public async Task PhoneTable_FitsItsScreen_AndAHeldCardShowsInFullScreen(
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
            "headless_table_evidence.py"
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
        report.ShouldContain("HEADLESS TABLE EVIDENCE COMPLETE");
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
