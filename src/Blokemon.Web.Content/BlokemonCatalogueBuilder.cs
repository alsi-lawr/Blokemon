using Blokemon.CardGen.Authority;
using Blokemon.CardGen.Domain;
using Blokemon.CardGen.Rendering;
using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;
using Blokemon.PackGen.Catalogue;
using Blokemon.PackGen.Domain;
using Blokemon.PackGen.Rendering;
using Blokemon.Web.Client.Api;

namespace Blokemon.Web.Content;

public static class BlokemonCatalogueBuilder
{
    public static BlokemonCatalogue Load(string contentRoot)
    {
        var authorityRoot = Path.Combine(contentRoot, "authorities");
        var mechanicsJson = File.ReadAllText(Path.Combine(authorityRoot, "mechanics.json"));
        var starterDecksJson = File.ReadAllText(Path.Combine(authorityRoot, "starter-decks.json"));
        var mechanics = BlokemonSetJson.RuntimeManifest(mechanicsJson);
        var publicContent = BlokemonPublicContentJson.Manifest(
            File.ReadAllText(Path.Combine(authorityRoot, "public-content.json"))
        );
        var artRoot = Path.Combine(contentRoot, "art");
        var printedSet = SetAuthority.Load(
            Path.Combine(authorityRoot, "public-content.json"),
            Path.Combine(authorityRoot, "mechanics.json"),
            Path.Combine(authorityRoot, "printing.json"),
            artRoot
        );
        var cardDocument = CardDocument.LoadReferenced(artRoot);
        var mechanicsValidation = BlokemonSetValidator.ValidateRuntime(mechanics);
        if (!mechanicsValidation.IsValid)
        {
            throw new InvalidDataException(
                $"The mechanical authority is invalid: {mechanicsValidation.Issues[0].Message}"
            );
        }
        var publicValidation = BlokemonPublicContentValidator.ValidateDocument(
            publicContent,
            mechanics
        );
        if (!publicValidation.IsValid)
        {
            throw new InvalidDataException(
                $"The public-content authority is invalid: {publicValidation.Issues[0].Message}"
            );
        }

        var publicCollectibles = publicContent.Collectibles.ToDictionary(static card => card.Id);
        var publicKits = publicContent.Supports.ToDictionary(static card => card.Id);
        var publicVim = publicContent.BasicEnergy.ToDictionary(static card =>
            $"VIM-{card.Id["ENERGY-".Length..]}"
        );
        var effects = publicContent
            .Collectibles.SelectMany(static card =>
                card.Abilities.Concat(card.Attacks).Concat(card.Rules)
            )
            .Concat(publicContent.Supports.SelectMany(static card => card.Effects))
            .ToDictionary(static effect => effect.MechanicalId, StringComparer.Ordinal);
        var printedCards = printedSet
            .Blokemon.Concat(printedSet.Supports)
            .Concat(printedSet.Energy)
            .ToDictionary(RuntimeCardId, StringComparer.Ordinal);
        var cards = new Dictionary<string, CardView>(StringComparer.Ordinal);

        foreach (var mechanical in mechanics.Collectibles)
        {
            var presentation = publicCollectibles[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.ApprovedName,
                    CardKindView.Blokemon,
                    presentation.ApprovedType.ToString(),
                    $"{mechanical.ProductBucket} · {mechanical.Rank}",
                    cardDocument.BuildMarkup(printedCards[mechanical.Id]),
                    CollectibleRules(mechanical, presentation),
                    0,
                    false
                )
            );
        }
        foreach (var mechanical in mechanics.Kits)
        {
            var presentation = publicKits[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.Kit,
                    "Kit",
                    mechanical.Kind.ToString(),
                    cardDocument.BuildMarkup(printedCards[mechanical.Id]),
                    KitRules(mechanical, presentation),
                    0,
                    mechanical.FreelyAvailable
                )
            );
        }
        foreach (var mechanical in mechanics.BasicVim)
        {
            var presentation = publicVim[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.BasicVim,
                    mechanical.MechanicalType.ToString(),
                    "Basic Vim",
                    cardDocument.BuildMarkup(printedCards[mechanical.Id]),
                    [
                        new(
                            CardRuleKindView.Energy,
                            "Basic Energy",
                            presentation.Definition,
                            [],
                            null
                        ),
                    ],
                    0,
                    mechanical.FreelyAvailable
                )
            );
        }

        return BlokemonCatalogue.Create(
            mechanicsJson,
            starterDecksJson,
            publicContent.ContentVersion,
            cardDocument.Stylesheet,
            cardDocument.BuildMarkup(printedSet.Reverse),
            DrawPackPresentation(),
            cards.Values,
            effects.Values.Select(static effect => new CatalogueEffect(
                effect.MechanicalId,
                effect.Name,
                effect.EffectText
            ))
        );
    }

    private static CardRuleView[] CollectibleRules(
        BlokemonCollectible mechanical,
        BlokemonPublicCollectible presentation
    )
    {
        var attacks = mechanical.Attacks.ToDictionary(
            static attack => attack.MechanicalId,
            StringComparer.Ordinal
        );
        return
        [
            .. presentation.Abilities.Select(static effect =>
                PublicRule(CardRuleKindView.Ability, effect)
            ),
            .. presentation.Attacks.Select(effect =>
                AttackRule(effect, attacks[effect.MechanicalId])
            ),
            .. presentation.Rules.Select(static effect =>
                PublicRule(CardRuleKindView.Rule, effect)
            ),
        ];
    }

    private static CardRuleView[] KitRules(
        BlokemonKit mechanical,
        BlokemonPublicSupport presentation
    )
    {
        var abilities = mechanical
            .PartyTricks.Select(static effect => effect.MechanicalId)
            .ToHashSet(StringComparer.Ordinal);
        var attacks = mechanical.Attacks.ToDictionary(
            static attack => attack.MechanicalId,
            StringComparer.Ordinal
        );
        var rules = mechanical
            .HouseRules.Select(static effect => effect.MechanicalId)
            .ToHashSet(StringComparer.Ordinal);
        return presentation
            .Effects.Select(effect =>
            {
                if (abilities.Contains(effect.MechanicalId))
                {
                    return PublicRule(CardRuleKindView.Ability, effect);
                }
                if (attacks.TryGetValue(effect.MechanicalId, out var attack))
                {
                    return AttackRule(effect, attack);
                }
                if (rules.Contains(effect.MechanicalId))
                {
                    return PublicRule(CardRuleKindView.Rule, effect);
                }
                throw new InvalidDataException(
                    $"The authority does not classify effect {effect.MechanicalId}."
                );
            })
            .ToArray();
    }

    private static CardRuleView PublicRule(CardRuleKindView kind, BlokemonPublicEffect effect) =>
        new(kind, effect.Name, effect.EffectText, [], null);

    private static CardRuleView AttackRule(BlokemonPublicEffect effect, BlokemonAttack attack) =>
        new(
            CardRuleKindView.Attack,
            effect.Name,
            effect.EffectText,
            attack.VimCost.Select(static cost => cost.ToString()).ToArray(),
            attack.PrintedDamage == 0 ? null : attack.PrintedDamage
        );

    private static string RuntimeCardId(ICard card) =>
        card.Id.Value.StartsWith("ENERGY-", StringComparison.Ordinal)
            ? $"VIM-{card.Id.Value["ENERGY-".Length..]}"
            : card.Id.Value;

    private static PackPresentationView DrawPackPresentation() =>
        new(DrawPackStock(PackStock.Gloss), DrawPackStock(PackStock.Kraft));

    private static PackStockPresentationView DrawPackStock(PackStock stock)
    {
        var profile = PackProfile.Blokemon(stock);
        return new(
            PackArt.Draw(PackCatalogue.Get(PackKey.Booster), profile),
            PackArt.Draw(PackCatalogue.Get(PackKey.StarterDeck), profile),
            CartonArt.DrawTray(PackCatalogue.Get(PackKey.StarterDeck), profile)
        );
    }
}
