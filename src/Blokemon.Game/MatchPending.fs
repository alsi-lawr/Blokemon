namespace Blokemon.Game

open Blokemon.Game.MatchRules

/// Parking a command that still needs an answer, and resuming it once the answer arrives.
module internal MatchPending =

    let pendEffect
        (builder: MatchBuilder)
        (command: MatchCommand)
        (source: CardInstanceId)
        (effect: EffectId)
        (requirements: FrozenList<ChoiceRequirement>)
        (recordedBeerMats: FrozenList<bool>)
        (plannedBeerMats: FrozenList<bool>)
        (attackStarted: bool)
        =
        if requirements.Count = 0 then
            HandlerResult.reject CommandRejectionCode.InvalidChoice
        else
            let chooser = requirements[0].Chooser

            if requirements |> Seq.exists (fun requirement -> requirement.Chooser <> chooser) then
                HandlerResult.rejectWith CommandRejectionCode.InvalidChoice requirements
            else
                let mismatched =
                    plannedBeerMats
                    |> Seq.skip (min recordedBeerMats.Count plannedBeerMats.Count)
                    |> Seq.toArray
                    |> Array.exists (fun expected ->
                        let actual = builder.TossBeerMat command.Actor

                        if actual = expected then
                            builder.Events.Add
                                { PendingMatchEvent.forCard
                                      MatchEventKind.BeerMatTossed
                                      command.Actor
                                      source with
                                    Effect = ValueSome effect
                                    BadgeSide = ValueSome actual }

                            false
                        else
                            true)

                if mismatched then
                    HandlerResult.reject CommandRejectionCode.AuthorityMismatch
                else
                    builder.PendingEffect <-
                        ValueSome
                            { Command = command
                              Source = source
                              Effect = effect
                              Chooser = chooser
                              Requirements = requirements
                              BeerMatResults = plannedBeerMats
                              AttackStarted = attackStarted }

                    builder.Phase <- MatchPhase.AwaitingEffectChoice

                    builder.Events.Add
                        { PendingMatchEvent.forCard
                              MatchEventKind.EffectChoiceRequested
                              chooser
                              source with
                            Effect = ValueSome effect }

                    HandlerResult.accepted

    /// The C# original rewrote Choices on exactly Attack, PlayKit and UsePartyTrick; the envelope
    /// makes that one copy-and-update, and the three-case guard survives as the shape it always was.
    let withChoices (command: MatchCommand) (choices: FrozenList<EffectChoice>) =
        match command.Action with
        | MatchAction.Attack _
        | MatchAction.PlayKit _
        | MatchAction.UsePartyTrick _ -> { command with Choices = choices }
        | _ -> command
