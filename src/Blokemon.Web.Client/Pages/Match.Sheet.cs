using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using static Blokemon.Web.Client.Components.MatchText;

namespace Blokemon.Web.Client.Pages;

// ---- Sheet copy ----------------------------------------------------------------------
//
// Whether a sheet is open, and every word on it, is decided from the stage here. The sheet
// presenter is handed the finished view and prints it.
public partial class Match
{
    private MatchSheetView? SheetFor(MatchView match, MatchActionView[] forced)
    {
        var view = BuildSheet(match, forced);
        _sheetKey = view is null ? null : $"{view.Mode}:{view.Heading}:{view.Detail}";
        return view;
    }

    private MatchSheetView? BuildSheet(MatchView match, MatchActionView[] forced)
    {
        if (Busy())
        {
            return null;
        }

        var frame = DisplayFrame();
        if (_stage == Stage.Idle)
        {
            // A forced decision the table can put a place to - a card that glows, the Deck the
            // cards come from - is asked by the table itself, and one with a single possible
            // answer has already answered itself. Either way a sheet would only be in the way,
            // including when the answer it sent came back refused: the decision holds where it
            // is instead, so no surface here ever has to print the engine's word for it.
            return
                forced.Length == 0
                || ForcedByAura(forced)
                || ForcedByDeck(forced)
                || ForcedIsAutomatic(forced)
                ? null
                : new(
                    MatchSheetMode.Forced,
                    ForcedHeading(forced),
                    "Required",
                    null,
                    null,
                    null,
                    true,
                    forced
                );
        }

        return _stage switch
        {
            // Where a card goes is a question the table can answer by itself, so it is only asked
            // in words when tapping a place on the table would not settle it.
            Stage.Destination when DestinationIsUnambiguous() => null,
            Stage.Destination => new(
                MatchSheetMode.Destination,
                DestinationHeading(frame),
                null,
                null,
                null,
                "Cancel",
                false,
                _directActions
            ),
            Stage.Actions => new(
                MatchSheetMode.Actions,
                OriginName(frame),
                "Available moves",
                null,
                null,
                "Cancel",
                false,
                _menu
            ),
            Stage.Choice when CurrentRequirement() is { } requirement => MatchSheetView.ChoiceStep(
                frame,
                _pending!,
                requirement,
                _forcedKinds.Contains(_pending!.Kind),
                ChoiceProgress(requirement),
                EffectDescription(_pending),
                StepBackLabel()
            ),
            // A decision the match posed and then refused holds here, because the table has
            // nowhere to show it.
            Stage.Confirm when _pending is not null && _autoStarted => MatchSheetView.PosedRetry(
                _pending
            ),
            // The only moves that stop to be confirmed are the two that end something and belong
            // to the turn rather than to a place on the table.
            Stage.Confirm when _pending is not null => new(
                MatchSheetMode.Confirm,
                ConfirmationHeading(_pending),
                "Confirm",
                null,
                null,
                "Cancel",
                false,
                []
            ),
            _ => null,
        };
    }

    // The step in front of the last one continues; the last one plays the move, and says so with
    // the same word the move would have been confirmed with.
    private string? ChoiceAdvanceLabel() =>
        _pending is null ? null
        : NextActiveStep(_choiceStep) < 0 ? ConfirmationButton(_pending)
        : "Continue";

    // Going back is offered while there is something behind: an earlier step of the same
    // question, or the flow the player started. A question that opened itself has nothing behind
    // its first step, and a Back there would leave the table with no way on.
    private string? StepBackLabel() =>
        !_autoStarted || PreviousActiveStep(_choiceStep) >= 0 ? "Back" : null;

    // A phone can show the sheet or the hand at the bottom, not both: the hand comes forward
    // only while the cards still to be chosen are in it. The already-chosen origin does not
    // count, or selecting a card from hand would raise the hand over the board it points at.
    private static string? SheetClass(
        MatchFrameView frame,
        MatchAuraView auras,
        MatchSheetView? sheet
    ) =>
        sheet is not null
        && frame.Player.Hand.Any(card => auras.Cards.Contains(card.Id, StringComparer.Ordinal))
            ? "has-sheet hand-forward"
        : sheet is not null ? "has-sheet"
        : null;

