namespace Blokemon.Game

open Blokemon.Game.ChoiceShapes
open Blokemon.Game.ChoiceInspection

/// Whether the answers a caller supplied actually satisfy the questions a program asked.
module internal ChoiceValidation =

    let private choiceIsValid (choice: EffectChoice) (requirement: ChoiceRequirement) =
        match choice with
        | EffectChoice.Optional _ -> requirement.Kind = ChoiceRequirementKind.Optional
        | EffectChoice.Amount(_, amount) ->
            requirement.Kind = ChoiceRequirementKind.Amount
            && amount >= requirement.Minimum
            && amount <= requirement.Maximum
        | EffectChoice.Cards(_, cards) ->
            requirement.Kind = ChoiceRequirementKind.Cards
            && cards.Count >= requirement.Minimum
            && cards.Count <= requirement.Maximum
            && (cards |> Seq.distinct |> Seq.length) = cards.Count
            && cards |> Seq.forall (fun card -> Seq.contains card requirement.EligibleCards)
            && (not requirement.RequireDifferentMechanicalTypes
                || haveDifferentMechanicalTypes cards requirement)
        | EffectChoice.MechanicalType(_, value) ->
            requirement.Kind = ChoiceRequirementKind.MechanicalType
            && Seq.contains value requirement.EligibleMechanicalTypes
        | EffectChoice.Attack(_, value) ->
            requirement.Kind = ChoiceRequirementKind.Attack
            && Seq.contains value requirement.EligibleEffects
        | EffectChoice.Distribution(_, allocations) ->
            requirement.Kind = ChoiceRequirementKind.Distribution
            && (allocations |> Seq.sumBy (fun allocation -> allocation.Counters)) = requirement.Maximum
            && (allocations
                |> Seq.map (fun allocation -> allocation.Card)
                |> Seq.distinct
                |> Seq.length) = allocations.Count
            && allocations
               |> Seq.forall (fun allocation ->
                   allocation.Counters >= 0
                   && Seq.contains allocation.Card requirement.EligibleCards)
        | EffectChoice.Attachments(_, placements) ->
            requirement.Kind = ChoiceRequirementKind.Attachments
            && placements.Count >= requirement.Minimum
            && placements.Count <= requirement.Maximum
            && (placements
                |> Seq.map (fun placement -> placement.Vim)
                |> Seq.distinct
                |> Seq.length) = placements.Count
            && placements
               |> Seq.forall (fun placement ->
                   Seq.contains placement.Vim requirement.EligibleCards
                   && Seq.contains placement.Bloke requirement.EligibleTargets)

    let validateChoices
        (choices: FrozenList<EffectChoice>)
        (requirements: FrozenList<ChoiceRequirement>)
        =
        let mutable rejection = ValueNone

        for requirement in requirements do
            if rejection.IsNone then
                let declined =
                    match requirement.DependsOnOptional with
                    | ValueNone -> false
                    | ValueSome dependency ->
                        match
                            choices
                            |> Seq.tryPick (
                                EffectChoice.optional dependency >> ValueOption.toOption
                            )
                        with
                        | None ->
                            rejection <- ValueSome CommandRejectionCode.ChoiceRequired
                            false
                        | Some accepted -> not accepted

                if rejection.IsNone && not declined then
                    let matching =
                        choices
                        |> Seq.filter (fun choice -> choice.Id = requirement.Id)
                        |> Seq.toArray

                    if matching.Length = 0 then
                        rejection <- ValueSome CommandRejectionCode.ChoiceRequired
                    elif matching.Length <> 1 || not (choiceIsValid matching[0] requirement) then
                        rejection <- ValueSome CommandRejectionCode.InvalidChoice

        if rejection.IsNone then
            if
                choices
                |> Seq.exists (fun choice ->
                    not (
                        requirements |> Seq.exists (fun requirement -> requirement.Id = choice.Id)
                    ))
            then
                ValueSome CommandRejectionCode.InvalidChoice
            else
                ValueNone
        else
            rejection

    let validateChoiceSubmission
        (choices: FrozenList<EffectChoice>)
        (requirements: FrozenList<ChoiceRequirement>)
        (chooser: PlayerId)
        =
        let owned =
            FrozenList<ChoiceRequirement>
                .Create(
                    requirements |> Seq.filter (fun requirement -> requirement.Chooser = chooser)
                )

        if
            choices
            |> Seq.exists (fun choice ->
                (requirements |> Seq.exists (fun requirement -> requirement.Id = choice.Id))
                && not (owned |> Seq.exists (fun requirement -> requirement.Id = choice.Id)))
        then
            ValueSome CommandRejectionCode.WrongChooser
        elif
            choices
            |> Seq.exists (fun choice ->
                not (requirements |> Seq.exists (fun requirement -> requirement.Id = choice.Id)))
        then
            ValueSome CommandRejectionCode.InvalidChoice
        else
            validateChoices choices owned
