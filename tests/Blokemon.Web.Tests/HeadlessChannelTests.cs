using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Blokemon.App;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless hosted-channel checks: Blokemon.Web on Kestrel with the BlokeBot provider, the
/// passkey relying party and two admitted channels whose registered parent is a local page,
/// driven by headless_channel_evidence.py: the passkey offer in a frame with the delegated
/// permission and through the continuation window without it, the approval prompt, and the
/// pending-approvals panel. Skipped where Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessChannelTests/*"</c>.
/// </summary>
public sealed class HeadlessChannelTests
{
    [Test]
    [Timeout(900_000)]
    public async Task HostedViewers_GetThePasskeyOfferTheApprovalPromptAndThePendingList(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        var port = FreePort();
        var origin = $"http://localhost:{port}";
        var parentPort = FreePort();
        var parentOrigin = $"http://localhost:{parentPort}";
        await using var host = ChannelHosting.Create(
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
            },
            kestrel: true,
            kestrelPort: port
        );
        host.Factory.StartServer();
        var operatorToken = await host.OperatorToken();
        var (alpha, _) = await host.AdmitChannel(
            operatorToken,
            "alpha",
            "Alpha",
            "1001",
            parentOrigin
        );
        var (beta, _) = await host.AdmitChannel(
            operatorToken,
            "beta",
            "Beta",
            "1002",
            parentOrigin
        );

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_channel_evidence.py"
        );
        var start = new ProcessStartInfo(python, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        start.Environment["BLOKEMON_ORIGIN"] = origin;
        start.Environment["BLOKEMON_PARENT_PORT"] = parentPort.ToString();
        start.Environment["BLOKEMON_ALPHA_TOKEN"] = alpha.Token;
        start.Environment["BLOKEMON_BETA_TOKEN"] = beta.Token;
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
        report.ShouldNotContain(alpha.Token!);
        report.ShouldNotContain(beta.Token!);
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS CHANNEL EVIDENCE COMPLETE");
        // Three viewers, two of whom enrolled a passkey; the channels that created them,
        // beta approved for the first, and alpha pending for the third.
        (await host.WithStore(store => store.List("link/twitch/"))).Count.ShouldBe(3);
        (await host.WithStore(store => store.List("credential/"))).Count.ShouldBe(2);
        (await host.WithStore(store => store.List("approval/"))).Count.ShouldBe(5);
        alpha.Dispose();
        beta.Dispose();
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
