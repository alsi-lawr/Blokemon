using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// Which card the presentation says is moving, and whose half of the table it belongs to.
//
// Four defects on this presentation shared one shape and every gate stayed green through all of
// them: the hand identified no card at all, so the card that crosses the table on a draw and the
// card that leaves the hand on a play were both described in full and neither had ever moved; a
// shuffle carried no actor, so one player's shuffle bounced both Decks; and a departure was
// recognised for a card played but not for one promoted, so every promotion drew two copies of the
// card for a whole beat. Each of them is a card the presentation failed to identify, and none of
// them could be found by reading what the table would wear.
//
// So the question is asked of the presentation rather than of the page: for a real beat of a real
// timeline, which cards does it say are the source, the target, the striking, the struck, the gone
// and the arriving - and which does it leave alone. Nothing below names a class, a rule, an
// element or a length of time, so renaming or restyling every one of them changes nothing here.
public sealed class MatchCueStateTests
{
    [Test]
    public void EveryCueIdentifiesExactlyTheCardsItIsAbout()
    {
        var wrong = new List<string>();
        var declared = 0;
        foreach (var kind in Enum.GetValues<MatchAnimationKindView>())
        {
            var contract = Contract(kind);
            declared += contract.WhenYouDoIt.Count + contract.WhenTheyDoIt.Count;
            Differences(kind, local: true, contract, wrong);
            Differences(kind, local: false, contract, wrong);
        }

        // Something was actually looked at, so that a table which quietly came up empty cannot pass
        // by finding nothing wrong with nothing.
        declared.ShouldBeGreaterThan(20);
        wrong.ShouldBeEmpty();
    }

    // A cue belongs to the half of the table doing it. The cards it names are picked out by name
    // and can be anywhere, but a Deck being shuffled and a hand being dealt into are not named by
    // anything: they are whichever half is acting, and a rule that forgets to say which is a rule
    // that takes both.
    [Test]
    public void OnlyTheHalfOfTheTableActingShufflesItsDeckOrIsDealtInto()
    {
        MatchAnimationKindView[] sideScoped =
        [
            MatchAnimationKindView.Shuffle,
            MatchAnimationKindView.Draw,
        ];

        foreach (var kind in Enum.GetValues<MatchAnimationKindView>())
        {
            foreach (var local in new[] { true, false })
            {
                var cue = MatchTableFixture.Beat(kind, local).Cue;
                var doing = local ? "you do it" : "they do it";
                foreach (var scoped in sideScoped)
                {
                    MatchCueState
                        .ActingOn(cue, scoped, opponent: local)
                        .ShouldBeFalse($"{kind}, when {doing}: {scoped} reached the other half");
                    MatchCueState
                        .ActingOn(cue, scoped, opponent: !local)
                        .ShouldBe(
                            kind == scoped,
                            $"{kind}, when {doing}: {scoped} on the acting half"
                        );
                }
            }
        }
    }

    // A card dealt to the opponent has no identity in their strip, so the one being dealt is the
    // newest back in it; a strip already showing its full width has nowhere to put another.
    [Test]
    public void ACardDealtToTheOpponentArrivesAsTheNewestBackInTheirStrip()
    {
        var draw = MatchTableFixture.Beat(MatchAnimationKindView.Draw, local: false).Cue;

        MatchCueState.ArrivingBack(draw, index: 4, shown: 5).ShouldBe(MatchCueRole.Arriving);
        MatchCueState.ArrivingBack(draw, index: 3, shown: 5).ShouldBe(MatchCueRole.None);
        MatchCueState
            .ArrivingBack(
                MatchTableFixture.Beat(MatchAnimationKindView.Play, local: false).Cue,
                index: 4,
                shown: 5
            )
            .ShouldBe(MatchCueRole.None);
    }

