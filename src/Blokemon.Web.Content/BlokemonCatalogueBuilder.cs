using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.CardGen.Authority;
using Blokemon.CardGen.Domain;
using Blokemon.CardGen.Rendering;
using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;
using Blokemon.PackGen.Catalogue;
using Blokemon.PackGen.Domain;
using Blokemon.PackGen.Rendering;

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
        // A card is bound to its approved illustration, which is what the set authority above is
        // read against. What the browser is sent is the delivered form of that same picture, which
        // is derived from it and lives beside it; see packaging/art/derive-web-art.py.
        var cardDocument = CardDocument.LoadReferenced(Path.Combine(contentRoot, "art-web"));
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
        var publicTrainers = publicContent.Trainers.ToDictionary(static card => card.Id);
        var publicEnergy = publicContent.Energy.ToDictionary(static card =>
            $"VIM-{card.Id["ENERGY-".Length..]}"
        );
        var effects = publicContent
            .Collectibles.SelectMany(static card =>
                card.PokemonPowers.Concat(card.Attacks).Concat(card.Rules)
            )
            .Concat(publicContent.Trainers.SelectMany(static card => card.Effects))
            .ToDictionary(static effect => effect.MechanicalId, StringComparer.Ordinal);
        var printedCards = printedSet
            .Blokemon.Concat(printedSet.Trainers)
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
                    CollectibleRules(mechanics, mechanical, presentation),
                    0,
                    false
                )
            );
        }
        foreach (var mechanical in mechanics.Kits)
        {
            var presentation = publicTrainers[mechanical.Id];
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.Trainer,
                    "Trainer",
                    "Trainer",
                    cardDocument.BuildMarkup(printedCards[mechanical.Id]),
                    KitRules(mechanics, mechanical, presentation),
                    0,
                    mechanical.FreelyAvailable
                )
            );
        }
        foreach (var mechanical in mechanics.BasicVim)
        {
            var presentation = publicEnergy[mechanical.Id];
            var energyKind = mechanical.IsBasic ? "Basic Energy" : "Special Energy";
            cards.Add(
                mechanical.Id,
                new(
                    mechanical.Id,
                    presentation.Name,
                    CardKindView.Energy,
                    BlokemonMechanicalDisplay
                        .ApprovedLabel(mechanics, mechanical.MechanicalType)
                        .ToString(),
                    energyKind,
                    cardDocument.BuildMarkup(printedCards[mechanical.Id]),
                    [new(CardRuleKindView.Energy, energyKind, presentation.Definition, [], null)],
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
        BlokemonRuntimeManifest mechanics,
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
            .. presentation.PokemonPowers.Select(static effect =>
                PublicRule(CardRuleKindView.PokemonPower, effect)
            ),
            .. presentation.Attacks.Select(effect =>
                AttackRule(mechanics, effect, attacks[effect.MechanicalId])
            ),
            .. presentation.Rules.Select(static effect =>
                PublicRule(CardRuleKindView.Rule, effect)
            ),
        ];
    }

    private static CardRuleView[] KitRules(
        BlokemonRuntimeManifest mechanics,
        BlokemonKit mechanical,
        BlokemonPublicTrainer presentation
    )
    {
        var powers = mechanical
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
                if (powers.Contains(effect.MechanicalId))
                {
                    return PublicRule(CardRuleKindView.PokemonPower, effect);
                }
                if (attacks.TryGetValue(effect.MechanicalId, out var attack))
                {
                    return AttackRule(mechanics, effect, attack);
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

    private static CardRuleView AttackRule(
        BlokemonRuntimeManifest mechanics,
        BlokemonPublicEffect effect,
        BlokemonAttack attack
    ) =>
        new(
            CardRuleKindView.Attack,
            effect.Name,
            effect.EffectText,
            attack
                .VimCost.Select(cost =>
                    BlokemonMechanicalDisplay.ApprovedLabel(mechanics, cost).ToString()
                )
                .ToArray(),
            attack.PrintedDamage == 0 ? null : attack.PrintedDamage
        );

    private static string RuntimeCardId(Card card) =>
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
