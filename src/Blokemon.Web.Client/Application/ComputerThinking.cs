using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.Cpu;
using Blokemon.Game;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>The rules a computer plays by, as its runtime reports them.</summary>
public sealed record ComputerVersions(string AuthorityVersion, int PolicyVersion);

[JsonSerializable(typeof(ComputerVersions))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ComputerVersionsJson : JsonSerializerContext;

/// <summary>
/// Where the browser game's computer thinks: in a Web Worker that runs a second copy of this
/// application's runtime, so the page's thread draws while the computer decides. The worker is
/// started when a battle opens, so its boot is paid before the first decision is wanted, and a
/// computer that cannot start, or plays by other rules than this page, is replaced by the one
/// that thinks on this thread so the game stays playable.
/// </summary>
public sealed class ComputerThinking(IJSRuntime js, BlokemonCatalogue catalogue)
    : IComputerDecider,
        IComputerRuntime,
        IAsyncDisposable
{
    private const string CatalogueUrl = "content/catalogue.json";

    private readonly IComputerDecider _inProcess = InProcess(catalogue);
    private Task<IJSObjectReference?>? _worker;

    /// <summary>Starts the computer's runtime. Safe to call more than once.</summary>
    public Task Start() => Worker();

    /// <summary>Stops the computer's runtime; the next decision or start boots a fresh one.</summary>
    public async Task Stop()
    {
        if (_worker is null)
        {
            return;
        }

        var starting = _worker;
        _worker = null;
        try
        {
            var worker = await starting;
            if (worker is not null)
            {
                await worker.InvokeVoidAsync("stop");
                await worker.DisposeAsync();
            }
        }
        catch (Exception error) when (error is JSException or JSDisconnectedException)
        {
            // Leaving the page has already released the worker.
        }
    }

    public async Task<string?> Decide(
        MatchDocument document,
        MatchState state,
        CancellationToken cancellationToken
    )
    {
        var worker = await Worker();
        if (worker is null)
        {
            return await _inProcess.Decide(document, state, cancellationToken);
        }

        try
        {
            return await worker.InvokeAsync<string?>(
                "decide",
                cancellationToken,
                ComputerDecisions.documentJson(document)
            );
        }
        catch (JSException error)
        {
            // One failed decision retires the worker for this battle: the answer the page needs
            // now comes from this thread, and the next battle starts a fresh runtime.
            Console.Error.WriteLine($"The computer's runtime failed: {error.Message}");
            await Stop();
            return await _inProcess.Decide(document, state, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync() => await Stop();

    private Task<IJSObjectReference?> Worker() => _worker ??= StartWorker();

    private async Task<IJSObjectReference?> StartWorker()
    {
        IJSObjectReference? module = null;
        try
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", "./computerThinking.js");
            var versions = await module.InvokeAsync<ComputerVersions>("start", CatalogueUrl);
            if (
                versions.AuthorityVersion != catalogue.Mechanics.ManifestVersion
                || versions.PolicyVersion != CpuPolicyVersion.active
            )
            {
                Console.Error.WriteLine(
                    "The computer's runtime plays by other rules than this page; it thinks here instead."
                );
                await module.InvokeVoidAsync("stop");
                return null;
            }

            return module;
        }
        catch (Exception error) when (error is JSException or JSDisconnectedException)
        {
            Console.Error.WriteLine($"The computer's runtime did not start: {error.Message}");
            if (module is not null)
            {
                try
                {
                    await module.InvokeVoidAsync("stop");
                }
                catch (JSException)
                {
                    // The module is already gone with the worker.
                }
            }

            return null;
        }
    }

    private static IComputerDecider InProcess(BlokemonCatalogue catalogue) =>
        ComputerDecisions.inProcess(new MatchEngine(catalogue.Mechanics), new DeterministicCpu());
}
