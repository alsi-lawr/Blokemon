using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;

namespace Blokemon.Web.Client.Pages;

// Which frame the table is drawing and which card the inspector is reading. None of it decides a
// move; all of it decides what is on screen.
public partial class Match
{
    // Looking through an Empties Tray is not a move: the pile is public, either player may read
    // either one whenever they like, and nothing about the game changes while it is open. So this
    // is the whole of it - which tray is being read, and that it has been put down again.
    private void OpenEmpties(bool opponent) => _emptiesOpponent = opponent;

    private void CloseEmpties() => _emptiesOpponent = null;

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
