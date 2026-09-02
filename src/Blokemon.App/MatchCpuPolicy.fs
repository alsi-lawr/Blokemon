namespace Blokemon.App

open System
open Blokemon.App.Contracts
open Blokemon.Game
open Blokemon.Cpu

module internal MatchCpuPolicy =

    let private gameDifficulty difficulty =
        match difficulty with
        | CpuDifficultyView.Easy -> Some CpuDifficulty.Easy
        | CpuDifficultyView.Normal -> Some CpuDifficulty.Normal
        | CpuDifficultyView.Hard -> Some CpuDifficulty.Hard
        | CpuDifficultyView.Impossible -> Some CpuDifficulty.Impossible
        | _ -> None

    let difficultyView difficulty =
        match difficulty with
        | CpuDifficulty.Easy -> CpuDifficultyView.Easy
        | CpuDifficulty.Normal -> CpuDifficultyView.Normal
        | CpuDifficulty.Hard -> CpuDifficultyView.Hard
        | CpuDifficulty.Impossible -> CpuDifficultyView.Impossible

    let initial difficulty seed =
        match gameDifficulty difficulty with
        | None -> None
        | Some _ ->
            Some
                { Version = CpuPolicyVersion.active
                  Difficulty = difficulty
                  Seed = seed
                  DecisionIndex = 0UL
                  Search = CpuSearchConfiguration.active }

    let isSupportedVersion version = version = CpuPolicyVersion.active

    let isValid (policy: CpuPolicyDocument | null) =
        match policy with
        | null -> false
        | value when isNull (box value.Search) -> false
        | value when value.Version = CpuPolicyVersion.active ->
            gameDifficulty value.Difficulty |> Option.isSome
            && value.Search = CpuSearchConfiguration.active
        | _ -> false

    let input (policy: CpuPolicyDocument) =
        { Difficulty = gameDifficulty policy.Difficulty |> Option.get
          Seed = policy.Seed
          DecisionIndex = policy.DecisionIndex }

    let tryAdvance (policy: CpuPolicyDocument) =
        if policy.DecisionIndex = UInt64.MaxValue then
            None
        else
            Some
                { policy with
                    DecisionIndex = policy.DecisionIndex + 1UL }

    let choose
        (context: MatchContext)
        (state: MatchState)
        (actor: PlayerId)
        (policy: CpuPolicyDocument)
        =
        context.Cpu.Choose(context.Engine, state, actor, input policy).Decision
