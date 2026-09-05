using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Blokemon.App;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless Google sign-in check (BLOKEMON-164): Blokemon.Web on Kestrel with the Google
/// provider enabled against the stub of Google's endpoints on the same host, and headless
/// Chrome driven by headless_google_evidence.py through the client's own pages: the link on
/// the sign-in page, the round trip out and back, the continuation, and the signed-in game.
/// Skipped where Chrome or Python is not installed.
/// </summary>
public sealed class HeadlessGoogleTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Client_SignsInThroughGoogleAndBackTwice(CancellationToken cancellationToken)
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        var port = FreePort();
        var origin = $"http://localhost:{port}";
        var stub = new StubGoogle();
        await using var host = SessionHost.Create(
            builder =>
            {
                builder.UseSetting(
                    IdentityConfigurationModule.providerEnabledKey("FirstParty"),
                    "true"
                );
                builder.UseSetting(
                    IdentityConfigurationModule.PasskeysRelyingPartyIdKey,
                    "localhost"
                );
                builder.UseSetting($"{IdentityConfigurationModule.PasskeysOriginsKey}:0", origin);
                stub.Configure(builder, origin);
            },
            kestrel: true,
            kestrelPort: port
        );
        host.Factory.StartServer();

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_google_evidence.py"
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
        if (
            Environment.GetEnvironmentVariable("BLOKEMON_HEADLESS_REPORT") is
            { Length: > 0 } reportPath
        )
        {
            File.WriteAllText(reportPath, report);
        }
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS GOOGLE EVIDENCE COMPLETE");
        // One account for the one Google subject, two rounds through the stub, and a session
        // for the sign-in the browser still holds.
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(1);
        (await host.WithStore(store => store.List("link/google/"))).Count.ShouldBe(1);
        stub.TokenRequests.Count.ShouldBe(2);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

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
