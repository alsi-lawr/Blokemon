using Shouldly;

namespace Blokemon.Web.Tests;

// A keyframe left out is not left out: the browser fills it in from the card as it stands, which
// is exactly the thing the presentation has been arranging to be untrue. A card being carried off
// is marked as already gone - that is what stops a second one of it being drawn - and a fade
// written as "to nothing", with no first keyframe, then reads that mark as where it starts and
// spends its whole run at nothing. The card vanished instead of falling, and the stylesheet said
// what it had always said.
//
// Nothing else catches this. The mark is applied, the rule matches, the animation runs, and it
// runs the full duration; the only thing wrong is the value it begins at, and sampling the end of
// it - which is nothing either way - agrees with both the fade and the vanish.
public sealed class MatchMotionStylesheetTests
{
    [Test]
    public async Task EveryRunThatCanPlayOnADepartedCardSaysWhereItStarts()
    {
        var stylesheet = await MatchStylesheet.Shipped();
        var overridden = Held(stylesheet);

        // Whatever the mark for having left holds a card at, said out loud, so that adding a
        // property to it widens this rather than leaving a second one of these to be found by eye.
        overridden.ShouldContain("opacity");

        var examined = 0;
        var unsaid = new List<string>();
        foreach (var animation in Cued(stylesheet).Distinct(StringComparer.Ordinal).Order())
        {
            if (!stylesheet.Keyframes.TryGetValue(animation, out var run))
            {
                continue;
            }

            foreach (var property in run.Animates.Intersect(overridden, StringComparer.Ordinal))
            {
                examined++;
                if (!run.Opens.Contains(property))
                {
                    unsaid.Add($"{animation} animates {property} without saying its first value");
                }
            }
        }

        // Something was actually looked at: a reading of the stylesheet that quietly matched
        // nothing would otherwise pass this by finding nothing wrong with nothing.
        examined.ShouldBeGreaterThan(0);
        unsaid.ShouldBeEmpty();
    }

    // What the mark for a card the presentation has carried off holds that card at.
    private static IReadOnlyList<string> Held(MatchStylesheet stylesheet) =>
        [
            .. stylesheet
                .Rules.Where(static rule =>
                    rule.Selector.Contains("is-cue-gone", StringComparison.Ordinal)
                )
                .SelectMany(static rule => rule.Properties)
                .Distinct(StringComparer.Ordinal),
        ];

    // Every run a card can be given by a cue naming it, which is every run that can find itself
    // playing on a card the presentation has already taken out of somewhere.
    private static IEnumerable<string> Cued(MatchStylesheet stylesheet) =>
        stylesheet
            .Rules.Where(static rule => rule.Selector.Contains("is-cue-", StringComparison.Ordinal))
            .SelectMany(static rule => rule.Animations);
}
