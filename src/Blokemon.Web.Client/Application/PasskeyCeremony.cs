using System.Text.Json;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// The browser's side of a passkey ceremony: hands the server's options to the credential API
/// and returns the credential as JSON, or null when the person declined or the browser could
/// not run it. The byte handling lives in passkeys.js; this is its handle.
/// </summary>
public sealed class PasskeyCeremony(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>Whether this browser can use passkeys at all.</summary>
    public async Task<bool> Available(CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await Module(cancellationToken);
            return await module.InvokeAsync<bool>("available", cancellationToken);
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a ceremony can run in this document: at top level always; inside a hosting
    /// frame only when the parent delegated the permission.
    /// </summary>
    public async Task<bool> CanRunHere(CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await Module(cancellationToken);
            return await module.InvokeAsync<bool>("canRunHere", cancellationToken);
        }
        catch (JSException)
        {
            return false;
        }
    }

    public Task<JsonElement?> Create(
        JsonElement options,
        CancellationToken cancellationToken = default
    ) => Run("create", options, cancellationToken);

    public Task<JsonElement?> Get(
        JsonElement options,
        CancellationToken cancellationToken = default
    ) => Run("get", options, cancellationToken);

    private async Task<JsonElement?> Run(
        string ceremony,
        JsonElement options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var module = await Module(cancellationToken);
            var credential = await module.InvokeAsync<JsonElement>(
                ceremony,
                cancellationToken,
                options
            );
            return credential.ValueKind is JsonValueKind.Object ? credential : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser already released the module.
            }
        }
    }

    private async Task<IJSObjectReference> Module(CancellationToken cancellationToken) =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./passkeys.js"
        );
}
