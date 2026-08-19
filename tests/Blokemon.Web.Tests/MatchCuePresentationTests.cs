using Blokemon.App.Contracts;
using Shouldly;

namespace Blokemon.Web.Tests;

// The pairing between a cue and what the table does about it, said out loud so that it can be
// wrong out loud.
//
// Four defects on this presentation shared one shape and every gate stayed green through all of
// them: a rule keyed on '.cue-draw' or '.cue-play' for a mark the hand zone never put on anything,
// so two card journeys had never once played in the product's history; a rule keyed on
// '.cue-shuffle' with no actor in it, so one player's shuffle bounced both Decks; and a
// concealment written for '.cue-play' that did not cover '.cue-evolve', so every promotion drew
// two copies of the card for a whole beat. None of them could be found by searching: the class
// they hang off is composed at runtime out of a MatchAnimationKindView member's name, so no
// literal for it exists in any file, and a search finding nothing is exactly what a search for a
// class that IS emitted returns.
//
// So neither half is read on its own. The stylesheet is fetched from a running site and matched,
// as a browser would match it, against the table as the real presenters really drew it for the
// real timeline's real beat - and what comes back is where on the table each cue reaches and what
// it does when it gets there. A rule for a mark nothing emits reaches nothing. A rule that reaches
// too far arrives somewhere it was not declared. Both are the same failure here.
//
// Nothing below is allowed to pin a length of time: what is recorded is which properties a rule
// sets and what the run it starts is called, and the standing ruling that no test may fix a
// duration still holds.
public sealed class MatchCuePresentationTests
{
    // Two cue-keyed rules in the shipped stylesheet are wrong today. They are named here rather
    // than fixed: a fix belongs in a round that is reviewed as a fix, and a check whose first act
    // is to quietly repair what it found proves nothing about either.
    //
    // Both are the same defect class this check exists to close, found by it on its first run.
    private const string PrizeTakeReachesNothing = ".cue-prize .prizes i:first-child";

    private const string SetupBouncesBothDecks = ".cue-setup .deck-card-back";

    [Test]
    public async Task EveryCueMovesWhatItSaysItMoves()
    {
        var motion = new MatchCueMotion(await MatchStylesheet.Shipped());
        using var table = MatchTable.Create();
        var wrong = new List<string>();
        var declared = 0;
        foreach (var kind in Enum.GetValues<MatchAnimationKindView>())
        {
            var contract = Contract(kind);
            contract.Says.ShouldNotBeNullOrWhiteSpace();
            declared += contract.WhenYouDoIt.Count + contract.WhenTheyDoIt.Count;
            Differences(motion, table, kind, local: true, contract.WhenYouDoIt, wrong);
            Differences(motion, table, kind, local: false, contract.WhenTheyDoIt, wrong);
        }

        // Something was actually looked at, so that a reading of the stylesheet or of the table
        // which quietly came up empty cannot pass by finding nothing wrong with nothing.
        declared.ShouldBeGreaterThan(40);
        motion.Rules.ShouldBeGreaterThan(20);
        wrong.ShouldBeEmpty();
    }

    // A rule nobody can reach is a rule that does nothing, and it looks exactly like one that
    // works. Every rule written for a cue has to land on something in one of the tables above, or
    // it is a movement the product has never once played.
    [Test]
    public async Task NoRuleIsWrittenForACueNothingEmits()
    {
        var stylesheet = await MatchStylesheet.Shipped();
        var motion = new MatchCueMotion(stylesheet);
        using var table = MatchTable.Create();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<MatchAnimationKindView>())
        {
            foreach (var local in new[] { true, false })
            {
                var screen = table.Draw(
                    MatchTableFixture.Beat(kind, local),
                    MatchTableFixture.NothingGlowing
                );
                reached.UnionWith(motion.Reaching(screen));
            }
        }

        var dead = stylesheet
            .Rules.Select(static rule => rule.Selector)
            .Where(selector => selector.Contains("cue-", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(selector => !reached.Contains(selector))
            .ToArray();

        reached.Count.ShouldBeGreaterThan(20);
        dead.ShouldBe([PrizeTakeReachesNothing]);
    }

    // A cue belongs to the half of the table doing it. A rule that picks a card out by name can
    // reach anywhere, because the presentation decided which card; a rule that reaches whatever it
    // structurally lands on cannot, because nothing decided anything - it takes every one of a
    // thing it can find, and one player's shuffle bouncing both Decks is what that looks like.
    [Test]
    public async Task NoCueMovesTheOtherHalfOfTheTable()
    {
        var motion = new MatchCueMotion(await MatchStylesheet.Shipped());
        using var table = MatchTable.Create();
        var reaching = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<MatchAnimationKindView>())
        {
            foreach (var local in new[] { true, false })
            {
                var screen = table.Draw(
                    MatchTableFixture.Beat(kind, local),
                    MatchTableFixture.NothingGlowing
                );
                var theirs = local ? "opponent-zone" : "player-zone";
                foreach (var (selector, element) in motion.ReachedWithoutNamingACard(screen))
                {
                    if (element.Within(theirs))
                    {
                        reaching.Add(selector);
                    }
                }
            }
        }

        reaching.ShouldBe([SetupBouncesBothDecks]);
    }

