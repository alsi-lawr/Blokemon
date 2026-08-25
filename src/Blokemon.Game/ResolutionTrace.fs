namespace Blokemon.Game

open Blokemon.Core.SetDesign

type internal ResolutionTraceEntry =
    | AttackStep of BlokemonAttackResolutionStep
    | DamageStep of BlokemonDamageResolutionStep

type internal ResolutionTrace = ResolutionTraceEntry -> unit

[<RequireQualifiedAccess>]
module internal ResolutionTrace =

    let none: ResolutionTrace = ignore
