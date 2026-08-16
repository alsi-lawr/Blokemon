using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class CardSemanticsTests
{
    [Test]
    public async Task NamedMateCondition_MatchesTheRequiredMateOnly()
    {
        var engine = MatchScenario.Engine();
        var wrongMate = NamedMateState("KIT-009");
        var requiredMate = NamedMateState("KIT-010");

        var wrongOutcome = Applied(
            engine.Apply(wrongMate, MatchScenario.AttackCommand(wrongMate, "BLK-112-B02"))
        );
        var requiredOutcome = Applied(
            engine.Apply(requiredMate, MatchScenario.AttackCommand(requiredMate, "BLK-112-B02"))
        );

        AttackDamage(wrongOutcome).ShouldBe(10);
        AttackDamage(requiredOutcome).ShouldBe(150);
    }

    [Test]
    public async Task TypedAttachedVimValue_CountsOnlyTheRequestedType()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-119",
            "BLK-150",
            ["VIM-SOBER", "VIM-BEER", "VIM-BEER"],
            811
        );

        var outcome = Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-119-B02"))
        );

        AttackDamage(outcome).ShouldBe(90);
    }

    [Test]
    public async Task ConditionalMoveBranch_DoesNotAttachVimWhenNothingWasReturned()
    {
        var engine = MatchScenario.Engine();
        var ownVim = MatchScenario.Card(
            "own-mitt-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var kit = MatchScenario.Card(
            "guvnor",
            "KIT-010",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var state = AddCards(MatchScenario.BattleState("BLK-001", "BLK-150", [], 821), ownVim, kit);
        var play = KitAction(engine, state, kit.Id);
        var applied = MatchScenario.Applied(engine.Apply(state, play));

        applied.Card(ownVim.Id).Zone.ShouldBe(CardZone.Mitt);
        applied.Card(ownVim.Id).AttachedTo.ShouldBeNull();
    }

    [Test]
    public async Task ConditionalMoveBranch_RequestsAndAttachesVimAfterAReturn()
    {
        var engine = MatchScenario.Engine();
        var ownVim = MatchScenario.Card(
            "own-mitt-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var opposingVim = MatchScenario.Card(
            "opposing-vim",
            "VIM-BEER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var kit = MatchScenario.Card(
            "guvnor",
            "KIT-010",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], 823);
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(opposingVim.Id),
                            }
                            : card
                    )
                    .Append(ownVim)
                    .Append(opposingVim)
                    .Append(kit)
                    .OrderBy(static card => card.Id)
            ),
        };
        var play = KitAction(engine, state, kit.Id);
        var requested = MatchScenario.Applied(engine.Apply(state, play));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var resolved = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(ownVim.Id)
                        )
                    )
                )
            )
        );

        resolved.Card(opposingVim.Id).Zone.ShouldBe(CardZone.Mitt);
        resolved.Card(ownVim.Id).AttachedTo.ShouldBe(new CardInstanceId("attacker"));
    }

    [Test]
    public async Task StackMove_TakesEachBlokeAndItsAttachedCards()
    {
        var engine = MatchScenario.Engine();
        var ownVim = MatchScenario.Card(
            "own-vim",
            "VIM-BLAZED",
            MatchScenario.FirstPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("attacker")
        );
        var opposingBench = MatchScenario.Card(
            "opposing-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var opposingVim = MatchScenario.Card(
            "opposing-vim",
            "VIM-SOBER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: opposingBench.Id
        );
        var state = MatchScenario.BattleState(
            "BLK-012",
            "BLK-150",
            ["VIM-SOBER", "VIM-SOBER"],
            827
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "attacker"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(
                                    card.Attachments.Append(ownVim.Id)
                                ),
                            }
                            : card
                    )
                    .Append(
                        opposingBench with
                        {
                            Attachments = FrozenList<CardInstanceId>.Create(opposingVim.Id),
                        }
                    )
                    .Append(ownVim)
                    .Append(opposingVim)
                    .OrderBy(static card => card.Id)
            ),
        };
        var attack = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action =>
                action.Kind == LegalActionKind.Attack
                && action.Command is MatchCommand.Attack command
                && command.AttackId == new EffectId("BLK-012-B02")
            )
            .Command;
        var requested = MatchScenario.Applied(engine.Apply(state, attack));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var resolved = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(opposingBench.Id)
                        )
                    )
                )
            )
        );

        foreach (var card in new[] { new CardInstanceId("attacker"), ownVim.Id })
        {
            resolved.Card(card).Zone.ShouldBe(CardZone.Stack);
        }
        foreach (var card in new[] { opposingBench.Id, opposingVim.Id })
        {
            resolved.Card(card).Zone.ShouldBe(CardZone.Stack);
        }
        resolved.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Oche);
    }

    [Test]
    public async Task AttackGate_CancelsOnEitherBlankAndAllowsTwoBadges()
    {
        var blocked = AttackGateState(SeedForTwoTosses(allBadges: false));
        var allowed = AttackGateState(SeedForTwoTosses(allBadges: true));
        var engine = MatchScenario.Engine();

        var blockedOutcome = Applied(
            engine.Apply(blocked, MatchScenario.AttackCommand(blocked, "BLK-001-B01"))
        );
        var allowedOutcome = Applied(
            engine.Apply(allowed, MatchScenario.AttackCommand(allowed, "BLK-001-B01"))
        );

        blockedOutcome
            .Events.Count(matchEvent => matchEvent.Kind == MatchEventKind.BeerMatTossed)
            .ShouldBe(2);
        blockedOutcome
            .Events.Any(matchEvent => matchEvent.Kind == MatchEventKind.AttackCancelled)
            .ShouldBeTrue();
        blockedOutcome.State.Card(new CardInstanceId("defender")).Damage.ShouldBe(0);
        allowedOutcome.State.Card(new CardInstanceId("defender")).Damage.ShouldBe(20);
    }

    [Test]
    public async Task FossilKitPlay_IsUngatedAndPutsTheKitIntoTheBooth()
    {
        var engine = MatchScenario.Engine();
        var fossil = MatchScenario.Card(
            "reenactor",
            "KIT-001",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var state = AddCards(MatchScenario.BattleState("BLK-001", "BLK-150", [], 829), fossil);
        var play = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action =>
                action.Kind == LegalActionKind.PlayKit
                && action.Command is MatchCommand.PlayKit command
                && command.Kit == fossil.Id
            );
        var applied = MatchScenario.Applied(engine.Apply(state, play.Command));

        play.ChoiceRequirements.Any(requirement =>
                requirement.Kind == ChoiceRequirementKind.Optional
            )
            .ShouldBeFalse();
        applied.Card(fossil.Id).Zone.ShouldBe(CardZone.Booth);
        engine
            .GetLegalActions(applied, MatchScenario.FirstPlayer)
            .Any(action =>
                action.Kind == LegalActionKind.ChuckFossil
                && action.Command is MatchCommand.ChuckFossil command
                && command.Fossil == fossil.Id
            )
            .ShouldBeTrue();
    }

    [Test]
    public async Task FossilKits_ShareAnUngatedPlayProgram()
    {
        foreach (var fossilId in new[] { "KIT-001", "KIT-002", "KIT-003" })
        {
            var rule = MatchScenario
                .Authority.Kits.Single(kit => kit.Id == fossilId)
                .HouseRules.Single(houseRule => houseRule.MechanicalId == $"{fossilId}-R01");

            rule.Program.Select(instruction => instruction.Opcode)
                .SequenceEqual([
                    BlokemonOpcode.ModifyTaxiFare,
                    BlokemonOpcode.RestrictTaxi,
                    BlokemonOpcode.PlayAsBloke,
                    BlokemonOpcode.ChuckSelf,
                ])
                .ShouldBeTrue();
            rule.Program.All(instruction =>
                    instruction.Predicates.Length == 0
                    && instruction.Then.Length == 0
                    && instruction.Otherwise.Length == 0
                )
                .ShouldBeTrue();
        }
    }

    [Test]
    public async Task RevealCards_EmitsAGenericRevealAndLeavesThePrizeCardsInPlace()
    {
        var engine = MatchScenario.Engine();
        var auntie = MatchScenario.Card(
            "auntie",
            "KIT-007",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var prizes = Enumerable
            .Range(0, 6)
            .Select(index =>
                MatchScenario.Card(
                    $"prize-{index}",
                    "VIM-SOBER",
                    MatchScenario.FirstPlayer,
                    CardZone.BarChit,
                    index
                )
            )
            .ToArray();
        var stacked = Enumerable
            .Range(0, 2)
            .Select(index =>
                MatchScenario.Card(
                    $"stacked-{index}",
                    "VIM-LAIRY",
                    MatchScenario.FirstPlayer,
                    CardZone.Stack,
                    index
                )
            )
            .ToArray();
        var state = AddCards(
            MatchScenario.BattleState("BLK-001", "BLK-150", [], 833),
            [auntie, .. prizes, .. stacked]
        );
        var play = KitAction(engine, state, auntie.Id);
        var outcome = Applied(engine.Apply(state, play));
        var reveal = outcome.Events.Single(matchEvent =>
            matchEvent.Kind == MatchEventKind.CardsRevealed
        );

        reveal
            .TargetCards.Select(static card => card.Value)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                prizes.Select(static prize => prize.Id.Value).Order(StringComparer.Ordinal)
            )
            .ShouldBeTrue();
        for (var index = 0; index < prizes.Length; index++)
        {
            var after = outcome.State.Card(prizes[index].Id);
            after.Zone.ShouldBe(CardZone.BarChit);
            after.StackPosition.ShouldBe(index);
        }
    }

    private static MatchState NamedMateState(string mateId)
    {
        var state = MatchScenario.BattleState(
            "BLK-112",
            "BLK-150",
            ["VIM-LAIRY", "VIM-LAIRY", "VIM-SOBER"],
            mateId == "KIT-010" ? 803UL : 801UL
        );
        return state with
        {
            RoundUsage = state.RoundUsage with
            {
                MatesPlayed = 1,
                KitsPlayed = FrozenList<MechanicalCardId>.Create(new MechanicalCardId(mateId)),
            },
        };
    }

    private static MatchState AttackGateState(ulong seed)
    {
        var state = MatchScenario.BattleState(
            "BLK-001",
            "BLK-150",
            ["VIM-BLAZED", "VIM-SOBER"],
            seed
        );
        return state with
        {
            Effects = FrozenList<TemporaryEffect>.Create(
                new TemporaryEffect(
                    new EffectId("BLK-117-B01"),
                    new CardInstanceId("defender"),
                    MatchScenario.SecondPlayer,
                    new CardInstanceId("attacker"),
                    TemporaryEffectKind.RestrictAttackOnBeerMat,
                    2,
                    [],
                    [],
                    [],
                    [],
                    EffectDuration.UntilEndOfOpponentsNextRound,
                    state.RoundNumber,
                    state.RoundNumber + 1
                )
            ),
        };
    }

    private static MatchCommand KitAction(
        MatchEngine engine,
        MatchState state,
        CardInstanceId kit
    ) =>
        engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action =>
                action.Kind == LegalActionKind.PlayKit
                && action.Command is MatchCommand.PlayKit command
                && command.Kit == kit
            )
            .Command;

    private static CommandOutcome.Applied Applied(CommandOutcome outcome) =>
        (CommandOutcome.Applied)outcome;

    private static int AttackDamage(CommandOutcome.Applied outcome) =>
        outcome
            .Events.Single(matchEvent =>
                matchEvent.Kind == MatchEventKind.DamagePlaced
                && matchEvent.DamageKind == DamageKind.Attack
                && matchEvent.TargetCards.Contains(new CardInstanceId("defender"))
            )
            .Amount;

    private static MatchState AddCards(MatchState state, params CardState[] cards) =>
        state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Concat(cards).OrderBy(static card => card.Id)
            ),
        };

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

        throw new InvalidOperationException("No two-toss seed was found.");
    }
}
