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
        var opening = WentBack(1);
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

    // The opponent's hand had three presentations in every opening and should have had one: it was
    // in front of them before anything dealt it, then their own draw dealt it, and when their first
    // hand went back into the Deck their second draw dealt it again.
    //
    // None of that could be seen by asking about the deal alone, which is all the guarantee here
    // used to do. It is a whole-opening question - how many times a hand is put on the table, and
    // by which beat - so it is asked of every beat of the opening rather than of the one the answer
    // was expected to be about.
    [Test]
    [Arguments("nobody went back")]
    [Arguments("the player's hand went back")]
    [Arguments("the opponent's hand went back")]
    public void TheOpponentsHandIsPutOnTheTableOnceByTheDrawThatKeepsIt(string opening)
    {
        var presentation = Opening(opening);
        var beats = MatchPresentationTimeline.Beats(
            presentation,
            MatchOpening.EmptyTable(presentation)
        );
        var strips = beats.Select(Strip).ToArray();
        var widened = Enumerable
            .Range(0, strips.Length)
            .Where(index => strips[index].Shown > (index == 0 ? 0 : strips[index - 1].Shown))
            .ToArray();
        var theirDraws = Enumerable
            .Range(0, beats.Count)
            .Where(index =>
                MatchCueState.ActingOn(
                    beats[index].Cue,
                    MatchAnimationKindView.Draw,
                    opponent: true
                )
            )
            .ToArray();

        // Their hand goes onto the table once in the whole opening.
        var dealt = widened.ShouldHaveSingleItem();
        // It is a draw of their own that puts it there, and the last of them, so a hand that went
        // back into the Deck is never in front of them - which is what the player's own deal has
        // promised since the opening was first dealt onto an empty table.
        theirDraws.ShouldNotBeEmpty();
        theirDraws[^1].ShouldBe(dealt);
        // The whole hand arrives on that beat, and nothing arrives on any other.
        strips[dealt].Shown.ShouldBe(OpeningHand.Length);
        strips[dealt].Arriving.ShouldBe(strips[dealt].Shown);
        strips
            .Where((_, index) => index != dealt)
            .Select(strip => strip.Arriving)
            .ShouldAllBe(arriving => arriving == 0);
        // And nothing of theirs is in front of them before it.
        strips.Take(dealt).Select(strip => strip.Shown).ShouldAllBe(shown => shown == 0);
    }

    // 'I just witnessed I think 4 separate shuffles before I drew my first hand.' Four goes at a
    // hand is three that went back, and every one of them was played at the length of the one that
    // counted - which is what the opening below reproduces, on the half of the table that went back
    // and on the half that did not, at once.
    //
    // A mulligan is still seen to happen: the goes that went back are played rather than hidden. So
    // what is asked here is that the presentation can TELL THEM APART - which is all the page needs
    // in order to play them short, and the only part of this that is a guarantee rather than taste.
    // Nothing here says how short, and nothing here would change if that were tuned.
    //
    // The criterion this replaces asked that the hand finally kept was the one whose cards were
    // presented. That stayed true the whole time, which is exactly why every gate was green while
    // the opening was wrong: nothing had ever been asked about the shuffle and the deal belonging
    // to a go that went back. So this is asked of those beats, of every go, on both halves.
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public void EveryGoAtAnOpeningHandThatWentBackIsToldApartFromTheGoThatKeptIt(int wentBack)
    {
        var opening = WentBack(wentBack);
        var beats = MatchPresentationTimeline.Beats(opening, MatchOpening.EmptyTable(opening));

        // Which beat each half of the table is dealt the hand it keeps on, asked the way that half
        // can be asked it: the player's deal names the cards it brings, and the opponent's has none
        // to name, so theirs is the beat their strip of backs fills on.
        var dealt = Enumerable
            .Range(0, beats.Count)
            .Single(index => Deals(beats[index], OpeningHand));
        var filled = Enumerable
            .Range(0, beats.Count)
            .Single(index => Strip(beats[index]).Arriving > 0);

        foreach (var opponent in new[] { false, true })
        {
            var riffles = Acting(beats, MatchAnimationKindView.Shuffle, opponent);
            var deals = Acting(beats, MatchAnimationKindView.Draw, opponent);

            // The go that stays is the last of theirs, and it is genuinely the go the hand arrives
            // on rather than merely the last thing that happened on their half of the table.
            deals[^1].ShouldBe(opponent ? filled : dealt);

            // It is played whole, and so is the shuffle that dealt it.
            beats[deals[^1]].WentBack.ShouldBeFalse();
            beats[riffles[^1]].WentBack.ShouldBeFalse();

            // Every go before it went back, both halves of it - the deal, and the shuffle that
            // brought the cards back out of the Deck they had just gone into.
            deals[..^1].ShouldAllBe(index => beats[index].WentBack);
            riffles[..^1].ShouldAllBe(index => beats[index].WentBack);
        }

        // The two halves answer only for themselves. The player going back three times running says
        // nothing about the opponent, who kept the hand they were dealt first time and is owed the
        // whole of their one go at it.
        Acting(beats, MatchAnimationKindView.Draw, opponent: false)
            .Length.ShouldBe(wentBack + 1);
        Acting(beats, MatchAnimationKindView.Draw, opponent: true).Length.ShouldBe(1);

        // And nothing that is not a go at a hand is one. The battle being announced, and the table
        // settling once the whole opening has been dealt, are not attempts at anything.
        beats
            .Where(beat =>
                beat.Cue?.Kind
                    is not (MatchAnimationKindView.Shuffle or MatchAnimationKindView.Draw)
            )
            .ShouldAllBe(beat => !beat.WentBack);
    }

    // Which beats are one half of the table doing one thing: the riffle a go at a hand starts with,
    // or the deal it ends with.
    private static int[] Acting(
        IReadOnlyList<MatchPresentationBeat> beats,
        MatchAnimationKindView kind,
        bool opponent
    ) =>
        [
            .. Enumerable
                .Range(0, beats.Count)
                .Where(index => MatchCueState.ActingOn(beats[index].Cue, kind, opponent)),
        ];

    // Whether this beat is the one that deals a named hand, which is the one saying it is dealing
    // every card of it.
    private static bool Deals(MatchPresentationBeat beat, string[] hand) =>
        hand.All(cardInstanceId =>
            MatchCueState
                .HeldCard(beat.Cue, beat.Overlay, cardInstanceId)
                .HasFlag(MatchCueRole.Target)
        );

    // How the opponent's hand is presented on one beat: how wide their strip of backs is drawn, and
    // how many of those the presentation says have just arrived. It is asked the way the table asks
    // it - a strip is dealt into only by a draw of the half it belongs to - because a back has no
    // identity of its own to ask after.
    private static (int Shown, int Arriving) Strip(MatchPresentationBeat beat)
    {
        var shown = beat.Frame.Opponent.HandCount;
        return (
            shown,
            MatchCueState.ActingOn(beat.Cue, MatchAnimationKindView.Draw, opponent: true)
                ? Enumerable
                    .Range(0, shown)
                    .Count(index =>
                        MatchCueState.ArrivingBack(beat.Cue, index, shown) == MatchCueRole.Arriving
                    )
                : 0
        );
    }

    // The most consequential tap in the game: the Blokemon chosen to stand in the Oche used to
    // arrive without being seen to arrive, because the cue that carries it was never one of the
    // cues a card can travel on. The turn it starts is told inside the same command, so it is also
    // the one card journey that has somewhere to be for the rest of its own step.
    [Test]
    public void TheBlokemonChosenToOpenTravelsToTheOcheAndStandsThereBeforeTheTurnBegins()
    {
        var choice = TheChoice();
        var beats = MatchPresentationTimeline.Beats(choice, BeforeTheChoice());
        var carried = Enumerable
            .Range(0, beats.Count)
            .Single(index =>
                MatchCueState
                    .HeldCard(beats[index].Cue, beats[index].Overlay, Chosen)
                    .HasFlag(MatchCueRole.Source)
            );

        // It is carried out of the hand towards the place it ends up standing in, and the Oche is
        // still empty while it is on its way: it is arriving rather than having arrived.
        beats[carried]
            .Overlay.LandingFor(opponent: false)
            .ShouldBe(new MatchLandingSlot(false, MatchLandingKind.Active, 0));
        MatchCueState
            .HeldCard(beats[carried].Cue, beats[carried].Overlay, Chosen)
            .HasFlag(MatchCueRole.Gone)
            .ShouldBeTrue();
        beats[carried].Frame.Player.Active.ShouldBeNull();

        // From the moment it gets there it is standing there, so it is never nowhere.
        beats
            .Skip(carried + 1)
            .Select(beat => beat.Frame.Player.Active?.Id)
            .ShouldAllBe(standing => standing == Chosen);

        // And the game is still in its opening while the card is travelling: the phase changes
        // after the motion rather than instead of it.
        beats
            .Take(carried + 1)
            .Select(beat => beat.Frame.Phase)
            .ShouldAllBe(phase => phase == MatchPhaseView.OpeningPlacement);
        beats[carried + 1].Frame.Phase.ShouldBe(MatchPhaseView.Playing);

        // Standing it up does not deal the hand the turn behind it draws: that card is still the
        // draw's own to bring, and reaches the hand on the beat that names it.
        var drawn = Enumerable
            .Range(0, beats.Count)
            .Single(index =>
                MatchCueState
                    .HeldCard(beats[index].Cue, beats[index].Overlay, Drawn)
                    .HasFlag(MatchCueRole.Target)
            );
        beats
            .Take(drawn)
            .SelectMany(beat => beat.Frame.Player.Hand)
            .Select(card => card.Id)
            .ShouldNotContain(Drawn);
        beats[drawn].Frame.Player.Hand.Select(card => card.Id).ShouldContain(Drawn);
    }

    [Test]
    public void TheOpponentsOpeningChoiceIsCarriedToTheirOcheTheSameWay()
    {
        // Their Blokemon comes out of a hand nobody can see, which is the only difference: it is
        // still carried to the place it ends up standing in, from the strip that hand is drawn as.
        var choice = TheirChoice();
        var beats = MatchPresentationTimeline.Beats(choice, BeforeTheChoice());

        beats[0]
            .Overlay.LandingFor(opponent: true)
            .ShouldBe(new MatchLandingSlot(true, MatchLandingKind.Active, 0));
        beats[0].Frame.Opponent.Active.ShouldBeNull();
        beats[^1].Frame.Opponent.Active!.Id.ShouldBe(TheirChosen);
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

    private static MatchPresentationView Opening(string opening) =>
        opening switch
        {
            "the player's hand went back" => WentBack(1),
            "the opponent's hand went back" => TheyMulliganed(),
            _ => Opening(),
        };

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

    // The same opening, with the player's hand going back into the Deck a given number of times
    // before one of them stays. The opponent keeps the first hand they are dealt throughout, so the
    // two halves of the table are asked the same question with different answers.
    //
    // This is the shape the engine deals in: both Decks are shuffled and both players dealt seven,
    // and then a hand with no Regular in it goes back, its own Deck is shuffled again and another
    // seven come out - only that player's, because only that player returned anything.
    private static MatchPresentationView WentBack(int times)
    {
        var events = new List<MatchEventCueView>
        {
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
        };

        for (var again = 1; again < times; again++)
        {
            events.Add(Cue(MatchAnimationKindView.Shuffle, local: true));
            events.Add(
                Cue(
                    MatchAnimationKindView.Draw,
                    local: true,
                    amount: Returned.Length,
                    targets: Returned
                )
            );
        }

        events.Add(Cue(MatchAnimationKindView.Shuffle, local: true));
        events.Add(
            Cue(
                MatchAnimationKindView.Draw,
                local: true,
                amount: OpeningHand.Length,
                targets: OpeningHand
            )
        );

        return new([new(Started(), [.. events])]);
    }

    // And the same again with the hand that went back being theirs, which is what a quarter of
    // openings do. Nothing names the cards of either of their hands, so nothing but the order of
    // their draws distinguishes the one they keep from the one they gave up.
    private static MatchPresentationView TheyMulliganed() =>
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
                    Cue(MatchAnimationKindView.Shuffle, local: false),
                    Cue(MatchAnimationKindView.Draw, local: false, amount: OpeningHand.Length),
                ]
            ),
        ]);

    private const string Chosen = "new-one";

    private const string Drawn = "turn-draw";

    private const string TheirChosen = "theirs-oche";

    // The table the opening choice is made on: the hands are dealt, the opponent has already stood
    // somebody out, and the player has not.
    private static MatchFrameView BeforeTheChoice() =>
        new(
            Guid.Parse("50000000-0000-0000-0000-000000000003"),
            2,
            0,
            MatchPhaseView.OpeningPlacement,
            Side("The Regular", hand: [], held: OpeningHand.Length),
            Side("You", hand: OpeningHand),
            false,
            null
        );

    // What choosing an opening Blokemon settles on, and everything the command tells: the Blokemon
    // stands in the Oche, the turn that choice starts begins, and that turn draws its card.
    private static MatchPresentationView TheChoice() =>
        new([
            new(
                BeforeTheChoice() with
                {
                    Phase = MatchPhaseView.Playing,
                    Player = Side(
                        "You",
                        hand: [.. OpeningHand.Except([Chosen], StringComparer.Ordinal), Drawn],
                        active: Chosen
                    ),
                },
                [
                    Cue(MatchAnimationKindView.Setup, local: true, source: Chosen),
                    Cue(MatchAnimationKindView.Turn, local: true),
                    Cue(MatchAnimationKindView.Draw, local: true, amount: 1, targets: [Drawn]),
                ]
            ),
        ]);

    // Theirs, which arrives as its own command in the middle of the opening.
    private static MatchPresentationView TheirChoice() =>
        new([
            new(
                BeforeTheChoice() with
                {
                    Opponent = Side(
                        "The Regular",
                        hand: [],
                        held: OpeningHand.Length - 1,
                        active: TheirChosen
                    ),
                },
                [Cue(MatchAnimationKindView.Setup, local: false, source: TheirChosen)]
            ),
        ]);

    // What a start settles on: both hands dealt, nothing standing on the table yet, and the Bar
    // Chits set aside. Their hand is a count and no cards, which is the whole of what the table
    // knows about it and the reason the deal that brings it is so easily lost.
    private static MatchFrameView Started() =>
        new(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            1,
            0,
            MatchPhaseView.OpeningPlacement,
            Side("The Regular", hand: [], held: OpeningHand.Length),
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
        int prizes = 6,
        int? held = null
    ) =>
        new(
            name,
            "Deck",
            53,
            held ?? hand.Length,
            prizes,
            active is null ? null : Instance(active),
            [.. (bench ?? []).Select(Instance)],
            [.. hand.Select(Instance)],
            [],
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

    // The extra draw is the one command that deals twice: it ends the setup and starts the turn
    // that follows, so the bonus card and that turn's first card are drawn inside it. Nothing may
    // put the second of them in the hand before the draw that brings it - shown early, the turn's
    // own draw then takes it back out of the hand to deal it again, and a card the player is
    // already holding is seen to vanish and be drawn out of the Deck.
    [Test]
    public void ATurnsFirstCardIsNotHeldBeforeTheDrawThatDealsIt()
    {
        var beats = MatchPresentationTimeline.Beats(ExtraDrawThenTurn(), BeforeExtraDraw());

        var bonus = beats.First(beat =>
            beat.Cue?.TargetCardInstanceIds.Contains(BonusCard) == true
        );
        var turn = beats.First(beat => beat.Cue?.TargetCardInstanceIds.Contains(TurnCard) == true);

        // The bonus card arrives on its own draw and the turn's card is nowhere yet.
        Presented(bonus).ShouldContain(BonusCard);
        Presented(bonus).ShouldNotContain(TurnCard);
        // And the turn's card arrives on the draw that deals it, with the bonus card still held.
        Presented(turn).ShouldContain(TurnCard);
        Presented(turn).ShouldContain(BonusCard);
    }

    private const string BonusCard = "bonus-card";
    private const string TurnCard = "turn-card";

    // The table the extra draw is played against: both players standing, neither card drawn.
    private static MatchFrameView BeforeExtraDraw() =>
        new(
            Guid.Parse("50000000-0000-0000-0000-000000000003"),
            2,
            0,
            MatchPhaseView.MulliganBonus,
            Side("The Regular", hand: [], held: 7, active: "theirs"),
            Side("You", hand: [], active: "yours"),
            false,
            null
        );

    // One command, two draws, and the table it settles on holds both cards.
    private static MatchPresentationView ExtraDrawThenTurn() =>
        new([
            new(
                new(
                    Guid.Parse("50000000-0000-0000-0000-000000000003"),
                    3,
                    1,
                    MatchPhaseView.Playing,
                    Side("The Regular", hand: [], held: 7, active: "theirs"),
                    Side("You", hand: [BonusCard, TurnCard], active: "yours"),
                    false,
                    null
                ),
                [
                    Cue(MatchAnimationKindView.Draw, local: true, amount: 1, targets: [BonusCard]),
                    Cue(MatchAnimationKindView.Draw, local: true, amount: 1, targets: [TurnCard]),
                ]
            ),
        ]);

    private static MatchEventCueView Cue(
        MatchAnimationKindView kind,
        bool? local,
        int amount = 0,
        string[]? targets = null,
        string? source = null
    ) => new(1, kind, "cue", source, targets ?? [], amount, null, local, []);
}
