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
/// The headless checks of BLOKEMON-152's pages: the operator page (listings, admission with
/// the token shown once, rotation, closure, re-admission, revocation, disable and enable, the
/// operator grant, the default owner, erase on behalf, the diagnostics), the tenant-owner page
/// (exclude and readmit) and the profile's erase-account section, at desktop and on a phone,
/// with the menu items that reach them. Blokemon.Web runs on Kestrel with the BlokeBot and
/// first-party providers; headless_lifecycle_evidence.py drives headless Chrome. Skipped where
/// Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessLifecycleTests/*"</c>.
/// </summary>
public sealed class HeadlessLifecycleTests
{
    [Test]
    [Timeout(900_000)]
    public async Task OperatorsOwnersAndPlayers_UseTheirPages(CancellationToken cancellationToken)
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

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
        var operatorSession = await host.SessionOf(operatorToken);
        // A channel with its broadcaster signed in (the owner) and a viewer (the player).
        var (alpha, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha", "1001");
        var owner = await host.Exchange(await alpha.HandoffCode("1001", "Alpha Owner"), "alpha");
        var player = await host.Exchange(await alpha.HandoffCode("3003", "Viewer Three"), "alpha");
        var playerSession = await host.SessionOf(player.Value!.Token);
        // A second viewer takes the disabling, exclusion and the erase on the player's behalf.
        var other = await host.Exchange(await alpha.HandoffCode("3004", "Viewer Four"), "alpha");
        var otherSession = await host.SessionOf(other.Value!.Token);
        // Each headless visitor holds a first-party session of their own account.
        var operatorFirstParty = await host.IssueDirectly(
            operatorSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );
        var playerFirstParty = await host.IssueDirectly(
            playerSession.Account,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)
        );

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_lifecycle_evidence.py"
        );
        var start = new ProcessStartInfo(python, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        start.Environment["BLOKEMON_ORIGIN"] = origin;
        start.Environment["BLOKEMON_OPERATOR_TOKEN"] = operatorFirstParty.Token;
        start.Environment["BLOKEMON_OWNER_TOKEN"] = owner.Value!.Token;
        start.Environment["BLOKEMON_PLAYER_TOKEN"] = playerFirstParty.Token;
        start.Environment["BLOKEMON_PLAYER_CHANNEL_TOKEN"] = player.Value.Token;
        start.Environment["BLOKEMON_OPERATOR_ACCOUNT"] = operatorSession.Account.Value;
        start.Environment["BLOKEMON_PLAYER_ACCOUNT"] = playerSession.Account.Value;
        start.Environment["BLOKEMON_OTHER_ACCOUNT"] = otherSession.Account.Value;
        start.Environment["BLOKEMON_EXPIRES_AT"] = operatorFirstParty.Session.ExpiresAt.ToString(
            "O"
        );
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
            var secret in new[]
            {
                operatorFirstParty.Token,
                owner.Value.Token,
                playerFirstParty.Token,
                player.Value.Token,
                other.Value.Token,
                alpha.Token!,
            }
        )
        {
            report.ShouldNotContain(secret);
        }
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS LIFECYCLE EVIDENCE COMPLETE");
        // The player erased their own account and the operator erased the other viewer's on
        // their behalf: the tombstones are all that is left of either.
        var keys = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ToList();
        keys.Where(key => key.Contains(playerSession.Account.Value))
            .ShouldBe([$"account/{playerSession.Account}"]);
        keys.Where(key => key.Contains(otherSession.Account.Value))
            .ShouldBe([$"account/{otherSession.Account}"]);
        keys.ShouldNotContain("link/twitch/3003");
        keys.ShouldNotContain("link/twitch/3004");
        alpha.Dispose();
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
