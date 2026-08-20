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

    private void CloseViewer()
    {
        if (_viewerCard is null)
        {
            return;
        }

        _viewerCard = null;
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
