namespace Blokemon.Core.SetDesign;

public sealed record BlokemonValidationIssue(string Code, string Message);

public sealed record BlokemonValidationResult(BlokemonValidationIssue[] Issues)
{
    public bool IsValid => Issues.Length == 0;
}

public static class BlokemonSetValidator
{
    public static BlokemonValidationResult ValidateRuntime(BlokemonRuntimeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<BlokemonValidationIssue>();

        Check(
            manifest.PresentationStatus == BlokemonPresentationStatus.PlaceholderBacked,
            "runtime.presentation",
            "Presentation must remain deferred to placeholder-backed publication.",
            issues
        );
        Check(
            manifest.Collectibles.Length == 151,
            "runtime.collectible-count",
            "The runtime manifest must contain exactly 151 collectible identities.",
            issues
        );
        Check(
            manifest.Kits.Length == 14,
            "runtime.kit-count",
            "The runtime manifest must contain exactly 14 fixed kit definitions.",
            issues
        );
        Check(
            manifest.BasicVim.Length == 7,
            "runtime.vim-count",
            "The runtime manifest must contain exactly seven Basic Vim definitions.",
            issues
        );

        ValidateCollectibles(manifest, issues);
        ValidateSupport(manifest, issues);
        ValidateProducts(manifest, issues);
        ValidateRules(manifest, issues);
        return new BlokemonValidationResult([.. issues]);
    }

