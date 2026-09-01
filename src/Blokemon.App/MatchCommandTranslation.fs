namespace Blokemon.App

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Linq
open Blokemon.App.Contracts
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchLabels
open Blokemon.App.DamagedDocument
open Blokemon.Core.SetDesign
open Blokemon.Game

/// Turns a legal action plus the player's submitted choices into the exact Blokemon.Game command
/// the engine will apply.
module internal MatchCommandTranslation =

    let toEffectChoice
        (requirement: ChoiceRequirement)
        (selection: MatchChoiceSelectionRequest)
        : EffectChoice voption =
        if selection.Kind <> choiceKind requirement.Kind then
            ValueNone
        else
            match requirement.Kind with
            | ChoiceRequirementKind.Amount when selection.Amount.HasValue ->
                ValueSome(EffectChoice.Amount(requirement.Id, selection.Amount.Value))
            | ChoiceRequirementKind.Cards ->
                ValueSome(
                    EffectChoice.Cards(
                        requirement.Id,
                        ImmutableArray.CreateRange(
                            orEmpty selection.CardInstanceIds |> Seq.map CardInstanceId
                        )
                    )
                )
            | ChoiceRequirementKind.MechanicalType ->
                match Enum.TryParse<BlokemonMechanicalType>(selection.MechanicalType, false) with
                | true, mechanicalType ->
                    ValueSome(EffectChoice.MechanicalType(requirement.Id, mechanicalType))
                | _ -> ValueNone
            | ChoiceRequirementKind.Attack ->
                match selection.EffectId with
                | null -> ValueNone
                | effectId -> ValueSome(EffectChoice.Attack(requirement.Id, EffectId effectId))
            | ChoiceRequirementKind.Attachments ->
                ValueSome(
                    EffectChoice.Attachments(
                        requirement.Id,
                        ImmutableArray.CreateRange(
                            orEmpty selection.Attachments
                            |> Seq.map (fun attachment ->
                                { Vim = CardInstanceId attachment.VimCardInstanceId
                                  Bloke = CardInstanceId attachment.BlokeCardInstanceId })
                        )
                    )
                )
            | _ -> ValueNone

    let materializeHumanCommand
        (action: LegalAction)
        (state: MatchState)
        (human: PlayerId)
        (clientCommandId: Guid)
        (submitted: IReadOnlyCollection<MatchChoiceSelectionRequest>)
        : CommandMaterialization =
        if
            submitted
            |> Seq.exists (fun choice -> not (choiceSubmissionIsStructurallyValid choice))
        then
            invalidChoice "A submitted choice is invalid."
        elif submitted |> Seq.countBy _.Id |> Seq.exists (fun (_, count) -> count > 1) then
            invalidChoice "Choose each option once."
        else

            let submittedById =
                submitted.ToDictionary((fun choice -> choice.Id), StringComparer.Ordinal)

            let requirements = action.ChoiceRequirements

            let wrongChooser =
                submitted
                |> Seq.tryPick (fun selection ->
                    match
                        requirements.SingleOrDefault(fun candidate ->
                            candidate.Id.Value = selection.Id)
                    with
                    | null -> Some(invalidChoice "This choice is not available.")
                    | requirement when requirement.Chooser <> human ->
                        Some
                            { Command = null
                              Error =
                                ApiError(
                                    "match.choice_wrong_chooser",
                                    "The computer must make this choice."
                                ) }
                    | _ -> None)

            match wrongChooser with
            | Some failure -> failure
            | None ->

                let choices = List<EffectChoice>()
                let mutable rejection: CommandMaterialization | null = null

                for requirement in
                    requirements |> Seq.filter (fun candidate -> candidate.Chooser = human) do
                    if isNull (box rejection) then
                        let declined =
                            if requirement.DependsOnOptional.IsSome then
                                match
                                    submittedById.TryGetValue
                                        requirement.DependsOnOptional.Value.Value
                                with
                                | true, parent when parent.Accepted.HasValue ->
                                    if parent.Accepted.Value then
                                        false
                                    elif submittedById.ContainsKey requirement.Id.Value then
                                        rejection <-
                                            invalidChoice
                                                "A choice was supplied for an optional branch that was declined."

                                        false
                                    else
                                        true
                                | _ ->
                                    rejection <- requiredChoice ()
                                    false
                            else
                                false

                        if isNull (box rejection) && not declined then
                            match submittedById.TryGetValue requirement.Id.Value with
                            | true, selection ->
                                match toEffectChoice requirement selection with
                                | ValueNone ->
                                    rejection <-
                                        invalidChoice
                                            "A submitted choice is not legal for this action."
                                | ValueSome choice -> choices.Add choice
                            | _ -> rejection <- requiredChoice ()

                match rejection with
                | null ->
                    let commandId: GameCommandId = CommandId $"client:{clientCommandId:D}"
                    let carriedChoices = ImmutableArray.CreateRange choices
                    let revision = state.Revision

                    // Only five cases ever carried submitted choices: Promote, PlayKit,
                    // UsePartyTrick, Attack and ResolveEffectChoice. The envelope makes the rest a
                    // copy-and-update that re-stamps the identity this client asked for.
                    let resolvedAction =
                        match action.Command.Action with
                        | MatchAction.ChooseOpening(oche, _) ->
                            let booth =
                                choices
                                |> Seq.pick (fun choice ->
                                    match choice with
                                    | EffectChoice.Cards(id, values) when
                                        id.Value = "opening:booth"
                                        ->
                                        Some values
                                    | _ -> None)

                            MatchAction.ChooseOpening(oche, booth)
                        | other -> other

                    let carriesChoices =
                        match resolvedAction with
                        | MatchAction.Promote _
                        | MatchAction.PlayKit _
                        | MatchAction.UsePartyTrick _
                        | MatchAction.Attack _
                        | MatchAction.ResolveEffectChoice -> true
                        | _ -> false

                    let command =
                        { action.Command with
                            Id = commandId
                            ExpectedRevision = revision
                            Choices =
                                (if carriesChoices then
                                     carriedChoices
                                 else
                                     ImmutableArray<_>.Empty)
                            Action = resolvedAction }

                    { Command = command; Error = null }
                | failure -> failure
