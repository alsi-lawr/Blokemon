using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Shouldly;

namespace Blokemon.Web.Tests;

// What a new game is presented over.
//
// Every other command is played against the table already on screen, and a match start was played
// against it too - so a new game opened on the previous game's hand, board and Prize rack, then
// jumped to its own. It is the one moment every player sees, and it showed the wrong table. The
// frame the start settles on is no answer either: both opening hands are already dealt in it, so
// the deal has nothing left to do and the hand simply appears.
//
// These are asked of the presentation rather than of the page - which cards it says are on the
// table at each beat, and which it says are arriving - so nothing below survives on a class name, a
// rendered sentence or a length of time. No duration appears here at all.
public sealed class MatchOpeningTests
{
    [Test]
    public void NoCardOfTheGameBeforeIsEverPresentedDuringANewGamesOpening()
    {
        var opening = Opening();
        var beats = MatchPresentationTimeline.Beats(opening, MatchOpening.EmptyTable(opening));

        var presented = beats.SelectMany(Presented).ToArray();

        // Nothing the last game left standing - held, standing on either half, or won - reaches
        // any beat of the new one.
        presented.Intersect(Presented(LastGame()), StringComparer.Ordinal).ShouldBeEmpty();
        // And the Prize rack is this game's own from the first beat, rather than the one the last
        // game was nearly finished on.
        beats
            .Select(beat => beat.Frame.Player.PrizeCards)
            .ShouldAllBe(prizes => prizes == Started().Player.PrizeCards);
        // Something was genuinely looked at: the new game's own cards do reach the table, so an
        // opening that presented nothing at all could not pass this by being empty.
        presented.ShouldContain(OpeningHand[0]);
    }

    [Test]
    public void ANewGameBeginsFromAnEmptyTableAndIsDealtItsOpeningHand()
    {
        var opening = Opening();
        var beats = MatchPresentationTimeline.Beats(opening, MatchOpening.EmptyTable(opening));
        var deal = beats.First(beat => beat.Cue?.Kind == MatchAnimationKindView.Draw);
        var before = beats
            .TakeWhile(beat => beat.Cue?.Kind != MatchAnimationKindView.Draw)
            .ToArray();

        // The battle is announced and both Decks are shuffled over a table with nothing on it:
        // nobody is holding anything and nobody is standing anything out.
        before.ShouldNotBeEmpty();
        before.SelectMany(Presented).ShouldBeEmpty();
        before.Select(beat => beat.Frame.Player.HandCount).ShouldAllBe(held => held == 0);
        before.Select(beat => beat.Frame.Opponent.HandCount).ShouldAllBe(held => held == 0);

        // Then the deal puts the hand there, and every card of it is a card that deal names -
        // which is what makes it dealt rather than simply present.
        Presented(deal).ShouldBe(OpeningHand, ignoreOrder: true);
        foreach (var cardInstanceId in OpeningHand)
        {
            MatchCueState
                .HeldCard(deal.Cue, deal.Overlay, cardInstanceId)
                .HasFlag(MatchCueRole.Target)
                .ShouldBeTrue($"{cardInstanceId} was there but nothing dealt it");
        }

        // The cards came out of the Deck they are dealt from rather than out of nowhere: the Deck
        // is short by exactly the hand it dealt.
        (before[^1].Frame.Player.DeckCount - deal.Frame.Player.DeckCount).ShouldBe(
            OpeningHand.Length
        );
    }

    [Test]
    public void AnOpeningThatGoesBackInTheDeckIsDealtOnlyTheHandItKeeps()
    {
        // A hand with no Blokemon in it goes back and is drawn again, and the frame the start
        // settles on holds only the hand that stayed. The deal that keeps it is therefore the one
        // the table is waiting for: the ones before it put nothing in front of the player, or the
        // kept hand would be lying there before the deal that brings it plays over the top of it.
        //
        // The hand that went back shares a card with the hand that stayed, which is what really
        // happens: it is shuffled into the Deck it came from and drawn out of it again, so the same
        // card comes back around often enough to matter. One card in common does not make the deal
        // that lost the other six the deal the table is waiting for.
        var opening = Mulliganed();
        var beats = MatchPresentationTimeline.Beats(opening, MatchOpening.EmptyTable(opening));
        var draws = beats.Where(beat => beat.Cue?.Kind == MatchAnimationKindView.Draw).ToArray();

        draws.Length.ShouldBe(3);
        Presented(draws[0]).ShouldBeEmpty();
        Presented(draws[1]).ShouldBeEmpty();
        Presented(draws[2]).ShouldBe(OpeningHand, ignoreOrder: true);
        foreach (var cardInstanceId in OpeningHand)
        {
            MatchCueState
                .HeldCard(draws[2].Cue, draws[2].Overlay, cardInstanceId)
                .HasFlag(MatchCueRole.Target)
                .ShouldBeTrue($"{cardInstanceId} was there but nothing dealt it");
        }

        // And the cards that went back and did not come out again were never in front of anybody.
        beats
            .SelectMany(Presented)
            .Intersect(Returned.Except(OpeningHand, StringComparer.Ordinal), StringComparer.Ordinal)
            .ShouldBeEmpty();
    }

