using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class FullSetBehaviorTests
{
    [Test]
    public async Task AttachmentChoice_MaterializesOneVimWhenSeveralAreEligible()
    {
        var state = MatchScenario.BattleState("BLK-123", "BLK-150", ["VIM-BLAZED"], 607);
        var bench = MatchScenario.Card(
            "own-booth",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        var firstVim = MatchScenario.Card(
            "first-discarded-vim",
            "VIM-BLAZED",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        var secondVim = MatchScenario.Card(
            "second-discarded-vim",
            "VIM-BLAZED",
            MatchScenario.FirstPlayer,
            CardZone.EmptiesTray,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Append(bench)
                    .Append(firstVim)
                    .Append(secondVim)
                    .OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId("BLK-123-B01")
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));
        var eligible = new[] { firstVim.Id, secondVim.Id };

        eligible.Count(id => applied.Card(id).AttachedTo == bench.Id).ShouldBe(1);
        eligible.Count(id => applied.Card(id).Zone == CardZone.EmptiesTray).ShouldBe(1);
    }

    [Test]
    public async Task KnockoutBonus_UsesDamageAfterWeakness()
    {
        var state = MatchScenario.BattleState(
            "BLK-036",
            "BLK-067",
            ["VIM-GEEKED", "VIM-GEEKED", "VIM-GEEKED"],
            611
        );
        var replacement = MatchScenario.Card(
            "other-booth",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var barChits = Enumerable
            .Range(0, 6)
            .Select(index =>
                MatchScenario.Card(
                    $"bar-chit-{index}",
                    "VIM-SOBER",
                    MatchScenario.FirstPlayer,
                    CardZone.BarChit,
                    index
                )
            );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(replacement).Concat(barChits).OrderBy(static card => card.Id)
            ),
        };

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-036-B02"))
        );

        applied.Player(MatchScenario.FirstPlayer).BarChitsRemaining.ShouldBe(4);
    }

    [Test]
    public async Task KnockoutBonus_IsNotTakenWhenTheDefenderRecovers()
    {
        var state = MatchScenario.BattleState(
            "BLK-036",
            "BLK-068",
            ["VIM-GEEKED", "VIM-GEEKED", "VIM-GEEKED"],
            SeedForBadge()
        );
        var barChits = Enumerable
            .Range(0, 6)
            .Select(index =>
                MatchScenario.Card(
                    $"recovery-bar-chit-{index}",
                    "VIM-SOBER",
                    MatchScenario.FirstPlayer,
                    CardZone.BarChit,
                    index
                )
            );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender" ? card with { Damage = 150 } : card
                    )
                    .Concat(barChits)
                    .OrderBy(static card => card.Id)
            ),
        };

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-036-B02"))
        );

        applied.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Oche);
        applied.Player(MatchScenario.FirstPlayer).BarChitsRemaining.ShouldBe(6);
    }

    [Test]
    public async Task QuadrupleWeakness_AppliesToTheDefendersPrintedWeakness()
    {
        var state = MatchScenario.BattleState(
            "BLK-001",
            "BLK-076",
            ["VIM-BLAZED", "VIM-SOBER"],
            617
        );
        var abilitySource = MatchScenario.Card(
            "ability-source",
            "BLK-141",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCard(state, abilitySource);

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-001-B01"))
        );

        applied.Card(new CardInstanceId("defender")).Damage.ShouldBe(80);
    }

    [Test]
    public async Task QuadrupleWeakness_DoesNotApplyWhenThePrintedWeaknessDoesNotMatch()
    {
        var state = MatchScenario.BattleState(
            "BLK-001",
            "BLK-150",
            ["VIM-BLAZED", "VIM-SOBER"],
            618
        );
        var abilitySource = MatchScenario.Card(
            "ability-source",
            "BLK-141",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCard(state, abilitySource);

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-001-B01"))
        );

        applied.Card(new CardInstanceId("defender")).Damage.ShouldBe(20);
    }

    [Test]
    public async Task QuadrupleWeakness_AppliesToAChosenReplacementWeakness()
    {
        var state = MatchScenario.BattleState(
            "BLK-001",
            "BLK-150",
            ["VIM-BLAZED", "VIM-SOBER"],
            618
        );
        state = state with
        {
            Effects = FrozenList<TemporaryEffect>.Create(
                new TemporaryEffect(
                    new EffectId("BLK-137-B01"),
                    new CardInstanceId("chosen-weakness-source"),
                    MatchScenario.FirstPlayer,
                    new CardInstanceId("defender"),
                    TemporaryEffectKind.ModifySoftSpot,
                    1,
                    FrozenList<BlokemonMechanicalType>.Create(BlokemonMechanicalType.Grass),
                    [],
                    [],
                    [],
                    EffectDuration.WhileTargetInPlay,
                    state.RoundNumber,
                    state.RoundNumber
                ),
                new TemporaryEffect(
                    new EffectId("BLK-141-T01"),
                    new CardInstanceId("quadruple-source"),
                    MatchScenario.FirstPlayer,
                    new CardInstanceId("defender"),
                    TemporaryEffectKind.ModifySoftSpot,
                    4,
                    [],
                    [],
                    [],
                    [],
                    EffectDuration.WhileSourceInPlay,
                    state.RoundNumber,
                    state.RoundNumber
                )
            ),
        };

        var applied = MatchScenario.Applied(
            MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand(state, "BLK-001-B01"))
        );

        applied.Card(new CardInstanceId("defender")).Damage.ShouldBe(80);
    }

    [Test]
    public async Task AbilityProof_PreventsAnOpposingAbilityFromPlacingDamageCounters()
    {
        var state = MatchScenario.BattleState("BLK-121", "KIT-003", [], 619);
        var ownBench = MatchScenario.Card(
            "own-booth",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = AddCard(state, ownBench);
        var applied = ExecuteAction(
            state,
            candidate =>
                candidate.Kind == LegalActionKind.UsePartyTrick
                && candidate.Command is MatchCommand.UsePartyTrick command
                && command.Effect == new EffectId("BLK-121-T01")
        ).State;

        applied.Card(new CardInstanceId("defender")).Damage.ShouldBe(0);
    }

    [Test]
    public async Task AttackEffectShield_PreventsAnAttackFromDiscardingAttachedVim()
    {
        var state = MatchScenario.BattleState(
            "BLK-023",
            "BLK-014",
            ["VIM-DODGY", "VIM-DODGY"],
            SeedForBadge()
        );
        var opposingVim = MatchScenario.Card(
            "opposing-vim",
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
                                Attachments = FrozenList<CardInstanceId>.Create(opposingVim.Id),
                            }
                            : card
                    )
                    .Append(opposingVim)
                    .OrderBy(static card => card.Id)
            ),
        };

        var applied = ExecuteAttack(state, "BLK-023-B01").State;

        applied.Card(opposingVim.Id).AttachedTo.ShouldBe(new CardInstanceId("defender"));
    }

    [Test]
    public async Task EveryDeclaredAttack_ExecutesDeterministicallyInARichBattleState()
    {
        var failures = new List<string>();
        foreach (var card in MatchScenario.Authority.Collectibles)
        {
            foreach (var attack in card.Attacks)
            {
                try
                {
                    var state = RichBattleState(card.Id);
                    var first = ExecuteAttack(state, attack.MechanicalId);
                    var repeated = ExecuteAttack(state, attack.MechanicalId);
                    if (
                        first.State != repeated.State
                        || !first.Events.SequenceEqual(repeated.Events)
                    )
                    {
                        failures.Add($"{attack.MechanicalId}: repeated execution diverged");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{attack.MechanicalId}: {exception.GetType().Name}: {exception.Message}"
                    );
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public async Task EveryActivatedPartyTrick_ExecutesDeterministicallyInARichBattleState()
    {
        var failures = new List<string>();
        foreach (var card in MatchScenario.Authority.Collectibles)
        {
            foreach (
                var trick in card.PartyTricks.Where(static trick =>
                    trick.Trigger == BlokemonTrigger.Activated
                )
            )
            {
                try
                {
                    var state = RichBattleState(card.Id);
                    var first = ExecuteAction(
                        state,
                        action =>
                            action.Kind == LegalActionKind.UsePartyTrick
                            && action.Command is MatchCommand.UsePartyTrick command
                            && command.Effect == new EffectId(trick.MechanicalId)
                    );
                    var repeated = ExecuteAction(
                        state,
                        action =>
                            action.Kind == LegalActionKind.UsePartyTrick
                            && action.Command is MatchCommand.UsePartyTrick command
                            && command.Effect == new EffectId(trick.MechanicalId)
                    );
                    if (
                        first.State != repeated.State
                        || !first.Events.SequenceEqual(repeated.Events)
                    )
                    {
                        failures.Add($"{trick.MechanicalId}: repeated execution diverged");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{trick.MechanicalId}: {exception.GetType().Name}: {exception.Message}"
                    );
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public async Task EveryKitPlay_ExecutesDeterministicallyInARichBattleState()
    {
        var failures = new List<string>();
        foreach (var kit in MatchScenario.Authority.Kits)
        {
            try
            {
                var state = KitState(kit.Id);
                var first = ExecuteAction(
                    state,
                    action =>
                        action.Kind == LegalActionKind.PlayKit
                        && action.Command is MatchCommand.PlayKit command
                        && command.Kit == new CardInstanceId("kit-under-test")
                );
                var repeated = ExecuteAction(
                    state,
                    action =>
                        action.Kind == LegalActionKind.PlayKit
                        && action.Command is MatchCommand.PlayKit command
                        && command.Kit == new CardInstanceId("kit-under-test")
                );
                if (first.State != repeated.State || !first.Events.SequenceEqual(repeated.Events))
                {
                    failures.Add($"{kit.Id}: repeated execution diverged");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{kit.Id}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public async Task EveryPromotionTrigger_ExecutesDeterministicallyInARichBattleState()
    {
        var failures = new List<string>();
        foreach (var promotion in MatchScenario.Authority.Collectibles)
        {
            foreach (
                var trick in promotion.PartyTricks.Where(static trick =>
                    trick.Trigger == BlokemonTrigger.OnPromotionFromMitt
                )
            )
            {
                try
                {
                    var state = PromotionState(promotion);
                    var first = ExecuteAction(
                        state,
                        action =>
                            action.Kind == LegalActionKind.Promote
                            && action.Command is MatchCommand.Promote command
                            && command.Promotion == new CardInstanceId("promotion")
                            && command.Bloke == new CardInstanceId("attacker")
                    );
                    var repeated = ExecuteAction(
                        state,
                        action =>
                            action.Kind == LegalActionKind.Promote
                            && action.Command is MatchCommand.Promote command
                            && command.Promotion == new CardInstanceId("promotion")
                            && command.Bloke == new CardInstanceId("attacker")
                    );
                    if (
                        first.State != repeated.State
                        || !first.Events.SequenceEqual(repeated.Events)
                    )
                    {
                        failures.Add($"{trick.MechanicalId}: repeated execution diverged");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{trick.MechanicalId}: {exception.GetType().Name}: {exception.Message}"
                    );
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public async Task EveryContinuousPartyTrick_RefreshesDeterministically()
    {
        var failures = new List<string>();
        var tricks = MatchScenario
            .Authority.Collectibles.SelectMany(card =>
                card.PartyTricks.Select(trick => (CardId: card.Id, Trick: trick))
            )
            .Concat(
                MatchScenario.Authority.Kits.SelectMany(card =>
                    card.PartyTricks.Select(trick => (CardId: card.Id, Trick: trick))
                )
            )
            .Where(static item => item.Trick.Trigger == BlokemonTrigger.Continuous);
        foreach (var (cardId, trick) in tricks)
        {
            try
            {
                var state = ContinuousState(cardId);
                var first = ExecuteAction(
                    state,
                    action =>
                        action.Kind == LegalActionKind.PlayBloke
                        && action.Command is MatchCommand.PlayBloke command
                        && command.Bloke == new CardInstanceId("own-mitt-bloke")
                );
                var repeated = ExecuteAction(
                    state,
                    action =>
                        action.Kind == LegalActionKind.PlayBloke
                        && action.Command is MatchCommand.PlayBloke command
                        && command.Bloke == new CardInstanceId("own-mitt-bloke")
                );
                if (first.State != repeated.State || !first.Events.SequenceEqual(repeated.Events))
                {
                    failures.Add($"{trick.MechanicalId}: repeated refresh diverged");
                }
                else if (
                    cardId != "BLK-040"
                    && !first.State.Effects.Any(effect =>
                        effect.SourceEffect == new EffectId(trick.MechanicalId)
                    )
                )
                {
                    failures.Add($"{trick.MechanicalId}: no continuous effect was registered");
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{trick.MechanicalId}: {exception.GetType().Name}: {exception.Message}"
                );
            }
        }

        failures.ShouldBeEmpty();
    }

    private static AttackExecution ExecuteAttack(MatchState initial, string attackId)
    {
        var engine = MatchScenario.Engine();
        var action = engine
            .GetLegalActions(initial, MatchScenario.FirstPlayer)
            .Single(candidate =>
                candidate.Kind == LegalActionKind.Attack
                && candidate.Command is MatchCommand.Attack attack
                && attack.AttackId == new EffectId(attackId)
            );
        var events = new List<MatchEvent>();
        var state = initial;
        state = Apply(action.Command);
        var resolutions = 0;
        while (
            state.Phase != MatchPhase.Complete
            && (
                state.Phase != MatchPhase.Playing || state.ActivePlayer == MatchScenario.FirstPlayer
            )
        )
        {
            if (resolutions++ >= 20)
            {
                throw new InvalidOperationException("Effect resolution did not settle.");
            }

            var chooser = state.Phase switch
            {
                MatchPhase.AwaitingEffectChoice => state.PendingEffect!.Chooser,
                MatchPhase.AwaitingTriggerChoice => state.PendingKnockout?.Chooser
                    ?? state.PendingBarChits[0].Player,
                MatchPhase.AwaitingReplacement => state.ReplacementPlayer!.Value,
                _ => throw new InvalidOperationException(
                    $"Attack remained with {state.ActivePlayer.Value} in phase {state.Phase}."
                ),
            };
            var resolution = engine.GetLegalActions(state, chooser).FirstOrDefault();
            if (resolution is null)
            {
                throw new InvalidOperationException(
                    $"No legal resolution for {chooser.Value} in phase {state.Phase}."
                );
            }

            state = Apply(resolution.Command);
        }

        return new AttackExecution(state, FrozenList<MatchEvent>.Create(events));

        MatchState Apply(MatchCommand command)
        {
            var applied = (CommandOutcome.Applied)engine.Apply(state, command);
            events.AddRange(applied.Events);
            return applied.State;
        }
    }

    private static AttackExecution ExecuteAction(MatchState initial, Func<LegalAction, bool> select)
    {
        var engine = MatchScenario.Engine();
        var action = engine.GetLegalActions(initial, MatchScenario.FirstPlayer).First(select);
        var events = new List<MatchEvent>();
        var state = initial;
        state = Apply(action.Command);
        var resolutions = 0;
        while (state.Phase != MatchPhase.Playing && state.Phase != MatchPhase.Complete)
        {
            if (resolutions++ >= 20)
            {
                throw new InvalidOperationException("Effect resolution did not settle.");
            }

            var chooser = state.Phase switch
            {
                MatchPhase.AwaitingEffectChoice => state.PendingEffect!.Chooser,
                MatchPhase.AwaitingTriggerChoice => state.PendingKnockout?.Chooser
                    ?? state.PendingBarChits[0].Player,
                MatchPhase.AwaitingReplacement => state.ReplacementPlayer!.Value,
                _ => throw new InvalidOperationException($"Unexpected phase {state.Phase}."),
            };
            var resolution = engine.GetLegalActions(state, chooser).FirstOrDefault();
            if (resolution is null)
            {
                throw new InvalidOperationException(
                    $"No legal resolution for {chooser.Value} in phase {state.Phase}."
                );
            }

            state = Apply(resolution.Command);
        }

        return new AttackExecution(state, FrozenList<MatchEvent>.Create(events));

        MatchState Apply(MatchCommand command)
        {
            var applied = (CommandOutcome.Applied)engine.Apply(state, command);
            events.AddRange(applied.Events);
            return applied.State;
        }
    }

    private static MatchState KitState(string kitId)
    {
        var state = RichBattleState("BLK-001");
        var kit = MatchScenario.Card(
            "kit-under-test",
            kitId,
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        return state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Where(card => card.Id.Value != "local")
                    .Append(kit)
                    .OrderBy(static card => card.Id)
            ),
            RoundUsage = RoundUsage.Empty(MatchScenario.FirstPlayer),
        };
    }

    private static MatchState PromotionState(BlokemonCollectible promotion)
    {
        var state = RichBattleState(promotion.PromotesFromId!);
        var promotionCard = MatchScenario.Card(
            "promotion",
            promotion.Id,
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        return state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(promotionCard).OrderBy(static card => card.Id)
            ),
        };
    }

    private static MatchState ContinuousState(string cardId)
    {
        var state = RichBattleState(cardId);
        if (cardId == "BLK-021")
        {
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
            };
        }

        if (cardId == "BLK-034")
        {
            state = AddCard(
                state,
                MatchScenario.Card(
                    "named-bloke",
                    "BLK-031",
                    MatchScenario.FirstPlayer,
                    CardZone.Booth,
                    -1
                )
            );
        }

        if (cardId == "BLK-104")
        {
            state = state with
            {
                Cards = FrozenList<CardState>.Create(
                    state
                        .Cards.Select(card =>
                            card.Id.Value == "attacker" ? card with { Zone = CardZone.Booth } : card
                        )
                        .Append(
                            MatchScenario.Card(
                                "replacement-active",
                                "BLK-001",
                                MatchScenario.FirstPlayer,
                                CardZone.Oche,
                                -1
                            )
                        )
                        .Append(
                            MatchScenario.Card(
                                "named-booth-bloke",
                                "BLK-105",
                                MatchScenario.FirstPlayer,
                                CardZone.Booth,
                                -1
                            )
                        )
                        .OrderBy(static card => card.Id)
                ),
            };
        }

        if (cardId == "BLK-122")
        {
            state = state with
            {
                Cards = FrozenList<CardState>.Create(
                    state
                        .Cards.Where(card =>
                            !(
                                card.Owner == MatchScenario.FirstPlayer
                                && card.Zone == CardZone.Attached
                                && card.Kind == CardKind.Vim
                                && card.Id.Value != "vim-0"
                            )
                        )
                        .Select(card =>
                            card.Id.Value == "attacker"
                                ? card with
                                {
                                    Attachments = FrozenList<CardInstanceId>.Create(
                                        new CardInstanceId("vim-0")
                                    ),
                                }
                                : card
                        )
                        .OrderBy(static card => card.Id)
                ),
            };
        }

        return state;
    }

    private static MatchState AddCard(MatchState state, CardState card) =>
        state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(card).OrderBy(static candidate => candidate.Id)
            ),
        };

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

        throw new InvalidOperationException("No badge-side seed found.");
    }

    private static MatchState RichBattleState(string attacker)
    {
        var vim = MatchScenario
            .Authority.BasicVim.Select(static card => card.Id)
            .SelectMany(static id => Enumerable.Repeat(id, 4))
            .ToArray();
        var state = MatchScenario.BattleState(attacker, "BLK-150", vim, 613);
        var additionalCards = new[]
        {
            MatchScenario.Card(
                "own-booth-1",
                "BLK-001",
                MatchScenario.FirstPlayer,
                CardZone.Booth,
                -1
            ),
            MatchScenario.Card(
                "own-booth-2",
                "BLK-004",
                MatchScenario.FirstPlayer,
                CardZone.Booth,
                -1
            ),
            MatchScenario.Card(
                "other-booth-1",
                "BLK-001",
                MatchScenario.SecondPlayer,
                CardZone.Booth,
                -1
            ),
            MatchScenario.Card(
                "other-booth-2",
                "BLK-004",
                MatchScenario.SecondPlayer,
                CardZone.Booth,
                -1
            ),
            MatchScenario.Card(
                "own-mitt-vim",
                "VIM-SOBER",
                MatchScenario.FirstPlayer,
                CardZone.Mitt,
                -1
            ),
            MatchScenario.Card(
                "own-mitt-bloke",
                "BLK-001",
                MatchScenario.FirstPlayer,
                CardZone.Mitt,
                -1
            ),
            MatchScenario.Card(
                "own-mitt-kit",
                "KIT-012",
                MatchScenario.FirstPlayer,
                CardZone.Mitt,
                -1
            ),
            MatchScenario.Card(
                "other-mitt-bloke",
                "BLK-001",
                MatchScenario.SecondPlayer,
                CardZone.Mitt,
                -1
            ),
            MatchScenario.Card(
                "other-mitt-kit",
                "KIT-012",
                MatchScenario.SecondPlayer,
                CardZone.Mitt,
                -1
            ),
            MatchScenario.Card(
                "own-stack-bloke",
                "BLK-001",
                MatchScenario.FirstPlayer,
                CardZone.Stack,
                0
            ),
            MatchScenario.Card(
                "own-stack-vim",
                "VIM-BLAZED",
                MatchScenario.FirstPlayer,
                CardZone.Stack,
                1
            ),
            MatchScenario.Card(
                "own-stack-kit",
                "KIT-006",
                MatchScenario.FirstPlayer,
                CardZone.Stack,
                2
            ),
            MatchScenario.Card(
                "own-empties-bloke",
                "BLK-001",
                MatchScenario.FirstPlayer,
                CardZone.EmptiesTray,
                -1
            ),
            MatchScenario.Card(
                "own-empties-vim",
                "VIM-BLAZED",
                MatchScenario.FirstPlayer,
                CardZone.EmptiesTray,
                -1
            ),
            MatchScenario.Card(
                "own-empties-vim-2",
                "VIM-BLAZED",
                MatchScenario.FirstPlayer,
                CardZone.EmptiesTray,
                -1
            ),
            MatchScenario.Card(
                "own-empties-kit",
                "KIT-012",
                MatchScenario.FirstPlayer,
                CardZone.EmptiesTray,
                -1
            ),
            MatchScenario.Card("local", "KIT-006", MatchScenario.FirstPlayer, CardZone.Local, -1),
            MatchScenario.Card(
                "other-vim",
                "VIM-SOBER",
                MatchScenario.SecondPlayer,
                CardZone.Attached,
                -1,
                attachedTo: new CardInstanceId("defender")
            ),
        };
        return state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Where(card => card.Id.Value != "first-draw")
                    .Select(card =>
                        card.Id.Value == "attacker"
                            ? card with
                            {
                                Damage = 10,
                                RoughStates = FrozenList<RoughStateEntry>.Create(
                                    new RoughStateEntry(BlokemonRoughState.DodgyPint, 1)
                                ),
                            }
                        : card.Id.Value == "defender"
                            ? card with
                            {
                                Damage = 10,
                                Attachments = FrozenList<CardInstanceId>.Create(
                                    new CardInstanceId("other-vim")
                                ),
                                RoughStates = FrozenList<RoughStateEntry>.Create(
                                    new RoughStateEntry(BlokemonRoughState.DodgyPint, 1)
                                ),
                            }
                        : card
                    )
                    .Concat(additionalCards)
                    .OrderBy(static card => card.Id)
            ),
            RoundUsage = new RoundUsage(MatchScenario.FirstPlayer, 0, 1, 0, 0, [], []),
        };
    }

    private sealed record AttackExecution(MatchState State, FrozenList<MatchEvent> Events);
}
