namespace Blokemon.Game

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
type CpuDecision =
    | Selected of action: LegalAction
    | NoLegalAction

/// A fixed policy over the legal action set: same state, same actor, same choice, every time.
type DeterministicCpu() =
    static let priority =
        dict
            [ LegalActionKind.ChooseMulliganBonus, 0
              LegalActionKind.ChooseOpening, 1
              LegalActionKind.ChooseReplacement, 2
              LegalActionKind.ResolveEffectChoice, 3
              LegalActionKind.ResolveKnockoutTrigger, 4
              LegalActionKind.ResolveBarChitTrigger, 5
              LegalActionKind.PlayBloke, 6
              LegalActionKind.Promote, 7
              LegalActionKind.AttachVim, 8
              LegalActionKind.PlayKit, 9
              LegalActionKind.UsePartyTrick, 10
              LegalActionKind.Attack, 11
              LegalActionKind.Taxi, 12
              LegalActionKind.ChuckFossil, 13
              LegalActionKind.EndRound, 14 ]

    member _.Choose(engine: MatchEngine, state: MatchState, actor: PlayerId) =
        // Resignation is voluntary and never automated, so the policy sees exactly the action
        // set it saw before resignation existed.
        let selected =
            engine.GetLegalActions(state, actor)
            |> Seq.filter (fun action -> action.Kind <> LegalActionKind.Resign)
            |> Seq.sortWith (fun left right ->
                let byPriority = compare priority[left.Kind] priority[right.Kind]

                if byPriority <> 0 then
                    byPriority
                else
                    String.CompareOrdinal(left.StableKey, right.StableKey))
            |> Seq.tryHead

        match selected with
        | Some action -> CpuDecision.Selected action
        | None -> CpuDecision.NoLegalAction
