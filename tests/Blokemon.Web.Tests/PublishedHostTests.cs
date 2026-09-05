using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Blokemon.Web.Hosting;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Blokemon.Web.Tests;

/// <summary>
/// The published host, as <c>dotnet publish src/Blokemon.Web</c> yields it and as it would run
/// in production: started from its own output with a temporary data directory, then checked
/// for the routes, headers, cache directives and compression BLOKEMON-155 states, and driven
/// through headless Chrome by headless_published_evidence.py for the anonymous browser-local
/// journey, the absence of any circuit, and the signed-out server choice landing on sign-in.
/// The publish takes over a minute; run alone with
/// <c>dotnet test --no-build --project tests/Blokemon.Web.Tests -- --treenode-filter "/*/*/PublishedHostTests/*"</c>.
/// </summary>
public sealed partial class PublishedHostTests
{
    [Test]
    [Timeout(900_000)]
    public async Task ThePublishedHost_ServesTheClientTheApiAndTheHeaders_AndPassesTheHeadlessSmoke(
        CancellationToken cancellationToken
    )
    {
        var root = Path.Combine(Path.GetTempPath(), $"blokemon-published-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var site = Path.Combine(root, "site");
            await Publish(root, site, cancellationToken);

            var port = FreePort();
            var origin = $"http://127.0.0.1:{port}";
            using var host = Start(site, Path.Combine(root, "data"), port);
            using var client = new HttpClient { BaseAddress = new Uri(origin) };
            await WaitForHealth(client, host, cancellationToken);

            await CheckRoutesAndHeaders(client);
            await CheckCompression(origin, site);
            await RunHeadlessSmoke(origin, cancellationToken);
            host.Kill(entireProcessTree: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CheckRoutesAndHeaders(HttpClient client)
    {
        var shell = await client.GetStringAsync("/");
        shell.ShouldContain("_framework/blazor.web.js");
        var state = JsonNode.Parse(await client.GetStringAsync("/api/state"))!;
        state["succeeded"]!.GetValue<bool>().ShouldBeTrue();
        state["value"]!["profile"].ShouldBeNull("the signed-out view has no profile");
        var health = JsonNode.Parse(await client.GetStringAsync("/healthz"))!;
        health["status"]!.GetValue<string>().ShouldBe("ready");

        foreach (var path in new[] { "/", "/profile", "/t/core/continue", "/t/nobody" })
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            Header(response, "Content-Security-Policy").ShouldBe(HostingHeaders.NoneFraming, path);
            Header(response, "X-Frame-Options").ShouldBeNull(path);
            Header(response, "X-Content-Type-Options").ShouldBe("nosniff", path);
            Header(response, "Referrer-Policy").ShouldBe("no-referrer", path);
        }
        foreach (var path in new[] { "/t/core", "/t/core/continue", "/t/nobody" })
        {
            using var response = await client.GetAsync(path);
            Header(response, "Cache-Control").ShouldBe(HostingHeaders.NoStore, path);
        }
        foreach (
            var path in new[]
            {
                "/",
                "/api/state",
                "/healthz",
                "/css/foundation.css",
                "/art/BLK-001-weedman-will-200.webp",
                "/fonts/GilliusADF-Bold.otf",
                "/_framework/blazor.web.js",
            }
        )
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            var cache = Header(response, "Cache-Control") ?? "";
            (cache.Contains("no-cache") || cache.Contains("no-store")).ShouldBeTrue(
                $"{path}: {cache}"
            );
        }
        var runtime = FingerprintedRuntime().Match(shell);
        runtime.Success.ShouldBeTrue();
        using var immutable = await client.GetAsync("/" + runtime.Value);
        Header(immutable, "Cache-Control").ShouldBe(HostingHeaders.Immutable);
        using var negotiate = await client.PostAsync("/_blazor/negotiate", new StringContent(""));
        negotiate.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        (await negotiate.Content.ReadAsStringAsync()).ShouldNotContain("connectionId");
    }

    private static async Task CheckCompression(string origin, string site)
    {
        // The native runtime is named by the boot manifest, not the shell: its fingerprinted
        // file is found in the published output.
        var wasm = Directory
            .EnumerateFiles(Path.Combine(site, "wwwroot", "_framework"), "dotnet.native.*.wasm")
            .Select(Path.GetFileName)
            .Single(static name => FingerprintedWasm().IsMatch("_framework/" + name));
        foreach (var encoding in new[] { "br", "gzip" })
        {
            using var client = new HttpClient(
                new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None }
            );
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(encoding);
            using var response = await client.GetAsync($"{origin}/_framework/{wasm}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentEncoding.ShouldBe([encoding]);
        }
    }

    private static async Task RunHeadlessSmoke(string origin, CancellationToken cancellationToken)
    {
        var browser = Find("google-chrome-stable", "google-chrome", "chromium", "chromium-browser");
        var python = Find("python3");
        if (browser is null || python is null)
        {
            throw new SkipTestException("Chrome and python3 are needed for the headless smoke.");
        }

        var script = Path.Combine(
            RepositoryRoot(),
            "tests",
            "Blokemon.Web.Tests",
            "headless_published_evidence.py"
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
        report.ShouldContain("HEADLESS PUBLISHED EVIDENCE COMPLETE");
    }

    private static async Task Publish(string root, string site, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        foreach (
            var argument in new[]
            {
                "publish",
                Path.Combine("src", "Blokemon.Web", "Blokemon.Web.csproj"),
                "--configuration",
                "Release",
                "--artifacts-path",
                Path.Combine(root, "artifacts"),
                "--output",
                site,
                "-m:1",
                "-p:BuildInParallel=false",
                "-nr:false",
                "-p:UseSharedCompilation=false",
                "-p:UseRazorBuildServer=false",
            }
        )
        {
            start.ArgumentList.Add(argument);
        }
        using var publish = Process.Start(start)!;
        var output = publish.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = publish.StandardError.ReadToEndAsync(cancellationToken);
        await publish.WaitForExitAsync(cancellationToken);
        publish.ExitCode.ShouldBe(0, $"{await output}\n{await errors}");
        File.Exists(Path.Combine(site, "Blokemon.Web.dll")).ShouldBeTrue();
    }

    private static Process Start(string site, string dataDirectory, int port)
    {
        Directory.CreateDirectory(dataDirectory);
        // The shipped appsettings bind 127.0.0.1:5080 and would win over an environment
        // variable; the command line is read last, so the port is given there.
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = site,
        };
        start.ArgumentList.Add(Path.Combine(site, "Blokemon.Web.dll"));
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Blokemon__DataDirectory"] = dataDirectory;
        var process = Process.Start(start)!;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForHealth(
        HttpClient client,
        Process host,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            host.HasExited.ShouldBeFalse("the published host exited before answering");
            try
            {
                using var response = await client.GetAsync("/healthz", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new ShouldAssertException("The published host did not answer /healthz in time.");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var content) ? string.Join(",", content)
        : null;

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

    [GeneratedRegex(@"_framework/dotnet\.[a-z0-9]{8,12}\.js")]
    private static partial Regex FingerprintedRuntime();

    [GeneratedRegex(@"_framework/dotnet\.native\.[a-z0-9]{8,12}\.wasm")]
    private static partial Regex FingerprintedWasm();
}
