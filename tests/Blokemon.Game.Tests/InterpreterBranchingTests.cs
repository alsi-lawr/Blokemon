using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class InterpreterBranchingTests
{
    [Test]
    public async Task CurrentAuthority_HasNoUnsupportedInterpreterInventory()
    {
        var audit = new BlokemonInterpreter(MatchScenario.Authority).AuditAuthority();

        await Assert.That(audit.IsInventoryComplete).IsTrue();
        await Assert.That(audit.EffectCount).IsEqualTo(310);
        await Assert.That(audit.InstructionCount).IsEqualTo(643);
    }

    [Test]
    public async Task BeerMatConditional_UsesBadgeAndBlankBranchesDeterministically()
    {
        var badgeSeed = SeedFor(firstBadge: true);
        var blankSeed = SeedFor(firstBadge: false);
        var engine = MatchScenario.Engine();
        var badgeState = MatchScenario.BattleState("BLK-056", "BLK-001", ["VIM-LAIRY"], badgeSeed);
        var blankState = MatchScenario.BattleState("BLK-056", "BLK-001", ["VIM-LAIRY"], blankSeed);

        var badgeResult = MatchScenario.Applied(
            engine.Apply(badgeState, MatchScenario.AttackCommand(badgeState, "BLK-056-B01"))
        );
        var blankResult = MatchScenario.Applied(
            engine.Apply(blankState, MatchScenario.AttackCommand(blankState, "BLK-056-B01"))
        );

        await Assert.That(badgeResult.Card(new CardInstanceId("defender")).Damage).IsEqualTo(40);
        await Assert.That(badgeResult.Card(new CardInstanceId("attacker")).Damage).IsEqualTo(0);
        await Assert.That(blankResult.Card(new CardInstanceId("defender")).Damage).IsEqualTo(20);
        await Assert.That(blankResult.Card(new CardInstanceId("attacker")).Damage).IsEqualTo(20);
    }

    [Test]
    public async Task OptionalCardMovement_RequiresAnExplicitBranchChoice()
    {
        var state = MatchScenario.BattleState("BLK-008", "BLK-003", ["VIM-SOBER"], 41);
        var engine = MatchScenario.Engine();
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-008-B01"));
        var optional = missing.Rejection.ChoiceRequirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Optional
        );
        var declined = MatchScenario.AttackCommand(
            state,
            "BLK-008-B01",
            FrozenList<EffectChoice>.Create(new EffectChoice.Optional(optional.Id, false))
        );

        var accepted = engine.Apply(state, declined);

        await Assert.That(missing.Rejection.Code).IsEqualTo(CommandRejectionCode.ChoiceRequired);
        await Assert.That(ReferenceEquals(missing.State, state)).IsTrue();
        await Assert.That(accepted).IsTypeOf<CommandOutcome.Applied>();
    }

    [Test]
    public async Task BeerMatSiblingEffect_DoesNotSwapOnBlankSide()
    {
        var blankSeed = SeedFor(firstBadge: false);
        var state = MatchScenario.BattleState("BLK-052", "BLK-001", ["VIM-SOBER"], blankSeed);
        var boothCard = MatchScenario.Card(
            "other-booth",
            "BLK-002",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(boothCard).OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var missing = (CommandOutcome.Rejected)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-052-B01"));
        var targetChoice = missing.Rejection.ChoiceRequirements.Single(requirement =>
            requirement.Kind == ChoiceRequirementKind.Cards
        );
        var command = MatchScenario.AttackCommand(
            state,
            "BLK-052-B01",
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    targetChoice.Id,
                    FrozenList<CardInstanceId>.Create(boothCard.Id)
                )
            )
        );

        var applied = MatchScenario.Applied(engine.Apply(state, command));

        await Assert
            .That(applied.Card(new CardInstanceId("defender")).Zone)
            .IsEqualTo(CardZone.Oche);
        await Assert.That(applied.Card(boothCard.Id).Zone).IsEqualTo(CardZone.Booth);
    }

    [Test]
    public async Task RequiredChoiceAction_IsLegalAndCpuMaterializesAnApplicableCommand()
    {
        var state = MatchScenario.BattleState(
            "BLK-052",
            "BLK-001",
            ["VIM-SOBER"],
            SeedFor(firstBadge: true)
        );
        var boothCard = MatchScenario.Card(
            "other-booth",
            "BLK-002",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state.Cards.Append(boothCard).OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();
        var legalAttack = engine
            .GetLegalActions(state, MatchScenario.FirstPlayer)
            .Single(action => action.Kind == LegalActionKind.Attack);
        var cpu = new DeterministicCpu();
        var decision = (CpuDecision.Selected)cpu.Choose(engine, state, MatchScenario.FirstPlayer);

        var applied = engine.Apply(state, decision.Action.Command);

        await Assert.That(legalAttack.ChoiceRequirements.Count).IsGreaterThan(0);
        await Assert.That(legalAttack.Command.Choices.Count).IsGreaterThan(0);
        await Assert.That(decision.Action.Kind).IsEqualTo(LegalActionKind.Attack);
        await Assert.That(applied).IsTypeOf<CommandOutcome.Applied>();
    }

    private static ulong SeedFor(bool firstBadge)
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            var random = new BlokemonSeededRandom(seed);
            if ((random.NextInt(2) == 1) == firstBadge)
            {
                return seed;
            }
        }

        return 0;
    }
}
