namespace Blokemon.App

open System
open Blokemon.App.Contracts
open Blokemon.Game

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
                { Version = CpuPolicyVersion.strategic
                  Difficulty = difficulty
                  Seed = seed
                  DecisionIndex = 0UL
                  Search = CpuSearchConfiguration.strategic }

    let legacy seed decisionIndex =
        { Version = CpuPolicyVersion.legacy
          Difficulty = CpuDifficultyView.Normal
          Seed = seed
          DecisionIndex = decisionIndex
          Search = CpuSearchConfiguration.legacy }

    let isSupportedVersion version =
        version = CpuPolicyVersion.legacy || version = CpuPolicyVersion.strategic

    let isValid (policy: CpuPolicyDocument | null) =
        match policy with
        | null -> false
        | value when isNull (box value.Search) -> false
        | value when value.Version = CpuPolicyVersion.legacy ->
            value.Difficulty = CpuDifficultyView.Normal
            && value.Search = CpuSearchConfiguration.legacy
        | value when value.Version = CpuPolicyVersion.strategic ->
            gameDifficulty value.Difficulty |> Option.isSome
            && value.Search = CpuSearchConfiguration.strategic
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
        if policy.Version = CpuPolicyVersion.legacy then
            context.Cpu.ChooseLegacy(context.Engine, state, actor)
        else
            context.Cpu.Choose(context.Engine, state, actor, input policy).Decision
