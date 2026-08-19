using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// The stylesheet describes what a card does when a cue is about it, and finds the card by the
// mark the presenter put on it. A rule written for a mark nobody applies is not a broken
// animation - it is an animation that has never run, and it reads in the stylesheet exactly like
// one that has: the hand carried no cue marking at all, so the card that crosses the table on a
// draw and the card that leaves the hand on a play were both described and neither ever moved.
// These hold the marking itself, which is the half that cannot be seen by reading the CSS.
public sealed class MatchCueMarkingTests
{
    [Test]
    public void AHeldCardCarriesTheCueThatIsAboutItAsWellAsItsOwnGlow()
    {
        var drawn = MatchCueMarking.HandCard(
            "drawn",
            Auras("drawn"),
            Cue(MatchAnimationKindView.Draw, targets: ["drawn"]),
            MatchPresentationOverlay.Empty
        );
        var played = MatchCueMarking.HandCard(
            "played",
            Auras(),
            Cue(MatchAnimationKindView.Play, source: "played"),
            MatchPresentationOverlay.Empty
        );

        drawn.ShouldBe("hand-card is-aura is-cue-target");
        played.ShouldBe("hand-card is-cue-source");
        MatchCueMarking
            .HandCard("held", Auras(), null, MatchPresentationOverlay.Empty)
            .ShouldBe("hand-card");

        // And a card the presentation has already carried out of the hand is marked gone from it,
        // whatever cue happens to be on screen by then.
        MatchCueMarking
            .HandCard(
                "played",
                Auras(),
                Cue(MatchAnimationKindView.Reveal),
                MatchPresentationOverlay.Empty.Gone(["played"])
            )
            .ShouldBe("hand-card is-cue-gone");
    }

    [Test]
    public void TheCardACueIsAboutIsMarkedForTheRuleWrittenAboutIt()
    {
        MatchCueMarking
            .For(Cue(MatchAnimationKindView.Draw, targets: ["drawn"]), "drawn")
            .ShouldBe("is-cue-target");
        MatchCueMarking
            .For(Cue(MatchAnimationKindView.Play, source: "played"), "played")
            .ShouldBe("is-cue-source");
        MatchCueMarking
            .For(Cue(MatchAnimationKindView.Evolve, source: "both", targets: ["both"]), "both")
            .ShouldBe("is-cue-source is-cue-target");
    }

    [Test]
    public void EveryOtherCardIsLeftAlone()
    {
        MatchCueMarking
            .For(Cue(MatchAnimationKindView.Draw, targets: ["drawn"]), "held")
            .ShouldBeNull();
        MatchCueMarking.For(null, "held").ShouldBeNull();
    }

    [Test]
    public void TheArrivingCardIsTheNewestBackInAStripThatHasRoomForIt()
    {
        var draw = Cue(MatchAnimationKindView.Draw);

        MatchCueMarking.Arrives(draw, index: 4, shown: 5).ShouldBeTrue();
        MatchCueMarking.Arrives(draw, index: 3, shown: 5).ShouldBeFalse();
        MatchCueMarking
            .Arrives(Cue(MatchAnimationKindView.Play), index: 4, shown: 5)
            .ShouldBeFalse();
    }

    [Test]
    public void AShufflingDeckIsDealtOutAsTwoPilesThatCrossOneCardAtATime()
    {
        // The two piles have to be written alternately: dealt as one side and then the other,
        // the order they are spaced by would cross a whole pile before the first card of the
        // other one moved, which is the two halves being pulled apart rather than a riffle.
        var cards = Enumerable
            .Range(0, MatchCueMarking.RiffleCards)
            .Select(MatchCueMarking.RiffleCard)
            .ToArray();

        cards.Count(card => card == "riffle-card is-left").ShouldBe(cards.Length / 2);
        cards.Count(card => card == "riffle-card is-right").ShouldBe(cards.Length / 2);
        cards[0].ShouldBe("riffle-card is-left");
        cards[1].ShouldBe("riffle-card is-right");
        cards[2].ShouldBe("riffle-card is-left");
    }

    [Test]
    public void EachCardOfAShufflingDeckCarriesItsPlaceInTheOrder()
    {
        MatchCueMarking.RiffleStyle(0).ShouldBe("--riffle-order: 0");
        MatchCueMarking.RiffleStyle(11).ShouldBe("--riffle-order: 11");
    }

    [Test]
    public void BothEndsOfABlowAreMarkedAndOnlyWhileThereIsOne()
    {
        var blow = MatchPresentationOverlay.Empty.Blow("attacker", ["target"]);

        MatchCueMarking.Blow(blow, "attacker").ShouldBe("is-cue-striking");
        MatchCueMarking.Blow(blow, "target").ShouldBe("is-cue-struck");
        MatchCueMarking.Blow(blow, "bystander").ShouldBeNull();

        // Damage nobody swung for is not a blow, so neither end of it is one.
        MatchCueMarking.Blow(MatchPresentationOverlay.Empty, "attacker").ShouldBeNull();
        MatchCueMarking.Blow(MatchPresentationOverlay.Empty, "target").ShouldBeNull();
    }

    [Test]
    public void ACardThatHurtsItselfIsThrowingTheBlowRatherThanTakingIt()
    {
        // A fumble damages the card that swung. It cannot be knocked back by the movement it is
        // in the middle of making, so it keeps the swing and takes no recoil - and this holds
        // even if something ever names it at both ends at once.
        MatchCueMarking
            .Blow(MatchPresentationOverlay.Empty.Blow("attacker", ["attacker"]), "attacker")
            .ShouldBe("is-cue-striking");
    }

    [Test]
    public void OnlyThePlaceExpectingTheCardTakesTheLanding()
    {
        var bench = new MatchLandingSlot(false, MatchLandingKind.Bench, 2);

        MatchCueMarking
            .LandingClass(bench, MatchLandingKind.Bench, 2)
            .ShouldBe("is-cue-landing is-landing-centre");
        MatchCueMarking.LandingClass(bench, MatchLandingKind.Bench, 1).ShouldBeNull();
        MatchCueMarking.LandingClass(bench, MatchLandingKind.Active, 0).ShouldBeNull();
        MatchCueMarking.LandingClass(null, MatchLandingKind.Bench, 2).ShouldBeNull();
    }

    [Test]
    public void TheActiveCardLandsAtItsOwnEndOfTheTable()
    {
        // The Active card leans away from the middle, so where it comes to rest inside its slot
        // is not the middle of the slot: which end depends on whose side of the table it is.
        MatchCueMarking
            .LandingClass(new(false, MatchLandingKind.Active, 0), MatchLandingKind.Active, 0)
            .ShouldBe("is-cue-landing is-landing-top");
        MatchCueMarking
            .LandingClass(new(true, MatchLandingKind.Active, 0), MatchLandingKind.Active, 0)
            .ShouldBe("is-cue-landing is-landing-bottom");
    }

    private static MatchAuraView Auras(params string[] cards) =>
        new(cards, [], false, false, new Dictionary<string, int>(StringComparer.Ordinal));

    private static MatchEventCueView Cue(
        MatchAnimationKindView kind,
        string? source = null,
        string[]? targets = null
    ) => new(1, kind, "cue", source, targets ?? [], 0, null, true, []);
}
