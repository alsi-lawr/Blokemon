namespace Blokemon.Web.Tests;

// What the shipped stylesheet does to a table while a cue is on screen.
//
// Every rule keyed on a cue is matched against the table as the presenters actually drew it, and
// what it lands on is said back as the place on the table and what happens there. A rule written
// for a mark nothing emits reaches nothing and says nothing; a rule that reaches further than it
// should says so somewhere it was not expected. Neither can be seen by reading either half alone,
// which is the whole reason this exists: the class the rules are keyed on is composed at runtime
// out of an enum member's name, so no literal for it exists in any file to be searched for.
//
// Lengths of time are deliberately not carried through. What is said back is which properties a
// rule sets and what the run it starts is called - never how long anything takes.
internal sealed class MatchCueMotion
{
    private readonly IReadOnlyList<(MatchStyleRule Rule, MatchSelector Selector)> _cueRules;

    public MatchCueMotion(MatchStylesheet stylesheet)
    {
        _cueRules =
        [
            .. stylesheet
                .Rules.Where(static rule =>
                    rule.Selector.Contains("cue-", StringComparison.Ordinal)
                )
                .Select(static rule => (rule, MatchSelector.Parse(rule.Selector))),
        ];
    }

    public int Rules => _cueRules.Count;

    // Everything the cue on screen does to this table, one line per place it reaches.
    public IReadOnlyList<string> OnTheTable(MatchElement screen) =>
        [
            .. screen
                .Descendants()
                .Select(element => (element, effects: Effects(element)))
                .Where(static reached => reached.effects.Count > 0)
                .Select(static reached =>
                    $"{reached.element.Path} :: {string.Join(", ", reached.effects)}"
                )
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    // Which of the rules written for a cue actually find something on this table.
    public IEnumerable<string> Reaching(MatchElement screen) =>
        from cueRule in _cueRules
        where screen.Descendants().Any(cueRule.Selector.Matches)
        select cueRule.Rule.Selector;

    // The rules that reach one half of the table by structure alone rather than by naming a card.
    // A rule of that sort applies to every one of a thing it can find, so where it stops is decided
    // entirely by how it is written - and one player's action reaching the other player's furniture
    // is the shape of a defect that shipped.
    public IEnumerable<(string Selector, MatchElement Element)> ReachedWithoutNamingACard(
        MatchElement screen
    ) =>
        from element in screen.Descendants()
        from cueRule in _cueRules
        where !cueRule.Selector.NamesACard && cueRule.Selector.Matches(element)
        select (cueRule.Rule.Selector, element);

    private SortedSet<string> Effects(MatchElement element)
    {
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var runs = new SortedSet<string>(StringComparer.Ordinal);
        var timed = false;
        foreach (var (rule, selector) in _cueRules)
        {
            if (!selector.Matches(element))
            {
                continue;
            }

            foreach (var property in rule.Properties)
            {
                if (property.StartsWith("animation", StringComparison.Ordinal))
                {
                    timed = true;
                }
                else
                {
                    effects.Add(property);
                }
            }

            foreach (var animation in rule.Animations)
            {
                runs.Add($"animation:{animation}");
            }
        }

        if (runs.Count > 0)
        {
            effects.UnionWith(runs);
        }
        else if (timed)
        {
            // A rule that says how a run should be played without saying which run it is: the name
            // comes from somewhere else, and until it does nothing is named here either.
            effects.Add("animation");
        }

        return effects;
    }
}