    [Test]
    public void ACardThatHurtsItselfIsThrowingTheBlowRatherThanTakingIt()
    {
        // A fumble damages the card that swung. It cannot be knocked back by the movement it is in
        // the middle of making, so it keeps the swing and takes no recoil - and this holds even if
        // something ever names it at both ends at once.
        MatchCueState
            .Blow(MatchPresentationOverlay.Empty.Blow("attacker", ["attacker"]), "attacker")
            .ShouldBe(MatchCueRole.Striking);

        // Damage nobody swung for is not a blow, so neither end of it is one.
        MatchCueState.Blow(MatchPresentationOverlay.Empty, "attacker").ShouldBe(MatchCueRole.None);
    }

    // Where a card being played is going is settled before it gets there, and only the place
    // expecting it takes the landing: the other places on that half, and the whole of the other
    // half, are asked the same question and hear no.
    [Test]
    public void OnlyThePlaceExpectingACardTakesTheLanding()
    {
        var played = MatchTableFixture.Beat(MatchAnimationKindView.Play, local: true).Overlay;

        MatchCueState
            .Landing(played.LandingFor(opponent: false), MatchLandingKind.Bench, 1)
            .ShouldBe(MatchLandingPlacement.Centre);
        MatchCueState
            .Landing(played.LandingFor(opponent: false), MatchLandingKind.Bench, 0)
            .ShouldBeNull();
        MatchCueState
            .Landing(played.LandingFor(opponent: false), MatchLandingKind.Active, 0)
            .ShouldBeNull();
        MatchCueState
            .Landing(played.LandingFor(opponent: true), MatchLandingKind.Bench, 1)
            .ShouldBeNull();
    }

    // A promotion lands in the Active place, and the Active card leans away from the middle of the
    // table: which end of its own place it comes to rest at depends on whose half it is.
    [Test]
    public void APromotedCardLandsAtItsOwnEndOfTheTable()
    {
        MatchCueState
            .Landing(
                MatchTableFixture
                    .Beat(MatchAnimationKindView.Evolve, local: true)
                    .Overlay.LandingFor(opponent: false),
                MatchLandingKind.Active,
                0
            )
            .ShouldBe(MatchLandingPlacement.Top);
        MatchCueState
            .Landing(
                MatchTableFixture
                    .Beat(MatchAnimationKindView.Evolve, local: false)
                    .Overlay.LandingFor(opponent: true),
                MatchLandingKind.Active,
                0
            )
            .ShouldBe(MatchLandingPlacement.Bottom);
    }

    [Test]
    public void AShufflingDeckIsDealtOutAsTwoPilesThatCrossOneCardAtATime()
    {
        // The two piles have to be written alternately: dealt as one side and then the other, the
        // order they are spaced by would cross a whole pile before the first card of the other one
        // moved, which is the two halves being pulled apart rather than a riffle.
        var piles = Enumerable
            .Range(0, MatchCueMarking.RiffleCards)
            .Select(MatchCueState.RifflePile)
            .ToArray();

        piles.Count(pile => pile == MatchRifflePile.Left).ShouldBe(piles.Length / 2);
        piles.Count(pile => pile == MatchRifflePile.Right).ShouldBe(piles.Length / 2);
        piles.Take(3).ShouldBe([MatchRifflePile.Left, MatchRifflePile.Right, MatchRifflePile.Left]);
    }

