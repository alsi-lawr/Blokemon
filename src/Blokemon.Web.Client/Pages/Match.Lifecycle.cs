using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Pages;

// Starting a battle, and the focus the surfaces borrow while one is on.
public partial class Match
{
    protected override async Task OnInitializedAsync()
    {
        var response = await ApplicationState.State();
        if (response.Succeeded)
        {
            _view = response.Value;
            _selectedDeckId = ReadyDecks().FirstOrDefault()?.Id;
            _selectedDifficulty = _view?.Match?.Difficulty ?? CpuDifficultyView.Normal;
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

            // The drag is the browser's, and it reports back to this page: what it picks up, and
            // where it puts it down.
            _dragModule = await Js.InvokeAsync<IJSObjectReference>("import", "./matchDrag.js");
            _dragReference = DotNetObjectReference.Create(this);
            await _dragModule.InvokeVoidAsync("armDrags", _dragReference);

            // The browser game's computer boots its own runtime now, in the background, so the
            // first decision of a battle is not also the first wait for it. The server game's
            // computer is the server's, and needs nothing here.
            var mode = await Modes.Mode();
            if (mode.Selected == PlayMode.BrowserLocal)
            {
                _ = Computer.Start();
            }

            // A battle resumed from this device can be part way through the computer's turn.
            // Nothing after the first render redraws the page on its own, so what the computer
            // did is drawn here; a page with nothing to play is left as it was drawn.
            if (await PlayComputerTurn())
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        if (_presentationModule is null)
        {
            return;
        }

        // Whichever surface is showing owns focus, and closing the last one hands focus back to
        // the card or button that opened it. Keying on the surface itself covers the forced
        // decisions that open themselves as well as the flows the player starts. An opened Empties
        // Tray covers the table the same way a forced decision does, so while it is up it is the
        // surface, and the sheet underneath takes focus back when the tray is put down.
        var surfaceKey = _emptiesOpponent is { } opponent ? $"empties:{opponent}" : _sheetKey;
        if (surfaceKey == _focusedSurfaceKey)
        {
            return;
        }

        _focusedSurfaceKey = surfaceKey;
        if (surfaceKey is null)
        {
            await _presentationModule.InvokeVoidAsync("restoreFocus");
            return;
        }

        await _presentationModule.InvokeVoidAsync(
            "focusSurface",
            _emptiesOpponent is null ? _sheet : _empties!.Element
        );
    }

    private DeckView[] ReadyDecks() =>
        _view?.Decks.Where(static deck => deck.IsLegal).ToArray() ?? [];

    private DeckView? SelectedDeck() =>
        ReadyDecks().FirstOrDefault(deck => deck.Id == _selectedDeckId)
        ?? ReadyDecks().FirstOrDefault();

    private Task StartSelected() =>
        SelectedDeck() is { } deck ? Start(deck.Id) : Task.CompletedTask;

    private void BeginAnotherBattle()
    {
        _operationError = null;
        _commandId = null;
        _selectingBattle = true;
    }

    private async Task Start(Guid deckId)
    {
        _working = true;
        _commandId ??= Guid.NewGuid();
        var response = await MatchOperations.StartMatch(
            new(_commandId.Value, deckId, _selectedDifficulty)
        );
        if (response.Succeeded && response.Value is not null)
        {
            _selectingBattle = false;
        }
        // A new game is not a move that can be seen happening: what came before it is the game
        // before. So it is presented over an empty table rather than over whatever the last battle
        // left standing, and the opening hands are dealt onto it.
        await CompleteMutation(response, MatchOpening.EmptyTable(response.Value?.Presentation));
        await PlayComputerTurn();
    }

    private static bool ActiveRecovery(MatchRecoveryView recovery) =>
        recovery.Kind
            is MatchRecoveryKindView.ActiveMatchUnsupportedVersion
                or MatchRecoveryKindView.ActiveMatchIncompatibleWithCurrentRules
                or MatchRecoveryKindView.ActiveMatchCorrupt;

    private void BeginRecoveryConfirmation()
    {
        _operationError = null;
        _confirmingRecovery = true;
    }

    private void CancelRecoveryConfirmation()
    {
        _operationError = null;
        _confirmingRecovery = false;
    }

    private async Task ConfirmRecovery()
    {
        if (_view?.MatchRecovery is not { } recovery)
        {
            return;
        }

        _working = true;
        ApiResponse<ApplicationView> response;
        if (ActiveRecovery(recovery))
        {
            response = await MatchRecoveryOperations.AbandonSavedMatch(
                new(recovery.Revision, recovery.ContentIdentity)
            );
        }
        else
        {
            response = await MatchRecoveryOperations.DiscardMatchHistory(
                new(recovery.Revision, recovery.ContentIdentity)
            );
        }

        _working = false;
        if (!response.Succeeded || response.Value is null)
        {
            _operationError =
                response.Error?.Message ?? "The saved battle data was not changed. Try again.";
            return;
        }

        _view = response.Value;
        _operationError = null;
        _confirmingRecovery = false;
        _selectedDeckId = ReadyDecks().FirstOrDefault()?.Id;
    }

    // A move in flight, a presentation playing, or the computer deciding: the turn's own controls
    // wait on all three. The computer decides off this thread, so the table is live while it
    // does, and a move of the player's sent meanwhile - both sides choose their opening Blokemon
    // at once - would arrive against a battle the computer's decision has already changed.
    private bool Busy() => _working || _animating || _thinking;

    public async ValueTask DisposeAsync()
    {
        _skipSignal?.TrySetResult();
        _revealSignal?.TrySetResult();
        _skipSignal = null;
        // The computer's runtime belongs to the battle on screen; leaving the table stops it.
        await Computer.Stop();
        await Release(_presentationModule);
        if (_dragModule is not null)
        {
            try
            {
                await _dragModule.InvokeVoidAsync("disarmDrags");
            }
            catch (JSDisconnectedException)
            {
                // A disconnected circuit has already let go of the page.
            }
        }

        await Release(_dragModule);
        _dragReference?.Dispose();
    }

    private static async Task Release(IJSObjectReference? module)
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // A disconnected circuit has already released the browser-side module.
        }
    }
}
