using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Blokemon.Web.Tests;

// One rule of the shipped stylesheet, as one selector: a rule written for several selectors at
// once is the same rule said several times, and each of them reaches whatever it reaches on its
// own. Durations are deliberately not kept - what is recorded is which properties a rule sets and,
// where it starts a run, what that run is called. Nothing here can pin a length of time.
internal sealed record MatchStyleRule(
    string Selector,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> Animations
);

// What one named run touches, and what it says for itself at its own beginning rather than
// leaving to be filled in from the card as it stands.
internal sealed record MatchKeyframeRun(IReadOnlySet<string> Animates, IReadOnlySet<string> Opens);

// The stylesheet the product actually serves, read as rules rather than as text.
//
// It is fetched from a running site rather than read off disk, so what is examined is what a
// browser would be given, and it is parsed once here for every check that needs it: a second
// reading of the same file is a second answer waiting to disagree with the first.
//
// Only rules at the top level are kept. A rule inside an at-rule - the reduced-motion block, a
// width breakpoint - is a different question about the same selector and is asked separately.
internal sealed class MatchStylesheet
{
    private MatchStylesheet(
        IReadOnlyList<MatchStyleRule> rules,
        IReadOnlyDictionary<string, MatchKeyframeRun> keyframes
    )
    {
        Rules = rules;
        Keyframes = keyframes;
    }

    public IReadOnlyList<MatchStyleRule> Rules { get; }

    public IReadOnlyDictionary<string, MatchKeyframeRun> Keyframes { get; }

    public static async Task<MatchStylesheet> Shipped()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"motion-stylesheet-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Blokemon:DataDirectory"] = dataDirectory,
                            }
                        )
                );
            });
            using var client = factory.CreateClient();
            var html = await client.GetStringAsync("/");
            return Parse(await client.GetStringAsync(StyleAsset(html)));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    private static string StyleAsset(string html)
    {
        var assets = new List<string>();
        var index = 0;
        while (true)
        {
            var href = html.IndexOf("href=\"", index, StringComparison.Ordinal);
            if (href < 0)
            {
                break;
            }

            var start = href + 6;
            var end = html.IndexOf('"', start);
            var asset = html[start..end];
            if (asset.EndsWith(".css", StringComparison.Ordinal))
            {
                assets.Add(asset);
            }

            index = end;
        }

        return assets.Single(static asset =>
            Path.GetFileName(asset).StartsWith("app.", StringComparison.Ordinal)
        );
    }

    internal static MatchStylesheet Parse(string css)
    {
        var text = WithoutComments(css);
        var rules = new List<MatchStyleRule>();
        var keyframes = new Dictionary<string, MatchKeyframeRun>(StringComparer.Ordinal);
        Read(text, 0, text.Length, rules, keyframes, nested: false);
        return new(rules, keyframes);
    }

    private static void Read(
        string css,
        int from,
        int to,
        List<MatchStyleRule> rules,
        Dictionary<string, MatchKeyframeRun> keyframes,
        bool nested
    )
    {
        var index = from;
        var preludeStart = from;
        while (index < to)
        {
            var character = css[index];
            if (character == ';')
            {
                index++;
                preludeStart = index;
                continue;
            }

            if (character != '{')
            {
                index++;
                continue;
            }

            var prelude = css[preludeStart..index].Trim();
            var close = Close(css, index, to);
            var body = (Start: index + 1, End: close);
            if (prelude.StartsWith("@keyframes", StringComparison.Ordinal))
            {
                keyframes[prelude["@keyframes".Length..].Trim()] = Run(css, body.Start, body.End);
            }
            else if (prelude.StartsWith('@'))
            {
                // A rule inside a breakpoint or a motion preference is a different question about
                // the same selector, so its keyframes are collected and its rules are not.
                Read(css, body.Start, body.End, rules, keyframes, nested: true);
            }
            else if (!nested)
            {
                var declarations = Declarations(css[body.Start..body.End]);
                foreach (var selector in prelude.Split(','))
                {
                    if (Normalised(selector) is { Length: > 0 } one)
                    {
                        rules.Add(new(one, declarations.Properties, declarations.Animations));
                    }
                }
            }

            index = close + 1;
            preludeStart = index;
        }
    }

    private static int Close(string css, int open, int to)
    {
        var depth = 0;
        for (var index = open; index < to; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return index;
            }
        }

        return to;
    }

    private static MatchKeyframeRun Run(string css, int from, int to)
    {
        var animates = new HashSet<string>(StringComparer.Ordinal);
        var opens = new HashSet<string>(StringComparer.Ordinal);
        var index = from;
        var preludeStart = from;
        while (index < to)
        {
            if (css[index] != '{')
            {
                index++;
                continue;
            }

            var offsets = css[preludeStart..index].Trim();
            var close = Close(css, index, to);
            var properties = Declarations(css[(index + 1)..close]).Properties;
            animates.UnionWith(properties);
            if (
                offsets
                    .Split(',')
                    .Select(static offset => offset.Trim())
                    .Any(static offset => offset is "0%" or "from")
            )
            {
                opens.UnionWith(properties);
            }

            index = close + 1;
            preludeStart = index;
        }

        return new(animates, opens);
    }

    private static (
        IReadOnlyList<string> Properties,
        IReadOnlyList<string> Animations
    ) Declarations(string body)
    {
        var properties = new List<string>();
        var animations = new List<string>();
        foreach (var declaration in body.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var property = declaration[..colon].Trim();
            if (
                property.Length == 0
                || !property.All(static c => char.IsAsciiLetter(c) || c == '-')
            )
            {
                continue;
            }

            properties.Add(property);
            if (property is not ("animation" or "animation-name"))
            {
                continue;
            }

            // The name always comes first in this stylesheet's shorthand, which is where the
            // browser looks for it too: everything after it is a length of time or a curve, and
            // nothing here is allowed to depend on either.
            var name = declaration[(colon + 1)..].Trim().Split(' ')[0].Trim();
            if (name.Length > 0 && name != "none")
            {
                animations.Add(name);
            }
        }

        return (properties, animations);
    }

    private static string WithoutComments(string css)
    {
        var stripped = new StringBuilder(css.Length);
        var index = 0;
        while (index < css.Length)
        {
            if (css[index] == '/' && index + 1 < css.Length && css[index + 1] == '*')
            {
                var end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 2;
                stripped.Append(' ');
                continue;
            }

            stripped.Append(css[index]);
            index++;
        }

        return stripped.ToString();
    }

    private static string Normalised(string selector)
    {
        var normalised = new StringBuilder(selector.Length);
        var space = false;
        foreach (var character in selector.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space && normalised.Length > 0)
            {
                normalised.Append(' ');
            }

            space = false;
            normalised.Append(character);
        }

        return normalised.ToString();
    }
}
