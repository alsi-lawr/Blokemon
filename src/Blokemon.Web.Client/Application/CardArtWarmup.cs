using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>Warms the catalogue illustrations into the browser cache once the shell has rendered.</summary>
public sealed class CardArtWarmup(
    IJSRuntime js,
    BlokemonCatalogue catalogue,
    IBlokemonApplication application
)
{
    private bool _started;

    /// <summary>Starts warming in the background. Later calls do nothing.</summary>
    /// <param name="cancellationToken">Cancels the warming request.</param>
    /// <returns>The completed warming request.</returns>
    public async Task Start(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }
        _started = true;

        // A player who has not chosen where to save still browses cards, so a state that will
        // not load only costs the ordering, not the warming.
        var state = (await application.State(cancellationToken)).Value;
        var art = CardArtAssets.WarmingOrder(catalogue, state);
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./artWarmup.js"
            );
            await module.InvokeVoidAsync("warm", cancellationToken, art);
        }
        catch (JSException)
        {
            // Warming is optional; every card still fetches its own art when it renders.
        }
        catch (OperationCanceledException)
        {
            // The browser left the page before warming began.
        }
    }
}
