using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;

namespace Blokemon.Web.Tests;

// One table, and one beat of a presentation on it for each thing a cue can be about, played once
// by each half of the table.
//
// The beats are built by the real timeline from a real step, so what is asked about is what the
// product works out: which cards a cue names, which of them the presentation has already carried
// off, where a played card is heading, and which card is part way through a blow. Nothing here
// decides any of that on the timeline's behalf.
//
// Both halves are played out because a cue belongs to the half doing it, and that is exactly the
// thing a rule can get wrong while looking right: the opponent's cues name the opponent's cards,
// so what reaches your side of the table when they act is the real answer rather than a mirror
// image assumed to hold.
internal static class MatchTableFixture
{
    public static readonly CardView Face = new(
        "snorlax",
        "Snorlax",
        CardKindView.Blokemon,
        "Sluggish",
        "A very large bloke.",
        string.Empty,
        [],
        0,
        false
    );

    // Who is doing it, and which cards are therefore theirs. A held card of the opponent's has no
    // identity on the table at all - their hand is drawn as a count of backs - so a cue of theirs
    // about one names a card nothing draws, which is the truth about their half of the table.
    private sealed record Doing(bool Local, string Held, string Active, string Struck);

    private static readonly Doing You = new(true, "hand-a", "you-active", "cpu-active");

    private static readonly Doing Them = new(false, "their-held", "cpu-active", "you-active");

    // The beat the table is on while this kind of cue is playing. Every kind gets the step it
    // really happens in: a knockout follows the blow that caused it, and a card played leaves a
    // hand that the frame behind the cue still has it in.
    public static MatchPresentationBeat Beat(MatchAnimationKindView kind, bool local)
    {
        var doing = local ? You : Them;
        var (after, events) = Step(kind, doing);
        var beats = MatchPresentationTimeline.Beats(new([new(after, events)]), Standing);
        return beats.Last(beat => beat.Cue?.Kind == kind);
    }

    // What the presentation says about every card the table draws for a beat, asked of the
    // presentation the same way each of them is drawn: your held cards are asked as held cards,
    // and everything standing on either half of the table is asked as a card on the field.
    //
    // Their held cards are not here because they are not drawn: the opponent's hand is a count of
    // backs, so a cue of theirs about one names a card nothing on the table is.
    public static IReadOnlyDictionary<string, MatchCueRole> Roles(MatchPresentationBeat beat)
    {
        var roles = new Dictionary<string, MatchCueRole>(StringComparer.Ordinal);
        foreach (var side in new[] { beat.Frame.Player, beat.Frame.Opponent })
        {
            foreach (var held in side.Hand)
            {
                Record(roles, held.Id, MatchCueState.HeldCard(beat.Cue, beat.Overlay, held.Id));
            }

            foreach (var standing in OnTheTable(side))
            {
                Record(
                    roles,
                    standing.Id,
                    MatchCueState.FieldCard(beat.Cue, beat.Overlay, standing.Id)
                );
            }
        }

        return roles;
    }

    private static IEnumerable<MatchCardInstanceView> OnTheTable(MatchSideView side) =>
        (side.Active is null ? [] : new[] { side.Active })
            .Concat(side.Bench)
            .Concat(side.InPlayKits);

    private static void Record(
        Dictionary<string, MatchCueRole> roles,
        string cardInstanceId,
        MatchCueRole role
    )
    {
        if (role != MatchCueRole.None)
        {
            roles[cardInstanceId] = role;
        }
    }

