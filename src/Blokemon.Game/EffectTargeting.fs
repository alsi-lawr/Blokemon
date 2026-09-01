namespace Blokemon.Game

open System
open System.Linq
open Blokemon.Core.SetDesign

/// Which cards an instruction is allowed to touch, before any choice narrows the set.
module internal EffectTargeting =

    let isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    let inPlay (builder: MatchBuilder) (player: PlayerId) =
        builder.Cards
        |> Seq.filter (fun card ->
            card.Owner = player && (card.Zone = CardZone.Oche || card.Zone = CardZone.Booth))

    let attachedVim (builder: MatchBuilder) (card: CardInstanceId) =
        (builder.Card card).Attachments
        |> Seq.map builder.Card
        |> Seq.filter (fun attached -> attached.Kind = CardKind.Vim)

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
        | BlokemonOpcode.AttachVim
        | BlokemonOpcode.MoveVim
        | BlokemonOpcode.ChuckVim -> true
        | _ -> false

    let filterCards
        (catalog: AuthorityCatalog)
        (cards: CardState seq)
        (instruction: BlokemonEffectInstruction)
        =
        cards
        |> Seq.filter (fun card ->
            (instruction.RelatedIds.Length = 0
             || instruction.RelatedIds.Contains(card.MechanicalId.Value, StringComparer.Ordinal))
            && (instruction.MechanicalTypes.Length = 0
                || not (filtersCandidatesByMechanicalType instruction.Opcode)
                || (if card.Kind = CardKind.Vim then
                        Array.contains
                            (catalog.Vim card.MechanicalId).MechanicalType
                            instruction.MechanicalTypes
                    else
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
        (triggerContext: TriggerContext voption)
        : CardState seq =
        match target with
        | BlokemonTarget.Self -> Seq.singleton source
        | BlokemonTarget.OwnOche -> yieldCard (builder.Oche actor)
        | BlokemonTarget.OwnBoothChosen -> builder.CardsIn(actor, CardZone.Booth)
        | BlokemonTarget.OwnBlokeChosen -> inPlay builder actor
        | BlokemonTarget.OwnBlokesAll -> inPlay builder actor
        | BlokemonTarget.OtherOche -> yieldCard (builder.Oche(builder.Other actor))
        | BlokemonTarget.OtherBoothChosen -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonTarget.OtherBoothAll -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonTarget.OtherBlokeChosen -> inPlay builder (builder.Other actor)
        | BlokemonTarget.OtherBlokesAll -> inPlay builder (builder.Other actor)
        | BlokemonTarget.OwnMitt -> builder.CardsIn(actor, CardZone.Mitt)
        | BlokemonTarget.OtherMitt -> builder.CardsIn(builder.Other actor, CardZone.Mitt)
        | BlokemonTarget.OwnStack -> builder.CardsIn(actor, CardZone.Stack)
        | BlokemonTarget.OtherStack -> builder.CardsIn(builder.Other actor, CardZone.Stack)
        | BlokemonTarget.OwnEmptiesTray -> builder.CardsIn(actor, CardZone.EmptiesTray)
        | BlokemonTarget.OtherEmptiesTray ->
            builder.CardsIn(builder.Other actor, CardZone.EmptiesTray)
        | BlokemonTarget.OwnAttachedBarKits ->
            inPlay builder actor
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Kit)
        | BlokemonTarget.OwnOcheAttachedVim ->
            yieldCard (builder.Oche actor)
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        | BlokemonTarget.OtherOcheAttachedVim ->
            yieldCard (builder.Oche(builder.Other actor))
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        | BlokemonTarget.KnockedOutBlokeAttachedVim ->
            match triggerContext with
            | ValueSome context when context.KnockedOutBloke.IsSome ->
                (builder.Card context.KnockedOutBloke.Value).Attachments
                |> Seq.map builder.Card
                |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
            | _ -> Seq.empty
        | BlokemonTarget.AttackingBloke ->
            match triggerContext with
            | ValueSome context when context.AttackingBloke.IsSome ->
                Seq.singleton (builder.Card context.AttackingBloke.Value)
            | _ -> Seq.empty
        | BlokemonTarget.BarChits -> builder.CardsIn(actor, CardZone.BarChit)
        | _ -> builder.Cards |> Seq.filter (fun card -> card.Zone = CardZone.Local)

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
        | BlokemonOpcode.SendHome
        | BlokemonOpcode.CopyAttack
        | BlokemonOpcode.Demote -> otherOche ()
        | BlokemonOpcode.DealBoothDamage -> builder.CardsIn(builder.Other actor, CardZone.Booth)
        | BlokemonOpcode.PlaceDamageCounters -> inPlay builder (builder.Other actor)
        | BlokemonOpcode.DealSelfDamage
        | BlokemonOpcode.HealDamage
        | BlokemonOpcode.ClearRoughState
        | BlokemonOpcode.PreventDamage
        | BlokemonOpcode.PreventEffects
        | BlokemonOpcode.ReduceDamage
        | BlokemonOpcode.ModifyAttackCost
        | BlokemonOpcode.ModifyTaxiFare
        | BlokemonOpcode.RestrictAttack
        | BlokemonOpcode.RestrictTaxi
        | BlokemonOpcode.RestrictKit
        | BlokemonOpcode.RestrictLocal
        | BlokemonOpcode.RestrictEmptiesRecovery
        | BlokemonOpcode.ReflectAttackDamage
        | BlokemonOpcode.RecoverFromSendHome
        | BlokemonOpcode.PlayAsBloke
        | BlokemonOpcode.ChuckSelf
        | BlokemonOpcode.TriggeredPartyTrick
        | BlokemonOpcode.ContinuousPartyTrick
        | BlokemonOpcode.OncePerRound
        | BlokemonOpcode.EndRoundEffect -> self ()
        | BlokemonOpcode.DrawFromStack
        | BlokemonOpcode.ShuffleStack -> builder.CardsIn(actor, CardZone.Stack)
        | BlokemonOpcode.SearchStack
        | BlokemonOpcode.TransformFromStack ->
            filterCards catalog (builder.CardsIn(actor, CardZone.Stack)) instruction
        | BlokemonOpcode.ChuckCards ->
            inPlay builder actor
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Kit)
        | BlokemonOpcode.AttachVim ->
            builder.CardsIn(actor, CardZone.Mitt)
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        | BlokemonOpcode.MoveVim ->
            inPlay builder actor
            |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        | BlokemonOpcode.ChuckVim ->
            source.Attachments
            |> Seq.map builder.Card
            |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        | BlokemonOpcode.SwapOche ->
            builder.CardsIn(builder.Other actor, CardZone.Booth)
            |> Seq.filter (fun card -> card.Kind = CardKind.Bloke)
        | _ -> Seq.empty

    let resolveCandidates
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (instruction: BlokemonEffectInstruction)
        (triggerContext: TriggerContext voption)
        =
        let declared = declaredSources instruction

        let candidates =
            if declared.Length = 0 then
                resolveImplicitCandidates catalog builder actor source instruction
            else
                declared
                |> Seq.collect (fun target ->
                    resolveTarget catalog builder actor source target triggerContext)

        let candidates =
            if
                instruction.Opcode = BlokemonOpcode.ChuckVim
                && not (hasDeclaredSources instruction)
            then
                candidates
                |> Seq.filter (fun card -> card.Kind = CardKind.Bloke)
                |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
                |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
            else
                candidates

        let candidates =
            if instruction.SourceTopCount > 0 then
                candidates |> Seq.truncate instruction.SourceTopCount
            else
                candidates

        filterCards catalog candidates instruction
