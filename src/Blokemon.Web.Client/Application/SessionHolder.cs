using Blokemon.App.Contracts;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// A session as the browser holds it: the token, its absolute expiry, the player's name, and
/// whether it is a recovery session that can only enrol a replacement passkey.
/// </summary>
public sealed record HeldSession(
    string Token,
    DateTimeOffset? ExpiresAt,
    string? DisplayName,
    bool Recovery = false
);

/// <summary>
/// Holds the session in memory with a sessionStorage copy, so a reload keeps it and closing the
/// tab drops it. The token goes nowhere else: not a URL, not a cookie, not a message.
/// </summary>
public sealed class SessionHolder(IJSRuntime js, SessionTokenStore tokens, TimeProvider time)
    : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private Task? _read;

    public HeldSession? Current { get; private set; }

    /// <summary>Raised after the held session is established, discarded or loaded.</summary>
    public event Action? Changed;

    /// <summary>
    /// Reads the sessionStorage copy once; a copy past its expiry is dropped unread. The menu
    /// and the page start together and both ask, so the second asker waits for the one read
    /// rather than being told there is no session while it is still being read.
    /// </summary>
    public async Task<HeldSession?> Load(CancellationToken cancellationToken = default)
    {
        await (_read ??= ReadStored(cancellationToken));
        return Current;
    }

    private async Task ReadStored(CancellationToken cancellationToken)
    {
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
            return;
        }

        if (stored.ExpiresAt is { } expiry && expiry <= time.GetUtcNow())
        {
            await Discard(cancellationToken);
            return;
        }

        Apply(new(stored.Token, stored.ExpiresAt, stored.DisplayName, stored.Recovery));
    }

    public async Task Establish(
        IssuedSessionView issued,
        CancellationToken cancellationToken = default
    )
    {
        _read = Task.CompletedTask;
        Apply(new(issued.Token, issued.ExpiresAt, issued.DisplayName, issued.Recovery));
        try
        {
            var module = await Module(cancellationToken);
            await module.InvokeVoidAsync(
                "write",
                cancellationToken,
                issued.Token,
                issued.ExpiresAt,
                issued.DisplayName,
                issued.Recovery
            );
        }
        catch (JSException)
        {
            // Without sessionStorage the session lasts until the page unloads.
        }
    }

    /// <summary>
    /// The player name the held session now acts for: a session issued before the profile
    /// existed carries none, the name the person chooses replaces it here and in storage, and a
    /// purge that removes the player takes the name with it.
    /// </summary>
    public async Task Rename(string? displayName, CancellationToken cancellationToken = default)
    {
        if (Current is not { } held)
        {
            return;
        }

        Apply(held with { DisplayName = displayName });
        try
        {
            var module = await Module(cancellationToken);
            await module.InvokeVoidAsync(
                "write",
                cancellationToken,
                held.Token,
                held.ExpiresAt,
                displayName,
                held.Recovery
            );
        }
        catch (JSException)
        {
            // Without sessionStorage the name lives in memory with the session.
        }
    }

    public async Task Discard(CancellationToken cancellationToken = default)
    {
        _read = Task.CompletedTask;
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
        string? DisplayName,
        bool Recovery = false
    );
}
