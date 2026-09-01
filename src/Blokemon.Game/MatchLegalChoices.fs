namespace Blokemon.Game

open System.Collections.Immutable
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
          Choices = ImmutableArray<_>.Empty
          Action = action }

    let private stableChoice (requirement: ChoiceRequirement) : EffectChoice seq =
        match requirement.Kind with
        | ChoiceRequirementKind.Amount ->
            [ EffectChoice.Amount(requirement.Id, requirement.Minimum) ]
        | ChoiceRequirementKind.Cards ->
            [ EffectChoice.Cards(
                  requirement.Id,
                  ImmutableArray.CreateRange(
                      requirement.EligibleCards |> Seq.truncate requirement.Minimum
                  )
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
        | ChoiceRequirementKind.Attachments ->
            requirement.EligibleTargets
            |> Seq.truncate 1
            |> Seq.map (fun target ->
                EffectChoice.Attachments(
                    requirement.Id,
                    ImmutableArray.CreateRange(
                        requirement.EligibleCards
                        |> Seq.truncate requirement.Minimum
                        |> Seq.map (fun vim -> { Vim = vim; Bloke = target })
                    )
                ))
        | other -> failwithf "Unhandled choice requirement kind %A." other

    let stableChoices (requirements: ImmutableArray<ChoiceRequirement>) =
        ImmutableArray.CreateRange(requirements |> Seq.collect stableChoice)

    let promotionChoiceRequirements
        (_: AuthorityCatalog)
        (_: BlokemonInterpreter)
        (_: MatchState)
        (_: PlayerId)
        (_: CardState)
        (_: CardState)
        =
        ImmutableArray<_>.Empty

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
              Choices = ImmutableArray<_>.Empty }
        )

    let legal
        (kind: LegalActionKind)
        (state: MatchState)
        (actor: PlayerId)
        (key: string)
        (stableKey: string)
        (requirements: ImmutableArray<ChoiceRequirement>)
        (choices: ImmutableArray<EffectChoice>)
        (action: MatchAction)
        =
        { Kind = kind
          Command =
            { command state actor key action with
                Choices = choices }
          ChoiceRequirements = requirements
          StableKey = stableKey
          Affordability = ActionAffordability.Payable }

    let simple
        (kind: LegalActionKind)
        (state: MatchState)
        (actor: PlayerId)
        (key: string)
        (action: MatchAction)
        =
        legal kind state actor key key ImmutableArray<_>.Empty ImmutableArray<_>.Empty action
