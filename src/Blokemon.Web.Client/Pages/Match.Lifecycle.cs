using Blokemon.App.Contracts;
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
        var previousFrame = _view?.Match?.Frame;
        var response = await Api.StartMatch(new(_commandId.Value, deckId));
        await CompleteMutation(response, previousFrame);
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