    // What a listed decision is about. A kind with no name of its own is named by the question
    // it carries, never by the engine's word for itself.
    private static string ForcedHeading(MatchActionView[] forced) =>
        forced[0].Kind switch
        {
            MatchActionKindView.ChooseOpening => "Choose your Active Blokemon.",
            MatchActionKindView.ChooseBonusPlacement =>
                "Put any Basic Blokemon you just drew on your Bench.",
            MatchActionKindView.ChooseReplacement => "Choose a new Active Blokemon.",
            MatchActionKindView.ResolveKnockout => "Resolve the Knock Out.",
            MatchActionKindView.TakePrize => "Take your Prize card.",
            _ => PosedHeading(forced[0]),
        };

    // The question is about the destination, so it is the action that has one that names it: a
    // board card can offer a retreat alongside attacks that need no destination at all.
    private string DestinationHeading(MatchFrameView frame) =>
        OriginActions()
            .FirstOrDefault(static action => action.TargetCardInstanceId is not null)
            ?.Kind switch
        {
            MatchActionKindView.AttachEnergy or MatchActionKindView.PlayTrainer =>
                $"Attach {OriginName(frame)} to which Blokemon?",
            MatchActionKindView.Evolve => $"Evolve which Blokemon into {OriginName(frame)}?",
            _ => $"Where does {OriginName(frame)} go?",
        };

    private string OriginName(MatchFrameView frame) =>
        _originCardInstanceId is { } origin ? VisibleCardName(frame, origin) : "this card";

    private string? EffectDescription(MatchActionView? action)
    {
        if (action?.SourceCardInstanceId is not { } sourceId)
        {
            return null;
        }

        var source = AllVisibleCards(DisplayFrame())
            .FirstOrDefault(card => card.Id == sourceId)
            ?.Card;
        if (source is null)
        {
            return null;
        }

        // An attack or an ability names its own effect, so the printed text can be matched by
        // name. A Kit's text is the whole card. Moving a Blokemon prints nothing, and showing
        // its attack text there would describe something the player is not doing.
        var rule =
            action.EffectId is not null
                ? source.Rules.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Name,
                        SimpleActionName(action.Label),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            : action.Kind == MatchActionKindView.PlayTrainer
                ? source.Rules.FirstOrDefault(static candidate =>
                    candidate.Kind != CardRuleKindView.Energy
                )
            : null;
        return string.IsNullOrWhiteSpace(rule?.Text) ? null : rule.Text;
    }

    private string CardName(
        MatchFrameView frame,
        MatchChoiceRequirementView requirement,
        string cardInstanceId
    ) =>
        AllVisibleCards(frame).FirstOrDefault(card => card.Id == cardInstanceId)?.Card.Name
        ?? requirement
            .EligibleCards.Concat(requirement.EligibleTargets)
            .FirstOrDefault(card => card.Id == cardInstanceId)
            ?.Card.Name
        ?? "that card";

    private static string ConfirmationHeading(MatchActionView action) =>
        action.Kind switch
        {
            MatchActionKindView.EndTurn => "End your turn?",
            MatchActionKindView.Resign => "Resign and lose this battle?",
            _ => $"{PublicText(action.Label)}?",
        };

    // The word on the button that commits a move: the last step of a question ends with the move
    // itself, so it says what the move is rather than that there is more to come.
    private static string ConfirmationButton(MatchActionView action) =>
        action.Kind switch
        {
            MatchActionKindView.AttachEnergy => "Attach",
            MatchActionKindView.PlayTrainer when action.TargetCardInstanceId is not null =>
                "Attach",
            MatchActionKindView.Attack => "Attack",
            MatchActionKindView.Retreat => "Retreat",
            MatchActionKindView.EndTurn => "End turn",
            MatchActionKindView.Resign => "Resign",
            MatchActionKindView.ChooseOpening => "Start battle",
            MatchActionKindView.ChooseBonusPlacement => "Done",
            MatchActionKindView.ChooseReplacement => "Choose",
            MatchActionKindView.ResolveChoice
            or MatchActionKindView.ResolveKnockout
            or MatchActionKindView.TakePrize => "Choose",
            _ => "Play",
        };

    private static string VisibleCardName(MatchFrameView frame, string cardInstanceId) =>
        AllVisibleCards(frame).FirstOrDefault(card => card.Id == cardInstanceId)?.Card.Name
        ?? "this Blokemon";

    private static string SimpleActionName(string label) =>
        label.StartsWith("Attack with ", StringComparison.OrdinalIgnoreCase)
            ? label["Attack with ".Length..]
            : PublicText(label);
}
