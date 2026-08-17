using System.Text.Json;
using Blokemon.Core.SetDesign;
using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class AuthorityAuditTests
{
    [Test]
    [MethodDataSource(nameof(ReactiveTriggerCases))]
    public async Task DeclaredReactiveTriggerProgramMutation_ControlsRuntimeOutcome(
        ReactiveTriggerCase testCase
    )
    {
        var owner = MatchScenario.Authority.Collectibles.Single(card => card.Id == testCase.CardId);
        var trigger = owner.PartyTricks.Single(trick => trick.MechanicalId == testCase.EffectId);
        var changedTrigger = trigger.WithProgram(
            MutateReactiveProgram(trigger.Program, testCase.Trigger)
        );
        var changedOwner = owner.WithPartyTricks([
            .. owner.PartyTricks.Select(trick =>
                trick.MechanicalId == changedTrigger.MechanicalId ? changedTrigger : trick
            ),
        ]);
        var authority = MatchScenario.Authority.WithCollectibles([
            .. MatchScenario.Authority.Collectibles.Select(card =>
                card.Id == changedOwner.Id ? changedOwner : card
            ),
        ]);
        var engine = new MatchEngine(authority);

        var baseline = ObserveReactiveTrigger(MatchScenario.Engine(), testCase.Trigger);
        var observation = ObserveReactiveTrigger(engine, testCase.Trigger);

        observation.ShouldNotBe(baseline);
        observation.ShouldBe(testCase.ExpectedObservation);
    }

    [Test]
    public async Task Reconciled310Effects_Flatten641InstructionsAfterFossilGateRemoval()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Authorities",
                    "sv151-authority-reconciliation.json"
                )
            )
        );
        var root = document.RootElement;
        var reconciled = root.GetProperty("effects").EnumerateArray().ToArray();
        var declared = MatchScenario
            .Authority.Collectibles.SelectMany(card =>
                card.PartyTricks.Select(effect => effect.MechanicalId)
                    .Concat(card.Attacks.Select(effect => effect.MechanicalId))
                    .Concat(card.HouseRules.Select(effect => effect.MechanicalId))
            )
            .Concat(
                MatchScenario.Authority.Kits.SelectMany(card =>
                    card.PartyTricks.Select(effect => effect.MechanicalId)
                        .Concat(card.Attacks.Select(effect => effect.MechanicalId))
                        .Concat(card.HouseRules.Select(effect => effect.MechanicalId))
                )
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        var documented = reconciled
            .Select(effect => effect.GetProperty("mechanicalId").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var audit = new BlokemonInterpreter(MatchScenario.Authority).AuditAuthority();

        root.GetProperty("authorityVersion")
            .GetString()
            .ShouldBe(MatchScenario.Authority.ManifestVersion);
        documented.SequenceEqual(declared).ShouldBeTrue();
        reconciled.Length.ShouldBe(310);
        reconciled
            .Count(effect =>
                effect.GetProperty("disposition").GetString() == "CorrectedFromCandidate6"
            )
            .ShouldBe(94);
        audit.EffectCount.ShouldBe(310);
        // Candidate.6's 643 was derived before BLK-113's SV151-correct optional Booth branch
        // (+1) and before the three fossil Kits lost their spurious Optional wrappers (-3).
        audit.InstructionCount.ShouldBe(641);
        audit.Issues.Count.ShouldBe(0);
    }

    public static IEnumerable<ReactiveTriggerCase> ReactiveTriggerCases() =>
        [
            new("BLK-026", "BLK-026-T01", BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage, 1),
            new("BLK-068", "BLK-068-T01", BlokemonTrigger.BeforeSelfSentHomeByAttackDamage, 160),
            new("BLK-107", "BLK-107-T01", BlokemonTrigger.AfterSelfDamagedByAttack, 40),
            new("BLK-110", "BLK-110-T01", BlokemonTrigger.AfterSelfSentHomeByAttackDamage, 40),
            new("BLK-113", "BLK-113-T01", BlokemonTrigger.OnBarChitTaken, 0),
        ];

    private static int ObserveReactiveTrigger(MatchEngine engine, BlokemonTrigger trigger) =>
        trigger switch
        {
            BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage => ObserveKnockoutVimMove(engine),
            BlokemonTrigger.BeforeSelfSentHomeByAttackDamage => ObserveRecovery(engine),
            BlokemonTrigger.AfterSelfDamagedByAttack => ObserveDamageRetaliation(engine),
            BlokemonTrigger.AfterSelfSentHomeByAttackDamage => ObserveSendHomeRetaliation(engine),
            BlokemonTrigger.OnBarChitTaken => ObserveBarChitTrigger(engine),
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private static int ObserveKnockoutVimMove(MatchEngine engine)
    {
        var state = MatchScenario.BattleState(
            "BLK-003",
            "BLK-001",
            ["VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER"],
            103
        );
        var triggerSource = MatchScenario.Card(
            "trigger-source",
            "BLK-026",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        var movableVim = MatchScenario.Card(
            "movable-vim",
            "VIM-SOBER",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: new CardInstanceId("defender")
        );
        var prize = MatchScenario.Card(
            "prize",
            "VIM-LAIRY",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            0
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Select(card =>
                        card.Id.Value == "defender"
                            ? card with
                            {
                                Attachments = FrozenList<CardInstanceId>.Create(movableVim.Id),
                            }
                            : card
                    )
                    .Append(triggerSource)
                    .Append(movableVim)
                    .Append(prize)
                    .OrderBy(static card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 1,
                        }
                        : player
                )
            ),
        };
        var attacked = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"));
        if (attacked.State.PendingKnockout is null)
        {
            return 0;
        }

        var resolved = MatchScenario.Applied(
            engine.Apply(
                attacked.State,
                new MatchCommand.ResolveKnockoutTrigger(
                    new CommandId("resolve-mutated-knockout-trigger"),
                    attacked.State.Id,
                    MatchScenario.SecondPlayer,
                    attacked.State.Revision,
                    movableVim.Id
                )
            )
        );
        return resolved.Card(movableVim.Id).AttachedTo == triggerSource.Id ? 1 : 0;
    }

    private static int ObserveRecovery(MatchEngine engine)
    {
        var state = MatchScenario.BattleState(
            "BLK-076",
            "BLK-068",
            ["VIM-LAIRY", "VIM-SOBER", "VIM-SOBER"],
            0
        );
        var applied = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B02"))
        );
        return applied.Card(new CardInstanceId("defender")).Damage;
    }

    private static int ObserveDamageRetaliation(MatchEngine engine)
    {
        var state = MatchScenario.BattleState("BLK-076", "BLK-107", ["VIM-LAIRY"], 107);
        var applied = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B01"))
        );
        return applied.Card(new CardInstanceId("attacker")).Damage;
    }

    private static int ObserveSendHomeRetaliation(MatchEngine engine)
    {
        var state = MatchScenario.BattleState(
            "BLK-076",
            "BLK-110",
            ["VIM-LAIRY", "VIM-SOBER", "VIM-SOBER"],
            SeedForBadge()
        );
        var applied = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B02"))
        );
        return applied.Card(new CardInstanceId("attacker")).Damage;
    }

    private static int ObserveBarChitTrigger(MatchEngine engine)
    {
        var state = MatchScenario.BattleState(
            "BLK-003",
            "BLK-001",
            ["VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER"],
            0
        );
        var triggeredPrize = MatchScenario.Card(
            "triggered-prize",
            "BLK-113",
            MatchScenario.FirstPlayer,
            CardZone.BarChit,
            0
        );
        var extraPrizes = new[]
        {
            MatchScenario.Card(
                "extra-prize-1",
                "VIM-LAIRY",
                MatchScenario.FirstPlayer,
                CardZone.BarChit,
                1
            ),
            MatchScenario.Card(
                "extra-prize-2",
                "VIM-SOBER",
                MatchScenario.FirstPlayer,
                CardZone.BarChit,
                2
            ),
        };
        var defenderBench = MatchScenario.Card(
            "defender-bench",
            "BLK-004",
            MatchScenario.SecondPlayer,
            CardZone.Booth,
            -1
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Append(triggeredPrize)
                    .Concat(extraPrizes)
                    .Append(defenderBench)
                    .OrderBy(static card => card.Id)
            ),
            Players = FrozenList<PlayerState>.Create(
                state.Players.Select(player =>
                    player.Id == MatchScenario.FirstPlayer
                        ? player with
                        {
                            BarChitsRemaining = 3,
                        }
                        : player
                )
            ),
        };
        var attacked = (CommandOutcome.Applied)
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-003-B01"));
        var resolved = MatchScenario.Applied(
            engine.Apply(
                attacked.State,
                new MatchCommand.ResolveBarChitTrigger(
                    new CommandId("resolve-mutated-bar-chit-trigger"),
                    attacked.State.Id,
                    MatchScenario.FirstPlayer,
                    attacked.State.Revision,
                    true
                )
            )
        );
        return resolved.Player(MatchScenario.FirstPlayer).BarChitsRemaining;
    }

    private static BlokemonEffectInstruction[] MutateReactiveProgram(
        BlokemonEffectInstruction[] program,
        BlokemonTrigger trigger
    ) =>
        MutateInstructions(
            program,
            instruction =>
                trigger switch
                {
                    BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
                        when instruction.Opcode == BlokemonOpcode.MoveVim =>
                        instruction.WithoutMechanicalTypes(),
                    BlokemonTrigger.BeforeSelfSentHomeByAttackDamage
                        when instruction.Opcode == BlokemonOpcode.RecoverFromSendHome =>
                        instruction.WithAmount(20),
                    BlokemonTrigger.AfterSelfDamagedByAttack
                        when instruction.Opcode == BlokemonOpcode.PlaceDamageCounters =>
                        instruction.WithAmount(4),
                    BlokemonTrigger.AfterSelfSentHomeByAttackDamage
                        when instruction.Opcode == BlokemonOpcode.SendHome =>
                        instruction.WithOpcodeAndAmount(BlokemonOpcode.PlaceDamageCounters, 4),
                    BlokemonTrigger.OnBarChitTaken
                        when instruction.Opcode == BlokemonOpcode.TakeExtraBarChit =>
                        instruction.WithAmount(2),
                    _ => instruction,
                }
        );

    private static BlokemonEffectInstruction[] MutateInstructions(
        BlokemonEffectInstruction[] program,
        Func<BlokemonEffectInstruction, BlokemonEffectInstruction> mutation
    ) =>
        program
            .Select(instruction =>
            {
                var changed = mutation(instruction);
                return changed.WithBranches(
                    MutateInstructions(changed.Then, mutation),
                    MutateInstructions(changed.Otherwise, mutation)
                );
            })
            .ToArray();

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

        return 0;
    }

    public sealed record ReactiveTriggerCase(
        string CardId,
        string EffectId,
        BlokemonTrigger Trigger,
        int ExpectedObservation
    );
}
