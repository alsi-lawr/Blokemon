namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.PokemonPowers

/// Continuous Pokemon Powers are re-derived before every command, so a modifier that stopped
/// applying stops mattering without anyone having to remember to remove it.
module internal MatchContinuous =

    let refreshContinuousEffects
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        =
        for source in builder.Cards |> Seq.sortBy (fun card -> card.Id) |> Seq.toArray do
            for trick in
                effectivePartyTricks catalog builder source
                |> Seq.filter (fun trick -> trick.Trigger = BlokemonTrigger.Continuous)
                |> Seq.toArray do
                let effect = EffectId trick.MechanicalId
                builder.RemoveEffects(effect, source.Id)

                if PokemonPowers.pokemonPowerIsEnabled catalog builder source then
                    interpreter.Execute(
                        builder,
                        source.Owner,
                        source,
                        effect,
                        trick.Program,
                        ImmutableArray<_>.Empty,
                        false
                    )
                    |> ignore