    // Everything above asks the presentation what it says. This asks whether what it says reaches
    // the page at all, which is a different question and the one nothing was asking: the marking
    // was wired to the presentation by a single call in each place, and cutting that call left the
    // whole suite green while the hand quietly stopped wearing any mark - the shape of two of the
    // four defects named at the top of this file.
    //
    // So it compares rather than pins. What a surface wears with a cue on it must differ from what
    // it wears with none, and nothing here says what either one equals: no class is named, no
    // string is compared against, and renaming every class in the stylesheet changes nothing. An
    // edit that makes it name a class or compare against a literal turns it into the thing this
    // suite exists not to be.
    [Test]
    public void WhatASurfaceWearsRespondsToWhatThePresentationSaysAboutIt()
    {
        var played = MatchTableFixture.Beat(MatchAnimationKindView.Play, local: true);

        // A held card the cue is acting from, against the same card with nothing said about it.
        MatchCueMarking
            .HandCard("hand-a", NoAuras, played.Cue, played.Overlay)
            .ShouldNotBe(MatchCueMarking.HandCard("hand-a", NoAuras, null, played.Overlay));

        // The place expecting the card, against the same place expecting nothing.
        MatchCueMarking
            .LandingClass(played.Overlay.LandingFor(opponent: false), MatchLandingKind.Bench, 1)
            .ShouldNotBe(MatchCueMarking.LandingClass(null, MatchLandingKind.Bench, 1));

        // And the table itself, which wears the cue that is playing over it.
        MatchCueMarking.Table(played.Cue).ShouldNotBe(MatchCueMarking.Table(null));
    }

    private static readonly MatchAuraView NoAuras = new(
        [],
        [],
        false,
        false,
        new Dictionary<string, int>()
    );

