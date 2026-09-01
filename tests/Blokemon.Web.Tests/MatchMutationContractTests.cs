using System.Text.Json;
using Blokemon.App.Contracts;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class MatchMutationContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public void StartRequestWithoutDifficulty_DefaultsToNormal()
    {
        var request = JsonSerializer.Deserialize<StartMatchRequest>(
            """
            {"commandId":"30000000-0000-0000-0000-000000000001","deckId":"20000000-0000-0000-0000-000000000001"}
            """,
            Json
        );

        request.ShouldNotBeNull();
        request.Difficulty.ShouldBe(CpuDifficultyView.Normal);
    }

    [Test]
    public void AppliedMutation_PreservesTheTwoArgumentConstructorAndOriginalWireShape()
    {
        var application = Application();
        var constructor = typeof(MatchMutationView).GetConstructor([
            typeof(ApplicationView),
            typeof(MatchPresentationView),
        ]);
        constructor.ShouldNotBeNull();
        var mutation = (MatchMutationView)constructor.Invoke([application, null]);
        var applicationJson = JsonSerializer.Serialize(application, Json);

        var wire = JsonSerializer.Serialize(mutation, Json);
        var restored = JsonSerializer.Deserialize<MatchMutationView>(wire, Json);

        wire.ShouldBe($"{{\"application\":{applicationJson},\"presentation\":null}}");
        restored.ShouldNotBeNull();
        restored.Outcome.ShouldBe(MatchMutationOutcomeView.Applied);
        JsonSerializer.Serialize(restored, Json).ShouldBe(wire);
    }

    [Test]
    public void RecoveryRequiredMutation_RetainsItsTypedOutcomeOnTheWire()
    {
        var application = Application();
        var mutation = new MatchMutationView(
            application,
            null,
            MatchMutationOutcomeView.RecoveryRequired
        );
        var applicationJson = JsonSerializer.Serialize(application, Json);

        var wire = JsonSerializer.Serialize(mutation, Json);
        var restored = JsonSerializer.Deserialize<MatchMutationView>(wire, Json);

        wire.ShouldBe($"{{\"application\":{applicationJson},\"presentation\":null,\"outcome\":1}}");
        restored.ShouldNotBeNull();
        restored.Outcome.ShouldBe(MatchMutationOutcomeView.RecoveryRequired);
        JsonSerializer.Serialize(restored, Json).ShouldBe(wire);
    }

    private static ApplicationView Application()
    {
        var stock = new PackStockPresentationView(string.Empty, string.Empty, string.Empty);
        return new(null, [], [], [], new(stock, stock), null, null, null);
    }
}
