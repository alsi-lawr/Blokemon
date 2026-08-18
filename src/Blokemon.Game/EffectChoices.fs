namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

type DamageAllocation = { Card: CardInstanceId; Counters: int }

type VimAttachment =
    { Vim: CardInstanceId
      Bloke: CardInstanceId }

type CardMechanicalTypes =
    { Card: CardInstanceId
      Types: ImmutableArray<BlokemonMechanicalType> }

/// What a player answered when an effect asked them something. The abstract Id the C# hierarchy
/// declared is a derived member over the match.
[<RequireQualifiedAccess>]
type EffectChoice =
    | Optional of optionalId: EffectChoiceId * isAccepted: bool
    | Amount of amountId: EffectChoiceId * amount: int
    | Cards of cardsId: EffectChoiceId * cards: ImmutableArray<CardInstanceId>
    | MechanicalType of typeId: EffectChoiceId * mechanicalType: BlokemonMechanicalType
    | Attack of attackId: EffectChoiceId * attack: EffectId
    | Distribution of distributionId: EffectChoiceId * allocations: ImmutableArray<DamageAllocation>
    | Attachments of attachmentsId: EffectChoiceId * placements: ImmutableArray<VimAttachment>

    member this.Id =
        match this with
        | Optional(id, _)
        | Amount(id, _)
        | Cards(id, _)
        | MechanicalType(id, _)
        | Attack(id, _)
        | Distribution(id, _)
        | Attachments(id, _) -> id

[<RequireQualifiedAccess>]
module EffectChoice =

    /// The C# hierarchy let callers ask "is this the Cards case, and if so which cards"; these
    /// keep those reads to one line at the call sites that used `OfType<…>().SingleOrDefault(…)`.
    let optional (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Optional(choiceId, accepted) when choiceId = id -> ValueSome accepted
        | _ -> ValueNone

    let cards (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Cards(choiceId, values) when choiceId = id -> ValueSome values
        | _ -> ValueNone

    let mechanicalType (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.MechanicalType(choiceId, value) when choiceId = id -> ValueSome value
        | _ -> ValueNone

    let attack (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Attack(choiceId, value) when choiceId = id -> ValueSome value
        | _ -> ValueNone

    let distribution (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Distribution(choiceId, values) when choiceId = id -> ValueSome values
        | _ -> ValueNone

    let attachments (id: EffectChoiceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Attachments(choiceId, values) when choiceId = id -> ValueSome values
        | _ -> ValueNone
