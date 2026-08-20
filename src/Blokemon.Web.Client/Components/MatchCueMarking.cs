using System.Globalization;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// The edge where what the presentation says about a card becomes something a browser can find it
// by. The stylesheet and the measuring script pick cards out by the marks put on the elements they
// are drawn as; every one of those marks is composed here, out of a MatchCueState value and
// nothing else.
//
// Nothing above this decides anything. Which card is moving, which is gone, which end of a blow it
// is and whose half of the table is acting are all settled in MatchCueState, so renaming any mark
// below changes what the page wears and nothing about what the presentation means.
public static class MatchCueMarking
{
    // Nothing outside composes one of these, so nothing outside is given the chance: every mark
    // leaves here as part of a whole class string, and the only way to earn one is to be in the
    // state it stands for.
    private const string Source = "is-cue-source";

    private const string Target = "is-cue-target";

    private const string Striking = "is-cue-striking";

    private const string Struck = "is-cue-struck";

    private const string Gone = "is-cue-gone";

    // The card being dealt into the opponent's strip.
    private const string Arriving = "is-drawn";

    private const string Landing = "is-cue-landing";

    // Every mark a card carries for what the presentation says about it, in one string. A card with
    // nothing said about it wears nothing.
    public static string? Classes(MatchCueRole role)
    {
        if (role == MatchCueRole.None)
        {
            return null;
        }

        var classes = new List<string>(3);
        Add(classes, role, MatchCueRole.Source, Source);
        Add(classes, role, MatchCueRole.Target, Target);
        Add(classes, role, MatchCueRole.Striking, Striking);
        Add(classes, role, MatchCueRole.Struck, Struck);
        Add(classes, role, MatchCueRole.Gone, Gone);
        Add(classes, role, MatchCueRole.Arriving, Arriving);
        return string.Join(' ', classes);
    }

    private static void Add(List<string> classes, MatchCueRole role, MatchCueRole one, string mark)
    {
        if (role.HasFlag(one))
        {
            classes.Add(mark);
        }
    }

    // Everything true of a held card at once: whether it can be chosen, whether it has been, and
    // what the presentation says about it. A held card is one end of both journeys between the hand
    // and the table, so the marking is built here rather than in the hand zone, where the cue half
    // of it was once simply left out and every rule written for it matched nothing.
    public static string HandCard(
        string cardInstanceId,
        MatchAuraView auras,
        MatchEventCueView? cue,
        MatchPresentationOverlay overlay
    )
    {
        var classes = new List<string>(3) { "hand-card" };
        if (auras.IsSelected(cardInstanceId))
        {
            classes.Add("is-aura is-aura-selected");
        }
        else if (auras.IsAura(cardInstanceId))
        {
            classes.Add("is-aura");
        }

        if (Classes(MatchCueState.HeldCard(cue, overlay, cardInstanceId)) is { } marks)
        {
            classes.Add(marks);
        }

        return string.Join(' ', classes);
    }

    // What the whole table wears while a cue is on screen, and the thing every rule written for
    // that cue is keyed on. It is composed out of the kind's own name and the half of the table
    // acting, so no literal for any of these classes exists to be searched for - which is how four
    // rules came to be written against a cue nothing emitted, or against one player's action
    // reaching both halves of the table, with every gate green and each one found by eye.
    public static string? Table(MatchEventCueView? cue) =>
        MatchCueState.Table(cue) is { } table
            ? $"cue-{table.Kind.ToString().ToLowerInvariant()}{ActorClass(table.Actor)}"
            : null;

    // All three halves a cue can belong to, including neither. Nobody acting was left unmarked,
    // which meant a rule that wanted only the events nobody does had nothing to say so with: it
    // could be written for one half or for the whole table and for nothing else, and the one that
    // needed the third thing was written for the whole table and reached both Decks.
    private static string ActorClass(MatchCueActor actor) =>
        actor switch
        {
            MatchCueActor.Local => " cue-actor-local",
            MatchCueActor.Opponent => " cue-actor-opponent",
            _ => " cue-actor-none",
        };

    // How many cards a Deck is shown to be made of while it is being shuffled: half of them part to
    // one side and half to the other, which is the fewest that reads as two piles rather than two
    // cards.
    public const int RiffleCards = 12;

    public static string RiffleCard(int index) =>
        MatchCueState.RifflePile(index) switch
        {
            MatchRifflePile.Left => "riffle-card is-left",
            _ => "riffle-card is-right",
        };

    // A card's place in the order is what spaces it behind the one before it; the stylesheet owns
    // how long that spacing is, as it owns every other duration on the table.
    public static string RiffleStyle(int index) =>
        $"--riffle-order: {index.ToString(CultureInfo.InvariantCulture)}";

    // Where the landing sits decides where the card it is expecting comes to rest.
    public static string? LandingClass(
        MatchLandingSlot? landing,
        MatchLandingKind kind,
        int index
    ) =>
        MatchCueState.Landing(landing, kind, index) switch
        {
            MatchLandingPlacement.Top => $"{Landing} is-landing-top",
            MatchLandingPlacement.Bottom => $"{Landing} is-landing-bottom",
            MatchLandingPlacement.Centre => $"{Landing} is-landing-centre",
            _ => null,
        };
}
