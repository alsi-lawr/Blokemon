namespace Blokemon.Web.Tests;

// One element of a rendered table: what it is, what it is marked with, and where it sits. This is
// what a selector is matched against, so that asking whether a rule reaches anything is the same
// question the browser answers rather than a search for its text.
internal sealed class MatchElement
{
    public required string Tag { get; init; }

    public required IReadOnlyList<string> Classes { get; init; }

    public MatchElement? Parent { get; set; }

    public List<MatchElement> Children { get; } = [];

    public int SiblingIndex { get; set; }

    // Where this element is on the table, said as the marks it and everything above it carry -
    // which is what the rules are written against, so a contract naming one of these names the
    // same thing the stylesheet does. The table itself is left off: every path would start with it.
    public string Path => Parent?.Parent is null ? Describe() : $"{Parent.Path} > {Describe()}";

    public IEnumerable<MatchElement> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var further in child.Descendants())
            {
                yield return further;
            }
        }
    }

    public bool Within(string cssClass) =>
        Parent is not null
        && (Parent.Classes.Contains(cssClass, StringComparer.Ordinal) || Parent.Within(cssClass));

    private string Describe() => Classes.Count == 0 ? $"<{Tag}>" : string.Join('.', Classes);
}

// A selector, read as the browser reads it rather than as a string to be searched for.
//
// Only the shapes this stylesheet actually uses are understood, and anything else throws rather
// than quietly matching nothing: a check whose reading of a rule silently comes up empty reports
// exactly what a rule nothing emits reports, which is the confusion this whole exercise exists to
// end.
internal sealed class MatchSelector
{
    private readonly IReadOnlyList<Compound> _compounds;

    // True where the compound before it must be the immediate parent rather than any ancestor.
    private readonly IReadOnlyList<bool> _child;

    private MatchSelector(IReadOnlyList<Compound> compounds, IReadOnlyList<bool> child)
    {
        _compounds = compounds;
        _child = child;
    }

    // Whether this selector picks a card out by name, or reaches whatever it structurally lands on.
    // A rule of the second sort applies to every one of a thing on the table, so where it can reach
    // is decided entirely by how it is written.
    public bool NamesACard => _compounds.Any(static compound => compound.NamesACard);

    public static MatchSelector Parse(string selector)
    {
        var compounds = new List<Compound>();
        var child = new List<bool>();
        var reader = new Reader(selector);
        var immediate = false;
        while (true)
        {
            reader.SkipSpace();
            if (reader.Done)
            {
                break;
            }

            if (reader.Take('>'))
            {
                immediate = true;
                continue;
            }

            compounds.Add(Compound.Parse(reader));
            if (compounds.Count > 1)
            {
                child.Add(immediate);
            }

            immediate = false;
        }

        if (compounds.Count == 0)
        {
            throw new NotSupportedException($"empty selector '{selector}'");
        }

        return new(compounds, child);
    }

    public bool Matches(MatchElement element) => Matches(element, _compounds.Count - 1);

    private bool Matches(MatchElement element, int index)
    {
        if (!_compounds[index].Matches(element))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        if (_child[index - 1])
        {
            return element.Parent is not null && Matches(element.Parent, index - 1);
        }

        for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (Matches(ancestor, index - 1))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Compound(
        string? Tag,
        IReadOnlyList<string> Classes,
        IReadOnlyList<Compound> Not,
        IReadOnlyList<Compound> HasChild,
        bool FirstChild,
        // A press is never held during a static reading of the table, so a rule that only applies
        // while one is reaches nothing here - which is the truth about it, not an omission.
        bool Hover
    )
    {
        // The marks a presentation puts on one card in particular, wherever in the selector they
        // are said - including inside a ':has()', which is how a press is found by the card it is
        // holding rather than by anything about itself.
        public bool NamesACard =>
            Classes.Any(static cssClass =>
                cssClass.StartsWith("is-cue-", StringComparison.Ordinal) || cssClass == "is-drawn"
            )
            || Not.Any(static compound => compound.NamesACard)
            || HasChild.Any(static compound => compound.NamesACard);

        public bool Matches(MatchElement element) =>
            (Tag is null || string.Equals(Tag, element.Tag, StringComparison.OrdinalIgnoreCase))
            && Classes.All(cssClass => element.Classes.Contains(cssClass, StringComparer.Ordinal))
            && (!FirstChild || element.SiblingIndex == 0)
            && !Hover
            && Not.All(excluded => !excluded.Matches(element))
            && HasChild.All(required => element.Children.Any(required.Matches));

        public static Compound Parse(Reader reader)
        {
            string? tag = null;
            var classes = new List<string>();
            var not = new List<Compound>();
            var hasChild = new List<Compound>();
            var firstChild = false;
            var hover = false;
            if (reader.PeekIsIdentifier)
            {
                tag = reader.Identifier();
            }

            while (!reader.Done)
            {
                if (reader.Take('.'))
                {
                    classes.Add(reader.Identifier());
                    continue;
                }

                if (!reader.Take(':'))
                {
                    break;
                }

                var pseudo = reader.Identifier();
                switch (pseudo)
                {
                    case "not":
                        reader.Expect('(');
                        not.Add(Parse(reader));
                        reader.Expect(')');
                        break;
                    case "has":
                        reader.Expect('(');
                        reader.SkipSpace();
                        if (!reader.Take('>'))
                        {
                            throw new NotSupportedException(
                                $"only ':has(> ...)' is understood, in '{reader.Text}'"
                            );
                        }

                        reader.SkipSpace();
                        hasChild.Add(Parse(reader));
                        reader.Expect(')');
                        break;
                    case "first-child":
                        firstChild = true;
                        break;
                    case "hover":
                        hover = true;
                        break;
                    default:
                        throw new NotSupportedException($"':{pseudo}' in '{reader.Text}'");
                }
            }

            if (
                tag is null
                && classes.Count == 0
                && not.Count == 0
                && hasChild.Count == 0
                && !firstChild
                && !hover
            )
            {
                throw new NotSupportedException($"nothing to match on in '{reader.Text}'");
            }

            return new(tag, classes, not, hasChild, firstChild, hover);
        }
    }

    private sealed class Reader(string text)
    {
        private int _index;

        public string Text { get; } = text;

        public bool Done => _index >= Text.Length;

        public bool PeekIsIdentifier =>
            !Done && (char.IsAsciiLetter(Text[_index]) || Text[_index] == '*');

        public void SkipSpace()
        {
            while (!Done && Text[_index] == ' ')
            {
                _index++;
            }
        }

        public bool Take(char character)
        {
            if (Done || Text[_index] != character)
            {
                return false;
            }

            _index++;
            return true;
        }

        public void Expect(char character)
        {
            if (!Take(character))
            {
                throw new NotSupportedException($"expected '{character}' in '{Text}'");
            }
        }

        public string Identifier()
        {
            var start = _index;
            while (!Done && (char.IsAsciiLetterOrDigit(Text[_index]) || Text[_index] is '-' or '_'))
            {
                _index++;
            }

            if (_index == start)
            {
                throw new NotSupportedException($"expected a name at {start} in '{Text}'");
            }

            return Text[start.._index];
        }
    }
}
