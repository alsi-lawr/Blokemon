using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// A decision the match poses - a pending effect choice - carries the engine's own name for itself
// (MatchCardProjection.actionLabel). That name is machinery, not something a player can act on, so
// no surface may print it.
//
// Two surfaces can reach it. The decision is taken the moment it is offered, so what the player
// normally sees is the step of the question inside it, whose eyebrow names the move being answered
// - and a posed decision is a move nobody chose, so there is no move there to name. The other is
// what is left when the answer comes back refused and the decision has nowhere on the table to
// hold. Both are pinned here, and each is pinned against an arbitrary label rather than against
// any one sentence, so renaming the decision cannot open a route back to the surface.
public sealed class MatchPosedDecisionCopyTests
{
    private const string EngineLabel = "ENGINE MACHINERY";

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
            .PosedHeading(PosedDecision("SOMETHING ELSE ENTIRELY", "Use this effect?"))
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
        foreach (var label in new[] { EngineLabel, "Resolve effect choice", "Choosing" })
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

    [Test]
    public void ChoiceStep_NamesTheMoveItAnswersAndNamesNoMoveForADecisionNobodyChose()
    {
        // The step is placed by the move it is answering - "Choose 2 cards" under "Play Talent
        // Scout" - and that is worth keeping. A decision the match posed is a move nobody chose,
        // so there is nothing to place it against and the engine's word for it is not a
        // substitute: whatever the decision is called, the step says nothing there.
        var chosen = new MatchActionView(
            "command-1",
            MatchActionKindView.PlayTrainer,
            "Play a Kit",
            true,
            null,
            null,
            null,
            [Requirement(Question)],
            null
        );

        Step(chosen).Eyebrow.ShouldBe("Play a Kit");
        Step(PosedDecision(EngineLabel, Question)).Eyebrow.ShouldBeNull();
        Step(PosedDecision("Resolve effect choice", Question)).Eyebrow.ShouldBeNull();
    }

    private static MatchSheetView Step(MatchActionView action) =>
        MatchSheetView.ChoiceStep(
            Table,
            action,
            action.ChoiceRequirements[0],
            posed: action.Kind == MatchActionKindView.ResolveChoice,
            null,
            null,
            null
        );

    // A table with nothing on it: what the step says about itself does not depend on where the
    // cards it asks for happen to be.
    private static readonly MatchFrameView Table = new(
        Guid.Parse("70000000-0000-0000-0000-000000000001"),
        1,
        1,
        MatchPhaseView.AwaitingEffectChoice,
        new("The Regular", "Deck", 20, 0, 6, null, [], [], [], [], false),
        new("You", "Deck", 20, 0, 6, null, [], [], [], [], true),
        false,
        null
    );

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
