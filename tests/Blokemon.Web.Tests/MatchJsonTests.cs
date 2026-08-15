using System.Text.Json;
using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Tests;

public sealed class MatchJsonTests
{
    [Test]
    public async Task PolymorphicCommandLog_RoundTripsEveryCommandAndChoicePayload()
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
        var match = new MatchId("match");
        var player = new PlayerId("player");
        var revision = new MatchRevision(7);
        var commands = FrozenList<MatchCommand>.Create(
            new MatchCommand.ChooseMulliganBonus(
                new("choose-mulligan"),
                match,
                player,
                revision,
                2
            ),
            new MatchCommand.ChooseOpening(
                new("choose-opening"),
                match,
                player,
                revision,
                new("oche"),
                FrozenList<CardInstanceId>.Create(new CardInstanceId("booth"))
            ),
            new MatchCommand.AttachVim(
                new("attach-vim"),
                match,
                player,
                revision,
                new("vim"),
                new("bloke")
            ),
            new MatchCommand.PlayBloke(new("play-bloke"), match, player, revision, new("bloke")),
            new MatchCommand.Promote(
                new("promote"),
                match,
                player,
                revision,
                new("promotion"),
                new("bloke"),
                choices
            ),
            new MatchCommand.PlayKit(
                new("play-kit"),
                match,
                player,
                revision,
                new("kit"),
                new CardInstanceId("target"),
                choices
            ),
            new MatchCommand.Taxi(
                new("taxi"),
                match,
                player,
                revision,
                new("booth-bloke"),
                FrozenList<CardInstanceId>.Create(new CardInstanceId("vim-to-chuck"))
            ),
            new MatchCommand.UsePartyTrick(
                new("party-trick"),
                match,
                player,
                revision,
                new("source"),
                new("effect"),
                choices
            ),
            new MatchCommand.Attack(
                new("attack-command"),
                match,
                player,
                revision,
                new("attacker"),
                new("attack-effect"),
                choices
            ),
            new MatchCommand.ChuckFossil(
                new("chuck-fossil"),
                match,
                player,
                revision,
                new("fossil")
            ),
            new MatchCommand.EndRound(new("end-round"), match, player, revision),
            new MatchCommand.ChooseReplacement(
                new("choose-replacement"),
                match,
                player,
                revision,
                new("replacement")
            ),
            new MatchCommand.ResolveEffectChoice(
                new("resolve-effect"),
                match,
                player,
                revision,
                choices
            ),
            new MatchCommand.ResolveKnockoutTrigger(
                new("resolve-knockout"),
                match,
                player,
                revision,
                new CardInstanceId("vim")
            ),
            new MatchCommand.ResolveBarChitTrigger(
                new("resolve-bar-chit"),
                match,
                player,
                revision,
                true
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
