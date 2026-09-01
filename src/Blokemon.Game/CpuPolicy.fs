namespace Blokemon.Game

[<RequireQualifiedAccess>]
type CpuDifficulty =
    | Easy
    | Normal
    | Hard
    | Impossible

type CpuPolicyInput =
    { Difficulty: CpuDifficulty
      Seed: uint64
      DecisionIndex: uint64 }

[<RequireQualifiedAccess>]
module CpuPolicyInput =

    let normal =
        { Difficulty = CpuDifficulty.Normal
          Seed = 0UL
          DecisionIndex = 0UL }

module internal CpuPolicyLimits =

    let rootCandidateLimit = 256
    let normalNodeLimit = 256
    let hardNodeLimit = 512
    let hardDepthLimit = 4
    let hardSamples = 2
    let beamWidth = 8

[<RequireQualifiedAccess>]
type CpuDecision =
    | Selected of action: LegalAction
    | NoLegalAction

type CpuWorkEvidence =
    { CandidatesConsidered: int
      NodesVisited: int
      NodeLimit: int
      DepthReached: int
      DepthLimit: int
      SamplesEvaluated: int }

type CpuDecisionEvidence =
    { Input: CpuPolicyInput
      Candidate: CpuCandidateId voption
      Score: int voption
      Work: CpuWorkEvidence }

type CpuPolicyDecision =
    { Decision: CpuDecision
      Evidence: CpuDecisionEvidence }
