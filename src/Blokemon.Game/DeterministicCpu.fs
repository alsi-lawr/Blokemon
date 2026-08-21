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
              LegalActionKind.ChooseBonusPlacement, 2
              LegalActionKind.ChooseReplacement, 3
              LegalActionKind.ResolveEffectChoice, 4
              LegalActionKind.ResolveKnockoutTrigger, 5
              LegalActionKind.ResolveBarChitTrigger, 6
              LegalActionKind.PlayBloke, 7
              LegalActionKind.Promote, 8
              LegalActionKind.AttachVim, 9
              LegalActionKind.PlayKit, 10
              LegalActionKind.UsePartyTrick, 11
              LegalActionKind.Attack, 12
              LegalActionKind.Taxi, 13
              LegalActionKind.ChuckFossil, 14
              LegalActionKind.EndRound, 15 ]

    member _.Choose(engine: MatchEngine, state: MatchState, actor: PlayerId) =
        // Resignation is voluntary and never automated, and an action the actor cannot pay for is
        // offered to the interface rather than to a policy, so the set seen here is exactly the
        // set of moves that would actually be accepted.
        let selected =
            engine.GetLegalActions(state, actor)
            |> Seq.filter (fun action ->
                action.Kind <> LegalActionKind.Resign
                && action.Affordability = ActionAffordability.Payable)
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
