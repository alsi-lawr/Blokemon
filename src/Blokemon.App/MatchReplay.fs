namespace Blokemon.App

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Linq
open System.Text.Json
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.App.MatchCommandTranslation
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchPayloads
open Blokemon.Product
open Blokemon.Game
open Blokemon.Cpu

/// The computer's turn, and the verified replay that turns a stored document back into a state.
/// A document is trusted only after every command in it replays to the same command.
module internal MatchReplay =

    let advanceCpu
        (context: MatchContext)
        (initial: MatchState)
        (initialPolicy: CpuPolicyDocument)
        (commands: List<MatchCommand>)
        (events: List<MatchEvent>)
        (presentation: List<PendingPresentation>)
        : CpuAdvance =
        let engine = context.Engine

        let mutable state = initial
        let mutable policy = initialPolicy
        let mutable settled: CpuAdvance | null = null
        let mutable count = 0

        while isNull (box settled) && count < maximumCpuCommandsPerRequest do
            match MatchCpuPolicy.choose context state cpuPlayer policy with
            | CpuDecision.Selected action ->
                match engine.Apply(state, action.Command) with
                | CommandOutcome.Applied(appliedState, appliedEvents) ->
                    match MatchCpuPolicy.tryAdvance policy with
                    | None ->
                        settled <-
                            { State = state
                              Policy = policy
                              Error = invalidReplayError () }
                    | Some advancedPolicy ->
                        commands.Add action.Command
                        events.AddRange appliedEvents

                        presentation.Add
                            { State = appliedState
                              Events = appliedEvents }

                        state <- appliedState
                        policy <- advancedPolicy
                | _ ->
                    settled <-
                        { State = state
                          Policy = policy
                          Error =
                            ApiError("match.cpu_rejected", "The computer made an invalid move.") }
            | _ ->
                settled <-
                    { State = state
                      Policy = policy
                      Error = null }

            count <- count + 1

        match settled with
        | null ->
            match MatchCpuPolicy.choose context state cpuPlayer policy with
            | CpuDecision.Selected _ ->
                { State = state
                  Policy = policy
                  Error = ApiError("match.cpu_limit", "The computer could not complete its turn.") }
            | _ ->
                { State = state
                  Policy = policy
                  Error = null }
        | finished -> finished

    let validateDocument
        (catalogue: BlokemonCatalogue)
        (profile: LocalProfile)
        (document: MatchDocument)
        : ApiError | null =
        if
            not (
                String.Equals(
                    document.AuthorityVersion,
                    catalogue.Mechanics.ManifestVersion,
                    StringComparison.Ordinal
                )
            )
        then
            ApiError(
                "match.authority_changed",
                "The card rules changed after this battle started. Start a new battle."
            )
        elif isMissing document.StartCommand.CpuPolicy || isMissing document.CpuPolicy then
            invalidReplayError ()
        elif
            not (MatchCpuPolicy.isSupportedVersion document.StartCommand.CpuPolicy.Version)
            || not (MatchCpuPolicy.isSupportedVersion document.CpuPolicy.Version)
        then
            ApiError(
                "match.cpu_policy_version",
                "This saved battle uses an unsupported computer policy. No data changed."
            )
        elif
            not (MatchCpuPolicy.isValid document.StartCommand.CpuPolicy)
            || not (MatchCpuPolicy.isValid document.CpuPolicy)
            || document.StartCommand.CpuPolicy.DecisionIndex <> 0UL
            || document.StartCommand.CpuPolicy.Version <> document.CpuPolicy.Version
            || document.StartCommand.CpuPolicy.Difficulty <> document.CpuPolicy.Difficulty
            || document.StartCommand.CpuPolicy.Seed <> document.CpuPolicy.Seed
            || document.StartCommand.CpuPolicy.Search <> document.CpuPolicy.Search
            || document.StartCommand.CpuPolicy.Seed <> document.Start.Seed.Value
        then
            invalidReplayError ()
        elif
            isMissing document.Start.FirstDeck
            || isMissing document.Start.SecondDeck
            || document.StartCommand.ClientCommandId = Guid.Empty
            || document.StartCommand.DeckId = Guid.Empty
            || String.IsNullOrWhiteSpace document.StartCommand.Fingerprint
            || document.StartCommand.Fingerprint
               <> startFingerprint
                   (StartMatchRequest(
                       document.StartCommand.ClientCommandId,
                       document.StartCommand.DeckId,
                       document.StartCommand.CpuPolicy.Difficulty
                   ))
                   document.StartCommand.CpuPolicy
            || String.IsNullOrWhiteSpace document.StartCommand.StartRequestFingerprint
            || document.StartCommand.StartRequestFingerprint
               <> gameStartFingerprint document.Start document.StartCommand.CpuPolicy
            || document.Start.MatchId.Value
               <> document.StartCommand.ClientCommandId.ToString "D"
            || document.Start.Seed
               <> matchSeedFor profile document.StartCommand.ClientCommandId
            || document.Start.FirstDeck.Owner <> humanPlayer profile
            || document.Start.SecondDeck.Owner <> cpuPlayer
            || not (startIsStructurallyValid document.Start)
            || document.Commands
               |> Seq.exists (fun command -> not (commandIsStructurallyValid command))
        then
            invalidReplayError ()
        elif document.ClientCommands |> Seq.exists isMissing then
            invalidReplayError ()
        else
            let duplicateClientCommands =
                document.ClientCommands
                |> Seq.countBy _.ClientCommandId
                |> Seq.exists (fun (_, count) -> count > 1)

            let duplicateAppliedCommands =
                document.ClientCommands
                |> Seq.countBy _.AppliedCommand
                |> Seq.exists (fun (_, count) -> count > 1)

            if
                duplicateClientCommands
                || duplicateAppliedCommands
                || document.ClientCommands
                   |> Seq.exists (fun receipt ->
                       receipt.ClientCommandId = Guid.Empty
                       || receipt.ClientCommandId = document.StartCommand.ClientCommandId
                       || String.IsNullOrWhiteSpace receipt.Fingerprint
                       || String.IsNullOrWhiteSpace receipt.RequestPayload
                       || fingerprint receipt.RequestPayload <> receipt.Fingerprint
                       || receipt.AppliedCommand <> CommandId $"client:{receipt.ClientCommandId:D}")
            then
                invalidReplayError ()
            else
                null

    let replayDocument
        (context: MatchContext)
        (profile: LocalProfile)
        (documentRevision: int64)
        (document: MatchDocument)
        : MatchLoad =
        let engine = context.Engine
        let validateDocument = validateDocument context.Catalogue

        if isMissing document.StartCommand || isMissing document.Start then
            invalidReplay ()
        elif document.SchemaVersion <> matchSchemaVersion then
            invalidDocument
                "match.document_version"
                "This saved battle uses an unsupported version. No data changed."
        else

            match validateDocument profile document with
            | null ->
                match engine.Start document.Start with
                | MatchStartOutcome.Started(startedState, startedEvents) ->
                    let human = humanPlayer profile

                    let receipts =
                        document.ClientCommands.ToDictionary(fun receipt -> receipt.AppliedCommand)

                    let mutable state = startedState
                    let mutable policy = document.StartCommand.CpuPolicy
                    let events = List<MatchEvent>(startedEvents)
                    let mutable pendingReceipt: MatchClientCommandReceipt | null = null
                    let mutable rejected = false
                    let mutable index = 0
                    let commands = document.Commands

                    while not rejected && index < commands.Length do
                        let command = commands[index]

                        if command.Actor = cpuPlayer then
                            match MatchCpuPolicy.choose context state cpuPlayer policy with
                            | CpuDecision.Selected action when action.Command = command ->
                                match MatchCpuPolicy.tryAdvance policy with
                                | Some advancedPolicy -> policy <- advancedPolicy
                                | None -> rejected <- true
                            | _ -> rejected <- true
                        elif command.Actor = human then
                            if
                                match pendingReceipt with
                                | NonNull pending -> pending.ResultRevision <> state.Revision
                                | Null -> false
                            then
                                rejected <- true
                            else
                                match receipts.TryGetValue command.Id with
                                | true, receipt when isClientCommand command.Id ->
                                    let payload = readActionPayload receipt.RequestPayload

                                    match payload with
                                    | null -> rejected <- true
                                    | value when
                                        value.MatchId.ToString "D" <> state.Id.Value
                                        || value.ExpectedRevision <> state.Revision.Value
                                        || isMissing value.Choices
                                        ->
                                        rejected <- true
                                    | value ->
                                        match
                                            engine
                                                .GetLegalActions(state, human)
                                                .SingleOrDefault(fun candidate ->
                                                    String.Equals(
                                                        candidate.StableKey,
                                                        value.ActionId,
                                                        StringComparison.Ordinal
                                                    ))
                                        with
                                        | null -> rejected <- true
                                        | action ->
                                            let materialized =
                                                materializeHumanCommand
                                                    action
                                                    state
                                                    human
                                                    receipt.ClientCommandId
                                                    value.Choices

                                            if
                                                not (isNull (box materialized.Error))
                                                || materialized.Command <> command
                                            then
                                                rejected <- true
                                            else
                                                pendingReceipt <- receipt
                                | _ -> rejected <- true
                        else
                            rejected <- true

                        if not rejected then
                            match engine.Apply(state, command) with
                            | CommandOutcome.Applied(appliedState, appliedEvents) ->
                                state <- appliedState
                                events.AddRange appliedEvents
                            | _ -> rejected <- true

                        index <- index + 1

                    if rejected then
                        invalidReplay ()
                    else

                        let cpuStillMoves =
                            match MatchCpuPolicy.choose context state cpuPlayer policy with
                            | CpuDecision.Selected _ -> true
                            | _ -> false

                        if
                            policy <> document.CpuPolicy
                            || cpuStillMoves
                            || (match pendingReceipt with
                                | NonNull pending -> pending.ResultRevision <> state.Revision
                                | Null -> false)
                        then
                            invalidReplay ()
                        elif
                            document.ClientCommands
                            |> Seq.exists (fun receipt ->
                                receipt.ResultRevision.Value > state.Revision.Value
                                || not (
                                    document.Commands
                                    |> Seq.exists (fun command ->
                                        command.Id = receipt.AppliedCommand)
                                ))
                        then
                            invalidReplay ()
                        else
                            let documentContentIdentity =
                                JsonSerializer.Serialize(document, MatchJson.Options)
                                |> DocumentIdentity.ofText

                            { Match =
                                { DocumentRevision = documentRevision
                                  DocumentContentIdentity = documentContentIdentity
                                  Document = document
                                  State = state
                                  Events = ImmutableArray.CreateRange events }
                              Error = null
                              Recovery = None }
                | _ -> invalidReplay ()
            | validationError ->
                { Match = null
                  Error = validationError
                  Recovery = None }