    private static void Differences(
        MatchAnimationKindView kind,
        bool local,
        CueContract contract,
        List<string> wrong
    )
    {
        var expected = local ? contract.WhenYouDoIt : contract.WhenTheyDoIt;
        var actual = MatchTableFixture.Roles(MatchTableFixture.Beat(kind, local));
        var doing = local ? "you do it" : "they do it";
        foreach (
            var cardInstanceId in expected
                .Keys.Union(actual.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        )
        {
            var want = expected.GetValueOrDefault(cardInstanceId, MatchCueRole.None);
            var got = actual.GetValueOrDefault(cardInstanceId, MatchCueRole.None);
            if (want != got)
            {
                wrong.Add(
                    $"{kind}, when {doing}: {cardInstanceId} is {got} and should be {want} - {contract.Says}"
                );
            }
        }
    }

    // What each cue says about the cards on the table, once for each half of it. Every card the
    // presentation identifies is written out with what it is identified as; a card left out is a
    // card nothing is claimed about, and a kind that identifies nobody says so, which is a claim
    // about it rather than a gap.
    //
    // The two halves are written separately rather than mirrored, because they are not mirrors: a
    // held card of the opponent's has no identity on the table at all - their hand is a count of
    // backs - so a cue of theirs about one names a card nothing on the table is.
    //
    // There is no last arm on purpose. A member added to MatchAnimationKindView without a
    // presentation declared for it does not compile - CS8509 is an error repository-wide - so the
    // one thing that cannot happen is a new animation quietly getting no contract at all. That is
    // the whole difference between this and a list somebody has to remember to extend.
#pragma warning disable CS8524
    private static CueContract Contract(MatchAnimationKindView kind) =>
        kind switch
        {
            MatchAnimationKindView.Setup => new(
                "The Blokemon chosen to open the game is picked out of the hand and carried to the Oche, so it leaves the hand exactly as a card played does. Theirs comes out of a hand nothing on the table draws.",
                new() { ["hand-a"] = MatchCueRole.Source | MatchCueRole.Gone },
                new()
            ),
            MatchAnimationKindView.Shuffle => new(
                "A shuffle is about a Deck, so it identifies no card at all.",
                new(),
                new()
            ),
            MatchAnimationKindView.Draw => new(
                "The card being dealt is the one arriving in the hand. A card dealt to the opponent is drawn as a back in their strip and is no card of the table's.",
                new() { ["hand-b"] = MatchCueRole.Target },
                new()
            ),
            MatchAnimationKindView.Play => new(
                "The held card is picked up and carried out of the hand, so it is both what the cue is acting from and a card the hand has already lost.",
                new() { ["hand-a"] = MatchCueRole.Source | MatchCueRole.Gone },
                new()
            ),
            MatchAnimationKindView.Attach => new(
                "Both ends say what they are and nothing leaves: an attached card is drawn as an icon face on the card it went to, never as a second card, so there is nothing for the hand to have lost. This asymmetry is deliberate.",
                new() { ["hand-a"] = MatchCueRole.Source, ["you-active"] = MatchCueRole.Target },
                new() { ["cpu-active"] = MatchCueRole.Target }
            ),
            MatchAnimationKindView.Evolve => new(
                "A promotion is a card played onto another one, so the held card leaves the hand exactly as a play does and the card underneath is what it is played onto.",
                new()
                {
                    ["hand-a"] = MatchCueRole.Source | MatchCueRole.Gone,
                    ["you-active"] = MatchCueRole.Target,
                },
                new() { ["cpu-active"] = MatchCueRole.Target }
            ),
            MatchAnimationKindView.Attack => new(
                "The blow is thrown: the card that declared it is already throwing it, and what it is aimed at is already what it will hit.",
                new()
                {
                    ["you-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["cpu-active"] = MatchCueRole.Target | MatchCueRole.Struck,
                },
                new()
                {
                    ["cpu-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["you-active"] = MatchCueRole.Target | MatchCueRole.Struck,
                }
            ),
            MatchAnimationKindView.Damage => new(
                "The blow lands on the card it was aimed at, and the card that swung is still the one throwing it.",
                new()
                {
                    ["you-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["cpu-active"] = MatchCueRole.Target | MatchCueRole.Struck,
                },
                new()
                {
                    ["cpu-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["you-active"] = MatchCueRole.Target | MatchCueRole.Struck,
                }
            ),
            MatchAnimationKindView.Heal => new(
                "The card being healed is also the card doing it, and it is both at once.",
                new() { ["you-active"] = MatchCueRole.Source | MatchCueRole.Target },
                new() { ["cpu-active"] = MatchCueRole.Source | MatchCueRole.Target }
            ),
            MatchAnimationKindView.Condition => new(
                "A card carrying a new condition is what the cue is about and nothing else is.",
                new() { ["you-active"] = MatchCueRole.Target },
                new() { ["cpu-active"] = MatchCueRole.Target }
            ),
            MatchAnimationKindView.Knockout => new(
                "The card sent home is the far end of the blow that finished it and a card the table has already lost, both at once and for the rest of the step the frame behind it still has it standing in.",
                new()
                {
                    ["you-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["cpu-active"] = MatchCueRole.Target | MatchCueRole.Struck | MatchCueRole.Gone,
                },
                new()
                {
                    ["cpu-active"] = MatchCueRole.Source | MatchCueRole.Striking,
                    ["you-active"] = MatchCueRole.Target | MatchCueRole.Struck | MatchCueRole.Gone,
                }
            ),
            MatchAnimationKindView.Prize => new(
                "A Prize being taken is about the rack rather than about any card on the table.",
                new(),
                new()
            ),
            MatchAnimationKindView.Turn => new(
                "The turn change is announced over the table and identifies no card.",
                new(),
                new()
            ),
            MatchAnimationKindView.Coin => new(
                "The beer mat is tossed over the table and identifies no card.",
                new(),
                new()
            ),
            MatchAnimationKindView.Victory => new(
                "The result is announced over the table and identifies no card.",
                new(),
                new()
            ),
            MatchAnimationKindView.Reveal => new(
                "Revealed cards are held up over the table rather than picked out of it, so no card standing on it is identified.",
                new(),
                new()
            ),
            MatchAnimationKindView.Other => new(
                "An event with no motion of its own: the words are said and no card is identified.",
                new(),
                new()
            ),
        };
#pragma warning restore CS8524

    private sealed record CueContract(
        string Says,
        Dictionary<string, MatchCueRole> WhenYouDoIt,
        Dictionary<string, MatchCueRole> WhenTheyDoIt
    );
}
