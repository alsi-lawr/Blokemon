namespace Blokemon.Game.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Game
open FsUnit
open TUnit.Core

[<RequireQualifiedAccess>]
type private CpuSeat =
    | First
    | Second

type private CpuMatchResult =
    { Commands: MatchCommand list
      Events: MatchEvent list
      Winner: PlayerId
      FinalState: MatchState }

[<AutoOpen>]
module private CpuMatchVerificationScenarios =

    let maximumMatchCommands = 256
    let maximumMatchRound = 64
    let fastFireDeckName = "simple-fire-v1"

    let fastFireDeck owner =
        Seq.concat
            [ Seq.replicate 4 "BLK-004"
              Seq.replicate 4 "BLK-126"
              Seq.replicate 52 "VIM-CURRY" ]
        |> fun cards -> FrozenDeckSnapshot.Create(owner, cards)

    let subjectFor seat =
        match seat with
        | CpuSeat.First -> MatchScenario.FirstPlayer
        | CpuSeat.Second -> MatchScenario.SecondPlayer

    let seatName seat =
        match seat with
        | CpuSeat.First -> "first"
        | CpuSeat.Second -> "second"

    let actorFor (state: MatchState) =
        match state.Phase with
        | MatchPhase.MulliganBonus ->
            state.Players |> Seq.find (fun player -> not player.MulliganBonusChosen) |> _.Id
        | MatchPhase.OpeningPlacement ->
            state.Players |> Seq.find (fun player -> not player.OpeningChosen) |> _.Id
        | MatchPhase.BonusPlacement ->
            state.Players
            |> Seq.find (fun player -> not player.BonusPlacementChosen)
            |> _.Id
        | MatchPhase.Playing -> state.ActivePlayer
        | MatchPhase.AwaitingEffectChoice -> state.PendingEffect.Value.Chooser
        | MatchPhase.AwaitingTriggerChoice ->
            match state.PendingKnockout with
            | ValueSome pending -> pending.Chooser
            | ValueNone -> state.PendingBarChits[0].Player
        | MatchPhase.AwaitingReplacement -> state.ReplacementPlayer.Value
        | MatchPhase.Complete -> failwith "A complete match has no actor."
        | phase -> failwith $"Unsupported match phase {phase}."

    let progressState (state: MatchState) =
        { state with
            Revision = MatchRevision 0L
            LastEventSequence = 0L
            ProcessedCommands = ImmutableArray<_>.Empty }

    let candidateName (candidate: CpuCandidateId voption) =
        match candidate with
        | ValueSome value -> value.Value
        | ValueNone -> "none"

    let diagnostic difficulty seat seed revision candidate =
        $"deck={fastFireDeckName}; authority={MatchScenario.Authority.ManifestVersion}; seat={seatName seat}; seed={seed}; policy={CpuPolicyVersion.strategic}; difficulty={difficulty}; revision={revision}; candidate={candidateName candidate}"

    let runMatch difficulty seat seed =
        let engine = MatchScenario.Engine()
        let cpu = DeterministicCpu()
        let subject = subjectFor seat

        let request =
            { MatchId = MatchId $"cpu-corpus:{difficulty}:{seatName seat}:{seed}"
              Seed = MatchSeed seed
              FirstDeck = fastFireDeck MatchScenario.FirstPlayer
              SecondDeck = fastFireDeck MatchScenario.SecondPlayer }

        let mutable state, startedEvents =
            match engine.Start request with
            | MatchStartOutcome.Started(started, events) -> started, events
            | MatchStartOutcome.Rejected issues ->
                failwith
                    $"deck={fastFireDeckName}; seat={seatName seat}; seed={seed}; policy={CpuPolicyVersion.strategic}; start rejected with {issues.Length} issues"

        let decisionIndices = Dictionary<PlayerId, uint64>()
        decisionIndices[MatchScenario.FirstPlayer] <- 0UL
        decisionIndices[MatchScenario.SecondPlayer] <- 0UL

        let commands = ResizeArray<MatchCommand>()
        let events = ResizeArray<MatchEvent>(startedEvents)
        let seen = HashSet<MatchState>()
        seen.Add(progressState state) |> ignore

        while state.Phase <> MatchPhase.Complete do
            if commands.Count >= maximumMatchCommands then
                failwith (
                    diagnostic difficulty seat seed state.Revision ValueNone
                    + $"; reached {maximumMatchCommands} commands"
                )

            if state.RoundNumber > maximumMatchRound then
                failwith (
                    diagnostic difficulty seat seed state.Revision ValueNone
                    + $"; exceeded round {maximumMatchRound}"
                )

            let actor = actorFor state

            let input =
                { Difficulty = if actor = subject then difficulty else CpuDifficulty.Normal
                  Seed = seed
                  DecisionIndex = decisionIndices[actor] }

            let selected = cpu.Choose(engine, state, actor, input)

            let context =
                diagnostic difficulty seat seed state.Revision selected.Evidence.Candidate

            if
                selected.Evidence.Work.CandidatesConsidered > CpuSearchConfiguration.strategic.RootCandidateLimit
                || selected.Evidence.Work.NodesVisited > selected.Evidence.Work.NodeLimit
                || selected.Evidence.Work.DepthReached > selected.Evidence.Work.DepthLimit
            then
                failwith $"{context}; exceeded deterministic work budget"

            match selected.Evidence.Candidate, selected.Decision with
            | ValueSome candidate, CpuDecision.Selected action ->
                match engine.TryMaterializeCpuCommand(state, actor, candidate) with
                | ValueSome command when command = action.Command -> ()
                | _ -> failwith $"{context}; selected command was not engine-issued"

                match engine.Apply(state, action.Command) with
                | CommandOutcome.Rejected(_, rejection) ->
                    failwith $"{context}; engine rejected its command with {rejection.Code}"
                | CommandOutcome.Applied(next, appliedEvents) ->
                    commands.Add action.Command
                    events.AddRange appliedEvents
                    decisionIndices[actor] <- decisionIndices[actor] + 1UL

                    if next.Phase <> MatchPhase.Complete && not (seen.Add(progressState next)) then
                        failwith $"{context}; repeated a previously reached semantic state"

                    state <- next
            | _ -> failwith $"{context}; CPU left a live match without a legal command"

        let context = diagnostic difficulty seat seed state.Revision ValueNone

        if
            commands.Count >= maximumMatchCommands
            || state.Winner.IsNone
            || state.PendingEffect.IsSome
            || state.PendingKnockout.IsSome
            || not state.PendingBarChits.IsEmpty
            || state.ReplacementPlayer.IsSome
            || state.PendingRoundEnd
        then
            failwith $"{context}; match completed with an unresolved result or choice"

        { Commands = commands |> Seq.toList
          Events = events |> Seq.toList
          Winner = state.Winner.Value
          FinalState = state }

    let assertFullMatch difficulty seat seed =
        let first = runMatch difficulty seat seed
        let repeated = runMatch difficulty seat seed
        let context = diagnostic difficulty seat seed first.FinalState.Revision ValueNone

        if repeated.Commands <> first.Commands then
            failwith $"{context}; repeated command log differed"

        if repeated.Events <> first.Events then
            failwith $"{context}; repeated event log differed"

        if repeated.Winner <> first.Winner then
            failwith $"{context}; repeated winner differed"

        if repeated.FinalState <> first.FinalState then
            failwith $"{context}; repeated final state differed"

    let policyChoice difficulty seed (state: MatchState) =
        let decision =
            DeterministicCpu()
                .Choose(
                    MatchScenario.Engine(),
                    state,
                    MatchScenario.FirstPlayer,
                    { Difficulty = difficulty
                      Seed = seed
                      DecisionIndex = 0UL }
                )

        match decision.Evidence.Candidate, decision.Decision with
        | ValueSome candidate, CpuDecision.Selected action -> candidate, action
        | _ -> failwith $"{difficulty} supplied no legal corpus action at {state.Revision.Value}."

    let applyPolicyChoice difficulty seed state =
        let engine = MatchScenario.Engine()
        let candidate, action = policyChoice difficulty seed state

        engine.TryMaterializeCpuCommand(state, MatchScenario.FirstPlayer, candidate)
        |> should equal (ValueSome action.Command)

        MatchScenario.Applied(engine.Apply(state, action.Command))

    let benchDevelopmentState () =
        let better =
            MatchScenario.PlainCard
                "better-basic"
                "BLK-001"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let plausible =
            MatchScenario.PlainCard
                "plausible-basic"
                "BLK-007"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        MatchScenario.BattleState "BLK-004" "BLK-001" [] 3601UL
        |> fun state -> MatchScenario.WithCards state [ better; plausible ]

    let knockoutState () =
        let original =
            MatchScenario.BattleState "BLK-004" "BLK-004" [ "VIM-CURRY"; "VIM-SOBER" ] 3602UL

        let defender =
            { original.Card(CardInstanceId "defender") with
                Damage = 20 }

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-007"
                MatchScenario.SecondPlayer
                CardZone.Booth
                0

        let barChit =
            MatchScenario.PlainCard
                "strength-bar-chit"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        MatchScenario.WithCards original [ defender; defenderBench; barChit ]
        |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1

    // The fixed score uses only engine outcomes: printed durability plus maximum printed damage
    // for the developed Blokemon, and 1000 points per Bar Chit actually taken. Policy scores are
    // deliberately excluded.
    let strengthScore difficulty =
        let development = applyPolicyChoice difficulty 0UL (benchDevelopmentState ())

        let developed =
            development.CardsIn(MatchScenario.FirstPlayer, CardZone.Booth)
            |> Seq.find (fun card ->
                card.Id = CardInstanceId "better-basic"
                || card.Id = CardInstanceId "plausible-basic")

        let printed =
            MatchScenario.Authority.Collectibles
            |> Seq.find (fun card -> card.Id = developed.MechanicalId.Value)

        let developmentScore =
            printed.StayingPower + (printed.Attacks |> Seq.map _.PrintedDamage |> Seq.max)

        let beforeKnockout = knockoutState ()
        let afterKnockout = applyPolicyChoice difficulty 0UL beforeKnockout

        let barChitsTaken =
            (beforeKnockout.Player MatchScenario.FirstPlayer).BarChitsRemaining
            - (afterKnockout.Player MatchScenario.FirstPlayer).BarChitsRemaining

        developmentScore + barChitsTaken * 1000

    let assertUsefulWildfire difficulty =
        let state = MatchScenario.BattleState "BLK-146" "BLK-004" [ "VIM-CURRY" ] 3603UL

        let candidate, action = policyChoice difficulty 0UL state

        MatchScenario.Engine().TryMaterializeCpuCommand(state, MatchScenario.FirstPlayer, candidate)
        |> should equal (ValueSome action.Command)

        match action.Command.Action with
        | MatchAction.Attack(_, effect) -> effect |> should equal (EffectId "BLK-146-B01")
        | other -> failwith $"{difficulty} selected {other} instead of useful Wildfire."

        action.Command.Choices
        |> Seq.choose (function
            | EffectChoice.Cards(_, cards) -> Some(cards |> Seq.toList)
            | _ -> None)
        |> Seq.exactlyOne
        |> should equal [ CardInstanceId "vim-0" ]

        let opponentTop =
            state.CardsIn(MatchScenario.SecondPlayer, CardZone.Stack) |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, action.Command))

        (applied.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.EmptiesTray
        (applied.Card opponentTop.Id).Zone |> should equal CardZone.EmptiesTray


type CpuMatchVerificationTests() =

    [<Test>]
    member _.``easy should complete and replay bounded matches from both seats``() =
        assertFullMatch CpuDifficulty.Easy CpuSeat.First 725198UL
        assertFullMatch CpuDifficulty.Easy CpuSeat.Second 725198UL

    [<Test>]
    member _.``normal should complete and replay bounded matches from both seats``() =
        assertFullMatch CpuDifficulty.Normal CpuSeat.First 725198UL
        assertFullMatch CpuDifficulty.Normal CpuSeat.Second 725198UL

    [<Test>]
    member _.``hard should complete and replay bounded matches from both seats``() =
        assertFullMatch CpuDifficulty.Hard CpuSeat.First 725198UL
        assertFullMatch CpuDifficulty.Hard CpuSeat.Second 725198UL

    [<Test>]
    member _.``impossible should complete and replay bounded matches from both seats``() =
        assertFullMatch CpuDifficulty.Impossible CpuSeat.First 725198UL
        assertFullMatch CpuDifficulty.Impossible CpuSeat.Second 725198UL

    [<Test>]
    member _.``every difficulty should make Wildfire discard Energy instead of doing nothing``() =
        assertUsefulWildfire CpuDifficulty.Easy
        assertUsefulWildfire CpuDifficulty.Normal
        assertUsefulWildfire CpuDifficulty.Hard
        assertUsefulWildfire CpuDifficulty.Impossible

    [<Test>]
    member _.``fixed semantic outcomes should preserve difficulty strength ordering``() =
        let easy = strengthScore CpuDifficulty.Easy
        let normal = strengthScore CpuDifficulty.Normal
        let hard = strengthScore CpuDifficulty.Hard
        let impossible = strengthScore CpuDifficulty.Impossible

        normal |> should be (greaterThan easy)
        hard |> should be (greaterThanOrEqualTo normal)
        impossible |> should be (greaterThanOrEqualTo hard)
