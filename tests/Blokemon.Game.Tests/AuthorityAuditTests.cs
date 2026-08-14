using System.Text.Json;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class AuthorityAuditTests
{
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
            .IsEqualTo(79);
        await Assert.That(audit.EffectCount).IsEqualTo(310);
        await Assert.That(audit.InstructionCount).IsEqualTo(643);
        await Assert.That(audit.Issues.Count).IsEqualTo(0);
    }
}
