namespace Blokemon.Game.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Reflection
open System.Text
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
          Heading: string
          Lines: string
          Rule: string }

    module Clauses =

        let mechanicalAuthority =
            { Id = "TR-AUTHORITY-7-9"
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:7-9"
              Rule =
                "The declarative authority supplies the complete base rules and validates all 310 programs against executable shapes." }

        let stack =
            { Id = "TR-STACK-15"
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:15"
              Rule =
                "Each side has 60 cards, at most four of one identity except Basic Vim, and a Regular Bloke." }

        let opening =
            { Id = "TR-OPENING-16"
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:16"
              Rule =
                "Sample the opener first; draw seven, place one Regular at the oche, up to five in the booth, and six bar chits." }

        let mulligan =
            { Id = "TR-MULLIGAN-17"
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:17"
              Rule =
                "Redraw illegal mitts; simultaneous mulligans give no bonus and each excess mulligan allows one extra card." }

        let openerLimits =
            { Id = "TR-OPENER-18"
              Heading = "Stack and opening"
              Lines = "technical-rulebook.md:18"
              Rule = "The opening side cannot play a Mate or declare an Attack in its first round." }

        let roundActions =
            { Id = "TR-ROUND-23"
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:22-23"
              Rule =
                "A required draw opens a round; an Attack ends it, a Party Trick does not, and one normal Vim may attach." }

        let promotion =
            { Id = "TR-PROMOTION-24"
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:24"
              Rule =
                "Promotion needs the exact edge and is forbidden on first rounds, a Bloke's first round in play, or twice for one Bloke in a round, except BLK-021's companion-authority second-opener ability." }

        let kits =
            { Id = "TR-KITS-25"
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:25"
              Rule =
                "A Bloke has at most one Bar Kit; one Mate and Local may be played per round; only one Local is in play." }

        let taxi =
            { Id = "TR-TAXI-26"
              Heading = "Round, promotion, Vim, kits and taxi"
              Lines = "technical-rulebook.md:26"
              Rule =
                "Taxi is once per round, needs a booth Bloke and its fare, and is barred by NoddedOff or Legless." }

        let roughStateLocation =
            { Id = "TR-ROUGH-52"
              Heading = "Rough states and checkup"
              Lines = "technical-rulebook.md:52"
              Rule = "Only the oche Bloke has rough states." }

        let roughStateCoexistence =
            { Id = "TR-ROUGH-59"
              Heading = "Rough states and checkup"
              Lines = "technical-rulebook.md:59"
              Rule =
                "Only one rotated state applies; Singed and DodgyPint may coexist with it and each other." }

        let sendHome =
            { Id = "TR-SEND-HOME-63"
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:63"
              Rule =
                "Damage at least staying power sends a Bloke and its attachments home and awards the stated bar chits." }

        let damage =
            { Id = "TR-DAMAGE-46"
              Heading = "Attack and damage ordering"
              Lines = "technical-rulebook.md:46"
              Rule = "Calculated damage is clamped at zero before counters are placed." }

        let terminal =
            { Id = "TR-TERMINAL-65"
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:65"
              Rule =
                "Only the three win methods and their simultaneous sudden-death rule end rulebook self-play." }

        let fossilsAndBigHitters =
            { Id = "TR-BIG-HITTER-67"
              Heading = "Send home, bar chits and terminal outcomes"
              Lines = "technical-rulebook.md:67"
              Rule = "Fossils award one bar chit and the twelve listed Big Hitters award two." }

        let deterministicState =
            { Id = "TR-STATE-9"
              Heading = "Authority and boundaries"
              Lines = "technical-rulebook.md:9"
              Rule =
                "The game persists deterministic random state, choices, trigger timing, and command identities in MatchState." }

        let All =
            [| mechanicalAuthority
               stack
               opening
               mulligan
               openerLimits
               roundActions
               promotion
               kits
               taxi
               damage
               roughStateLocation
               roughStateCoexistence
               sendHome
               terminal
               fossilsAndBigHitters
               deterministicState |]

    type BoutStatus =
        | Completed
        | Incomplete

    type BoutStopReason =
        | RuleCompleted
        | StepCeilingReached
        | PolicyStalled

    type PersistedStep = { Actor: string; StableKey: string }

    type PersistedDeck = { Owner: string; Cards: string array }

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
          Findings: ResizeArray<string>
          FindingClauses: HashSet<string>
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
          ObservedEffects: ImmutableArray<EffectId>
          StartRequest: MatchStartRequest
          Commands: ImmutableArray<PersistedStep>
          Events: ImmutableArray<MatchEvent>
          FinalState: MatchState }

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

    let private recordAssertion (observation: Observation) =
        observation.Assertions <- observation.Assertions + 1

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
        recordAssertion observation

        if not condition && observation.FindingClauses.Add clause.Id then
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

    let private bigHitterIds = authority.BaseRules.BigHitters.BlokeIds |> Set.ofArray

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

    let private rankMatches (effect: TemporaryEffect) (card: CardState) =
        if card.Kind <> CardKind.Bloke then
            not (
                effect.Conditions
                |> Seq.exists (fun condition ->
                    condition = BlokemonCondition.TargetIsRegular
                    || condition = BlokemonCondition.TargetIsSeasoned
                    || condition = BlokemonCondition.TargetIsLandlord)
            )
        else
            let rank =
                authority.Collectibles
                |> Array.find (fun candidate -> candidate.Id = card.MechanicalId.Value)
                |> _.Rank

            (not (Seq.contains BlokemonCondition.TargetIsRegular effect.Conditions)
             || rank = BlokemonRank.Regular)
            && (not (Seq.contains BlokemonCondition.TargetIsSeasoned effect.Conditions)
                || rank = BlokemonRank.Seasoned)
            && (not (Seq.contains BlokemonCondition.TargetIsLandlord effect.Conditions)
                || rank = BlokemonRank.Landlord)

    let private stayingPower (state: MatchState) (card: CardState) =
        let printed =
            if card.Kind = CardKind.Bloke then
                authority.Collectibles
                |> Array.find (fun candidate -> candidate.Id = card.MechanicalId.Value)
                |> _.StayingPower
            else
                authority.BaseRules.FossilKits.PlayAsRegularLocalStayingPower

        printed
        + (state.Effects
           |> Seq.filter (fun effect ->
               effect.TargetCard = ValueSome card.Id
               && effect.Kind = TemporaryEffectKind.ModifyStayingPower
               && rankMatches effect card)
           |> Seq.sumBy _.Amount)

    let private assertDeck
        (observation: Observation)
        (seed: uint64)
        (step: int)
        (deck: FrozenDeckSnapshot)
        =
        enforce
            observation
            Clauses.stack
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
                Clauses.stack
                seed
                step
                $"deck owner={deck.Owner.Value}; card={mechanicalId.Value}; copies={count}; limit={limit}"
                (count <= limit)

        enforce
            observation
            Clauses.stack
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
            Clauses.opening
            seed
            0
            $"opening player={state.OpeningPlayer.Value}; expected first RNG sample={expectedOpening.Value}"
            (state.OpeningPlayer = expectedOpening)

        for player in state.Players do
            let mitt = state.CardsIn(player.Id, CardZone.Mitt) |> Seq.toArray

            enforce
                observation
                Clauses.opening
                seed
                0
                $"player={player.Id.Value}; opening mitt={mitt.Length}"
                (mitt.Length = authority.BaseRules.Opening.MittSize)

            enforce
                observation
                Clauses.mulligan
                seed
                0
                $"player={player.Id.Value}; final redrawn mitt must contain a Regular Bloke"
                (mitt |> Array.exists (fun card -> regularIds.Contains card.MechanicalId))

            let other = state.Player(state.Other player.Id)
            let expectedAllowance = max 0 (other.MulliganCount - player.MulliganCount)

            enforce
                observation
                Clauses.mulligan
                seed
                0
                $"player={player.Id.Value}; mulligans={player.MulliganCount}; allowance={player.MulliganBonusAllowance}; expected={expectedAllowance}"
                (player.MulliganBonusAllowance = expectedAllowance)

        for revealed in
            events |> Seq.filter (fun event -> event.Kind = MatchEventKind.CardsRevealed) do
            enforce
                observation
                Clauses.mulligan
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
        let ids = state.Cards |> Seq.map _.Id |> Seq.toArray
        let distinctIds = ids |> Seq.distinct |> Seq.length

        enforce
            observation
            Clauses.mechanicalAuthority
            seed
            step
            $"card conservation requires 120 unique instances; count={ids.Length}; unique={distinctIds}"
            (ids.Length = 120 && distinctIds = 120)

        for parent in state.Cards do
            for child in parent.Attachments do
                let related = state.Cards |> Seq.tryFind (fun card -> card.Id = child)

                enforce
                    observation
                    Clauses.mechanicalAuthority
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
                    Clauses.mechanicalAuthority
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
                    Clauses.mechanicalAuthority
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
                    Clauses.mechanicalAuthority
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
                Clauses.opening
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
                    Clauses.opening
                    seed
                    step
                    $"player={player.Id.Value}; continuing play requires exactly one oche Bloke; count={oche.Length}"
                    (oche.Length = authority.BaseRules.Opening.OcheRegularCount)

            if player.OpeningChosen && state.Phase = MatchPhase.OpeningPlacement then
                enforce
                    observation
                    Clauses.opening
                    seed
                    step
                    $"player={player.Id.Value}; opening oche and booth cards must all be Regular"
                    (oche.Length = 1
                     && regularIds.Contains oche[0].MechanicalId
                     && (booth |> Seq.forall (fun card -> regularIds.Contains card.MechanicalId)))

            let locals =
                state.Cards
                |> Seq.filter (fun card -> card.Owner = player.Id && card.Zone = CardZone.Local)
                |> Seq.length

            enforce
                observation
                Clauses.kits
                seed
                step
                $"player={player.Id.Value}; Locals={locals}"
                (locals <= 1)

            let barChits = state.CardsIn(player.Id, CardZone.BarChit) |> Seq.length

            enforce
                observation
                Clauses.opening
                seed
                step
                $"player={player.Id.Value}; bar chits={player.BarChitsRemaining}; zoned={barChits}"
                (player.BarChitsRemaining >= 0
                 && player.BarChitsRemaining <= authority.BaseRules.Opening.BarChitCount
                 && (not player.OpeningChosen
                     || state.Phase = MatchPhase.OpeningPlacement
                     || state.Phase = MatchPhase.Complete
                     || barChits = player.BarChitsRemaining))

        for card in state.Cards do
            enforce
                observation
                Clauses.damage
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

            if isInPlay card && state.Phase <> MatchPhase.Complete then
                let attachedBarKits =
                    card.Attachments
                    |> Seq.map state.Card
                    |> Seq.filter (fun attached -> barKitIds.Contains attached.MechanicalId)
                    |> Seq.length

                enforce
                    observation
                    Clauses.kits
                    seed
                    step
                    $"card={card.Id.Value}; attached Bar Kits={attachedBarKits}"
                    (attachedBarKits <= authority.BaseRules.Kit.BarKitsPerBloke)

                let pendingSendHome =
                    match state.PendingKnockout with
                    | ValueSome pending ->
                        pending.KnockedOutCard = card.Id
                        || Seq.contains card.Id pending.RemainingKnockouts
                    | ValueNone -> false

                enforce
                    observation
                    Clauses.sendHome
                    seed
                    step
                    $"card={card.Id.Value}; damage={card.Damage}; staying power={stayingPower state card}; pending={pendingSendHome}"
                    (pendingSendHome || card.Damage < stayingPower state card)

        enforce
            observation
            Clauses.roundActions
            seed
            step
            $"round={state.RoundNumber}; recorded Vim attachments={state.RoundUsage.VimAttachments}"
            (state.RoundUsage.VimAttachments
             <= authority.BaseRules.Vim.NormalAttachmentPerRound)

        enforce
            observation
            Clauses.kits
            seed
            step
            $"round={state.RoundNumber}; Mates={state.RoundUsage.MatesPlayed}; Locals={state.RoundUsage.LocalsPlayed}"
            (state.RoundUsage.MatesPlayed <= authority.BaseRules.Kit.MatesPerRound
             && state.RoundUsage.LocalsPlayed <= authority.BaseRules.Kit.LocalsPerRound)

        enforce
            observation
            Clauses.taxi
            seed
            step
            $"round={state.RoundNumber}; taxis={state.RoundUsage.TaxisUsed}"
            (state.RoundUsage.TaxisUsed <= authority.BaseRules.Taxi.PerRound)

        let localsInPlay =
            state.Cards |> Seq.filter (fun card -> card.Zone = CardZone.Local) |> Seq.length

        enforce
            observation
            Clauses.kits
            seed
            step
            $"global Locals in play={localsInPlay}"
            (localsInPlay <= 1)

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
                    $"seed={seed}; step={step}; clause={Clauses.mechanicalAuthority.Id}; context={context}; action={action.StableKey} raised {error.GetType().Name}",
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
        let mutable stalled = false

        while state.Phase <> MatchPhase.Complete
              && hasPendingResolution state
              && commands < 32
              && not stalled do
            let action =
                state.Players
                |> Seq.map _.Id
                |> Seq.sortBy _.Value
                |> Seq.tryPick (fun actor ->
                    match cpu.Choose(engine, state, actor) with
                    | CpuDecision.Selected selected -> Some selected
                    | CpuDecision.NoLegalAction -> None)

            match action with
            | None -> stalled <- true
            | Some selected ->
                match applyWithEvidence engine state selected seed step context with
                | CommandOutcome.Rejected _ -> stalled <- true
                | CommandOutcome.Applied(applied, appliedEvents) ->
                    state <- applied
                    events.AddRange appliedEvents
                    commands <- commands + 1

        state, ImmutableArray.CreateRange events

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
                    let isForbidden =
                        match action.Command.Action with
                        | MatchAction.Attack _ -> true
                        | MatchAction.PlayKit(kit, _) ->
                            mateIds.Contains((state.Card kit).MechanicalId)
                        | _ -> false

                    enforce
                        observation
                        Clauses.openerLimits
                        seed
                        step
                        $"opening player's first-round action={action.StableKey}"
                        (not isForbidden)

                match action.Affordability with
                | ActionAffordability.Payable ->
                    let outcome =
                        applyWithEvidence engine state action seed step observation.Context

                    match outcome with
                    | CommandOutcome.Rejected(_, rejection) ->
                        enforce
                            observation
                            Clauses.mechanicalAuthority
                            seed
                            step
                            $"offered action={action.StableKey} rejected with {rejection.Code}"
                            false
                    | CommandOutcome.Applied(applied, appliedEvents) ->
                        let settled, trialEvents =
                            settleTrial engine seed step observation.Context applied appliedEvents

                        observeEffects observation trialEvents
                        assertState observation seed step settled

                        enforce
                            observation
                            Clauses.mechanicalAuthority
                            seed
                            step
                            $"offered action={action.StableKey} must change the table or reveal hidden cards after deterministic settlement"
                            (semanticState settled <> semanticState state
                             || trialEvents
                                |> Seq.exists (fun event ->
                                    event.Kind = MatchEventKind.CardsRevealed))
                | ActionAffordability.ShortOfTaxiFare fare ->
                    let rejectedForFare =
                        match
                            applyWithEvidence engine state action seed step observation.Context
                        with
                        | CommandOutcome.Rejected(_, rejection) ->
                            rejection.Code = CommandRejectionCode.InvalidTaxiFare
                        | CommandOutcome.Applied _ -> false

                    enforce
                        observation
                        Clauses.taxi
                        seed
                        step
                        $"unaffordable UI taxi={action.StableKey}; fare={fare} must remain non-submittable"
                        rejectedForFare

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
                Clauses.roundActions
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
                    Clauses.kits
                    seed
                    step
                    $"actor={actor.Value}; round={state.RoundNumber}; observed Mates={counts.Mate}"
                    (counts.Mate <= authority.BaseRules.Kit.MatesPerRound)

            if localIds.Contains kit.MechanicalId then
                counts.Local <- counts.Local + 1

                enforce
                    observation
                    Clauses.kits
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
                Clauses.taxi
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; observed taxis={counts.Taxi}; incoming zone={incoming.Zone}; fare cards={vimToChuck.Length}"
                (counts.Taxi <= authority.BaseRules.Taxi.PerRound
                 && incoming.Zone = CardZone.Booth
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
                Clauses.promotion
                seed
                step
                $"actor={actor.Value}; round={state.RoundNumber}; target={target.MechanicalId.Value}; promotion={promotion.MechanicalId.Value}; rounds started={player.RoundsStarted}; entered={target.EnteredAtOwnerRound}; last promoted={target.LastPromotedRound}; observed promotions={counts.Promotions}"
                (exactEdge
                 && (reconciledFirstRoundException
                     || (player.RoundsStarted > 1
                         && target.EnteredAtOwnerRound < player.RoundsStarted))
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
                        Clauses.roundActions
                        seed
                        step
                        $"Attack from round={pending.RoundNumber}; terminal={after.Phase = MatchPhase.Complete}; saw RoundEnded={pending.SawRoundEnd}"
                        (after.Phase = MatchPhase.Complete || pending.SawRoundEnd)
                | LegalActionKind.UsePartyTrick ->
                    enforce
                        observation
                        Clauses.roundActions
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
            Clauses.deterministicState
            seed
            step
            $"revision before={before.Revision.Value}; after={after.Revision.Value}"
            (after.Revision = before.Revision.Next())

        let sequences = events |> Seq.map _.Sequence |> Seq.toArray

        enforce
            observation
            Clauses.deterministicState
            seed
            step
            $"event sequences after {before.LastEventSequence}: {String.Join(',', sequences)}"
            (sequences.Length > 0
             && (sequences |> Seq.pairwise |> Seq.forall (fun (left, right) -> right > left))
             && sequences[0] > before.LastEventSequence
             && sequences[sequences.Length - 1] = after.LastEventSequence)

        enforce
            observation
            Clauses.deterministicState
            seed
            step
            "all command events must carry the committed revision"
            (events |> Seq.forall (fun event -> event.Revision = after.Revision))

        let committed = events[events.Length - 1]

        enforce
            observation
            Clauses.deterministicState
            seed
            step
            "only the terminal event may carry the committed state"
            (committed.Kind = MatchEventKind.StateCommitted
             && committed.CommittedState = ValueSome after
             && (events
                 |> Seq.take (events.Length - 1)
                 |> Seq.forall (fun event -> event.CommittedState.IsNone)))

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
                Clauses.roundActions
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
                Clauses.terminal
                seed
                step
                $"player={player.Id.Value}; bar chits before={previous}; after={current}; sudden death={suddenDeathReset}"
                (current <= previous || suddenDeathReset)

    let private expectedBarChits (card: CardState) =
        if bigHitterIds.Contains card.MechanicalId.Value then
            authority.BaseRules.BigHitters.SentHomeBarChits
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
                    Clauses.sendHome
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
                         Clauses.fossilsAndBigHitters
                     else
                         Clauses.sendHome)
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
                Clauses.terminal
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
            let winner = after.Winner

            enforce
                observation
                Clauses.terminal
                seed
                step
                "completed rulebook self-play must name a winner"
                winner.IsSome

            let winnerMethods = winMethodCount before after events winner.Value
            let loserMethods = winMethodCount before after events (after.Other winner.Value)

            enforce
                observation
                Clauses.terminal
                seed
                step
                $"winner={winner.Value.Value}; winner methods={winnerMethods}; loser methods={loserMethods}"
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
              Findings = ResizeArray()
              FindingClauses = HashSet(StringComparer.Ordinal)
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
                            $"seed={seed}; step=0; clause={Clauses.stack.Id}; generated match start raised {error.GetType().Name}",
                            error
                        )
                    )

            match start with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected issues ->
                enforce
                    observation
                    Clauses.stack
                    seed
                    0
                    $"generated decks rejected: {String.Join(',', issues |> Seq.map _.Code)}"
                    false

                failwith
                    $"seed={seed}; step=0; clause={Clauses.stack.Id}; generated decks were rejected"

        assertOpening observation seed initialState initialEvents
        assertState observation seed 0 initialState
        observeEffects observation initialEvents

        let events = ResizeArray<MatchEvent>(initialEvents)
        let commands = ResizeArray<PersistedStep>()
        let mutable state = initialState
        let mutable steps = 0
        let mutable stalled = false

        while state.Phase <> MatchPhase.Complete && steps < stepCeiling && not stalled do
            assertOfferedActions observation seed steps engine state

            match nextAction engine cpu state with
            | None -> stalled <- true
            | Some action ->
                let source = actionSource state action

                observation.Context <- $"action={action.StableKey}; source={source}"
                assertActionPreconditions observation seed steps state action

                commands.Add
                    { Actor = action.Command.Actor.Value
                      StableKey = action.StableKey }

                let before = state

                let applied, appliedEvents =
                    match applyWithEvidence engine state action seed steps observation.Context with
                    | CommandOutcome.Applied(applied, appliedEvents) -> applied, appliedEvents
                    | CommandOutcome.Rejected(_, rejection) ->
                        enforce
                            observation
                            Clauses.mechanicalAuthority
                            seed
                            steps
                            $"selected action={action.StableKey} rejected with {rejection.Code}"
                            false

                        failwith
                            $"seed={seed}; step={steps}; clause={Clauses.mechanicalAuthority.Id}; selected action={action.StableKey} was rejected with {rejection.Code}"

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
            if state.Phase = MatchPhase.Complete then RuleCompleted
            elif steps >= stepCeiling then StepCeilingReached
            else PolicyStalled
          Steps = steps
          Assertions = observation.Assertions
          Findings = ImmutableArray.CreateRange observation.Findings
          ObservedEffects =
            ImmutableArray.CreateRange(observation.ObservedEffects |> Seq.sortBy _.Value)
          StartRequest = request
          Commands = ImmutableArray.CreateRange commands
          Events = ImmutableArray.CreateRange events
          FinalState = state }

    let private defaultSweep =
        lazy (DefaultSeeds |> Array.map (fun seed -> runBout seed DefaultStepCeiling))

    let defaultSweepResults () = defaultSweep.Value

    let defaultBout seed = runBout seed DefaultStepCeiling

    let canonicalEventBytes (events: ImmutableArray<MatchEvent>) =
        events |> sprintf "%A" |> Encoding.UTF8.GetBytes

    let private cachedState (service: LocalMatchService) =
        let flags = BindingFlags.Instance ||| BindingFlags.NonPublic

        let context =
            typeof<LocalMatchService>.GetFields(flags)
            |> Array.find (fun field -> field.Name.Contains("context", StringComparison.Ordinal))
            |> fun field ->
                match field.GetValue service with
                | Null -> failwith "The production match service had no replay context."
                | NonNull value -> value

        let property name =
            match
                context
                    .GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic
                    )
            with
            | Null -> failwith $"The production replay context had no {name} property."
            | NonNull value -> value

        let cached =
            match (property "Cached").GetValue context with
            | Null -> failwith "The production persisted-match path did not cache a replayed match."
            | NonNull value -> value

        match
            cached
                .GetType()
                .GetProperty(
                    "State",
                    BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic
                )
        with
        | Null -> failwith "The production replay cache had no final State property."
        | NonNull stateProperty ->
            match stateProperty.GetValue cached with
            | :? MatchState as state -> state
            | _ -> failwith "The production replay cache did not contain a MatchState."

    let private errorCode (error: ApiError | null) =
        match error with
        | Null -> "unknown"
        | NonNull failure -> failure.Code

    let productionPersistedReplay () =
        task {
            let bootstrap =
                Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
                |> File.ReadAllText
                |> BlokemonCatalogue.FromBootstrapJson

            let documents = MemoryDocumentStore()
            let matches = LocalMatchService(bootstrap, documents)

            let application =
                LocalApplicationService(bootstrap, documents, matches, EconomyRules.Unlimited)

            let! created =
                application.CreateProfile(
                    CreateProfileRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000001",
                        "BLOKEMON-079 replay"
                    )
                )

            if not created.Succeeded then
                failwith $"The deterministic replay profile failed: {errorCode created.Error}."

            let! claimed =
                application.ClaimStarterDeck(
                    ClaimStarterDeckRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000002",
                        "growroom"
                    )
                )

            if not claimed.Succeeded then
                failwith $"The deterministic replay deck failed: {errorCode claimed.Error}."

            let! started =
                application.StartMatch(
                    StartMatchRequest(
                        Guid.Parse "07900000-0000-0000-0000-000000000003",
                        Guid.Parse "b16430b9-0c41-5bbf-a201-1ed29d1d9378"
                    )
                )

            if not started.Succeeded then
                failwith $"The deterministic persisted match failed: {errorCode started.Error}."

            let original = cachedState matches
            let replayMatches = LocalMatchService(bootstrap, documents)

            let replayApplication =
                LocalApplicationService(bootstrap, documents, replayMatches, EconomyRules.Unlimited)

            let! replayed = replayApplication.State()

            if not replayed.Succeeded then
                failwith
                    $"The production persisted-document replay failed: {errorCode replayed.Error}."

            return original, cachedState replayMatches
        }

    let persist (result: BoutResult) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)

        let writeDeck (name: string) (snapshot: FrozenDeckSnapshot) =
            writer.WritePropertyName name
            writer.WriteStartObject()
            writer.WriteString("Owner", snapshot.Owner.Value)
            writer.WritePropertyName "Cards"
            writer.WriteStartArray()

            for card in snapshot.Cards do
                writer.WriteStringValue card.Value

            writer.WriteEndArray()
            writer.WriteEndObject()

        writer.WriteStartObject()
        writer.WriteString("MatchId", result.StartRequest.MatchId.Value)
        writer.WriteNumber("Seed", result.Seed)
        writeDeck "FirstDeck" result.StartRequest.FirstDeck
        writeDeck "SecondDeck" result.StartRequest.SecondDeck
        writer.WritePropertyName "Steps"
        writer.WriteStartArray()

        for step in result.Commands do
            writer.WriteStartObject()
            writer.WriteString("Actor", step.Actor)
            writer.WriteString("StableKey", step.StableKey)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        stream.ToArray()

    let replayPersisted (bytes: byte array) =
        use persisted = JsonDocument.Parse bytes
        let root = persisted.RootElement

        let elementText (value: JsonElement) (name: string) =
            match value.GetString() with
            | Null -> failwith $"The persisted member {name} was null."
            | NonNull parsed -> parsed

        let text (value: JsonElement) (name: string) =
            elementText (value.GetProperty name) name

        let deck (name: string) =
            let value = root.GetProperty name

            { Owner = text value "Owner"
              Cards =
                value.GetProperty("Cards").EnumerateArray()
                |> Seq.map (fun card -> elementText card "Cards")
                |> Seq.toArray }

        let firstDeck = deck "FirstDeck"
        let secondDeck = deck "SecondDeck"

        let steps =
            root.GetProperty("Steps").EnumerateArray()
            |> Seq.map (fun value ->
                { Actor = text value "Actor"
                  StableKey = text value "StableKey" })
            |> Seq.toArray

        let request =
            { MatchId = MatchId(text root "MatchId")
              Seed = MatchSeed(root.GetProperty("Seed").GetUInt64())
              FirstDeck = FrozenDeckSnapshot.Create(PlayerId firstDeck.Owner, firstDeck.Cards)
              SecondDeck = FrozenDeckSnapshot.Create(PlayerId secondDeck.Owner, secondDeck.Cards) }

        let engine = MatchEngine authority

        let mutable state, startedEvents =
            match engine.Start request with
            | MatchStartOutcome.Started(state, events) -> state, events
            | MatchStartOutcome.Rejected _ -> failwith "The persisted start request was rejected."

        let events = ResizeArray<MatchEvent>(startedEvents)

        for recorded in steps do
            let actor = PlayerId recorded.Actor

            let action =
                engine.GetLegalActions(state, actor)
                |> Seq.filter (fun candidate -> candidate.StableKey = recorded.StableKey)
                |> Seq.exactlyOne

            match engine.Apply(state, action.Command) with
            | CommandOutcome.Applied(applied, appliedEvents) ->
                state <- applied
                events.AddRange appliedEvents
            | CommandOutcome.Rejected(_, rejection) ->
                failwith $"Persisted action {recorded.StableKey} rejected with {rejection.Code}."

        state, ImmutableArray.CreateRange events

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

    let coverageReport
        (filename: string)
        (seeds: uint64 array)
        (stepCeiling: int)
        (results: BoutResult array)
        =
        let observed =
            results
            |> Seq.collect (fun result -> result.ObservedEffects |> Seq.toArray)
            |> Seq.map _.Value
            |> Set.ofSeq

        let neverObserved = allPrograms |> Array.filter (observed.Contains >> not)
        let contentCoverage = coveredContent seeds
        let incomplete = results |> Array.filter (fun result -> result.Status = Incomplete)

        let findings =
            results |> Array.collect (fun result -> result.Findings |> Seq.toArray)

        let longestBout = results |> Array.maxBy _.Steps |> _.Steps

        let ceilingRationale =
            if stepCeiling > longestBout * 2 then
                $"leaves more than 2x headroom over the longest {longestBout}-command observed bout for deferred choices and triggers"
            else
                $"records the longest {longestBout}-command observed bout without treating a ceiling hit as a rules failure"

        let lines = ResizeArray<string>()
        lines.Add "# BLOKEMON-079 self-play program coverage"
        lines.Add ""
        lines.Add "- Authority programs: 310"
        lines.Add $"- Effect-attributed program IDs observed: {observed.Count}/310"
        lines.Add "- Coverage mode: APPROXIMATE"

        lines.Add
            "- Companion authorities: content/authorities/mechanics.json; content/reference/sv151-authority-reconciliation.json"

        lines.Add
            "- Reason: MatchEvent.Effect records effectful events, but accepted program invocation has no universal event; continuous refresh and multi-rule Kit execution can be unobservable when no instruction emits an effect event."

        lines.Add
            "- Coverage population: selected self-play commands plus deterministic settlement probes for every payable action offered in each reached state; an EffectId can be attributed before every instruction settles."

        lines.Add $"- Seed count: {seeds.Length}"

        lines.Add(
            if seeds = DefaultSeeds then
                "- Seed rationale: the three default seeds are the minimum recorded 39-card cyclic deck windows whose two sides cover all 165 content identities while retaining 21 Basic Vim per deck."
            else
                "- Seed rationale: the opt-in arithmetic progression broadens action/effect sampling while preserving deterministic, greppable seeds and the same playable deck construction."
        )

        lines.Add
            $"- Step ceiling: {stepCeiling} commands per bout (run control only; not a game rule)"

        lines.Add $"- Ceiling rationale: {ceilingRationale}"
        lines.Add $"- Content cards in seeded decks: {contentCoverage.Count}/165"
        lines.Add $"- Completed bouts: {results.Length - incomplete.Length}"
        lines.Add $"- INCOMPLETE bouts: {incomplete.Length}"
        lines.Add $"- Rule finding representatives: {findings.Length}"
        lines.Add "- Finding retention: first failing assertion per rulebook clause and bout"
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
            lines.Add $"- {clause.Id} | {clause.Lines} | {clause.Heading} | {clause.Rule}"

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
        lines.Add "## Effect-attributed program IDs observed"
        lines.Add ""

        for effect in observed |> Set.toArray |> Array.sort do
            lines.Add $"- {effect}"

        lines.Add ""
        lines.Add "## Never-observed or event-unobservable programs"
        lines.Add ""

        for effect in neverObserved do
            lines.Add $"- {effect}"

        let path = Path.Combine(AppContext.BaseDirectory, filename)
        File.WriteAllLines(path, lines)
        path, observed.Count, neverObserved.Length, incomplete.Length
