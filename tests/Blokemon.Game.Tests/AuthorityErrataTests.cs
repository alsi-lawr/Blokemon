using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class AuthorityErrataTests
{
    [Test]
    public async Task Parkrun_DiscardsOnlyBasicSoberEnergyForAdditionalDamage()
    {
        var engine = MatchScenario.Engine();
        var soberVim = MatchScenario.Card(
            "sober-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var soberBlokemon = MatchScenario.Card(
            "sober-blokemon",
            "BLK-061",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var state = AddCards(
            MatchScenario.BattleState("BLK-009", "BLK-003", ["VIM-SOBER", "VIM-SOBER"], 881),
            soberVim,
            soberBlokemon
        );
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-009-B01")
            );
        var requested = (CommandOutcome.Applied)engine.Apply(state, action.Command);
        var cards = requested.State.PendingEffect!.Requirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Cards
        );
        var applied = (CommandOutcome.Applied)
            engine.Apply(
                requested.State,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested.State,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            cards.Id,
                            FrozenList<CardInstanceId>.Create(soberVim.Id)
                        )
                    )
                )
            );

        cards.EligibleCards.ShouldBe([soberVim.Id]);
        applied.State.Card(soberVim.Id).Zone.ShouldBe(CardZone.EmptiesTray);
        applied.State.Card(soberBlokemon.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.State.Card(new CardInstanceId("defender")).Damage.ShouldBe(140);
    }

    [Test]
    [MethodDataSource(nameof(ProtectionCases))]
    public async Task ProtectionAttacks_BlockDamageAndAttackEffects(ProtectionCase testCase)
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            testCase.Protector,
            "BLK-003",
            testCase.Vim,
            SeedForBadge()
        );
        state = AttachToDefender(state, "VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER");

        var protectedRound = ApplyAttack(engine, state, MatchScenario.FirstPlayer, testCase.Attack);
        var retaliation = ApplyAttack(
            engine,
            protectedRound.State,
            MatchScenario.SecondPlayer,
            "BLK-003-B01"
        );
        var protectedCard = retaliation.State.Card(new CardInstanceId("attacker"));

        protectedCard.Damage.ShouldBe(0);
        protectedCard.RoughStates.ShouldBeEmpty();
    }

    [Test]
    public async Task DayTwo_ForcesTheOpponentsCoinFlipToTails()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-054",
            "BLK-001",
            ["VIM-SOBER"],
            SeedForBadge(),
            defenderRoughStates: FrozenList<RoughStateEntry>.Create(
                new RoughStateEntry(BlokemonRoughState.Muddled, 1)
            )
        );
        state = AttachToDefender(state, "VIM-BLAZED", "VIM-SOBER");

        var dayTwo = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-054-B01");
        var opponent = ApplyAttack(engine, dayTwo.State, MatchScenario.SecondPlayer, "BLK-001-B01");

        dayTwo.Events.Any(matchEvent => matchEvent.Kind == MatchEventKind.BeerMatTossed)
            .ShouldBeFalse();
        opponent.Events.Any(matchEvent => matchEvent.Kind == MatchEventKind.AttackCancelled)
            .ShouldBeTrue();
        opponent.State.Card(new CardInstanceId("attacker")).Damage.ShouldBe(0);
    }

    [Test]
    public async Task DayTwo_DoesNotChangeTheCheckupBeforeTheOpponentsTurn()
    {
        var state = MatchScenario.BattleState(
            "BLK-054",
            "BLK-001",
            ["VIM-SOBER"],
            SeedForBadge(),
            defenderRoughStates: FrozenList<RoughStateEntry>.Create(
                new RoughStateEntry(BlokemonRoughState.Singed, 1)
            )
        );

        var applied = ApplyAttack(
            MatchScenario.Engine(),
            state,
            MatchScenario.FirstPlayer,
            "BLK-054-B01"
        );
        var defender = applied.State.Card(new CardInstanceId("defender"));

        defender.RoughStates.Any(entry => entry.State == BlokemonRoughState.Singed).ShouldBeFalse();
        defender.Damage.ShouldBe(20);
    }

    [Test]
    public async Task DayTwo_RemainsInForceWhenDayTwoMovesToTheBooth()
    {
        var engine = MatchScenario.Engine();
        var replacement = MatchScenario.Card(
            "replacement",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var matchmaker = MatchScenario.Card(
            "matchmaker",
            "KIT-009",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        var state = MatchScenario.BattleState(
            "BLK-054",
            "BLK-001",
            ["VIM-SOBER"],
            SeedForBadge(),
            defenderRoughStates: FrozenList<RoughStateEntry>.Create(
                new RoughStateEntry(BlokemonRoughState.Muddled, 1)
            )
        );
        state = AddCards(
            AttachToDefender(state, "VIM-BLAZED", "VIM-SOBER"),
            replacement,
            matchmaker
        );

        var dayTwo = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-054-B01");
        var kitAction = engine
            .GetLegalActions(dayTwo.State, MatchScenario.SecondPlayer)
            .Single(action =>
                action.Command is MatchCommand.PlayKit play && play.Kit == matchmaker.Id
            );
        var switched = (CommandOutcome.Applied)engine.Apply(dayTwo.State, kitAction.Command);
        var reply = ApplyAttack(engine, switched.State, MatchScenario.SecondPlayer, "BLK-001-B01");

        reply.State.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.Booth);
        reply.Events.Any(matchEvent => matchEvent.Kind == MatchEventKind.AttackCancelled)
            .ShouldBeTrue();
        reply.State.Card(replacement.Id).Damage.ShouldBe(0);
    }

    [Test]
    public async Task RonniePickering_LeavesItselfMuddledAfterAttacking()
    {
        var state = MatchScenario.BattleState("BLK-057", "BLK-150", ["VIM-LAIRY"], 907);

        var applied = ApplyAttack(
            MatchScenario.Engine(),
            state,
            MatchScenario.FirstPlayer,
            "BLK-057-B01"
        );

        applied
            .State.Card(new CardInstanceId("attacker"))
            .RoughStates.Select(static entry => entry.State)
            .ShouldContain(BlokemonRoughState.Muddled);
    }

    [Test]
    public async Task FlyTipper_IncreasesRetreatCostWithoutIncreasingAttackCost()
    {
        var engine = MatchScenario.Engine();
        var state = AttachToDefender(
            MatchScenario.BattleState("BLK-088", "BLK-001", ["VIM-DODGY"], 911),
            "VIM-BLAZED",
            "VIM-SOBER"
        );

        var applied = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-088-B01");
        var target = new CardInstanceId("defender");

        applied.State.Effects.Any(effect =>
            effect.TargetCard == target && effect.Kind == TemporaryEffectKind.ModifyTaxiFare
        ).ShouldBeTrue();
        applied.State.Effects.Any(effect =>
            effect.TargetCard == target
            && effect.Kind == TemporaryEffectKind.ModifyAttackCost
        ).ShouldBeFalse();
        engine
            .GetLegalActions(applied.State, MatchScenario.SecondPlayer)
            .Any(action =>
                action.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-001-B01")
            ).ShouldBeTrue();
    }

    [Test]
    public async Task WasteLicencePending_IncreasesBothAttackAndRetreatCosts()
    {
        var engine = MatchScenario.Engine();
        var state = AttachToDefender(
            MatchScenario.BattleState("BLK-089", "BLK-001", ["VIM-DODGY"], 917),
            "VIM-BLAZED",
            "VIM-SOBER"
        );

        var applied = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-089-B01");
        var target = new CardInstanceId("defender");

        applied.State.Effects.Any(effect =>
            effect.TargetCard == target
            && effect.Kind == TemporaryEffectKind.ModifyAttackCost
        ).ShouldBeTrue();
        applied.State.Effects.Any(effect =>
            effect.TargetCard == target && effect.Kind == TemporaryEffectKind.ModifyTaxiFare
        ).ShouldBeTrue();
        engine
            .GetLegalActions(applied.State, MatchScenario.SecondPlayer)
            .Any(action =>
                action.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-001-B01")
            ).ShouldBeFalse();
    }

    [Test]
    public async Task PoolCue_BoostsLastOrdersWhileLastOrdersIsActive()
    {
        var engine = MatchScenario.Engine();
        var poolCue = MatchScenario.Card(
            "pool-cue",
            "BLK-104",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var boothTarget = MatchScenario.Card(
            "booth-target",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var state = AddCards(
            MatchScenario.BattleState("BLK-105", "BLK-150", ["VIM-LAIRY"], 919),
            poolCue,
            boothTarget
        );

        var applied = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-105-B01");

        applied.State.Card(boothTarget.Id).Damage.ShouldBe(60);
    }

    [Test]
    public async Task Hotbox_RequiresTheMatchmakerForItsAdditionalDamage()
    {
        var wrongMate = NamedMateState("BLK-114", "KIT-010", 923);
        var matchmaker = NamedMateState("BLK-114", "KIT-009", 927);
        var engine = MatchScenario.Engine();

        var wrongOutcome = ApplyAttack(engine, wrongMate, MatchScenario.FirstPlayer, "BLK-114-B01");
        var matchmakerOutcome = ApplyAttack(
            engine,
            matchmaker,
            MatchScenario.FirstPlayer,
            "BLK-114-B01"
        );

        wrongOutcome.State.Card(new CardInstanceId("defender")).Damage.ShouldBe(10);
        matchmakerOutcome.State.Card(new CardInstanceId("defender")).Damage.ShouldBe(70);
    }

    [Test]
    public async Task OneYearChip_DefersBothRequiredCoinFlipsUntilTheOpponentsAttack()
    {
        var engine = MatchScenario.Engine();
        var state = AttachToDefender(
            MatchScenario.BattleState(
                "BLK-117",
                "BLK-001",
                ["VIM-SOBER", "VIM-SOBER", "VIM-SOBER"],
                SeedForTwoTosses(allBadges: false)
            ),
            "VIM-BLAZED",
            "VIM-SOBER"
        );

        var firstAttack = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-117-B01");
        var reply = ApplyAttack(
            engine,
            firstAttack.State,
            MatchScenario.SecondPlayer,
            "BLK-001-B01"
        );

        firstAttack.Events.Count(matchEvent =>
            matchEvent.Kind == MatchEventKind.BeerMatTossed
        ).ShouldBe(0);
        reply.Events.Count(matchEvent => matchEvent.Kind == MatchEventKind.BeerMatTossed)
            .ShouldBe(2);
        reply.Events.Any(matchEvent => matchEvent.Kind == MatchEventKind.AttackCancelled)
            .ShouldBeTrue();
    }

    [Test]
    public async Task OldHabit_ReturnsOpposingEnergyWithoutAttachingEnergyFromHand()
    {
        var engine = MatchScenario.Engine();
        var ownVim = MatchScenario.Card(
            "own-mitt-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var state = AttachToDefender(
            MatchScenario.BattleState("BLK-138", "BLK-150", ["VIM-SOBER", "VIM-SOBER"], 929),
            "VIM-BEER"
        );
        state = AddCards(state, ownVim);
        var opposingVim = state.CardsIn(MatchScenario.SecondPlayer, CardZone.Attached).Single();

        var applied = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-138-B01");

        applied.State.Card(opposingVim.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.State.Card(ownVim.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.State.Card(ownVim.Id).AttachedTo.ShouldBeNull();
    }

    [Test]
    public async Task FatLes_CanTargetABenchedBlokemonOfAnyType()
    {
        var engine = MatchScenario.Engine();
        var boothTarget = MatchScenario.Card(
            "booth-target",
            "BLK-036",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var state = AddCards(
            MatchScenario.BattleState(
                "BLK-146",
                "BLK-150",
                ["VIM-CURRY", "VIM-CURRY", "VIM-CURRY"],
                933
            ),
            boothTarget
        );

        var applied = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-146-B01");

        applied.State.Card(boothTarget.Id).Damage.ShouldBe(120);
    }

    [Test]
    public async Task MirrorTheMood_ReflectsAttackDamageEvenWhenItIsKnockedOut()
    {
        var engine = MatchScenario.Engine();
        var replacement = MatchScenario.Card(
            "replacement",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var state = AttachToDefender(
            MatchScenario.BattleState("BLK-150", "BLK-001", ["VIM-SOBER", "VIM-SOBER"], 937),
            "VIM-BLAZED",
            "VIM-SOBER"
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id == new CardInstanceId("attacker")
                            ? card with
                            {
                                Damage = 110,
                            }
                            : card
                    )
                    .Append(replacement)
                    .OrderBy(static card => card.Id)
            ),
        };

        var mirror = ApplyAttack(engine, state, MatchScenario.FirstPlayer, "BLK-150-B01");
        var reply = ApplyAttack(engine, mirror.State, MatchScenario.SecondPlayer, "BLK-001-B01");

        reply.State.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.EmptiesTray);
        reply.Events.Any(matchEvent =>
            matchEvent.Kind == MatchEventKind.DamagePlaced
            && matchEvent.DamageKind == DamageKind.PlacedCounter
            && matchEvent.Amount == 20
            && matchEvent.TargetCards.Contains(new CardInstanceId("defender"))
        ).ShouldBeTrue();
    }

    public static IEnumerable<ProtectionCase> ProtectionCases() =>
        [
            new("BLK-018", "BLK-018-B02", ["VIM-SOBER", "VIM-SOBER", "VIM-SOBER"]),
            new("BLK-119", "BLK-119-B01", ["VIM-SOBER"]),
        ];

    private static CommandOutcome.Applied ApplyAttack(
        MatchEngine engine,
        MatchState state,
        PlayerId actor,
        string effect
    )
    {
        var action = engine
            .GetLegalActions(state, actor)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId(effect)
            );
        return (CommandOutcome.Applied)engine.Apply(state, action.Command);
    }

    private static MatchState AttachToDefender(MatchState state, params string[] vimIds)
    {
        var attachments = vimIds
            .Select(
                (mechanicalId, index) =>
                    MatchScenario.Card(
                        $"other-vim-{index}",
                        mechanicalId,
                        MatchScenario.SecondPlayer,
                        CardZone.Attached,
                        -1,
                        attachedTo: new CardInstanceId("defender")
                    )
            )
            .ToArray();
        return state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id == new CardInstanceId("defender")
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(
                                    card.Attachments.Concat(
                                        attachments.Select(static value => value.Id)
                                    )
                                ),
                            }
                            : card
                    )
                    .Concat(attachments)
                    .OrderBy(static card => card.Id)
            ),
        };
    }

    private static MatchState AddCards(MatchState state, params CardState[] cards) =>
        state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Concat(cards).OrderBy(static card => card.Id)
            ),
        };

    private static MatchState NamedMateState(string attacker, string mate, ulong seed)
    {
        var state = MatchScenario.BattleState(attacker, "BLK-150", ["VIM-BLAZED"], seed);
        return state with
        {
            RoundUsage = state.RoundUsage with
            {
                MatesPlayed = 1,
                KitsPlayed = FrozenList<MechanicalCardId>.Create(new MechanicalCardId(mate)),
            },
        };
    }

    private static ulong SeedForBadge()
    {
        for (ulong seed = 0; seed < 1_000; seed++)
        {
            if (new BlokemonSeededRandom(seed).NextInt(2) == 1)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("No badge-side seed found.");
    }

    private static ulong SeedForTwoTosses(bool allBadges)
    {
        for (ulong seed = 0; seed < 1_000; seed++)
        {
            var random = new BlokemonSeededRandom(seed);
            var result = random.NextInt(2) == 1 && random.NextInt(2) == 1;
            if (result == allBadges)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("No matching two-toss seed found.");
    }

    public sealed record ProtectionCase(string Protector, string Attack, string[] Vim);
}
