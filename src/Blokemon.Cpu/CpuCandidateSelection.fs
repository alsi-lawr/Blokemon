namespace Blokemon.Cpu

open Blokemon.Game

open System
open System.Collections.Generic

type internal EvaluatedCpuCandidate =
    { Candidate: CpuLegalCandidate
      Score: int }

type internal CpuWorkBudget(limit: int) =
    let mutable visited = 0
    let mutable depthReached = 0

    member _.CanVisit = visited < limit
    member _.Visited = visited
    member _.DepthReached = depthReached

    member _.Visit(depth: int) =
        if visited < limit then
            visited <- visited + 1
            depthReached <- max depthReached depth
            true
        else
            false

module internal CpuCandidateSelection =

    open CpuPolicyLimits

    let private mix value =
        let mutable mixed = value
        mixed <- (mixed ^^^ (mixed >>> 30)) * 0xBF58476D1CE4E5B9UL
        mixed <- (mixed ^^^ (mixed >>> 27)) * 0x94D049BB133111EBUL
        mixed ^^^ (mixed >>> 31)

    let private deterministicIndex (input: CpuPolicyInput) (revision: MatchRevision) count =
        let value =
            mix (
                input.Seed
                ^^^ input.DecisionIndex
                ^^^ uint64 revision.Value
                ^^^ 0x9E3779B97F4A7C15UL
            )

        int (value % uint64 count)

    let playableCandidates (observation: CpuObservation) =
        observation.Candidates
        |> Seq.filter (fun candidate -> candidate.Kind <> LegalActionKind.Resign)
        |> Seq.truncate rootCandidateLimit
        |> Seq.toArray

    let tryAdvance
        (engine: MatchEngine)
        (actor: PlayerId)
        (mode: CpuObservationMode)
        (budget: CpuWorkBudget)
        depth
        (state: MatchState)
        (observation: CpuObservation)
        (candidate: CpuLegalCandidate)
        =
        if not (budget.Visit depth) then
            ValueNone
        else
            match engine.TryMaterializeCpuAction(state, actor, candidate.Id) with
            | ValueNone -> ValueNone
            | ValueSome action ->
                match engine.Apply(state, action.Command) with
                | CommandOutcome.Rejected _ -> ValueNone
                | CommandOutcome.Applied(next, _) ->
                    let nextObservation = engine.GetCpuObservation(next, actor, mode)

                    let score =
                        CpuEvaluation.scoreTransition
                            engine
                            actor
                            candidate.Kind
                            state
                            observation
                            next
                            nextObservation

                    ValueSome(score, next, nextObservation)

    let evaluateImmediate
        (engine: MatchEngine)
        (actor: PlayerId)
        (mode: CpuObservationMode)
        (budget: CpuWorkBudget)
        (state: MatchState)
        (observation: CpuObservation)
        (candidates: CpuLegalCandidate array)
        =
        candidates
        |> Seq.choose (fun candidate ->
            match tryAdvance engine actor mode budget 1 state observation candidate with
            | ValueSome(score, _, _) -> Some { Candidate = candidate; Score = score }
            | ValueNone -> None)
        |> Seq.toArray

    let aggregateSamples
        (samples: EvaluatedCpuCandidate array array)
        (candidates: CpuLegalCandidate array)
        =
        let byId = Dictionary<CpuCandidateId, ResizeArray<int>>()

        for sample in samples do
            for evaluated in sample do
                match byId.TryGetValue evaluated.Candidate.Id with
                | true, scores -> scores.Add evaluated.Score
                | _ ->
                    let scores = ResizeArray<int>()
                    scores.Add evaluated.Score
                    byId.Add(evaluated.Candidate.Id, scores)

        candidates
        |> Seq.choose (fun candidate ->
            match byId.TryGetValue candidate.Id with
            | true, scores when scores.Count = samples.Length ->
                Some
                    { Candidate = candidate
                      Score = scores |> Seq.sum |> (fun total -> total / scores.Count) }
            | _ -> None)
        |> Seq.toArray

    let chooseBest (evaluated: EvaluatedCpuCandidate array) =
        evaluated
        |> Seq.sortWith (fun left right ->
            let byScore = compare right.Score left.Score

            if byScore <> 0 then
                byScore
            else
                String.CompareOrdinal(left.Candidate.Id.Value, right.Candidate.Id.Value))
        |> Seq.tryHead

    let chooseEasy input revision (evaluated: EvaluatedCpuCandidate array) =
        match chooseBest evaluated with
        | None -> None
        | Some best ->
            let endScore =
                evaluated
                |> Seq.tryFind (fun value -> value.Candidate.Kind = LegalActionKind.EndRound)
                |> Option.map _.Score
                |> Option.defaultValue Int32.MinValue

            let productive =
                evaluated
                |> Seq.filter (fun value ->
                    value.Candidate.Kind <> LegalActionKind.EndRound && value.Score > endScore)
                |> Seq.toArray

            let pool = if productive.Length > 0 then productive else evaluated
            let threshold = max 20 (abs best.Score / 5)

            let plausible =
                pool
                |> Seq.filter (fun value -> value.Score >= best.Score - threshold)
                |> Seq.sortWith (fun left right ->
                    let byScore = compare right.Score left.Score

                    if byScore <> 0 then
                        byScore
                    else
                        String.CompareOrdinal(left.Candidate.Id.Value, right.Candidate.Id.Value))
                |> Seq.toArray

            Some plausible[deterministicIndex input revision plausible.Length]
