namespace Blokemon.Cpu

open Blokemon.Game

open System
open Blokemon.Cpu.CpuCandidateSelection
open Blokemon.Cpu.CpuPolicyLimits

/// A deterministic strategic policy. Every choice begins and ends at the engine-owned CPU
/// candidate boundary; only bounded planning snapshots are advanced between those two points.
type DeterministicCpu() =

    member this.Choose(engine: MatchEngine, state: MatchState, actor: PlayerId) =
        let input =
            { CpuPolicyInput.normal with
                DecisionIndex = uint64 (max 0L state.Revision.Value) }

        this.Choose(engine, state, actor, input).Decision

    member _.Choose
        (engine: MatchEngine, state: MatchState, actor: PlayerId, input: CpuPolicyInput)
        =
        let mode, nodeLimit, depthLimit =
            match input.Difficulty with
            | CpuDifficulty.Easy
            | CpuDifficulty.Normal -> CpuObservationMode.Fair, immediateNodeLimit, 1
            | CpuDifficulty.Hard -> CpuObservationMode.Fair, searchNodeLimit, searchDepthLimit
            | CpuDifficulty.Impossible ->
                CpuObservationMode.Authoritative, searchNodeLimit, searchDepthLimit

        let observation = engine.GetCpuObservation(state, actor, mode)

        let knowledge =
            match mode with
            | CpuObservationMode.Fair ->
                observation.State.Cards
                |> Seq.map _.Id
                |> Set.ofSeq
                |> CpuEvaluationKnowledge.Fair
            | CpuObservationMode.Authoritative -> CpuEvaluationKnowledge.Authoritative

        let candidates = playableCandidates observation
        let budget = CpuWorkBudget nodeLimit

        let immediate =
            evaluateImmediate engine actor mode knowledge budget state observation candidates

        let evaluated =
            match input.Difficulty with
            | CpuDifficulty.Hard
            | CpuDifficulty.Impossible when budget.CanVisit ->
                let top =
                    immediate
                    |> Seq.sortWith (fun left right ->
                        let byScore = compare right.Score left.Score

                        if byScore <> 0 then
                            byScore
                        else
                            String.CompareOrdinal(
                                left.Candidate.Id.Value,
                                right.Candidate.Id.Value
                            ))
                    |> Seq.truncate beamWidth
                    |> Seq.map _.Candidate.Id
                    |> Set.ofSeq

                immediate
                |> Array.map (fun evaluated ->
                    if top.Contains evaluated.Candidate.Id && budget.CanVisit then
                        match
                            tryAdvance
                                engine
                                actor
                                mode
                                knowledge
                                budget
                                1
                                state
                                observation
                                evaluated.Candidate
                        with
                        | ValueSome(immediateScore, next, nextObservation) ->
                            let forward =
                                if canContinue knowledge nextObservation then
                                    CpuForwardSearch.score
                                        engine
                                        actor
                                        mode
                                        knowledge
                                        budget
                                        1
                                        depthLimit
                                        next
                                        nextObservation
                                else
                                    0

                            { evaluated with
                                Score = immediateScore + forward }
                        | ValueNone -> evaluated
                    else
                        evaluated)
            | _ -> immediate

        let selected =
            match input.Difficulty with
            | CpuDifficulty.Easy -> chooseEasy input observation.State.Revision evaluated
            | CpuDifficulty.Normal
            | CpuDifficulty.Hard
            | CpuDifficulty.Impossible -> chooseBest evaluated

        let fallback =
            candidates
            |> Seq.tryFind (fun candidate -> candidate.Kind = LegalActionKind.EndRound)
            |> Option.orElseWith (fun () -> candidates |> Seq.tryHead)

        let selected = selected |> Option.map _.Candidate |> Option.orElse fallback

        let selectedScore =
            selected
            |> Option.bind (fun candidate ->
                evaluated
                |> Seq.tryFind (fun value -> value.Candidate.Id = candidate.Id)
                |> Option.map _.Score)

        let decision =
            selected
            |> Option.bind (fun candidate ->
                engine.TryMaterializeCpuAction(state, actor, candidate.Id)
                |> ValueOption.toOption)
            |> Option.map CpuDecision.Selected
            |> Option.defaultValue CpuDecision.NoLegalAction

        { Decision = decision
          Evidence =
            { Input = input
              Candidate = selected |> ValueOption.ofOption |> ValueOption.map _.Id
              Score = selectedScore |> ValueOption.ofOption
              Work =
                { CandidatesConsidered = immediate.Length
                  NodesVisited = budget.Visited
                  NodeLimit = nodeLimit
                  DepthReached = budget.DepthReached
                  DepthLimit = depthLimit } } }
