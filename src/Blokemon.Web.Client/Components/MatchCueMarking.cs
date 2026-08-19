using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// A cue names cards; the stylesheet and the measuring script find them by the marks put on the
// elements those cards are drawn as. Every zone that a cue can be about has to do the marking,
// or the rules written for it match nothing and the motion never plays while looking, in the
// stylesheet, exactly as though it does.
public static class MatchCueMarking
{
    public const string Source = "is-cue-source";

    public const string Target = "is-cue-target";

    // The card being dealt into the opponent's strip, which has no identity of its own there:
    // their held cards are drawn as a count of backs.
    public const string Arriving = "is-drawn";

    public const string Landing = "is-cue-landing";

    // Everything true of a held card at once: whether it can be chosen, whether it has been, and
    // whether the cue on screen is about it. A held card is one end of both journeys between the
    // hand and the table, so the marking is built here rather than in the hand zone, where the
    // cue half of it was once simply left out and every rule written for it matched nothing.
    public static string HandCard(
        string cardInstanceId,
        MatchAuraView auras,
        MatchEventCueView? cue
    )
    {
        var classes = new List<string>(4) { "hand-card" };
        if (auras.IsSelected(cardInstanceId))
        {
            classes.Add("is-aura is-aura-selected");
        }
        else if (auras.IsAura(cardInstanceId))
        {
            classes.Add("is-aura");
        }

        if (For(cue, cardInstanceId) is { } marking)
        {
            classes.Add(marking);
        }

        return string.Join(' ', classes);
    }

    public static string? For(MatchEventCueView? cue, string cardInstanceId)
    {
        if (cue is null)
        {
            return null;
        }

        var source = cue.SourceCardInstanceId == cardInstanceId;
        var target = cue.TargetCardInstanceIds.Contains(cardInstanceId, StringComparer.Ordinal);
        return (source, target) switch
        {
            (true, true) => $"{Source} {Target}",
            (true, false) => Source,
            (false, true) => Target,
            _ => null,
        };
    }

    // The Deck deals one card at a time whoever is drawing, so the arriving back is the newest
    // one in the strip; a strip already showing its full width has nowhere to put another and
    // shows none arriving.
    public static bool Arrives(MatchEventCueView? cue, int index, int shown) =>
        cue?.Kind == MatchAnimationKindView.Draw && index == shown - 1;

    // Where the landing sits decides where the card it is expecting comes to rest: the Active
    // card leans away from the middle of the table at its own end, and everything else stands in
    // the middle of the place kept for it.
    public static string? LandingClass(MatchLandingSlot? landing, MatchLandingKind kind, int index)
    {
        if (
            landing is null
            || landing.Kind != kind
            || (kind == MatchLandingKind.Bench && landing.BenchIndex != index)
        )
        {
            return null;
        }

        return kind switch
        {
            MatchLandingKind.Active when landing.Opponent => $"{Landing} is-landing-bottom",
            MatchLandingKind.Active => $"{Landing} is-landing-top",
            _ => $"{Landing} is-landing-centre",
        };
    }
}
