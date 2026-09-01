namespace Blokemon.Cpu

open Blokemon.Game

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

type CpuSearchConfiguration =
    { RootCandidateLimit: int
      NormalNodeLimit: int
      HardNodeLimit: int
      HardDepthLimit: int
      HardSamples: int
      BeamWidth: int }

[<RequireQualifiedAccess>]
module CpuSearchConfiguration =

    let legacy =
        { RootCandidateLimit = 0
          NormalNodeLimit = 0
          HardNodeLimit = 0
          HardDepthLimit = 0
          HardSamples = 0
          BeamWidth = 0 }

    let strategic =
        { RootCandidateLimit = 256
          NormalNodeLimit = 256
          HardNodeLimit = 512
          HardDepthLimit = 4
          HardSamples = 2
          BeamWidth = 8 }

[<RequireQualifiedAccess>]
module CpuPolicyVersion =

    let legacy = 1
    let strategic = 2

module internal CpuPolicyLimits =

    let rootCandidateLimit = CpuSearchConfiguration.strategic.RootCandidateLimit
    let normalNodeLimit = CpuSearchConfiguration.strategic.NormalNodeLimit
    let hardNodeLimit = CpuSearchConfiguration.strategic.HardNodeLimit
    let hardDepthLimit = CpuSearchConfiguration.strategic.HardDepthLimit
    let hardSamples = CpuSearchConfiguration.strategic.HardSamples
    let beamWidth = CpuSearchConfiguration.strategic.BeamWidth

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