    private static void ValidateCollectibles(
        BlokemonRuntimeManifest manifest,
        List<BlokemonValidationIssue> issues
    )
    {
        Check(
            manifest.Collectibles.Select(static card => card.Id).Distinct().Count()
                == manifest.Collectibles.Length,
            "runtime.collectible-id",
            "Collectible mechanical IDs must be unique.",
            issues
        );
        Check(
            manifest.Collectibles.All(static card =>
                card.PresentationStatus == BlokemonPresentationStatus.PlaceholderBacked
            ),
            "runtime.collectible-presentation",
            "Every collectible must prevent presentation before placeholder-backed publication.",
            issues
        );
        Check(
            manifest.ApprovedMechanicalDisplayMap.Length
                == Enum.GetValues<BlokemonMechanicalType>().Length
                && manifest
                    .ApprovedMechanicalDisplayMap.Select(static value => value.MechanicalType)
                    .Distinct()
                    .Count() == Enum.GetValues<BlokemonMechanicalType>().Length
                && manifest
                    .ApprovedMechanicalDisplayMap.Select(static value => value.ApprovedLabel)
                    .Distinct()
                    .Count() == Enum.GetValues<BlokemonApprovedMechanicalLabel>().Length
                && manifest
                    .ApprovedMechanicalDisplayMap.Single(static value =>
                        value.MechanicalType == BlokemonMechanicalType.Metal
                    )
                    .ApprovedLabel == BlokemonApprovedMechanicalLabel.Roadie,
            "runtime.mechanical-display-map",
            "Every internal mechanical type must have one approved display label and Metal must display as Roadie.",
            issues
        );

        var roadieSoftSpots = manifest
            .Collectibles.Where(static card =>
                card.SoftSpots.Any(static modifier =>
                    modifier.MechanicalType == BlokemonMechanicalType.Metal
                )
            )
            .Select(static card => card.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Check(
            roadieSoftSpots.SequenceEqual(["BLK-035", "BLK-036", "BLK-124"]),
            "runtime.roadie-soft-spots",
            "Internal Metal must appear on exactly the three D-224 Roadie soft-spot surfaces.",
            issues
        );

        var roadieSelector = manifest.Collectibles.SingleOrDefault(static card =>
            card.Id == "BLK-137"
        );
        var selectableMetal =
            roadieSelector is not null
            && roadieSelector
                .PartyTricks.Select(static trick => trick.Program)
                .Concat(roadieSelector.Attacks.Select(static attack => attack.Program))
                .Concat(roadieSelector.HouseRules.Select(static rule => rule.Program))
                .SelectMany(Flatten)
                .Any(static instruction =>
                    instruction.MechanicalTypes.Contains(BlokemonMechanicalType.Metal)
                );
        Check(
            selectableMetal,
            "runtime.roadie-selection",
            "BLK-137 must retain internal Metal in the D-224 Roadie selectable-type mechanic.",
            issues
        );

        var programs = manifest
            .Collectibles.SelectMany(static card =>
                card.PartyTricks.Select(static value => value.Program)
                    .Concat(card.Attacks.Select(static value => value.Program))
                    .Concat(card.HouseRules.Select(static value => value.Program))
            )
            .Concat(
                manifest.Kits.SelectMany(static card =>
                    card.PartyTricks.Select(static value => value.Program)
                        .Concat(card.Attacks.Select(static value => value.Program))
                        .Concat(card.HouseRules.Select(static value => value.Program))
                )
            )
            .ToArray();
        Check(
            programs.Length == 310,
            "runtime.program-count",
            "The typed manifest must structurally define all 310 mechanical programs.",
            issues
        );
        Check(
            programs.All(static program => program.Length > 0),
            "runtime.program-empty",
            "Every mechanical program must contain at least one typed instruction.",
            issues
        );
        Check(
            programs.SelectMany(Flatten).All(InstructionIsClosed),
            "runtime.program-shape",
            "Every instruction must use a finite, internally consistent typed shape.",
            issues
        );
    }

    private static void ValidateSupport(
        BlokemonRuntimeManifest manifest,
        List<BlokemonValidationIssue> issues
    )
    {
        Check(
            manifest.Kits.All(static card =>
                card.PresentationStatus == BlokemonPresentationStatus.PlaceholderBacked
                && card.FreelyAvailable
                && !card.Owned
                && !card.Pulled
                && !card.Traded
            ),
            "runtime.kit-boundary",
            "Kits must be free, non-owned, non-pulled, non-traded and presentation-deferred.",
            issues
        );
        Check(
            manifest.BasicVim.All(static card =>
                card.PresentationStatus == BlokemonPresentationStatus.PlaceholderBacked
                && card.FreelyAvailable
                && !card.Owned
                && !card.Pulled
                && !card.Traded
            ),
            "runtime.vim-boundary",
            "Basic Vim must be free, non-owned, non-pulled, non-traded and presentation-deferred.",
            issues
        );
    }

    private static void ValidateProducts(
        BlokemonRuntimeManifest manifest,
        List<BlokemonValidationIssue> issues
    )
    {
        var products = manifest.Products;
        Check(
            products.Single.Count == 1
                && products.Single.NamedIdentityOdds.Numerator == 1
                && products.Single.NamedIdentityOdds.Denominator == 151,
            "runtime.single-product",
            "The one-card product must be uniform across all 151 identities.",
            issues
        );
        Check(
            products.Eleven.Count == 11
                && products.Eleven.WithoutReplacementWithinPack
                && !products.Eleven.Pity
                && products.Eleven.DuplicatesAcrossPacks,
            "runtime.eleven-product",
            "The eleven-card product must be no-pity and without replacement within one pack.",
            issues
        );
        var expected = new[]
        {
            new BlokemonProductSlot(BlokemonProductBucket.Rare, 1, 49),
            new BlokemonProductSlot(BlokemonProductBucket.Uncommon, 3, 49),
            new BlokemonProductSlot(BlokemonProductBucket.Common, 7, 53),
        };
        Check(
            products.Eleven.Slots.SequenceEqual(expected),
            "runtime.product-slots",
            "The eleven-card product must use one Rare, three Uncommon and seven Common slots.",
            issues
        );
        foreach (var slot in expected)
        {
            Check(
                manifest.Collectibles.Count(card => card.ProductBucket == slot.Bucket)
                    == slot.PoolSize,
                "runtime.product-pool",
                $"The {slot.Bucket} product pool must contain {slot.PoolSize} identities.",
                issues
            );
        }
    }

    private static void ValidateRules(
        BlokemonRuntimeManifest manifest,
        List<BlokemonValidationIssue> issues
    )
    {
        var rules = manifest.BaseRules;
        Check(
            rules.Stack.CardCount == 60
                && rules.Opening.BarChitCount == 6
                && rules.Opening.OpeningParticipantSampledBeforeShuffle,
            "runtime.base-rules",
            "The mechanical rules must retain 60 cards, six bar chits and opening-side sampling before shuffles.",
            issues
        );
        Check(
            rules.OpcodeInventory.Distinct().Count() == Enum.GetValues<BlokemonOpcode>().Length
                && rules
                    .OpcodeInventory.Order()
                    .SequenceEqual(Enum.GetValues<BlokemonOpcode>().Order()),
            "runtime.opcode-inventory",
            "The runtime rules must list every finite opcode exactly once.",
            issues
        );
    }

    private static bool InstructionIsClosed(BlokemonEffectInstruction instruction) =>
        instruction.TargetCount >= 0
        && instruction.Predicates.All(static predicate => predicate.Value >= 0)
        && instruction.Then.All(InstructionIsClosed)
        && instruction.Otherwise.All(InstructionIsClosed);

    private static IEnumerable<BlokemonEffectInstruction> Flatten(
        IEnumerable<BlokemonEffectInstruction> instructions
    )
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            foreach (var nested in Flatten(instruction.Then))
            {
                yield return nested;
            }
            foreach (var nested in Flatten(instruction.Otherwise))
            {
                yield return nested;
            }
        }
    }

    private static void Check(
        bool condition,
        string code,
        string message,
        List<BlokemonValidationIssue> issues
    )
    {
        if (!condition)
        {
            issues.Add(new BlokemonValidationIssue(code, message));
        }
    }
}
