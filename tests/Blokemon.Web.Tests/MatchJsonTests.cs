using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Blokemon.Web.Content;
using Microsoft.FSharp.Core;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class MatchJsonTests
{
    private static FrozenList<EffectChoice> Choices() =>
        FrozenList<EffectChoice>.Create(
            EffectChoice.NewOptional(new("optional"), true),
            EffectChoice.NewAmount(new("amount"), 2),
            EffectChoice.NewCards(
                new("cards"),
                FrozenList<CardInstanceId>.Create(new CardInstanceId("C1"))
            ),
            EffectChoice.NewMechanicalType(new("type"), BlokemonMechanicalType.Grass),
            EffectChoice.NewAttack(new("attack"), new("effect")),
            EffectChoice.NewDistribution(
                new("distribution"),
                FrozenList<DamageAllocation>.Create(
                    new DamageAllocation(new CardInstanceId("C2"), 3)
                )
            ),
            EffectChoice.NewAttachments(
                new("attachments"),
                FrozenList<VimAttachment>.Create(
                    new VimAttachment(new CardInstanceId("V1"), new CardInstanceId("B1"))
                )
            )
        );

    private static FrozenList<MatchCommand> Commands()
    {
        var choices = Choices();
        var empty = FrozenList<EffectChoice>.Empty;
        var match = new MatchId("match");
        var player = new PlayerId("player");
        var revision = new MatchRevision(7);

        MatchCommand Command(string id, FrozenList<EffectChoice> carried, MatchAction action) =>
            new(new CommandId(id), match, player, revision, carried, action);

        return FrozenList<MatchCommand>.Create(
            Command("choose-mulligan", empty, MatchAction.NewChooseMulliganBonus(2)),
            Command(
                "choose-opening",
                empty,
                MatchAction.NewChooseOpening(
                    new("oche"),
                    FrozenList<CardInstanceId>.Create(new CardInstanceId("booth"))
                )
            ),
            Command("attach-vim", empty, MatchAction.NewAttachVim(new("vim"), new("bloke"))),
            Command("play-bloke", empty, MatchAction.NewPlayBloke(new("bloke"))),
            Command("promote", choices, MatchAction.NewPromote(new("promotion"), new("bloke"))),
            Command(
                "play-kit",
                choices,
                MatchAction.NewPlayKit(
                    new("kit"),
                    FSharpValueOption<CardInstanceId>.Some(new CardInstanceId("target"))
                )
            ),
            Command(
                "play-kit-untargeted",
                choices,
                MatchAction.NewPlayKit(new("kit"), FSharpValueOption<CardInstanceId>.None)
            ),
            Command(
                "taxi",
                empty,
                MatchAction.NewTaxi(
                    new("booth-bloke"),
                    FrozenList<CardInstanceId>.Create(new CardInstanceId("vim-to-chuck"))
                )
            ),
            Command(
                "party-trick",
                choices,
                MatchAction.NewUsePartyTrick(new("source"), new("effect"))
            ),
            Command(
                "attack-command",
                choices,
                MatchAction.NewAttack(new("attacker"), new("attack-effect"))
            ),
            Command("chuck-fossil", empty, MatchAction.NewChuckFossil(new("fossil"))),
            Command("end-round", empty, MatchAction.EndRound),
            Command(
                "choose-replacement",
                empty,
                MatchAction.NewChooseReplacement(new("replacement"))
            ),
            Command("resolve-effect", choices, MatchAction.ResolveEffectChoice),
            Command(
                "resolve-knockout",
                empty,
                MatchAction.NewResolveKnockoutTrigger(
                    FSharpValueOption<CardInstanceId>.Some(new CardInstanceId("vim"))
                )
            ),
            Command(
                "resolve-knockout-declined",
                empty,
                MatchAction.NewResolveKnockoutTrigger(FSharpValueOption<CardInstanceId>.None)
            ),
            Command("resolve-bar-chit", empty, MatchAction.NewResolveBarChitTrigger(true)),
            Command("resign", empty, MatchAction.Resign)
        );
    }

    [Test]
    public async Task CommandLog_RoundTripsEveryCommandAndChoicePayload()
    {
        var commands = Commands();

        var json = JsonSerializer.Serialize(commands, MatchJson.Options);
        var restored = JsonSerializer.Deserialize<FrozenList<MatchCommand>>(
            json,
            MatchJson.Options
        );

        restored.ShouldBe(commands);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Command_WritesTheEnvelopeWithADiscriminatedActionPayload()
    {
        var command = new MatchCommand(
            new CommandId("attack-command"),
            new MatchId("match"),
            new PlayerId("player"),
            new MatchRevision(7),
            FrozenList<EffectChoice>.Create(EffectChoice.NewOptional(new("optional"), true)),
            MatchAction.NewAttack(new("attacker"), new("attack-effect"))
        );

        var json = JsonSerializer.Serialize(command, MatchJson.Options);

        json.ShouldBe(
            """
            {"id":{"value":"attack-command"},"matchId":{"value":"match"},"actor":{"value":"player"},"expectedRevision":{"value":7},"choices":[{"$choice":"optional","choiceId":{"value":"optional"},"isAccepted":true}],"action":{"$command":"attack","attacker":{"value":"attacker"},"attackId":{"value":"attack-effect"}}}
            """
        );
        await Task.CompletedTask;
    }

    [Test]
    public async Task PreBreakCommandPayload_IsRejectedRatherThanMigrated()
    {
        // The pre-break shape put the discriminator on the command itself and inlined the payload.
        const string preBreak = """
            {"$command":"attack","id":{"value":"attack-command"},"matchId":{"value":"match"},"actor":{"value":"player"},"expectedRevision":{"value":7},"choices":[],"attacker":{"value":"attacker"},"attackId":{"value":"attack-effect"}}
            """;

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<MatchCommand>(preBreak, MatchJson.Options)
        );
        await Task.CompletedTask;
    }

    [Test]
    public async Task PreBreakChoicePayload_IsRejectedRatherThanMigrated()
    {
        const string preBreak = """
            {"$choice":"cards","choiceId":{"value":"cards"},"cards":[{"value":"C1"}]}
            """;

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<EffectChoice>(preBreak, MatchJson.Options)
        );
        await Task.CompletedTask;
    }

    [Test]
    public async Task SerializedCommandLog_ReplaysToADeeplyEqualState()
    {
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var engine = new MatchEngine(catalogue.Mechanics);
        var cpu = new DeterministicCpu();
        var first = new PlayerId("first");
        var second = new PlayerId("second");
        var deck = catalogue.StarterDecks.Decks.First().ExpandedCardIds;
        var start = new MatchStartRequest(
            new MatchId("00000000-0000-0000-0000-0000000000ff"),
            new MatchSeed(982451653UL),
            FrozenDeckSnapshot.Create(first, deck),
            FrozenDeckSnapshot.Create(second, deck)
        );

        var started = (MatchStartOutcome.Started)engine.Start(start);
        var state = started.state;
        var commands = new List<MatchCommand>();
        for (var step = 0; step < 40; step++)
        {
            var actor = state.ActivePlayer;
            if (cpu.Choose(engine, state, actor) is not CpuDecision.Selected selected)
            {
                actor = state.Other(actor);
                if (cpu.Choose(engine, state, actor) is not CpuDecision.Selected other)
                {
                    break;
                }

                selected = other;
            }

            if (engine.Apply(state, selected.action.Command) is not CommandOutcome.Applied applied)
            {
                break;
            }

            commands.Add(selected.action.Command);
            state = applied.state;
        }

        var startJson = JsonSerializer.Serialize(start, MatchJson.Options);
        var commandJson = JsonSerializer.Serialize(
            FrozenList<MatchCommand>.Create(commands),
            MatchJson.Options
        );
        var restoredStart = JsonSerializer.Deserialize<MatchStartRequest>(
            startJson,
            MatchJson.Options
        );
        var restoredCommands = JsonSerializer.Deserialize<FrozenList<MatchCommand>>(
            commandJson,
            MatchJson.Options
        );

        var replayedStart = (MatchStartOutcome.Started)engine.Start(restoredStart!);
        var replayed = replayedStart.state;
        foreach (var command in restoredCommands)
        {
            replayed = ((CommandOutcome.Applied)engine.Apply(replayed, command)).state;
        }

        commands.ShouldNotBeEmpty();
        replayed.ShouldBe(state);
        await Task.CompletedTask;
    }
}
