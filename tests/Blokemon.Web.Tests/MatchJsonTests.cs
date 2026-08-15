using System.Text.Json;
using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Tests;

public sealed class MatchJsonTests
{
    [Test]
    public async Task PolymorphicCommandLog_RoundTripsEveryChoicePayload()
    {
        var choices = FrozenList<EffectChoice>.Create(
            new EffectChoice.Optional(new("optional"), true),
            new EffectChoice.Amount(new("amount"), 2),
            new EffectChoice.Cards(
                new("cards"),
                FrozenList<CardInstanceId>.Create(new CardInstanceId("C1"))
            ),
            new EffectChoice.MechanicalType(new("type"), BlokemonMechanicalType.Grass),
            new EffectChoice.Attack(new("attack"), new("effect")),
            new EffectChoice.Distribution(
                new("distribution"),
                FrozenList<DamageAllocation>.Create(
                    new DamageAllocation(new CardInstanceId("C2"), 3)
                )
            ),
            new EffectChoice.Attachments(
                new("attachments"),
                FrozenList<VimAttachment>.Create(
                    new VimAttachment(new CardInstanceId("V1"), new CardInstanceId("B1"))
                )
            )
        );
        var commands = FrozenList<MatchCommand>.Create(
            new MatchCommand.Attack(
                new("command"),
                new("match"),
                new("player"),
                new(7),
                new("attacker"),
                new("attack-effect"),
                choices
            )
        );

        var json = JsonSerializer.Serialize(commands, MatchJson.Options);
        var restored = JsonSerializer.Deserialize<FrozenList<MatchCommand>>(
            json,
            MatchJson.Options
        );

        await Assert.That(restored).IsEqualTo(commands);
    }
}
