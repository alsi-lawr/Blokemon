using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Blokemon.Web.Client.Pages;

// ---- Tap routing ----------------------------------------------------------------------
//
// Where a tap goes is decided here and nowhere else. The presenters report that a card, a place
// on the table or the table itself was pressed; the stage says what that means. A tap that
// leaves nothing further to ask is the move itself, so it is played rather than confirmed.
//
// A card is picked up by tapping it, and put down on a target by tapping the target: the same
// two steps a drag makes in one movement, so the two are routed through the same places here.
public partial class Match
{
    // The only moves that still stop to be confirmed: they end something, and neither of them
    // has a place on the table that could have said it instead.
    private static readonly MatchActionKindView[] _confirmKinds =
    [
        MatchActionKindView.EndTurn,
        MatchActionKindView.Resign,
    ];

    private Task TapCard(string cardInstanceId)
    {
        if (_view?.Match is not { } match || Busy())
        {
            return Task.CompletedTask;
        }

        _selectedCardInstanceId = cardInstanceId;
        _operationError = null;

        var forced = ForcedDecision(match);
        switch (_stage)
        {
            case Stage.Choice when CurrentRequirement() is { } requirement:
                TapChoiceCard(requirement, cardInstanceId);
                return Task.CompletedTask;

            // A decision the match posed is answered by its cards: tapping a candidate picks it
            // up, and the place it goes glows for it.
            case Stage.Idle when forced.Length > 0:
                return
                    ForcedByAura(forced)
                    && forced.Any(option => option.SourceCardInstanceId == cardInstanceId)
                    ? SelectOrigin(cardInstanceId)
                    : Task.CompletedTask;

            case Stage.Destination when IsTarget(cardInstanceId):
                return DropOn(MatchTarget.Card(cardInstanceId));

            // The picked-up card is put down by tapping it again: where it stands, when its one
            // move has no place on the table, or on the one place it can go.
            case Stage.Armed when cardInstanceId == _originCardInstanceId:
                return StartAction(_menu[0]);

            case Stage.Destination when cardInstanceId == _originCardInstanceId:
                return OnlyTarget() is { } only ? DropOn(only) : Task.CompletedTask;

            case Stage.Idle when Pickable(match).Contains(cardInstanceId, StringComparer.Ordinal):
                return SelectOrigin(cardInstanceId);

            case Stage.Armed
            or Stage.Destination
                when Pickable(match).Contains(cardInstanceId, StringComparer.Ordinal):
                // Tapping another card that can be picked up puts the first one back down and
                // picks that one up instead, whichever kind of move was being set up.
                CancelFlow();
                return SelectOrigin(cardInstanceId);

            default:
                return Task.CompletedTask;
        }
    }

    private bool IsTarget(string cardInstanceId) =>
        TargetCardIds().Contains(cardInstanceId, StringComparer.Ordinal);

    // The one place the picked-up card can go, when there is exactly one and nothing it does is
    // used where it stands: a tap on the card itself is then as good as a tap on the place.
    private MatchTarget? OnlyTarget()
    {
        if (InPlaceActions().Length > 0)
        {
            return null;
        }

        var targets = OriginActions().SelectMany(TargetsOf).Distinct().ToArray();
        return targets.Length == 1 ? targets[0] : null;
    }

    private Task TapBench() => DropOn(MatchTarget.At(MatchTargetPlaces.Bench));

    private Task TapActiveSlot() => DropOn(MatchTarget.At(MatchTargetPlaces.Active));

    private Task TapInPlay() => DropOn(MatchTarget.At(MatchTargetPlaces.InPlay));

    private Task TapEmpties() => DropOn(MatchTarget.At(MatchTargetPlaces.Empties));

    // The picked-up card put down on a place the table shows: a target card, an empty Bench
    // position, the empty Active position, the In-play place or the Empties Tray, reached by a
    // tap or by a drop. The moves that go there are what happens: one plays, and several are a
    // real choice between effects that the sheet asks. A Power aimed at a card the table shows
    // is used, and the card it landed on is its answer.
    private async Task DropOn(MatchTarget target)
    {
        if (_stage != Stage.Destination || _originCardInstanceId is null || Busy())
        {
            return;
        }

        _operationError = null;
        _destinationCardInstanceId = target.CardInstanceId;
        var actions = OriginActions().Where(action => TargetsOf(action).Contains(target)).ToArray();
        if (
            actions is [{ Kind: MatchActionKindView.UsePokemonPower } power]
            && target.CardInstanceId is { } answer
            && TableQuestion(power) is { } question
        )
        {
            await StartAction(power);
            if (_stage == Stage.Choice && CurrentRequirement()?.Id == question.Id)
            {
                TapChoiceCard(question, answer);
                if (StepComplete())
                {
                    await AdvanceChoice();
                }
            }

            return;
        }

        await OpenMenu(actions);
    }

