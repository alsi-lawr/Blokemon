namespace Blokemon.Game

open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules

/// The stable, boring answer to every requirement a proposed action would raise, so a legal action
/// carries a command that would actually be accepted rather than one that merely looks plausible.
module internal MatchLegalChoices =

    let cpuCommandId (state: MatchState) (key: string) =
        CommandId $"cpu:{state.Revision.Value}:{key}"

    let command (state: MatchState) (actor: PlayerId) (key: string) (action: MatchAction) =
        { Id = cpuCommandId state key
          MatchId = state.Id
          Actor = actor
          ExpectedRevision = state.Revision
          Choices = FrozenList.empty
          Action = action }

    let private stableChoice (requirement: ChoiceRequirement) : EffectChoice seq =
        match requirement.Kind with
        | ChoiceRequirementKind.Optional -> [ EffectChoice.Optional(requirement.Id, true) ]
        | ChoiceRequirementKind.Amount ->
            [ EffectChoice.Amount(requirement.Id, requirement.Minimum) ]
        | ChoiceRequirementKind.Cards ->
            [ EffectChoice.Cards(
                  requirement.Id,
                  FrozenList<CardInstanceId>
                      .Create(requirement.EligibleCards |> Seq.truncate requirement.Minimum)
              ) ]
        | ChoiceRequirementKind.MechanicalType ->
            requirement.EligibleMechanicalTypes
            |> Seq.truncate 1
            |> Seq.map (fun mechanicalType ->
                EffectChoice.MechanicalType(requirement.Id, mechanicalType))
        | ChoiceRequirementKind.Attack ->
            requirement.EligibleEffects
            |> Seq.truncate 1
            |> Seq.map (fun effect -> EffectChoice.Attack(requirement.Id, effect))
        | ChoiceRequirementKind.Distribution ->
            requirement.EligibleCards
            |> Seq.truncate 1
            |> Seq.map (fun card ->
                EffectChoice.Distribution(
                    requirement.Id,
                    FrozenList<DamageAllocation>.Create
                        { Card = card
                          Counters = requirement.Maximum }
                ))
        | ChoiceRequirementKind.Attachments ->
            requirement.EligibleTargets
            |> Seq.truncate 1
            |> Seq.map (fun target ->
                EffectChoice.Attachments(
                    requirement.Id,
                    FrozenList<VimAttachment>
                        .Create(
                            requirement.EligibleCards
                            |> Seq.truncate requirement.Minimum
                            |> Seq.map (fun vim -> { Vim = vim; Bloke = target })
                        )
                ))
        | other -> failwithf "Unhandled choice requirement kind %A." other

    let stableChoices (requirements: FrozenList<ChoiceRequirement>) =
        FrozenList<EffectChoice>.Create(requirements |> Seq.collect stableChoice)

    /// Promotion triggers read the table as it will be after the swap, so the requirements have to be
    /// derived from a projected state rather than the one on the table now.
    let promotionChoiceRequirements
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        (promotion: CardState)
        (target: CardState)
        =
        let promotedCards =
            state.Cards
            |> Seq.map (fun card ->
                if card.Id = target.Id then
                    { card with
                        Zone = CardZone.Attached
                        AttachedTo = ValueSome promotion.Id
                        Attachments = FrozenList.empty
                        RoughStates = FrozenList.empty }
                elif card.Id = promotion.Id then
                    { card with
                        Zone = target.Zone
                        StackPosition = -1
                        Damage = target.Damage
                        Attachments = target.Attachments
                        UnderlyingCards =
                            FrozenList<CardInstanceId>
                                .Create(Seq.append target.UnderlyingCards [ target.Id ])
                        RoughStates = FrozenList.empty
                        EnteredAtOwnerRound = target.EnteredAtOwnerRound
                        LastPromotedRound = state.RoundNumber }
                elif Seq.contains card.Id target.Attachments then
                    { card with
                        AttachedTo = ValueSome promotion.Id }
                else
                    card)

        let promotedState =
            { state with
                Cards = FrozenList<CardState>.Create promotedCards }

        FrozenList<ChoiceRequirement>
            .Create(
                catalog.PartyTricks promotion
                |> Seq.filter (fun trick -> trick.Trigger = BlokemonTrigger.OnPromotionFromMitt)
                |> Seq.collect (fun trick ->
                    interpreter.GetChoiceRequirements(
                        promotedState,
                        { Actor = actor
                          Source = promotion.Id
                          Effect = EffectId trick.MechanicalId
                          Choices = FrozenList.empty }
                    ))
                |> fun requirements -> requirements.DistinctBy(fun requirement -> requirement.Id)
            )

    let invocationRequirements
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        (source: CardInstanceId)
        (effect: EffectId)
        =
        interpreter.GetChoiceRequirements(
            state,
            { Actor = actor
              Source = source
              Effect = effect
              Choices = FrozenList.empty }
        )

    let legal
        (kind: LegalActionKind)
        (state: MatchState)
        (actor: PlayerId)
        (key: string)
        (stableKey: string)
        (requirements: FrozenList<ChoiceRequirement>)
        (choices: FrozenList<EffectChoice>)
        (action: MatchAction)
        =
        { Kind = kind
          Command =
            { command state actor key action with
                Choices = choices }
          ChoiceRequirements = requirements
          StableKey = stableKey }

    let simple
        (kind: LegalActionKind)
        (state: MatchState)
        (actor: PlayerId)
        (key: string)
        (action: MatchAction)
        =
        legal kind state actor key key FrozenList.empty FrozenList.empty action
