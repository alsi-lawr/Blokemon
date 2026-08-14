using Blokemon.Core.SetDesign;
using Blokemon.Game;

namespace Blokemon.Game.Tests;

public sealed class DamageAndRoughStateTests
{
    [Test]
    public async Task AttackDamage_AppliesSoftSpotBeforeDefenderReduction()
    {
        var state = MatchScenario.BattleState(
            "BLK-001",
            "BLK-028",
            ["VIM-BLAZED", "VIM-SOBER"],
            17
        );
        var defender = state.Card(new CardInstanceId("defender"));
        var barKit = MatchScenario.Card(
            "bar-kit",
            "KIT-014",
            MatchScenario.SecondPlayer,
            CardZone.Attached,
            -1,
            attachedTo: defender.Id
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                state
                    .Cards.Where(card => card.Id != defender.Id)
                    .Append(
                        defender with
                        {
                            Attachments = FrozenList<CardInstanceId>.Create(barKit.Id),
                        }
                    )
                    .Append(barKit)
                    .OrderBy(static card => card.Id)
            ),
        };
        var engine = MatchScenario.Engine();

        var result = MatchScenario.Applied(
            engine.Apply(state, MatchScenario.AttackCommand(state, "BLK-001-B01"))
        );

        await Assert.That(result.Card(new CardInstanceId("defender")).Damage).IsEqualTo(10);
        await Assert
            .That(result.CardsIn(MatchScenario.FirstPlayer, CardZone.Oche).Single().Damage)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Checkup_ResolvesMarkerDamageBeforeRecoveryAndClearsExpiredLegless()
    {
        var roughStates = FrozenList<RoughStateEntry>.Create(
            new RoughStateEntry(BlokemonRoughState.DodgyPint, 2),
            new RoughStateEntry(BlokemonRoughState.Singed, 2),
            new RoughStateEntry(BlokemonRoughState.Legless, 1)
        );
        var state = MatchScenario.BattleState(
            "BLK-003",
            "BLK-001",
            ["VIM-BLAZED", "VIM-BLAZED", "VIM-SOBER"],
            23,
            attackerRoughStates: roughStates
        );
        var engine = MatchScenario.Engine();
        var command = new MatchCommand.EndRound(
            new CommandId("checkup"),
            state.Id,
            MatchScenario.FirstPlayer,
            state.Revision
        );

        var applied = (CommandOutcome.Applied)engine.Apply(state, command);
        var checkedCard = applied.State.Card(new CardInstanceId("attacker"));
        var damageEvents = applied.Events.Where(matchEvent =>
            matchEvent.Kind == MatchEventKind.DamagePlaced
            && matchEvent.SourceCard is null
            && matchEvent.TargetCards.Contains(new CardInstanceId("attacker"))
        );

        await Assert.That(checkedCard.Damage).IsEqualTo(30);
        await Assert
            .That(checkedCard.RoughStates.Any(entry => entry.State == BlokemonRoughState.DodgyPint))
            .IsTrue();
        await Assert
            .That(checkedCard.RoughStates.Any(entry => entry.State == BlokemonRoughState.Legless))
            .IsFalse();
        await Assert
            .That(damageEvents.Select(static matchEvent => matchEvent.Amount))
            .IsEquivalentTo([10, 20]);
    }
}