    // The Deck is a place on the table like any other. While a draw is outstanding it glows, and
    // tapping it takes one card off it; what is left of the draw keeps it glowing for the next.
    private Task TapDeck()
    {
        if (_view?.Match is not { } match || _stage != Stage.Idle || Busy())
        {
            return Task.CompletedTask;
        }

        _operationError = null;
        var forced = ForcedDecision(match);
        return forced.Length > 0 && ForcedByDeck(forced)
            ? StartAction(DeckDraw(forced))
            : Task.CompletedTask;
    }

    // A tap on the empty table, or Escape, puts a picked-up card down. A question that opened
    // itself has nothing to go back to, so the table cannot dismiss it: the match is waiting on
    // the answer.
    private void TapBackground()
    {
        if (_stage == Stage.Idle || _autoStarted)
        {
            return;
        }

        CancelFlow();
    }

    // The keyboard's way of putting a card down. In full screen the browser takes Escape for
    // itself before the page hears it, so the tap on the table is the way out there.
    private void Key(KeyboardEventArgs eventArgs)
    {
        if (eventArgs.Key == "Escape")
        {
            TapBackground();
        }
    }

    // Picks a card up. Where it can go glows for it and the rest of the table steps back; a card
    // with one move and nowhere on the table to send it is held where it stands, and tapping it
    // again is the move. Several moves with nowhere to go are a real choice, asked by the sheet.
    private Task SelectOrigin(string cardInstanceId)
    {
        _originCardInstanceId = cardInstanceId;
        _destinationCardInstanceId = null;
        var actions = OriginActions();
        _directActions =
        [
            .. actions.Where(static action =>
                action.TargetCardInstanceId is null
                && action.Kind != MatchActionKindView.PlayBlokemon
            ),
        ];
        if (actions.Any(action => TargetsOf(action).Any()))
        {
            _hadDestinationStep = true;
            _stage = Stage.Destination;
            return Task.CompletedTask;
        }

        _hadDestinationStep = false;
        if (_directActions.Length == 1)
        {
            _menu = _directActions;
            _directActions = [];
            _stage = Stage.Armed;
            return Task.CompletedTask;
        }

        return OpenMenu(_directActions);
    }

    // A tap on a place the table shows has said everything a move needs, so the move it settles
    // on happens.
    private Task OpenMenu(MatchActionView[] actions)
    {
        _menu = actions;
        _directActions = [];
        if (actions.Length == 0)
        {
            CancelFlow();
            return Task.CompletedTask;
        }

        if (actions.Length == 1)
        {
            // One available action needs no menu: the tap that reached it already said it.
            return StartAction(actions[0]);
        }

        _stage = Stage.Actions;
        return Task.CompletedTask;
    }

    private Task StartAction(MatchActionView action)
    {
        _pending = action;
        _choiceValidation = null;
        _attachmentCardInstanceId = null;
        _autoStarted = false;
        _drafts.Clear();
        foreach (var requirement in LocalRequirements(action))
        {
            _drafts[requirement.Id] = new ChoiceDraft { Amount = requirement.Minimum };
        }

        _choiceStep = NextActiveStep(-1);
        if (_choiceStep >= 0)
        {
            _stage = Stage.Choice;
            return Task.CompletedTask;
        }

        // Nothing is left to ask. A move that ends the turn stops to be confirmed; every other
        // move was already said by the place that was tapped, so it is played.
        if (_confirmKinds.Contains(action.Kind))
        {
            _stage = Stage.Confirm;
            return Task.CompletedTask;
        }

        return CommitPending();
    }

    // One step back at a time, so cancelling is reachable from every surface without ever
    // trapping the player: an earlier choice step, then the action, then the flow itself.
    private void StepBack()
    {
        _choiceValidation = null;
        if (_stage == Stage.Choice)
        {
            var previous = PreviousActiveStep(_choiceStep);
            if (previous >= 0)
            {
                _choiceStep = previous;
                return;
            }

            BackFromAction();
            return;
        }

        // Only a move with nothing to ask still confirms, so there is never a step behind it.
        if (_stage == Stage.Confirm && _pending is not null)
        {
            BackFromAction();
            return;
        }

        CancelFlow();
    }

    private void BackFromAction()
    {
        var forced = _pending is not null && _forcedKinds.Contains(_pending.Kind);
        _pending = null;
        _drafts.Clear();
        _choiceValidation = null;
        _attachmentCardInstanceId = null;
        if (forced && !_hadDestinationStep)
        {
            _stage = Stage.Idle;
            return;
        }

        if (_menu.Length > 1)
        {
            _stage = Stage.Actions;
            return;
        }

        // Going back from an action reached through a destination returns to the destination
        // step. An action that had no such step has nothing behind it but the table.
        if (_hadDestinationStep && _originCardInstanceId is { } origin)
        {
            SelectOrigin(origin);
            return;
        }

        CancelFlow();
    }

    private void CancelFlow()
    {
        _stage = Stage.Idle;
        _pending = null;
        _menu = [];
        _directActions = [];
        _originCardInstanceId = null;
        _destinationCardInstanceId = null;
        _attachmentCardInstanceId = null;
        _hadDestinationStep = false;
        _choiceValidation = null;
        _autoStarted = false;
        _choiceStep = -1;
        _drafts.Clear();
    }
}
