using Blokemon.App.Contracts;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>A session as the browser holds it: the token, its absolute expiry and the player's name.</summary>
public sealed record HeldSession(string Token, DateTimeOffset? ExpiresAt, string? DisplayName);

/// <summary>
/// Holds the session in memory with a sessionStorage copy, so a reload keeps it and closing the
/// tab drops it. The token goes nowhere else: not a URL, not a cookie, not a message.
/// </summary>
public sealed class SessionHolder(IJSRuntime js, SessionTokenStore tokens, TimeProvider time)
    : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _loaded;

    public HeldSession? Current { get; private set; }

    /// <summary>Raised after the held session is established, discarded or loaded.</summary>
    public event Action? Changed;

    /// <summary>Reads the sessionStorage copy once; a copy past its expiry is dropped unread.</summary>
    public async Task<HeldSession?> Load(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return Current;
        }

        _loaded = true;
        StoredSession? stored = null;
        try
        {
            var module = await Module(cancellationToken);
            stored = await module.InvokeAsync<StoredSession?>("read", cancellationToken);
        }
        catch (JSException)
        {
            // sessionStorage is unavailable in this context; the session lives in memory only.
        }

        if (stored is null)
        {
            return Current;
        }

        if (stored.ExpiresAt is { } expiry && expiry <= time.GetUtcNow())
        {
            await Discard(cancellationToken);
            return Current;
        }

        Apply(new(stored.Token, stored.ExpiresAt, stored.DisplayName));
        return Current;
    }

    public async Task Establish(
        IssuedSessionView issued,
        CancellationToken cancellationToken = default
    )
    {
        _loaded = true;
        Apply(new(issued.Token, issued.ExpiresAt, issued.DisplayName));
        try
        {
            var module = await Module(cancellationToken);
            await module.InvokeVoidAsync(
                "write",
                cancellationToken,
                issued.Token,
                issued.ExpiresAt,
                issued.DisplayName
            );
        }
        catch (JSException)
        {
            // Without sessionStorage the session lasts until the page unloads.
        }
    }

    public async Task Discard(CancellationToken cancellationToken = default)
    {
        _loaded = true;
        Apply(null);
        try
        {
            var module = await Module(cancellationToken);
            await module.InvokeVoidAsync("clear", cancellationToken);
        }
        catch (JSException)
        {
            // Nothing was stored.
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

    private void Apply(HeldSession? session)
    {
        Current = session;
        tokens.Token = session?.Token;
        Changed?.Invoke();
    }

    private async Task<IJSObjectReference> Module(CancellationToken cancellationToken) =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./sessionHolder.js"
        );

    private sealed record StoredSession(
        string Token,
        DateTimeOffset? ExpiresAt,
        string? DisplayName
    );
}
