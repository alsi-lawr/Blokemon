using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Pages;

// ---- Tap routing ----------------------------------------------------------------------
//
// Where a tap goes is decided here and nowhere else. The presenters report that a card, the
// Bench or the table itself was pressed; the stage says what that means.
public partial class Match
{
    private void TapCard(string cardInstanceId)
    {
        if (_view?.Match is not { } match || Busy())
        {
            return;
        }

        _selectedCardInstanceId = cardInstanceId;
        _operationError = null;

        var forced = ForcedDecision(match);
        switch (_stage)
        {
            case Stage.Choice when CurrentRequirement() is { } requirement:
                TapChoiceCard(requirement, cardInstanceId);
                return;

            case Stage.Idle when forced.Length > 0:
                if (
                    ForcedByAura(forced)
                    && forced.FirstOrDefault(option =>
                        option.SourceCardInstanceId == cardInstanceId
                    )
                        is { } candidate
                )
                {
                    StartAction(candidate);
                }
                return;

            case Stage.Destination
                when DestinationCardIds().Contains(cardInstanceId, StringComparer.Ordinal):
                _destinationCardInstanceId = cardInstanceId;
                _benchDestination = false;
                OpenMenu([
                    .. OriginActions()
                        .Where(action => action.TargetCardInstanceId == cardInstanceId),
                ]);
                return;

            case Stage.Idle
                when PlayableCardIds(match).Contains(cardInstanceId, StringComparer.Ordinal):
                SelectOrigin(cardInstanceId);
                return;

            case Stage.Destination
                when PlayableCardIds(match).Contains(cardInstanceId, StringComparer.Ordinal):
                // Tapping another playable card while choosing a destination switches origin.
                CancelFlow();
                SelectOrigin(cardInstanceId);
                return;

            default:
                return;
        }
    }

    private void TapBench()
    {
        if (_stage != Stage.Destination || _originCardInstanceId is null || Busy())
        {
            return;
        }

        _destinationCardInstanceId = null;
        OpenMenu([
            .. OriginActions()
                .Where(static action =>
                    action.Kind == MatchActionKindView.PlayBlokemon
                    && action.TargetCardInstanceId is null
                ),
        ]);
    }

    private void TapBackground()
    {
        if (_stage == Stage.Idle)
        {
            return;
        }

        CancelFlow();
    }

    private void SelectOrigin(string cardInstanceId)
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
            return;
        }

        _hadDestinationStep = false;
        OpenMenu(_directActions);
    }

    private void OpenMenu(MatchActionView[] actions)
    {
        _menu = actions;
        _directActions = [];
        if (actions.Length == 0)
        {
            CancelFlow();
            return;
        }

        if (actions.Length == 1)
        {
            // One available action needs no menu: the pop-up is its confirmation.
            StartAction(actions[0]);
            return;
        }

        _stage = Stage.Actions;
    }

    private void StartAction(MatchActionView action)
    {
        _pending = action;
        _choiceValidation = null;
        _attachmentCardInstanceId = null;
        _drafts.Clear();
        foreach (var requirement in LocalRequirements(action))
        {
            _drafts[requirement.Id] = new ChoiceDraft { Amount = requirement.Minimum };
        }

        _choiceStep = NextActiveStep(-1);
        _stage = _choiceStep < 0 ? Stage.Confirm : Stage.Choice;
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

        if (_stage == Stage.Confirm && _pending is not null)
        {
            var last = PreviousActiveStep(LocalRequirements(_pending).Length);
            if (last >= 0)
            {
                _choiceStep = last;
                _stage = Stage.Choice;
                return;
            }

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
        _choiceStep = -1;
        _drafts.Clear();
    }
}
