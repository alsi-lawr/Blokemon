using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class ExceptionalEffectTests
{
    [Test]
    public async Task SpreadAttack_DamagesEveryOpponentAndSwitchesWithOwnChosenBench()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-106", "BLK-001", ["VIM-LAIRY"], 397);
        var ownBench = MatchScenario.Card(
            "own-bench",
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
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(ownBench).Append(otherBench).OrderBy(static card => card.Id)
            ),
        };
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-106-B01"));
        var choice = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.AttackCommand(
            state,
            "BLK-106-B01",
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(choice.Id, FrozenList<CardInstanceId>.Create(ownBench.Id))
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(state, command));

        await Assert.That(applied.Card(new CardInstanceId("defender")).Damage).IsEqualTo(10);
        await Assert.That(applied.Card(otherBench.Id).Damage).IsEqualTo(10);
        await Assert.That(applied.Card(ownBench.Id).Zone).IsEqualTo(CardZone.Oche);
        await Assert
            .That(applied.Card(new CardInstanceId("attacker")).Zone)
            .IsEqualTo(CardZone.Booth);
    }

    [Test]
    public async Task ToolChuck_UsesChosenAttachedToolsBeforeScalingAttackDamage()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-101", "BLK-150", ["VIM-BEER"], 401);
        var firstTool = MatchScenario.Card(
            "tool-1",
            "KIT-012",
            MatchScenario.FirstPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("attacker")
        );
        var secondTool = MatchScenario.Card(
            "tool-2",
            "KIT-013",
            MatchScenario.FirstPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("attacker")
        );
        state = AddCardsAndAttachments(state, "attacker", firstTool, secondTool);
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-101-B01"));
        var optional = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Optional
        );
        var requested = (CommandOutcome.Applied)
            engine.Apply(
                state,
                MatchScenario.AttackCommand(
                    state,
                    "BLK-101-B01",
                    FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, true))
                )
            );
        var cards = requested.State.PendingEffect!.Requirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.ResolveEffectChoiceCommand(
            requested.State,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    cards.Id,
                    FrozenList<CardInstanceId>.Create(firstTool.Id, secondTool.Id)
                )
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(requested.State, command));

        await Assert.That(applied.Card(new CardInstanceId("defender")).Damage).IsEqualTo(100);
        await Assert.That(applied.Card(firstTool.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
        await Assert.That(applied.Card(secondTool.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    [Test]
    public async Task AttachedEnergyChuck_ChoosesAndDiscardsTheOpposingActiveEnergy()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState(
            "BLK-130",
            "BLK-150",
            ["VIM-SOBER", "VIM-BLAZED", "VIM-CURRY", "VIM-LAIRY"],
            409
        );
        var opposingVim = MatchScenario.Card(
            "opposing-vim",
            "VIM-BEER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        state = AddCardsAndAttachments(state, "defender", opposingVim);
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-130-B01"));
        var cards = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.AttackCommand(
            state,
            "BLK-130-B01",
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(cards.Id, FrozenList<CardInstanceId>.Create(opposingVim.Id))
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(state, command));

        await Assert.That(cards.EligibleCards).IsEquivalentTo([opposingVim.Id]);
        await Assert.That(applied.Card(opposingVim.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    [Test]
    public async Task VoluntarySelfChuck_DamagesTheChosenOpponentWithoutAwardingBarChits()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-121", "BLK-150", [], 419);
        var replacement = MatchScenario.Card(
            "replacement",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(replacement).OrderBy(static card => card.Id)
            ),
        };
        var effect = new EffectId("BLK-121-T01");
        var missing = (CommandOutcome.Rejected)engine.Apply(state, PartyTrick(state, effect, []));
        var optional = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Optional
        );
        var requested = (CommandOutcome.Applied)
            engine.Apply(
                state,
                PartyTrick(
                    state,
                    effect,
                    FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, true))
                )
            );
        var target = requested.State.PendingEffect!.Requirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.ResolveEffectChoiceCommand(
            requested.State,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    target.Id,
                    FrozenList<CardInstanceId>.Create(new CardInstanceId("defender"))
                )
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(requested.State, command));

        await Assert.That(applied.Card(new CardInstanceId("defender")).Damage).IsEqualTo(20);
        await Assert
            .That(applied.Card(new CardInstanceId("attacker")).Zone)
            .IsEqualTo(CardZone.EmptiesTray);
        await Assert
            .That(applied.Player(MatchScenario.SecondPlayer).BarChitsRemaining)
            .IsEqualTo(6);
        await Assert.That(applied.Phase).IsEqualTo(MatchPhase.AwaitingReplacement);
    }

    [Test]
    public async Task FirstTurnTransform_ReplacesSteveAndDiscardsItsAttachments()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-132", "BLK-150", ["VIM-SOBER"], 421);
        var replacement = MatchScenario.Card(
            "stack-basic",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            0
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
                state
                    .Cards.Where(card => card.Id.Value != "first-draw")
                    .Append(replacement)
                    .OrderBy(static card => card.Id)
            ),
        };
        var effect = new EffectId("BLK-132-T01");
        var missing = (CommandOutcome.Rejected)engine.Apply(state, PartyTrick(state, effect, []));
        var optional = missing.Rejection.ChoiceRequirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Optional
        );
        var requested = (CommandOutcome.Applied)
            engine.Apply(
                state,
                PartyTrick(
                    state,
                    effect,
                    FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, true))
                )
            );
        var searched = requested.State.PendingEffect!.Requirements.Single(value =>
            value.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.ResolveEffectChoiceCommand(
            requested.State,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    searched.Id,
                    FrozenList<CardInstanceId>.Create(replacement.Id)
                )
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(requested.State, command));

        await Assert.That(applied.Card(replacement.Id).Zone).IsEqualTo(CardZone.Oche);
        await Assert
            .That(applied.Card(new CardInstanceId("attacker")).Zone)
            .IsEqualTo(CardZone.EmptiesTray);
        await Assert
            .That(applied.Card(new CardInstanceId("vim-0")).Zone)
            .IsEqualTo(CardZone.EmptiesTray);
        await Assert
            .That(applied.Player(MatchScenario.SecondPlayer).BarChitsRemaining)
            .IsEqualTo(6);
    }

    [Test]
    public async Task LocalOncePerRound_UsesTheActivePlayersHandAndIsMaterializedByCpu()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], 431);
        var local = MatchScenario.Card(
            "local",
            "KIT-006",
            MatchScenario.SecondPlayer,
            CardZone.Local,
            -1
        );
        var vim = MatchScenario.Card(
            "mitt-vim",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(local).Append(vim).OrderBy(static card => card.Id)
            ),
        };
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(value =>
                value.Kind == LegalActionKind.UsePartyTrick
                && value.Command is MatchCommand.UsePartyTrick use
                && use.Effect == new EffectId("KIT-006-R01")
            );

        var requested = (CommandOutcome.Applied)engine.Apply(state, action.Command);
        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.FirstPlayer);
        var applied = MatchScenario.Applied(engine.Apply(requested.State, decision.Action.Command));

        await Assert.That(applied.Card(vim.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
        await Assert
            .That(applied.Card(new CardInstanceId("first-draw")).Zone)
            .IsEqualTo(CardZone.Mitt);
        await Assert.That(applied.RoundUsage.EffectsUsed).Contains(new EffectId("KIT-006-R01"));
    }

    [Test]
    public async Task Supporter_PutsAChosenBasicFromOpponentHandOntoBenchThenSwitchesItActive()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-001", "BLK-150", [], 439);
        var kit = MatchScenario.Card(
            "supporter",
            "KIT-009",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var basic = MatchScenario.Card(
            "other-basic",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Mitt,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(kit).Append(basic).OrderBy(static card => card.Id)
            ),
        };
        var action = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(value =>
                value.Kind == LegalActionKind.PlayKit
                && value.Command is MatchCommand.PlayKit play
                && play.Kit == kit.Id
            );

        var applied = MatchScenario.Applied(engine.Apply(state, action.Command));

        await Assert.That(applied.Card(basic.Id).Zone).IsEqualTo(CardZone.Oche);
        await Assert
            .That(applied.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.Booth);
        await Assert.That(applied.Card(kit.Id).Zone).IsEqualTo(CardZone.EmptiesTray);
    }

    private static MatchState AddCardsAndAttachments(
        MatchState state,
        string target,
        params CardState[] attachments
    ) =>
        state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == target
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

    private static MatchCommand.UsePartyTrick PartyTrick(
        MatchState state,
        EffectId effect,
        FrozenList<EffectChoice> choices
    ) =>
        new(
            new CommandId($"command:{effect.Value}"),
            state.Id,
            MatchScenario.FirstPlayer,
            state.Revision,
            new CardInstanceId("attacker"),
            effect,
            choices
        );
}
