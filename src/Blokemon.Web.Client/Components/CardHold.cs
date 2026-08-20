using Microsoft.AspNetCore.Components.Web;

namespace Blokemon.Web.Client.Components;

// The press that reads a card, wherever the card is drawn. A press held past the threshold is a
// view and lasts until the pointer is released; a press that travels far enough first is a scroll
// or a drag and is neither a view nor a tap.
//
// It is one object rather than one copy per surface because it is one gesture: a card on the table,
// a card in hand and a card attached to another card are all read by the same hold, and a threshold
// that drifted between them would be a different gesture in each place a card happens to be drawn.
internal sealed class CardHold
{
    // Long enough that a deliberate hold is unambiguous, short enough to feel immediate.
    private const int _holdMilliseconds = 480;

    // A press that travels further than this is a scroll or a drag, not a tap or a hold.
    private const double _travelTolerance = 12;

    private CancellationTokenSource? _hold;
    private double _startX;
    private double _startY;

    // Whether the press has already opened the viewer, and therefore has one to close.
    public bool Viewing { get; private set; }

    // Starts a press. The viewer opens if this press is still the current one when the threshold
    // passes: a new press always starts clean, so a view whose release landed on the viewer rather
    // than on the card does not survive into the press after it.
    public void Down(PointerEventArgs eventArgs, Func<Task> view)
    {
        Viewing = false;
        _startX = eventArgs.ClientX;
        _startY = eventArgs.ClientY;
        Stop();
        var hold = new CancellationTokenSource();
        _hold = hold;
        _ = Wait(hold, view);
    }

    // Whether the press has become a scroll or a drag. A press that has already opened the viewer
    // is not reconsidered - the pointer is holding a card up, and moving while it does so is not a
    // scroll.
    public bool Travelled(PointerEventArgs eventArgs)
    {
        if (_hold is null || Viewing)
        {
            return false;
        }

        var travelled =
            Math.Abs(eventArgs.ClientX - _startX) > _travelTolerance
            || Math.Abs(eventArgs.ClientY - _startY) > _travelTolerance;
        if (!travelled)
        {
            return false;
        }

        Stop();
        return true;
    }

    // Ends the press, saying whether it was holding a card up and so has one to put back down.
    public bool Release()
    {
        Stop();
        if (!Viewing)
        {
            return false;
        }

        Viewing = false;
        return true;
    }

    private async Task Wait(CancellationTokenSource hold, Func<Task> view)
    {
        try
        {
            await Task.Delay(_holdMilliseconds, hold.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_hold, hold))
        {
            return;
        }

        Viewing = true;
        await view();
    }

    private void Stop()
    {
        var hold = _hold;
        _hold = null;
        if (hold is null)
        {
            return;
        }

        hold.Cancel();
        hold.Dispose();
    }
}
