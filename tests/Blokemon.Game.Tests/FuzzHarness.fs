namespace Blokemon.Game.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Blokemon.App
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.Product
open FsUnit
open TUnit.Core

module internal FuzzHarness =

    type RulebookClause =
        { Id: string
          Group: string
          AcceptedAssertion: string
          Heading: string
          Lines: string
          Rule: string
          Check: string }

    module Clauses =

        let authorityInventory =
            { Id = "TR-AUTHORITY-INVENTORY-7-9"
              Group = "acceptance gate"
              AcceptedAssertion = "All authority cards and effect programs are in scope."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:7-9"
              Rule =
                "The declarative authority supplies the collectible, kit and Basic Vim libraries and every validated program."
              Check =
                "Count authority content and program IDs, bind them to the reconciliation authority, and require seeded decks to cover every content card." }

        let stackSize =
            { Id = "TR-STACK-SIZE-15"
              Group = "(c) opening"
              AcceptedAssertion = "Each side has exactly 60 cards."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:15"
              Rule = "Each side has exactly 60 cards."
              Check = "Count each generated frozen deck before match start." }

        let stackCopyLimit =
            { Id = "TR-STACK-COPY-LIMIT-15"
              Group = "(c) opening"
              AcceptedAssertion =
                "No mechanical identity has more than four copies except unlimited Basic Vim."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:15"
              Rule =
                "A mechanical identity has at most four copies, except that Basic Vim is unlimited."
              Check = "Count each mechanical identity in each generated frozen deck." }

        let stackRegular =
            { Id = "TR-STACK-REGULAR-15"
              Group = "(c) opening"
              AcceptedAssertion = "Each side's deck contains at least one Regular Bloke."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:15"
              Rule = "Each side has at least one Regular Bloke."
              Check = "Require a Regular mechanical identity in each generated frozen deck." }

        let openingSide =
            { Id = "TR-OPENING-SIDE-16"
              Group = "(c) opening"
              AcceptedAssertion =
                "The opening side is sampled before either shuffle or opening draw."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:16"
              Rule = "Sample the opening side before either shuffle or opening draw."
              Check = "Reproduce the first deterministic RNG sample and compare the opening player." }

        let openingMitt =
            { Id = "TR-OPENING-MITT-16"
              Group = "(c) opening"
              AcceptedAssertion = "Each side draws a seven-card opening mitt."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:16"
              Rule = "Each side shuffles and draws a seven-card mitt."
              Check = "Count each final opening mitt before placement." }

        let openingPlacement =
            { Id = "TR-OPENING-PLACEMENT-16"
              Group = "(c) opening"
              AcceptedAssertion =
                "Each side opens with one Regular Bloke at the oche and up to five Regular Blokes in the booth."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:16"
              Rule =
                "Place one Regular Bloke at the oche and up to five Regular Blokes in the booth."
              Check = "Inspect each completed opening placement while setup is still in progress." }

        let openingBarChits =
            { Id = "TR-OPENING-BAR-CHITS-16"
              Group = "(c) opening"
              AcceptedAssertion = "Each side starts with six bar chits."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:16"
              Rule = "Each side sets six bar chits."
              Check = "Compare both the opening counter and BarChit zone count with six." }

        let mulliganLegalMitt =
            { Id = "TR-MULLIGAN-LEGAL-MITT-17"
              Group = "(c) opening"
              AcceptedAssertion =
                "A mitt without a Regular Bloke is reshuffled and redrawn until legal."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:17"
              Rule = "A mitt without a Regular Bloke is reshuffled and redrawn until legal."
              Check =
                "Require each final mitt to contain a Regular and each mulligan reveal to contain seven cards." }

        let simultaneousMulligan =
            { Id = "TR-MULLIGAN-SIMULTANEOUS-17"
              Group = "(c) opening"
              AcceptedAssertion = "Simultaneous mulligans grant no bonus."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:17"
              Rule = "Simultaneous mulligans grant no bonus."
              Check = "Require zero allowance and zero bonus cards when mulligan counts match." }

        let excessMulligan =
            { Id = "TR-MULLIGAN-EXCESS-17"
              Group = "(c) opening"
              AcceptedAssertion =
                "The other side may draw at most one extra card for each excess mulligan."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:17"
              Rule = "For each excess mulligan, the other side may draw up to one extra card."
              Check =
                "Compare remaining allowance plus already drawn bonus cards with the mulligan-count difference and cap the draw at that grant." }

        let cardZones =
            { Id = "TR-CARD-ZONES-18"
              Group = "(a) every step"
              AcceptedAssertion = "Every card instance occupies exactly one zone."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:18"
              Rule = "Throughout a bout, every card instance occupies exactly one zone."
              Check =
                "Preserve the exact starting instance set and require Attached-zone parent, attachment and promotion-stack references to agree." }

        let boothLimit =
            { Id = "TR-BOOTH-LIMIT-18"
              Group = "(a) every step"
              AcceptedAssertion = "Each side's booth holds at most five Blokes at every step."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:18"
              Rule = "Each side's booth holds at most five Blokes throughout a bout."
              Check = "Count each side's Booth zone after every reached transition and probe." }

        let ocheCount =
            { Id = "TR-OCHE-COUNT-16-64"
              Group = "(a) every step"
              AcceptedAssertion =
                "Each side has exactly one Bloke at the oche while play continues outside a pending replacement."
              Heading = "Stack and opening; Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:16,64"
              Rule =
                "Opening places one Regular Bloke at the oche; after send-home, continuing play promotes a booth Bloke to the oche."
              Check =
                "Count each side's Oche zone in settled non-terminal states, excluding only the explicit pending replacement window." }

        let openerMate =
            { Id = "TR-OPENER-MATE-19"
              Group = "(b) each round"
              AcceptedAssertion = "The opening side cannot play a Mate in its first round."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:19"
              Rule = "The opening side cannot play a Mate in its first round."
              Check = "Reject any offered Mate action on the opening side's first round." }

        let openerAttack =
            { Id = "TR-OPENER-ATTACK-19"
              Group = "(b) each round"
              AcceptedAssertion = "The opening side cannot declare an Attack in its first round."
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:19"
              Rule = "The opening side cannot declare an Attack in its first round."
              Check = "Reject any offered Attack action on the opening side's first round." }

        let requiredRoundDraw =
            { Id = "TR-REQUIRED-ROUND-DRAW-23"
              Group = "(d) ending"
              AcceptedAssertion =
                "Failure to make the required round-opening stack draw is one of the three win methods."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:23,66"
              Rule =
                "A required stack draw opens each round, and failure to make it loses the bout."
              Check =
                "For every RoundStarted event, require the matching required draw or a terminal short-stack loss." }

        let attackEndsRound =
            { Id = "TR-ATTACK-ENDS-ROUND-24"
              Group = "(b) each round"
              AcceptedAssertion = "An Attack ends the round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:24"
              Rule = "An Attack ends the round."
              Check =
                "Track a selected Attack through pending choices and require RoundEnded unless terminal." }

        let partyTrickContinuesRound =
            { Id = "TR-PARTY-TRICK-CONTINUES-24"
              Group = "(b) each round"
              AcceptedAssertion = "A Party Trick does not end the round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:24"
              Rule = "A Party Trick does not end the round."
              Check =
                "Track a selected Party Trick through pending choices and require the same actor and round with no RoundEnded event unless terminal." }

        let vimPerRound =
            { Id = "TR-VIM-PER-ROUND-24"
              Group = "(b) each round"
              AcceptedAssertion = "At most one normal Vim attachment is allowed per round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:24"
              Rule = "One normal Vim attachment is allowed per round."
              Check =
                "Check both recorded RoundUsage and selected AttachVim commands, including independently applied offered-action probes." }

        let promotionEdge =
            { Id = "TR-PROMOTION-EDGE-25"
              Group = "(b) each round"
              AcceptedAssertion = "Promotion requires the exact mechanical edge."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:25"
              Rule = "Promotion requires the exact mechanical edge."
              Check = "Compare every selected promotion's source identity with PromotesFromId." }

        let promotionLimits =
            { Id = "TR-PROMOTION-LIMITS-9-25"
              Group = "(b) each round"
              AcceptedAssertion =
                "A Bloke cannot promote on either side's first round, its first round in play, or twice in one round, subject to an authority-program override."
              Heading = "Authority and boundaries; Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:9,25"
              Rule =
                "The base promotion timing limits apply unless an authority-bound effect program explicitly overrides them; BLK-021-T01 is the mapped second-opener override."
              Check =
                "Inspect rounds started, entry round and last promotion round, and recognize only the active BLK-021-T01 continuous authority effect." }

        let barKitPerBloke =
            { Id = "TR-BAR-KIT-PER-BLOKE-26"
              Group = "(a) every step"
              AcceptedAssertion = "A Bloke has at most one Bar Kit attached."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:26"
              Rule = "At most one Bar Kit may be attached to a Bloke."
              Check = "Count Bar Kit attachments on every Bloke after every reached state." }

        let matePerRound =
            { Id = "TR-MATE-PER-ROUND-26"
              Group = "(b) each round"
              AcceptedAssertion = "At most one Mate may be played per round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:26"
              Rule = "At most one Mate may be played per round."
              Check = "Check RoundUsage and selected Mate commands per actor and round." }

        let localPerRound =
            { Id = "TR-LOCAL-PER-ROUND-26"
              Group = "(b) each round"
              AcceptedAssertion = "At most one Local may be played per round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:26"
              Rule = "At most one Local may be played per round."
              Check = "Check RoundUsage and selected Local commands per actor and round." }

        let localPerSide =
            { Id = "TR-LOCAL-PER-SIDE-26"
              Group = "(a) every step"
              AcceptedAssertion = "At most one Local is in play per side."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:26"
              Rule = "Only one Local is in play per side."
              Check = "Count the Local zone separately for each owner; no global limit is asserted." }

        let taxiPerRound =
            { Id = "TR-TAXI-PER-ROUND-27"
              Group = "(b) each round"
              AcceptedAssertion = "Taxi may be used at most once per round."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:27"
              Rule = "Taxi is once per round."
              Check = "Check RoundUsage and selected Taxi commands per actor and round." }

        let taxiEligibility =
            { Id = "TR-TAXI-ELIGIBILITY-27"
              Group = "(b) each round"
              AcceptedAssertion =
                "Taxi requires a booth Bloke and cannot be used by a NoddedOff or Legless Bloke."
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:27"
              Rule = "Taxi requires a booth Bloke; NoddedOff or Legless Blokes cannot taxi."
              Check =
                "Inspect the incoming zone and outgoing oche rough states for each selected Taxi." }

        let damageNonNegative =
            { Id = "TR-DAMAGE-NONNEGATIVE-47"
              Group = "(a) every step"
              AcceptedAssertion = "Damage is never negative."
              Heading = "Attack and damage ordering"
              Lines = "technical-rulebook.md:47"
              Rule = "Calculated damage is clamped at zero before counters are placed."
              Check = "Require every card's persisted damage counter total to be non-negative." }

        let effectChoices =
            { Id = "TR-EFFECT-CHOICES-49"
              Group = "(e) effects"
              AcceptedAssertion =
                "Pending legal effect choices and round continuations settle deterministically or are reported as failures."
              Heading = "Attack and damage ordering"
              Lines = "technical-rulebook.md:37-43,49"
              Rule =
                "Required choices are resolved in attack order; explicit eligible choices, optional effects and deterministic random selections use their ruled shapes."
              Check =
                "Drive each payable offered action through pending choices, knockouts, bar chits, replacements and round end, recording an explicit settlement status." }

        let roughStateLocation =
            { Id = "TR-ROUGH-OCHE-53"
              Group = "(a) every step"
              AcceptedAssertion = "Only the oche Bloke has rough states."
              Heading = "Rough states and checkup"
              Lines = "technical-rulebook.md:53"
              Rule = "Only the oche Bloke has rough states."
              Check = "Inspect the kind and zone of every card carrying any rough state." }

        let roughStateCoexistence =
            { Id = "TR-ROUGH-COEXISTENCE-60"
              Group = "(a) every step"
              AcceptedAssertion =
                "At most one of NoddedOff, Muddled and Legless applies; Singed and DodgyPint may coexist with it and each other."
              Heading = "Rough states and checkup"
              Lines = "technical-rulebook.md:60"
              Rule =
                "The rotated group has at most one member; Singed and DodgyPint are independent markers."
              Check =
                "Count only the three rotated states and deliberately impose no mutual-exclusion assertion on Singed or DodgyPint." }

        let sendHomeState =
            { Id = "TR-SEND-HOME-STATE-64"
              Group = "(a) every step"
              AcceptedAssertion =
                "No Bloke remains in play past a send-home check at or above staying power; a sent-home Bloke and its attachments are chucked."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:64"
              Rule = "A Bloke at or above staying power is sent home with every attachment."
              Check =
                "Allow only an explicitly pending knockout at the threshold, then inspect send-home events and resulting zones and relationships." }

        let normalSendHomeAward =
            { Id = "TR-NORMAL-SEND-HOME-AWARD-64"
              Group = "(d) ending"
              AcceptedAssertion = "Sending home a normal target awards one bar chit."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:64"
              Rule = "A normal target awards one bar chit."
              Check = "Match each normal BlokeSentHome event to its available BarChitsTaken amount." }

        let terminalMethods =
            { Id = "TR-TERMINAL-METHODS-66"
              Group = "(d) ending"
              AcceptedAssertion = "A bout ends only by the three rulebook win methods."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:66"
              Rule =
                "A side wins by taking its last bar chit, leaving the other side with no Bloke in play, or the other side failing its required draw."
              Check =
                "Derive all three methods from the before/after transition and require a completed bout's winner to have more simultaneous methods." }

        let suddenDeath =
            { Id = "TR-SUDDEN-DEATH-66"
              Group = "(d) ending"
              AcceptedAssertion =
                "One simultaneous win method per side starts one-bar-chit sudden death; more simultaneous methods wins immediately."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:66"
              Rule =
                "Equal simultaneous methods start one-bar-chit sudden death and repeat; more simultaneous methods wins immediately."
              Check =
                "Compare both sides' method counts, winner, sudden-death counter and one-chit reset." }

        let barChitCeiling =
            { Id = "TR-BAR-CHIT-CEILING-16-66"
              Group = "(a) every step"
              AcceptedAssertion = "Bar chits never exceed six."
              Heading = "Stack and opening; Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:16,66"
              Rule = "A side starts with six bar chits; sudden death resets it to one."
              Check = "Bound each side's counter and BarChit zone by the opening total." }

        let barChitsDoNotIncrease =
            { Id = "TR-BAR-CHITS-DECREASE-64-66"
              Group = "(a) every step"
              AcceptedAssertion =
                "Bar chits never increase during ordinary play; the one-chit sudden-death reset is the terminal-rule exception."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:64,66"
              Rule =
                "Awards take bar chits; only the simultaneous-win rule resets each side to one for sudden death."
              Check =
                "Compare each side's counter across every transition and recognize only an exact sudden-death reset." }

        let fossilAward =
            { Id = "TR-FOSSIL-AWARD-68"
              Group = "(d) ending"
              AcceptedAssertion = "A fossil Local awards one bar chit when sent home."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:68"
              Rule = "KIT-001 through KIT-003 award one bar chit when sent home."
              Check = "Match fossil send-home events to their available one-chit award." }

        let bigHitterAward =
            { Id = "TR-BIG-HITTER-AWARD-68"
              Group = "(d) ending"
              AcceptedAssertion = "Each of the eleven Big Hitters awards two bar chits."
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:68"
              Rule = "The eleven IDs in bigHitters.blokeIds award two bar chits."
              Check =
                "Require exactly eleven authority IDs and match Big Hitter send-home events to the available two-chit award." }

        let programShapes =
            { Id = "TR-PROGRAM-SHAPES-9"
              Group = "(e) effects"
              AcceptedAssertion = "Every authority effect program has a valid executable shape."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "All authority programs are validated against executable opcode, condition, target, selection, distribution and trigger shapes."
              Check =
                "Load the validated runtime authority and require its exact sorted program IDs to match reconciliation." }

        let offeredActionApplies =
            { Id = "TR-OFFERED-ACTION-APPLIES-9"
              Group = "(e) effects"
              AcceptedAssertion = "Every offered payable legal action applies successfully."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule = "Blokemon.Game executes the validated authority programs."
              Check =
                "Independently apply every payable offered action and every selected self-play action; retain every rejection as a finding." }

        let offeredActionFunctional =
            { Id = "TR-OFFERED-ACTION-FUNCTIONAL-9"
              Group = "(e) effects"
              AcceptedAssertion =
                "Every offered payable legal action changes semantic state or reveals hidden cards after complete settlement."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule = "Blokemon.Game executes the validated authority programs."
              Check =
                "After an explicitly settled probe, compare semantic state excluding revision and accept a CardsRevealed event as observable function." }

        let effectDoesNotRaise =
            { Id = "TR-EFFECT-NO-RAISE-9"
              Group = "(e) effects"
              AcceptedAssertion = "No reached effect program raises an exception."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule = "Blokemon.Game executes programs validated against executable shapes."
              Check =
                "Wrap every offered and selected command application with seed, step and action evidence." }

        let programCoverage =
            { Id = "TR-PROGRAM-COVERAGE-9"
              Group = "(e) effects"
              AcceptedAssertion =
                "The report distinguishes effect-attributed execution from programs not observed across the run set."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule = "The authority contains every validated program."
              Check =
                "Aggregate MatchEvent.Effect attribution across self-play and settled offered-action probes, explicitly labelling unobservable executions." }

        let deterministicEvents =
            { Id = "TR-DETERMINISTIC-EVENTS-9"
              Group = "(f) determinism"
              AcceptedAssertion = "The same seed reproduces an identical event log."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "MatchState persists the deterministic random stream, choices, trigger timing and accepted command identities."
              Check =
                "Compare supported MatchJson UTF-8 bytes for independently generated event arrays from the same seed." }

        let deterministicFinalState =
            { Id = "TR-DETERMINISTIC-FINAL-STATE-9"
              Group = "(f) determinism"
              AcceptedAssertion = "The same seed reproduces the identical final MatchState."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "MatchState persists the deterministic random stream, choices, trigger timing and accepted command identities."
              Check = "Compare exact final MatchState values from independent same-seed runs." }

        let persistedReplayState =
            { Id = "TR-PERSISTED-REPLAY-STATE-9"
              Group = "(f) determinism"
              AcceptedAssertion =
                "The supported persisted MatchDocument replay reproduces an identical final MatchState."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "The App persists random state, choices, trigger timing and accepted command identities in MatchState."
              Check =
                "Create, start and play through LocalApplicationService, deserialize the stored MatchDocument with MatchJson, then compare exact states from independent MatchReplay.replayDocument calls." }

        let persistedReplayEvents =
            { Id = "TR-PERSISTED-REPLAY-EVENTS-9"
              Group = "(f) determinism"
              AcceptedAssertion =
                "Independent supported replays of one persisted MatchDocument produce identical stable event bytes."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "The App persists random state, choices, trigger timing and accepted command identities in MatchState."
              Check =
                "Serialize each replay's exact MatchEvent array with supported MatchJson options and compare UTF-8 bytes." }

        let monotonicRevision =
            { Id = "TR-MONOTONIC-REVISION-9"
              Group = "(f) determinism"
              AcceptedAssertion = "Every accepted command advances revision monotonically."
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "Accepted command identities and their deterministic state are persisted in MatchState."
              Check =
                "Require each applied command's revision to equal the previous revision's successor." }

        let All =
            [| authorityInventory
               stackSize
               stackCopyLimit
               stackRegular
               openingSide
               openingMitt
               openingPlacement
               openingBarChits
               mulliganLegalMitt
               simultaneousMulligan
               excessMulligan
               cardZones
               boothLimit
               ocheCount
               openerMate
               openerAttack
               requiredRoundDraw
               attackEndsRound
               partyTrickContinuesRound
               vimPerRound
               promotionEdge
               promotionLimits
               barKitPerBloke
               matePerRound
               localPerRound
               localPerSide
               taxiPerRound
               taxiEligibility
               damageNonNegative
               effectChoices
               roughStateLocation
               roughStateCoexistence
               sendHomeState
               normalSendHomeAward
               terminalMethods
               suddenDeath
               barChitCeiling
               barChitsDoNotIncrease
               fossilAward
               bigHitterAward
               programShapes
               offeredActionApplies
               offeredActionFunctional
               effectDoesNotRaise
               programCoverage
               deterministicEvents
               deterministicFinalState
               persistedReplayState
               persistedReplayEvents
               monotonicRevision |]

    type BoutStatus =
        | Completed
        | Incomplete

    type BoutStopReason =
        | RuleCompleted
        | StepCeilingReached

    type ProbeSettlementStatus =
        | Settled
        | DirectActionRejected
        | NoSettlementAction
        | SettlementActionRejected
        | SettlementCeilingReached

    type ProbeSettlement =
        { Seed: uint64
          Step: int
          Action: string
          Status: ProbeSettlementStatus
          SettlementCommands: int
          Detail: string }

    type ClauseEvaluation = { ClauseId: string; Count: int }

    type private SettlementResult =
        { State: MatchState
          Events: ImmutableArray<MatchEvent>
          Status: ProbeSettlementStatus
          Commands: int
          Detail: string }

    type ProductionReplayResult =
        { PersistedCommands: int
          FirstState: MatchState
          FirstEvents: ImmutableArray<MatchEvent>
          SecondState: MatchState
          SecondEvents: ImmutableArray<MatchEvent> }

    type private MemoryDocumentStore() =
        let documents = Dictionary<string, StoredDocument>(StringComparer.Ordinal)

        interface IStateDocumentStore with
            member _.Read(key, cancellationToken) =
                cancellationToken.ThrowIfCancellationRequested()

                match documents.TryGetValue key with
                | true, document -> Task.FromResult document
                | false, _ -> Task.FromResult null

            member _.Create(key, json, cancellationToken) =
                cancellationToken.ThrowIfCancellationRequested()

                let result: DocumentWriteResult =
                    if documents.ContainsKey key then
                        DocumentWriteResult.Conflict()
                    else
                        let revision = 1L
                        documents[key] <- StoredDocument(revision, json)
                        DocumentWriteResult.Written revision

                Task.FromResult result

            member _.Update(key, expectedRevision, json, cancellationToken) =
                cancellationToken.ThrowIfCancellationRequested()

                let result: DocumentWriteResult =
                    match documents.TryGetValue key with
                    | true, current when current.Revision = expectedRevision ->
                        let revision = current.Revision + 1L
                        documents[key] <- StoredDocument(revision, json)
                        DocumentWriteResult.Written revision
                    | _ -> DocumentWriteResult.Conflict()

                Task.FromResult result

            member _.Delete(key, cancellationToken) =
                cancellationToken.ThrowIfCancellationRequested()
                documents.Remove key |> ignore
                Task.CompletedTask

            member _.DeleteIfUnchanged(key, expectedRevision, expectedJson, cancellationToken) =
                cancellationToken.ThrowIfCancellationRequested()

                let result: DocumentDeleteResult =
                    match documents.TryGetValue key with
                    | false, _ -> DocumentDeleteResult.Missing()
                    | true, current when
                        current.Revision = expectedRevision
                        && String.Equals(current.Json, expectedJson, StringComparison.Ordinal)
                        ->
                        documents.Remove key |> ignore
                        DocumentDeleteResult.Deleted()
                    | _ -> DocumentDeleteResult.Conflict()

                Task.FromResult result

    type PendingRoundAction =
        { Kind: LegalActionKind
          ActivePlayer: PlayerId
          RoundNumber: int
          mutable SawRoundEnd: bool }

    type RoundActionCounts =
        { mutable Vim: int
          mutable Mate: int
          mutable Local: int
          mutable Taxi: int
          mutable Promotions: int }

    type Observation =
        { mutable Assertions: int
          mutable Context: string
          mutable PendingRoundAction: PendingRoundAction option
          mutable ExpectedCardInstances: Set<CardInstanceId> option
          Findings: ResizeArray<string>
          FindingClauses: HashSet<string>
          ClauseEvaluations: Dictionary<string, int>
          ProbeSettlements: ResizeArray<ProbeSettlement>
          ObservedEffects: HashSet<EffectId>
          RoundActions: Dictionary<struct (PlayerId * int), RoundActionCounts> }

    type BoutResult =
        { Seed: uint64
          StepCeiling: int
          Status: BoutStatus
          StopReason: BoutStopReason
          Steps: int
          Assertions: int
          Findings: ImmutableArray<string>
          ClauseEvaluations: ImmutableArray<ClauseEvaluation>
          ProbeSettlements: ImmutableArray<ProbeSettlement>
          ObservedEffects: ImmutableArray<EffectId>
          StartRequest: MatchStartRequest
          Events: ImmutableArray<MatchEvent>
          FinalState: MatchState }

    type ApproximateCoverageSummary =
        { ObservedPrograms: int
          UnobservedPrograms: int
          CompletedBouts: int
          IncompleteBouts: int
          Findings: int }

    let DefaultSeeds = [| 0UL; 78UL; 156UL |]
    let DefaultStepCeiling = 384
    let LargeSweepSeeds = [| for seed in 0UL .. 23UL -> seed * 37UL |]
    let LargeSweepStepCeiling = 768

    let private authority = MatchScenario.Authority
    let private firstPlayer = PlayerId "fuzz-first"
    let private secondPlayer = PlayerId "fuzz-second"

    let allContentIds =
        Array.append (authority.Collectibles |> Array.map _.Id) (authority.Kits |> Array.map _.Id)

    let allPrograms =
        seq {
            for card in authority.Collectibles do
                for trick in card.PartyTricks do
                    yield trick.MechanicalId

                for attack in card.Attacks do
                    yield attack.MechanicalId

                for rule in card.HouseRules do
                    yield rule.MechanicalId

            for card in authority.Kits do
                for trick in card.PartyTricks do
                    yield trick.MechanicalId

                for attack in card.Attacks do
                    yield attack.MechanicalId

                for rule in card.HouseRules do
                    yield rule.MechanicalId
        }
        |> Seq.sort
        |> Seq.toArray

    let reconciliationAuthorityVersion, reconciliationProgramIds =
        use document =
            JsonDocument.Parse(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Authorities",
                        "sv151-authority-reconciliation.json"
                    )
                )
            )

        let root = document.RootElement

        let authorityVersion =
            match root.GetProperty("authorityVersion").GetString() with
            | Null -> failwith "The reconciliation authority version was null."
            | NonNull value -> value

        let programs =
            root.GetProperty("effects").EnumerateArray()
            |> Seq.map (fun effect ->
                match effect.GetProperty("mechanicalId").GetString() with
                | Null -> failwith "A reconciliation mechanicalId was null."
                | NonNull value -> value)
            |> Seq.sort
            |> Seq.toArray

        authorityVersion, programs

    let private recordAssertion (observation: Observation) (clause: RulebookClause) =
        observation.Assertions <- observation.Assertions + 1

        observation.ClauseEvaluations[clause.Id] <-
            match observation.ClauseEvaluations.TryGetValue clause.Id with
            | true, count -> count + 1
            | false, _ -> 1

    let private observeEffects (observation: Observation) (events: seq<MatchEvent>) =
        for event in events do
            match event.Effect with
            | ValueSome effect -> observation.ObservedEffects.Add effect |> ignore
            | ValueNone -> ()

    let private enforce
        (observation: Observation)
        (clause: RulebookClause)
        (seed: uint64)
        (step: int)
        (detail: string)
        (condition: bool)
        =
        recordAssertion observation clause

        if not condition && observation.FindingClauses.Add clause.Id then
            observation.Findings.Add(
                $"seed={seed}; step={step}; context={observation.Context}; clause={clause.Id} ({clause.Lines}, {clause.Heading}); {detail}; rule={clause.Rule}"
            )

    let private enforceEveryFailure
        (observation: Observation)
        (clause: RulebookClause)
        (seed: uint64)
        (step: int)
        (detail: string)
        (condition: bool)
        =
        recordAssertion observation clause

        if not condition then
            observation.Findings.Add(
                $"seed={seed}; step={step}; context={observation.Context}; clause={clause.Id} ({clause.Lines}, {clause.Heading}); {detail}; rule={clause.Rule}"
            )

    let private shuffled (seed: uint64) (values: 'T array) =
        let result = Array.copy values
        let random = BlokemonSeededRandom seed

        for index in result.Length - 1 .. -1 .. 1 do
            let swap = random.NextInt(index + 1)
            let held = result[index]
            result[index] <- result[swap]
            result[swap] <- held

        result

    let private cyclicWindow (offset: int) (count: int) (values: 'T array) =
        [| for index in 0 .. count - 1 -> values[(offset + index) % values.Length] |]

    let private deckFor (seed: uint64) (lane: int) (owner: PlayerId) =
        let contentOffset = int ((seed + uint64 (lane * 39)) % uint64 allContentIds.Length)
        let selectedContent = cyclicWindow contentOffset 39 allContentIds

        let basicVim =
            authority.BasicVim |> Array.collect (fun vim -> Array.replicate 3 vim.Id)

        let cards =
            Array.append selectedContent basicVim
            |> shuffled (seed ^^^ (0x9E3779B97F4A7C15UL + uint64 lane))

        FrozenDeckSnapshot.Create(owner, cards)

    let requestFor (seed: uint64) : MatchStartRequest =
        { MatchId = MatchId $"fuzz-{seed}"
          Seed = MatchSeed seed
          FirstDeck = deckFor seed 0 firstPlayer
          SecondDeck = deckFor seed 1 secondPlayer }

    let private regularIds =
        authority.Collectibles
        |> Seq.filter (fun card -> card.Rank = BlokemonRank.Regular)
        |> Seq.map (fun card -> MechanicalCardId card.Id)
        |> Set.ofSeq

    let private basicVimIds =
        authority.BasicVim
        |> Seq.map (fun card -> MechanicalCardId card.Id)
        |> Set.ofSeq

    let private barKitIds =
        authority.Kits
        |> Seq.filter (fun kit -> kit.Kind = BlokemonKitKind.BarKit)
        |> Seq.map (fun kit -> MechanicalCardId kit.Id)
        |> Set.ofSeq

    let private mateIds =
        authority.Kits
        |> Seq.filter (fun kit -> kit.Kind = BlokemonKitKind.Mate)
        |> Seq.map (fun kit -> MechanicalCardId kit.Id)
        |> Set.ofSeq

    let private localIds =
        authority.Kits
        |> Seq.filter (fun kit -> kit.Kind = BlokemonKitKind.Local)
        |> Seq.map (fun kit -> MechanicalCardId kit.Id)
        |> Set.ofSeq

    let private fossilIds = authority.BaseRules.FossilKits.KitIds |> Set.ofArray

    let private bigHitterIds =
        set
            [ "BLK-003"
              "BLK-006"
              "BLK-009"
              "BLK-024"
              "BLK-038"
              "BLK-065"
              "BLK-076"
              "BLK-115"
              "BLK-124"
              "BLK-145"
              "BLK-151" ]

    let private isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    let private roundCounts (observation: Observation) (player: PlayerId) (round: int) =
        let key = struct (player, round)

        match observation.RoundActions.TryGetValue key with
        | true, counts -> counts
        | false, _ ->
            let counts =
                { Vim = 0
                  Mate = 0
                  Local = 0
                  Taxi = 0
                  Promotions = 0 }

            observation.RoundActions[key] <- counts
            counts

    let private stayingPower (card: CardState) =
        if card.Kind = CardKind.Bloke then
            authority.Collectibles
            |> Array.find (fun candidate -> candidate.Id = card.MechanicalId.Value)
            |> _.StayingPower
        else
            authority.BaseRules.FossilKits.PlayAsRegularLocalStayingPower

    let private assertDeck
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (deck: FrozenDeckSnapshot)
        =
        enforce
            observation
            Clauses.stackSize
            seed
            step
            $"deck owner={deck.Owner.Value} has {deck.Cards.Length} cards"
            (deck.Cards.Length = authority.BaseRules.Stack.CardCount)

        for mechanicalId, count in deck.Cards |> Seq.countBy id do
            let limit =
                if basicVimIds.Contains mechanicalId then
                    Int32.MaxValue
                else
                    4

            enforce
                observation
                Clauses.stackCopyLimit
                seed
                step
                $"deck owner={deck.Owner.Value}; card={mechanicalId.Value}; copies={count}; limit={limit}"
                (count <= limit)

        enforce
            observation
            Clauses.stackRegular
            seed
            step
            $"deck owner={deck.Owner.Value} must contain a Regular Bloke"
            (deck.Cards |> Seq.exists regularIds.Contains)

    let private assertOpening
        (observation: Observation)
        (seed: uint64)
        (state: MatchState)
        (events: ImmutableArray<MatchEvent>)
        =
        let expectedOpeningIndex = BlokemonSeededRandom(seed).NextInt 2

        let expectedOpening =
            if expectedOpeningIndex = 0 then
                firstPlayer
            else
                secondPlayer

        enforce
            observation
            Clauses.openingSide
            seed
            0
            $"opening player={state.OpeningPlayer.Value}; expected first RNG sample={expectedOpening.Value}"
            (state.OpeningPlayer = expectedOpening)

        for player in state.Players do
            let mitt = state.CardsIn(player.Id, CardZone.Mitt) |> Seq.toArray

            enforce
                observation
                Clauses.openingMitt
                seed
                0
                $"player={player.Id.Value}; opening mitt={mitt.Length}"
                (mitt.Length = authority.BaseRules.Opening.MittSize)

            enforce
                observation
                Clauses.mulliganLegalMitt
                seed
                0
                $"player={player.Id.Value}; final redrawn mitt must contain a Regular Bloke"
                (mitt |> Array.exists (fun card -> regularIds.Contains card.MechanicalId))

            let other = state.Player(state.Other player.Id)
            let expectedAllowance = max 0 (other.MulliganCount - player.MulliganCount)

            enforce
                observation
                Clauses.excessMulligan
                seed
                0
                $"player={player.Id.Value}; mulligans={player.MulliganCount}; allowance={player.MulliganBonusAllowance}; expected={expectedAllowance}"
                (player.MulliganBonusAllowance + player.BonusDrawn.Length = expectedAllowance
                 && player.BonusDrawn.Length <= expectedAllowance)

            if player.MulliganCount = other.MulliganCount then
                enforce
                    observation
                    Clauses.simultaneousMulligan
                    seed
                    0
                    $"player={player.Id.Value}; simultaneous mulligans={player.MulliganCount}; allowance={player.MulliganBonusAllowance}; bonus cards={player.BonusDrawn.Length}"
                    (player.MulliganBonusAllowance = 0 && player.BonusDrawn.IsEmpty)

            let barChits = state.CardsIn(player.Id, CardZone.BarChit) |> Seq.length

            enforce
                observation
                Clauses.openingBarChits
                seed
                0
                $"player={player.Id.Value}; opening bar chits={player.BarChitsRemaining}; zoned={barChits}"
                (player.BarChitsRemaining = authority.BaseRules.Opening.BarChitCount)

        for revealed in
            events |> Seq.filter (fun event -> event.Kind = MatchEventKind.CardsRevealed) do
            enforce
                observation
                Clauses.mulliganLegalMitt
                seed
                0
                $"mulligan reveal actor={revealed.Actor.Value.Value}; cards={revealed.TargetCards.Length}"
                (revealed.TargetCards.Length = authority.BaseRules.Opening.MittSize)

    let private assertRelationships
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (state: MatchState)
        =
        let ids = state.Cards |> Seq.map _.Id |> Set.ofSeq

        enforce
            observation
            Clauses.cardZones
            seed
            step
            $"card instance set count={ids.Count}; expected={observation.ExpectedCardInstances |> Option.map _.Count}"
            (observation.ExpectedCardInstances = Some ids)

        for parent in state.Cards do
            for child in parent.Attachments do
                let related = state.Cards |> Seq.tryFind (fun card -> card.Id = child)

                enforce
                    observation
                    Clauses.cardZones
                    seed
                    step
                    $"parent={parent.Id.Value}; child={child.Value} must exist, be Attached, and point back"
                    (match related with
                     | Some card ->
                         card.Zone = CardZone.Attached && card.AttachedTo = ValueSome parent.Id
                     | None -> false)

            for child in parent.UnderlyingCards do
                let related = state.Cards |> Seq.tryFind (fun card -> card.Id = child)

                enforce
                    observation
                    Clauses.cardZones
                    seed
                    step
                    $"promoted parent={parent.Id.Value}; underlying={child.Value} must exist in the Attached zone"
                    (match related with
                     | Some card -> card.Zone = CardZone.Attached
                     | None -> false)

        for card in state.Cards do
            if card.Zone = CardZone.Attached then
                enforce
                    observation
                    Clauses.cardZones
                    seed
                    step
                    $"attached card={card.Id.Value} must point to a parent that lists it"
                    (match card.AttachedTo with
                     | ValueSome parent ->
                         match
                             state.Cards |> Seq.tryFind (fun candidate -> candidate.Id = parent)
                         with
                         | Some related ->
                             Seq.contains card.Id related.Attachments
                             || Seq.contains card.Id related.UnderlyingCards
                         | None -> false
                     | ValueNone -> false)
            else
                enforce
                    observation
                    Clauses.cardZones
                    seed
                    step
                    $"non-attached card={card.Id.Value}; zone={card.Zone}; AttachedTo must be empty"
                    card.AttachedTo.IsNone

    let private assertState
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (state: MatchState)
        =
        assertRelationships observation seed step state

        for player in state.Players do
            let booth = state.CardsIn(player.Id, CardZone.Booth) |> Seq.toArray
            let oche = state.CardsIn(player.Id, CardZone.Oche) |> Seq.toArray

            let boothSummary =
                booth
                |> Seq.map (fun card -> $"{card.Id.Value}:{card.MechanicalId.Value}")
                |> String.concat ","

            enforce
                observation
                Clauses.boothLimit
                seed
                step
                $"player={player.Id.Value}; booth count={booth.Length}; cards={boothSummary}"
                (booth.Length <= authority.BaseRules.Opening.BoothLimit)

            if
                player.OpeningChosen
                && state.Phase <> MatchPhase.Complete
                && state.ReplacementPlayer <> ValueSome player.Id
                && state.PendingEffect.IsNone
                && state.PendingKnockout.IsNone
                && state.PendingBarChits.IsEmpty
                && not state.PendingRoundEnd
            then
                enforce
                    observation
                    Clauses.ocheCount
                    seed
                    step
                    $"player={player.Id.Value}; continuing play requires exactly one oche Bloke; count={oche.Length}"
                    (oche.Length = authority.BaseRules.Opening.OcheRegularCount)

            if player.OpeningChosen && state.Phase = MatchPhase.OpeningPlacement then
                enforce
                    observation
                    Clauses.openingPlacement
                    seed
                    step
                    $"player={player.Id.Value}; opening oche and booth cards must all be Regular"
                    (oche.Length = 1
                     && regularIds.Contains oche[0].MechanicalId
                     && (booth |> Seq.forall (fun card -> regularIds.Contains card.MechanicalId)))

            let other = state.Player(state.Other player.Id)
            let expectedAllowance = max 0 (other.MulliganCount - player.MulliganCount)

            enforce
                observation
                Clauses.excessMulligan
                seed
                step
                $"player={player.Id.Value}; mulligans={player.MulliganCount}; allowance={player.MulliganBonusAllowance}; bonus cards={player.BonusDrawn.Length}; expected allowance={expectedAllowance}"
                (player.MulliganBonusAllowance + player.BonusDrawn.Length = expectedAllowance
                 && player.BonusDrawn.Length <= expectedAllowance)

            if player.MulliganCount = other.MulliganCount then
                enforce
                    observation
                    Clauses.simultaneousMulligan
                    seed
                    step
                    $"player={player.Id.Value}; simultaneous mulligans={player.MulliganCount}; allowance={player.MulliganBonusAllowance}; bonus cards={player.BonusDrawn.Length}"
                    (player.MulliganBonusAllowance = 0 && player.BonusDrawn.IsEmpty)

            let locals =
                state.Cards
                |> Seq.filter (fun card -> card.Owner = player.Id && card.Zone = CardZone.Local)
                |> Seq.length

            enforce
                observation
                Clauses.localPerSide
                seed
                step
                $"player={player.Id.Value}; Locals={locals}"
                (locals <= 1)

            let barChits = state.CardsIn(player.Id, CardZone.BarChit) |> Seq.length

            if
                state.Players |> Seq.forall _.OpeningChosen
                && player.BarChitsRemaining = authority.BaseRules.Opening.BarChitCount
            then
                enforce
                    observation
                    Clauses.openingBarChits
                    seed
                    step
                    $"player={player.Id.Value}; set-aside opening bar chits={player.BarChitsRemaining}; zoned={barChits}"
                    (barChits = authority.BaseRules.Opening.BarChitCount)

            enforce
                observation
                Clauses.barChitCeiling
                seed
                step
                $"player={player.Id.Value}; bar chits={player.BarChitsRemaining}; zoned={barChits}"
                (player.BarChitsRemaining <= authority.BaseRules.Opening.BarChitCount
                 && (not player.OpeningChosen
                     || state.Phase = MatchPhase.OpeningPlacement
                     || state.Phase = MatchPhase.Complete
                     || barChits = player.BarChitsRemaining))

        for card in state.Cards do
            enforce
                observation
                Clauses.damageNonNegative
                seed
                step
                $"card={card.Id.Value}; damage={card.Damage}"
                (card.Damage >= 0)

            if card.RoughStates.Length > 0 then
                enforce
                    observation
                    Clauses.roughStateLocation
                    seed
                    step
                    $"card={card.Id.Value}; kind={card.Kind}; zone={card.Zone}; rough states={card.RoughStates.Length}"
                    (card.Kind = CardKind.Bloke && card.Zone = CardZone.Oche)

            let rotated =
                card.RoughStates
                |> Seq.filter (fun entry ->
                    entry.State = BlokemonRoughState.NoddedOff
                    || entry.State = BlokemonRoughState.Muddled
                    || entry.State = BlokemonRoughState.Legless)
                |> Seq.length

            enforce
                observation
                Clauses.roughStateCoexistence
                seed
                step
                $"card={card.Id.Value}; rotated rough states={rotated}"
                (rotated <= 1)

            if card.Kind = CardKind.Bloke then
                let attachedBarKits =
                    card.Attachments
                    |> Seq.map state.Card
                    |> Seq.filter (fun attached -> barKitIds.Contains attached.MechanicalId)
                    |> Seq.length

                enforce
                    observation
                    Clauses.barKitPerBloke
                    seed
                    step
                    $"card={card.Id.Value}; attached Bar Kits={attachedBarKits}"
                    (attachedBarKits <= authority.BaseRules.Kit.BarKitsPerBloke)

            if isInPlay card && state.Phase <> MatchPhase.Complete then
                let pendingSendHome =
                    match state.PendingKnockout with
                    | ValueSome pending ->
                        pending.KnockedOutCard = card.Id
                        || Seq.contains card.Id pending.RemainingKnockouts
                    | ValueNone -> false

                enforce
                    observation
                    Clauses.sendHomeState
                    seed
                    step
                    $"card={card.Id.Value}; damage={card.Damage}; staying power={stayingPower card}; pending={pendingSendHome}"
                    (pendingSendHome || card.Damage < stayingPower card)

        enforce
            observation
            Clauses.vimPerRound
            seed
            step
            $"round={state.RoundNumber}; recorded Vim attachments={state.RoundUsage.VimAttachments}"
            (state.RoundUsage.VimAttachments
             <= authority.BaseRules.Vim.NormalAttachmentPerRound)

        enforce
            observation
            Clauses.matePerRound
            seed
            step
            $"round={state.RoundNumber}; Mates={state.RoundUsage.MatesPlayed}"
            (state.RoundUsage.MatesPlayed <= authority.BaseRules.Kit.MatesPerRound)

        enforce
            observation
            Clauses.localPerRound
            seed
            step
            $"round={state.RoundNumber}; Locals={state.RoundUsage.LocalsPlayed}"
            (state.RoundUsage.LocalsPlayed <= authority.BaseRules.Kit.LocalsPerRound)

        enforce
            observation
            Clauses.taxiPerRound
            seed
            step
            $"round={state.RoundNumber}; taxis={state.RoundUsage.TaxisUsed}"
            (state.RoundUsage.TaxisUsed <= authority.BaseRules.Taxi.PerRound)

    let private semanticState (state: MatchState) =
        state.Phase,
        state.ActivePlayer,
        state.RoundNumber,
        state.Players,
        state.Cards,
        state.Effects,
        state.PendingEffect,
        state.PendingKnockout,
        state.PendingBarChits,
        state.ReplacementPlayer,
        state.PendingRoundEnd,
        state.Winner,
        state.SuddenDeathCount

    let private hasPendingResolution (state: MatchState) =
        state.PendingEffect.IsSome
        || state.PendingKnockout.IsSome
        || not state.PendingBarChits.IsEmpty
        || state.ReplacementPlayer.IsSome
        || state.PendingRoundEnd

    let private applyWithEvidence
        (engine: MatchEngine)
        (state: MatchState)
        (action: LegalAction)
        (seed: uint64)
        (step: int)
        (context: string)
        =
        try
            engine.Apply(state, action.Command)
        with error ->
            raise (
                InvalidOperationException(
                    $"seed={seed}; step={step}; clause={Clauses.effectDoesNotRaise.Id}; context={context}; action={action.StableKey} raised {error.GetType().Name}",
                    error
                )
            )

    let private settleTrial
        (engine: MatchEngine)
        (seed: uint64)
        (step: int)
        (context: string)
        (initial: MatchState)
        (initialEvents: ImmutableArray<MatchEvent>)
        =
        let cpu = DeterministicCpu()
        let events = ResizeArray<MatchEvent>(initialEvents)
        let mutable state = initial
        let mutable commands = 0
        let mutable unsettled: (ProbeSettlementStatus * string) option = None

        while state.Phase <> MatchPhase.Complete
              && hasPendingResolution state
              && commands < 32
              && unsettled.IsNone do
            let action =
                state.Players
                |> Seq.map _.Id
                |> Seq.sortBy _.Value
                |> Seq.tryPick (fun actor ->
                    match cpu.Choose(engine, state, actor) with
                    | CpuDecision.Selected selected -> Some selected
                    | CpuDecision.NoLegalAction -> None)

            match action with
            | None ->
                unsettled <-
                    Some(
                        NoSettlementAction,
                        $"no legal settlement action while phase={state.Phase}; pending-effect={state.PendingEffect.IsSome}; pending-knockout={state.PendingKnockout.IsSome}; pending-bar-chits={state.PendingBarChits.Length}; replacement={state.ReplacementPlayer.IsSome}; pending-round-end={state.PendingRoundEnd}"
                    )
            | Some selected ->
                match applyWithEvidence engine state selected seed step context with
                | CommandOutcome.Rejected(_, rejection) ->
                    unsettled <-
                        Some(
                            SettlementActionRejected,
                            $"settlement action={selected.StableKey} rejected with {rejection.Code}"
                        )
                | CommandOutcome.Applied(applied, appliedEvents) ->
                    state <- applied
                    events.AddRange appliedEvents
                    commands <- commands + 1

        let status, detail =
            match unsettled with
            | Some failed -> failed
            | None when state.Phase = MatchPhase.Complete ->
                Settled, "settled at legitimate MatchPhase.Complete"
            | None when not (hasPendingResolution state) ->
                Settled, "all pending choices and round continuations settled"
            | None ->
                SettlementCeilingReached,
                $"settlement command ceiling=32 reached while phase={state.Phase}; pending-effect={state.PendingEffect.IsSome}; pending-knockout={state.PendingKnockout.IsSome}; pending-bar-chits={state.PendingBarChits.Length}; replacement={state.ReplacementPlayer.IsSome}; pending-round-end={state.PendingRoundEnd}"

        { State = state
          Events = ImmutableArray.CreateRange events
          Status = status
          Commands = commands
          Detail = detail }

    let private actionSource (state: MatchState) (action: LegalAction) =
        match action.Command.Action with
        | MatchAction.PlayKit(card, _)
        | MatchAction.PlayBloke card
        | MatchAction.ChuckFossil card -> (state.Card card).MechanicalId.Value
        | MatchAction.Promote(card, _)
        | MatchAction.AttachVim(card, _) -> (state.Card card).MechanicalId.Value
        | MatchAction.UsePartyTrick(card, _)
        | MatchAction.Attack(card, _) -> (state.Card card).MechanicalId.Value
        | _ -> "none"

    let private assertOfferedActions
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (engine: MatchEngine)
        (state: MatchState)
        =
        let previousContext = observation.Context

        for actor in state.Players |> Seq.map _.Id |> Seq.sortBy _.Value do
            let actions = engine.GetLegalActions(state, actor)

            for action in actions do
                observation.Context <-
                    $"probe={action.StableKey}; source={actionSource state action}"

                let openingPlayerFirstRound =
                    state.Phase = MatchPhase.Playing
                    && actor = state.OpeningPlayer
                    && (state.Player actor).RoundsStarted = 1

                if openingPlayerFirstRound then
                    enforce
                        observation
                        Clauses.openerAttack
                        seed
                        step
                        $"opening player's first-round action={action.StableKey}"
                        (match action.Command.Action with
                         | MatchAction.Attack _ -> false
                         | _ -> true)

                    enforce
                        observation
                        Clauses.openerMate
                        seed
                        step
                        $"opening player's first-round action={action.StableKey}"
                        (match action.Command.Action with
                         | MatchAction.PlayKit(kit, _) ->
                             not (mateIds.Contains((state.Card kit).MechanicalId))
                         | _ -> true)

                match action.Affordability with
                | ActionAffordability.Payable ->
                    let outcome =
                        applyWithEvidence engine state action seed step observation.Context

                    enforce
                        observation
                        Clauses.effectDoesNotRaise
                        seed
                        step
                        $"offered action={action.StableKey} applied without raising"
                        true

                    match outcome with
                    | CommandOutcome.Rejected(_, rejection) ->
                        observation.ProbeSettlements.Add
                            { Seed = seed
                              Step = step
                              Action = action.StableKey
                              Status = DirectActionRejected
                              SettlementCommands = 0
                              Detail = $"offered action rejected with {rejection.Code}" }

                        enforceEveryFailure
                            observation
                            Clauses.offeredActionApplies
                            seed
                            step
                            $"offered action={action.StableKey} rejected with {rejection.Code}"
                            false
                    | CommandOutcome.Applied(applied, appliedEvents) ->
                        enforceEveryFailure
                            observation
                            Clauses.offeredActionApplies
                            seed
                            step
                            $"offered action={action.StableKey} applied successfully"
                            true

                        let settlement =
                            settleTrial engine seed step observation.Context applied appliedEvents

                        observation.ProbeSettlements.Add
                            { Seed = seed
                              Step = step
                              Action = action.StableKey
                              Status = settlement.Status
                              SettlementCommands = settlement.Commands
                              Detail = settlement.Detail }

                        observeEffects observation settlement.Events
                        assertState observation seed step settlement.State

                        enforceEveryFailure
                            observation
                            Clauses.effectChoices
                            seed
                            step
                            $"offered action={action.StableKey}; settlement status={settlement.Status}; commands={settlement.Commands}; detail={settlement.Detail}"
                            (settlement.Status = Settled)

                        if settlement.Status = Settled then
                            enforceEveryFailure
                                observation
                                Clauses.offeredActionFunctional
                                seed
                                step
                                $"offered action={action.StableKey} must change the table or reveal hidden cards after complete deterministic settlement"
                                (semanticState settlement.State <> semanticState state
                                 || settlement.Events
                                    |> Seq.exists (fun event ->
                                        event.Kind = MatchEventKind.CardsRevealed))
                | ActionAffordability.ShortOfTaxiFare _ -> ()

        observation.Context <- previousContext

    let private assertActionPreconditions
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (state: MatchState)
        (action: LegalAction)
        =
        let actor = action.Command.Actor
        let counts = roundCounts observation actor state.RoundNumber

        match action.Command.Action with
        | MatchAction.AttachVim _ ->
            counts.Vim <- counts.Vim + 1

            enforce
                observation
                Clauses.vimPerRound
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; observed normal Vim attachments={counts.Vim}"
                (counts.Vim <= authority.BaseRules.Vim.NormalAttachmentPerRound)
        | MatchAction.PlayKit(kitId, _) ->
            let kit = state.Card kitId

            if mateIds.Contains kit.MechanicalId then
                counts.Mate <- counts.Mate + 1

                enforce
                    observation
                    Clauses.matePerRound
                    seed
                    step
                    $"actor={actor.Value}; round={state.RoundNumber}; observed Mates={counts.Mate}"
                    (counts.Mate <= authority.BaseRules.Kit.MatesPerRound)

            if localIds.Contains kit.MechanicalId then
                counts.Local <- counts.Local + 1

                enforce
                    observation
                    Clauses.localPerRound
                    seed
                    step
                    $"actor={actor.Value}; round={state.RoundNumber}; observed Locals={counts.Local}"
                    (counts.Local <= authority.BaseRules.Kit.LocalsPerRound)
        | MatchAction.Taxi(boothBloke, vimToChuck) ->
            counts.Taxi <- counts.Taxi + 1
            let outgoing = state.Oche actor
            let incoming = state.Card boothBloke

            enforce
                observation
                Clauses.taxiPerRound
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; observed taxis={counts.Taxi}"
                (counts.Taxi <= authority.BaseRules.Taxi.PerRound)

            enforce
                observation
                Clauses.taxiEligibility
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; incoming zone={incoming.Zone}; fare cards={vimToChuck.Length}"
                (incoming.Zone = CardZone.Booth
                 && (match outgoing with
                     | ValueSome card ->
                         card.RoughStates
                         |> Seq.exists (fun rough ->
                             rough.State = BlokemonRoughState.NoddedOff
                             || rough.State = BlokemonRoughState.Legless)
                         |> not
                     | ValueNone -> false))
        | MatchAction.Promote(promotionId, targetId) ->
            counts.Promotions <- counts.Promotions + 1
            let promotion = state.Card promotionId
            let target = state.Card targetId
            let player = state.Player actor

            let exactEdge =
                authority.Collectibles
                |> Array.find (fun candidate -> candidate.Id = promotion.MechanicalId.Value)
                |> fun card -> card.PromotesFromId = target.MechanicalId.Value

            let reconciledFirstRoundException =
                target.MechanicalId.Value = "BLK-021"
                && actor <> state.OpeningPlayer
                && (state.Effects
                    |> Seq.exists (fun effect ->
                        effect.SourceCard = target.Id
                        && effect.SourceEffect = EffectId "BLK-021-T01"
                        && effect.Kind = TemporaryEffectKind.ContinuousPartyTrick))

            enforce
                observation
                Clauses.promotionEdge
                seed
                step
                $"actor={actor.Value}; target={target.MechanicalId.Value}; promotion={promotion.MechanicalId.Value}"
                exactEdge

            enforce
                observation
                Clauses.promotionLimits
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; target={target.MechanicalId.Value}; rounds started={player.RoundsStarted}; entered={target.EnteredAtOwnerRound}; last promoted={target.LastPromotedRound}; BLK-021 override={reconciledFirstRoundException}"
                ((reconciledFirstRoundException
                  || (player.RoundsStarted > 1 && target.EnteredAtOwnerRound < player.RoundsStarted))
                 && target.LastPromotedRound <> state.RoundNumber)
        | _ -> ()

    let private updateRoundAction
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (before: MatchState)
        (after: MatchState)
        (events: ImmutableArray<MatchEvent>)
        (action: LegalAction)
        =
        match action.Command.Action with
        | MatchAction.Attack _ ->
            observation.PendingRoundAction <-
                Some
                    { Kind = LegalActionKind.Attack
                      ActivePlayer = before.ActivePlayer
                      RoundNumber = before.RoundNumber
                      SawRoundEnd = false }
        | MatchAction.UsePartyTrick _ ->
            observation.PendingRoundAction <-
                Some
                    { Kind = LegalActionKind.UsePartyTrick
                      ActivePlayer = before.ActivePlayer
                      RoundNumber = before.RoundNumber
                      SawRoundEnd = false }
        | _ -> ()

        match observation.PendingRoundAction with
        | None -> ()
        | Some pending ->
            if events |> Seq.exists (fun event -> event.Kind = MatchEventKind.RoundEnded) then
                pending.SawRoundEnd <- true

            let settled =
                after.Phase = MatchPhase.Complete
                || (after.Phase = MatchPhase.Playing
                    && after.PendingEffect.IsNone
                    && after.PendingKnockout.IsNone
                    && after.PendingBarChits.IsEmpty
                    && after.ReplacementPlayer.IsNone
                    && not after.PendingRoundEnd)

            if settled then
                match pending.Kind with
                | LegalActionKind.Attack ->
                    enforce
                        observation
                        Clauses.attackEndsRound
                        seed
                        step
                        $"Attack from round={pending.RoundNumber}; terminal={after.Phase = MatchPhase.Complete}; saw RoundEnded={pending.SawRoundEnd}"
                        (after.Phase = MatchPhase.Complete || pending.SawRoundEnd)
                | LegalActionKind.UsePartyTrick ->
                    enforce
                        observation
                        Clauses.partyTrickContinuesRound
                        seed
                        step
                        $"Party Trick from actor={pending.ActivePlayer.Value}; round={pending.RoundNumber}; after actor={after.ActivePlayer.Value}; round={after.RoundNumber}; saw RoundEnded={pending.SawRoundEnd}"
                        (not pending.SawRoundEnd
                         && (after.Phase = MatchPhase.Complete
                             || (after.ActivePlayer = pending.ActivePlayer
                                 && after.RoundNumber = pending.RoundNumber)))
                | other -> failwith $"Unexpected pending round action {other}."

                observation.PendingRoundAction <- None

    let private assertTransition
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (before: MatchState)
        (after: MatchState)
        (events: ImmutableArray<MatchEvent>)
        =
        enforce
            observation
            Clauses.monotonicRevision
            seed
            step
            $"revision before={before.Revision.Value}; after={after.Revision.Value}"
            (after.Revision = before.Revision.Next())

        for roundStarted in
            events |> Seq.filter (fun event -> event.Kind = MatchEventKind.RoundStarted) do
            let actor = roundStarted.Actor.Value

            let requiredDraw =
                events
                |> Seq.exists (fun event ->
                    event.Kind = MatchEventKind.CardsDrawn
                    && event.Actor = ValueSome actor
                    && event.DrawReason = ValueSome DrawReason.RequiredRoundDraw)

            let lostForShortStack =
                after.Phase = MatchPhase.Complete
                && after.Winner = ValueSome(after.Other actor)
                && (before.CardsIn(actor, CardZone.Stack) |> Seq.isEmpty)

            enforce
                observation
                Clauses.requiredRoundDraw
                seed
                step
                $"round-start actor={actor.Value}; required draw={requiredDraw}; lost for short stack={lostForShortStack}"
                (requiredDraw || lostForShortStack)

        for player in before.Players do
            let previous = player.BarChitsRemaining
            let current = (after.Player player.Id).BarChitsRemaining

            let suddenDeathReset =
                after.SuddenDeathCount = before.SuddenDeathCount + 1
                && current = authority.BaseRules.Win.SuddenDeathBarChits

            enforce
                observation
                Clauses.barChitsDoNotIncrease
                seed
                step
                $"player={player.Id.Value}; bar chits before={previous}; after={current}; sudden death={suddenDeathReset}"
                (current <= previous || suddenDeathReset)

    let private expectedBarChits (card: CardState) =
        if bigHitterIds.Contains card.MechanicalId.Value then
            authority.BaseRules.SendHome.BigHitterBarChits
        else
            authority.BaseRules.SendHome.NormalBarChits

    let private assertSendHomeAwards
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (before: MatchState)
        (after: MatchState)
        (events: ImmutableArray<MatchEvent>)
        =
        let remaining = Dictionary<PlayerId, int>()

        for player in before.Players do
            remaining[player.Id] <- player.BarChitsRemaining

        let eventArray = events |> Seq.toArray

        for index in 0 .. eventArray.Length - 1 do
            let sentHome = eventArray[index]

            if sentHome.Kind = MatchEventKind.BlokeSentHome then
                let cardId = sentHome.SourceCard.Value
                let card = after.Card cardId
                let takingPlayer = sentHome.Actor.Value

                enforce
                    observation
                    Clauses.sendHomeState
                    seed
                    step
                    $"sent home={card.MechanicalId.Value}; zone={card.Zone}; attachments={card.Attachments.Length}; underlying={card.UnderlyingCards.Length}"
                    (card.Zone = CardZone.EmptiesTray
                     && card.Attachments.IsEmpty
                     && card.UnderlyingCards.IsEmpty)

                let taken =
                    eventArray
                    |> Seq.skip (index + 1)
                    |> Seq.tryFind (fun candidate ->
                        candidate.Kind = MatchEventKind.BarChitsTaken
                        && candidate.SourceCard = ValueSome cardId)

                let expected = min (expectedBarChits card) remaining[takingPlayer]

                enforce
                    observation
                    (if bigHitterIds.Contains card.MechanicalId.Value then
                         Clauses.bigHitterAward
                     elif fossilIds.Contains card.MechanicalId.Value then
                         Clauses.fossilAward
                     else
                         Clauses.normalSendHomeAward)
                    seed
                    step
                    $"sent home={card.MechanicalId.Value}; taker={takingPlayer.Value}; expected available award={expected}"
                    (match taken with
                     | Some award -> award.Amount = expected
                     | None -> false)

            if sentHome.Kind = MatchEventKind.BarChitsTaken then
                match sentHome.Actor with
                | ValueSome actor -> remaining[actor] <- remaining[actor] - sentHome.Amount
                | ValueNone -> ()

    let private winMethodCount
        (before: MatchState)
        (after: MatchState)
        (events: ImmutableArray<MatchEvent>)
        (player: PlayerId)
        =
        let other = after.Other player

        let barChitsTaken =
            events
            |> Seq.filter (fun event ->
                event.Kind = MatchEventKind.BarChitsTaken && event.Actor = ValueSome player)
            |> Seq.sumBy _.Amount

        let tookLastBarChit =
            let available = (before.Player player).BarChitsRemaining
            available > 0 && barChitsTaken >= available

        let leftOtherWithoutBloke =
            after.Cards
            |> Seq.exists (fun card -> card.Owner = other && isInPlay card)
            |> not

        let otherFailedRequiredDraw =
            events
            |> Seq.exists (fun event ->
                event.Kind = MatchEventKind.RoundStarted && event.Actor = ValueSome other)
            && (events
                |> Seq.exists (fun event ->
                    event.Kind = MatchEventKind.CardsDrawn
                    && event.Actor = ValueSome other
                    && event.DrawReason = ValueSome DrawReason.RequiredRoundDraw)
                |> not)
            && (after.CardsIn(other, CardZone.Stack) |> Seq.isEmpty)

        [ tookLastBarChit; leftOtherWithoutBloke; otherFailedRequiredDraw ]
        |> List.filter id
        |> List.length

    let private assertEnding
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (before: MatchState)
        (after: MatchState)
        (events: ImmutableArray<MatchEvent>)
        =
        if
            events
            |> Seq.exists (fun event -> event.Kind = MatchEventKind.SuddenDeathStarted)
        then
            let firstMethods = winMethodCount before after events firstPlayer
            let secondMethods = winMethodCount before after events secondPlayer

            enforce
                observation
                Clauses.suddenDeath
                seed
                step
                $"sudden death count={after.SuddenDeathCount}; winner={after.Winner}; methods={firstMethods}:{secondMethods}"
                (after.SuddenDeathCount > before.SuddenDeathCount
                 && after.Winner.IsNone
                 && firstMethods > 0
                 && firstMethods = secondMethods
                 && after.Players
                    |> Seq.forall (fun player ->
                        player.BarChitsRemaining = authority.BaseRules.Win.SuddenDeathBarChits))

        if after.Phase = MatchPhase.Complete then
            match after.Winner with
            | ValueNone ->
                enforce
                    observation
                    Clauses.terminalMethods
                    seed
                    step
                    "completed rulebook self-play must name a winner"
                    false
            | ValueSome winner ->
                let winnerMethods = winMethodCount before after events winner
                let loserMethods = winMethodCount before after events (after.Other winner)

                enforce
                    observation
                    Clauses.terminalMethods
                    seed
                    step
                    $"winner={winner.Value}; winner methods={winnerMethods}; loser methods={loserMethods}"
                    (winnerMethods > 0 && winnerMethods > loserMethods)

    let private nextAction (engine: MatchEngine) (cpu: DeterministicCpu) (state: MatchState) =
        state.Players
        |> Seq.map _.Id
        |> Seq.sortBy _.Value
        |> Seq.tryPick (fun actor ->
            match cpu.Choose(engine, state, actor) with
            | CpuDecision.Selected action -> Some action
            | CpuDecision.NoLegalAction -> None)

    let runBout (seed: uint64) (stepCeiling: int) =
        let observation =
            { Assertions = 0
              Context = "start"
              PendingRoundAction = None
              ExpectedCardInstances = None
              Findings = ResizeArray()
              FindingClauses = HashSet(StringComparer.Ordinal)
              ClauseEvaluations = Dictionary(StringComparer.Ordinal)
              ProbeSettlements = ResizeArray()
              ObservedEffects = HashSet()
              RoundActions = Dictionary() }

        let request = requestFor seed
        assertDeck observation seed 0 request.FirstDeck
        assertDeck observation seed 0 request.SecondDeck

        let engine = MatchEngine authority
        let cpu = DeterministicCpu()

        let initialState, initialEvents =
            let start =
                try
                    engine.Start request
                with error ->
                    raise (
                        InvalidOperationException(
                            $"seed={seed}; step=0; clause={Clauses.stackSize.Id}; generated match start raised {error.GetType().Name}",
                            error
                        )
                    )

            match start with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected issues ->
                enforce
                    observation
                    Clauses.stackSize
                    seed
                    0
                    $"generated decks rejected: {String.Join(',', issues |> Seq.map _.Code)}"
                    false

                failwith
                    $"seed={seed}; step=0; clause={Clauses.stackSize.Id}; generated decks were rejected"

        observation.ExpectedCardInstances <- initialState.Cards |> Seq.map _.Id |> Set.ofSeq |> Some
        assertOpening observation seed initialState initialEvents
        assertState observation seed 0 initialState
        observeEffects observation initialEvents

        let events = ResizeArray<MatchEvent>(initialEvents)
        let mutable state = initialState
        let mutable steps = 0

        while state.Phase <> MatchPhase.Complete && steps < stepCeiling do
            assertOfferedActions observation seed steps engine state

            match nextAction engine cpu state with
            | None ->
                failwith
                    $"seed={seed}; step={steps}; deterministic self-play had no payable action before completion"
            | Some action ->
                let source = actionSource state action

                observation.Context <- $"action={action.StableKey}; source={source}"
                assertActionPreconditions observation seed steps state action

                let before = state

                let outcome = applyWithEvidence engine state action seed steps observation.Context

                enforce
                    observation
                    Clauses.effectDoesNotRaise
                    seed
                    steps
                    $"selected action={action.StableKey} applied without raising"
                    true

                let applied, appliedEvents =
                    match outcome with
                    | CommandOutcome.Applied(applied, appliedEvents) -> applied, appliedEvents
                    | CommandOutcome.Rejected(_, rejection) ->
                        enforceEveryFailure
                            observation
                            Clauses.offeredActionApplies
                            seed
                            steps
                            $"selected action={action.StableKey} rejected with {rejection.Code}"
                            false

                        failwith
                            $"seed={seed}; step={steps}; clause={Clauses.offeredActionApplies.Id}; selected action={action.StableKey} was rejected with {rejection.Code}"

                steps <- steps + 1
                assertTransition observation seed steps before applied appliedEvents
                assertSendHomeAwards observation seed steps before applied appliedEvents
                updateRoundAction observation seed steps before applied appliedEvents action
                assertEnding observation seed steps before applied appliedEvents
                observeEffects observation appliedEvents
                events.AddRange appliedEvents
                state <- applied
                assertState observation seed steps state

        { Seed = seed
          StepCeiling = stepCeiling
          Status =
            if state.Phase = MatchPhase.Complete then
                Completed
            else
                Incomplete
          StopReason =
            if state.Phase = MatchPhase.Complete then
                RuleCompleted
            else
                StepCeilingReached
          Steps = steps
          Assertions = observation.Assertions
          Findings = ImmutableArray.CreateRange observation.Findings
          ClauseEvaluations =
            observation.ClauseEvaluations
            |> Seq.map (fun pair ->
                { ClauseId = pair.Key
                  Count = pair.Value })
            |> Seq.sortBy _.ClauseId
            |> ImmutableArray.CreateRange
          ProbeSettlements = ImmutableArray.CreateRange observation.ProbeSettlements
          ObservedEffects =
            ImmutableArray.CreateRange(observation.ObservedEffects |> Seq.sortBy _.Value)
          StartRequest = request
          Events = ImmutableArray.CreateRange events
          FinalState = state }

    let defaultBout seed = runBout seed DefaultStepCeiling

    let canonicalEventBytes (events: ImmutableArray<MatchEvent>) =
        JsonSerializer.SerializeToUtf8Bytes(events, MatchJson.Options)

    let private errorCode (error: ApiError | null) =
        match error with
        | Null -> "unknown"
        | NonNull failure -> failure.Code

    let private choiceSelectionFor
        (requirement: MatchChoiceRequirementView)
        (accepted: Nullable<bool>)
        (amount: Nullable<int>)
        (cardIds: string array)
        (mechanicalType: string | null)
        (effectId: string | null)
        (distribution: MatchDamageAllocationRequest array)
        (attachments: MatchAttachmentRequest array)
        =
        MatchChoiceSelectionRequest(
            requirement.Id,
            requirement.Kind,
            accepted,
            amount,
            cardIds,
            mechanicalType,
            effectId,
            distribution,
            attachments
        )

    let private choiceSelection (requirement: MatchChoiceRequirementView) =
        let emptyBool = Nullable<bool>()
        let emptyInt = Nullable<int>()
        let noCards = Array.empty<string>
        let noDistribution = Array.empty<MatchDamageAllocationRequest>
        let noAttachments = Array.empty<MatchAttachmentRequest>

        let selection accepted amount cards mechanicalType effectId distribution attachments =
            choiceSelectionFor
                requirement
                accepted
                amount
                cards
                mechanicalType
                effectId
                distribution
                attachments

        if requirement.Kind = MatchChoiceKindView.Optional then
            selection (Nullable false) emptyInt noCards null null noDistribution noAttachments
        elif requirement.Kind = MatchChoiceKindView.Amount then
            selection
                emptyBool
                (Nullable requirement.Minimum)
                noCards
                null
                null
                noDistribution
                noAttachments
        elif requirement.Kind = MatchChoiceKindView.Cards then
            selection
                emptyBool
                emptyInt
                (requirement.EligibleCards
                 |> Seq.truncate requirement.Minimum
                 |> Seq.map _.Id
                 |> Seq.toArray)
                null
                null
                noDistribution
                noAttachments
        elif requirement.Kind = MatchChoiceKindView.MechanicalType then
            selection
                emptyBool
                emptyInt
                noCards
                requirement.EligibleMechanicalTypes[0].Value
                null
                noDistribution
                noAttachments
        elif requirement.Kind = MatchChoiceKindView.Attack then
            selection
                emptyBool
                emptyInt
                noCards
                null
                requirement.EligibleEffects[0].Id
                noDistribution
                noAttachments
        elif requirement.Kind = MatchChoiceKindView.Distribution then
            selection
                emptyBool
                emptyInt
                noCards
                null
                null
                [| MatchDamageAllocationRequest(
                       requirement.EligibleCards[0].Id,
                       requirement.Maximum
                   ) |]
                noAttachments
        elif requirement.Kind = MatchChoiceKindView.Attachments then
            selection
                emptyBool
                emptyInt
                noCards
                null
                null
                noDistribution
                (requirement.EligibleCards
                 |> Seq.truncate requirement.Minimum
                 |> Seq.map (fun card ->
                     MatchAttachmentRequest(card.Id, requirement.EligibleTargets[0].Id))
                 |> Seq.toArray)
        else
            failwith $"Unsupported persisted-replay choice kind {requirement.Kind}."

    let private actionRequest (matchView: MatchView) (action: MatchActionView) (commandId: Guid) =
        let localRequirements =
            action.ChoiceRequirements |> Array.filter _.Chooser.IsLocalPlayer

        let includeRequirement (requirement: MatchChoiceRequirementView) =
            match requirement.DependsOnOptional with
            | Null -> true
            | NonNull dependsOn ->
                localRequirements
                |> Array.find (fun parent -> parent.Id = dependsOn)
                |> fun parent -> parent.Kind <> MatchChoiceKindView.Optional

        ApplyMatchActionRequest(
            commandId,
            matchView.Frame.Revision,
            action.Id,
            localRequirements
            |> Array.filter includeRequirement
            |> Array.map choiceSelection
        )

    let private restoreProfile (catalogue: BlokemonCatalogue) (stored: StoredDocument | null) =
        match stored with
        | Null -> failwith "The App persistence route did not write a profile document."
        | NonNull document ->
            let options = JsonSerializerOptions(JsonSerializerDefaults.Web)

            match JsonSerializer.Deserialize<ProductDocument>(document.Json, options) with
            | Null -> failwith "The App profile document deserialized to null."
            | NonNull profileDocument ->
                match LocalProfile.Restore(profileDocument.Profile, catalogue.Mechanics) with
                | DomainResult.Failed failure ->
                    failwith $"The App profile document did not restore: {failure}."
                | DomainResult.Succeeded profile -> profile

    let private replayContext (catalogue: BlokemonCatalogue) (documents: IStateDocumentStore) =
        { Catalogue = catalogue
          Documents = documents
          Engine = MatchEngine(catalogue.Mechanics)
          Cpu = DeterministicCpu()
          Cached = null }

    let private replayStoredDocument
        (context: MatchContext)
        (profile: LocalProfile)
        (revision: int64)
        (document: MatchDocument)
        =
        let replayed = MatchReplay.replayDocument context profile revision document

        match replayed.Error, replayed.Match with
        | Null, NonNull loaded -> loaded
        | NonNull error, _ -> failwith $"MatchReplay.replayDocument failed: {error.Code}."
        | Null, Null -> failwith "MatchReplay.replayDocument returned no match and no error."

    let productionPersistedReplay () =
        task {
            let bootstrap =
                Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
                |> File.ReadAllText
                |> BlokemonCatalogue.FromBootstrapJson

            let documents = MemoryDocumentStore()
            let store = documents :> IStateDocumentStore
            let matches = LocalMatchService(bootstrap, store)

            let application =
                LocalApplicationService(
                    bootstrap,
                    store,
                    matches,
                    EconomyRules.Unlimited,
                    ProfileAuthorityPolicy.Preserve
                )

            let! created =
                application.CreateProfile(
                    CreateProfileRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000001",
                        "conformance replay"
                    )
                )

            if not created.Succeeded then
                failwith
                    $"Creating the deterministic replay profile failed: {errorCode created.Error}."

            let! claimed =
                application.ClaimStarterDeck(
                    ClaimStarterDeckRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000002",
                        "growroom"
                    )
                )

            if not claimed.Succeeded then
                failwith
                    $"Claiming the deterministic replay deck failed: {errorCode claimed.Error}."

            let! started =
                application.StartMatch(
                    StartMatchRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000003",
                        Guid.Parse "b16430b9-0c41-5bbf-a201-1ed29d1d9378"
                    )
                )

            if not started.Succeeded then
                failwith
                    $"Starting the deterministic persisted match failed: {errorCode started.Error}."

            let startedMutation = started.Value |> nonNull

            let startedView =
                match startedMutation.Application.Match with
                | Null -> failwith "The App persistence route started no match."
                | NonNull value -> value

            let action =
                startedView.LegalActions
                |> Array.find (fun candidate ->
                    candidate.Kind <> MatchActionKindView.Resign && isNull candidate.DisabledReason)

            let! applied =
                application.ApplyMatchAction(
                    startedView.Frame.Id,
                    actionRequest
                        startedView
                        action
                        (Guid.Parse "07900000-0000-0000-0000-000000000004")
                )

            if not applied.Succeeded then
                failwith
                    $"Applying the deterministic persisted human action failed: {errorCode applied.Error}."

            let! storedProfile = store.Read "profile"
            let profile = restoreProfile bootstrap storedProfile
            let! storedMatch = store.Read "match"

            let revision, document =
                match storedMatch with
                | Null -> failwith "The App persistence route did not write a MatchDocument."
                | NonNull stored ->
                    match
                        JsonSerializer.Deserialize<MatchDocument>(stored.Json, MatchJson.Options)
                    with
                    | Null -> failwith "MatchJson deserialized the persisted MatchDocument to null."
                    | NonNull parsed ->
                        stored.Revision, MatchDocumentNormalization.matchDocument parsed

            let first =
                replayStoredDocument (replayContext bootstrap store) profile revision document

            let second =
                replayStoredDocument (replayContext bootstrap store) profile revision document

            return
                { PersistedCommands = document.Commands.Length
                  FirstState = first.State
                  FirstEvents = first.Events
                  SecondState = second.State
                  SecondEvents = second.Events }
        }

    let coveredContent (seeds: uint64 array) =
        seeds
        |> Seq.collect (fun seed ->
            let request = requestFor seed
            Seq.append request.FirstDeck.Cards request.SecondDeck.Cards)
        |> Seq.map _.Value
        |> Seq.filter (fun id -> Array.contains id allContentIds)
        |> Set.ofSeq

    let assertNoFindings (results: BoutResult seq) =
        let findings =
            results
            |> Seq.collect (fun result -> result.Findings |> Seq.toArray)
            |> Seq.toArray

        (findings.Length = 0, String.concat Environment.NewLine findings)
        |> should equal (true, "")

    let writeApproximateCoverageReport
        (outputPath: string)
        (seeds: uint64 array)
        (stepCeiling: int)
        (results: BoutResult array)
        =
        let observed =
            results
            |> Seq.collect (fun result -> result.ObservedEffects |> Seq.toArray)
            |> Seq.map _.Value
            |> Set.ofSeq

        let unobserved = allPrograms |> Array.filter (observed.Contains >> not)
        let contentCoverage = coveredContent seeds
        let incomplete = results |> Array.filter (fun result -> result.Status = Incomplete)

        let findings =
            results |> Array.collect (fun result -> result.Findings |> Seq.toArray)

        let probes =
            results |> Array.collect (fun result -> result.ProbeSettlements |> Seq.toArray)

        let probeCount status =
            probes |> Array.filter (fun probe -> probe.Status = status) |> Array.length

        let unsettledProbes = probes |> Array.filter (fun probe -> probe.Status <> Settled)

        let clauseEvaluations = Dictionary<string, int>(StringComparer.Ordinal)

        for result in results do
            for evaluation in result.ClauseEvaluations do
                clauseEvaluations[evaluation.ClauseId] <-
                    match clauseEvaluations.TryGetValue evaluation.ClauseId with
                    | true, count -> count + evaluation.Count
                    | false, _ -> evaluation.Count

        let longestBout = results |> Array.maxBy _.Steps |> _.Steps

        let ceilingRationale =
            if stepCeiling > longestBout * 2 then
                $"leaves more than 2x headroom over the longest {longestBout}-command observed bout for deferred choices and triggers"
            else
                $"records the longest {longestBout}-command observed bout without treating a ceiling hit as a rules failure"

        let lines = ResizeArray<string>()
        let programCount = allPrograms.Length
        lines.Add "# Self-play program attribution"
        lines.Add ""
        lines.Add $"- Authority programs: {programCount}"
        lines.Add $"- Effect-attributed program IDs: {observed.Count}/{programCount}"

        lines.Add
            $"- Unobserved or event-unobservable program IDs: {unobserved.Length}/{programCount}"

        lines.Add "- Coverage mode: APPROXIMATE"

        lines.Add
            "- Interpretation: an unobserved program is not proof that the program did not execute."

        lines.Add
            "- Companion authorities: content/authorities/mechanics.json; content/reference/sv151-authority-reconciliation.json"

        lines.Add
            "- Reason: MatchEvent.Effect records effectful events, but accepted program invocation has no universal event; continuous refresh and multi-rule Kit execution can be unobservable when no instruction emits an effect event."

        lines.Add
            "- Coverage population: selected self-play commands plus explicitly settled deterministic probes for every payable action offered in each reached state; only an emitted MatchEvent.Effect is labelled attributed."

        lines.Add $"- Seed count: {seeds.Length}"
        lines.Add $"- Seed set: {String.Join(',', seeds)}"

        lines.Add(
            if seeds = DefaultSeeds then
                "- Seed rationale: the three default seeds are the minimum recorded 39-card cyclic deck windows whose two sides cover all 165 content identities while retaining 21 Basic Vim per deck."
            else
                "- Seed rationale: the opt-in arithmetic progression broadens action/effect sampling while preserving deterministic, greppable seeds and the same playable deck construction."
        )

        lines.Add
            $"- Step ceiling: {stepCeiling} commands per bout (run control only; not a game rule)"

        lines.Add $"- Ceiling rationale: {ceilingRationale}"
        lines.Add $"- Content cards in seeded decks: {contentCoverage.Count}/{allContentIds.Length}"
        lines.Add $"- Completed bouts: {results.Length - incomplete.Length}"
        lines.Add $"- INCOMPLETE bouts: {incomplete.Length}"
        lines.Add $"- Rule findings: {findings.Length}"

        lines.Add
            "- Finding retention: first ordinary failure per rulebook clause and bout; every offered-action and settlement-probe failure retained"

        lines.Add "- Exclusions: none"
        lines.Add ""
        lines.Add "## Bouts"
        lines.Add ""

        for result in results do
            lines.Add(
                $"- seed {result.Seed}: {result.Status.ToString().ToUpperInvariant()}, reason={result.StopReason}, steps={result.Steps}, assertions={result.Assertions}, final-revision={result.FinalState.Revision.Value}"
            )

        lines.Add ""
        lines.Add "## Seeded deck records"
        lines.Add ""

        for result in results do
            let first =
                result.StartRequest.FirstDeck.Cards |> Seq.map _.Value |> String.concat ","

            let second =
                result.StartRequest.SecondDeck.Cards |> Seq.map _.Value |> String.concat ","

            lines.Add $"- seed {result.Seed}, fuzz-first: {first}"
            lines.Add $"- seed {result.Seed}, fuzz-second: {second}"

        lines.Add ""
        lines.Add "## Rulebook clause map"
        lines.Add ""

        for clause in Clauses.All do
            let evaluations = clauseEvaluations.GetValueOrDefault(clause.Id, 0)

            lines.Add
                $"- {clause.Group} | {clause.Id} | {clause.Lines} | accepted: {clause.AcceptedAssertion} | authority: {clause.Rule} | check: {clause.Check} | sweep evaluations: {evaluations}"

        lines.Add ""
        lines.Add "## Offered-action probe settlement statuses"
        lines.Add ""
        lines.Add $"- Total payable offered-action probes: {probes.Length}"
        lines.Add $"- Settled: {probeCount Settled}"
        lines.Add $"- DirectActionRejected: {probeCount DirectActionRejected}"
        lines.Add $"- NoSettlementAction: {probeCount NoSettlementAction}"
        lines.Add $"- SettlementActionRejected: {probeCount SettlementActionRejected}"
        lines.Add $"- SettlementCeilingReached: {probeCount SettlementCeilingReached}"
        lines.Add ""
        lines.Add "### Unsettled probe details"
        lines.Add ""

        if unsettledProbes.Length = 0 then
            lines.Add "- none"
        else
            for probe in unsettledProbes do
                lines.Add
                    $"- seed={probe.Seed}; step={probe.Step}; action={probe.Action}; status={probe.Status}; settlement-commands={probe.SettlementCommands}; detail={probe.Detail}"

        lines.Add ""
        lines.Add "## INCOMPLETE seeds"
        lines.Add ""

        if incomplete.Length = 0 then
            lines.Add "- none"
        else
            for result in incomplete do
                lines.Add
                    $"- {result.Seed} at {result.Steps}/{result.StepCeiling} commands; reason={result.StopReason}"

        lines.Add ""
        lines.Add "## Rule findings"
        lines.Add ""

        if findings.Length = 0 then
            lines.Add "- none"
        else
            for finding in findings do
                lines.Add $"- {finding}"

        lines.Add ""
        lines.Add "## Effect-attributed program IDs"
        lines.Add ""

        for effect in observed |> Set.toArray |> Array.sort do
            lines.Add $"- {effect}"

        lines.Add ""
        lines.Add "## Unobserved or event-unobservable programs"
        lines.Add ""

        for effect in unobserved do
            lines.Add $"- {effect}"

        match Path.GetDirectoryName outputPath with
        | null
        | "" -> ()
        | parent -> Directory.CreateDirectory parent |> ignore

        File.WriteAllText(outputPath, String.concat "\n" lines + "\n")

        { ObservedPrograms = observed.Count
          UnobservedPrograms = unobserved.Length
          CompletedBouts = results.Length - incomplete.Length
          IncompleteBouts = incomplete.Length
          Findings = findings.Length }
