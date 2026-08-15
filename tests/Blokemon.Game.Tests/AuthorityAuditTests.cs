using System.Text.Json;
using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class AuthorityAuditTests
{
    [Test]
    public async Task DeclaredReactiveTriggerProgram_ControlsRuntimeOutcome()
    {
        var owner = MatchScenario.Authority.Collectibles.Single(card => card.Id == "BLK-107");
        var trigger = owner.PartyTricks.Single(trick => trick.MechanicalId == "BLK-107-T01");
        var changedTrigger = trigger with
        {
            Program = ReplaceInstructionAmount(
                trigger.Program,
                BlokemonOpcode.PlaceDamageCounters,
                4
            ),
        };
        var changedOwner = owner with { PartyTricks = [changedTrigger] };
        var authority = MatchScenario.Authority with
        {
            Collectibles =
            [
                .. MatchScenario.Authority.Collectibles.Select(card =>
                    card.Id == changedOwner.Id ? changedOwner : card
                ),
            ],
        };
        var engine = new MatchEngine(authority);
        var state = MatchScenario.BattleState("BLK-076", "BLK-107", ["VIM-LAIRY"], 107);

        var applied = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-076-B01"))
        );

        await Assert.That(applied.Card(new CardInstanceId("attacker")).Damage).IsEqualTo(40);
    }

    [Test]
    public async Task ReconciledEffects_AllHaveExecutableSemanticShapesAndProvenance()
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

        await Assert
            .That(root.GetProperty("authorityVersion").GetString())
            .IsEqualTo(MatchScenario.Authority.ManifestVersion);
        await Assert.That(documented.SequenceEqual(declared)).IsTrue();
        await Assert.That(reconciled.Length).IsEqualTo(310);
        await Assert
            .That(
                reconciled.Count(effect =>
                    effect.GetProperty("disposition").GetString() == "CorrectedFromCandidate6"
                )
            )
            .IsEqualTo(83);
        await Assert.That(audit.EffectCount).IsEqualTo(310);
        await Assert.That(audit.InstructionCount).IsEqualTo(644);
        await Assert.That(audit.Issues.Count).IsEqualTo(0);
    }

    private static BlokemonEffectInstruction[] ReplaceInstructionAmount(
        BlokemonEffectInstruction[] program,
        BlokemonOpcode opcode,
        int amount
    ) =>
        program
            .Select(instruction =>
                (
                    instruction.Opcode == opcode
                        ? instruction with
                        {
                            Amount = amount,
                        }
                        : instruction
                ) with
                {
                    Then = ReplaceInstructionAmount(instruction.Then, opcode, amount),
                    Otherwise = ReplaceInstructionAmount(instruction.Otherwise, opcode, amount),
                }
            )
            .ToArray();
}
