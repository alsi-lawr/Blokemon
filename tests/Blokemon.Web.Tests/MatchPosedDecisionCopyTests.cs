using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// A decision the match poses - a pending effect choice - carries the engine's own name for
// itself: Blokemon.App prints "Make the required choice" for MatchAction.ResolveEffectChoice
// (MatchCardProjection.actionLabel). That name is machinery, not something a player can act on,
// so no surface may print it. The decision is normally taken the moment it is offered and the
// player only ever sees the question inside it; the one surface that could have printed the name
// is what is left when the answer comes back refused, which is what these pin.
public sealed class MatchPosedDecisionCopyTests
{
    private const string EngineLabel = "Make the required choice";

    private const string Question = "Choose 2 cards";

    [Test]
    public void PosedHeading_AsksTheQuestionRatherThanNamingTheDecision()
    {
        MatchText.PosedHeading(PosedDecision(EngineLabel, Question)).ShouldBe(Question);
    }

    [Test]
    public void PosedHeading_NeverPrintsTheDecisionsOwnLabel()
    {
        // Structural rather than a match on one string: whatever the engine calls a decision,
        // the heading comes from the question it carries.
        MatchText
            .PosedHeading(PosedDecision("ENGINE MACHINERY", "Use this effect?"))
            .ShouldBe("Use this effect?");

        // A decision carrying no question at all falls back to minimal status, never the label.
        MatchText.PosedHeading(PosedDecision(EngineLabel)).ShouldBe("Required");
    }

    [Test]
    public void PosedRetry_AsksTheQuestionAndListsNothing()
    {
        var decision = PosedDecision(EngineLabel, Question);

        var sheet = MatchSheetView.PosedRetry(decision);

        sheet.Mode.ShouldBe(MatchSheetMode.Confirm);
        sheet.Heading.ShouldBe(Question);
        // No option list, so the decision's own label has nowhere to be printed a second time,
        // and no way to dismiss something the match is still waiting on.
        sheet.Options.ShouldBeEmpty();
        sheet.CancelLabel.ShouldBeNull();
        sheet.Centred.ShouldBeTrue();
    }

    [Test]
    public void PosedRetry_PrintsNothingTheEngineNamedTheDecision()
    {
        foreach (var label in new[] { EngineLabel, "ENGINE MACHINERY", "Resolve effect choice" })
        {
            var sheet = MatchSheetView.PosedRetry(PosedDecision(label, Question));

            foreach (
                var printed in new[]
                {
                    sheet.Heading,
                    sheet.Eyebrow,
                    sheet.Detail,
                    sheet.Effect,
                    sheet.CancelLabel,
                }
            )
            {
                (printed ?? string.Empty).ShouldNotContain(label, Case.Insensitive);
            }

            sheet
                .Options.Select(static option => option.Label)
                .ShouldNotContain(option =>
                    option.Contains(label, StringComparison.OrdinalIgnoreCase)
                );
        }
    }

    private static MatchActionView PosedDecision(string label, params string[] questions) =>
        new(
            "choice:command-1",
            MatchActionKindView.ResolveChoice,
            label,
            true,
            null,
            null,
            null,
            [.. questions.Select(Requirement)],
            null
        );

    private static MatchChoiceRequirementView Requirement(string label) =>
        new(
            "choice:command-1:root/0:cards",
            MatchChoiceKindView.Cards,
            label,
            new("first", "You", true),
            1,
            2,
            [],
            [],
            [],
            null,
            [],
            false,
            []
        );
}