    private static (MatchFrameView After, MatchEventCueView[] Events) Step(
        MatchAnimationKindView kind,
        Doing doing
    )
    {
        MatchEventCueView Cue(
            MatchAnimationKindView cueKind,
            string? source = null,
            string[]? targets = null,
            int amount = 0,
            bool? badgeSide = null,
            CardView[]? revealed = null
        ) =>
            new(
                1,
                cueKind,
                "It happened.",
                source,
                targets ?? [],
                amount,
                badgeSide,
                doing.Local,
                revealed ?? []
            );

        // The blow the damage and the knockout both belong to: one swing, told over three cues,
        // exactly as the engine hands it over.
        MatchEventCueView[] Blow(params MatchEventCueView[] tail) =>
            [
                Cue(MatchAnimationKindView.Attack, doing.Active, [doing.Struck], 30),
                Cue(MatchAnimationKindView.Damage, doing.Active, [doing.Struck], 30),
                .. tail,
            ];

        // Where a card played by this half of the table ends up: on their own Bench, or standing
        // in their own Active place.
        MatchFrameView Landed(MatchLandingKind landing) =>
            doing.Local
                ? Table(
                    ["hand-b"],
                    landing == MatchLandingKind.Bench ? ["you-bench", "hand-a"] : ["you-bench"],
                    landing == MatchLandingKind.Bench ? "you-active" : "hand-a",
                    ["cpu-bench"],
                    "cpu-active"
                )
                : Table(
                    ["hand-a", "hand-b"],
                    ["you-bench"],
                    "you-active",
                    landing == MatchLandingKind.Bench ? ["cpu-bench", "their-held"] : ["cpu-bench"],
                    landing == MatchLandingKind.Bench ? "cpu-active" : "their-held"
                );

        // No last arm here either: a kind with no step to play it in is a kind nobody worked out
        // what the table does about, and it stops the build in the same breath as the contract for
        // it does.
#pragma warning disable CS8524
        return kind switch
        {
            MatchAnimationKindView.Setup => (Standing, [Cue(kind, doing.Held)]),
            MatchAnimationKindView.Shuffle => (Standing, [Cue(kind)]),
            MatchAnimationKindView.Draw => (Standing, [Cue(kind, targets: [Drawn(doing)])]),
            MatchAnimationKindView.Play => (
                Landed(MatchLandingKind.Bench),
                [Cue(kind, doing.Held)]
            ),
            MatchAnimationKindView.Attach => (Standing, [Cue(kind, doing.Held, [doing.Active])]),
            MatchAnimationKindView.Evolve => (
                Landed(MatchLandingKind.Active),
                [Cue(kind, doing.Held, [doing.Active])]
            ),
            MatchAnimationKindView.Attack or MatchAnimationKindView.Damage => (Standing, Blow()),
            MatchAnimationKindView.Heal => (
                Standing,
                [Cue(kind, doing.Active, [doing.Active], 10)]
            ),
            MatchAnimationKindView.Condition => (Standing, [Cue(kind, targets: [doing.Active])]),
            MatchAnimationKindView.Knockout => (
                Standing,
                Blow(Cue(kind, doing.Active, [doing.Struck]))
            ),
            MatchAnimationKindView.Prize => (Standing, [Cue(kind)]),
            MatchAnimationKindView.Turn => (Standing, [Cue(kind)]),
            MatchAnimationKindView.Coin => (Standing, [Cue(kind, badgeSide: true)]),
            MatchAnimationKindView.Victory => (Standing, [Cue(kind)]),
            MatchAnimationKindView.Reveal => (Standing, [Cue(kind, revealed: [Face])]),
            MatchAnimationKindView.Other => (Standing, [Cue(kind)]),
        };
#pragma warning restore CS8524
    }

    // The card dealt: one of your own held cards, which is drawn as itself, or one of theirs,
    // which arrives as the newest back in their strip.
    private static string Drawn(Doing doing) => doing.Local ? "hand-b" : doing.Held;

    private static readonly MatchFrameView Standing = Table(
        ["hand-a", "hand-b"],
        ["you-bench"],
        "you-active",
        ["cpu-bench"],
        "cpu-active"
    );

    private static MatchFrameView Table(
        string[] hand,
        string[] bench,
        string active,
        string[] theirBench,
        string theirActive
    ) =>
        new(
            Guid.Parse("0f000000-0000-0000-0000-000000000001"),
            1,
            3,
            "Playing",
            new(
                "CPU",
                "Theirs",
                20,
                3,
                6,
                Instance(theirActive),
                [.. theirBench.Select(Instance)],
                [],
                [],
                false
            ),
            new(
                "You",
                "Yours",
                20,
                hand.Length,
                6,
                Instance(active),
                [.. bench.Select(Instance)],
                [.. hand.Select(Instance)],
                [],
                true
            ),
            false,
            null
        );

    // Every card carries damage, so the counter a cue turns over is on the table to be turned.
    private static MatchCardInstanceView Instance(string cardInstanceId) =>
        new(cardInstanceId, Face, "You", "Field", 20, 60, [], [], [], []);
}
