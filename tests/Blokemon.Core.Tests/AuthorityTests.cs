using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;

namespace Blokemon.Core.Tests;

public sealed class AuthorityTests
{
    private static readonly Lazy<BlokemonRuntimeManifest> _mechanics = new(() =>
        BlokemonSetJson.RuntimeManifest(ReadAuthority("mechanics.json"))
    );

    private static readonly Lazy<BlokemonPublicContentManifest> _publicContent = new(() =>
        BlokemonPublicContentJson.Manifest(ReadAuthority("public-content.json"))
    );

    [Test]
    public async Task CurrentAuthorities_PassOwnedValidation()
    {
        await Assert.That(BlokemonSetValidator.ValidateRuntime(_mechanics.Value).IsValid).IsTrue();
        await Assert
            .That(
                BlokemonPublicContentValidator
                    .ValidateDocument(_publicContent.Value, _mechanics.Value)
                    .IsValid
            )
            .IsTrue();
    }

    [Test]
    public async Task ElevenCardSampling_IsDeterministicAndPreservesPackComposition()
    {
        var first = new BlokemonSeededRandom(0xB10CE188UL);
        var replay = new BlokemonSeededRandom(0xB10CE188UL);
        var cards = _mechanics.Value.Collectibles.ToDictionary(static card => card.Id);

        for (var sample = 0; sample < 256; sample++)
        {
            var pack = BlokemonPackSampler.SampleEleven(_mechanics.Value, first);
            var repeated = BlokemonPackSampler.SampleEleven(_mechanics.Value, replay);

            await Assert.That(pack.SequenceEqual(repeated)).IsTrue();
            await Assert.That(pack.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(11);
            await Assert
                .That(
                    pack.Select(id => cards[id])
                        .Count(static card => card.ProductBucket == BlokemonProductBucket.Rare)
                )
                .IsEqualTo(1);
            await Assert
                .That(
                    pack.Select(id => cards[id])
                        .Count(static card => card.ProductBucket == BlokemonProductBucket.Uncommon)
                )
                .IsEqualTo(3);
            await Assert
                .That(
                    pack.Select(id => cards[id])
                        .Count(static card => card.ProductBucket == BlokemonProductBucket.Common)
                )
                .IsEqualTo(7);
        }

        await Assert.That(first.ConsumptionIndex).IsEqualTo(replay.ConsumptionIndex);
    }

    [Test]
    public async Task RuntimeValidation_RejectsAChangedRoadieAffinity()
    {
        var roadie = _mechanics.Value.Collectibles.Single(static card => card.Id == "BLK-035");
        var changed = _mechanics.Value with
        {
            Collectibles =
            [
                .. _mechanics.Value.Collectibles.Where(static card => card.Id != "BLK-035"),
                roadie with
                {
                    SoftSpots = [],
                },
            ],
        };

        var result = BlokemonSetValidator.ValidateRuntime(changed);

        await Assert.That(result.IsValid).IsFalse();
        await Assert
            .That(result.Issues.Any(static issue => issue.Code == "runtime.roadie-soft-spots"))
            .IsTrue();
    }

    [Test]
    public async Task RuntimeAuthority_RejectsUnknownFields()
    {
        var document = JsonNode.Parse(ReadAuthority("mechanics.json"))!.AsObject();
        document["unsupported"] = true;

        await Assert
            .That(() => BlokemonSetJson.RuntimeManifest(document.ToJsonString()))
            .Throws<JsonException>();
    }

    private static string ReadAuthority(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", name));
}
