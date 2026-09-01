namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectRegistration
open Blokemon.Game.PokemonPowers

/// Discarding attached Energy for reviewed attack costs and effects.
module internal EffectVimOperations =

    let private isAttachedEnergy
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (card: CardState)
        =
        match card.AttachedTo with
        | ValueSome host ->
            effectiveEnergy catalog runtime.Builder (runtime.Builder.Card host) card
            |> Seq.isEmpty
            |> not
        | ValueNone -> false

    let executeChuckVim
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let selected =
            resolveSelectedTargets catalog runtime instruction path
            |> Seq.filter (isAttachedEnergy catalog runtime)
            |> Seq.truncate instruction.Amount
            |> Seq.toArray

        if
            hasDeclaredSources instruction
            && instruction.Selection = BlokemonSelection.Chosen
            && selected.Length < instruction.TargetCount
        then
            runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable
        else
            for vim in selected do
                let prevented =
                    match vim.AttachedTo with
                    | ValueSome attachedTo ->
                        effectIsPrevented runtime (runtime.Builder.Card attachedTo)
                    | ValueNone -> false

                if not prevented then
                    runtime.Builder.DetachTo(vim.Id, CardZone.EmptiesTray)
