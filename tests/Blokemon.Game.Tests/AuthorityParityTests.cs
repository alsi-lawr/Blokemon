using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class AuthorityParityTests
{
    [Test]
    public async Task SecondOpener_CanUseItsFirstRoundPromotionAbility()
    {
        var state = MatchScenario.BattleState("BLK-021", "BLK-150", [], 701);
        var promotion = MatchScenario.Card(
            "promotion",
            "BLK-022",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        state = state with
        {
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            RoundsStarted = 1,
                        }
                        : player
                )
            ),
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(promotion).OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Promote
                && candidate.Command is MatchCommand.Promote command
                && command.Promotion == promotion.Id
                && command.Bloke == new CardInstanceId("attacker")
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        applied.Card(promotion.Id).Zone.ShouldBe(CardZone.Oche);
    }

    [Test]
    public async Task RecoilKnockout_DoesNotTriggerDonni()
    {
        var state = MatchScenario.BattleState("BLK-081", "BLK-076", ["VIM-BEER", "VIM-SOBER"], 703);
        var donni = MatchScenario.Card(
            "donni",
            "BLK-026",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCards(state, donni);

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-081-B02"))
        );

        applied.PendingKnockout.ShouldBeNull();
        applied.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.EmptiesTray);
        applied.Card(donni.Id).Zone.ShouldBe(CardZone.Booth);
    }

    [Test]
    public async Task ItemLock_DoesNotBlockAnOtherwiseLegalSupporter()
    {
        var state = MatchScenario.BattleState("BLK-049", "BLK-150", ["VIM-BLAZED"], 709);
        var item = MatchScenario.Card(
            "item",
            "KIT-001",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        var supporter = MatchScenario.Card(
            "supporter",
            "KIT-005",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        state = AddCards(state, item, supporter);
        var attacked = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-049-B01"))
        );

        var actions = MatchScenario.Engine().GetLegalActions(attacked, MatchScenario.SecondPlayer);

        actions
            .Any(action =>
                action.Kind == LegalActionKind.PlayKit
                && action.Command is MatchCommand.PlayKit command
                && command.Kit == item.Id
            )
            .ShouldBeFalse();
        actions
            .Any(action =>
                action.Kind == LegalActionKind.PlayKit
                && action.Command is MatchCommand.PlayKit command
                && command.Kit == supporter.Id
            )
            .ShouldBeTrue();
    }

    [Test]
    public async Task ChosenWeakness_PersistsUntilTheDefenderLeavesTheOche()
    {
        var state = MatchScenario.BattleState("BLK-137", "BLK-076", ["VIM-SOBER"], 719);
        state = AddCards(
            state,
            MatchScenario.Card(
                "first-stack-2",
                "VIM-SOBER",
                MatchScenario.FirstPlayer,
                CardZone.Stack,
                1
            ),
            MatchScenario.Card(
                "second-stack-2",
                "VIM-SOBER",
                MatchScenario.SecondPlayer,
                CardZone.Stack,
                1
            )
        );
        var engine = MatchScenario.Engine();
        var attack = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack command
                && command.AttackId == new EffectId("BLK-137-B01")
            );
        var attacked = MatchScenario.Applied(engine.Apply(state, attack.Command));
        var opponentEnded = MatchScenario.Applied(
            engine.Apply(
                attacked,
                new MatchCommand.EndRound(
                    new CommandId("end-opponent-round"),
                    attacked.Id,
                    MatchScenario.SecondPlayer,
                    attacked.Revision
                )
            )
        );
        var ownerEnded = MatchScenario.Applied(
            engine.Apply(
                opponentEnded,
                new MatchCommand.EndRound(
                    new CommandId("end-owner-round"),
                    opponentEnded.Id,
                    MatchScenario.FirstPlayer,
                    opponentEnded.Revision
                )
            )
        );

        ownerEnded
            .Effects.Any(effect =>
                effect.SourceEffect == new EffectId("BLK-137-B01")
                && effect.TargetCard == new CardInstanceId("defender")
            )
            .ShouldBeTrue();
    }

    [Test]
    public async Task Demotion_LeavesOnlyTheLowerStageWithBattleState()
    {
        var state = MatchScenario.BattleState(
            "BLK-142",
            "BLK-002",
            ["VIM-SOBER", "VIM-SOBER"],
            727
        );
        var lowerStage = MatchScenario.Card(
            "lower-stage",
            "BLK-150",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var opposingVim = MatchScenario.Card(
            "opposing-vim",
            "VIM-BEER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(opposingVim.Id),
                                UnderlyingCards = FrozenList<CardInstanceId>.Create(lowerStage.Id),
                            }
                            : card
                    )
                    .Append(lowerStage)
                    .Append(opposingVim)
                    .OrderBy(static card => card.Id)
            ),
            Effects = FrozenList<TemporaryEffect>.Create(
                new TemporaryEffect(
                    new EffectId("BLK-137-B01"),
                    new CardInstanceId("attacker"),
                    MatchScenario.FirstPlayer,
                    new CardInstanceId("defender"),
                    TemporaryEffectKind.ModifySoftSpot,
                    2,
                    FrozenList<BlokemonMechanicalType>.Create(BlokemonMechanicalType.Grass),
                    [],
                    [],
                    [],
                    EffectDuration.WhileTargetInPlay,
                    state.RoundNumber,
                    state.RoundNumber
                )
            ),
        };

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-142-B02"))
        );
        var returned = applied.Card(new CardInstanceId("defender"));
        var active = applied.Card(lowerStage.Id);

        returned.Zone.ShouldBe(CardZone.Mitt);
        returned.Damage.ShouldBe(0);
        returned.Attachments.ShouldBeEmpty();
        returned.UnderlyingCards.ShouldBeEmpty();
        active.Zone.ShouldBe(CardZone.Oche);
        active.Damage.ShouldBe(100);
        active.Attachments.ShouldBe([opposingVim.Id]);
        applied.Card(opposingVim.Id).AttachedTo.ShouldBe(active.Id);
        applied
            .Effects.Any(effect =>
                effect.TargetCard == returned.Id
                && effect.Duration == EffectDuration.WhileTargetInPlay
            )
            .ShouldBeFalse();
    }

    [Test]
    public async Task PlacedCounters_DoNotQueueDonniAsAttackDamage()
    {
        var state = CounterKnockoutState("BLK-110", includeDonni: true);
        var engine = MatchScenario.Engine();
        var attack = CounterAttack(engine, state);

        var applied = MatchScenario.Applied(engine.Apply(state, attack));

        applied.PendingKnockout.ShouldBeNull();
        applied.Card(new CardInstanceId("donni")).Zone.ShouldBe(CardZone.Booth);
    }

    [Test]
    public async Task PlacedCounters_DoNotTriggerAttackDamageRetaliation()
    {
        var state = CounterKnockoutState("BLK-110", includeDonni: false);
        var engine = MatchScenario.Engine();
        var attack = CounterAttack(engine, state);

        var applied = MatchScenario.Applied(engine.Apply(state, attack));

        applied.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.Oche);
    }

    [Test]
    public async Task RepeatedTools_KeepAnEffectForEachAttachedCopy()
    {
        var state = MatchScenario.BattleState("BLK-001", "BLK-002", [], 733);
        var secondTarget = MatchScenario.Card(
            "second-target",
            "BLK-036",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var firstTool = MatchScenario.Card(
            "first-tool",
            "KIT-014",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var secondTool = MatchScenario.Card(
            "second-tool",
            "KIT-014",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: secondTarget.Id
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(firstTool.Id),
                            }
                            : card
                    )
                    .Append(
                        secondTarget with
                        {
                            Attachments = FrozenList<CardInstanceId>.Create(secondTool.Id),
                        }
                    )
                    .Append(firstTool)
                    .Append(secondTool)
                    .OrderBy(static card => card.Id)
            ),
        };

        var applied = MatchScenario.Applied(
            MatchScenario
                .Engine()
                .Apply(
                    state,
                    new MatchCommand.EndRound(
                        new CommandId("refresh-repeated-tools"),
                        state.Id,
                        MatchScenario.FirstPlayer,
                        state.Revision
                    )
                )
        );
        var effects = applied.Effects.Where(effect =>
            effect.SourceEffect == new EffectId("KIT-014-R01")
        );

        effects
            .Select(static effect => effect.SourceCard)
            .ShouldBe([firstTool.Id, secondTool.Id], ignoreOrder: true);
    }

    [Test]
    public async Task TalentScout_ShufflesOnlyTheCardsThatRemainInTheStack()
    {
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], 739);
        var talentScout = MatchScenario.Card(
            "talent-scout",
            "KIT-005",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var selected = MatchScenario.Card(
            "selected",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            0
        );
        var remaining = Enumerable
            .Range(1, 4)
            .Select(index =>
                MatchScenario.Card(
                    $"remaining-{index}",
                    "VIM-SOBER",
                    MatchScenario.FirstPlayer,
                    CardZone.Stack,
                    index
                )
            )
            .ToArray();
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Where(card => card.Id.Value != "first-draw")
                    .Append(talentScout)
                    .Append(selected)
                    .Concat(remaining)
                    .OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var play = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action =>
                action.Kind == LegalActionKind.PlayKit
                && action.Command is MatchCommand.PlayKit command
                && command.Kit == talentScout.Id
            );
        var requested = MatchScenario.Applied(engine.Apply(state, play.Command));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var resolved = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(selected.Id)
                        )
                    )
                )
            )
        );

        resolved.Card(selected.Id).Zone.ShouldBe(CardZone.Mitt);
        (resolved.Random.ConsumptionIndex - requested.Random.ConsumptionIndex).ShouldBe(3);
    }

    [Test]
    public async Task CopiedAttack_RequestsItsOwnCardChoice()
    {
        var state = MatchScenario.BattleState(
            "BLK-151",
            "BLK-106",
            ["VIM-SOBER", "VIM-SOBER", "VIM-SOBER"],
            743
        );
        var firstBench = MatchScenario.Card(
            "first-bench",
            "BLK-001",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var secondBench = MatchScenario.Card(
            "second-bench",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var otherBench = MatchScenario.Card(
            "other-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCards(state, firstBench, secondBench, otherBench);
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-151-B01")
            );

        var requested = MatchScenario.Applied(engine.Apply(state, action.Command));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var resolved = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(secondBench.Id)
                        )
                    )
                )
            )
        );

        requirement.EligibleCards.ShouldBe([firstBench.Id, secondBench.Id], ignoreOrder: true);
        resolved.Card(secondBench.Id).Zone.ShouldBe(CardZone.Oche);
        resolved.Card(new CardInstanceId("attacker")).Zone.ShouldBe(CardZone.Booth);
    }

    [Test]
    public async Task ForcedSwitch_AppliesDamageBeforeRemovingTheOutgoingDefendersEffects()
    {
        var state = MatchScenario.BattleState("BLK-012", "BLK-009", ["VIM-BLAZED"], 751);
        var otherBench = MatchScenario.Card(
            "other-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCards(state, otherBench);
        var engine = MatchScenario.Engine();
        var attack = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack command
                && command.AttackId == new EffectId("BLK-012-B01")
            );
        var requested = MatchScenario.Applied(engine.Apply(state, attack.Command));
        var requirement = requested.PendingEffect!.Requirements.Single();
        var applied = MatchScenario.Applied(
            engine.Apply(
                requested,
                MatchScenario.ResolveEffectChoiceCommand(
                    requested,
                    FrozenList<EffectChoice>.Create(
                        new EffectChoice.Cards(
                            requirement.Id,
                            FrozenList<CardInstanceId>.Create(otherBench.Id)
                        )
                    ),
                    MatchScenario.SecondPlayer
                )
            )
        );

        applied.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Booth);
        applied.Card(new CardInstanceId("defender")).Damage.ShouldBe(30);
    }

    [Test]
    public async Task TypedFareAbility_RequiresMatchingVimAndDoesNotAttachFromTheMitt()
    {
        var matching = FareAbilityState(["VIM-SOBER"]);
        var nonMatching = FareAbilityState(["VIM-BEER", "VIM-BEER"]);
        var matchingActions = MatchScenario
            .Engine()
            .GetLegalActions(matching, MatchScenario.FirstPlayer);
        var nonMatchingActions = MatchScenario
            .Engine()
            .GetLegalActions(nonMatching, MatchScenario.FirstPlayer);
        var freeTaxi = (MatchCommand.Taxi)
            matchingActions
                .Single(action =>
                    action.Kind == LegalActionKind.Taxi
                    && action.Command is MatchCommand.Taxi command
                    && command.BoothBloke == new CardInstanceId("own-booth")
                )
                .Command;
        var paidTaxi = (MatchCommand.Taxi)
            nonMatchingActions
                .Single(action =>
                    action.Kind == LegalActionKind.Taxi
                    && action.Command is MatchCommand.Taxi command
                    && command.BoothBloke == new CardInstanceId("own-booth")
                )
                .Command;

        freeTaxi.VimToChuck.ShouldBeEmpty();
        paidTaxi.VimToChuck.Count.ShouldBe(2);
        matching
            .CardsIn(MatchScenario.FirstPlayer, CardZone.Mitt)
            .Single(card => card.Id == new CardInstanceId("mitt-vim"))
            .Zone.ShouldBe(CardZone.Mitt);
    }

    [Test]
    public async Task DelayedCounters_FollowTheOriginalDefenderToTheBooth()
    {
        var state = MatchScenario.BattleState(
            "BLK-071",
            "BLK-024",
            ["VIM-BLAZED", "VIM-SOBER"],
            761
        );
        var replacement = MatchScenario.Card(
            "other-booth",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var firstVim = MatchScenario.Card(
            "other-vim-1",
            "VIM-SOBER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var secondVim = MatchScenario.Card(
            "other-vim-2",
            "VIM-SOBER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(
                                    firstVim.Id,
                                    secondVim.Id
                                ),
                            }
                            : card
                    )
                    .Append(replacement)
                    .Append(firstVim)
                    .Append(secondVim)
                    .OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var attacked = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-071-B02"))
        );
        var taxied = MatchScenario.Applied(
            engine.Apply(
                attacked,
                new MatchCommand.Taxi(
                    new CommandId("taxi-delayed-target"),
                    attacked.Id,
                    MatchScenario.SecondPlayer,
                    attacked.Revision,
                    replacement.Id,
                    FrozenList<CardInstanceId>.Create(firstVim.Id, secondVim.Id)
                )
            )
        );
        var ended = (CommandOutcome.Applied)
            engine.Apply(
                taxied,
                new MatchCommand.EndRound(
                    new CommandId("end-delayed-round"),
                    taxied.Id,
                    MatchScenario.SecondPlayer,
                    taxied.Revision
                )
            );

        ended
            .Events.Any(matchEvent =>
                matchEvent.Kind == MatchEventKind.DamagePlaced
                && matchEvent.TargetCards.Contains(new CardInstanceId("defender"))
                && matchEvent.Amount == 120
            )
            .ShouldBeTrue();
    }

    [Test]
    public async Task AttachedTool_ReducesAttackDamageToABoothTarget()
    {
        var state = MatchScenario.BattleState("BLK-042", "BLK-150", ["VIM-SOBER"], 769);
        var boothTarget = MatchScenario.Card(
            "booth-target",
            "BLK-036",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var tool = MatchScenario.Card(
            "booth-tool",
            "KIT-014",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: boothTarget.Id
        );
        state = AddCards(
            state,
            boothTarget with
            {
                Attachments = FrozenList<CardInstanceId>.Create(tool.Id),
            },
            tool
        );
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack command
                && command.AttackId == new EffectId("BLK-042-B01")
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        applied.Card(boothTarget.Id).Damage.ShouldBe(10);
    }

    [Test]
    public async Task DiscardRecoveryLock_BlocksAnOpposingItemFromReturningATrainerToStack()
    {
        var authority = AuthorityWithItemDiscardRecovery();
        var engine = new MatchEngine(authority);
        var state = MatchScenario.BattleState("BLK-001", "BLK-027", [], 773);
        var item = MatchScenario.Card(
            "recovery-item",
            "KIT-001",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var discardedTrainer = MatchScenario.Card(
            "discarded-trainer",
            "KIT-012",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        state = AddCards(state, item, discardedTrainer);
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.PlayKit
                && candidate.Command is MatchCommand.PlayKit command
                && command.Kit == item.Id
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        applied.Card(discardedTrainer.Id).Zone.ShouldBe(CardZone.EmptiesTray);
    }

    private static BlokemonRuntimeManifest AuthorityWithItemDiscardRecovery()
    {
        var item = MatchScenario.Authority.Kits.Single(card => card.Id == "KIT-001");
        var recovery = new BlokemonEffectInstruction(
            BlokemonOpcode.MoveCards,
            1,
            BlokemonValueSource.Fixed,
            [BlokemonTarget.OwnEmptiesTray],
            BlokemonSelection.Chosen,
            1,
            [],
            [],
            [],
            [],
            [],
            [],
            sources: [BlokemonTarget.OwnEmptiesTray],
            destination: BlokemonEffectDestination.OwnStack,
            cardFilter: new BlokemonEffectCardFilter(
                [BlokemonCardCategory.Kit],
                [],
                [],
                false,
                false,
                []
            ),
            sourceTopCount: 0
        );
        var changed = item.WithHouseRules([
            item.HouseRules[0].WithProgram([recovery]),
            item.HouseRules[1],
        ]);
        return MatchScenario.Authority.WithKits([
            .. MatchScenario.Authority.Kits.Select(card => card.Id == changed.Id ? changed : card),
        ]);
    }

    private static MatchState FareAbilityState(string[] attachedVim)
    {
        var state = MatchScenario.BattleState("BLK-144", "BLK-150", attachedVim, 757);
        return AddCards(
            state,
            MatchScenario.Card(
                "own-booth",
                "BLK-004",
                MatchScenario.FirstPlayer,
                CardZone.Booth,
                -1
            ),
            MatchScenario.Card(
                "mitt-vim",
                "VIM-SOBER",
                MatchScenario.FirstPlayer,
                CardZone.Mitt,
                -1
            )
        );
    }

    private static MatchState CounterKnockoutState(string defenderId, bool includeDonni)
    {
        var state = MatchScenario.BattleState("BLK-122", defenderId, ["VIM-SOBER"], SeedForBadge());
        var knockedOutBeer = MatchScenario.Card(
            "knocked-out-beer",
            "VIM-BEER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var cards = state.Cards.Select(card =>
            card.Id.Value == "defender"
                ? card with
                {
                    Damage = 80,
                    Attachments = FrozenList<CardInstanceId>.Create(knockedOutBeer.Id),
                }
                : card
        );
        cards = cards.Append(knockedOutBeer);
        if (includeDonni)
        {
            cards = cards.Append(
                MatchScenario.Card(
                    "donni",
                    "BLK-026",
                    MatchScenario.SecondPlayer,
                    CardZone.Booth,
                    -1
                )
            );
        }

        return state with
        {
            Cards = FrozenList<CardState>.Create(cards.OrderBy(static card => card.Id)),
        };
    }

    private static MatchCommand CounterAttack(MatchEngine engine, MatchState state) =>
        engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action =>
                action.Kind == LegalActionKind.Attack
                && action.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-122-B01")
            )
            .Command;

    private static ulong SeedForBadge()
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            var random = new BlokemonSeededRandom(seed);
            if (random.NextInt(2) == 1)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("No badge-side seed was found.");
    }

    private static MatchState AddCards(MatchState state, params CardState[] cards) =>
        state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Concat(cards).OrderBy(static card => card.Id)
            ),
        };
}
