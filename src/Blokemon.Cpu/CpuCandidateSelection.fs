namespace Blokemon.Cpu

open Blokemon.Game

open System

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
        (knowledge: CpuEvaluationKnowledge)
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
                            knowledge
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
        (knowledge: CpuEvaluationKnowledge)
        (budget: CpuWorkBudget)
        (state: MatchState)
        (observation: CpuObservation)
        (candidates: CpuLegalCandidate array)
        =
        candidates
        |> Seq.choose (fun candidate ->
            match tryAdvance engine actor mode knowledge budget 1 state observation candidate with
            | ValueSome(score, _, _) -> Some { Candidate = candidate; Score = score }
            | ValueNone -> None)
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
        let ending =
            evaluated
            |> Seq.tryFind (fun value -> value.Candidate.Kind = LegalActionKind.EndRound)

        let endScore = ending |> Option.map _.Score |> Option.defaultValue Int32.MinValue

        let productive =
            evaluated
            |> Seq.filter (fun value ->
                value.Candidate.Kind <> LegalActionKind.EndRound
                && value.Score > 0
                && value.Score > endScore
                && not (
                    evaluated
                    |> Seq.exists (fun alternative ->
                        alternative.Candidate.Action = value.Candidate.Action
                        && alternative.Score > value.Score)
                ))
            |> Seq.sortWith (fun left right ->
                let byScore = compare right.Score left.Score

                if byScore <> 0 then
                    byScore
                else
                    String.CompareOrdinal(left.Candidate.Id.Value, right.Candidate.Id.Value))
            |> Seq.toArray

        if productive.Length > 0 then
            Some productive[deterministicIndex input revision productive.Length]
        else
            ending |> Option.orElseWith (fun () -> chooseBest evaluated)

    let canContinue knowledge (observation: CpuObservation) =
        match knowledge with
        | CpuEvaluationKnowledge.Authoritative -> true
        | CpuEvaluationKnowledge.Fair knownAtRoot ->
            observation.State.Cards |> Seq.forall (fun card -> knownAtRoot.Contains card.Id)
