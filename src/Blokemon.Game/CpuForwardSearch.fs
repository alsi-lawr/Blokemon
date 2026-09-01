namespace Blokemon.Game

open Blokemon.Game.CpuCandidateSelection
open Blokemon.Game.CpuPolicyLimits

module internal CpuForwardSearch =

    let rec score
        (engine: MatchEngine)
        (actor: PlayerId)
        (mode: CpuObservationMode)
        (budget: CpuWorkBudget)
        depth
        depthLimit
        (state: MatchState)
        (observation: CpuObservation)
        =
        if depth >= depthLimit || not budget.CanVisit || state.Winner.IsSome then
            0
        else
            let candidates = playableCandidates observation

            let bounded =
                if candidates.Length <= beamWidth then
                    candidates
                else
                    let ending =
                        candidates
                        |> Array.tryFind (fun candidate ->
                            candidate.Kind = LegalActionKind.EndRound)

                    let actions =
                        candidates
                        |> Seq.filter (fun candidate -> candidate.Kind <> LegalActionKind.EndRound)
                        |> Seq.truncate (beamWidth - (if ending.IsSome then 1 else 0))

                    match ending with
                    | Some candidate -> Seq.append actions (Seq.singleton candidate) |> Seq.toArray
                    | None -> actions |> Seq.toArray

            bounded
            |> Seq.choose (fun candidate ->
                match
                    tryAdvance engine actor mode budget (depth + 1) state observation candidate
                with
                | ValueNone -> None
                | ValueSome(immediate, next, nextObservation) ->
                    Some(
                        immediate
                        + score
                            engine
                            actor
                            mode
                            budget
                            (depth + 1)
                            depthLimit
                            next
                            nextObservation
                    ))
            |> Seq.append (Seq.singleton 0)
            |> Seq.max
