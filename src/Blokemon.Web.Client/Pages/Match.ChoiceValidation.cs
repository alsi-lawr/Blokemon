using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Pages;

// What counts as an answered question, what the step says about itself while it is being
// answered, and how the answers are turned into the request the engine is sent.
public partial class Match
{
    private bool TryBuildChoices(MatchActionView action, out MatchChoiceSelectionRequest[] choices)
    {
        var result = new List<MatchChoiceSelectionRequest>();
        foreach (var requirement in LocalRequirements(action))
        {
            if (!ChoiceIsActive(requirement))
            {
                continue;
            }
            var draft = Draft(requirement);
            if (
                requirement.Kind == MatchChoiceKindView.Cards
                && draft.Cards.Count is var cardCount
                && (cardCount < requirement.Minimum || cardCount > requirement.Maximum)
            )
            {
                return ChoiceFailure(
                    $"{CardChoiceInstruction(requirement.Minimum, requirement.Maximum)}.",
                    out choices
                );
            }
            if (
                requirement.Kind == MatchChoiceKindView.MechanicalType
                && string.IsNullOrEmpty(draft.MechanicalType)
            )
            {
                return ChoiceFailure("Choose an Energy type.", out choices);
            }
            if (
                requirement.Kind == MatchChoiceKindView.Attack
                && string.IsNullOrEmpty(draft.EffectId)
            )
            {
                return ChoiceFailure("Choose an attack.", out choices);
            }
            if (
                requirement.Kind == MatchChoiceKindView.Distribution
                && draft.Distribution.Values.Sum() != requirement.Maximum
            )
            {
                return ChoiceFailure(
                    $"Place {requirement.Maximum} damage {(requirement.Maximum == 1 ? "counter" : "counters")}.",
                    out choices
                );
            }
            if (
                requirement.Kind == MatchChoiceKindView.Attachments
                && draft.Attachments.Count is var attachmentCount
                && (attachmentCount < requirement.Minimum || attachmentCount > requirement.Maximum)
            )
            {
                return ChoiceFailure(
                    $"Choose targets for {requirement.Minimum} Energy {(requirement.Minimum == 1 ? "card" : "cards")}.",
                    out choices
                );
            }

            result.Add(
                new(
                    requirement.Id,
                    requirement.Kind,
                    requirement.Kind == MatchChoiceKindView.Optional ? draft.Accepted : null,
                    requirement.Kind == MatchChoiceKindView.Amount ? draft.Amount : null,
                    requirement.Kind == MatchChoiceKindView.Cards ? [.. draft.Cards] : [],
                    requirement.Kind == MatchChoiceKindView.MechanicalType
                        ? draft.MechanicalType
                        : null,
                    requirement.Kind == MatchChoiceKindView.Attack ? draft.EffectId : null,
                    requirement.Kind == MatchChoiceKindView.Distribution
                        ? draft
                            .Distribution.Where(static item => item.Value > 0)
                            .Select(static item => new MatchDamageAllocationRequest(
                                item.Key,
                                item.Value
                            ))
                            .ToArray()
                        : [],
                    requirement.Kind == MatchChoiceKindView.Attachments
                        ? draft
                            .Attachments.Select(static item => new MatchAttachmentRequest(
                                item.Key,
                                item.Value
                            ))
                            .ToArray()
                        : []
                )
            );
        }
        _choiceValidation = null;
        choices = [.. result];
        return true;
    }

    private bool ChoiceFailure(string message, out MatchChoiceSelectionRequest[] choices)
    {
        _choiceValidation = message;
        choices = [];
        return false;
    }

    private ChoiceDraft Draft(MatchChoiceRequirementView requirement) =>
        _drafts.TryGetValue(requirement.Id, out var draft)
            ? draft
            : _drafts[requirement.Id] = new ChoiceDraft { Amount = requirement.Minimum };

    // An active requirement is one the match still expects an answer for, so it is always
    // submitted even when the answer can only be empty.
    private bool ChoiceIsActive(MatchChoiceRequirementView requirement) =>
        requirement.DependsOnOptional is null
        || (_drafts.TryGetValue(requirement.DependsOnOptional, out var parent) && parent.Accepted);

