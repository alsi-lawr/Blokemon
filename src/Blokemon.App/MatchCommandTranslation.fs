namespace Blokemon.App

open System
open System.Collections.Generic
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
        : EffectChoice | null =
        if selection.Kind <> choiceKind requirement.Kind then
            null
        else
            match requirement.Kind with
            | ChoiceRequirementKind.Optional when selection.Accepted.HasValue ->
                EffectChoice.Optional(requirement.Id, selection.Accepted.Value)
            | ChoiceRequirementKind.Amount when selection.Amount.HasValue ->
                EffectChoice.Amount(requirement.Id, selection.Amount.Value)
            | ChoiceRequirementKind.Cards ->
                EffectChoice.Cards(
                    requirement.Id,
                    FrozenList<CardInstanceId>
                        .Create(orEmpty selection.CardInstanceIds |> Seq.map CardInstanceId)
                )
            | ChoiceRequirementKind.MechanicalType ->
                match Enum.TryParse<BlokemonMechanicalType>(selection.MechanicalType, false) with
                | true, mechanicalType ->
                    EffectChoice.MechanicalType(requirement.Id, mechanicalType)
                | _ -> null
            | ChoiceRequirementKind.Attack ->
                match selection.EffectId with
                | null -> null
                | effectId -> EffectChoice.Attack(requirement.Id, EffectId effectId)
            | ChoiceRequirementKind.Distribution ->
                EffectChoice.Distribution(
                    requirement.Id,
                    FrozenList<DamageAllocation>
                        .Create(
                            orEmpty selection.Distribution
                            |> Seq.map (fun allocation ->
                                DamageAllocation(
                                    CardInstanceId allocation.CardInstanceId,
                                    allocation.Counters
                                ))
                        )
                )
            | ChoiceRequirementKind.Attachments ->
                EffectChoice.Attachments(
                    requirement.Id,
                    FrozenList<VimAttachment>
                        .Create(
                            orEmpty selection.Attachments
                            |> Seq.map (fun attachment ->
                                VimAttachment(
                                    CardInstanceId attachment.VimCardInstanceId,
                                    CardInstanceId attachment.BlokeCardInstanceId
                                ))
                        )
                )
            | _ -> null

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
                            if requirement.DependsOnOptional.HasValue then
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
                                | null ->
                                    rejection <-
                                        invalidChoice
                                            "A submitted choice is not legal for this action."
                                | choice -> choices.Add choice
                            | _ -> rejection <- requiredChoice ()

                match rejection with
                | null ->
                    let commandId = GameCommandId $"client:{clientCommandId:D}"
                    let frozenChoices = FrozenList<EffectChoice>.Create choices
                    let revision = state.Revision

                    // The C# original wrote this as an 18-arm `with` block. Blokemon.Game's MatchCommand
                    // is a C# record, which F# cannot copy-and-update (FS0786), so each arm is an
                    // explicit construction. FIVE cases declare Choices as a constructor parameter -
                    // Promote, PlayKit, UsePartyTrick, Attack and ResolveEffectChoice (Commands.cs:133,
                    // 143, 165, 175, 217) - and each of those is reconstructed with the choices this
                    // player submitted. The other eleven declare it as an init-only override defaulted
                    // to `[]`, and the engine never sets it on them: its own Choices-carrying
                    // constructions are exactly those five (MatchEngine.cs:2907 ResolveEffectChoice,
                    // :3035 Promote, :3104 PlayKit, :3139/:3250 UsePartyTrick, :3168 Attack) and
                    // WithChoices (:1658) rewrites only Attack, PlayKit and UsePartyTrick. So dropping
                    // the source command's Choices on those eleven drops nothing.
                    let command =
                        action.Command.Match<MatchCommand>(
                            (fun value ->
                                MatchCommand.ChooseMulliganBonus(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.CardsToDraw
                                )),
                            (fun value ->
                                let booth =
                                    choices
                                        .OfType<EffectChoice.Cards>()
                                        .Single(fun choice -> choice.Id.Value = "opening:booth")
                                        .Values

                                MatchCommand.ChooseOpening(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Oche,
                                    booth
                                )),
                            (fun value ->
                                MatchCommand.AttachVim(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Vim,
                                    value.Bloke
                                )),
                            (fun value ->
                                MatchCommand.PlayBloke(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Bloke
                                )),
                            (fun value ->
                                MatchCommand.Promote(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Promotion,
                                    value.Bloke,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.PlayKit(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Kit,
                                    value.Target,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.Taxi(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.BoothBloke,
                                    value.VimToChuck
                                )),
                            (fun value ->
                                MatchCommand.UsePartyTrick(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Source,
                                    value.Effect,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.Attack(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Attacker,
                                    value.AttackId,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.ChuckFossil(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Fossil
                                )),
                            (fun value ->
                                MatchCommand.EndRound(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision
                                )),
                            (fun value ->
                                MatchCommand.ChooseReplacement(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.BoothBloke
                                )),
                            (fun value ->
                                MatchCommand.ResolveEffectChoice(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.ResolveKnockoutTrigger(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Vim
                                )),
                            (fun value ->
                                MatchCommand.ResolveBarChitTrigger(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.PutOntoBooth
                                )),
                            (fun value ->
                                MatchCommand.Resign(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision
                                ))
                        )

                    { Command = command; Error = null }
                | failure -> failure
