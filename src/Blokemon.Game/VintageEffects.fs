namespace Blokemon.Game

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectTargeting
open Blokemon.Game.PokemonPowers
open Blokemon.Game.EffectRegistration

module internal VintageEffects =

    let private addEffect
        (runtime: EffectRuntime)
        (kind: TemporaryEffectKind)
        (source: CardInstanceId)
        (target: CardInstanceId voption)
        (amount: int)
        (types: BlokemonMechanicalType seq)
        (related: string seq)
        (duration: EffectDuration)
        (applies: int)
        (expires: int)
        =
        runtime.Builder.AddEffect
            { SourceEffect = runtime.Effect
              SourceCard = source
              Owner = runtime.Actor
              TargetCard = target
              Kind = kind
              Amount = amount
              MechanicalTypes = ImmutableArray.CreateRange types
              RoughStates = ImmutableArray<_>.Empty
              RelatedCards = ImmutableArray.CreateRange(related |> Seq.map MechanicalCardId)
              Conditions = ImmutableArray<_>.Empty
              Duration = duration
              AppliesFromRound = applies
              ExpiresAfterRound = expires }

    let private chosenCards (runtime: EffectRuntime) path suffix =
        runtime.CardsChoice(choiceId runtime.Effect path suffix)
        |> ValueOption.defaultValue ImmutableArray<_>.Empty

    let private chosenCard runtime path suffix =
        chosenCards runtime path suffix |> Seq.tryHead

    let private chosenType (runtime: EffectRuntime) path =
        runtime.TypeChoice(choiceId runtime.Effect path "type")

    let private attachedEnergy catalog (runtime: EffectRuntime) (pokemon: CardState) =
        pokemon.Attachments
        |> Seq.map runtime.Builder.Card
        |> Seq.filter (fun card ->
            effectiveEnergy catalog runtime.Builder pokemon card |> Seq.isEmpty |> not)

    let private transferDamage catalog (runtime: EffectRuntime) path mayKnockOut =
        match chosenCard runtime path "from", chosenCard runtime path "to" with
        | Some fromId, Some toId when fromId <> toId ->
            let fromCard = runtime.Builder.Card fromId
            let toCard = runtime.Builder.Card toId

            if
                fromCard.Damage >= 10
                && (mayKnockOut
                    || toCard.Damage + 10 < effectiveStayingPower catalog runtime.Builder toCard)
            then
                runtime.Builder.Heal(runtime.Actor, fromId, 10, ValueSome runtime.Source.Id)

                runtime.Builder.PlaceDamage(
                    runtime.Actor,
                    toId,
                    10,
                    DamageKind.PlacedCounter,
                    ValueSome runtime.Source.Id
                )
            else
                runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable
        | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice

    let private changeTopOrder (runtime: EffectRuntime) instruction path =
        let selected =
            chosenCards runtime path "cards" |> Seq.map runtime.Builder.Card |> Seq.toArray

        if selected.Length > 0 then
            let owner = selected[0].Owner

            let allowed =
                runtime.Builder.CardsIn(owner, CardZone.Stack)
                |> Seq.truncate instruction.Amount
                |> Seq.toArray

            if
                selected |> Array.exists (fun card -> card.Owner <> owner)
                || selected
                   |> Array.exists (fun card ->
                       allowed |> Array.exists (fun candidate -> candidate.Id = card.Id) |> not)
            then
                runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice
            else
                let positions = selected |> Array.map _.StackPosition |> Array.sort

                for index in 0 .. selected.Length - 1 do
                    runtime.Builder.SetCard
                        { runtime.Builder.Card selected[index].Id with
                            StackPosition = positions[index] }

    let private resetMovedCard (builder: MatchBuilder) cardId =
        builder.SetCard
            { builder.Card cardId with
                AttachedTo = ValueNone
                Attachments = ImmutableArray<_>.Empty
                UnderlyingCards = ImmutableArray<_>.Empty
                Damage = 0
                RoughStates = ImmutableArray<_>.Empty }

    let private returnWholePokemonToHand (runtime: EffectRuntime) (pokemon: CardState) =
        for cardId in
            Seq.concat
                [ pokemon.UnderlyingCards :> seq<CardInstanceId>
                  pokemon.Attachments :> seq<CardInstanceId>
                  Seq.singleton pokemon.Id ]
            |> Seq.distinct do
            runtime.Builder.RemoveEffectsFor cardId
            runtime.Builder.MoveCard(cardId, CardZone.Mitt)
            resetMovedCard runtime.Builder cardId

    let private scoopUp (runtime: EffectRuntime) (pokemon: CardState) =
        let basicId =
            if pokemon.UnderlyingCards.IsEmpty then
                pokemon.Id
            else
                pokemon.UnderlyingCards[0]

        let discarded =
            Seq.concat
                [ pokemon.UnderlyingCards :> seq<CardInstanceId>
                  pokemon.Attachments :> seq<CardInstanceId>
                  Seq.singleton pokemon.Id ]
            |> Seq.distinct
            |> Seq.filter (fun card -> card <> basicId)
            |> Seq.toArray

        for cardId in discarded do
            runtime.Builder.RemoveEffectsFor cardId
            runtime.Builder.MoveCard(cardId, CardZone.EmptiesTray)
            resetMovedCard runtime.Builder cardId

        runtime.Builder.RemoveEffectsFor basicId
        runtime.Builder.MoveCard(basicId, CardZone.Mitt)
        resetMovedCard runtime.Builder basicId

    let private devolveTo (runtime: EffectRuntime) targetId destination =
        let target = runtime.Builder.Card targetId

        if target.UnderlyingCards.Length = 0 then
            targetId
        else
            let underlyingId = target.UnderlyingCards[target.UnderlyingCards.Length - 1]
            let underlying = runtime.Builder.Card underlyingId
            runtime.Builder.ClearAndRetargetPokemonEffects(target.Id, underlyingId)
            runtime.Builder.MoveCard(target.Id, destination)
            resetMovedCard runtime.Builder target.Id

            runtime.Builder.SetCard
                { underlying with
                    Zone = target.Zone
                    StackPosition = target.StackPosition
                    Damage = target.Damage
                    Attachments = target.Attachments
                    UnderlyingCards =
                        ImmutableArray.CreateRange(
                            target.UnderlyingCards |> Seq.take (target.UnderlyingCards.Length - 1)
                        )
                    AttachedTo = ValueNone
                    RoughStates = ImmutableArray<_>.Empty }

            for attachment in target.Attachments do
                runtime.Builder.SetCard
                    { runtime.Builder.Card attachment with
                        AttachedTo = ValueSome underlyingId }

            underlyingId

    let private attachTrainer (runtime: EffectRuntime) target modifier expires =
        runtime.Builder.Attach(runtime.Source.Id, target)

        addEffect
            runtime
            TemporaryEffectKind.AttachedTrainer
            runtime.Source.Id
            (ValueSome target)
            modifier
            Seq.empty
            Seq.empty
            EffectDuration.UntilEndOfOpponentsNextRound
            runtime.Builder.RoundNumber
            expires

    let execute
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        : bool voption =
        let builder = runtime.Builder

        match instruction.Opcode with
        | BlokemonOpcode.EnergyTrans ->
            match runtime.AttachmentsChoice(choiceId runtime.Effect path "attachments") with
            | ValueSome placements when placements.Length = 1 ->
                let placement = placements[0]
                let energy = builder.Card placement.Vim

                match energy.AttachedTo with
                | ValueSome previous when previous <> placement.Bloke ->
                    builder.DetachTo(energy.Id, CardZone.Mitt)
                    builder.Attach(energy.Id, placement.Bloke)
                | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.EnergyBurn ->
            addEffect
                runtime
                TemporaryEffectKind.EnergyBurn
                runtime.Source.Id
                (ValueSome runtime.Source.Id)
                0
                Seq.empty
                Seq.empty
                EffectDuration.UntilEndOfRound
                builder.RoundNumber
                builder.RoundNumber

            ValueSome true
        | BlokemonOpcode.RainDance ->
            match runtime.AttachmentsChoice(choiceId runtime.Effect path "attachments") with
            | ValueSome placements when placements.Length = 1 ->
                builder.Attach(placements[0].Vim, placements[0].Bloke)
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.Shift ->
            match chosenType runtime path with
            | ValueSome selected ->
                builder.RemoveEffects(runtime.Effect, runtime.Source.Id)

                addEffect
                    runtime
                    TemporaryEffectKind.ChangeType
                    runtime.Source.Id
                    (ValueSome runtime.Source.Id)
                    0
                    [ selected ]
                    Seq.empty
                    EffectDuration.WhileSourceInPlay
                    builder.RoundNumber
                    Int32.MaxValue
            | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.Peek ->
            match chosenCard runtime path "cards" with
            | Some selected when selected = runtime.Source.Id ->
                let opponentHand =
                    builder.CardsIn(builder.Other runtime.Actor, CardZone.Mitt) |> Seq.toArray

                if opponentHand.Length > 0 then
                    let revealed = opponentHand[builder.Random.NextInt opponentHand.Length]

                    builder.Events.Add
                        { PendingMatchEvent.forCards
                              MatchEventKind.CardsRevealed
                              runtime.Actor
                              runtime.Source.Id
                              (ImmutableArray.Create revealed.Id) with
                            Effect = ValueSome runtime.Effect }
            | Some selected ->
                builder.Events.Add
                    { PendingMatchEvent.forCards
                          MatchEventKind.CardsRevealed
                          runtime.Actor
                          runtime.Source.Id
                          (ImmutableArray.Create selected) with
                        Effect = ValueSome runtime.Effect }
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.DamageSwap
        | BlokemonOpcode.StrangeBehavior ->
            transferDamage catalog runtime path false
            ValueSome true
        | BlokemonOpcode.Curse ->
            transferDamage catalog runtime path true
            ValueSome true
        | BlokemonOpcode.Cowardice ->
            let source = runtime.Builder.Card runtime.Source.Id

            if source.EnteredAtOwnerRound < (builder.Player runtime.Actor).RoundsStarted then
                for attachment in source.Attachments do
                    builder.DetachTo(attachment, CardZone.EmptiesTray)

                builder.MoveCard(source.Id, CardZone.Mitt)
                resetMovedCard builder source.Id
                runtime.SourceLeftPlay <- true
            else
                runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable

            ValueSome true
        | BlokemonOpcode.Buzzap ->
            match chosenCard runtime path "cards", chosenType runtime path with
            | Some target, ValueSome energyType ->
                for attachment in runtime.Source.Attachments do
                    builder.DetachTo(attachment, CardZone.EmptiesTray)

                for underlying in runtime.Source.UnderlyingCards do
                    builder.MoveCard(underlying, CardZone.EmptiesTray)
                    resetMovedCard builder underlying

                builder.SetCard
                    { builder.Card runtime.Source.Id with
                        Damage = 0
                        RoughStates = ImmutableArray<_>.Empty
                        Attachments = ImmutableArray<_>.Empty
                        UnderlyingCards = ImmutableArray<_>.Empty }

                builder.Attach(runtime.Source.Id, target)

                addEffect
                    runtime
                    TemporaryEffectKind.BuzzapEnergy
                    runtime.Source.Id
                    (ValueSome runtime.Source.Id)
                    2
                    [ energyType ]
                    Seq.empty
                    EffectDuration.WhileSourceInPlay
                    builder.RoundNumber
                    Int32.MaxValue
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.ToxicGas
        | BlokemonOpcode.InvisibleWall
        | BlokemonOpcode.Transform
        | BlokemonOpcode.KabutoArmor
        | BlokemonOpcode.PrehistoricPower
        | BlokemonOpcode.ThickSkinned -> ValueSome true
        | BlokemonOpcode.Clairvoyance ->
            addEffect
                runtime
                TemporaryEffectKind.RevealHand
                runtime.Source.Id
                ValueNone
                0
                Seq.empty
                Seq.empty
                EffectDuration.WhileSourceInPlay
                builder.RoundNumber
                Int32.MaxValue

            ValueSome true
        | BlokemonOpcode.ChangeResistance ->
            match chosenType runtime path with
            | ValueSome selected ->
                addEffect
                    runtime
                    TemporaryEffectKind.ChangeResistance
                    runtime.Source.Id
                    (ValueSome runtime.Source.Id)
                    0
                    [ selected ]
                    Seq.empty
                    EffectDuration.WhileTargetInPlay
                    builder.RoundNumber
                    Int32.MaxValue
            | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.Devolve ->
            match chosenCard runtime path "cards" with
            | Some target when not (effectIsPrevented runtime (builder.Card target)) ->
                devolveTo runtime target CardZone.Mitt |> ignore
            | Some _ -> ()
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.HalfRemainingHpDamage ->
            match builder.Oche(builder.Other runtime.Actor) with
            | ValueSome target ->
                let remaining = max 0 (effectiveStayingPower catalog builder target - target.Damage)

                addPendingDamage
                    catalog
                    runtime
                    instruction
                    path
                    (((remaining + 19) / 20) * 10)
                    DamageKind.Attack
            | ValueNone -> ()

            ValueSome true
        | BlokemonOpcode.BoostNextAttack ->
            addEffect
                runtime
                TemporaryEffectKind.ScaleNextAttackDamage
                runtime.Source.Id
                (ValueSome runtime.Source.Id)
                instruction.Amount
                Seq.empty
                instruction.RelatedIds
                EffectDuration.WhileSourceInPlay
                (builder.RoundNumber + 2)
                (builder.RoundNumber + 2)

            ValueSome true
        | BlokemonOpcode.DestinyBond ->
            addEffect
                runtime
                TemporaryEffectKind.DestinyBond
                runtime.Source.Id
                (ValueSome runtime.Source.Id)
                0
                Seq.empty
                Seq.empty
                EffectDuration.UntilEndOfOpponentsNextRound
                builder.RoundNumber
                (builder.RoundNumber + 1)

            ValueSome true
        | BlokemonOpcode.PreventDamageUpTo ->
            addEffect
                runtime
                TemporaryEffectKind.PreventDamageUpTo
                runtime.Source.Id
                (ValueSome runtime.Source.Id)
                instruction.Amount
                Seq.empty
                Seq.empty
                EffectDuration.UntilEndOfOpponentsNextRound
                builder.RoundNumber
                (builder.RoundNumber + 1)

            ValueSome true
        | BlokemonOpcode.FlipAttachedEnergy ->
            let mutable heads = 0

            for _ in attachedEnergy catalog runtime runtime.Source do
                let result = runtime.NextBeerMat()
                runtime.RecordBeerMatEvent result

                if result then
                    heads <- heads + 1

            addPendingDamage
                catalog
                runtime
                instruction
                path
                (heads * instruction.Amount)
                DamageKind.Attack

            ValueSome true
        | BlokemonOpcode.MirrorMove ->
            match
                builder.Effects
                |> Seq.tryFindBack (fun effect ->
                    effect.TargetCard = ValueSome runtime.Source.Id
                    && effect.Kind = TemporaryEffectKind.MirrorMoveMemory)
            with
            | Some remembered ->
                addPendingDamage
                    catalog
                    runtime
                    instruction
                    path
                    remembered.Amount
                    DamageKind.Attack

                for state in remembered.RoughStates do
                    match builder.Oche(builder.Other runtime.Actor) with
                    | ValueSome target ->
                        builder.ApplyRoughState(
                            runtime.Actor,
                            target.Id,
                            state,
                            ValueSome runtime.Source.Id
                        )
                    | ValueNone -> ()
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable

            ValueSome true
        | BlokemonOpcode.ReduceDamageFromDefender ->
            match builder.Oche(builder.Other runtime.Actor) with
            | ValueSome defender ->
                addEffect
                    runtime
                    TemporaryEffectKind.ReduceDamageFromAttacker
                    defender.Id
                    (ValueSome runtime.Source.Id)
                    instruction.Amount
                    Seq.empty
                    Seq.empty
                    EffectDuration.UntilEndOfOpponentsNextRound
                    builder.RoundNumber
                    (builder.RoundNumber + 1)
            | ValueNone -> ()

            ValueSome true
        | BlokemonOpcode.RearrangeTopDeck ->
            changeTopOrder runtime instruction path
            ValueSome true
        | BlokemonOpcode.HealFromDamage
        | BlokemonOpcode.ReturnDefenderToHand ->
            runtime.PostDamageInstructions.Add instruction
            ValueSome true
        | BlokemonOpcode.RequireDefenderCondition ->
            match builder.Oche(builder.Other runtime.Actor) with
            | ValueSome defender when
                instruction.RoughStates
                |> Array.exists (fun state ->
                    defender.RoughStates |> Seq.exists (fun entry -> entry.State = state))
                ->
                ()
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable

            ValueSome true
        | BlokemonOpcode.Wildfire ->
            let selected = chosenCards runtime path "cards"

            for energy in selected do
                builder.DetachTo(energy, CardZone.EmptiesTray)

            for card in
                builder.CardsIn(builder.Other runtime.Actor, CardZone.Stack)
                |> Seq.truncate selected.Length
                |> Seq.toArray do
                builder.MoveCard(card.Id, CardZone.EmptiesTray)

            ValueSome true
        | BlokemonOpcode.DevolutionSpray ->
            match chosenCard runtime path "cards" with
            | Some chosenStage ->
                let host =
                    inPlay builder runtime.Actor
                    |> Seq.filter catalog.CountsAsPokemon
                    |> Seq.tryFind (fun card ->
                        card.Id = chosenStage || card.UnderlyingCards |> Seq.contains chosenStage)

                let mutable current = host |> Option.map _.Id |> Option.defaultValue chosenStage
                let mutable removedChosenStage = false

                while not removedChosenStage && (builder.Card current).UnderlyingCards.Length > 0 do
                    let discarded = current
                    current <- devolveTo runtime current CardZone.EmptiesTray
                    removedChosenStage <- discarded = chosenStage

                if not removedChosenStage then
                    runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice

                builder.ClearRoughStates(runtime.Actor, current)
                builder.RemoveEffectsFor current

                builder.SetCard
                    { builder.Card current with
                        LastPromotedRound = -1 }
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.PokemonBreeder ->
            match chosenCard runtime path "evolution", chosenCard runtime path "basic" with
            | Some evolutionId, Some basicId ->
                let evolution = builder.Card evolutionId
                let basic = builder.Card basicId
                let evolutionDefinition = catalog.Bloke evolution.MechanicalId
                let player = builder.Player runtime.Actor

                let directStageOne =
                    catalog.Manifest.Collectibles
                    |> Array.tryFind (fun candidate ->
                        candidate.Rank = BlokemonRank.Seasoned
                        && candidate.PromotesFromId = basic.MechanicalId.Value
                        && evolutionDefinition.PromotesFromId = candidate.Id)

                if
                    evolutionDefinition.Rank <> BlokemonRank.Landlord
                    || directStageOne.IsNone
                    || player.RoundsStarted <= 1
                    || basic.EnteredAtOwnerRound = player.RoundsStarted
                then
                    runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice
                else
                    builder.SetCard
                        { basic with
                            Zone = CardZone.Attached
                            AttachedTo = ValueSome evolution.Id
                            Attachments = ImmutableArray<_>.Empty }

                    builder.SetCard
                        { evolution with
                            Zone = basic.Zone
                            StackPosition = basic.StackPosition
                            Damage = basic.Damage
                            Attachments = basic.Attachments
                            UnderlyingCards =
                                ImmutableArray.CreateRange(
                                    Seq.append basic.UnderlyingCards [ basic.Id ]
                                )
                            EnteredAtOwnerRound = basic.EnteredAtOwnerRound
                            LastPromotedRound = builder.RoundNumber }

                    for attachment in basic.Attachments do
                        builder.SetCard
                            { builder.Card attachment with
                                AttachedTo = ValueSome evolution.Id }

                    builder.ClearAndRetargetPokemonEffects(basic.Id, evolution.Id)
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.ScoopUp ->
            match chosenCard runtime path "cards" with
            | Some target -> scoopUp runtime (builder.Card target)
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.AttachDefender ->
            match chosenCard runtime path "cards" with
            | Some target ->
                attachTrainer runtime target 0 (builder.RoundNumber + 1)

                addEffect
                    runtime
                    TemporaryEffectKind.ReduceDamage
                    runtime.Source.Id
                    (ValueSome target)
                    instruction.Amount
                    Seq.empty
                    Seq.empty
                    EffectDuration.UntilEndOfOpponentsNextRound
                    builder.RoundNumber
                    (builder.RoundNumber + 1)
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.AttachPlusPower ->
            match builder.Oche runtime.Actor with
            | ValueSome target ->
                attachTrainer runtime target.Id instruction.Amount builder.RoundNumber
            | ValueNone -> runtime.Rejection <- ValueSome CommandRejectionCode.EffectUnavailable

            ValueSome true
        | BlokemonOpcode.PokemonCenter ->
            for pokemon in
                inPlay builder runtime.Actor
                |> Seq.filter (fun card -> card.Damage > 0)
                |> Seq.toArray do
                for energy in attachedEnergy catalog runtime pokemon |> Seq.toArray do
                    builder.DetachTo(energy.Id, CardZone.EmptiesTray)

                builder.Heal(runtime.Actor, pokemon.Id, pokemon.Damage, ValueSome runtime.Source.Id)

            ValueSome true
        | BlokemonOpcode.Revive ->
            match chosenCard runtime path "cards" with
            | Some target ->
                let card = builder.Card target
                let maximumHp = catalog.StayingPower card
                let remainingHp = ((maximumHp + 19) / 20) * 10
                builder.MoveCard(target, CardZone.Booth)

                builder.SetCard
                    { builder.Card target with
                        Damage = maximumHp - remainingHp
                        EnteredAtOwnerRound = (builder.Player runtime.Actor).RoundsStarted }
            | None -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | BlokemonOpcode.SuperPotion ->
            match
                chosenCard runtime path "cards",
                chosenCard runtime path "energy",
                runtime.AmountChoice(choiceId runtime.Effect path "amount")
            with
            | Some target, Some energy, ValueSome counters when
                (builder.Card energy).AttachedTo = ValueSome target
                && (effectiveEnergy catalog builder (builder.Card target) (builder.Card energy)
                    |> Seq.isEmpty
                    |> not)
                && counters >= 0
                && counters <= min 4 ((builder.Card target).Damage / 10)
                ->
                builder.DetachTo(energy, CardZone.EmptiesTray)
                builder.Heal(runtime.Actor, target, counters * 10, ValueSome runtime.Source.Id)
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.InvalidChoice

            ValueSome true
        | BlokemonOpcode.Potion ->
            match
                chosenCard runtime path "cards",
                runtime.AmountChoice(choiceId runtime.Effect path "amount")
            with
            | Some target, ValueSome counters when
                counters >= 0 && counters <= min 2 ((builder.Card target).Damage / 10)
                ->
                builder.Heal(runtime.Actor, target, counters * 10, ValueSome runtime.Source.Id)
            | _ -> runtime.Rejection <- ValueSome CommandRejectionCode.ChoiceRequired

            ValueSome true
        | _ -> ValueNone

    let resolvePostDamage (catalog: AuthorityCatalog) (runtime: EffectRuntime) =
        for instruction in runtime.PostDamageInstructions do
            match instruction.Opcode with
            | BlokemonOpcode.HealFromDamage ->
                let dealt =
                    runtime.ResolvedAttackDamage.Values |> Seq.tryHead |> Option.defaultValue 0

                let divisor = max 1 instruction.Amount
                let amount = (dealt + divisor * 10 - 1) / (divisor * 10) * 10

                let amount =
                    if instruction.TargetCount > 1 then
                        min instruction.TargetCount amount
                    else
                        amount

                runtime.Builder.Heal(
                    runtime.Actor,
                    runtime.Source.Id,
                    amount,
                    ValueSome runtime.Source.Id
                )
            | BlokemonOpcode.ReturnDefenderToHand ->
                match runtime.Builder.Oche(runtime.Builder.Other runtime.Actor) with
                | ValueSome target when
                    target.Damage < effectiveStayingPower catalog runtime.Builder target
                    && not (effectIsPrevented runtime target)
                    ->
                    returnWholePokemonToHand runtime target
                | _ -> ()
            | _ -> ()
