using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// Moves the held session to a top-level window for what a frame cannot do: a continuation
/// code is minted for it and the window opened at the tenant's continuation route with the
/// code in the fragment. The session token itself never leaves this document.
/// </summary>
public sealed class ContinuationWindow(
    SessionApiClient api,
    NavigationManager navigation,
    IJSRuntime js
)
{
    public static readonly ApiError Blocked = new(
        "continuation.blocked",
        "Your browser blocked the window. Allow pop-ups for this page and try again."
    );

    private IJSObjectReference? _module;

    /// <summary>Opens the window; the typed error says why it did not.</summary>
    public async Task<ApiResponse<ContinuationView>> Open(
        CancellationToken cancellationToken = default
    )
    {
        var response = await api.Continue(cancellationToken);
        if (!response.Succeeded || response.Value is null)
        {
            return response;
        }

        var url =
            $"{navigation.BaseUri.TrimEnd('/')}{response.Value.Path}#handoff={Uri.EscapeDataString(response.Value.Code)}";
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./signIn.js"
            );
            var opened = await _module.InvokeAsync<bool>(
                "openContinuation",
                cancellationToken,
                url
            );
            return opened ? response : new(false, null, Blocked);
        }
        catch (JSException)
        {
            return new(false, null, Blocked);
        }
    }
}
