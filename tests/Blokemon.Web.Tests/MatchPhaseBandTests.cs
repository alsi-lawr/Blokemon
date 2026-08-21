using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// The band across the middle of the table, and the surface that used to cover the Bench.
//
// Two reports, one surface. A player picking their starting Blokemon was given a table that looked
// exactly like an ordinary turn, because affordances can show what may be touched and never which
// situation the player is in. And a player being asked to choose a target had the targets hidden
// behind a panel pinned to the middle of the table telling them to choose.
//
// Everything below is asked of match state - the phase, whose turn it is, whose cards are where -
// and never of the screen. Nothing here matches a rendered sentence: the guarantees are that
// situations the player must tell apart come out different, and that a question asked OF the table
// is never printed OVER it. Reword any of it and these still hold; collapse two situations into
// one and they do not.
//
// That every phase is handled at all is not asked here and must not be. MatchBand.For has no last
// arm and CS8509 is an error, so a phase added without words decided for it fails the build - a
// test looping the cases to check the same thing would only be asking the compiler's question
// again, later and worse.
public sealed class MatchPhaseBandTests
{
    [Test]
    public void TheOpeningIsNotPresentedAsAnOrdinaryTurn()
    {
        // The reported case. Both are your move to make and the affordances are the same shape, so
        // the band is the only thing that can separate them.
        var opening = MatchBand.For(Frame(MatchPhaseView.OpeningPlacement, yours: true));
        var ordinary = MatchBand.For(Frame(MatchPhaseView.Playing, yours: true));

        opening.ShouldNotBe(ordinary);
        Silent(opening).ShouldBeFalse();
    }

    [Test]
    public void AChoiceInProgressIsNotPresentedAsOrdinaryPlay()
    {
        // The state that used to be announced by covering the Bench. It has to read differently
        // from ordinary play on both halves of the table: yours, because you are being asked for
        // something; theirs, because a table that pauses in silence while they resolve is
        // indistinguishable from one that has hung.
        foreach (var yours in new[] { true, false })
        {
            MatchBand
                .For(Frame(MatchPhaseView.AwaitingEffectChoice, yours))
                .ShouldNotBe(MatchBand.For(Frame(MatchPhaseView.Playing, yours)));
        }
    }

    [Test]
    public void TheSameStructuralQuestionIsAskedInTheSameWordsWhereverItComesUp()
    {
        // Choosing an Active at the start of the game and choosing one after a Knock Out are the
        // same question asked at different moments, so they are asked identically and the concept
        // is learned once. An effect choice and a trigger choice differ to the engine and not to
        // the player: in both, someone is resolving something before play goes on.
        MatchBand
            .For(Frame(MatchPhaseView.OpeningPlacement, yours: true))
            .ShouldBe(MatchBand.For(Frame(MatchPhaseView.AwaitingReplacement, yours: true)));
        MatchBand
            .For(Frame(MatchPhaseView.AwaitingEffectChoice, yours: true))
            .ShouldBe(MatchBand.For(Frame(MatchPhaseView.AwaitingTriggerChoice, yours: true)));
    }

    [Test]
    public void AFinishedMatchSaysWhichSideWon()
    {
        var won = MatchBand.For(Complete(winner: "You"));
        var lost = MatchBand.For(Complete(winner: "The Regular"));

        won.ShouldNotBe(lost);
        Silent(won).ShouldBeFalse();
        Silent(lost).ShouldBeFalse();

        // A finished match with no winner recorded still says something. The band holds a
        // permanent place in the layout, so a state it has nothing to say about is a blank strip
        // across the middle of the table rather than a surface that has gone away - and this is
        // the one branch of the band that no phase of a live game is guaranteed to reach.
        Silent(MatchBand.For(Complete(winner: null))).ShouldBeFalse();
    }

    [Test]
    public void AChoiceAnsweredByTappingTheTableIsNotPrintedOverTheTable()
    {
        // The obstruction. The decision the match posed asks for a card standing on the Bench, so
        // the surface asking it must not take the middle of the table: the card being asked for is
        // underneath it, and a phone table's middle IS the Bench.
        var frame = Frame(MatchPhaseView.AwaitingEffectChoice, yours: true);
        var onTheTable = Requirement(frame.Player.Bench[0]);

        MatchTable.AsksTheTable(frame, onTheTable).ShouldBeTrue();
        Posed(frame, onTheTable).Centred.ShouldBeFalse();
    }

