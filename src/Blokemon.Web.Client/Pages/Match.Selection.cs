using System.Globalization;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Pages;

// Which frame the table is drawing, which card the inspector is reading, and the card viewer a
// held press opens. None of it decides a move; all of it decides what is on screen.
public partial class Match
{
    private async Task OpenViewer(string pressed)
    {
        var card = LookupCard(pressed);
        if (card is null)
        {
            return;
        }

        if (_presentationModule is not null)
        {
            _viewerScale = await _presentationModule.InvokeAsync<double>("viewerScale");
        }
        _viewerCard = card;
        StateHasChanged();
    }

    // A card asked for by a reading control rather than held up by the pointer. It stays up until
    // it is put down, so it takes focus off the table for as long as it is there and the card that
    // asked for it is owed that focus back.
    private async Task ReadCard(CardReadRequest request)
    {
        // Both are set before the viewer goes up rather than after, because the render that puts it
        // up is also the one that can hand it focus, and a page that only decided afterwards would
        // be waiting on a second render to catch up with the first.
        _viewerTakesFocus = true;
        _viewerReturn = request.Card;
        await OpenViewer(request.CardInstanceId);
        if (_viewerCard is not null)
        {
            return;
        }

        // Nothing to read, so nothing has borrowed focus and nothing is owed it back.
        _viewerTakesFocus = false;
        _viewerReturn = null;
    }

    // Looking through an Empties Tray is not a move: the pile is public, either player may read
    // either one whenever they like, and nothing about the game changes while it is open. So this
    // is the whole of it - which tray is being read, and that it has been put down again.
    private void OpenEmpties(bool opponent) => _emptiesOpponent = opponent;

    private void CloseEmpties() => _emptiesOpponent = null;

    private void CloseViewer()
    {
        if (_viewerCard is null)
        {
            return;
        }

        _viewerCard = null;
        _viewerTakesFocus = false;
        _returnFocus = _viewerReturn;
        _viewerReturn = null;
        StateHasChanged();
    }

    // What a press names: a card the table is drawing, one of the cards attached to it, or - for a
    // press on a card a question is offering that the table itself is not showing - a card the
    // pending question is holding out.
    private CardView? LookupCard(string pressed)
    {
        if (MatchTable.Pressed(AllVisibleCards(DisplayFrame()), pressed) is { } shown)
        {
            return shown;
        }

        return _pending
            ?.ChoiceRequirements.SelectMany(requirement =>
                requirement.EligibleCards.Concat(requirement.EligibleTargets)
            )
            .FirstOrDefault(card => card.Id == pressed)
            ?.Card;
    }

    private string ViewerStyle() =>
        $"--viewer-scale: {_viewerScale.ToString("0.####", CultureInfo.InvariantCulture)}";

    private void ToggleActionDock() => _dockOpen = !_dockOpen;

    private string DockClass() => _dockOpen ? "dock-open" : "dock-retracted";

    private void EnsureCardSelection(bool selectDefault)
    {
        if (_view?.Match is not { } match)
        {
            _selectedCardInstanceId = null;
            return;
        }

        var frame = match.Frame;
        if (
            _selectedCardInstanceId is not null
            && AllVisibleCards(frame).Any(card => card.Id == _selectedCardInstanceId)
        )
        {
            return;
        }

        _selectedCardInstanceId = selectDefault
            ? frame.Player.Active?.Id ?? frame.Player.Hand.FirstOrDefault()?.Id
            : frame.Player.Active?.Id;
    }

    private MatchCardInstanceView? SelectedInstance(MatchFrameView frame) =>
        AllVisibleCards(frame).FirstOrDefault(card => card.Id == _selectedCardInstanceId);

    private static IEnumerable<MatchCardInstanceView> AllVisibleCards(MatchFrameView frame) =>
        MatchTable.Shown(frame);

    private MatchFrameView DisplayFrame() => _presentedFrame ?? _view!.Match!.Frame;
}
