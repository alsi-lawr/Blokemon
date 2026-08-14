using System.Text.RegularExpressions;
using Blokemon.Core.SetDesign;

namespace Blokemon.Core.PublicContent;

public static class BlokemonPublicContentValidator
{
    public const string SchemaVersion = "blokemon-public-content-schema-2.0.0-candidate.5";
    public const string ContentVersion = "blokemon-public-content-2.0.0-candidate.5";
    public const string TerminologyVersion = "blokemon-public-terminology-2.0.0-candidate.5";
    private const string _artAuthority = "Blokemon";

    private static readonly string[] _rejectedMechanicsVocabulary =
    [
        "Barney",
        "Bother",
        "Staying Power",
        "Party Trick",
        "Vim",
        "Stack",
        "Oche",
        "Booth",
        "Soft Spot",
        "Stubborn Streak",
        "Taxi",
        "Bar Chit",
        "Sent Home",
        "Promotion",
        "Mitt",
        "Empties Tray",
        "Chuck",
        "Rough",
        "beer mat",
        "badge side",
        "blank side",
        "other side",
        "bloke",
    ];

    private static readonly IReadOnlyDictionary<BlokemonPublicTermCategory, int> _termCounts =
        new Dictionary<BlokemonPublicTermCategory, int>
        {
            [BlokemonPublicTermCategory.Type] = 10,
            [BlokemonPublicTermCategory.Stage] = 3,
            [BlokemonPublicTermCategory.Category] = 4,
            [BlokemonPublicTermCategory.Status] = 5,
            [BlokemonPublicTermCategory.Target] = 17,
            [BlokemonPublicTermCategory.Choice] = 8,
            [BlokemonPublicTermCategory.Quantity] = 11,
            [BlokemonPublicTermCategory.Cost] = 4,
            [BlokemonPublicTermCategory.Timing] = 17,
            [BlokemonPublicTermCategory.Core] = 22,
            [BlokemonPublicTermCategory.BattleTiming] = 20,
        };

    public static BlokemonPublicContentValidation ValidateDocument(
        BlokemonPublicContentManifest manifest,
        BlokemonRuntimeManifest mechanics
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(mechanics);
        var issues = new List<BlokemonPublicContentIssue>();

        Check(
            manifest.SchemaVersion == SchemaVersion,
            "document.schema",
            "The public schema version is not candidate.5.",
            issues
        );
        Check(
            manifest.ContentVersion == ContentVersion,
            "document.version",
            "The public content version is not candidate.5.",
            issues
        );
        Check(
            manifest.TerminologyVersion == TerminologyVersion,
            "document.terminology-version",
            "The terminology version is not candidate.5.",
            issues
        );
        Check(
            manifest.MechanicalManifestVersion == mechanics.ManifestVersion,
            "document.mechanical-version",
            "The public manifest must bind the exact mechanical manifest version.",
            issues
        );
        Check(
            manifest.HumanApprovalStatus == BlokemonPublicContentApprovalStatus.AwaitingApproval,
            "document.approval",
            "Candidate.5 must remain pending exact human signoff.",
            issues
        );
        ValidateTerminology(manifest, issues);
        ValidateCollectibles(manifest, mechanics, issues);
        ValidateSupports(manifest, mechanics, issues);
        ValidateBasicEnergy(manifest, mechanics, issues);
        ValidatePublicStrings(manifest, issues);
        ValidateMechanicsVocabulary(manifest, issues);
        return new BlokemonPublicContentValidation([.. issues]);
    }

