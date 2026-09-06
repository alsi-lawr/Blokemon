using Blokemon.Web.Client.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Pages;

// ---- The drag ---------------------------------------------------------------------------
//
// A card is played by carrying it to where it goes. The carrying is the browser's (matchDrag.js):
// the card follows the pointer there, and the place under it is looked up there, so no frame of
// the movement goes through the renderer. The page is told three things - a card was picked up,
// a card was dropped on a place, a card was let go over nothing - and answers each the way it
// answers the tap that means the same. What was named by the browser is checked against what
// glows before anything happens: a drop is only ever what the equivalent tap would have been.
public partial class Match
{
    private IJSObjectReference? _dragModule;
    private DotNetObjectReference<Match>? _dragReference;

    // A drag has begun on a card. It is picked up the way a tap picks it up, putting down
    // whatever was held before. Answers whether it was, so a card that cannot be picked up now
    // stays where it is.
    [JSInvokable]
    public async Task<bool> CardPickedUp(string cardInstanceId)
    {
        var picked = false;
        await InvokeAsync(async () =>
        {
            picked = await PickUp(cardInstanceId);
            StateHasChanged();
        });
        return picked;
    }

    // The dragged card was let go over a place the table shows.
    [JSInvokable]
    public Task CardDropped(string cardInstanceId, string place, string? targetCardInstanceId) =>
        InvokeAsync(async () =>
        {
            if (
                _originCardInstanceId == cardInstanceId
                && _stage == Stage.Destination
                && DroppedTarget(place, targetCardInstanceId) is { } target
            )
            {
                _selectedCardInstanceId = cardInstanceId;
                await DropOn(target);
            }
            else
            {
                CancelFlow();
            }

            await SettleDrag();
            StateHasChanged();
        });

    // The dragged card was let go over nothing: it has sprung back, and it is put down.
    [JSInvokable]
    public Task CardReleased(string cardInstanceId) =>
        InvokeAsync(() =>
        {
            if (
                _originCardInstanceId == cardInstanceId
                && _stage is Stage.Armed or Stage.Destination
            )
            {
                CancelFlow();
            }

            StateHasChanged();
        });

    private async Task<bool> PickUp(string cardInstanceId)
    {
        if (
            _view?.Match is not { } match
            || Busy()
            || !Pickable(match).Contains(cardInstanceId, StringComparer.Ordinal)
        )
        {
            return false;
        }

        if (_stage is Stage.Armed or Stage.Destination && _originCardInstanceId == cardInstanceId)
        {
            return true;
        }

        if (_stage is not (Stage.Idle or Stage.Armed or Stage.Destination))
        {
            return false;
        }

        _selectedCardInstanceId = cardInstanceId;
        _operationError = null;
        CancelFlow();
        await SelectOrigin(cardInstanceId);
        if (_stage is Stage.Armed or Stage.Destination)
        {
            return true;
        }

        // Several moves with nowhere on the table to go are a choice the sheet asks, which is
        // not a thing to carry: the card goes back down, and the tap asks it.
        CancelFlow();
        return false;
    }

    private MatchTarget? DroppedTarget(string place, string? targetCardInstanceId)
    {
        MatchTarget? named = place switch
        {
            MatchDropPlaces.Card when targetCardInstanceId is not null => MatchTarget.Card(
                targetCardInstanceId
            ),
            MatchDropPlaces.Bench => MatchTarget.At(MatchTargetPlaces.Bench),
            MatchDropPlaces.Active => MatchTarget.At(MatchTargetPlaces.Active),
            MatchDropPlaces.InPlay => MatchTarget.At(MatchTargetPlaces.InPlay),
            MatchDropPlaces.Empties => MatchTarget.At(MatchTargetPlaces.Empties),
            _ => null,
        };
        return named is { } target && OriginActions().SelectMany(TargetsOf).Contains(target)
            ? target
            : null;
    }

    // A card from the hand dropped on the table is left where it landed until the table takes
    // it, so the move it made starts from there. One still in the hand once the drop has settled
    // - a move refused, a question opened - goes back where it was.
    private async Task SettleDrag()
    {
        if (_dragModule is null)
        {
            return;
        }

        try
        {
            await _dragModule.InvokeVoidAsync("settleDrag");
        }
        catch (JSException)
        {
            // A card the browser no longer has has nothing to settle.
        }
    }
}
