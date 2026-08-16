using Blokemon.Game;
using Shouldly;

namespace Blokemon.Game.Tests;

public sealed class TopStackDistributionTests
{
    [Test]
    public async Task PromotionTrigger_AttachesOnlyTheSelectedBasicVimFromTheTopStackWindow()
    {
        var engine = MatchScenario.Engine();
        var state = MatchScenario.BattleState("BLK-043", "BLK-001", [], 59);
        var retained = state.Cards.Where(card => card.Id.Value != "first-draw");
        var promotion = MatchScenario.Card(
            "promotion",
            "BLK-044",
            MatchScenario.FirstPlayer,
            CardZone.Mitt,
            -1
        );
        var topGrass = MatchScenario.Card(
            "top-grass",
            "VIM-BLAZED",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            0
        );
        var topBloke = MatchScenario.Card(
            "top-bloke",
            "BLK-004",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            1
        );
        var topWater = MatchScenario.Card(
            "top-water",
            "VIM-SOBER",
            MatchScenario.FirstPlayer,
            CardZone.Stack,
            2
        );
        state = state with
        {
            Cards = FrozenList<CardState>.Create(
                retained
                    .Append(promotion)
                    .Append(topGrass)
                    .Append(topBloke)
                    .Append(topWater)
                    .OrderBy(card => card.Id)
            ),
        };
        var choices = FrozenList<EffectChoice>.Create(
            new EffectChoice.Optional(new EffectChoiceId("BLK-044-T01:root/0:optional"), true),
            new EffectChoice.Attachments(
                new EffectChoiceId("BLK-044-T01:root/0/then/3:attachments"),
                FrozenList<VimAttachment>.Create(
                    new VimAttachment(
                        new CardInstanceId("top-grass"),
                        new CardInstanceId("promotion")
                    )
                )
            )
        );
        var command = new MatchCommand.Promote(
            new CommandId("promote-with-top-stack-vim"),
            state.Id,
            MatchScenario.FirstPlayer,
            state.Revision,
            promotion.Id,
            new CardInstanceId("attacker"),
            choices
        );

        var applied = (CommandOutcome.Applied)engine.Apply(state, command);

        applied.State.Card(topGrass.Id).AttachedTo.ShouldBe(promotion.Id);
        applied.State.Card(topWater.Id).Zone.ShouldBe(CardZone.Stack);
        applied.State.Card(topWater.Id).AttachedTo.ShouldBeNull();
        applied.State.Card(topBloke.Id).Zone.ShouldBe(CardZone.Stack);
    }
}
