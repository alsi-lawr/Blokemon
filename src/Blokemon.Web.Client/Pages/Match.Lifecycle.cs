using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Pages;

// Starting a battle, and the focus the surfaces borrow while one is on.
public partial class Match
{
    protected override async Task OnInitializedAsync()
    {
        var response = await Api.State();
        if (response.Succeeded)
        {
            _view = response.Value;
            _selectedDeckId = ReadyDecks().FirstOrDefault()?.Id;
            EnsureCardSelection(selectDefault: true);
            // A battle resumed from this device can already be waiting on such a decision.
            await ResolveAutomaticDecisions();
        }
        else
        {
            _error = response.Error?.Message;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await MoveViewerFocus();
        if (firstRender)
        {
            _presentationModule = await Js.InvokeAsync<IJSObjectReference>(
                "import",
                "./matchPresentation.js"
            );
            _reducedMotion = await _presentationModule.InvokeAsync<bool>("prefersReducedMotion");
        }

        if (_presentationModule is null)
        {
            return;
        }

        // Whichever surface is showing owns focus, and closing the last one hands focus back to
        // the card or button that opened it. Keying on the surface itself covers the forced
        // decisions that open themselves as well as the flows the player starts.
        if (_sheetKey == _focusedSheetKey)
        {
            return;
        }

        _focusedSheetKey = _sheetKey;
        if (_sheetKey is null)
        {
            await _presentationModule.InvokeVoidAsync("restoreFocus");
            return;
        }

        await _presentationModule.InvokeVoidAsync("focusSurface", _sheet);
    }

    // A card being read holds focus for as long as it is up, and the card it was opened from takes
    // it back when it goes. Both moves wait for the browser to have the element in hand, which is
    // why neither happens where the viewer is opened or closed.
    private async Task MoveViewerFocus()
    {
        if (_viewerTakesFocus && _viewer is not null)
        {
            _viewerTakesFocus = false;
            await _viewer.Element.FocusAsync();
            // Taking focus is what puts the viewer in the way of the keyboard, so it is also what
            // makes it answerable for the two keys it takes off the browser. A card held up by the
            // pointer takes no focus and takes nothing.
            if (_presentationModule is not null)
            {
                await _presentationModule.InvokeVoidAsync("guardViewer", _viewer.Element);
            }
        }

        if (_returnFocus is { } card)
        {
            _returnFocus = null;
            await card.FocusAsync();
        }
    }

    private DeckView[] ReadyDecks() =>
        _view?.Decks.Where(static deck => deck.IsLegal).ToArray() ?? [];

    private DeckView? SelectedDeck() =>
        ReadyDecks().FirstOrDefault(deck => deck.Id == _selectedDeckId)
        ?? ReadyDecks().FirstOrDefault();

    private Task StartSelected() =>
        SelectedDeck() is { } deck ? Start(deck.Id) : Task.CompletedTask;

    private async Task Start(Guid deckId)
    {
        _working = true;
        _commandId ??= Guid.NewGuid();
        var response = await Api.StartMatch(new(_commandId.Value, deckId));
        // A new game is not a move that can be seen happening: what came before it is the game
        // before. So it is presented over an empty table rather than over whatever the last battle
        // left standing, and the opening hands are dealt onto it.
        await CompleteMutation(response, MatchOpening.EmptyTable(response.Value?.Presentation));
    }

    private bool Busy() => _working || _animating;

    public async ValueTask DisposeAsync()
    {
        _skipSignal?.TrySetResult();
        _revealSignal?.TrySetResult();
        _skipSignal = null;
        if (_presentationModule is not null)
        {
            try
            {
                await _presentationModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // A disconnected circuit has already released the browser-side module.
            }
        }
    }
}
