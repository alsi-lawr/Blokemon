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
/// BLOKEMON-154's milestone browser journey: Blokemon.Web on Kestrel with the BlokeBot and
/// first-party providers, an operator, the default tenant (core) and two channels admitted with
/// a local parent page as their registered origin, plus a second short-session-lifetime host for
/// the re-authentication step, driven by headless_milestone_evidence.py. It covers both sign-in
/// paths at desktop and narrow viewports, and the channel path's continuation, blokemon.reauth,
/// closure and core-adoption steps, capturing a screenshot of each into the directory named by
/// BLOKEMON_MILESTONE_SHOTS (the Casefile evidence directory when set; a temp directory otherwise).
/// The card-evidence harness is not touched. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/MilestoneBrowserJourneyTests/*"</c>.
/// </summary>
public sealed class MilestoneBrowserJourneyTests
{
    [Test]
    [Timeout(900_000)]
    public async Task BothSignInPaths_ContinuationReauthClosureAndAdoption_AtBothViewports(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the milestone journey.");
        }

        var parentPort = FreePort();
        var parentOrigin = $"http://localhost:{parentPort}";

        var port = FreePort();
        var origin = $"http://localhost:{port}";
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
        var (bravo, _) = await host.AdmitChannel(
            operatorToken,
            "bravo",
            "Bravo",
            "1002",
            parentOrigin
        );
        var (gamma, _) = await host.AdmitChannel(
            operatorToken,
            "gamma",
            "Gamma",
            "1003",
            parentOrigin
        );
        var core = await host.Admit(operatorToken, "core", "Core", null, parentOrigin);
        core.Succeeded.ShouldBeTrue(core.Error?.Message);

        var reauthPort = FreePort();
        var reauthOrigin = $"http://localhost:{reauthPort}";
        await using var reauthHost = ChannelHosting.Create(
            builder =>
                builder.UseSetting(IdentityConfigurationModule.SessionLifetimeKey, "00:00:05"),
            kestrel: true,
            kestrelPort: reauthPort
        );
        reauthHost.Factory.StartServer();
        var reauthOperator = await reauthHost.OperatorToken();
        var (reauthChannel, _) = await reauthHost.AdmitChannel(
            reauthOperator,
            "alpha",
            "Alpha",
            "1001",
            parentOrigin
        );

        var shots = Environment.GetEnvironmentVariable("BLOKEMON_MILESTONE_SHOTS")
            is { Length: > 0 } named
            ? named
            : Path.Combine(Path.GetTempPath(), $"blokemon-milestone-shots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(shots);

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_milestone_evidence.py"
        );
        var start = new ProcessStartInfo(python, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        start.Environment["BLOKEMON_ORIGIN"] = origin;
        start.Environment["BLOKEMON_REAUTH_ORIGIN"] = reauthOrigin;
        start.Environment["BLOKEMON_PARENT_PORT"] = parentPort.ToString();
        start.Environment["BLOKEMON_ALPHA_TOKEN"] = alpha.Token!;
        start.Environment["BLOKEMON_BRAVO_TOKEN"] = bravo.Token!;
        start.Environment["BLOKEMON_GAMMA_TOKEN"] = gamma.Token!;
        start.Environment["BLOKEMON_CORE_TOKEN"] = core.Value!.Token;
        start.Environment["BLOKEMON_REAUTH_TOKEN"] = reauthChannel.Token!;
        start.Environment["BLOKEMON_SCREENSHOTS"] = shots;
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
        foreach (
            var token in new[]
            {
                alpha.Token!,
                bravo.Token!,
                gamma.Token!,
                core.Value.Token,
                reauthChannel.Token!,
            }
        )
        {
            report.ShouldNotContain(token);
        }
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS MILESTONE EVIDENCE COMPLETE");

        foreach (var viewport in new[] { "1440", "412" })
        {
            foreach (
                var step in new[]
                {
                    "passkey-signin",
                    "continuation",
                    "reauth",
                    "closure",
                    "adoption",
                }
            )
            {
                File.Exists(Path.Combine(shots, $"{step}-{viewport}.png"))
                    .ShouldBeTrue($"{step}-{viewport}.png");
            }
        }

        alpha.Dispose();
        bravo.Dispose();
        gamma.Dispose();
        reauthChannel.Dispose();
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
