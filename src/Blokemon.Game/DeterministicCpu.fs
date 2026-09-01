namespace Blokemon.Game

open System
open System.Collections.Generic
open Blokemon.Game.CpuCandidateSelection
open Blokemon.Game.CpuPolicyLimits

/// A deterministic strategic policy. Every choice begins and ends at the engine-owned CPU
/// candidate boundary; only bounded planning snapshots are advanced between those two points.
type DeterministicCpu() =

    static let legacyPriority =
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

    member this.Choose(engine: MatchEngine, state: MatchState, actor: PlayerId) =
        let input =
            { CpuPolicyInput.normal with
                DecisionIndex = uint64 (max 0L state.Revision.Value) }

        this.Choose(engine, state, actor, input).Decision

    member _.ChooseLegacy(engine: MatchEngine, state: MatchState, actor: PlayerId) =
        engine.GetLegalActions(state, actor)
        |> Seq.filter (fun action ->
            action.Kind <> LegalActionKind.Resign
            && action.Affordability = ActionAffordability.Payable)
        |> Seq.sortWith (fun left right ->
            let byPriority = compare legacyPriority[left.Kind] legacyPriority[right.Kind]

            if byPriority <> 0 then
                byPriority
            else
                String.CompareOrdinal(left.StableKey, right.StableKey))
        |> Seq.tryHead
        |> Option.map CpuDecision.Selected
        |> Option.defaultValue CpuDecision.NoLegalAction

    member _.Choose
        (engine: MatchEngine, state: MatchState, actor: PlayerId, input: CpuPolicyInput)
        =
        let mode, sampleCount, nodeLimit, depthLimit =
            match input.Difficulty with
            | CpuDifficulty.Easy
            | CpuDifficulty.Normal -> CpuObservationMode.Fair, 1, normalNodeLimit, 1
            | CpuDifficulty.Hard ->
                CpuObservationMode.Fair, hardSamples, hardNodeLimit, hardDepthLimit
            | CpuDifficulty.Impossible ->
                CpuObservationMode.Authoritative, 1, hardNodeLimit, hardDepthLimit

        let observation = engine.GetCpuObservation(state, actor, mode)
        let candidates = playableCandidates observation
        let budget = CpuWorkBudget nodeLimit

        let samples =
            [| for sampleIndex in 0 .. sampleCount - 1 ->
                   let sampleState =
                       engine.CreateCpuPlanningState(
                           state,
                           observation,
                           mode,
                           input.Seed,
                           uint64 sampleIndex
                       )

                   let sampleObservation = engine.GetCpuObservation(sampleState, actor, mode)
                   sampleState, sampleObservation |]

        let immediate =
            samples
            |> Array.map (fun (sampleState, sampleObservation) ->
                evaluateImmediate engine actor mode budget sampleState sampleObservation candidates)

        let aggregate = aggregateSamples immediate candidates

        let evaluated =
            match input.Difficulty with
            | CpuDifficulty.Hard
            | CpuDifficulty.Impossible when budget.CanVisit ->
                let top =
                    aggregate
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

                let forwardById = Dictionary<CpuCandidateId, ResizeArray<int>>()

                for sampleIndex in 0 .. samples.Length - 1 do
                    let rootState, rootObservation = samples[sampleIndex]

                    for evaluated in immediate[sampleIndex] do
                        if top.Contains evaluated.Candidate.Id && budget.CanVisit then
                            match
                                tryAdvance
                                    engine
                                    actor
                                    mode
                                    budget
                                    1
                                    rootState
                                    rootObservation
                                    evaluated.Candidate
                            with
                            | ValueSome(immediateScore, next, nextObservation) ->
                                let score =
                                    immediateScore
                                    + CpuForwardSearch.score
                                        engine
                                        actor
                                        mode
                                        budget
                                        1
                                        depthLimit
                                        next
                                        nextObservation

                                match forwardById.TryGetValue evaluated.Candidate.Id with
                                | true, scores -> scores.Add score
                                | _ ->
                                    let scores = ResizeArray<int>()
                                    scores.Add score
                                    forwardById.Add(evaluated.Candidate.Id, scores)
                            | ValueNone -> ()

                aggregate
                |> Array.map (fun evaluated ->
                    match forwardById.TryGetValue evaluated.Candidate.Id with
                    | true, scores when scores.Count = samples.Length ->
                        { evaluated with
                            Score = scores |> Seq.sum |> (fun total -> total / scores.Count) }
                    | _ -> evaluated)
            | _ -> aggregate

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
                { CandidatesConsidered = aggregate.Length
                  NodesVisited = budget.Visited
                  NodeLimit = nodeLimit
                  DepthReached = budget.DepthReached
                  DepthLimit = depthLimit
                  SamplesEvaluated = sampleCount } } }
