using System.Collections.Immutable;
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
    private static ImmutableArray<EffectChoice> Choices() =>
        ImmutableArray.Create(
            EffectChoice.NewOptional(new("optional"), true),
            EffectChoice.NewAmount(new("amount"), 2),
            EffectChoice.NewCards(new("cards"), ImmutableArray.Create(new CardInstanceId("C1"))),
            EffectChoice.NewMechanicalType(new("type"), BlokemonMechanicalType.Grass),
            EffectChoice.NewAttack(new("attack"), new("effect")),
            EffectChoice.NewDistribution(
                new("distribution"),
                ImmutableArray.Create(new DamageAllocation(new CardInstanceId("C2"), 3))
            ),
            EffectChoice.NewAttachments(
                new("attachments"),
                ImmutableArray.Create(
                    new VimAttachment(new CardInstanceId("V1"), new CardInstanceId("B1"))
                )
            )
        );

    private static ImmutableArray<MatchCommand> Commands()
    {
        var choices = Choices();
        var empty = ImmutableArray<EffectChoice>.Empty;
        var match = new MatchId("match");
        var player = new PlayerId("player");
        var revision = new MatchRevision(7);

        MatchCommand Command(string id, ImmutableArray<EffectChoice> carried, MatchAction action) =>
            new(new CommandId(id), match, player, revision, carried, action);

        return ImmutableArray.Create(
            Command("choose-mulligan", empty, MatchAction.NewChooseMulliganBonus(2)),
            Command(
                "choose-opening",
                empty,
                MatchAction.NewChooseOpening(
                    new("oche"),
                    ImmutableArray.Create(new CardInstanceId("booth"))
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
                    ImmutableArray.Create(new CardInstanceId("vim-to-chuck"))
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
        var restored = JsonSerializer.Deserialize<ImmutableArray<MatchCommand>>(
            json,
            MatchJson.Options
        );

        // ImmutableArray's own == and Equals are reference equality over the backing array, so the
        // round trip is asserted element-wise; each MatchCommand is an F# record and compares
        // structurally, including through its own ImmutableArray members.
        restored.SequenceEqual(commands).ShouldBeTrue();
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
            ImmutableArray.Create(EffectChoice.NewOptional(new("optional"), true)),
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

    // A stored document whose nested collection members were never written: the two deck
    // snapshots inside the start request, and a command's choices. Everything the document type
    // itself declares is present, because each of those members carries JsonRequired.
    private const string DocumentWithAbsentCollectionMembers = """
        {"schemaVersion":2,"authorityVersion":"authority-1","startCommand":{"clientCommandId":"30000000-0000-0000-0000-000000000001","deckId":"20000000-0000-0000-0000-000000000001","fingerprint":"start","startRequestFingerprint":"game-start"},"start":{"matchId":{"value":"match"},"seed":{"value":7},"firstDeck":{"owner":{"value":"player"}},"secondDeck":{"owner":{"value":"cpu"}}},"commands":[{"id":{"value":"resign"},"matchId":{"value":"match"},"actor":{"value":"player"},"expectedRevision":{"value":0},"action":{"$command":"resign"}}],"clientCommands":[]}
        """;

    [Test]
    public async Task DocumentWithAbsentCollectionMembers_NormalizesToEmptyCollections()
    {
        var parsed = JsonSerializer.Deserialize<MatchDocument>(
            DocumentWithAbsentCollectionMembers,
            MatchJson.Options
        )!;

        var normalized = MatchDocumentNormalization.matchDocument(parsed);

        // What the deserializer left behind, and what the ingress makes of it.
        parsed.Start.FirstDeck.Cards.IsDefault.ShouldBeTrue();
        parsed.Start.SecondDeck.Cards.IsDefault.ShouldBeTrue();
        parsed.Commands[0].Choices.IsDefault.ShouldBeTrue();
        normalized.Start.FirstDeck.Cards.ShouldBeEmpty();
        normalized.Start.SecondDeck.Cards.ShouldBeEmpty();
        normalized.Commands[0].Choices.ShouldBeEmpty();
        normalized.ClientCommands.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task HistoryWithAbsentCollectionMembers_NormalizesEveryArchivedBattle()
    {
        var history = $$"""
            {"schemaVersion":2,"authorityVersion":"authority-1","matches":[{{DocumentWithAbsentCollectionMembers}}]}
            """;

        var normalized = MatchDocumentNormalization.historyDocument(
            JsonSerializer.Deserialize<MatchHistoryDocument>(history, MatchJson.Options)!
        );

        var archived = normalized.Matches.Single();
        archived.Start.FirstDeck.Cards.ShouldBeEmpty();
        archived.Start.SecondDeck.Cards.ShouldBeEmpty();
        archived.Commands[0].Choices.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task NormalizedDocument_WritesTheAbsentCollectionsAsEmptyArrays()
    {
        var normalized = MatchDocumentNormalization.matchDocument(
            JsonSerializer.Deserialize<MatchDocument>(
                DocumentWithAbsentCollectionMembers,
                MatchJson.Options
            )!
        );

        var json = JsonSerializer.Serialize(normalized, MatchJson.Options);

        // The same bytes FrozenList<T> wrote for a defaulted member, which is what keeps the
        // stored schema unchanged when a normalized document is written back out.
        json.ShouldBe(
            """
            {"schemaVersion":2,"authorityVersion":"authority-1","startCommand":{"clientCommandId":"30000000-0000-0000-0000-000000000001","deckId":"20000000-0000-0000-0000-000000000001","fingerprint":"start","startRequestFingerprint":"game-start"},"start":{"matchId":{"value":"match"},"seed":{"value":7},"firstDeck":{"owner":{"value":"player"},"cards":[]},"secondDeck":{"owner":{"value":"cpu"},"cards":[]}},"commands":[{"id":{"value":"resign"},"matchId":{"value":"match"},"actor":{"value":"player"},"expectedRevision":{"value":0},"choices":[],"action":{"$command":"resign"}}],"clientCommands":[]}
            """
        );
        await Task.CompletedTask;
    }

    [Test]
    public async Task NullCollectionMember_IsStillRejectedRatherThanNormalized()
    {
        // Absent is empty; an explicit null is a damaged document, and stays one.
        var withNullChoices = DocumentWithAbsentCollectionMembers.Replace(
            "\"expectedRevision\":{\"value\":0},",
            "\"expectedRevision\":{\"value\":0},\"choices\":null,",
            StringComparison.Ordinal
        );

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<MatchDocument>(withNullChoices, MatchJson.Options)
        );
        await Task.CompletedTask;
    }

    [Test]
    public async Task ActionPayloadWithAnAbsentMember_IsStillRejected()
    {
        // The union converters count the members they read, so an absent payload member never
        // reaches a defaulted collection: it is refused here, as it was before the swap.
        const string missingBooth = """
            {"$command":"chooseOpening","oche":{"value":"oche"}}
            """;
        const string missingValues = """
            {"$choice":"cards","choiceId":{"value":"cards"}}
            """;

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<MatchAction>(missingBooth, MatchJson.Options)
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<EffectChoice>(missingValues, MatchJson.Options)
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
            ImmutableArray.CreateRange(commands),
            MatchJson.Options
        );
        var restoredStart = JsonSerializer.Deserialize<MatchStartRequest>(
            startJson,
            MatchJson.Options
        );
        var restoredCommands = JsonSerializer.Deserialize<ImmutableArray<MatchCommand>>(
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
