using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
public sealed partial class MatchMotionStylesheetTests
{
    [Test]
    public async Task EveryRunThatCanPlayOnADepartedCardSaysWhereItStarts()
    {
        var css = await Stylesheet();
        var overridden = GoneOverrides(css);
        var keyframes = Keyframes(css);

        // Whatever the mark for having left holds a card at, said out loud, so that adding a
        // property to it widens this rather than leaving a second one of these to be found by eye.
        overridden.ShouldContain("opacity");

        var examined = 0;
        var unsaid = new List<string>();
        foreach (var animation in CueAnimations(css).Distinct(StringComparer.Ordinal).Order())
        {
            if (!keyframes.TryGetValue(animation, out var run))
            {
                continue;
            }

            foreach (var property in run.Animates.Intersect(overridden, StringComparer.Ordinal))
            {
                examined++;
                if (!run.Opens.Contains(property, StringComparer.Ordinal))
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

    private static async Task<string> Stylesheet()
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
            var stylesheet = StyleAsset()
                .Matches(html)
                .Select(static match => match.Groups[1].Value)
                .Single(static asset =>
                    Path.GetFileName(asset).StartsWith("app.", StringComparison.Ordinal)
                );

            return await client.GetStringAsync(stylesheet);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    // What the mark for a card the presentation has carried off holds that card at.
    private static IReadOnlyList<string> GoneOverrides(string css) =>
        [
            .. GoneRule()
                .Matches(css)
                .SelectMany(match => Properties(match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal),
        ];

    // Every run a card can be given by a cue naming it, which is every run that can find itself
    // playing on a card the presentation has already taken out of somewhere.
    private static IEnumerable<string> CueAnimations(string css) =>
        CueRule()
            .Matches(css)
            .Select(static match => match.Groups[2].Value)
            .Where(static name => name is not "none");

    private static IEnumerable<string> Properties(string declarations) =>
        Property().Matches(declarations).Select(static match => match.Groups[1].Value);

    // What each run touches, and what it says for itself at its own beginning rather than leaving
    // to be filled in.
    private static Dictionary<string, (HashSet<string> Animates, HashSet<string> Opens)> Keyframes(
        string css
    )
    {
        var runs = new Dictionary<string, (HashSet<string>, HashSet<string>)>(
            StringComparer.Ordinal
        );
        foreach (Match run in KeyframesRule().Matches(css))
        {
            var animates = new HashSet<string>(StringComparer.Ordinal);
            var opens = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match step in KeyframeStep().Matches(run.Groups[2].Value))
            {
                var properties = Properties(step.Groups[2].Value).ToArray();
                animates.UnionWith(properties);
                var offsets = step.Groups[1]
                    .Value.Split(',')
                    .Select(static offset => offset.Trim());
                if (offsets.Any(static offset => offset is "0%" or "from"))
                {
                    opens.UnionWith(properties);
                }
            }

            runs[run.Groups[1].Value] = (animates, opens);
        }

        return runs;
    }

    [GeneratedRegex("href=\"([^\"]+\\.css)\"")]
    private static partial Regex StyleAsset();

    [GeneratedRegex("[^{}]*is-cue-gone[^{}]*\\{([^{}]*)\\}")]
    private static partial Regex GoneRule();

    [GeneratedRegex("([^{}]*is-cue-[^{}]*)\\{[^{}]*animation(?:-name)?:\\s*([\\w-]+)")]
    private static partial Regex CueRule();

    [GeneratedRegex("@keyframes\\s+([\\w-]+)\\s*\\{((?:[^{}]|\\{[^{}]*\\})*)\\}")]
    private static partial Regex KeyframesRule();

    [GeneratedRegex("([^{}]+)\\{([^{}]*)\\}")]
    private static partial Regex KeyframeStep();

    [GeneratedRegex("(?:^|;)\\s*([a-z-]+)\\s*:")]
    private static partial Regex Property();
}
