using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;

namespace Blokemon.Web.Client.Pages;

// ---- Choice steps --------------------------------------------------------------------
//
// A pending action's own questions, asked one at a time. The draft each answer goes into and
// the walk through the steps both live here; the sheet only reports which button was pressed.
public partial class Match
{
    private MatchChoiceRequirementView[] LocalRequirements(MatchActionView action) =>
        [
            .. action.ChoiceRequirements.Where(static requirement =>
                requirement.Chooser.IsLocalPlayer
            ),
        ];

    private MatchChoiceRequirementView? CurrentRequirement() =>
        _pending is { } pending
        && _choiceStep >= 0
        && LocalRequirements(pending) is { } requirements
        && _choiceStep < requirements.Length
            ? requirements[_choiceStep]
            : null;

    // What the open step has been answered so far, named for printing. It is projected on every
    // render from the live draft, so an answer given by a tap is on screen by the next one.
    private MatchChoiceView ChoiceFor(MatchChoiceRequirementView requirement)
    {
        var frame = DisplayFrame();
        var draft = Draft(requirement);
        return new(
            draft.Amount,
            draft.MechanicalType,
            draft.EffectId,
            [
                .. draft.Attachments.Select(item => new MatchAttachmentView(
                    item.Key,
                    CardName(frame, requirement, item.Key),
                    CardName(frame, requirement, item.Value)
                )),
            ]
        );
    }

    private int NextActiveStep(int from)
    {
        if (_pending is null)
        {
            return -1;
        }

        var requirements = LocalRequirements(_pending);
        for (var index = from + 1; index < requirements.Length; index++)
        {
            if (ChoiceHasStep(requirements[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private int PreviousActiveStep(int from)
    {
        if (_pending is null)
        {
            return -1;
        }

        var requirements = LocalRequirements(_pending);
        for (var index = Math.Min(from, requirements.Length) - 1; index >= 0; index--)
        {
            if (ChoiceHasStep(requirements[index]))
            {
                return index;
            }
        }

        return -1;
    }

    // Answering the last question is the move: there is nothing further to ask, so the answer is
    // played rather than shown back to the player to agree with.
    private Task AdvanceChoice()
    {
        if (!StepComplete())
        {
            return Task.CompletedTask;
        }

        _attachmentCardInstanceId = null;
        _choiceValidation = null;
        var next = NextActiveStep(_choiceStep);
        if (next < 0)
        {
            return CommitPending();
        }

        _choiceStep = next;
        return Task.CompletedTask;
    }

    private void TapChoiceCard(MatchChoiceRequirementView requirement, string cardInstanceId)
    {
        var draft = Draft(requirement);
        switch (requirement.Kind)
        {
            case MatchChoiceKindView.Cards:
                if (!Eligible(requirement, cardInstanceId))
                {
                    return;
                }
                if (draft.Cards.Contains(cardInstanceId))
                {
                    draft.Cards.Remove(cardInstanceId);
                }
                else if (draft.Cards.Count < requirement.Maximum)
                {
                    draft.Cards.Add(cardInstanceId);
                }
                _choiceValidation = null;
                return;

            case MatchChoiceKindView.Distribution:
                if (!Eligible(requirement, cardInstanceId))
                {
                    return;
                }
                // Repeated taps add counters and wrap back to none once the pool is spent.
                var placed = draft.Distribution.Values.Sum();
                var current = draft.Distribution.GetValueOrDefault(cardInstanceId);
                draft.Distribution[cardInstanceId] =
                    placed >= requirement.Maximum ? 0 : current + 1;
                _choiceValidation = null;
                return;

            case MatchChoiceKindView.Attachments:
                if (_attachmentCardInstanceId is { } energy)
                {
                    if (requirement.EligibleTargets.Any(card => card.Id == cardInstanceId))
                    {
                        draft.Attachments[energy] = cardInstanceId;
                        _attachmentCardInstanceId = null;
                    }
                    return;
                }
                if (requirement.EligibleCards.Any(card => card.Id == cardInstanceId))
                {
                    _attachmentCardInstanceId = cardInstanceId;
                }
                return;

            default:
                return;
        }
    }

    private static bool Eligible(MatchChoiceRequirementView requirement, string cardInstanceId) =>
        requirement.EligibleCards.Any(card => card.Id == cardInstanceId);

    private Task AnswerOptional(MatchChoiceRequirementView requirement, bool accepted)
    {
        Draft(requirement).Accepted = accepted;
        return AdvanceChoice();
    }

    private void StepAmount(MatchChoiceRequirementView requirement, int delta)
    {
        var draft = Draft(requirement);
        draft.Amount = Math.Clamp(draft.Amount + delta, requirement.Minimum, requirement.Maximum);
    }

    private Task PickMechanicalType(MatchChoiceRequirementView requirement, string value)
    {
        Draft(requirement).MechanicalType = value;
        return AdvanceChoice();
    }

    private Task PickEffect(MatchChoiceRequirementView requirement, string effectId)
    {
        Draft(requirement).EffectId = effectId;
        return AdvanceChoice();
    }

    private void ClearAttachment(MatchChoiceRequirementView requirement, string energy) =>
        Draft(requirement).Attachments.Remove(energy);

    private MatchCardInstanceView[] TrayCards(MatchChoiceRequirementView requirement)
    {
        var frame = DisplayFrame();
        var visible = AllVisibleCards(frame)
            .Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var candidates =
            requirement.Kind == MatchChoiceKindView.Attachments
            && _attachmentCardInstanceId is not null
                ? requirement.EligibleTargets
                : requirement.EligibleCards;
        // Cards the table cannot show - a Deck search, the Discard pile, a Prize card - glow in
        // the sheet instead, with the same aura and the same press behaviour.
        return [.. candidates.Where(card => !visible.Contains(card.Id))];
    }

    private bool StepComplete() =>
        CurrentRequirement() is { } requirement && ChoiceRequirementComplete(requirement);
}
