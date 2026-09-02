namespace Blokemon.App

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Linq
open System.Text.Json
open System.Threading
open Blokemon.App.Contracts
open Blokemon.App.MatchConflicts
open Blokemon.App.MatchCueProjection
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchReplay
open Blokemon.App.MatchStore
open Blokemon.App.MatchViewProjection
open Blokemon.Core.SetDesign
open Blokemon.Product
open Blokemon.Game

/// Starting a battle: the saved battle is settled first, the deck is validated, the computer
/// answers, and only then is the new document written.
module internal MatchStartFlow =

    let start
        (context: MatchContext)
        (profile: LocalProfile)
        (displayName: string)
        (request: StartMatchRequest)
        (cancellationToken: CancellationToken)
        =
        let catalogue = context.Catalogue
        let documents = context.Documents
        let engine = context.Engine
        let load = load context
        let toView = toView context
        let toPresentation = toPresentation context
        let advanceCpu = advanceCpu context
        let archiveCompletedMatch = archiveCompletedMatch context
        let reconcileStartConflict = reconcileStartConflict context
        let matchSeed = matchSeedFor profile request.CommandId
        // The persisted seed controls Easy's deterministic variation and is part of replay and
        // policy identity. A different seed can select different commands, so replay preserves it.
        let requestedPolicy = MatchCpuPolicy.initial request.Difficulty matchSeed.Value

        task {
            if request.CommandId = Guid.Empty then
                return failed "match.command_id" "Select the action again."
            elif requestedPolicy.IsNone then
                return failed "match.cpu_difficulty" "Select a supported computer difficulty."
            else

                let initialPolicy = requestedPolicy.Value

                let! loaded = load profile cancellationToken

                if not (isNull (box loaded.Error)) then
                    return
                        { View = null
                          Error = loaded.Error
                          Recovery =
                            match loaded.Recovery with
                            | None -> null
                            | Some requirement -> MatchMigration.recoveryView requirement
                          Presentation = null
                          DocumentIdentity = noDocumentProjection }
                else

                    let requestFingerprint = startFingerprint request initialPolicy

                    let existingConflict =
                        match loaded.Match with
                        | null -> None
                        | existing ->
                            if
                                existing.Document.StartCommand.ClientCommandId = request.CommandId
                            then
                                if
                                    String.Equals(
                                        existing.Document.StartCommand.Fingerprint,
                                        requestFingerprint,
                                        StringComparison.Ordinal
                                    )
                                then
                                    Some
                                        { View = toView existing displayName
                                          Error = null
                                          Recovery = null
                                          Presentation = null
                                          DocumentIdentity = documentProjection existing }
                                else
                                    Some(
                                        failed
                                            "match.command_conflict"
                                            "This request conflicts with a saved move. Start the battle again."
                                    )
                            elif
                                existing.Document.ClientCommands
                                |> Seq.exists (fun receipt ->
                                    receipt.ClientCommandId = request.CommandId)
                            then
                                Some(
                                    failed
                                        "match.command_conflict"
                                        "This request conflicts with a saved move. Start the battle again."
                                )
                            elif existing.State.Phase <> MatchPhase.Complete then
                                Some(
                                    failed
                                        "match.active"
                                        "Finish the current battle before you start another battle."
                                )
                            else
                                None

                    match existingConflict with
                    | Some result -> return result
                    | None ->

                        let savedDeck =
                            match DeckId.Create(request.DeckId.ToString "D") with
                            | DomainResult.Succeeded deckId ->
                                match profile.SavedDecks.TryGetValue deckId with
                                | true, deck -> Some deck
                                | _ -> None
                            | DomainResult.Failed _ -> None

                        match savedDeck with
                        | None ->
                            return
                                failed
                                    "match.deck_not_found"
                                    "The selected saved deck no longer exists."
                        | Some deck ->

                            let validation =
                                DeckValidator.Validate
                                    profile
                                    catalogue.Mechanics
                                    (deck.Cards
                                     |> Seq.map (fun card ->
                                         { CardId = card.Key
                                           Quantity = card.Value }))

                            match validation with
                            | DeckValidationResult.Invalid _ ->
                                return
                                    failed
                                        "match.deck_illegal"
                                        "This deck does not follow the current deck rules."
                            | DeckValidationResult.Valid validDeck ->

                                let human = humanPlayer profile

                                let cards =
                                    validDeck.Cards
                                    |> Seq.sortWith (fun left right ->
                                        String.CompareOrdinal(left.Key.Value, right.Key.Value))
                                    |> Seq.collect (fun card ->
                                        Seq.replicate card.Value card.Key.Value)
                                    |> Seq.toArray

                                let cpuDeck =
                                    catalogue.StarterDecks.OpponentFor(
                                        match profile.LatestStarterDeckClaim with
                                        | null -> null
                                        | claim -> claim.Id.Value
                                    )

                                let start =
                                    { MatchId = MatchId(request.CommandId.ToString "D")
                                      Seed = matchSeed
                                      FirstDeck = FrozenDeckSnapshot.Create(human, cards)
                                      SecondDeck =
                                        FrozenDeckSnapshot.Create(
                                            cpuPlayer,
                                            cpuDeck.ExpandedCardIds
                                        ) }

                                match engine.Start start with
                                | MatchStartOutcome.Started(startedState, startedEvents) ->
                                    let commands = List<MatchCommand>()
                                    let events = List<MatchEvent>(startedEvents)

                                    let presentation =
                                        List<PendingPresentation>(
                                            [ { State = startedState
                                                Events = startedEvents } ]
                                        )

                                    let advanced =
                                        advanceCpu
                                            startedState
                                            initialPolicy
                                            commands
                                            events
                                            presentation

                                    if not (isNull (box advanced.Error)) then
                                        return
                                            { View = null
                                              Error = advanced.Error
                                              Recovery = null
                                              Presentation = null
                                              DocumentIdentity = noDocumentProjection }
                                    else

                                        let document =
                                            { SchemaVersion = matchSchemaVersion
                                              AuthorityVersion = catalogue.Mechanics.ManifestVersion
                                              StartCommand =
                                                { ClientCommandId = request.CommandId
                                                  DeckId = request.DeckId
                                                  Fingerprint = requestFingerprint
                                                  StartRequestFingerprint =
                                                    gameStartFingerprint start initialPolicy
                                                  CpuPolicy = initialPolicy }
                                              Start = start
                                              CpuPolicy = advanced.Policy
                                              Commands = ImmutableArray.CreateRange commands
                                              ClientCommands =
                                                ImmutableArray<MatchClientCommandReceipt>.Empty }

                                        let! historyError =
                                            task {
                                                match loaded.Match with
                                                | NonNull completed when
                                                    completed.State.Phase = MatchPhase.Complete
                                                    ->
                                                    return!
                                                        archiveCompletedMatch
                                                            profile
                                                            completed
                                                            cancellationToken
                                                | Null ->
                                                    let! history =
                                                        historyRecovery
                                                            context
                                                            profile
                                                            cancellationToken

                                                    return
                                                        match history with
                                                        | Ok None -> MatchArchiveOutcome.Ready
                                                        | Ok(Some requirement) ->
                                                            MatchArchiveOutcome.RecoveryRequired
                                                                requirement
                                                        | Error error ->
                                                            MatchArchiveOutcome.Failed error
                                                | _ -> return MatchArchiveOutcome.Ready
                                            }

                                        match historyError with
                                        | MatchArchiveOutcome.RecoveryRequired requirement ->
                                            return
                                                { View =
                                                    loaded.Match
                                                    |> Option.ofObj
                                                    |> Option.map (fun value ->
                                                        toView value displayName)
                                                    |> Option.toObj
                                                  Error = MatchMigration.recoveryError requirement
                                                  Recovery = MatchMigration.recoveryView requirement
                                                  Presentation = null
                                                  DocumentIdentity =
                                                    match loaded.Match with
                                                    | null -> noDocumentProjection
                                                    | value -> documentProjection value }
                                        | MatchArchiveOutcome.Failed error ->
                                            return
                                                { View = null
                                                  Error = error
                                                  Recovery = null
                                                  Presentation = null
                                                  DocumentIdentity = noDocumentProjection }
                                        | MatchArchiveOutcome.Ready ->

                                            let json =
                                                JsonSerializer.Serialize(
                                                    document,
                                                    MatchJson.Options
                                                )

                                            let! write =
                                                match loaded.Match with
                                                | null ->
                                                    documents.Create(
                                                        matchKey,
                                                        json,
                                                        cancellationToken
                                                    )
                                                | existing ->
                                                    documents.Update(
                                                        matchKey,
                                                        existing.DocumentRevision,
                                                        json,
                                                        cancellationToken
                                                    )

                                            match write with
                                            | :? DocumentWriteResult.Written as written ->
                                                let committed =
                                                    { DocumentRevision = written.Revision
                                                      DocumentContentIdentity =
                                                        DocumentIdentity.ofText json
                                                      Document = document
                                                      State = advanced.State
                                                      Events = ImmutableArray.CreateRange events }

                                                context.Cached <- committed

                                                return
                                                    { View = toView committed displayName
                                                      Error = null
                                                      Recovery = null
                                                      Presentation =
                                                        toPresentation
                                                            document
                                                            displayName
                                                            presentation
                                                      DocumentIdentity =
                                                        documentProjection committed }
                                            | _ ->
                                                return!
                                                    reconcileStartConflict
                                                        profile
                                                        displayName
                                                        request.CommandId
                                                        requestFingerprint
                                                        cancellationToken
                                | _ ->
                                    return
                                        failed
                                            "match.deck_illegal"
                                            "The game cannot start with this deck."
        }
