using System.Globalization;
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

    // The two ends of a blow. The card throwing it keeps its mark from the declaration through
    // the damage, so the one movement is not cut in half where the cues change; the card taking
    // it is marked only while the damage is landing.
    public const string Striking = "is-cue-striking";

    public const string Struck = "is-cue-struck";

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

    // Which end of a blow a card on the table is, if it is either. Who is throwing it is not
    // something a single cue knows - the declaration and the damage are two cues - so it is asked
    // of the presentation, which worked it out before any of this was drawn. Damage nobody swung
    // for names nobody, and nothing here marks anything.
    public static string? Blow(
        string? strikingCardInstanceId,
        MatchEventCueView? cue,
        string cardInstanceId
    )
    {
        if (strikingCardInstanceId is null)
        {
            return null;
        }

        if (strikingCardInstanceId == cardInstanceId)
        {
            return Striking;
        }

        return cue?.Kind == MatchAnimationKindView.Damage
            && cue.TargetCardInstanceIds.Contains(cardInstanceId, StringComparer.Ordinal)
            ? Struck
            : null;
    }

    // How many cards a Deck is shown to be made of while it is being shuffled: half of them part
    // to one side and half to the other, which is the fewest that reads as two piles rather than
    // two cards.
    public const int RiffleCards = 12;

    // Which pile a card of a shuffling Deck belongs to. They alternate, so that dealing them in
    // the order they are written crosses the two sides one card at a time instead of one whole
    // side and then the other.
    public static string RiffleCard(int index) =>
        index % 2 == 0 ? "riffle-card is-left" : "riffle-card is-right";

    // A card's place in the order is what spaces it behind the one before it; the stylesheet owns
    // how long that spacing is, as it owns every other duration on the table.
    public static string RiffleStyle(int index) =>
        $"--riffle-order: {index.ToString(CultureInfo.InvariantCulture)}";

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
