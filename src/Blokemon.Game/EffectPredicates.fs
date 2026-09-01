namespace Blokemon.Game

open Blokemon.Core.SetDesign

/// The predicates present in the reviewed 1999 programs, read from the live staging area.
module internal EffectPredicates =

    let evaluatePredicate
        (_: AuthorityCatalog)
        (runtime: EffectRuntime)
        (predicate: BlokemonEffectPredicate)
        (_: string)
        =
        match predicate.Condition with
        | BlokemonCondition.SelfIsInBooth ->
            (runtime.Builder.Card runtime.Source.Id).Zone = CardZone.Booth
        | BlokemonCondition.TargetHasDamage ->
            runtime.LastSelectedCards
            |> Seq.exists (fun id -> (runtime.Builder.Card id).Damage > 0)
        | unsupported -> invalidOp $"Unsupported condition {int unsupported}."
