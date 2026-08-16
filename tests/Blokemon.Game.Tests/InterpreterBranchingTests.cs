using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class InterpreterBranchingTests
{
    [Test]
    public async Task CurrentAuthority_HasNoUnsupportedInterpreterInventory()
    {
        var audit = new BlokemonInterpreter(MatchScenario.Authority).AuditAuthority();

        audit.IsInventoryComplete.ShouldBeTrue();
        audit.EffectCount.ShouldBe(310);
        audit.InstructionCount.ShouldBe(641);
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

        badgeResult.Card(new CardInstanceId("defender")).Damage.ShouldBe(40);
        badgeResult.Card(new CardInstanceId("attacker")).Damage.ShouldBe(0);
        blankResult.Card(new CardInstanceId("defender")).Damage.ShouldBe(20);
        blankResult.Card(new CardInstanceId("attacker")).Damage.ShouldBe(20);
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

        missing.Rejection.Code.ShouldBe(CommandRejectionCode.ChoiceRequired);
        ReferenceEquals(missing.State, state).ShouldBeTrue();
        accepted.ShouldBeOfType<CommandOutcome.Applied>();
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
        var applied = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-052-B01"))
        );

        applied.PendingEffect.ShouldBeNull();
        applied.Card(new CardInstanceId("defender")).Zone.ShouldBe(CardZone.Oche);
        applied.Card(boothCard.Id).Zone.ShouldBe(CardZone.Booth);
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
        var attackDecision = (CpuDecision.Selected)
            cpu.Choose(engine, state, MatchScenario.FirstPlayer);
        var requested = (CommandOutcome.Applied)engine.Apply(state, attackDecision.Action.Command);
        var pending = requested.State.PendingEffect!;
        var wrongChooser = new MatchCommand.ResolveEffectChoice(
            new CommandId("wrong-branch-chooser"),
            requested.State.Id,
            MatchScenario.SecondPlayer,
            requested.State.Revision,
            FrozenList<EffectChoice>.Create(
                new EffectChoice.Cards(
                    pending.Requirements.Single().Id,
                    FrozenList<CardInstanceId>.Create(boothCard.Id)
                )
            )
        );
        var rejected = (CommandOutcome.Rejected)engine.Apply(requested.State, wrongChooser);
        var choiceDecision = (CpuDecision.Selected)
            cpu.Choose(engine, requested.State, MatchScenario.FirstPlayer);
        var resolved = (CommandOutcome.Applied)
            engine.Apply(requested.State, choiceDecision.Action.Command);

        legalAttack.ChoiceRequirements.Count.ShouldBe(0);
        legalAttack.Command.Choices.Count.ShouldBe(0);
        attackDecision.Action.Kind.ShouldBe(LegalActionKind.Attack);
        requested.State.Phase.ShouldBe(MatchPhase.AwaitingEffectChoice);
        pending.Requirements.Single().Chooser.ShouldBe(MatchScenario.FirstPlayer);
        rejected.Rejection.Code.ShouldBe(CommandRejectionCode.WrongChooser);
        rejected.State.ShouldBe(requested.State);
        choiceDecision.Action.Kind.ShouldBe(LegalActionKind.ResolveEffectChoice);
        resolved.State.PendingEffect.ShouldBeNull();
        resolved.State.Card(boothCard.Id).Zone.ShouldBe(CardZone.Oche);
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
