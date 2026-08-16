using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.Core.PublicContent;
using Blokemon.Core.SetDesign;
using Shouldly;

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
        BlokemonSetValidator.ValidateRuntime(_mechanics.Value).IsValid.ShouldBeTrue();
        BlokemonPublicContentValidator
            .ValidateDocument(_publicContent.Value, _mechanics.Value)
            .IsValid.ShouldBeTrue();
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

            pack.SequenceEqual(repeated).ShouldBeTrue();
            pack.Distinct(StringComparer.Ordinal).Count().ShouldBe(11);
            pack.Select(id => cards[id])
                .Count(static card => card.ProductBucket == BlokemonProductBucket.Rare)
                .ShouldBe(1);
            pack.Select(id => cards[id])
                .Count(static card => card.ProductBucket == BlokemonProductBucket.Uncommon)
                .ShouldBe(3);
            pack.Select(id => cards[id])
                .Count(static card => card.ProductBucket == BlokemonProductBucket.Common)
                .ShouldBe(7);
        }

        first.ConsumptionIndex.ShouldBe(replay.ConsumptionIndex);
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

        result.IsValid.ShouldBeFalse();
        result.Issues.Any(static issue => issue.Code == "runtime.roadie-soft-spots").ShouldBeTrue();
    }

    [Test]
    public async Task RuntimeAuthority_RejectsUnknownFields()
    {
        var document = JsonNode.Parse(ReadAuthority("mechanics.json"))!.AsObject();
        document["unsupported"] = true;

        Should.Throw<JsonException>(() => BlokemonSetJson.RuntimeManifest(document.ToJsonString()));
    }

    private static string ReadAuthority(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", name));
}
