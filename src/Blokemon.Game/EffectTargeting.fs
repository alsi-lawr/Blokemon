namespace Blokemon.Game

open System
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.PokemonPowers

/// Which cards an instruction is allowed to touch, before any choice narrows the set.
module internal EffectTargeting =

    let isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    let inPlay (builder: MatchBuilder) (player: PlayerId) =
        builder.Cards
        |> Seq.filter (fun card ->
            card.Owner = player && (card.Zone = CardZone.Oche || card.Zone = CardZone.Booth))

    let isEnergyAttachment
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (host: CardState)
        (attached: CardState)
        =
        effectiveEnergy catalog builder host attached |> Seq.isEmpty |> not

    let attachedVim (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardInstanceId) =
        let host = builder.Card card

        host.Attachments
        |> Seq.map builder.Card
        |> Seq.filter (isEnergyAttachment catalog builder host)

    let choiceId (effect: EffectId) (path: string) (kind: string) =
        EffectChoiceId $"{effect.Value}:{path}:{kind}"

    let parentPath (path: string) = path[.. path.LastIndexOf '/' - 1]

    let private yieldCard (card: CardState voption) =
        match card with
        | ValueSome value -> Seq.singleton value
        | ValueNone -> Seq.empty

    let private declaredSources (instruction: BlokemonEffectInstruction) =
        match instruction.Sources with
        | null -> instruction.Targets
        | sources when sources.Length = 0 -> instruction.Targets
        | sources -> sources

    let hasDeclaredSources (instruction: BlokemonEffectInstruction) =
        match instruction.Sources with
        | null -> false
        | sources -> sources.Length > 0

    let matchesCardFilter
        (catalog: AuthorityCatalog)
        (card: CardState)
        (filter: BlokemonEffectCardFilter | null)
        =
        match filter with
        | null -> true
        | filter ->
            let category =
                match card.Kind with
                | CardKind.Bloke -> BlokemonCardCategory.Bloke
                | CardKind.Vim -> BlokemonCardCategory.Vim
                | _ -> BlokemonCardCategory.Kit

            (filter.Categories.Length = 0 || Array.contains category filter.Categories)
            && (filter.Ranks.Length = 0
                || (card.Kind = CardKind.Bloke
                    && Array.contains (catalog.Bloke card.MechanicalId).Rank filter.Ranks))
            && (not filter.BasicVimOnly
                || (card.Kind = CardKind.Vim && (catalog.Vim card.MechanicalId).IsBasic))
            && not (
                filter.ExcludedRelatedIds.Contains(card.MechanicalId.Value, StringComparer.Ordinal)
            )

    let private filtersCandidatesByMechanicalType (opcode: BlokemonOpcode) =
        match opcode with
        | BlokemonOpcode.SearchStack
        | BlokemonOpcode.ShuffleStack
        | BlokemonOpcode.MoveCards
        | BlokemonOpcode.ChuckCards
        | BlokemonOpcode.ChuckVim -> true
        | _ -> false

    let filterCards
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (cards: CardState seq)
        (instruction: BlokemonEffectInstruction)
        =
        cards
        |> Seq.filter (fun card ->
            (instruction.RelatedIds.Length = 0
             || instruction.RelatedIds.Contains(card.MechanicalId.Value, StringComparer.Ordinal))
            && (instruction.MechanicalTypes.Length = 0
                || not (filtersCandidatesByMechanicalType instruction.Opcode)
                || (match card.AttachedTo with
                    | ValueSome host when
                        effectiveEnergy catalog builder (builder.Card host) card
                        |> Seq.exists (fun value ->
                            Array.contains value instruction.MechanicalTypes)
                        ->
                        true
                    | _ when card.Kind = CardKind.Vim ->
                        Array.contains
                            (catalog.Vim card.MechanicalId).MechanicalType
                            instruction.MechanicalTypes
                    | _ ->
                        card.Kind = CardKind.Bloke
                        && (catalog.Bloke card.MechanicalId).MechanicalTypes
                           |> Array.exists (fun value ->
                               Array.contains value instruction.MechanicalTypes)))
            && (not (
                    instruction.Predicates
                    |> Array.exists (fun predicate ->
                        predicate.Condition = BlokemonCondition.TargetHasDamage)
                )
                || card.Damage > 0)
            && matchesCardFilter catalog card instruction.CardFilter)

    let resolveTarget
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (target: BlokemonTarget)
        : CardState seq =
        match target with
        | BlokemonTarget.Self -> Seq.singleton source
        | BlokemonTarget.OwnOche -> yieldCard (builder.Oche actor)
        | BlokemonTarget.OwnBoothChosen -> builder.CardsIn(actor, CardZone.Booth)
        | BlokemonTarget.OwnBlokeChosen -> inPlay builder actor
        | BlokemonTarget.OtherOche -> yieldCard (builder.Oche(builder.Other actor))
        | BlokemonTarget.OtherBoothChosen -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonTarget.OtherBoothAll -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonTarget.OtherBlokeChosen -> inPlay builder (builder.Other actor)
        | BlokemonTarget.OwnMitt -> builder.CardsIn(actor, CardZone.Mitt)
        | BlokemonTarget.OtherMitt -> builder.CardsIn(builder.Other actor, CardZone.Mitt)
        | BlokemonTarget.OwnStack -> builder.CardsIn(actor, CardZone.Stack)
        | BlokemonTarget.OtherStack -> builder.CardsIn(builder.Other actor, CardZone.Stack)
        | BlokemonTarget.OwnEmptiesTray -> builder.CardsIn(actor, CardZone.EmptiesTray)
        | BlokemonTarget.OtherEmptiesTray ->
            builder.CardsIn(builder.Other actor, CardZone.EmptiesTray)
        | unsupported -> invalidOp $"Unsupported target {int unsupported}."

    let resolveImplicitCandidates
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (instruction: BlokemonEffectInstruction)
        : CardState seq =
        let otherOche () =
            yieldCard (builder.Oche(builder.Other actor))

        let self () = Seq.singleton source

        match instruction.Opcode with
        | BlokemonOpcode.DealPrintedDamage
        | BlokemonOpcode.AdjustDamage
        | BlokemonOpcode.ScaleDamage
        | BlokemonOpcode.ApplyRoughState
        | BlokemonOpcode.ModifySoftSpot
        | BlokemonOpcode.CopyAttack -> otherOche ()
        | BlokemonOpcode.DealBoothDamage -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonOpcode.DealSelfDamage
        | BlokemonOpcode.HealDamage
        | BlokemonOpcode.ClearRoughState
        | BlokemonOpcode.PreventDamage
        | BlokemonOpcode.PreventEffects
        | BlokemonOpcode.ReduceDamage
        | BlokemonOpcode.ModifyTaxiFare
        | BlokemonOpcode.RestrictAttack
        | BlokemonOpcode.RestrictTaxi
        | BlokemonOpcode.RestrictKit
        | BlokemonOpcode.ReflectAttackDamage
        | BlokemonOpcode.PlayAsBloke
        | BlokemonOpcode.OncePerRound -> self ()
        | BlokemonOpcode.DrawFromStack
        | BlokemonOpcode.ShuffleStack -> builder.CardsIn(actor, CardZone.Stack)
        | BlokemonOpcode.SearchStack ->
            filterCards catalog builder (builder.CardsIn(actor, CardZone.Stack)) instruction
        | BlokemonOpcode.ChuckCards ->
            inPlay builder actor
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Kit)
        | BlokemonOpcode.ChuckVim ->
            source.Attachments
            |> Seq.map builder.Card
            |> Seq.filter (isEnergyAttachment catalog builder source)
        | BlokemonOpcode.SwapOche ->
            builder.CardsIn(builder.Other actor, CardZone.Booth)
            |> Seq.filter catalog.CountsAsPokemon
        | _ -> Seq.empty

    let resolveCandidates
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (instruction: BlokemonEffectInstruction)
        =
        let declared = declaredSources instruction

        let candidates =
            if declared.Length = 0 then
                resolveImplicitCandidates catalog builder actor source instruction
            else
                declared
                |> Seq.collect (fun target -> resolveTarget catalog builder actor source target)

        let candidates =
            if
                instruction.Opcode = BlokemonOpcode.ChuckVim
                && not (hasDeclaredSources instruction)
            then
                candidates
                |> Seq.filter catalog.CountsAsPokemon
                |> Seq.collect (fun host ->
                    host.Attachments
                    |> Seq.map builder.Card
                    |> Seq.filter (isEnergyAttachment catalog builder host))
            else
                candidates

        let candidates =
            if instruction.SourceTopCount > 0 then
                candidates |> Seq.truncate instruction.SourceTopCount
            else
                candidates

        candidates
        |> Seq.filter (fun card ->
            card.Id <> source.Id
            || (card.Zone <> CardZone.Mitt && card.Zone <> CardZone.EmptiesTray))
        |> fun cards -> filterCards catalog builder cards instruction
