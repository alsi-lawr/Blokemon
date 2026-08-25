namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectRegistration

/// Attaching, moving and chucking vim - the three opcodes that rearrange what is stuck to a bloke.
module internal EffectVimOperations =

    let executeAttachVim
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        if instruction.Selection = BlokemonSelection.AnyDistribution then
            match runtime.AttachmentsChoice(choiceId runtime.Effect path "attachments") with
            | ValueSome placements ->
                for placement in placements do
                    runtime.Builder.Attach(placement.Vim, placement.Bloke)
            | ValueNone ->
                let eligibleVim =
                    resolveCandidates
                        catalog
                        runtime.Builder
                        runtime.Actor
                        runtime.Source
                        instruction
                        runtime.TriggerContext
                    |> Seq.exists (fun card -> card.Kind = CardKind.Vim)

                let eligibleTarget =
                    instruction.Targets
                    |> Array.exists (fun target ->
                        resolveTarget
                            catalog
                            runtime.Builder
                            runtime.Actor
                            runtime.Source
                            target
                            runtime.TriggerContext
                        |> Seq.exists isInPlay)

                if eligibleVim && eligibleTarget then
                    runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired
        else

            let fromSources =
                if hasDeclaredSources instruction then
                    resolveSelectedTargets
                        catalog
                        runtime
                        { instruction with
                            Targets =
                                (match instruction.Sources with
                                 | null -> Array.empty
                                 | sources -> sources)
                            Sources = null }
                        path
                    |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                    |> Seq.toArray
                else
                    runtime.LastSelectedCards
                    |> Seq.map runtime.Builder.Card
                    |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                    |> Seq.toArray

            let selected =
                if fromSources.Length = 0 then
                    resolveSelectedTargets catalog runtime instruction path
                    |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                    |> Seq.toArray
                else
                    fromSources

            let targets =
                if instruction.Targets.Length > 0 then
                    instruction.Targets
                    |> Seq.collect (fun target ->
                        resolveTarget
                            catalog
                            runtime.Builder
                            runtime.Actor
                            runtime.Source
                            target
                            runtime.TriggerContext)
                    |> Seq.filter isInPlay
                    |> Seq.toArray
                else
                    [| runtime.Builder.Card runtime.Source.Id |]

            if targets.Length > 0 then
                for index in 0 .. min instruction.Amount selected.Length - 1 do
                    runtime.Builder.Attach(selected[index].Id, targets[index % targets.Length].Id)

    let executeMoveVim
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let selected =
            resolveSelectedTargets catalog runtime instruction path
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
            |> Seq.truncate instruction.Amount
            |> Seq.toArray

        for vim in selected do
            let prevented =
                match vim.AttachedTo with
                | ValueSome attachedTo ->
                    effectIsPrevented runtime (runtime.Builder.Card attachedTo)
                | ValueNone -> false

            if not prevented then
                runtime.Builder.DetachTo(vim.Id, CardZone.Mitt)
                runtime.Builder.Attach(vim.Id, runtime.Source.Id)

    let executeChuckVim
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        let selected =
            resolveSelectedTargets catalog runtime instruction path
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
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
