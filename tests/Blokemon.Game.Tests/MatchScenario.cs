using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

internal static class MatchScenario
{
    public static readonly PlayerId FirstPlayer = new("first");
    public static readonly PlayerId SecondPlayer = new("second");

    public static BlokemonRuntimeManifest Authority { get; } =
        BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
            )
        );

    public static MatchEngine Engine() => new(Authority);

    public static FrozenDeckSnapshot RegularDeck(PlayerId owner)
    {
        var cards = Authority
            .Collectibles.Where(static card => card.Rank == BlokemonRank.Regular)
            .Take(15)
            .SelectMany(static card => Enumerable.Repeat(card.Id, 4));
        return FrozenDeckSnapshot.Create(owner, cards);
    }

    public static MatchStartRequest StartRequest(ulong seed = 0xB10CEUL) =>
        new(
            new MatchId("match"),
            new MatchSeed(seed),
            RegularDeck(FirstPlayer),
            RegularDeck(SecondPlayer)
        );

    public static MatchState BattleState(
        string attacker,
        string defender,
        IEnumerable<string> attachedVim,
        ulong randomSeed,
        FrozenList<RoughStateEntry> attackerRoughStates = default,
        FrozenList<RoughStateEntry> defenderRoughStates = default,
        FrozenList<TemporaryEffect> effects = default
    )
    {
        var attackerCard = Card(
            "attacker",
            attacker,
            FirstPlayer,
            CardZone.Oche,
            -1,
            FrozenList<CardInstanceId>.Create(
                attachedVim.Select((_, index) => new CardInstanceId($"vim-{index}"))
            ),
            attackerRoughStates
        );
        var defenderCard = Card(
            "defender",
            defender,
            SecondPlayer,
            CardZone.Oche,
            -1,
            [],
            defenderRoughStates
        );
        var vim = attachedVim.Select(
            (mechanicalId, index) =>
                Card(
                    $"vim-{index}",
                    mechanicalId,
                    FirstPlayer,
                    CardZone.Attached,
                    -1,
                    [],
                    [],
                    new CardInstanceId("attacker")
                )
        );
        var cards = new[]
        {
            attackerCard,
            defenderCard,
            Card("first-draw", "VIM-BLAZED", FirstPlayer, CardZone.Stack, 0),
            Card("second-draw", "VIM-SOBER", SecondPlayer, CardZone.Stack, 0),
        }.Concat(vim);
        return new MatchState(
            new MatchId("battle"),
            Authority.ManifestVersion,
            new MatchSeed(randomSeed),
            new MatchRandomState(randomSeed, 0),
            new MatchRevision(7),
            0,
            MatchPhase.Playing,
            SecondPlayer,
            FirstPlayer,
            4,
            FrozenList<PlayerState>.Create(
                new PlayerState(FirstPlayer, 6, 0, 0, true, true, 2),
                new PlayerState(SecondPlayer, 6, 0, 0, true, true, 2)
            ),
            FrozenList<CardState>.Create(cards.OrderBy(static card => card.Id)),
            effects,
            [],
            RoundUsage.Empty(FirstPlayer),
            null,
            null,
            [],
            null,
            false,
            null,
            0
        );
    }

    public static MatchCommand.Attack AttackCommand(
        MatchState state,
        string effect,
        FrozenList<EffectChoice> choices = default
    ) =>
        new(
            new CommandId($"command:{effect}"),
            state.Id,
            FirstPlayer,
            state.Revision,
            new CardInstanceId("attacker"),
            new EffectId(effect),
            choices
        );

    public static CardState Card(
        string id,
        string mechanicalId,
        PlayerId owner,
        CardZone zone,
        int stackPosition,
        FrozenList<CardInstanceId> attachments = default,
        FrozenList<RoughStateEntry> roughStates = default,
        CardInstanceId? attachedTo = null
    )
    {
        var kind =
            Authority.Collectibles.Any(card => card.Id == mechanicalId) ? CardKind.Bloke
            : Authority.Kits.Any(card => card.Id == mechanicalId) ? CardKind.Kit
            : CardKind.Vim;
        return new CardState(
            new CardInstanceId(id),
            new MechanicalCardId(mechanicalId),
            owner,
            kind,
            zone,
            zone == CardZone.BarChit,
            stackPosition,
            attachedTo,
            attachments,
            [],
            0,
            roughStates,
            1,
            -1
        );
    }

    public static MatchState Applied(CommandOutcome outcome) =>
        ((CommandOutcome.Applied)outcome).State;

    public static MatchState Started(MatchStartOutcome outcome) =>
        ((MatchStartOutcome.Started)outcome).State;
}
