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
            // A forced decision whose every candidate glows on the table is asked by the table
            // itself: a sheet that lists nothing would only restate what the auras already show.
            return forced.Length == 0 || ForcedByAura(forced)
                ? null
                : new(
                    MatchSheetMode.Forced,
                    ForcedHeading(forced[0].Kind),
                    "Required",
                    "Choose how to resolve this.",
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
                "Choose a destination",
                null,
                null,
                "Cancel",
                false,
                _directActions
            ),
            Stage.Actions => new(
                MatchSheetMode.Actions,
                $"{OriginName(frame)}: choose a move.",
                "Available moves",
                null,
                null,
                "Cancel",
                false,
                _menu
            ),
            Stage.Choice when CurrentRequirement() is { } requirement => new(
                MatchSheetMode.Choice,
                PublicText(requirement.Label),
                PublicText(_pending!.Label),
                ChoiceProgress(requirement),
                EffectDescription(_pending),
                "Back",
                _forcedKinds.Contains(_pending!.Kind),
                []
            ),
            Stage.Confirm when _pending is not null => new(
                MatchSheetMode.Confirm,
                ConfirmationHeading(_pending),
                "Confirm",
                ChoiceSummary(_pending),
                EffectDescription(_pending),
                "Cancel",
                _forcedKinds.Contains(_pending.Kind),
                []
            ),
            _ => null,
        };
    }

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

    private static string ForcedHeading(MatchActionKindView kind) =>
        kind switch
        {
            MatchActionKindView.ChooseMulliganBonus => "Take your bonus cards.",
            MatchActionKindView.ChooseOpening => "Choose your Active Blokemon.",
            MatchActionKindView.ChooseReplacement => "Choose a new Active Blokemon.",
            MatchActionKindView.ResolveKnockout => "Resolve the Knock Out.",
            MatchActionKindView.TakePrize => "Take your Prize card.",
            _ => "Make the required choice.",
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

    private string? ChoiceSummary(MatchActionView action)
    {
        var frame = DisplayFrame();
        var parts = new List<string>();
        foreach (var requirement in LocalRequirements(action).Where(ChoiceHasStep))
        {
            var draft = Draft(requirement);
            var value = requirement.Kind switch
            {
                MatchChoiceKindView.Optional => draft.Accepted ? "Yes" : "No",
                MatchChoiceKindView.Amount => draft.Amount.ToString(),
                MatchChoiceKindView.Cards => draft.Cards.Count == 0
                    ? "None"
                    : string.Join(
                        ", ",
                        draft.Cards.Select(card => CardName(frame, requirement, card))
                    ),
                MatchChoiceKindView.MechanicalType => requirement
                    .EligibleMechanicalTypes.FirstOrDefault(option =>
                        option.Value == draft.MechanicalType
                    )
                    ?.Label
                    ?? "—",
                MatchChoiceKindView.Attack => requirement
                    .EligibleEffects.FirstOrDefault(option => option.Id == draft.EffectId)
                    ?.Label
                    ?? "—",
                MatchChoiceKindView.Distribution => string.Join(
                    ", ",
                    draft
                        .Distribution.Where(static item => item.Value > 0)
                        .Select(item => $"{CardName(frame, requirement, item.Key)} {item.Value}")
                ),
                MatchChoiceKindView.Attachments => string.Join(
                    ", ",
                    draft.Attachments.Select(item =>
                        $"{CardName(frame, requirement, item.Key)} → {CardName(frame, requirement, item.Value)}"
                    )
                ),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{PublicText(requirement.Label)}: {value}");
            }
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
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

    private string ConfirmationHeading(MatchActionView action) =>
        action.Kind switch
        {
            MatchActionKindView.Attack => $"Use {SimpleActionName(action.Label)}?",
            MatchActionKindView.EndTurn => "End your turn?",
            MatchActionKindView.Resign => "Resign and lose this battle?",
            _ => $"{PublicText(action.Label)}?",
        };

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