    private static void ValidateTerminology(
        BlokemonPublicContentManifest manifest,
        List<BlokemonPublicContentIssue> issues
    )
    {
        Check(
            manifest.Terminology.Length == _termCounts.Values.Sum(),
            "terminology.count",
            "The terminology table has the wrong total cardinality.",
            issues
        );
        Check(
            manifest
                .Terminology.Select(static term => term.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == manifest.Terminology.Length,
            "terminology.id",
            "Every terminology ID must be unique.",
            issues
        );
        foreach (var pair in _termCounts)
        {
            var terms = manifest.Terminology.Where(term => term.Category == pair.Key).ToArray();
            Check(
                terms.Length == pair.Value,
                "terminology.category-count",
                $"{pair.Key} must contain exactly {pair.Value} terms.",
                issues
            );
            var prefix = $"TERM-{CategoryToken(pair.Key)}-";
            Check(
                terms
                    .Select(static term => term.Id)
                    .SequenceEqual(
                        Enumerable.Range(1, pair.Value).Select(index => $"{prefix}{index:000}")
                    ),
                "terminology.order",
                $"{pair.Key} terminology IDs must be complete and deterministic.",
                issues
            );
        }

        var roadie = manifest.Terminology.SingleOrDefault(static term =>
            term.Id == "TERM-TYPE-010"
        );
        Check(
            roadie?.Singular == "Roadie" && roadie.Plural == "Roadie",
            "terminology.roadie",
            "D224 Roadie must remain the tenth effect-only type term.",
            issues
        );

        var requiredTerms = new[]
        {
            "HP",
            "Ability",
            "Attack",
            "Damage",
            "Energy",
            "Deck",
            "Active Blokemon",
            "Bench",
            "Weakness",
            "Resistance",
            "Retreat",
            "Prize Card",
            "Knocked Out",
            "Evolution",
            "Item",
            "Tool",
            "Supporter",
            "Stadium",
        };
        var publicLabels = manifest.Terminology.SelectMany(static term =>
            new[] { term.Singular, term.Plural }
        );
        foreach (var requiredTerm in requiredTerms)
        {
            Check(
                publicLabels.Contains(requiredTerm, StringComparer.Ordinal),
                "terminology.required",
                $"The public terminology is missing {requiredTerm}.",
                issues
            );
        }
    }

    private static void ValidateCollectibles(
        BlokemonPublicContentManifest manifest,
        BlokemonRuntimeManifest mechanics,
        List<BlokemonPublicContentIssue> issues
    )
    {
        Check(
            manifest.Collectibles.Length == 151,
            "collectible.count",
            "There must be exactly 151 public collectible entries.",
            issues
        );
        for (
            var index = 0;
            index < Math.Min(manifest.Collectibles.Length, mechanics.Collectibles.Length);
            index++
        )
        {
            var content = manifest.Collectibles[index];
            var mechanical = mechanics.Collectibles[index];
            Check(
                content.Id == mechanical.Id,
                "collectible.id",
                $"Collectible {index + 1} is not bound to the locked ID.",
                issues
            );
            Check(
                content.ApprovedName == mechanical.ApprovedName,
                "collectible.name",
                $"{mechanical.Id} changed its D223 name.",
                issues
            );
            Check(
                content.ApprovedType == mechanical.ApprovedType,
                "collectible.type",
                $"{mechanical.Id} changed its D223 type.",
                issues
            );
            Check(
                content.Art.Status == BlokemonPublicArtStatus.Placeholder
                    && content.Art.Authority == _artAuthority,
                "collectible.art",
                $"{mechanical.Id} must expose only the placeholder-art boundary.",
                issues
            );
            ValidateEffects(
                content.Id,
                "ability",
                content.Abilities,
                mechanical.PartyTricks.Select(static effect => new ExpectedEffect(
                    effect.MechanicalId,
                    effect.Program,
                    CanOmitText: false
                )),
                issues
            );
            ValidateEffects(
                content.Id,
                "attack",
                content.Attacks,
                mechanical.Attacks.Select(static effect => new ExpectedEffect(
                    effect.MechanicalId,
                    effect.Program,
                    CanOmitText: BlokemonAttackSemantics.IsPureDamageAttack(effect),
                    CanBeUsedFromBench: effect.CanBeUsedFromBench
                )),
                issues
            );
            ValidateEffects(
                content.Id,
                "rule",
                content.Rules,
                mechanical.HouseRules.Select(static effect => new ExpectedEffect(
                    effect.MechanicalId,
                    effect.Program,
                    CanOmitText: false
                )),
                issues
            );
        }

        var namedEffects = manifest
            .Collectibles.SelectMany(static card => card.Abilities.Concat(card.Attacks))
            .ToArray();
        Check(
            namedEffects
                .Select(static effect => effect.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == namedEffects.Length,
            "collectible.effect-name",
            "Every Blokemon Ability and Attack name must remain individually authored and unique.",
            issues
        );
        Check(
            manifest
                .Collectibles.Select(static card => card.FlavourText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 151,
            "collectible.flavour",
            "Every flavour line must be individually authored.",
            issues
        );
        Check(
            manifest
                .Collectibles.Select(static card => card.Illustration.Brief)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 151,
            "collectible.brief",
            "Every illustration brief must be individually authored.",
            issues
        );
        Check(
            manifest
                .Collectibles.Select(static card => card.Illustration.Prompt)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 151,
            "collectible.prompt",
            "Every illustration prompt must be individually authored.",
            issues
        );
        Check(
            manifest
                .Collectibles.Select(static card => card.Illustration.AltIntent)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 151,
            "collectible.alt",
            "Every alt intent must be individually authored.",
            issues
        );
    }

    private static void ValidateSupports(
        BlokemonPublicContentManifest manifest,
        BlokemonRuntimeManifest mechanics,
        List<BlokemonPublicContentIssue> issues
    )
    {
        Check(
            manifest.Supports.Length == 14,
            "support.count",
            "There must be exactly 14 public support entries.",
            issues
        );
        for (
            var index = 0;
            index < Math.Min(manifest.Supports.Length, mechanics.Kits.Length);
            index++
        )
        {
            var content = manifest.Supports[index];
            var mechanical = mechanics.Kits[index];
            Check(
                content.Id == mechanical.Id,
                "support.id",
                $"Support {index + 1} is not bound to the locked ID.",
                issues
            );
            var expectedCategory = mechanical.Kind switch
            {
                BlokemonKitKind.BarBit => "TERM-CATEGORY-001",
                BlokemonKitKind.BarKit => "TERM-CATEGORY-002",
                BlokemonKitKind.Mate => "TERM-CATEGORY-003",
                BlokemonKitKind.Local => "TERM-CATEGORY-004",
                _ => throw new ArgumentOutOfRangeException(nameof(mechanics)),
            };
            Check(
                content.CategoryTermId == expectedCategory,
                "support.category",
                $"{content.Id} has the wrong public support category.",
                issues
            );
            var expectedEffects = mechanical
                .PartyTricks.Select(static effect => new ExpectedEffect(
                    effect.MechanicalId,
                    effect.Program,
                    CanOmitText: false
                ))
                .Concat(
                    mechanical.Attacks.Select(static effect => new ExpectedEffect(
                        effect.MechanicalId,
                        effect.Program,
                        CanOmitText: false
                    ))
                )
                .Concat(
                    mechanical.HouseRules.Select(static effect => new ExpectedEffect(
                        effect.MechanicalId,
                        effect.Program,
                        CanOmitText: false
                    ))
                );
            ValidateEffects(content.Id, "effect", content.Effects, expectedEffects, issues);
        }

        Check(
            manifest
                .Supports.Select(static support => support.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 14,
            "support.name",
            "Every support must retain an individually authored name.",
            issues
        );
    }

    private static void ValidateBasicEnergy(
        BlokemonPublicContentManifest manifest,
        BlokemonRuntimeManifest mechanics,
        List<BlokemonPublicContentIssue> issues
    )
    {
        Check(
            manifest.BasicEnergy.Length == 7,
            "energy.count",
            "There must be exactly seven Basic Energy entries.",
            issues
        );
        for (
            var index = 0;
            index < Math.Min(manifest.BasicEnergy.Length, mechanics.BasicVim.Length);
            index++
        )
        {
            var content = manifest.BasicEnergy[index];
            var mechanical = mechanics.BasicVim[index];
            var approvedLabel = mechanics
                .ApprovedMechanicalDisplayMap.Single(mapping =>
                    mapping.MechanicalType == mechanical.MechanicalType
                )
                .ApprovedLabel.ToString();
            Check(
                content.Id == $"ENERGY-{approvedLabel.ToUpperInvariant()}",
                "energy.id",
                $"Basic Energy {index + 1} has the wrong public ID.",
                issues
            );
            Check(
                content.SymbolKey == $"energy-{approvedLabel.ToLowerInvariant()}",
                "energy.symbol",
                $"{content.Id} has the wrong public symbol key.",
                issues
            );
            Check(
                content.AccessibleLabel.EndsWith(", Basic Energy", StringComparison.Ordinal),
                "energy.accessibility",
                $"{content.Id} does not use the standard Basic Energy accessibility label.",
                issues
            );
        }
        Check(
            manifest
                .BasicEnergy.Select(static energy => energy.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 7,
            "energy.name",
            "Every Basic Energy entry must retain an individually authored name.",
            issues
        );
        Check(
            manifest
                .BasicEnergy.Select(static energy => energy.SymbolKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == 7,
            "energy.symbol",
            "Every Basic Energy entry must have a unique symbol key.",
            issues
        );
    }

    private static void ValidateEffects(
        string ownerId,
        string kind,
        BlokemonPublicEffect[] content,
        IEnumerable<ExpectedEffect> expectedEffects,
        List<BlokemonPublicContentIssue> issues
    )
    {
        var expected = expectedEffects.ToArray();
        Check(
            content
                .Select(static effect => effect.MechanicalId)
                .SequenceEqual(expected.Select(static effect => effect.MechanicalId)),
            "effect.mechanical-id",
            $"{ownerId} public {kind} IDs or order do not match the locked mechanical authority.",
            issues
        );
        for (var index = 0; index < Math.Min(content.Length, expected.Length); index++)
        {
            var effect = content[index];
            var authority = expected[index];
            Check(
                !string.IsNullOrWhiteSpace(effect.Name),
                "effect.name",
                $"{effect.MechanicalId} has no public name.",
                issues
            );
            if (authority.CanOmitText)
            {
                Check(
                    effect.EffectText is null,
                    "effect.pure-damage",
                    $"{effect.MechanicalId} is a pure-Damage Attack and must omit effectText.",
                    issues
                );
                continue;
            }

            Check(
                !string.IsNullOrWhiteSpace(effect.EffectText),
                "effect.text",
                $"{effect.MechanicalId} has no exact public effect text.",
                issues
            );
            if (string.IsNullOrWhiteSpace(effect.EffectText))
            {
                continue;
            }
            Check(
                char.IsUpper(effect.EffectText[0]) && ".!?".Contains(effect.EffectText[^1]),
                "effect.grammar",
                $"{effect.MechanicalId} effect text must be a complete, capitalised sentence.",
                issues
            );
            Check(
                !authority.CanBeUsedFromBench
                    || effect.EffectText.Contains("Bench", StringComparison.Ordinal),
                "effect.bench-declaration",
                $"{effect.MechanicalId} must state its typed Bench declaration permission.",
                issues
            );
        }
    }

    private static void ValidatePublicStrings(
        BlokemonPublicContentManifest manifest,
        List<BlokemonPublicContentIssue> issues
    )
    {
        foreach (var value in PublicStrings(manifest))
        {
            Check(
                value == value.Trim() && value.Length > 0,
                "text.trim",
                "Public strings must be non-empty and trimmed.",
                issues
            );
            Check(
                !Uri.TryCreate(value, UriKind.Absolute, out _),
                "text.uri",
                "Public content cannot contain an absolute URI.",
                issues
            );
        }
    }

    private static void ValidateMechanicsVocabulary(
        BlokemonPublicContentManifest manifest,
        List<BlokemonPublicContentIssue> issues
    )
    {
        foreach (var value in MechanicsStrings(manifest))
        {
            foreach (var rejected in _rejectedMechanicsVocabulary)
            {
                Check(
                    !ContainsTerm(value, rejected),
                    "text.rejected-mechanics-term",
                    $"Public mechanics text contains rejected candidate.2 vocabulary: {rejected}.",
                    issues
                );
            }
        }
    }

    private static IEnumerable<string> MechanicsStrings(BlokemonPublicContentManifest manifest) =>
        manifest
            .Terminology.SelectMany(static term =>
                new[] { term.Singular, term.Plural, term.Definition }
            )
            .Concat(
                manifest.Collectibles.SelectMany(static card =>
                    card.Abilities.Concat(card.Attacks)
                        .Concat(card.Rules)
                        .Select(static effect => effect.EffectText)
                        .OfType<string>()
                )
            )
            .Concat(
                manifest.Supports.SelectMany(static support =>
                    support.Effects.Select(static effect => effect.EffectText).OfType<string>()
                )
            )
            .Concat(
                manifest.BasicEnergy.SelectMany(static energy =>
                    new[] { energy.Definition, energy.AccessibleLabel }
                )
            );

    private static IEnumerable<string> PublicStrings(BlokemonPublicContentManifest manifest)
    {
        yield return manifest.SchemaVersion;
        yield return manifest.ContentVersion;
        yield return manifest.MechanicalManifestVersion;
        yield return manifest.TerminologyVersion;
        foreach (var term in manifest.Terminology)
        {
            yield return term.Id;
            yield return term.Singular;
            yield return term.Plural;
            yield return term.Definition;
        }
        foreach (var card in manifest.Collectibles)
        {
            yield return card.Id;
            yield return card.ApprovedName;
            yield return card.FlavourText;
            foreach (var effect in card.Abilities.Concat(card.Attacks).Concat(card.Rules))
            {
                yield return effect.MechanicalId;
                yield return effect.Name;
                if (effect.EffectText is not null)
                {
                    yield return effect.EffectText;
                }
            }
            yield return card.Illustration.Brief;
            yield return card.Illustration.Prompt;
            yield return card.Illustration.AltIntent;
            yield return card.Art.Authority;
        }
        foreach (var support in manifest.Supports)
        {
            yield return support.Id;
            yield return support.Name;
            yield return support.CategoryTermId;
            foreach (var effect in support.Effects)
            {
                yield return effect.MechanicalId;
                yield return effect.Name;
                if (effect.EffectText is not null)
                {
                    yield return effect.EffectText;
                }
            }
        }
        foreach (var energy in manifest.BasicEnergy)
        {
            yield return energy.Id;
            yield return energy.Name;
            yield return energy.Definition;
            yield return energy.SymbolKey;
            yield return energy.AccessibleLabel;
        }
    }

    private static bool ContainsTerm(string value, string term) =>
        Regex.IsMatch(
            value,
            $@"(?<![A-Za-z]){Regex.Escape(term)}(?:s)?(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

    private static string CategoryToken(BlokemonPublicTermCategory category) =>
        category switch
        {
            BlokemonPublicTermCategory.BattleTiming => "BATTLE-TIMING",
            _ => category.ToString().ToUpperInvariant(),
        };

    private static void Check(
        bool condition,
        string code,
        string message,
        List<BlokemonPublicContentIssue> issues
    )
    {
        if (!condition)
        {
            issues.Add(new BlokemonPublicContentIssue(code, message));
        }
    }

    private sealed record ExpectedEffect(
        string MechanicalId,
        BlokemonEffectInstruction[] Program,
        bool CanOmitText,
        bool CanBeUsedFromBench = false
    );
}
