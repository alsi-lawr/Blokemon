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
    public void ACounterAPartyTrickPutsOnItselfLandsWithItsOwnCueRatherThanTwiceAroundIt()
    {
        // The Lads draw a card by putting a damage counter on themselves, and the engine does it
        // in that order: draw, then counter. A draw takes its step's frame early so the card has
        // a hand to be dealt into - and that frame is the one the whole command ends on, counter
        // and all. So the Blokemon lost the HP before anything had said it would, the counter's
        // own cue then put a second one on top of a frame that already had it, and the table
        // settling took that one back off again: 10, 20, 10 for a single counter.
        var drewAndTookIt = Frame(defenderDamage: 0, playerHasTurn: true) with
        {
            Player = Side(
                "You",
                true,
                active: Instance(Attacker, 10),
                hand: [Instance("card-drawn", 0)]
            ),
        };

        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                drewAndTookIt,
                Cue(1, MatchAnimationKindView.Draw, targets: ["card-drawn"]),
                Cue(
                    2,
                    MatchAnimationKindView.Damage,
                    amount: 10,
                    source: Attacker,
                    targets: [Attacker]
                )
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        // Whole while the card is being dealt, the counter on for its own cue, and one counter.
        beats.Select(beat => Shown(beat, Attacker)).ShouldBe([0, 10, 10]);
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
        // An attack whose program does something else on its way to the damage - leaving the
        // defender in a rough state before it hits them - puts that cue between the declaration
        // and the damage. If it clears the mark the movement is taken off the card half way
        // through, which reads as a glitch rather than as a blow, and nothing is left to be
        // knocked back either.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, amount: 30, source: Attacker),
                Cue(2, MatchAnimationKindView.Condition),
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
    public void AnAttackIsAnnouncedAfterTheTossesThatDecideWhatItDid()
    {
        // The announcement carries how much damage the attack did, and an attack that tosses for
        // its damage does not know that until the tosses have landed. The engine declares first
        // and tosses after, so played in the order it arrives the table said "100 DAMAGE" and only
        // then began flipping beer mats to find out whether that was true.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                Frame(defenderDamage: 30, playerHasTurn: false),
                Cue(1, MatchAnimationKindView.Attack, amount: 30, source: Attacker),
                Cue(2, MatchAnimationKindView.Coin, targets: []),
                Cue(3, MatchAnimationKindView.Coin, targets: []),
                Cue(4, MatchAnimationKindView.Damage, amount: 30, source: Attacker)
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats
            .Select(beat => beat.Cue?.Kind)
            .ShouldBe([
                MatchAnimationKindView.Coin,
                MatchAnimationKindView.Coin,
                MatchAnimationKindView.Attack,
                MatchAnimationKindView.Damage,
                null,
            ]);
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

    // ---- The two invariants Alex's eye caught and no gate could -------------------------------
    //
    // The engine commits one state per command, so between a card leaving a place and the frame
    // agreeing that it has, the frame still has it there. Everything the table does in that gap is
    // the presentation's word against the frame's, and there are exactly two ways to get it wrong:
    // draw the card twice, or draw it back into a place it has already been seen leaving. These
    // hold the outcome rather than the means - what a viewer would count on the screen at every
    // beat - so that changing how the concealment is expressed cannot quietly bring either back.

    // Whether the presentation is holding this card up over the table, travelling or in the middle.
    private static bool Carried(MatchPresentationBeat beat, string cardInstanceId) =>
        beat.Overlay.CarriedCardInstanceId == cardInstanceId;

    // Whether the hand draws this card at this beat, which it does not once the presentation says
    // the hand has lost it.
    private static bool HandDraws(MatchPresentationBeat beat, string cardInstanceId) =>
        beat.Frame.Player.Hand.Any(card => card.Id == cardInstanceId)
        && !MatchCueState
            .HeldCard(beat.Cue, beat.Overlay, cardInstanceId)
            .HasFlag(MatchCueRole.Gone);

    // And whether the field does.
    private static bool TableDraws(MatchPresentationBeat beat, string cardInstanceId) =>
        new[] { beat.Frame.Player, beat.Frame.Opponent }.Any(side =>
            side.Active?.Id == cardInstanceId
            || side.Bench.Any(card => card.Id == cardInstanceId)
            || side.InPlayKits.Any(card => card.Id == cardInstanceId)
        )
        && !MatchCueState
            .FieldCard(beat.Cue, beat.Overlay, cardInstanceId)
            .HasFlag(MatchCueRole.Gone);

    private static int VisibleCopies(MatchPresentationBeat beat, string cardInstanceId) =>
        (Carried(beat, cardInstanceId) ? 1 : 0)
        + (HandDraws(beat, cardInstanceId) ? 1 : 0)
        + (TableDraws(beat, cardInstanceId) ? 1 : 0);

    [Test]
    [Arguments(MatchAnimationKindView.Play)]
    [Arguments(MatchAnimationKindView.Evolve)]
    [Arguments(MatchAnimationKindView.Setup)]
    public void OnlyOneOfACardIsEverOnScreen(MatchAnimationKindView played)
    {
        // Playing a card, promoting one and choosing who opens the game are the same journey: the
        // presentation picks the card up and carries it, while the hand it came out of still holds
        // it until the frame catches up. Anything the command does in between - reading a card out,
        // tossing a beer mat - used to hand the copy straight back, a promotion never concealed it
        // at all, and the opening choice handed it back the moment it stood the card up in the
        // Oche, so the one card of the opening was held and standing at once.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                HandFrame(held: false),
                Cue(1, played, source: Held, targets: []),
                Cue(2, MatchAnimationKindView.Reveal, targets: []),
                Cue(3, MatchAnimationKindView.Coin, targets: [])
            ),
            HandFrame(held: true)
        );

        // Never two of it.
        beats.Select(beat => VisibleCopies(beat, Held)).ShouldAllBe(copies => copies <= 1);
        // It is genuinely on screen while the presentation is carrying it, not merely hidden.
        VisibleCopies(beats[0], Held).ShouldBe(1);
        // And the hand it left never draws it again, whatever is on screen afterwards.
        beats.Select(beat => HandDraws(beat, Held)).ShouldAllBe(drawn => !drawn);
    }

    [Test]
    public void ACardKnockedOutIsNotStoodBackUpBeforeTheFrameCatchesUp()
    {
        // A knockout is always followed by the Prize cue that pays for it, and the frame does not
        // change until the whole step is over, so the beat after the fade used to stand the
        // Blokemon back up in the Oche at full strength.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                KnockedOutFrame(),
                Cue(1, MatchAnimationKindView.Attack, amount: 90, source: Attacker),
                Cue(2, MatchAnimationKindView.Damage, amount: 90, source: Attacker),
                Cue(3, MatchAnimationKindView.Knockout, source: Attacker),
                Cue(4, MatchAnimationKindView.Prize, targets: []),
                Cue(5, MatchAnimationKindView.Turn, targets: [])
            ),
            Frame(defenderDamage: 0, playerHasTurn: true)
        );

        beats.Select(beat => VisibleCopies(beat, Defender)).ShouldAllBe(copies => copies <= 1);
        // It is standing there until it is knocked out, and never again after.
        TableDraws(beats[0], Defender).ShouldBeTrue();
        beats.Skip(2).Select(beat => TableDraws(beat, Defender)).ShouldAllBe(drawn => !drawn);
    }

    [Test]
    public void OnceTheFrameHasCaughtUpThereIsNothingLeftToHideFromIt()
    {
        // The concealment is only ever the presentation disagreeing with the frame, and a draw
        // settles that argument early: it needs its own frame to have somewhere to deal the card
        // into, and that frame already knows where everything else went. Carrying the older
        // disagreement across the swap hides the played card in the place it has just been put -
        // it is standing on the Bench in this frame, and it is gone from nowhere.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                PlayedAndDrewFrame(),
                Cue(1, MatchAnimationKindView.Play, source: Held, targets: []),
                Cue(2, MatchAnimationKindView.Draw, targets: ["card-drawn"]),
                Cue(3, MatchAnimationKindView.Reveal, targets: [])
            ),
            HandFrame(held: true)
        );

        // Still exactly one of it throughout, and the hand never takes it back.
        beats.Select(beat => VisibleCopies(beat, Held)).ShouldAllBe(copies => copies <= 1);
        beats.Select(beat => HandDraws(beat, Held)).ShouldAllBe(drawn => !drawn);
        // The presentation carries it, and from the swap onwards the table stands it where it
        // landed rather than leaving the Bench slot empty for the rest of the step.
        Carried(beats[0], Held).ShouldBeTrue();
        beats.Skip(1).Select(beat => TableDraws(beat, Held)).ShouldAllBe(drawn => drawn);
    }

    [Test]
    public void ACardDrawnAndSpentInOneCommandIsNotStandingWhereItLandsBeforeItIsPlayed()
    {
        // The other half of the rule above, and the half that had it backwards. A command that
        // draws a card and then plays it settles on a table the card is standing on the Bench of
        // and no hand holds, so no draw of that command is one the frame is the hand of. Asked
        // which draw it was, the answer used to be the first cue rather than none - so the whole
        // command stood up at the draw, the card was lying on the Bench while it was still being
        // dealt, and the cue that plays it then took it back off in order to carry it there.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                DrewAndPlayedFrame(),
                Cue(1, MatchAnimationKindView.Draw, targets: [Held]),
                Cue(2, MatchAnimationKindView.Play, source: Held, targets: [])
            ),
            HandFrame(held: false)
        );

        // Still only ever one of it.
        beats.Select(beat => VisibleCopies(beat, Held)).ShouldAllBe(copies => copies <= 1);
        // The Bench does not stand it up until the cue that puts it there has played: it is dealt,
        // then carried, then standing.
        TableDraws(beats[0], Held).ShouldBeFalse();
        Carried(beats[1], Held).ShouldBeTrue();
        TableDraws(beats[^1], Held).ShouldBeTrue();
    }

    // ---- And the third: a card that acts without going anywhere -------------------------------
    //
    // An activated ability is announced with the same cue as a card being played, because both are
    // one command a player takes with one card. Only one of them moves anything. The Ring Road is
    // played to the pub slot and then goes on standing in it, offering its trade to both players
    // every round; a Blokemon uses its party trick from the Oche or the Booth it is already in. Told
    // as a card being played, all of them were carried out of the place they were standing in and
    // hidden there for the rest of the command - so the card whose whole point is that it stays
    // vanished at the moment it was used.
    //
    // What tells the two apart is not the cue, which cannot say, but the table: a card standing in
    // the same place before the command and after it has not been anywhere.

    private const string Standing = "card-standing";

    [Test]
    [Arguments("the pub slot")]
    [Arguments("the Oche")]
    [Arguments("the Booth")]
    public void ACardAnAbilityDoesNotMoveIsOnTheTableForTheWholeCommand(string place)
    {
        // The concealment outlives the cue that started it by design, so the question is asked of
        // every beat rather than of the one the ability is announced on: at the end of the command
        // the frame has caught up and the card is present again whatever the presentation did.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                AbilityTable(place, defenderDamage: 20),
                Cue(1, MatchAnimationKindView.Play, source: Standing, targets: []),
                Cue(2, MatchAnimationKindView.Damage, amount: 20, source: Standing)
            ),
            AbilityTable(place, defenderDamage: 0)
        );

        // It is on the table at every beat of the command, and there is only ever the one of it:
        // nothing is holding a second copy up over the table while the first stands where it is.
        beats.Select(beat => TableDraws(beat, Standing)).ShouldAllBe(drawn => drawn);
        beats.Select(beat => VisibleCopies(beat, Standing)).ShouldAllBe(copies => copies == 1);
        // Nowhere is expecting it, because it is not going anywhere: a landing on the place it is
        // already standing in is a journey from there to there.
        beats.Select(beat => beat.Overlay.Landing).ShouldAllBe(landing => landing == null);
        // And it is still the card that acted, which is what the ability has to be legible as.
        MatchCueState
            .FieldCard(beats[0].Cue, beats[0].Overlay, Standing)
            .HasFlag(MatchCueRole.Source)
            .ShouldBeTrue();
    }

    [Test]
    public void ACardCarriedFromOnePlaceOnTheTableToAnotherStillLeavesTheOneItCameFrom()
    {
        // The other side of the same question, and the one a table-to-table journey turns on: a
        // Blokemon taxied in from the Booth is standing on the table before the command and on the
        // table after it, and it has still crossed between the two. It is carried, hidden in the
        // Booth slot it left, and aimed at the Oche.
        var beats = MatchPresentationTimeline.Beats(
            Presentation(
                TaxiedFrame(),
                Cue(1, MatchAnimationKindView.Play, source: Standing, targets: [])
            ),
            BeforeTheTaxiFrame()
        );

        MatchCueState
            .FieldCard(beats[0].Cue, beats[0].Overlay, Standing)
            .HasFlag(MatchCueRole.Gone)
            .ShouldBeTrue();
        beats[0].Overlay.Landing.ShouldBe(new MatchLandingSlot(false, MatchLandingKind.Active, 0));
    }

    // The table an ability is used from - the same table it is used on, because using it moves
    // nothing of the player's. Only the card it is aimed at across the way changes.
    private static MatchFrameView AbilityTable(string place, int defenderDamage) =>
        new(
            Guid.Parse("40000000-0000-0000-0000-000000000005"),
            1,
            3,
            MatchPhaseView.Playing,
            Side("Opponent", false, active: Instance(Defender, defenderDamage)),
            place switch
            {
                "the pub slot" => Side(
                    "You",
                    true,
                    active: Instance(Attacker, 0),
                    inPlayKits: [Instance(Standing, 0)]
                ),
                "the Oche" => Side("You", true, active: Instance(Standing, 0)),
                _ => Side(
                    "You",
                    true,
                    active: Instance(Attacker, 0),
                    bench: [Instance("card-benched", 0), Instance(Standing, 0)]
                ),
            },
            false,
            null
        );

    // The Blokemon called in from the Booth, before and after: it swaps with the one that was in
    // the Oche, so both are on the table at both ends and both have moved.
    private static MatchFrameView BeforeTheTaxiFrame() =>
        AbilityTable("the Booth", defenderDamage: 0);

    private static MatchFrameView TaxiedFrame() =>
        BeforeTheTaxiFrame() with
        {
            Player = Side(
                "You",
                true,
                active: Instance(Standing, 0),
                bench: [Instance("card-benched", 0), Instance(Attacker, 0)]
            ),
        };

    // What a command that draws a card and then plays it settles on: the card is standing on the
    // Bench, and no hand on the table ever held it.
    private static MatchFrameView DrewAndPlayedFrame() =>
        HandFrame(held: false) with
        {
            Player = Side("You", true, active: Instance(Attacker, 0), bench: [Instance(Held, 0)]),
        };

    // What a command that plays a card and then draws one settles on: the played card is standing
    // on the Bench and the drawn one is in the hand.
    private static MatchFrameView PlayedAndDrewFrame() =>
        new(
            Guid.Parse("40000000-0000-0000-0000-000000000004"),
            1,
            3,
            MatchPhaseView.Playing,
            Side("Opponent", false, active: Instance(Defender, 0)),
            Side(
                "You",
                true,
                active: Instance(Attacker, 0),
                bench: [Instance(Held, 0)],
                hand: [Instance("card-drawn", 0)]
            ),
            false,
            null
        );

    private const string Held = "card-held";

    // The frame a command that knocks a Blokemon out settles on: the Oche it was standing in is
    // empty, which is exactly why the frames before it must not keep drawing it there.
    private static MatchFrameView KnockedOutFrame() =>
        new(
            Guid.Parse("40000000-0000-0000-0000-000000000003"),
            1,
            3,
            MatchPhaseView.Playing,
            Side("Opponent", false),
            Side("You", true, active: Instance(Attacker, 0)),
            false,
            null
        );

    private static MatchFrameView HandFrame(bool held) =>
        new(
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            1,
            3,
            MatchPhaseView.Playing,
            Side("Opponent", false, active: Instance(Defender, 0)),
            Side("You", true, active: Instance(Attacker, 0), hand: held ? [Instance(Held, 0)] : []),
            false,
            null
        );

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
            MatchPhaseView.Playing,
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
        MatchCardInstanceView[]? hand = null,
        MatchCardInstanceView[]? inPlayKits = null
    ) =>
        new(
            name,
            "Deck",
            40,
            hand?.Length ?? 0,
            6,
            active,
            bench ?? [],
            hand ?? [],
            inPlayKits ?? [],
            [],
            hasTurn
        );

    private static MatchCardInstanceView Instance(string id, int damage) =>
        new(id, Card(id), "You", "Field", damage, 90, [], [], [], []);

    private static CardView Card(string id) =>
        new(id, id, CardKindView.Blokemon, "Bloke", "", "", [], 0, false);
}