    private static void Differences(
        MatchCueMotion motion,
        MatchTable table,
        MatchAnimationKindView kind,
        bool local,
        IReadOnlyList<string> declared,
        List<string> wrong
    )
    {
        var screen = table.Draw(
            MatchTableFixture.Beat(kind, local),
            MatchTableFixture.NothingGlowing
        );
        var doing = local ? "you do it" : "they do it";
        var actual = motion.OnTheTable(screen);
        foreach (var missing in declared.Except(actual, StringComparer.Ordinal))
        {
            wrong.Add($"{kind}, when {doing}: nothing does this - {missing}");
        }

        foreach (var extra in actual.Except(declared, StringComparer.Ordinal))
        {
            wrong.Add($"{kind}, when {doing}: undeclared - {extra}");
        }
    }

    // What each cue must do to the table, once for each half of it. Every place a rule reaches is
    // written out as the marks it and everything above it carry, because that is what a selector
    // matches on, and what happens there is written as the run it starts or the properties it
    // sets. A kind that moves nothing says so, which is a claim about it rather than a gap.
    //
    // There is no last arm on purpose. A member added to MatchAnimationKindView without a
    // presentation declared for it does not compile - CS8509 is an error in this project - so the
    // one thing that cannot happen is a new animation quietly getting no contract at all. That is
    // the whole difference between this and a list somebody has to remember to extend.
#pragma warning disable CS8524
    private static CuePresentation Contract(MatchAnimationKindView kind) =>
        kind switch
        {
            MatchAnimationKindView.Setup => new(
                "The card being put out is picked out of the hand, and both Decks are squared up. Both, whoever is setting theirs: the Deck rule carries no actor, so a player choosing their own opening shuffles the other player's Deck as well. That is the same defect as the shuffle one, still in the stylesheet, and it is reported here rather than fixed.",
                [
                    "battlefield > side-zone.opponent-zone > deck-stack > deck-card-back :: animation:shuffle",
                    "battlefield > side-zone.player-zone.has-turn > deck-stack > deck-card-back :: animation:shuffle",
                    "hand-zone > hand-card.is-cue-source :: z-index",
                    "hand-zone > hand-card.is-cue-source > hand-card-visual :: animation:source-action",
                ],
                [
                    "battlefield > side-zone.opponent-zone > deck-stack > deck-card-back :: animation:shuffle",
                    "battlefield > side-zone.player-zone.has-turn > deck-stack > deck-card-back :: animation:shuffle",
                ]
            ),
            MatchAnimationKindView.Shuffle => new(
                "The Deck of the player shuffling parts into two piles, crosses back a card at a time, and is squared up. The other Deck does nothing.",
                [
                    "battlefield > side-zone.player-zone.has-turn > deck-stack.is-riffling > deck-card-back :: animation:deck-square-up",
                    "battlefield > side-zone.player-zone.has-turn > deck-stack.is-riffling > deck-card-back > shuffle-riffle > riffle-card.is-left :: animation:riffle-left",
                    "battlefield > side-zone.player-zone.has-turn > deck-stack.is-riffling > deck-card-back > shuffle-riffle > riffle-card.is-right :: animation:riffle-right",
                ],
                [
                    "battlefield > side-zone.opponent-zone > deck-stack.is-riffling > deck-card-back :: animation:deck-square-up",
                    "battlefield > side-zone.opponent-zone > deck-stack.is-riffling > deck-card-back > shuffle-riffle > riffle-card.is-left :: animation:riffle-left",
                    "battlefield > side-zone.opponent-zone > deck-stack.is-riffling > deck-card-back > shuffle-riffle > riffle-card.is-right :: animation:riffle-right",
                ]
            ),
            MatchAnimationKindView.Draw => new(
                "The Deck presses under the pull, the card arcs across into the hand, and the rest of the hand moves aside for it. A card dealt to the opponent arrives face down in their strip and never turns over. The waiting that opens the fan is not scoped to whose draw it is, so your held cards take it on their draw too - which costs nothing, because nothing about your hand is changing then.",
                [
                    "battlefield > side-zone.player-zone.has-turn > deck-stack > deck-card-back :: animation:deck-press",
                    "hand-zone > hand-card :: transition-delay",
                    "hand-zone > hand-card.is-cue-target :: z-index",
                    "hand-zone > hand-card.is-cue-target > hand-card-visual :: animation:draw-arc, animation:target-pulse",
                ],
                [
                    "battlefield > side-zone.opponent-zone > deck-stack > deck-card-back :: animation:deck-press",
                    "battlefield > side-zone.opponent-zone > field-center > opponent-hand > oche-card-back.is-drawn :: animation:draw-arc-face-down, position, z-index",
                    "hand-zone > hand-card :: transition-delay",
                ]
            ),
            MatchAnimationKindView.Play => new(
                "The held card is picked up and carried out of the hand, which is why it is also concealed there, and the place it is heading for takes the landing. A card played by the opponent was never drawn as itself, so only the place it arrives at moves.",
                [
                    "battlefield > side-zone.player-zone.has-turn > field-center > bench-row > bench-slot.is-cue-landing.is-landing-centre :: animation:slot-land",
                    "hand-zone > hand-card.is-cue-source.is-cue-gone :: pointer-events, z-index",
                    "hand-zone > hand-card.is-cue-source.is-cue-gone > hand-card-visual :: animation:hand-play, animation:source-action, opacity, pointer-events",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > bench-row > bench-slot.is-cue-landing.is-landing-centre :: animation:slot-land",
                ]
            ),
            MatchAnimationKindView.Attach => new(
                "Both ends say what they are and nothing leaves: an attached card is drawn as an icon face on the card it went to, never as a second card, so there is nothing to conceal. This asymmetry is deliberate.",
                [
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                    "hand-zone > hand-card.is-cue-source :: z-index",
                    "hand-zone > hand-card.is-cue-source > hand-card-visual :: animation:source-action",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                ]
            ),
            MatchAnimationKindView.Evolve => new(
                "A promotion is a card played onto another one, so it is picked up and concealed exactly as a play is, and the Active place it lands in takes the landing.",
                [
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot.is-cue-landing.is-landing-top :: animation:slot-land",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot.is-cue-landing.is-landing-top > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                    "hand-zone > hand-card.is-cue-source.is-cue-gone :: pointer-events, z-index",
                    "hand-zone > hand-card.is-cue-source.is-cue-gone > hand-card-visual :: animation:hand-play, animation:source-action, opacity, pointer-events",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot.is-cue-landing.is-landing-bottom :: animation:slot-land",
                    "battlefield > side-zone.opponent-zone > field-center > active-slot.is-cue-landing.is-landing-bottom > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                ]
            ),
            MatchAnimationKindView.Attack => new(
                "The blow is thrown: the card that declared it winds up and crosses the gap, and what it is aimed at is already marked as what it will hit.",
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck :: animation:target-pulse, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck :: animation:target-pulse, z-index",
                ]
            ),
            MatchAnimationKindView.Damage => new(
                "The blow lands: the card taking it is knocked back and the number it caused turns over on it, while the card that swung is still recovering.",
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck :: animation:attack-recoil, animation:target-pulse, z-index",
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck > damage-badge :: animation:counter-pop",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck :: animation:attack-recoil, animation:target-pulse, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck > damage-badge :: animation:counter-pop",
                ]
            ),
            MatchAnimationKindView.Heal => new(
                "The number turns over on the card being healed, which is also the card doing it.",
                [
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-target :: animation:source-action, animation:target-pulse, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-target > damage-badge :: animation:counter-pop",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-target :: animation:source-action, animation:target-pulse, z-index",
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-target > damage-badge :: animation:counter-pop",
                ]
            ),
            MatchAnimationKindView.Condition => new(
                "A card carrying a new condition says so and nothing moves.",
                [
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target :: animation:target-pulse, z-index",
                ]
            ),
            MatchAnimationKindView.Knockout => new(
                "The card sent home falls and stays gone - not drawn, and not pressable either, for the rest of the step the frame behind it still has it standing in.",
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button :: pointer-events",
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck.is-cue-gone :: animation:knockout, animation:target-pulse, opacity, pointer-events, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                ],
                [
                    "battlefield > side-zone.opponent-zone > field-center > active-slot > field-card-button > battle-card-shell.is-cue-source.is-cue-striking :: animation:attack-lunge, animation:source-action, z-index",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button :: pointer-events",
                    "battlefield > side-zone.player-zone.has-turn > field-center > active-slot > field-card-button > battle-card-shell.is-cue-target.is-cue-struck.is-cue-gone :: animation:knockout, animation:target-pulse, opacity, pointer-events, z-index",
                ]
            ),
            MatchAnimationKindView.Prize => new(
                "Nothing on the table moves. The one rule written for a Prize being taken reaches nothing at all, which is a defect reported rather than fixed here.",
                [],
                []
            ),
            MatchAnimationKindView.Turn => new(
                "The turn change is announced over the table; nothing on the table itself moves.",
                [],
                []
            ),
            MatchAnimationKindView.Coin => new(
                "The beer mat is tossed over the table; nothing on the table itself moves.",
                [],
                []
            ),
            MatchAnimationKindView.Victory => new(
                "The result is announced over the table; nothing on the table itself moves.",
                [],
                []
            ),
            MatchAnimationKindView.Reveal => new(
                "The revealed cards are shown over the table; nothing on the table itself moves.",
                [],
                []
            ),
            MatchAnimationKindView.Other => new(
                "An event with no motion of its own: the words are said and nothing on the table moves.",
                [],
                []
            ),
        };
#pragma warning restore CS8524

    private sealed record CuePresentation(
        string Says,
        IReadOnlyList<string> WhenYouDoIt,
        IReadOnlyList<string> WhenTheyDoIt
    );
}
