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
      ImmediateNodeLimit: int
      SearchNodeLimit: int
      SearchDepthLimit: int
      BeamWidth: int }

[<RequireQualifiedAccess>]
module CpuSearchConfiguration =

    let active =
        { RootCandidateLimit = 256
          ImmediateNodeLimit = 256
          SearchNodeLimit = 512
          SearchDepthLimit = 4
          BeamWidth = 8 }

[<RequireQualifiedAccess>]
module CpuPolicyVersion =

    let active = 3

module internal CpuPolicyLimits =

    let rootCandidateLimit = CpuSearchConfiguration.active.RootCandidateLimit
    let immediateNodeLimit = CpuSearchConfiguration.active.ImmediateNodeLimit
    let searchNodeLimit = CpuSearchConfiguration.active.SearchNodeLimit
    let searchDepthLimit = CpuSearchConfiguration.active.SearchDepthLimit
    let beamWidth = CpuSearchConfiguration.active.BeamWidth

[<RequireQualifiedAccess>]
type CpuDecision =
    | Selected of action: LegalAction
    | NoLegalAction

type CpuWorkEvidence =
    { CandidatesConsidered: int
      NodesVisited: int
      NodeLimit: int
      DepthReached: int
      DepthLimit: int }

type CpuDecisionEvidence =
    { Input: CpuPolicyInput
      Candidate: CpuCandidateId voption
      Score: int voption
      Work: CpuWorkEvidence }

type CpuPolicyDecision =
    { Decision: CpuDecision
      Evidence: CpuDecisionEvidence }
