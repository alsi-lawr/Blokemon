namespace Blokemon.Game

open System
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectDamage

/// The conditions a conditional instruction asks about, read off the live staging area.
module internal EffectPredicates =

    let private otherOcheHasType (runtime: EffectRuntime) (predicate: BlokemonEffectPredicate) =
        match runtime.Builder.Oche(runtime.Builder.Other runtime.Actor) with
        | ValueNone -> false
        | ValueSome other ->
            if predicate.MechanicalType.HasValue then
                Seq.contains predicate.MechanicalType.Value (runtime.Catalog.MechanicalTypes other)
            else
                true

    let evaluatePredicate
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (predicate: BlokemonEffectPredicate)
        (path: string)
        =
        let builder = runtime.Builder
        let self () = builder.Card runtime.Source.Id

        let otherOche () =
            builder.Oche(builder.Other runtime.Actor)

        match predicate.Condition with
        | BlokemonCondition.Optional ->
            match runtime.OptionalChoice(choiceId runtime.Effect path "optional") with
            | ValueSome accepted -> accepted
            | ValueNone -> false
        | BlokemonCondition.FirstBeerMatIsBlankSide -> runtime.FirstBeerMatIsBlank
        | BlokemonCondition.SelfIsAtOche -> (self ()).Zone = CardZone.Oche
        | BlokemonCondition.SelfIsInBooth -> (self ()).Zone = CardZone.Booth
        | BlokemonCondition.SelfHasDamage -> (self ()).Damage > 0
        | BlokemonCondition.SelfHasVim ->
            attachedVim builder runtime.Source.Id |> Seq.isEmpty |> not
        | BlokemonCondition.SelfHasSpecialVim ->
            attachedVim builder runtime.Source.Id
            |> Seq.exists (fun vim ->
                not (vim.MechanicalId.Value.StartsWith("VIM-", StringComparison.Ordinal)))
        | BlokemonCondition.SelfHasRoughState ->
            predicate.RoughState.HasValue
            && (self ()).RoughStates
               |> Seq.exists (fun entry -> entry.State = predicate.RoughState.Value)
        | BlokemonCondition.OwnMittIsEmpty ->
            builder.CardsIn(runtime.Actor, CardZone.Mitt) |> Seq.isEmpty
        | BlokemonCondition.MittCountsAreEqual ->
            (builder.CardsIn(runtime.Actor, CardZone.Mitt) |> Seq.length) = (builder.CardsIn(
                                                                                 builder.Other
                                                                                     runtime.Actor,
                                                                                 CardZone.Mitt
                                                                             )
                                                                             |> Seq.length)
        | BlokemonCondition.MatePlayedThisRound ->
            builder.RoundUsage.MatesPlayed > 0
            && (match predicate.RelatedId with
                | null -> true
                | relatedId ->
                    builder.RoundUsage.KitsPlayed |> Seq.exists (fun kit -> kit.Value = relatedId))
        | BlokemonCondition.NamedBlokeInPlay ->
            (match predicate.RelatedId with
             | null -> false
             | relatedId ->
                 inPlay builder runtime.Actor
                 |> Seq.exists (fun card -> card.MechanicalId.Value = relatedId))
        | BlokemonCondition.NamedBlokeInBooth ->
            (match predicate.RelatedId with
             | null -> false
             | relatedId ->
                 builder.CardsIn(runtime.Actor, CardZone.Booth)
                 |> Seq.exists (fun card -> card.MechanicalId.Value = relatedId))
        | BlokemonCondition.OtherOcheHasMechanicalType -> otherOcheHasType runtime predicate
        | BlokemonCondition.OtherOcheHasDamage ->
            match otherOche () with
            | ValueSome card -> card.Damage > 0
            | ValueNone -> false
        | BlokemonCondition.OtherOcheHasRoughState ->
            predicate.RoughState.HasValue
            && (match otherOche () with
                | ValueSome other ->
                    other.RoughStates
                    |> Seq.exists (fun entry -> entry.State = predicate.RoughState.Value)
                | ValueNone -> false)
        | BlokemonCondition.OtherOcheIsPromoted ->
            match otherOche () with
            | ValueSome card -> card.UnderlyingCards.Length > 0
            | ValueNone -> false
        | BlokemonCondition.OtherOcheIsBigHitter ->
            match otherOche () with
            | ValueSome other ->
                catalog.Manifest.BaseRules.BigHitters.BlokeIds.Contains(
                    other.MechanicalId.Value,
                    StringComparer.Ordinal
                )
            | ValueNone -> false
        | BlokemonCondition.AttachedVimCountsAreEqual ->
            match builder.Oche runtime.Actor, otherOche () with
            | ValueSome own, ValueSome other ->
                (attachedVim builder own.Id |> Seq.length) = (attachedVim builder other.Id
                                                              |> Seq.length)
            | _ -> false
        | BlokemonCondition.OwnBarChitCountIsGreater ->
            (builder.Player runtime.Actor).BarChitsRemaining > (builder.Player(
                builder.Other runtime.Actor
            ))
                .BarChitsRemaining
        | BlokemonCondition.TargetHasDamage ->
            runtime.LastSelectedCards |> Seq.exists (fun id -> (builder.Card id).Damage > 0)
        | BlokemonCondition.OtherBoothExists ->
            builder.CardsIn(builder.Other runtime.Actor, CardZone.Booth)
            |> Seq.isEmpty
            |> not
        | BlokemonCondition.BoothHasSpace ->
            (builder.CardsIn(runtime.Actor, CardZone.Booth) |> Seq.length) < runtime.Catalog.Manifest.BaseRules.Opening.BoothLimit
        | BlokemonCondition.OwnBlokeSentHomeByOtherAttackDamage ->
            match runtime.TriggerContext with
            | ValueSome context -> context.KnockedOutBloke.IsSome
            | ValueNone -> false
        | BlokemonCondition.OtherSentHomeByThisAttackDamage -> pendingSendsHome catalog runtime
        | BlokemonCondition.OwnersFirstRound -> (builder.Player runtime.Actor).RoundsStarted = 1
        | BlokemonCondition.OpenedSecond -> builder.OpeningPlayer <> runtime.Actor
        | BlokemonCondition.PromotedFromMittThisRound ->
            (self ()).LastPromotedRound = builder.RoundNumber
        | BlokemonCondition.SourceIsRegular ->
            runtime.Source.Kind = CardKind.Bloke
            && (catalog.Bloke runtime.Source.MechanicalId).Rank = BlokemonRank.Regular
        | _ -> false
