namespace Blokemon.App

open System
open System.Text.Json
open System.Threading.Tasks
open Blokemon.App.Catalogue
open Blokemon.App.MatchFailures
open Blokemon.Game
open Blokemon.Cpu

/// The computer's decision for the state a saved battle stands at, in the two places it is made:
/// on the calling thread, and in another runtime that is handed nothing but the document.
module ComputerDecisions =

    /// The id of the candidate the policy selects, or null when the computer has no legal move.
    let internal choose
        (engine: MatchEngine)
        (cpu: DeterministicCpu)
        (state: MatchState)
        (policy: CpuPolicyDocument)
        : string | null =
        let decision = cpu.Choose(engine, state, cpuPlayer, MatchCpuPolicy.input policy)

        match decision.Decision, decision.Evidence.Candidate with
        | CpuDecision.Selected _, ValueSome candidate -> candidate.Value
        | _ -> null

    /// The decider that thinks on the thread that asks.
    let inProcess (engine: MatchEngine) (cpu: DeterministicCpu) =
        { new IComputerDecider with
            member _.Decide(document, state, _) =
                Task.FromResult(choose engine cpu state document.CpuPolicy) }

    /// The document as another runtime reads it.
    let documentJson (document: MatchDocument) =
        JsonSerializer.Serialize(document, MatchJson.Options)

/// One computer for a runtime that holds no match of its own: it builds the engine once and
/// keeps the state it last rebuilt, so the later decisions of a turn replay only the commands
/// added since the one before. The document is trusted as the process that wrote it verified
/// it; what comes back is a candidate id the writer's own engine turns into a command.
[<Sealed>]
type ComputerTurn(catalogue: BlokemonCatalogue) =

    let engine = MatchEngine(catalogue.Mechanics)
    let cpu = DeterministicCpu()
    let mutable cached: (MatchId * CommandId * int * MatchState) voption = ValueNone

    let started (document: MatchDocument) =
        match engine.Start document.Start with
        | MatchStartOutcome.Started(state, _) -> state
        | MatchStartOutcome.Rejected _ -> invalidOp "The saved battle does not start."

    let rebuild (document: MatchDocument) =
        let commands = document.Commands

        let applied, initial =
            match cached with
            | ValueSome(matchId, lastCommand, applied, state) when
                matchId = document.Start.MatchId
                && applied <= commands.Length
                && commands[applied - 1].Id = lastCommand
                ->
                applied, state
            | _ -> 0, started document

        let mutable state = initial

        for index in applied .. commands.Length - 1 do
            match engine.Apply(state, commands[index]) with
            | CommandOutcome.Applied(next, _) -> state <- next
            | CommandOutcome.Rejected _ -> invalidOp "The saved battle does not replay."

        if commands.Length > 0 then
            cached <-
                ValueSome(
                    document.Start.MatchId,
                    commands[commands.Length - 1].Id,
                    commands.Length,
                    state
                )

        state

    /// The rules this computer plays by; a caller on another runtime checks them against its own
    /// before it trusts a decision.
    member _.AuthorityVersion = catalogue.Mechanics.ManifestVersion

    member _.PolicyVersion = CpuPolicyVersion.active

    /// The id of the candidate the policy selects for the document, or null when the computer has
    /// no legal move.
    member _.Decide(documentJson: string) : string | null =
        let document =
            JsonSerializer.Deserialize<MatchDocument>(documentJson, MatchJson.Options)

        match document with
        | null -> invalidOp "The saved battle is not a document."
        | document -> ComputerDecisions.choose engine cpu (rebuild document) document.CpuPolicy
