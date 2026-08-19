using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// A command is applied whole: the engine commits one state at the end of it and the application
// sends one frame per command, so an attack that damages a Blokemon and then hands the turn over
// arrives as a single frame in which both have already happened. Played against that frame alone,
// the blow lands silently and the damage appears later, at the frame change - which is the turn
// banner, and which is what made the table read as broken.
//
// The timeline is what fixes it: the damage is carried as a delta against the frame still on
// screen and is true from the moment its own cue plays. These pin the order things become visible
// in and nothing about how long any of it takes: durations are tunables and no test may hold
// them still.
public sealed class MatchPresentationTimelineTests
{
    private const string Attacker = "card-attacker";

    private const string Defender = "card-defender";

    [Test]
    public void Damage_IsVisibleWhileItsOwnCuePlaysAndBeforeTheTurnChanges()
    {
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, amount: 30),
                Cue(2, MatchAnimationKindView.Damage, amount: 30),
                Cue(3, MatchAnimationKindView.Turn)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        var damage = beats.Select(beat => Shown(beat, Defender)).ToArray();
        var kinds = beats.Select(beat => beat.Cue?.Kind).ToArray();

        // The blow is announced, lands, and only then does the turn change: the damage is on the
        // card for the whole of its own cue and every cue after it.
        kinds.ShouldBe([
            MatchAnimationKindView.Attack,
            MatchAnimationKindView.Damage,
            MatchAnimationKindView.Turn,
            null,
        ]);
        damage.ShouldBe([0, 30, 30, 30]);
    }

    [Test]
    public void DamageIsShownAgainstTheFrameOnScreen_NotTheOneTheCommandEndsOn()
    {
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Damage, amount: 30),
                Cue(2, MatchAnimationKindView.Turn)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        // The table is still the one from before the command while the cues play - it is the
        // damage that has moved ahead of it, not the frame - and it settles on the command's own
        // frame once they are done, with the delta spent rather than counted twice.
        beats.Select(beat => beat.Frame.Player.HasTurn).ShouldBe([true, true, false]);
        beats[^1].Overlay.DamageDeltas.ShouldBeEmpty();
        Shown(beats[^1], Defender).ShouldBe(30);
    }

    [Test]
    public void SuccessiveBlowsAreCountedAsTheyLand()
    {
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 50, playerHasTurn: true),
                Cue(1, MatchAnimationKindView.Damage, amount: 20),
                Cue(2, MatchAnimationKindView.Damage, amount: 30)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats.Select(beat => Shown(beat, Defender)).ShouldBe([20, 50, 50]);
    }

    [Test]
    public void HealingIsTakenOffTheSameWay()
    {
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 10, playerHasTurn: true),
                Cue(1, MatchAnimationKindView.Heal, amount: 20)
            ),
            Frame(defenderDamage: 30, playerHasTurn: true)
        );

        beats.Select(beat => Shown(beat, Defender)).ShouldBe([10, 10]);
    }

    [Test]
    public void ADrawTakesItsFrameEarlySoTheCardIsThereToBeDealt()
    {
        var drawn = Frame(defenderDamage: 0, playerHasTurn: true) with
        {
            Player = Side("You", hasTurn: true, hand: [Instance("card-drawn", 0)]),
        };

        var beats = MatchPresentationTimeline.Beats(
            Presentation(drawn, Cue(1, MatchAnimationKindView.Draw, targets: ["card-drawn"])),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats[0].Frame.Player.Hand.Single().Id.ShouldBe("card-drawn");
    }

    [Test]
    public void APlayedCardIsAimedAtThePlaceItEndsUpStandingIn()
    {
        var played = Frame(defenderDamage: 0, playerHasTurn: true) with
        {
            Player = Side(
                "You",
                hasTurn: true,
                active: Instance(Attacker, 0),
                bench: [Instance("card-benched", 0), Instance("card-played", 0)]
            ),
        };

        var beats = MatchPresentationTimeline.Beats(
            Presentation(played, Cue(1, MatchAnimationKindView.Play, source: "card-played")),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats[0].Overlay.Landing.ShouldBe(new MatchLandingSlot(false, MatchLandingKind.Bench, 1));
        beats[0].Overlay.LandingFor(opponent: true).ShouldBeNull();
        // The landing belongs to the cue that is travelling, and to nothing after it.
        beats[^1].Overlay.Landing.ShouldBeNull();
    }

    [Test]
    public void ACardWithNowhereOnTheTableToLandIsSimplyShown()
    {
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 0, playerHasTurn: true),
                Cue(1, MatchAnimationKindView.Play, source: "card-discarded")
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats[0].Overlay.Landing.ShouldBeNull();
    }

    private static int Shown(MatchPresentationBeat beat, string cardInstanceId) =>
        beat.Overlay.Damage(
            beat.Frame.Opponent.Active?.Id == cardInstanceId
                ? beat.Frame.Opponent.Active
                : beat.Frame.Player.Active!
        );

    [Test]
    public void TheCardThrowingABlowStaysNamedUntilTheTableSettles()
    {
        // The blow is one movement and the cues it spans are several, so the card throwing it has
        // to stay named across all of them or the movement is taken off it part way through. It
        // is done with when the step is: the table settles on what the command did, and nobody is
        // mid-swing any more.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, amount: 30, source: Attacker),
                Cue(2, MatchAnimationKindView.Damage, amount: 30, source: Attacker),
                Cue(3, MatchAnimationKindView.Turn)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats
            .Select(beat => beat.Overlay.StrikingCardInstanceId)
            .ShouldBe([Attacker, Attacker, Attacker, null]);
    }

    [Test]
    public void ABlowSurvivesWhateverTheEngineDoesBetweenDeclaringItAndLandingIt()
    {
        // An attack that has to toss a beer mat to find out whether it connects puts a Coin cue
        // between the declaration and the damage - always, because the toss happens inside the
        // program and every bit of damage is placed at the end of it. If that clears the mark the
        // movement is taken off the card half way through, which reads as a glitch rather than as
        // a blow, and nothing is left to be knocked back either.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, amount: 30, source: Attacker),
                Cue(2, MatchAnimationKindView.Coin, targets: []),
                Cue(3, MatchAnimationKindView.Damage, amount: 30, source: Attacker),
                Cue(4, MatchAnimationKindView.Turn)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats
            .Select(beat => beat.Overlay.StrikingCardInstanceId)
            .ShouldBe([Attacker, Attacker, Attacker, Attacker, null]);
        beats
            .Select(beat => beat.Overlay.IsStruck(Defender))
            .ShouldBe([true, true, true, true, false]);
    }

    [Test]
    public void ABlowIsAimedAtWhatItDamagesRatherThanAtWhoeverIsStandingOpposite()
    {
        // An attack that reaches past the Active card and hits the Bench is aimed at the Bench,
        // and one that catches several cards is aimed at all of them, so the swing crosses them
        // rather than one of them and past the rest. Both are known from the declaration, because
        // the whole step is laid out before any of it is drawn.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, source: Attacker, targets: []),
                Cue(
                    2,
                    MatchAnimationKindView.Damage,
                    amount: 30,
                    source: Attacker,
                    targets: ["bench-one", "bench-two"]
                )
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats[0].Overlay.StruckCardInstanceIds.ShouldBe(["bench-one", "bench-two"]);
        beats[0].Overlay.IsStruck(Defender).ShouldBeFalse();
    }

    [Test]
    public void ACardThatOnlyHurtsItselfIsAimedAtNothing()
    {
        // The Muddled fumble: the attacker damages itself instead of what it swung at. It is
        // throwing the blow, so it is not also what the blow struck - which leaves the blow with
        // nothing to aim at, and it turns where it stands instead of crossing the table at nobody.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 0, playerHasTurn: true),
                Cue(1, MatchAnimationKindView.Attack, source: Attacker, targets: []),
                Cue(
                    2,
                    MatchAnimationKindView.Damage,
                    amount: 20,
                    source: Attacker,
                    targets: [Attacker]
                )
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats[0].Overlay.StrikingCardInstanceId.ShouldBe(Attacker);
        beats[0].Overlay.StruckCardInstanceIds.ShouldBeEmpty();
        beats[0].Overlay.IsStruck(Attacker).ShouldBeFalse();
    }

    [Test]
    public void DamageNobodySwungForNamesNobody()
    {
        // A Kit doing its work damages a Blokemon without anything having lunged at it. Nothing
        // is marked, so the plain cue presentation is what plays and no blow is drawn.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: true),
                Cue(1, MatchAnimationKindView.Play, source: "card-kit", targets: []),
                Cue(2, MatchAnimationKindView.Damage, amount: 30, source: "card-kit")
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats.Select(beat => beat.Overlay.StrikingCardInstanceId).ShouldBe([null, null, null]);
    }

    private static MatchPresentationView Presentation(
        MatchFrameView frame,
        params MatchEventCueView[] cues
    ) => new([new(frame, cues)]);

    private static MatchEventCueView Cue(
        long sequence,
        MatchAnimationKindView kind,
        int amount = 0,
        string? source = null,
        string[]? targets = null
    ) => new(sequence, kind, "cue", source, targets ?? [Defender], amount, null, true, []);

    private static MatchFrameView Frame(int defenderDamage, bool playerHasTurn) =>
        new(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            1,
            3,
            "InProgress",
            Side("Opponent", !playerHasTurn, active: Instance(Defender, defenderDamage)),
            Side("You", playerHasTurn, active: Instance(Attacker, 0)),
            false,
            null
        );

    private static MatchSideView Side(
        string name,
        bool hasTurn,
        MatchCardInstanceView? active = null,
        MatchCardInstanceView[]? bench = null,
        MatchCardInstanceView[]? hand = null
    ) => new(name, "Deck", 40, hand?.Length ?? 0, 6, active, bench ?? [], hand ?? [], [], hasTurn);

    private static MatchCardInstanceView Instance(string id, int damage) =>
        new(id, Card(id), "You", "Field", damage, 90, [], [], [], []);

    private static CardView Card(string id) =>
        new(id, id, CardKindView.Blokemon, "Bloke", "", "", [], 0, false);
}