    [Test]
    public void TheOpeningDealShowsTheWholeOfTheOpponentsHandArriving()
    {
        // The opponent's held cards are drawn as a strip of backs with no identity of their own, so
        // the only thing that says which of them have just arrived is how many the draw dealt. A
        // deal that brings a whole hand brings every back in the strip; an ordinary draw still
        // brings the newest one alone.
        var opening = Opening();
        var deal = MatchPresentationTimeline
            .Beats(opening, MatchOpening.EmptyTable(opening))
            .Last(beat => beat.Cue?.Kind == MatchAnimationKindView.Draw);

        var arriving = Enumerable
            .Range(0, OpeningHand.Length)
            .Select(index => MatchCueState.ArrivingBack(deal.Cue, index, OpeningHand.Length))
            .ToArray();

        arriving.ShouldAllBe(role => role == MatchCueRole.Arriving);
        MatchCueState
            .ArrivingBack(Cue(MatchAnimationKindView.Draw, local: false, amount: 1), 3, 5)
            .ShouldBe(MatchCueRole.None);
    }

    // Every card the table draws while a beat is on screen: whatever either side is holding and
    // whatever is standing on either half of it, less anything the presentation has already carried
    // off. It is what a player could point at, which is what the guarantee is about.
    private static IEnumerable<string> Presented(MatchPresentationBeat beat) =>
        Presented(beat.Frame)
            .Where(cardInstanceId =>
                !MatchCueState
                    .HeldCard(beat.Cue, beat.Overlay, cardInstanceId)
                    .HasFlag(MatchCueRole.Gone)
                && !MatchCueState
                    .FieldCard(beat.Cue, beat.Overlay, cardInstanceId)
                    .HasFlag(MatchCueRole.Gone)
            );

    private static IEnumerable<string> Presented(MatchFrameView frame) =>
        new[] { frame.Player, frame.Opponent }.SelectMany(side =>
            side.Hand.Concat(side.Active is null ? [] : new[] { side.Active })
                .Concat(side.Bench)
                .Concat(side.InPlayKits)
                .Select(card => card.Id)
        );

    private static readonly string[] OpeningHand =
    [
        "new-one",
        "new-two",
        "new-three",
        "new-four",
        "new-five",
        "new-six",
        "new-seven",
    ];

    // The hand that went back into the Deck, one card of which comes out of it again in the hand
    // that stays.
    private static readonly string[] Returned =
    [
        OpeningHand[0],
        "returned-two",
        "returned-three",
        "returned-four",
        "returned-five",
        "returned-six",
        "returned-seven",
    ];

    // The battle is announced, both Decks are shuffled, and both players are dealt seven.
    private static MatchPresentationView Opening() =>
        new([
            new(
                Started(),
                [
                    Cue(MatchAnimationKindView.Setup, local: null),
                    Cue(MatchAnimationKindView.Shuffle, local: true),
                    Cue(MatchAnimationKindView.Shuffle, local: false),
                    Cue(
                        MatchAnimationKindView.Draw,
                        local: true,
                        amount: OpeningHand.Length,
                        targets: OpeningHand
                    ),
                    Cue(MatchAnimationKindView.Draw, local: false, amount: OpeningHand.Length),
                ]
            ),
        ]);

    // The same opening, with a hand that went back into the Deck before the one that stayed.
    private static MatchPresentationView Mulliganed() =>
        new([
            new(
                Started(),
                [
                    Cue(MatchAnimationKindView.Setup, local: null),
                    Cue(MatchAnimationKindView.Shuffle, local: true),
                    Cue(MatchAnimationKindView.Shuffle, local: false),
                    Cue(
                        MatchAnimationKindView.Draw,
                        local: true,
                        amount: Returned.Length,
                        targets: Returned
                    ),
                    Cue(MatchAnimationKindView.Draw, local: false, amount: OpeningHand.Length),
                    Cue(MatchAnimationKindView.Shuffle, local: true),
                    Cue(
                        MatchAnimationKindView.Draw,
                        local: true,
                        amount: OpeningHand.Length,
                        targets: OpeningHand
                    ),
                ]
            ),
        ]);

    // What a start settles on: both hands dealt, nothing standing on the table yet, and the Bar
    // Chits set aside.
    private static MatchFrameView Started() =>
        new(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            1,
            0,
            MatchPhaseView.OpeningPlacement,
            Side("The Regular", hand: []),
            Side("You", hand: OpeningHand),
            false,
            null
        );

    // The battle before this one, at the point it was won: a hand still held, a Blokemon standing
    // in the Oche, one on the Bench, and one Prize left to take.
    private static MatchFrameView LastGame() =>
        new(
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            42,
            9,
            MatchPhaseView.Complete,
            Side("The Regular", hand: [], active: "old-theirs"),
            Side(
                "You",
                hand: ["old-held"],
                active: "old-active",
                bench: ["old-benched"],
                prizes: 1
            ),
            true,
            "You"
        );

    private static MatchSideView Side(
        string name,
        string[] hand,
        string? active = null,
        string[]? bench = null,
        int prizes = 6
    ) =>
        new(
            name,
            "Deck",
            53,
            hand.Length,
            prizes,
            active is null ? null : Instance(active),
            [.. (bench ?? []).Select(Instance)],
            [.. hand.Select(Instance)],
            [],
            false
        );

    private static MatchCardInstanceView Instance(string cardInstanceId) =>
        new(
            cardInstanceId,
            new(
                cardInstanceId,
                cardInstanceId,
                CardKindView.Blokemon,
                "Bloke",
                "",
                "",
                [],
                0,
                false
            ),
            "You",
            "Field",
            0,
            90,
            [],
            [],
            [],
            []
        );

    private static MatchEventCueView Cue(
        MatchAnimationKindView kind,
        bool? local,
        int amount = 0,
        string[]? targets = null
    ) => new(1, kind, "cue", null, targets ?? [], amount, null, local, []);
}
