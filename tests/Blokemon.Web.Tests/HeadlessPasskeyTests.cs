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
/// The headless first-party sign-in checks: Blokemon.Web on Kestrel with the first-party
/// provider enabled for the relying party <c>localhost</c>, and headless Chrome with a virtual
/// authenticator (the DevTools WebAuthn domain) driven by headless_passkey_evidence.py through
/// the client's own pages, with a second Chrome as the other device the simple login reaches. Skipped where Chrome or Python is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessPasskeyTests/*"</c>.
/// </summary>
public sealed class HeadlessPasskeyTests
{
    [Test]
    [Timeout(600_000)]
    public async Task Client_CreatesSignsInAddsRegeneratesRecoversWithPasskeysAndSignsInElsewhereWithAPassword(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        // The relying party origin is configuration, so the port is chosen before the host
        // starts rather than read back from it.
        var port = FreePort();
        var origin = $"http://localhost:{port}";
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
            },
            kestrel: true,
            kestrelPort: port
        );
        host.Factory.StartServer();

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_passkey_evidence.py"
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
        report.ShouldContain("HEADLESS PASSKEY EVIDENCE COMPLETE");
        // Two accounts (the passkey one, the password one), three passkeys on the first (the
        // first, the added one, the recovery replacement), a login on each and one live code set
        // each are what the browsers left behind.
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(2);
        (await host.WithStore(store => store.List("credential/"))).Count.ShouldBe(3);
        (await host.WithStore(store => store.List("login/"))).Count.ShouldBe(2);
        (await host.WithStore(store => store.List("loginname/"))).Count.ShouldBe(2);
        (await host.WithStore(store => store.List("recovery/"))).Count.ShouldBe(2);
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