    [Test]
    public void AChoiceTheTableCannotShowKeepsTheSurfaceThatHoldsIt()
    {
        // The other half of the same rule, so the fix is not simply "never centre anything": a
        // search of the Deck has no card on the table to tap, so the surface holding those cards
        // is where the question is asked and is allowed the middle.
        var frame = Frame(MatchPhaseView.AwaitingEffectChoice, yours: true);
        var offTheTable = Requirement(Instance("buried-in-the-deck"));

        MatchTable.AsksTheTable(frame, offTheTable).ShouldBeFalse();
        Posed(frame, offTheTable).Centred.ShouldBeTrue();
    }

    [Test]
    public void APlayerDrivenChoiceIsNeverCentredWhicheverCardsItAsksFor()
    {
        // Only a decision the match posed can take the middle. A step of a move the player chose
        // sits beside the table as it always has, so this change moved one surface rather than
        // rearranging every sheet in the game.
        var frame = Frame(MatchPhaseView.Playing, yours: true);

        foreach (
            var requirement in new[]
            {
                Requirement(frame.Player.Bench[0]),
                Requirement(Instance("buried")),
            }
        )
        {
            MatchSheetView
                .ChoiceStep(
                    frame,
                    Chosen(requirement),
                    requirement,
                    posed: false,
                    null,
                    null,
                    "Back"
                )
                .Centred.ShouldBeFalse();
        }
    }

    // Whether the band has anything at all to say.
    private static bool Silent(MatchBand band) =>
        string.IsNullOrWhiteSpace(band.Turn) && string.IsNullOrWhiteSpace(band.Phase);

    private static MatchSheetView Posed(
        MatchFrameView frame,
        MatchChoiceRequirementView requirement
    ) =>
        MatchSheetView.ChoiceStep(
            frame,
            Decision(MatchActionKindView.ResolveChoice, requirement),
            requirement,
            posed: true,
            null,
            null,
            null
        );

    private static MatchActionView Chosen(MatchChoiceRequirementView requirement) =>
        Decision(MatchActionKindView.PlayTrainer, requirement);

    private static MatchActionView Decision(
        MatchActionKindView kind,
        MatchChoiceRequirementView requirement
    ) => new("command-1", kind, "a move", true, null, null, null, [requirement], null);

    private static MatchChoiceRequirementView Requirement(MatchCardInstanceView candidate) =>
        new(
            "command-1:root/0:cards",
            MatchChoiceKindView.Cards,
            "a question",
            new("first", "You", true),
            1,
            1,
            [candidate],
            [],
            [],
            null,
            [],
            false,
            []
        );

    private static MatchFrameView Complete(string? winner) =>
        Frame(MatchPhaseView.Complete, yours: false, complete: true, winner: winner);

    // One table, in whichever phase and with the match waiting on whichever half. Both sides hold
    // an Active and a benched card throughout, so what separates two bands is never what is
    // standing on the table.
    private static MatchFrameView Frame(
        MatchPhaseView phase,
        bool yours,
        string opponent = "The Regular",
        bool complete = false,
        string? winner = null
    ) =>
        new(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            4,
            3,
            phase,
            Side(opponent, hasTurn: !yours),
            Side("You", hasTurn: yours),
            complete,
            winner
        );

    private static MatchSideView Side(string name, bool hasTurn) =>
        new(
            name,
            "Deck",
            20,
            1,
            6,
            Instance($"{name}-active"),
            [Instance($"{name}-bench")],
            [Instance($"{name}-held")],
            [],
            [],
            hasTurn
        );

    private static MatchCardInstanceView Instance(string cardInstanceId) =>
        new(
            cardInstanceId,
            new(
                cardInstanceId,
                cardInstanceId,
                CardKindView.Blokemon,
                "Bloke",
                string.Empty,
                string.Empty,
                [],
                0,
                false
            ),
            "You",
            "Field",
            10,
            90,
            [],
            [],
            [],
            []
        );
}
