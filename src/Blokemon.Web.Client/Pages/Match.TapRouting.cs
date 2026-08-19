using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Pages;

// ---- Tap routing ----------------------------------------------------------------------
//
// Where a tap goes is decided here and nowhere else. The presenters report that a card, the
// Bench, the Deck or the table itself was pressed; the stage says what that means. A tap that
// leaves nothing further to ask is the move itself, so it is played rather than confirmed.
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

            case Stage.Idle when forced.Length > 0:
                return
                    ForcedByAura(forced)
                    && forced.FirstOrDefault(option =>
                        option.SourceCardInstanceId == cardInstanceId
                    )
                        is { } candidate
                    ? StartAction(candidate)
                    : Task.CompletedTask;

            case Stage.Destination
                when DestinationCardIds().Contains(cardInstanceId, StringComparer.Ordinal):
                _destinationCardInstanceId = cardInstanceId;
                _benchDestination = false;
                return OpenMenu([
                    .. OriginActions()
                        .Where(action => action.TargetCardInstanceId == cardInstanceId),
                ]);

            // The armed card is played by tapping it again: the first tap picked it up, and this
            // is the one that puts it down.
            case Stage.Armed when cardInstanceId == _originCardInstanceId:
                return StartAction(_menu[0]);

            case Stage.Idle
                when PlayableCardIds(match).Contains(cardInstanceId, StringComparer.Ordinal):
                return SelectOrigin(cardInstanceId);

            case Stage.Armed
            or Stage.Destination
                when PlayableCardIds(match).Contains(cardInstanceId, StringComparer.Ordinal):
                // Tapping another playable card puts the first one back down and picks that one
                // up instead, whichever kind of move was being set up.
                CancelFlow();
                return SelectOrigin(cardInstanceId);

            default:
                return Task.CompletedTask;
        }
    }

    private Task TapBench()
    {
        if (_stage != Stage.Destination || _originCardInstanceId is null || Busy())
        {
            return Task.CompletedTask;
        }

        _destinationCardInstanceId = null;
        return OpenMenu([
            .. OriginActions()
                .Where(static action =>
                    action.Kind == MatchActionKindView.PlayBlokemon
                    && action.TargetCardInstanceId is null
                ),
        ]);
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

    private void TapBackground()
    {
        // A question that opened itself has nothing to go back to, so the table cannot dismiss
        // it: the match is waiting on the answer.
        if (_stage == Stage.Idle || _autoStarted)
        {
            return;
        }

        CancelFlow();
    }

    private Task SelectOrigin(string cardInstanceId)
    {
        _originCardInstanceId = cardInstanceId;
        _destinationCardInstanceId = null;
        var actions = OriginActions();
        var destinations = actions.Any(static action => action.TargetCardInstanceId is not null);
        _benchDestination =
            actions.Any(static action => action.Kind == MatchActionKindView.PlayBlokemon)
            && DisplayFrame().Player.Bench.Length < 5;
        _directActions =
        [
            .. actions.Where(static action =>
                action.TargetCardInstanceId is null
                && action.Kind != MatchActionKindView.PlayBlokemon
            ),
        ];
        if (destinations || _benchDestination)
        {
            _hadDestinationStep = true;
            _stage = Stage.Destination;
            return Task.CompletedTask;
        }

        _hadDestinationStep = false;

        // A card with one move and no place on the table to send it to is picked up rather than
        // played outright: it holds the chosen glow, everything else that could be played still
        // glows behind it, and tapping it again is the move. Anywhere else puts it back down.
        if (_directActions.Length == 1)
        {
            _menu = _directActions;
            _directActions = [];
            _stage = Stage.Armed;
            return Task.CompletedTask;
        }

        return OpenMenu(_directActions);
    }

    // A tap on a place the table shows - a destination card, an empty Bench position - has said
    // everything a move needs, so the move it settles on happens.
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
        if (forced)
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
        _benchDestination = false;
        _hadDestinationStep = false;
        _choiceValidation = null;
        _autoStarted = false;
        _choiceStep = -1;
        _drafts.Clear();
    }
}
