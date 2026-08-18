namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules

/// Continuous party tricks and declarative house rules are re-derived before every command, so a
/// modifier that stopped applying stops mattering without anyone having to remember to remove it.
module internal MatchContinuous =

    let refreshContinuousEffects
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        =
        for source in
            builder.Cards
            |> Seq.filter isInPlay
            |> Seq.sortBy (fun card -> card.Id)
            |> Seq.toArray do
            for trick in
                catalog.PartyTricks source
                |> Seq.filter (fun trick -> trick.Trigger = BlokemonTrigger.Continuous)
                |> Seq.toArray do
                let effect = EffectId trick.MechanicalId
                builder.RemoveEffects(effect, source.Id)

                interpreter.Execute(
                    builder,
                    source.Owner,
                    source,
                    effect,
                    trick.Program,
                    FrozenList.empty,
                    false
                )
                |> ignore

        for source in
            builder.Cards
            |> Seq.filter (fun card -> card.Kind = CardKind.Kit && card.Zone = CardZone.Attached)
            |> Seq.toArray do
            for rule in
                catalog.HouseRules source
                |> Seq.filter (fun rule ->
                    not (containsCondition rule.Program BlokemonCondition.Optional))
                |> Seq.toArray do
                let effect = EffectId rule.MechanicalId
                builder.RemoveEffects(effect, source.Id)

                interpreter.Execute(
                    builder,
                    source.Owner,
                    source,
                    effect,
                    rule.Program,
                    FrozenList.empty,
                    false,
                    true,
                    ValueNone,
                    FrozenList.empty,
                    ValueNone
                )
                |> ignore
