namespace Blokemon.Game

open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.ChoiceShapes

/// Deterministically materialize every answer to the requirements raised by a proposed action.
/// MatchEngine remains the authority and removes every materialized command it would reject.
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

    let rec private combinations count values =
        seq {
            match count, values with
            | 0, _ -> yield []
            | _, [] -> ()
            | remaining, value :: rest ->
                for selected in combinations (remaining - 1) rest do
                    yield value :: selected

                yield! combinations remaining rest
        }

    let rec private permutations count values =
        seq {
            if count = 0 then
                yield []
            else
                for index, value in values |> List.indexed do
                    for selected in permutations (count - 1) (List.removeAt index values) do
                        yield value :: selected
        }

    let cardSets minimum maximum preserveOrder (cards: CardInstanceId seq) =
        let cards = cards |> Seq.distinct |> Seq.sort |> List.ofSeq
        let maximum = min maximum cards.Length

        seq {
            for count in max 0 minimum .. maximum do
                let selections =
                    if preserveOrder then
                        permutations count cards
                    else
                        combinations count cards

                for selected in selections do
                    yield ImmutableArray.CreateRange selected
        }

    let private attachmentSets (requirement: ChoiceRequirement) =
        let targets = requirement.EligibleTargets |> Seq.distinct |> Seq.sort |> List.ofSeq

        let rec placements (vims: CardInstanceId list) : VimAttachment list seq =
            seq {
                match vims with
                | [] -> yield []
                | vim :: rest ->
                    for target in targets do
                        for placed in placements rest do
                            yield ({ Vim = vim; Bloke = target }: VimAttachment) :: placed
            }

        seq {
            for vims in
                cardSets requirement.Minimum requirement.Maximum false requirement.EligibleCards do
                for placed in placements (List.ofSeq vims) do
                    yield ImmutableArray.CreateRange placed
        }

    let private choicesFor (requirement: ChoiceRequirement) : EffectChoice seq =
        match requirement.Kind with
        | ChoiceRequirementKind.Amount ->
            seq {
                for amount in requirement.Minimum .. requirement.Maximum do
                    yield EffectChoice.Amount(requirement.Id, amount)
            }
        | ChoiceRequirementKind.Cards ->
            cardSets
                requirement.Minimum
                requirement.Maximum
                requirement.PreserveCardOrder
                requirement.EligibleCards
            |> Seq.filter (fun cards ->
                not requirement.RequireDifferentMechanicalTypes
                || haveDifferentMechanicalTypes cards requirement)
            |> Seq.map (fun cards -> EffectChoice.Cards(requirement.Id, cards))
        | ChoiceRequirementKind.MechanicalType ->
            requirement.EligibleMechanicalTypes
            |> Seq.distinct
            |> Seq.sort
            |> Seq.map (fun mechanicalType ->
                EffectChoice.MechanicalType(requirement.Id, mechanicalType))
        | ChoiceRequirementKind.Attack ->
            requirement.EligibleEffects
            |> Seq.distinct
            |> Seq.sort
            |> Seq.map (fun effect -> EffectChoice.Attack(requirement.Id, effect))
        | ChoiceRequirementKind.Attachments ->
            attachmentSets requirement
            |> Seq.map (fun placements -> EffectChoice.Attachments(requirement.Id, placements))
        | other -> failwithf "Unhandled choice requirement kind %A." other

    let choiceCombinations (requirements: ImmutableArray<ChoiceRequirement>) (chooser: PlayerId) =
        let owned =
            requirements
            |> Seq.filter (fun requirement -> requirement.Chooser = chooser)
            |> Seq.toList

        let ownedIds = owned |> Seq.map _.Id |> Set.ofSeq

        let rec dependencyOrder
            (ordered: ChoiceRequirement list)
            (orderedIds: Set<EffectChoiceId>)
            (remaining: ChoiceRequirement list)
            : ChoiceRequirement array option =
            match remaining with
            | [] -> Some(List.rev ordered |> List.toArray)
            | values ->
                values
                |> List.tryFindIndex (fun requirement ->
                    match requirement.DependsOnOptional with
                    | ValueNone -> true
                    | ValueSome dependency ->
                        not (ownedIds.Contains dependency) || orderedIds |> Set.contains dependency)
                |> Option.bind (fun index ->
                    let requirement = values[index]

                    dependencyOrder
                        (requirement :: ordered)
                        (orderedIds |> Set.add requirement.Id)
                        (List.removeAt index values))

        match dependencyOrder [] Set.empty owned with
        | None -> Seq.empty
        | Some ordered ->
            let rec enumerate (index: int) (selected: EffectChoice list) =
                seq {
                    if index = ordered.Length then
                        yield ImmutableArray.CreateRange(List.rev selected)
                    else
                        let requirement = ordered[index]

                        let isActive =
                            match requirement.DependsOnOptional with
                            | ValueNone -> true
                            | ValueSome dependency ->
                                selected
                                |> List.tryFind (fun choice -> choice.Id = dependency)
                                |> Option.exists choiceAcceptsDependents

                        if isActive then
                            for choice in choicesFor requirement do
                                yield! enumerate (index + 1) (choice :: selected)
                        else
                            yield! enumerate (index + 1) selected
                }

            enumerate 0 []

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