    // A requirement that admits nothing but the empty answer is not a decision, so it gets no
    // step and no line in the summary: it would show a heading, no glowing cards, and a Continue
    // that means "there was nothing here".
    private bool ChoiceHasStep(MatchChoiceRequirementView requirement) =>
        ChoiceIsActive(requirement) && !NothingToDecide(requirement);

    private static bool NothingToDecide(MatchChoiceRequirementView requirement) =>
        requirement.Minimum == 0
        && requirement.Kind
            is MatchChoiceKindView.Cards
                or MatchChoiceKindView.Distribution
                or MatchChoiceKindView.Attachments
        && (
            requirement.Maximum == 0
            || (requirement.EligibleCards.Length == 0 && requirement.EligibleTargets.Length == 0)
        );

    private static string ChoiceInstruction(MatchChoiceRequirementView requirement)
    {
        if (requirement.Minimum == requirement.Maximum)
        {
            return requirement.Minimum == 1 ? "Choose 1" : $"Choose {requirement.Minimum}";
        }

        return requirement.Minimum == 0
            ? $"Choose up to {requirement.Maximum}"
            : $"Choose {requirement.Minimum} to {requirement.Maximum}";
    }

    // How far through a counted answer the player is. A step whose options are on screen as
    // buttons, or whose eligible cards are already glowing, says nothing at all: the count is
    // state the table cannot show by itself, and everything else would be narration.
    private string? ChoiceProgress(MatchChoiceRequirementView requirement)
    {
        var draft = Draft(requirement);
        return requirement.Kind switch
        {
            MatchChoiceKindView.Cards =>
                $"{ChoiceInstruction(requirement)} · {draft.Cards.Count}/{requirement.Maximum} selected",
            MatchChoiceKindView.Distribution =>
                $"Place {requirement.Maximum} · {draft.Distribution.Values.Sum()} placed",
            MatchChoiceKindView.Attachments => $"{draft.Attachments.Count}/{requirement.Maximum}",
            MatchChoiceKindView.Amount =>
                $"Between {requirement.Minimum} and {requirement.Maximum}",
            _ => null,
        };
    }

    private bool PlayerChoicesValid(MatchActionView action) =>
        LocalRequirements(action).Where(ChoiceIsActive).All(ChoiceRequirementComplete);

    // Every kind of choice says for itself when it has been made. The last arm here answered "not
    // yet" for any kind nobody had written down, which is the safe-looking half of the wrong
    // answer: a kind added later would leave its action permanently unconfirmable with no sign of
    // why. The suppression below is for the other half of the same warning: a value cast in from
    // outside the names the enum declares, which the requirement handed down from the engine
    // cannot be.
#pragma warning disable CS8524
    private bool ChoiceRequirementComplete(MatchChoiceRequirementView requirement)
    {
        var draft = Draft(requirement);
        return requirement.Kind switch
        {
            MatchChoiceKindView.Optional => true,
            MatchChoiceKindView.Amount => draft.Amount >= requirement.Minimum
                && draft.Amount <= requirement.Maximum,
            MatchChoiceKindView.Cards => draft.Cards.Count >= requirement.Minimum
                && draft.Cards.Count <= requirement.Maximum,
            MatchChoiceKindView.MechanicalType => !string.IsNullOrEmpty(draft.MechanicalType),
            MatchChoiceKindView.Attack => !string.IsNullOrEmpty(draft.EffectId),
            MatchChoiceKindView.Distribution => draft.Distribution.Values.Sum()
                == requirement.Maximum,
            MatchChoiceKindView.Attachments => draft.Attachments.Count >= requirement.Minimum
                && draft.Attachments.Count <= requirement.Maximum,
        };
    }
#pragma warning restore CS8524

    private static string CardChoiceInstruction(int minimum, int maximum)
    {
        if (minimum == maximum)
        {
            return $"Choose {minimum} {(minimum == 1 ? "card" : "cards")}";
        }

        return minimum == 0
            ? $"Choose up to {maximum} {(maximum == 1 ? "card" : "cards")}"
            : $"Choose {minimum} to {maximum} cards";
    }

    private sealed class ChoiceDraft
    {
        public bool Accepted { get; set; }
        public int Amount { get; set; }
        public HashSet<string> Cards { get; } = new(StringComparer.Ordinal);
        public string? MechanicalType { get; set; }
        public string? EffectId { get; set; }
        public Dictionary<string, int> Distribution { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Attachments { get; } = new(StringComparer.Ordinal);
    }
}
