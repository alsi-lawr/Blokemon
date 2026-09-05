using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core.Exceptions;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// The headless client checks: Blokemon.Web on Kestrel with the test-only provider double, a
/// second host with no provider and no core sign-in URL, and headless Chrome driven by
/// headless_session_evidence.py through the DevTools protocol. Skipped where Chrome or Python
/// is not installed. Run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/HeadlessSessionTests/*"</c>.
/// </summary>
public sealed class HeadlessSessionTests
{
    [Test]
    [Timeout(600_000)]
    public async Task Client_HoldsSendsAndDropsItsSessionReadsFragmentsAndListensOnlyToItsParent(
        CancellationToken cancellationToken
    )
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless checks.");
        }

        var parentPort = FreePort();
        var parentOrigin = $"http://localhost:{parentPort}";
        await using var host = SessionHost.Create(
            builder =>
            {
                builder.UseSetting(
                    "Blokemon:Identity:Providers:BlokeBot:CoreSignInUrl",
                    $"{parentOrigin}/core-signin.html"
                );
            },
            kestrel: true
        );
        await using var plain = SessionHost.Create(withProvider: false, kestrel: true);
        host.Factory.StartServer();
        plain.Factory.StartServer();
        var origin = Origin(host);
        var plainOrigin = Origin(plain);

        // The parent page is the tenant's registered parent, as admission will record it.
        await host.WithStore(async store =>
        {
            var key = tenantKey(await host.DefaultTenantId());
            var stored = (await store.Read(key))!;
            var document = JsonNode.Parse(stored.Json)!.AsObject();
            document["registeredParentOrigin"] = parentOrigin;
            await store.Update(key, stored.Revision, document.ToJsonString());
        });
        var issued = await host.SignIn("headless", "Headless Player");

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_session_evidence.py"
        );
        var start = new ProcessStartInfo(python, script)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        start.Environment["BLOKEMON_ORIGIN"] = origin;
        start.Environment["BLOKEMON_PLAIN_ORIGIN"] = plainOrigin;
        start.Environment["BLOKEMON_PARENT_PORT"] = parentPort.ToString();
        start.Environment["BLOKEMON_SESSION_TOKEN"] = issued.Token;
        start.Environment["BLOKEMON_SESSION_EXPIRES_AT"] = issued.Session.ExpiresAt.ToString("O");
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
        report.ShouldNotContain(issued.Token);
        process.ExitCode.ShouldBe(0, report);
        report.ShouldContain("HEADLESS SESSION EVIDENCE COMPLETE");
        // The browser signed out: the session document is gone from the store.
        (await host.WithStore(store => store.Read($"session/{issued.Session.Id}"))).ShouldBeNull();
    }

    private static string Origin(SessionHost host)
    {
        using var client = host.Factory.CreateClient();
        return client.BaseAddress!.ToString().TrimEnd('/');
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
