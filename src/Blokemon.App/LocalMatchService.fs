namespace Blokemon.App

open System
open System.Collections.Generic
open System.Diagnostics
open System.Linq
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.Core.SetDesign
open Blokemon.Product
open Blokemon.Game

type internal GameCommandId = Blokemon.Game.CommandId

/// What a match operation produced: a view, a typed failure, and the steps to animate.
type MatchServiceResult =
    { View: MatchView | null
      Error: ApiError | null
      Presentation: MatchPresentationView | null }

    // The C# sealed record this replaces carried compiler-generated structural operators, and an
    // F# record emits none: a C# `==` against it would silently fall back to reference equality.
    static member op_Equality(left: MatchServiceResult, right: MatchServiceResult) =
        left.Equals right

    static member op_Inequality(left: MatchServiceResult, right: MatchServiceResult) =
        not (left.Equals right)

// The persisted battle documents. [<CLIMutable>] is what lets these fields carry
// [<property: JsonRequired>]: System.Text.Json refuses a required property with no setter, and an
// immutable F# record has none. See .agent-workspace/068/probe-and-censuses.md leg (a0).
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
[<CLIMutable>]
type MatchStartReceipt =
    { [<property: JsonRequired>]
      ClientCommandId: Guid
      [<property: JsonRequired>]
      DeckId: Guid
      [<property: JsonRequired>]
      Fingerprint: string
      [<property: JsonRequired>]
      StartRequestFingerprint: string }

[<CLIMutable>]
type MatchClientCommandReceipt =
    { [<property: JsonRequired>]
      ClientCommandId: Guid
      [<property: JsonRequired>]
      Fingerprint: string
      [<property: JsonRequired>]
      RequestPayload: string
      [<property: JsonRequired>]
      AppliedCommand: GameCommandId
      [<property: JsonRequired>]
      ResultRevision: MatchRevision }

[<CLIMutable>]
type MatchDocument =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      AuthorityVersion: string
      [<property: JsonRequired>]
      StartCommand: MatchStartReceipt
      [<property: JsonRequired>]
      Start: MatchStartRequest
      [<property: JsonRequired>]
      Commands: FrozenList<MatchCommand>
      [<property: JsonRequired>]
      ClientCommands: FrozenList<MatchClientCommandReceipt> }

[<CLIMutable>]
type MatchHistoryDocument =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      AuthorityVersion: string
      [<property: JsonRequired>]
      Matches: FrozenList<MatchDocument> }

[<CLIMutable>]
type MatchActionPayload =
    { [<property: JsonRequired>]
      MatchId: Guid
      [<property: JsonRequired>]
      ExpectedRevision: int64
      [<property: JsonRequired>]
      ActionId: string
      [<property: JsonRequired>]
      Choices: MatchChoiceSelectionRequest array }

// The in-memory carriers. None of these is persisted, so none needs CLIMutable.
type internal LoadedMatch =
    { DocumentRevision: int64
      Document: MatchDocument
      State: MatchState
      Events: FrozenList<MatchEvent> }

type internal MatchLoad =
    { Match: LoadedMatch | null
      Error: ApiError | null }

type internal CpuAdvance =
    { State: MatchState
      Error: ApiError | null }

type internal PendingPresentation =
    { State: MatchState
      Events: FrozenList<MatchEvent> }

type internal ActionSubjectView =
    { Source: string | null
      Target: string | null
      Effect: string | null }

type internal CommandMaterialization =
    { Command: MatchCommand | null
      Error: ApiError | null }

[<Sealed>]
type LocalMatchService(catalogue: BlokemonCatalogue, documents: IStateDocumentStore) =

    static let matchKey = "match"
    static let matchHistoryKey = "match-history"
    static let matchSchemaVersion = 1
    static let matchHistorySchemaVersion = 1
    static let maximumCpuCommandsPerRequest = 256
    static let cpuPlayerId = "cpu:local"
    static let cpuName = "The Regular"

    static let cpuPlayer = PlayerId cpuPlayerId

    static let failed code message : MatchServiceResult =
        { View = null
          Error = ApiError(code, message)
          Presentation = null }

    static let stateConflict () =
        failed "state.conflict" "The saved battle changed. Select the action again."

    static let invalidChoice message : CommandMaterialization =
        { Command = null
          Error = ApiError("match.choice_invalid", message) }

    static let requiredChoice () : CommandMaterialization =
        { Command = null
          Error = ApiError("match.choice_required", "Make each required choice.") }

    static let invalidDocument code message : MatchLoad =
        { Match = null
          Error = ApiError(code, message) }

    static let invalidReplayError () =
        ApiError("match.replay_invalid", "The saved battle is damaged. No data changed.")

    static let invalidReplay () : MatchLoad =
        { Match = null
          Error = invalidReplayError () }

    static let historyCorrupt () =
        ApiError("match.history_corrupt", "The saved battle history is damaged. No data changed.")

    static let historyVersion () =
        ApiError(
            "match.history_version",
            "The saved battle history uses an unsupported version. No data changed."
        )

    static let historyAuthorityChanged () =
        ApiError(
            "match.authority_changed",
            "The card rules changed after these battles were saved. No data changed."
        )

    static let hasValue (value: string | null) = not (String.IsNullOrWhiteSpace value)

    static let cardIdHasValue (card: CardInstanceId) = hasValue card.Value

    static let humanPlayer (profile: LocalProfile) = PlayerId $"local:{profile.Id.Value}"

    static let playerName (player: PlayerId) (human: PlayerId) (displayName: string) =
        if player = human then displayName else cpuName

    static let fingerprint (payload: string) =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes payload)).ToLowerInvariant()

    static let startFingerprint (request: StartMatchRequest) =
        fingerprint $"start:{request.DeckId:D}"

    static let gameStartFingerprint (start: MatchStartRequest) =
        fingerprint (JsonSerializer.Serialize(start, MatchJson.Options))

    static let matchSeedFor (profile: LocalProfile) (commandId: Guid) =
        let hash =
            SHA256.HashData(Encoding.UTF8.GetBytes $"{profile.Id.Value}:match:{commandId:D}")

        MatchSeed(BitConverter.ToUInt64 hash)

    static let isClientCommand (command: GameCommandId) =
        command.Value.StartsWith("client:", StringComparison.Ordinal)
        && fst (Guid.TryParse(command.Value["client:".Length ..]))

    static let humanize (value: string) =
        let result = StringBuilder(value.Length + 8)

        for index in 0 .. value.Length - 1 do
            if index > 0 && Char.IsUpper value[index] && not (Char.IsUpper value[index - 1]) then
                result.Append ' ' |> ignore

            result.Append value[index] |> ignore

        result.ToString()

    static let phaseLabel (phase: MatchPhase) =
        match phase with
        | MatchPhase.MulliganBonus -> "Extra draw"
        | MatchPhase.OpeningPlacement -> "Choose starting Blokemon"
        | MatchPhase.Playing -> "Battle"
        | MatchPhase.AwaitingEffectChoice -> "Choose an effect"
        | MatchPhase.AwaitingTriggerChoice -> "Make a required choice"
        | MatchPhase.AwaitingReplacement -> "Choose replacement"
        | MatchPhase.Complete -> "Complete"
        | _ -> raise (UnreachableException())

    static let cardChoiceLabel (minimum: int) (maximum: int) =
        if minimum = maximum then
            $"""Choose {minimum} {if minimum = 1 then "card" else "cards"}"""
        elif minimum = 0 then
            $"""Choose up to {maximum} {if maximum = 1 then "card" else "cards"}"""
        else
            $"Choose {minimum} to {maximum} cards"

    static let requirementLabel (requirement: ChoiceRequirement) =
        match requirement.Kind with
        | ChoiceRequirementKind.Optional -> "Use this effect?"
        | ChoiceRequirementKind.Amount ->
            $"Choose an amount from {requirement.Minimum} to {requirement.Maximum}"
        | ChoiceRequirementKind.Cards when requirement.Id.Value = "opening:booth" ->
            "Choose Blokemon for the Bench"
        | ChoiceRequirementKind.Cards -> cardChoiceLabel requirement.Minimum requirement.Maximum
        | ChoiceRequirementKind.MechanicalType -> "Choose an Energy type"
        | ChoiceRequirementKind.Attack -> "Choose an attack"
        | ChoiceRequirementKind.Distribution ->
            $"""Place {requirement.Maximum} damage {if requirement.Maximum = 1 then "counter" else "counters"}"""
        | ChoiceRequirementKind.Attachments ->
            $"""Choose targets for {requirement.Minimum} Energy {if requirement.Minimum = 1 then "card" else "cards"}"""
        | _ -> raise (UnreachableException())

    static let choiceKind (kind: ChoiceRequirementKind) =
        match kind with
        | ChoiceRequirementKind.Optional -> MatchChoiceKindView.Optional
        | ChoiceRequirementKind.Amount -> MatchChoiceKindView.Amount
        | ChoiceRequirementKind.Cards -> MatchChoiceKindView.Cards
        | ChoiceRequirementKind.MechanicalType -> MatchChoiceKindView.MechanicalType
        | ChoiceRequirementKind.Attack -> MatchChoiceKindView.Attack
        | ChoiceRequirementKind.Distribution -> MatchChoiceKindView.Distribution
        | ChoiceRequirementKind.Attachments -> MatchChoiceKindView.Attachments
        | _ -> raise (UnreachableException())

    static let actionKind (kind: LegalActionKind) =
        match kind with
        | LegalActionKind.ChooseMulliganBonus -> MatchActionKindView.ChooseMulliganBonus
        | LegalActionKind.ChooseOpening -> MatchActionKindView.ChooseOpening
        | LegalActionKind.ChooseReplacement -> MatchActionKindView.ChooseReplacement
        | LegalActionKind.AttachVim -> MatchActionKindView.AttachEnergy
        | LegalActionKind.PlayBloke -> MatchActionKindView.PlayBlokemon
        | LegalActionKind.Promote -> MatchActionKindView.Evolve
        | LegalActionKind.PlayKit -> MatchActionKindView.PlayTrainer
        | LegalActionKind.UsePartyTrick -> MatchActionKindView.UseAbility
        | LegalActionKind.Attack -> MatchActionKindView.Attack
        | LegalActionKind.Taxi -> MatchActionKindView.Retreat
        | LegalActionKind.ChuckFossil -> MatchActionKindView.DiscardFossil
        | LegalActionKind.EndRound -> MatchActionKindView.EndTurn
        | LegalActionKind.ResolveEffectChoice -> MatchActionKindView.ResolveChoice
        | LegalActionKind.ResolveKnockoutTrigger -> MatchActionKindView.ResolveKnockout
        | LegalActionKind.ResolveBarChitTrigger -> MatchActionKindView.TakePrize
        | LegalActionKind.Resign -> MatchActionKindView.Resign
        | _ -> raise (UnreachableException())

    static let rejection (code: CommandRejectionCode) =
        match code with
        | CommandRejectionCode.StaleRevision ->
            ApiError("match.stale", "The battle changed. Choose the move again.")
        | CommandRejectionCode.ChoiceRequired ->
            ApiError("match.choice_required", "Make each required choice.")
        | CommandRejectionCode.InvalidChoice ->
            ApiError("match.choice_invalid", "This choice is not available.")
        | CommandRejectionCode.IllegalOpening ->
            ApiError("match.choice_invalid", "The selected opening placement is not legal.")
        | CommandRejectionCode.WrongChooser ->
            ApiError("match.choice_wrong_chooser", "The opponent must make this choice.")
        | CommandRejectionCode.DuplicateCommand ->
            ApiError("match.command_conflict", "This move was already used.")
        | _ -> ApiError("match.action_illegal", "You cannot use that move now.")

    static let isPublicEvent (matchEvent: MatchEvent) =
        match matchEvent.Kind with
        | MatchEventKind.MatchStarted
        | MatchEventKind.CommandApplied
        | MatchEventKind.CardsShuffled
        | MatchEventKind.CardsDrawn
        | MatchEventKind.CardsRevealed
        | MatchEventKind.BeerMatTossed
        | MatchEventKind.DamagePlaced
        | MatchEventKind.DamageHealed
        | MatchEventKind.RoughStateApplied
        | MatchEventKind.RoughStateCleared
        | MatchEventKind.AttackDeclared
        | MatchEventKind.AttackCancelled
        | MatchEventKind.BlokeSentHome
        | MatchEventKind.BarChitsTaken
        | MatchEventKind.RoundStarted
        | MatchEventKind.RoundEnded
        | MatchEventKind.SuddenDeathStarted
        | MatchEventKind.MatchWon -> true
        | _ -> false

    static let animationKind (matchEvent: MatchEvent) =
        match matchEvent.Kind with
        | MatchEventKind.MatchStarted -> Nullable MatchAnimationKindView.Setup
        | MatchEventKind.CardsShuffled -> Nullable MatchAnimationKindView.Shuffle
        | MatchEventKind.CardsDrawn -> Nullable MatchAnimationKindView.Draw
        | MatchEventKind.CardsRevealed -> Nullable MatchAnimationKindView.Reveal
        | MatchEventKind.BeerMatTossed -> Nullable MatchAnimationKindView.Coin
        | MatchEventKind.CommandApplied ->
            match matchEvent.Command with
            | :? MatchCommand.ChooseOpening -> Nullable MatchAnimationKindView.Setup
            | :? MatchCommand.AttachVim -> Nullable MatchAnimationKindView.Attach
            | :? MatchCommand.Promote -> Nullable MatchAnimationKindView.Evolve
            | :? MatchCommand.PlayBloke
            | :? MatchCommand.PlayKit
            | :? MatchCommand.UsePartyTrick
            | :? MatchCommand.Taxi -> Nullable MatchAnimationKindView.Play
            | _ -> Nullable()
        | MatchEventKind.AttackDeclared -> Nullable MatchAnimationKindView.Attack
        | MatchEventKind.DamagePlaced -> Nullable MatchAnimationKindView.Damage
        | MatchEventKind.DamageHealed -> Nullable MatchAnimationKindView.Heal
        | MatchEventKind.RoughStateApplied
        | MatchEventKind.RoughStateCleared -> Nullable MatchAnimationKindView.Condition
        | MatchEventKind.BlokeSentHome -> Nullable MatchAnimationKindView.Knockout
        | MatchEventKind.BarChitsTaken -> Nullable MatchAnimationKindView.Prize
        | MatchEventKind.RoundStarted -> Nullable MatchAnimationKindView.Turn
        | MatchEventKind.MatchWon -> Nullable MatchAnimationKindView.Victory
        | _ -> Nullable()

    static let commandSource (command: MatchCommand | null) =
        match command with
        | :? MatchCommand.ChooseOpening as value -> Nullable value.Oche
        | :? MatchCommand.AttachVim as value -> Nullable value.Vim
        | :? MatchCommand.PlayBloke as value -> Nullable value.Bloke
        | :? MatchCommand.Promote as value -> Nullable value.Promotion
        | :? MatchCommand.PlayKit as value -> Nullable value.Kit
        | :? MatchCommand.Taxi as value -> Nullable value.BoothBloke
        | :? MatchCommand.UsePartyTrick as value -> Nullable value.Source
        | :? MatchCommand.Attack as value -> Nullable value.Attacker
        | :? MatchCommand.ChuckFossil as value -> Nullable value.Fossil
        | _ -> Nullable()

    static let canReveal (state: MatchState) (human: PlayerId) (cardId: CardInstanceId) =
        let card = state.Card cardId

        card.Owner = human
        || match card.Zone with
           | CardZone.Oche
           | CardZone.Booth
           | CardZone.Attached
           | CardZone.EmptiesTray -> true
           | _ -> false

    static let attackDisabledReason (state: MatchState) (human: PlayerId) =
        if state.Phase <> MatchPhase.Playing then
            "Complete setup before attacking."
        elif state.ActivePlayer <> human then
            "Wait for your turn."
        else
            "Attach the required Energy or satisfy the attack's requirements."

    static let resolvedAttackDamage (attack: MatchEvent) (stepEvents: MatchEvent seq) =
        stepEvents
        |> Seq.filter (fun matchEvent ->
            matchEvent.Sequence >= attack.Sequence
            && matchEvent.Kind = MatchEventKind.DamagePlaced
            && matchEvent.Actor = attack.Actor
            && matchEvent.SourceCard = attack.SourceCard
            && matchEvent.DamageKind.HasValue
            && (matchEvent.DamageKind.Value = DamageKind.Attack
                || matchEvent.DamageKind.Value = DamageKind.BoothAttack))
        |> Seq.sumBy _.Amount

    static let startIsStructurallyValid (start: MatchStartRequest) =
        hasValue start.MatchId.Value
        && hasValue start.FirstDeck.Owner.Value
        && hasValue start.SecondDeck.Owner.Value
        && start.FirstDeck.Cards |> Seq.forall (fun card -> hasValue card.Value)
        && start.SecondDeck.Cards |> Seq.forall (fun card -> hasValue card.Value)

    static let effectChoiceIsStructurallyValid (choice: EffectChoice | null) =
        match choice with
        | null -> false
        | value when not (hasValue value.Id.Value) -> false
        | value ->
            // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
            value.Match(
                (fun _ -> true),
                (fun _ -> true),
                (fun cards -> cards.Values |> Seq.forall cardIdHasValue),
                (fun _ -> true),
                (fun attack -> hasValue attack.Value.Value),
                (fun distribution ->
                    distribution.Values |> Seq.forall (fun item -> hasValue item.Card.Value)),
                (fun attachments ->
                    attachments.Values
                    |> Seq.forall (fun item ->
                        hasValue item.Vim.Value && hasValue item.Bloke.Value))
            )

    static let commandIsStructurallyValid (command: MatchCommand | null) =
        match command with
        | null -> false
        | value when
            not (hasValue value.Id.Value)
            || not (hasValue value.MatchId.Value)
            || not (hasValue value.Actor.Value)
            || value.ExpectedRevision.Value < 0L
            || value.Choices
               |> Seq.exists (fun choice -> not (effectChoiceIsStructurallyValid choice))
            ->
            false
        | value ->
            // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
            value.Match(
                (fun _ -> true),
                (fun opening ->
                    hasValue opening.Oche.Value && Seq.forall cardIdHasValue opening.Booth),
                (fun attach -> hasValue attach.Vim.Value && hasValue attach.Bloke.Value),
                (fun play -> hasValue play.Bloke.Value),
                (fun promote -> hasValue promote.Promotion.Value && hasValue promote.Bloke.Value),
                (fun kit ->
                    hasValue kit.Kit.Value
                    && (not kit.Target.HasValue || hasValue kit.Target.Value.Value)),
                (fun taxi ->
                    hasValue taxi.BoothBloke.Value && Seq.forall cardIdHasValue taxi.VimToChuck),
                (fun trick -> hasValue trick.Source.Value && hasValue trick.Effect.Value),
                (fun attack -> hasValue attack.Attacker.Value && hasValue attack.AttackId.Value),
                (fun fossil -> hasValue fossil.Fossil.Value),
                (fun _ -> true),
                (fun replacement -> hasValue replacement.BoothBloke.Value),
                (fun _ -> true),
                (fun knockout -> not knockout.Vim.HasValue || hasValue knockout.Vim.Value.Value),
                (fun _ -> true),
                (fun _ -> true)
            )

    static let choiceSubmissionIsStructurallyValid (choice: MatchChoiceSelectionRequest | null) =
        match choice with
        | null -> false
        | value ->
            hasValue value.Id
            && not (isMissing value.CardInstanceIds)
            && value.CardInstanceIds |> Array.forall hasValue
            && not (isMissing value.Distribution)
            && value.Distribution
               |> Array.forall (fun allocation ->
                   not (isMissing allocation) && hasValue allocation.CardInstanceId)
            && not (isMissing value.Attachments)
            && value.Attachments
               |> Array.forall (fun attachment ->
                   not (isMissing attachment)
                   && hasValue attachment.VimCardInstanceId
                   && hasValue attachment.BlokeCardInstanceId)

    static let actionPayload (matchId: Guid) (request: ApplyMatchActionRequest) =
        let choices =
            (orEmpty request.Choices)
                .OrderBy((fun choice -> choice.Id), StringComparer.Ordinal)
                .ToArray()

        JsonSerializer.Serialize(
            { MatchId = matchId
              ExpectedRevision = request.ExpectedRevision
              ActionId = request.ActionId
              Choices = choices },
            MatchJson.Options
        )

    static let readActionPayload (json: string) : MatchActionPayload | null =
        try
            JsonSerializer.Deserialize<MatchActionPayload>(json, MatchJson.Options)
        with
        | :? JsonException -> null
        | :? NotSupportedException -> null

    static let readSchemaVersion (json: string) =
        try
            use document = JsonDocument.Parse json

            if document.RootElement.ValueKind = JsonValueKind.Object then
                match document.RootElement.TryGetProperty "schemaVersion" with
                | true, version ->
                    match version.TryGetInt32() with
                    | true, value -> Nullable value
                    | _ -> Nullable()
                | _ -> Nullable()
            else
                Nullable()
        with :? JsonException ->
            Nullable()

    static let documentsMatch (left: MatchDocument) (right: MatchDocument) =
        String.Equals(
            JsonSerializer.Serialize(left, MatchJson.Options),
            JsonSerializer.Serialize(right, MatchJson.Options),
            StringComparison.Ordinal
        )

    static let toEffectChoice
        (requirement: ChoiceRequirement)
        (selection: MatchChoiceSelectionRequest)
        : EffectChoice | null =
        if selection.Kind <> choiceKind requirement.Kind then
            null
        else
            match requirement.Kind with
            | ChoiceRequirementKind.Optional when selection.Accepted.HasValue ->
                EffectChoice.Optional(requirement.Id, selection.Accepted.Value)
            | ChoiceRequirementKind.Amount when selection.Amount.HasValue ->
                EffectChoice.Amount(requirement.Id, selection.Amount.Value)
            | ChoiceRequirementKind.Cards ->
                EffectChoice.Cards(
                    requirement.Id,
                    FrozenList<CardInstanceId>
                        .Create(orEmpty selection.CardInstanceIds |> Seq.map CardInstanceId)
                )
            | ChoiceRequirementKind.MechanicalType ->
                match Enum.TryParse<BlokemonMechanicalType>(selection.MechanicalType, false) with
                | true, mechanicalType ->
                    EffectChoice.MechanicalType(requirement.Id, mechanicalType)
                | _ -> null
            | ChoiceRequirementKind.Attack ->
                match selection.EffectId with
                | null -> null
                | effectId -> EffectChoice.Attack(requirement.Id, EffectId effectId)
            | ChoiceRequirementKind.Distribution ->
                EffectChoice.Distribution(
                    requirement.Id,
                    FrozenList<DamageAllocation>
                        .Create(
                            orEmpty selection.Distribution
                            |> Seq.map (fun allocation ->
                                DamageAllocation(
                                    CardInstanceId allocation.CardInstanceId,
                                    allocation.Counters
                                ))
                        )
                )
            | ChoiceRequirementKind.Attachments ->
                EffectChoice.Attachments(
                    requirement.Id,
                    FrozenList<VimAttachment>
                        .Create(
                            orEmpty selection.Attachments
                            |> Seq.map (fun attachment ->
                                VimAttachment(
                                    CardInstanceId attachment.VimCardInstanceId,
                                    CardInstanceId attachment.BlokeCardInstanceId
                                ))
                        )
                )
            | _ -> null

    let engine = MatchEngine(catalogue.Mechanics)
    let cpu = DeterministicCpu()

    // The verified reconstruction of the stored document identified by DocumentRevision.
    // Skips the O(history) deserialize-and-replay on every action; any revision mismatch
    // (another writer, cold load) falls back to the full verified replay.
    let mutable cachedMatch: LoadedMatch | null = null

    let playerDeckName (deckId: Guid) =
        match
            catalogue.StarterDecks.Decks.SingleOrDefault(fun deck -> deck.SavedDeckId = deckId)
        with
        | null -> "Custom deck"
        | deck -> deck.Name

    let cpuDeckName (snapshot: FrozenDeckSnapshot) =
        let quantities = Dictionary<string, int>(StringComparer.Ordinal)

        for card in snapshot.Cards do
            match quantities.TryGetValue card.Value with
            | true, count -> quantities[card.Value] <- count + 1
            | _ -> quantities[card.Value] <- 1

        match
            catalogue.StarterDecks.Decks.SingleOrDefault(fun deck ->
                deck.Entries.Count = quantities.Count
                && deck.Entries
                   |> Seq.forall (fun entry ->
                       quantities.GetValueOrDefault(entry.CardId, 0) = entry.Quantity))
        with
        | null -> "Starter deck"
        | deck -> deck.Name

    let energyLabel (cardType: BlokemonMechanicalType) =
        match
            catalogue.Mechanics.ApprovedMechanicalDisplayMap
            |> Array.tryFind (fun entry -> entry.MechanicalType = cardType)
        with
        | Some entry -> entry.ApprovedLabel.ToString()
        | None -> humanize (cardType.ToString())

    let cardName (state: MatchState) (card: CardInstanceId) =
        catalogue.Card((state.Card card).MechanicalId.Value).Name

    let cardInstance
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (cardId: CardInstanceId)
        =
        let card = state.Card cardId

        MatchCardInstanceView(
            card.Id.Value,
            catalogue.Card card.MechanicalId.Value,
            playerName card.Owner human displayName,
            humanize (card.Zone.ToString()),
            card.Damage,
            catalogue.StayingPower card.MechanicalId.Value,
            card.Attachments
            |> Seq.map state.Card
            |> Seq.filter (fun attachment -> attachment.Kind = CardKind.Vim)
            |> Seq.map (fun attachment -> catalogue.Card attachment.MechanicalId.Value)
            |> Seq.toArray,
            card.Attachments
            |> Seq.map state.Card
            |> Seq.filter (fun attachment -> attachment.Kind = CardKind.Kit)
            |> Seq.map (fun attachment -> catalogue.Card attachment.MechanicalId.Value)
            |> Seq.toArray,
            card.UnderlyingCards
            |> Seq.map state.Card
            |> Seq.map (fun underlying -> catalogue.Card underlying.MechanicalId.Value)
            |> Seq.toArray,
            card.RoughStates
            |> Seq.map (fun rough -> humanize (rough.State.ToString()))
            |> Seq.toArray
        )

    let actionSubject (command: MatchCommand) : ActionSubjectView =
        // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
        command.Match<ActionSubjectView>(
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun opening ->
                { Source = opening.Oche.Value
                  Target = null
                  Effect = null }),
            (fun attach ->
                { Source = attach.Vim.Value
                  Target = attach.Bloke.Value
                  Effect = null }),
            (fun play ->
                { Source = play.Bloke.Value
                  Target = null
                  Effect = null }),
            (fun promote ->
                { Source = promote.Promotion.Value
                  Target = promote.Bloke.Value
                  Effect = null }),
            (fun kit ->
                { Source = kit.Kit.Value
                  Target = if kit.Target.HasValue then kit.Target.Value.Value else null
                  Effect = null }),
            (fun taxi ->
                { Source = taxi.BoothBloke.Value
                  Target = null
                  Effect = null }),
            (fun trick ->
                { Source = trick.Source.Value
                  Target = null
                  Effect = trick.Effect.Value }),
            (fun attack ->
                { Source = attack.Attacker.Value
                  Target = null
                  Effect = attack.AttackId.Value }),
            (fun fossil ->
                { Source = fossil.Fossil.Value
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun replacement ->
                { Source = replacement.BoothBloke.Value
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun knockout ->
                { Source =
                    if knockout.Vim.HasValue then
                        knockout.Vim.Value.Value
                    else
                        null
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null }),
            (fun _ ->
                { Source = null
                  Target = null
                  Effect = null })
        )

    let actionLabel (state: MatchState) (command: MatchCommand) =
        // A Game union, so this stays visitor-style until Blokemon.Game migrates in slice 7.
        command.Match(
            (fun bonus ->
                if bonus.CardsToDraw = 0 then
                    "Draw no extra cards"
                else
                    $"""Draw {bonus.CardsToDraw} extra {if bonus.CardsToDraw = 1 then "card" else "cards"}"""),
            (fun opening -> $"Make {cardName state opening.Oche} your Active Blokemon"),
            (fun attach -> $"Attach {cardName state attach.Vim} to {cardName state attach.Bloke}"),
            (fun play -> $"Play {cardName state play.Bloke} to the Bench"),
            (fun promote ->
                $"Evolve {cardName state promote.Bloke} into {cardName state promote.Promotion}"),
            (fun kit -> $"Play {cardName state kit.Kit}"),
            (fun taxi -> $"Retreat to {cardName state taxi.BoothBloke}"),
            (fun trick -> catalogue.EffectName trick.Effect.Value),
            (fun attack -> $"Attack with {catalogue.EffectName attack.AttackId.Value}"),
            (fun fossil -> $"Discard {cardName state fossil.Fossil}"),
            (fun _ -> "End the turn"),
            (fun replacement ->
                $"Move {cardName state replacement.BoothBloke} from the Bench to Active"),
            (fun _ -> "Make the required choice"),
            (fun knockout ->
                if knockout.Vim.HasValue then
                    $"Attach {cardName state knockout.Vim.Value}"
                else
                    "Do not attach Energy"),
            (fun barChit ->
                if barChit.PutOntoBooth then
                    "Put the card on the Bench"
                else
                    "Put the card in your Hand"),
            (fun _ -> "Resign the battle")
        )

    let eventLabel
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (matchEvent: MatchEvent)
        =
        let actor =
            if matchEvent.Actor.HasValue then
                playerName matchEvent.Actor.Value human displayName
            else
                "The match"

        match matchEvent.Kind with
        | MatchEventKind.MatchStarted -> "The battle started."
        | MatchEventKind.CommandApplied ->
            match matchEvent.Command with
            | :? MatchCommand.PlayKit as playKit when playKit.Target.HasValue ->
                $"{actor}: Attached {cardName state playKit.Kit} to {cardName state playKit.Target.Value}."
            | null -> raise (UnreachableException())
            | command -> $"{actor}: {actionLabel state command}."
        | MatchEventKind.CardsShuffled -> $"{actor} shuffled the Deck."
        | MatchEventKind.CardsDrawn ->
            $"""{actor} drew {matchEvent.Amount} {if matchEvent.Amount = 1 then "card" else "cards"}."""
        | MatchEventKind.CardsRevealed when
            matchEvent.TargetCards.Count > 0
            && matchEvent.TargetCards
               |> Seq.forall (fun card -> (state.Card card).Zone = CardZone.BarChit)
            ->
            $"{actor} looked at their Prize Cards."
        | MatchEventKind.CardsRevealed ->
            $"""{actor} revealed {matchEvent.TargetCards.Count} {if matchEvent.TargetCards.Count = 1 then "card" else "cards"}."""
        | MatchEventKind.BeerMatTossed ->
            let landed =
                if matchEvent.BadgeSide.HasValue && matchEvent.BadgeSide.Value then
                    "badge"
                else
                    "blank"

            $"The coin landed on {landed}."
        | MatchEventKind.DamagePlaced -> $"{actor} did {matchEvent.Amount} damage."
        | MatchEventKind.DamageHealed -> $"{actor} healed {matchEvent.Amount} damage."
        | MatchEventKind.RoughStateApplied ->
            $"{humanize (matchEvent.RoughState.Value.ToString())} started."
        | MatchEventKind.RoughStateCleared ->
            $"{humanize (matchEvent.RoughState.Value.ToString())} ended."
        | MatchEventKind.BarChitsTaken ->
            $"""{actor} took {matchEvent.Amount} Prize {if matchEvent.Amount = 1 then "Card" else "Cards"}."""
        | MatchEventKind.RoundStarted -> $"{actor}'s turn started."
        | MatchEventKind.RoundEnded -> $"{actor} ended the turn."
        | MatchEventKind.BlokeSentHome -> "A Blokemon was Knocked Out."
        | MatchEventKind.AttackDeclared when matchEvent.Effect.HasValue ->
            $"{actor} used {catalogue.EffectName matchEvent.Effect.Value.Value}."
        | MatchEventKind.AttackDeclared -> $"{actor} attacked."
        | MatchEventKind.AttackCancelled -> "The attack stopped."
        | MatchEventKind.SuddenDeathStarted -> "Sudden death started."
        | MatchEventKind.MatchWon -> $"{actor} won the battle."
        | _ -> raise (UnreachableException())

    let requirementView
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (requirement: ChoiceRequirement)
        =
        // A candidate is offered to the chooser precisely because the effect entitles them to see
        // it: an effect that asks you to pick a card from your opponent's hand reveals that hand
        // to you first. So eligibility is the entitlement, and the candidates are not filtered
        // again for visibility - only for whether this viewer is the chooser at all.
        let exposeOptions = requirement.Chooser = human

        let dependsOnOptional: string | null =
            if requirement.DependsOnOptional.HasValue then
                requirement.DependsOnOptional.Value.Value
            else
                null

        MatchChoiceRequirementView(
            requirement.Id.Value,
            choiceKind requirement.Kind,
            requirementLabel requirement,
            MatchChooserView(
                requirement.Chooser.Value,
                playerName requirement.Chooser human displayName,
                requirement.Chooser = human
            ),
            requirement.Minimum,
            requirement.Maximum,
            (if exposeOptions then
                 requirement.EligibleCards
                 |> Seq.map (cardInstance state human displayName)
                 |> Seq.toArray
             else
                 Array.empty),
            (if exposeOptions then
                 requirement.EligibleMechanicalTypes
                 |> Seq.map (fun cardType ->
                     MatchMechanicalTypeOptionView(
                         cardType.ToString(),
                         humanize (cardType.ToString())
                     ))
                 |> Seq.toArray
             else
                 Array.empty),
            (if exposeOptions then
                 requirement.EligibleEffects
                 |> Seq.map (fun effect ->
                     MatchEffectOptionView(effect.Value, catalogue.EffectName effect.Value))
                 |> Seq.toArray
             else
                 Array.empty),
            dependsOnOptional,
            (if exposeOptions then
                 requirement.EligibleTargets
                 |> Seq.map (cardInstance state human displayName)
                 |> Seq.toArray
             else
                 Array.empty),
            requirement.RequireDifferentMechanicalTypes,
            (if exposeOptions then
                 requirement.EligibleCardTypes
                 |> Seq.map (fun card ->
                     MatchCardTypesView(
                         card.Card.Value,
                         card.Types
                         |> Seq.map (fun cardType -> humanize (cardType.ToString()))
                         |> Seq.toArray
                     ))
                 |> Seq.toArray
             else
                 Array.empty)
        )

    let actionView
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (action: LegalAction)
        =
        let subject = actionSubject action.Command

        MatchActionView(
            action.StableKey,
            actionKind action.Kind,
            actionLabel state action.Command,
            (match action.Kind with
             | LegalActionKind.ChooseOpening
             | LegalActionKind.Attack
             | LegalActionKind.ResolveEffectChoice
             | LegalActionKind.ResolveKnockoutTrigger
             | LegalActionKind.ResolveBarChitTrigger -> true
             | _ -> false),
            subject.Source,
            subject.Target,
            subject.Effect,
            action.ChoiceRequirements
            |> Seq.map (requirementView state human displayName)
            |> Seq.toArray
        )

    let attacks (state: MatchState) (human: PlayerId) (legalActions: LegalAction seq) =
        match state.Oche human with
        | null -> Array.empty
        | active when active.Kind <> CardKind.Bloke -> Array.empty
        | active ->
            let legal =
                legalActions
                    .Where(fun action -> action.Command :? MatchCommand.Attack)
                    .ToDictionary(
                        (fun action -> (action.Command :?> MatchCommand.Attack).AttackId.Value),
                        (fun action -> action.StableKey),
                        StringComparer.Ordinal
                    )

            let mechanical =
                catalogue.Mechanics.Collectibles.Single(fun card ->
                    String.Equals(card.Id, active.MechanicalId.Value, StringComparison.Ordinal))

            mechanical.Attacks
            |> Array.map (fun attack ->
                let actionId: string | null =
                    match legal.TryGetValue attack.MechanicalId with
                    | true, value -> value
                    | _ -> null

                let disabledReason: string | null =
                    if isNull actionId then
                        attackDisabledReason state human
                    else
                        null

                MatchAttackView(
                    active.Id.Value,
                    attack.MechanicalId,
                    catalogue.EffectName attack.MechanicalId,
                    attack.VimCost |> Array.map energyLabel,
                    attack.PrintedDamage,
                    actionId,
                    disabledReason
                ))

    let side
        (state: MatchState)
        (player: PlayerId)
        (human: PlayerId)
        (name: string)
        (deckName: string)
        (exposeHand: bool)
        =
        let active = state.Oche player

        MatchSideView(
            name,
            deckName,
            state.CardsIn(player, CardZone.Stack).Count(),
            state.CardsIn(player, CardZone.Mitt).Count(),
            (state.Player player).BarChitsRemaining,
            (match active with
             | null -> null
             | card -> cardInstance state human name card.Id),
            state.CardsIn(player, CardZone.Booth)
            |> Seq.map (fun card -> cardInstance state human name card.Id)
            |> Seq.toArray,
            (if exposeHand then
                 state.CardsIn(player, CardZone.Mitt)
                 |> Seq.map (fun card -> cardInstance state human name card.Id)
                 |> Seq.toArray
             else
                 Array.empty),
            state.CardsIn(player, CardZone.Local)
            |> Seq.map (fun card -> cardInstance state human name card.Id)
            |> Seq.toArray,
            // Resignation is always available, so it cannot decide whose turn it is.
            engine
                .GetLegalActions(state, player)
                .Any(fun action -> action.Kind <> LegalActionKind.Resign)
        )

    let frame (document: MatchDocument) (state: MatchState) (displayName: string) =
        let human = document.Start.FirstDeck.Owner
        let cpuSide = document.Start.SecondDeck.Owner

        let winner: string | null =
            if state.Winner.HasValue then
                playerName state.Winner.Value human displayName
            else
                null

        MatchFrameView(
            Guid.Parse state.Id.Value,
            state.Revision.Value,
            state.RoundNumber,
            phaseLabel state.Phase,
            side state cpuSide human cpuName (cpuDeckName document.Start.SecondDeck) false,
            side state human human displayName (playerDeckName document.StartCommand.DeckId) true,
            state.Phase = MatchPhase.Complete,
            winner
        )

    let toView (loaded: LoadedMatch) (displayName: string) =
        let human = loaded.Document.Start.FirstDeck.Owner
        let legalActions = engine.GetLegalActions(loaded.State, human)

        MatchView(
            frame loaded.Document loaded.State displayName,
            legalActions
            |> Seq.map (actionView loaded.State human displayName)
            |> Seq.toArray,
            attacks loaded.State human legalActions,
            loaded.Events
                .Where(isPublicEvent)
                .TakeLast(16)
                .Select(fun matchEvent -> eventLabel loaded.State human displayName matchEvent)
                .ToArray()
        )

    let cue
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (matchEvent: MatchEvent)
        (stepEvents: MatchEvent seq)
        : MatchEventCueView | null =
        let kind = animationKind matchEvent

        if not kind.HasValue then
            null
        else

            let eventSource =
                if matchEvent.SourceCard.HasValue then
                    matchEvent.SourceCard
                else
                    commandSource matchEvent.Command

            let source: string | null =
                if eventSource.HasValue && canReveal state human eventSource.Value then
                    eventSource.Value.Value
                else
                    null

            let visibleTargets =
                matchEvent.TargetCards |> Seq.filter (canReveal state human) |> Seq.toArray

            // Reveal faces only for authorised cards the presentation would otherwise hide
            // (face-down Prize Cards, deck cards). Cards the viewer already sees - their own
            // hand, anything in a public zone - need no reveal overlay.
            let revealed: CardView array =
                if matchEvent.Kind = MatchEventKind.CardsRevealed then
                    visibleTargets
                    |> Array.filter (fun card ->
                        match (state.Card card).Zone with
                        | CardZone.Stack
                        | CardZone.BarChit -> true
                        | _ -> false)
                    |> Array.map (fun card -> catalogue.Card (state.Card card).MechanicalId.Value)
                else
                    Array.empty

            MatchEventCueView(
                matchEvent.Sequence,
                kind.Value,
                eventLabel state human displayName matchEvent,
                source,
                visibleTargets |> Array.map _.Value,
                (if matchEvent.Kind = MatchEventKind.AttackDeclared then
                     resolvedAttackDamage matchEvent stepEvents
                 else
                     matchEvent.Amount),
                matchEvent.BadgeSide,
                (if matchEvent.Actor.HasValue then
                     Nullable(matchEvent.Actor.Value = human)
                 else
                     Nullable()),
                revealed
            )

    let toPresentation
        (document: MatchDocument)
        (displayName: string)
        (pending: PendingPresentation seq)
        =
        let human = document.Start.FirstDeck.Owner

        MatchPresentationView(
            pending
            |> Seq.map (fun step ->
                MatchPresentationStepView(
                    frame document step.State displayName,
                    step.Events
                    |> Seq.map (fun matchEvent ->
                        cue step.State human displayName matchEvent step.Events)
                    |> Seq.choose Option.ofObj
                    |> Seq.toArray
                ))
            |> Seq.toArray
        )

    let materializeHumanCommand
        (action: LegalAction)
        (state: MatchState)
        (human: PlayerId)
        (clientCommandId: Guid)
        (submitted: IReadOnlyCollection<MatchChoiceSelectionRequest>)
        : CommandMaterialization =
        if
            submitted
            |> Seq.exists (fun choice -> not (choiceSubmissionIsStructurallyValid choice))
        then
            invalidChoice "A submitted choice is invalid."
        elif submitted |> Seq.countBy _.Id |> Seq.exists (fun (_, count) -> count > 1) then
            invalidChoice "Choose each option once."
        else

            let submittedById =
                submitted.ToDictionary((fun choice -> choice.Id), StringComparer.Ordinal)

            let requirements = action.ChoiceRequirements

            let wrongChooser =
                submitted
                |> Seq.tryPick (fun selection ->
                    match
                        requirements.SingleOrDefault(fun candidate ->
                            candidate.Id.Value = selection.Id)
                    with
                    | null -> Some(invalidChoice "This choice is not available.")
                    | requirement when requirement.Chooser <> human ->
                        Some
                            { Command = null
                              Error =
                                ApiError(
                                    "match.choice_wrong_chooser",
                                    "The computer must make this choice."
                                ) }
                    | _ -> None)

            match wrongChooser with
            | Some failure -> failure
            | None ->

                let choices = List<EffectChoice>()
                let mutable rejection: CommandMaterialization | null = null

                for requirement in
                    requirements |> Seq.filter (fun candidate -> candidate.Chooser = human) do
                    if isNull (box rejection) then
                        let declined =
                            if requirement.DependsOnOptional.HasValue then
                                match
                                    submittedById.TryGetValue
                                        requirement.DependsOnOptional.Value.Value
                                with
                                | true, parent when parent.Accepted.HasValue ->
                                    if parent.Accepted.Value then
                                        false
                                    elif submittedById.ContainsKey requirement.Id.Value then
                                        rejection <-
                                            invalidChoice
                                                "A choice was supplied for an optional branch that was declined."

                                        false
                                    else
                                        true
                                | _ ->
                                    rejection <- requiredChoice ()
                                    false
                            else
                                false

                        if isNull (box rejection) && not declined then
                            match submittedById.TryGetValue requirement.Id.Value with
                            | true, selection ->
                                match toEffectChoice requirement selection with
                                | null ->
                                    rejection <-
                                        invalidChoice
                                            "A submitted choice is not legal for this action."
                                | choice -> choices.Add choice
                            | _ -> rejection <- requiredChoice ()

                match rejection with
                | null ->
                    let commandId = GameCommandId $"client:{clientCommandId:D}"
                    let frozenChoices = FrozenList<EffectChoice>.Create choices
                    let revision = state.Revision

                    // The C# original wrote this as an 18-arm `with` block. Blokemon.Game's MatchCommand
                    // is a C# record, which F# cannot copy-and-update (FS0786), so each arm is an
                    // explicit construction. The eleven cases whose Choices is an init-only override are
                    // reconstructed with its `[]` default, which is what the engine always leaves it as.
                    let command =
                        action.Command.Match<MatchCommand>(
                            (fun value ->
                                MatchCommand.ChooseMulliganBonus(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.CardsToDraw
                                )),
                            (fun value ->
                                let booth =
                                    choices
                                        .OfType<EffectChoice.Cards>()
                                        .Single(fun choice -> choice.Id.Value = "opening:booth")
                                        .Values

                                MatchCommand.ChooseOpening(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Oche,
                                    booth
                                )),
                            (fun value ->
                                MatchCommand.AttachVim(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Vim,
                                    value.Bloke
                                )),
                            (fun value ->
                                MatchCommand.PlayBloke(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Bloke
                                )),
                            (fun value ->
                                MatchCommand.Promote(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Promotion,
                                    value.Bloke,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.PlayKit(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Kit,
                                    value.Target,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.Taxi(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.BoothBloke,
                                    value.VimToChuck
                                )),
                            (fun value ->
                                MatchCommand.UsePartyTrick(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Source,
                                    value.Effect,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.Attack(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Attacker,
                                    value.AttackId,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.ChuckFossil(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Fossil
                                )),
                            (fun value ->
                                MatchCommand.EndRound(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision
                                )),
                            (fun value ->
                                MatchCommand.ChooseReplacement(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.BoothBloke
                                )),
                            (fun value ->
                                MatchCommand.ResolveEffectChoice(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    frozenChoices
                                )),
                            (fun value ->
                                MatchCommand.ResolveKnockoutTrigger(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.Vim
                                )),
                            (fun value ->
                                MatchCommand.ResolveBarChitTrigger(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision,
                                    value.PutOntoBooth
                                )),
                            (fun value ->
                                MatchCommand.Resign(
                                    commandId,
                                    value.MatchId,
                                    value.Actor,
                                    revision
                                ))
                        )

                    { Command = command; Error = null }
                | failure -> failure

    let advanceCpu
        (initial: MatchState)
        (commands: List<MatchCommand>)
        (events: List<MatchEvent>)
        (presentation: List<PendingPresentation>)
        : CpuAdvance =
        let mutable state = initial
        let mutable settled: CpuAdvance | null = null
        let mutable count = 0

        while isNull (box settled) && count < maximumCpuCommandsPerRequest do
            match cpu.Choose(engine, state, cpuPlayer) with
            | :? CpuDecision.Selected as selected ->
                match engine.Apply(state, selected.Action.Command) with
                | :? CommandOutcome.Applied as applied ->
                    commands.Add selected.Action.Command
                    events.AddRange applied.Events

                    presentation.Add
                        { State = applied.State
                          Events = applied.Events }

                    state <- applied.State
                | _ ->
                    settled <-
                        { State = state
                          Error =
                            ApiError("match.cpu_rejected", "The computer made an invalid move.") }
            | _ -> settled <- { State = state; Error = null }

            count <- count + 1

        match settled with
        | null ->
            match cpu.Choose(engine, state, cpuPlayer) with
            | :? CpuDecision.Selected ->
                { State = state
                  Error = ApiError("match.cpu_limit", "The computer could not complete its turn.") }
            | _ -> { State = state; Error = null }
        | finished -> finished

    let validateDocument (profile: LocalProfile) (document: MatchDocument) : ApiError | null =
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
        elif
            isMissing document.Start.FirstDeck
            || isMissing document.Start.SecondDeck
            || document.StartCommand.ClientCommandId = Guid.Empty
            || document.StartCommand.DeckId = Guid.Empty
            || String.IsNullOrWhiteSpace document.StartCommand.Fingerprint
            || document.StartCommand.Fingerprint
               <> startFingerprint (
                   StartMatchRequest(
                       document.StartCommand.ClientCommandId,
                       document.StartCommand.DeckId
                   )
               )
            || String.IsNullOrWhiteSpace document.StartCommand.StartRequestFingerprint
            || document.StartCommand.StartRequestFingerprint
               <> gameStartFingerprint document.Start
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
                       || receipt.AppliedCommand
                          <> GameCommandId $"client:{receipt.ClientCommandId:D}")
            then
                invalidReplayError ()
            else
                null

    let replayDocument
        (profile: LocalProfile)
        (documentRevision: int64)
        (document: MatchDocument)
        : MatchLoad =
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
                | :? MatchStartOutcome.Started as started ->
                    let human = humanPlayer profile

                    let receipts =
                        document.ClientCommands.ToDictionary(fun receipt -> receipt.AppliedCommand)

                    let mutable state = started.State
                    let events = List<MatchEvent>(started.Events)
                    let mutable pendingReceipt: MatchClientCommandReceipt | null = null
                    let mutable rejected = false
                    let mutable index = 0
                    let commands = document.Commands

                    while not rejected && index < commands.Count do
                        let command = commands[index]

                        if command.Actor = cpuPlayer then
                            match cpu.Choose(engine, state, cpuPlayer) with
                            | :? CpuDecision.Selected as selected when
                                selected.Action.Command = command
                                ->
                                ()
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
                            | :? CommandOutcome.Applied as applied ->
                                state <- applied.State
                                events.AddRange applied.Events
                            | _ -> rejected <- true

                        index <- index + 1

                    if rejected then
                        invalidReplay ()
                    else

                        let cpuStillMoves =
                            match cpu.Choose(engine, state, cpuPlayer) with
                            | :? CpuDecision.Selected -> true
                            | _ -> false

                        if
                            cpuStillMoves
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
                            { Match =
                                { DocumentRevision = documentRevision
                                  Document = document
                                  State = state
                                  Events = FrozenList<MatchEvent>.Create events }
                              Error = null }
                | _ -> invalidReplay ()
            | validationError ->
                { Match = null
                  Error = validationError }

    let load (profile: LocalProfile) (cancellationToken: CancellationToken) =
        task {
            let! stored = documents.Read(matchKey, cancellationToken)

            match stored with
            | null ->
                cachedMatch <- null
                return { Match = null; Error = null }
            | document ->
                match cachedMatch with
                | NonNull cached when cached.DocumentRevision = document.Revision ->
                    return { Match = cached; Error = null }
                | _ ->
                    let schemaVersion = readSchemaVersion document.Json

                    if not schemaVersion.HasValue then
                        return
                            invalidDocument
                                "match.document_corrupt"
                                "The saved battle is damaged. No data changed."
                    elif schemaVersion.Value <> matchSchemaVersion then
                        return
                            invalidDocument
                                "match.document_version"
                                "This saved battle uses an unsupported version. No data changed."
                    else
                        let parsed =
                            try
                                Ok(
                                    JsonSerializer.Deserialize<MatchDocument>(
                                        document.Json,
                                        MatchJson.Options
                                    )
                                )
                            with
                            | :? JsonException -> Error()
                            | :? NotSupportedException -> Error()

                        match parsed with
                        | Error() ->
                            return
                                invalidDocument
                                    "match.document_corrupt"
                                    "The saved battle is damaged. No data changed."
                        | Ok Null ->
                            return
                                invalidDocument
                                    "match.document_corrupt"
                                    "The saved battle is damaged. No data changed."
                        | Ok(NonNull value) ->
                            if isMissing value.StartCommand || isMissing value.Start then
                                return
                                    invalidDocument
                                        "match.document_corrupt"
                                        "The saved battle is damaged. No data changed."
                            else
                                let replayed = replayDocument profile document.Revision value
                                cachedMatch <- replayed.Match
                                return replayed
        }

    let archiveCompletedMatch
        (profile: LocalProfile)
        (completed: LoadedMatch)
        (cancellationToken: CancellationToken)
        // The archive either rejects the saved history with a typed error, or reports nothing;
        // an F# option carries that better than a null across the whole body.
        : Task<ApiError option> =
        task {
            let! stored = documents.Read(matchHistoryKey, cancellationToken)

            let history =
                match stored with
                | null ->
                    Ok
                        { SchemaVersion = matchHistorySchemaVersion
                          AuthorityVersion = catalogue.Mechanics.ManifestVersion
                          Matches = FrozenList<MatchDocument>.Empty }
                | document ->
                    let parsed =
                        try
                            match
                                JsonSerializer.Deserialize<MatchHistoryDocument>(
                                    document.Json,
                                    MatchJson.Options
                                )
                            with
                            | null -> Error(historyCorrupt ())
                            | value -> Ok value
                        with
                        | :? JsonException -> Error(historyCorrupt ())
                        | :? NotSupportedException -> Error(historyCorrupt ())

                    match parsed with
                    | Error failure -> Error failure
                    | Ok value ->
                        if value.SchemaVersion <> matchHistorySchemaVersion then
                            Error(historyVersion ())
                        elif
                            not (
                                String.Equals(
                                    value.AuthorityVersion,
                                    catalogue.Mechanics.ManifestVersion,
                                    StringComparison.Ordinal
                                )
                            )
                        then
                            Error(historyAuthorityChanged ())
                        else
                            Ok value

            match history with
            | Error failure -> return Some failure
            | Ok document ->
                let archiveFailure =
                    document.Matches
                    |> Seq.tryPick (fun archived ->
                        if
                            isMissing archived
                            || isMissing archived.StartCommand
                            || isMissing archived.Start
                        then
                            Some(historyCorrupt ())
                        elif archived.SchemaVersion <> matchSchemaVersion then
                            Some(historyVersion ())
                        else
                            let replay = replayDocument profile 0L archived

                            match replay.Error with
                            | NonNull error when error.Code = "match.authority_changed" ->
                                Some(historyAuthorityChanged ())
                            | NonNull _ -> Some(historyCorrupt ())
                            | Null ->
                                match replay.Match with
                                | Null -> Some(historyCorrupt ())
                                | NonNull loaded when loaded.State.Phase <> MatchPhase.Complete ->
                                    Some(historyCorrupt ())
                                | NonNull _ -> None)

                match archiveFailure with
                | Some failure -> return Some failure
                | None ->
                    if
                        document.Matches
                        |> Seq.countBy _.Start.MatchId
                        |> Seq.exists (fun (_, count) -> count > 1)
                    then
                        return Some(historyCorrupt ())
                    else
                        match
                            document.Matches.SingleOrDefault(fun archived ->
                                archived.Start.MatchId = completed.Document.Start.MatchId)
                        with
                        | NonNull duplicate ->
                            return
                                (if documentsMatch duplicate completed.Document then
                                     None
                                 else
                                     Some(historyCorrupt ()))
                        | Null ->
                            let changed =
                                { document with
                                    Matches =
                                        FrozenList<MatchDocument>
                                            .Create(
                                                Seq.append document.Matches [ completed.Document ]
                                            ) }

                            let json = JsonSerializer.Serialize(changed, MatchJson.Options)

                            let! write =
                                match stored with
                                | null -> documents.Create(matchHistoryKey, json, cancellationToken)
                                | existing ->
                                    documents.Update(
                                        matchHistoryKey,
                                        existing.Revision,
                                        json,
                                        cancellationToken
                                    )

                            match write with
                            | :? DocumentWriteResult.Written -> return None
                            | _ ->
                                return
                                    Some(
                                        ApiError(
                                            "state.conflict",
                                            "The saved battle history changed in another tab. Start the battle again."
                                        )
                                    )
        }

    let reconcileStartConflict
        (profile: LocalProfile)
        (displayName: string)
        (commandId: Guid)
        (requestFingerprint: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let! reloaded = load profile cancellationToken

            if not (isNull (box reloaded.Error)) then
                return
                    { View = null
                      Error = reloaded.Error
                      Presentation = null }
            else
                match reloaded.Match with
                | null -> return stateConflict ()
                | loaded ->
                    if loaded.Document.StartCommand.ClientCommandId = commandId then
                        if
                            String.Equals(
                                loaded.Document.StartCommand.Fingerprint,
                                requestFingerprint,
                                StringComparison.Ordinal
                            )
                        then
                            return
                                { View = toView loaded displayName
                                  Error = null
                                  Presentation = null }
                        else
                            return
                                failed
                                    "match.command_conflict"
                                    "This request conflicts with a saved move. Start the battle again."
                    elif
                        loaded.Document.ClientCommands
                        |> Seq.exists (fun receipt -> receipt.ClientCommandId = commandId)
                    then
                        return
                            failed
                                "match.command_conflict"
                                "This request conflicts with a saved move. Start the battle again."
                    else
                        return stateConflict ()
        }

    let reconcileActionConflict
        (profile: LocalProfile)
        (displayName: string)
        (commandId: Guid)
        (requestFingerprint: string)
        (cancellationToken: CancellationToken)
        =
        task {
            let! reloaded = load profile cancellationToken

            if not (isNull (box reloaded.Error)) then
                return
                    { View = null
                      Error = reloaded.Error
                      Presentation = null }
            else
                match reloaded.Match with
                | null -> return stateConflict ()
                | loaded ->
                    if loaded.Document.StartCommand.ClientCommandId = commandId then
                        return
                            failed
                                "match.command_conflict"
                                "This request conflicts with the saved battle. Select the move again."
                    else
                        match
                            loaded.Document.ClientCommands.SingleOrDefault(fun candidate ->
                                candidate.ClientCommandId = commandId)
                        with
                        | Null -> return stateConflict ()
                        | NonNull receipt ->
                            if
                                String.Equals(
                                    receipt.Fingerprint,
                                    requestFingerprint,
                                    StringComparison.Ordinal
                                )
                            then
                                return
                                    { View = toView loaded displayName
                                      Error = null
                                      Presentation = null }
                            else
                                return
                                    failed
                                        "match.command_conflict"
                                        "This request conflicts with a saved move. Select the move again."
        }

    /// The saved battle as this player sees it.
    member _.State
        (
            profile: LocalProfile,
            displayName: string,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! loaded = load profile cancellationToken

            if not (isNull (box loaded.Error)) then
                return
                    { View = null
                      Error = loaded.Error
                      Presentation = null }
            else
                return
                    { View =
                        match loaded.Match with
                        | null -> null
                        | value -> toView value displayName
                      Error = null
                      Presentation = null }
        }

    /// Starts a battle against the computer.
    member _.Start
        (
            profile: LocalProfile,
            displayName: string,
            request: StartMatchRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            if request.CommandId = Guid.Empty then
                return failed "match.command_id" "Select the action again."
            else

                let! loaded = load profile cancellationToken

                if not (isNull (box loaded.Error)) then
                    return
                        { View = null
                          Error = loaded.Error
                          Presentation = null }
                else

                    let requestFingerprint = startFingerprint request

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
                                          Presentation = null }
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
                                    MatchStartRequest(
                                        MatchId(request.CommandId.ToString "D"),
                                        matchSeedFor profile request.CommandId,
                                        FrozenDeckSnapshot.Create(human, cards),
                                        FrozenDeckSnapshot.Create(
                                            cpuPlayer,
                                            cpuDeck.ExpandedCardIds
                                        )
                                    )

                                match engine.Start start with
                                | :? MatchStartOutcome.Started as started ->
                                    let commands = List<MatchCommand>()
                                    let events = List<MatchEvent>(started.Events)

                                    let presentation =
                                        List<PendingPresentation>(
                                            [ { State = started.State
                                                Events = started.Events } ]
                                        )

                                    let advanced =
                                        advanceCpu started.State commands events presentation

                                    if not (isNull (box advanced.Error)) then
                                        return
                                            { View = null
                                              Error = advanced.Error
                                              Presentation = null }
                                    else

                                        let document =
                                            { SchemaVersion = matchSchemaVersion
                                              AuthorityVersion = catalogue.Mechanics.ManifestVersion
                                              StartCommand =
                                                { ClientCommandId = request.CommandId
                                                  DeckId = request.DeckId
                                                  Fingerprint = requestFingerprint
                                                  StartRequestFingerprint =
                                                    gameStartFingerprint start }
                                              Start = start
                                              Commands = FrozenList<MatchCommand>.Create commands
                                              ClientCommands =
                                                FrozenList<MatchClientCommandReceipt>.Empty }

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
                                                | _ -> return None
                                            }

                                        match historyError with
                                        | Some error ->
                                            return
                                                { View = null
                                                  Error = error
                                                  Presentation = null }
                                        | None ->

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
                                                      Document = document
                                                      State = advanced.State
                                                      Events = FrozenList<MatchEvent>.Create events }

                                                cachedMatch <- committed

                                                return
                                                    { View = toView committed displayName
                                                      Error = null
                                                      Presentation =
                                                        toPresentation
                                                            document
                                                            displayName
                                                            presentation }
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

    /// Applies one player move, then lets the computer answer.
    member _.Apply
        (
            profile: LocalProfile,
            displayName: string,
            routeMatchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let submittedChoices = orEmpty request.Choices

            if request.CommandId = Guid.Empty then
                return failed "match.command_id" "Select the move again."
            elif
                String.IsNullOrWhiteSpace request.ActionId
                || submittedChoices
                   |> Seq.exists (fun choice -> not (choiceSubmissionIsStructurallyValid choice))
            then
                return failed "match.choice_invalid" "A submitted choice is invalid."
            else

                let! loaded = load profile cancellationToken

                if not (isNull (box loaded.Error)) then
                    return
                        { View = null
                          Error = loaded.Error
                          Presentation = null }
                else

                    match loaded.Match with
                    | null ->
                        return failed "match.required" "Start a battle before you select a move."
                    | current ->

                        let requestPayload = actionPayload routeMatchId request
                        let payloadFingerprint = fingerprint requestPayload

                        if current.Document.StartCommand.ClientCommandId = request.CommandId then
                            return
                                failed
                                    "match.command_conflict"
                                    "This request conflicts with the saved battle. Select the move again."
                        else

                            let receipt =
                                current.Document.ClientCommands.SingleOrDefault(fun candidate ->
                                    candidate.ClientCommandId = request.CommandId)

                            match receipt with
                            | NonNull saved ->
                                if
                                    String.Equals(
                                        saved.Fingerprint,
                                        payloadFingerprint,
                                        StringComparison.Ordinal
                                    )
                                then
                                    return
                                        { View = toView current displayName
                                          Error = null
                                          Presentation = null }
                                else
                                    return
                                        failed
                                            "match.command_conflict"
                                            "This move conflicts with a saved move. Select the move again."
                            | Null ->

                                match Guid.TryParse current.State.Id.Value with
                                | false, _ ->
                                    return
                                        failed
                                            "match.replay_invalid"
                                            "The saved battle is damaged. No data changed."
                                | true, persistedMatchId ->

                                    if persistedMatchId <> routeMatchId then
                                        return
                                            failed "match.wrong_match" "This battle is not active."
                                    elif current.State.Phase = MatchPhase.Complete then
                                        return
                                            failed
                                                "match.complete"
                                                "This battle is complete. Start a new battle."
                                    elif
                                        current.State.Revision.Value <> request.ExpectedRevision
                                    then
                                        return
                                            failed
                                                "match.stale"
                                                "The battle changed. Select the move again."
                                    else

                                        let human = humanPlayer profile

                                        match
                                            engine
                                                .GetLegalActions(current.State, human)
                                                .SingleOrDefault(fun candidate ->
                                                    String.Equals(
                                                        candidate.StableKey,
                                                        request.ActionId,
                                                        StringComparison.Ordinal
                                                    ))
                                        with
                                        | null ->
                                            return
                                                failed
                                                    "match.action_illegal"
                                                    "You cannot use that move now."
                                        | action ->

                                            let materialized =
                                                materializeHumanCommand
                                                    action
                                                    current.State
                                                    human
                                                    request.CommandId
                                                    submittedChoices

                                            if not (isNull (box materialized.Error)) then
                                                return
                                                    { View = null
                                                      Error = materialized.Error
                                                      Presentation = null }
                                            else

                                                match materialized.Command with
                                                | null ->
                                                    return
                                                        failed
                                                            "match.action_illegal"
                                                            "You cannot use that move now."
                                                | command ->

                                                    match engine.Apply(current.State, command) with
                                                    | :? CommandOutcome.Rejected as rejected ->
                                                        return
                                                            { View = null
                                                              Error =
                                                                rejection rejected.Rejection.Code
                                                              Presentation = null }
                                                    | outcome ->
                                                        let applied =
                                                            outcome :?> CommandOutcome.Applied

                                                        let commands =
                                                            List<MatchCommand>(
                                                                current.Document.Commands
                                                            )

                                                        commands.Add command

                                                        let events =
                                                            List<MatchEvent>(current.Events)

                                                        events.AddRange applied.Events

                                                        let presentation =
                                                            List<PendingPresentation>(
                                                                [ { State = applied.State
                                                                    Events = applied.Events } ]
                                                            )

                                                        let advanced =
                                                            advanceCpu
                                                                applied.State
                                                                commands
                                                                events
                                                                presentation

                                                        if not (isNull (box advanced.Error)) then
                                                            return
                                                                { View = null
                                                                  Error = advanced.Error
                                                                  Presentation = null }
                                                        else

                                                            let clientCommands =
                                                                List<MatchClientCommandReceipt>(
                                                                    current.Document.ClientCommands
                                                                )

                                                            clientCommands.Add
                                                                { ClientCommandId =
                                                                    request.CommandId
                                                                  Fingerprint = payloadFingerprint
                                                                  RequestPayload = requestPayload
                                                                  AppliedCommand = command.Id
                                                                  ResultRevision =
                                                                    advanced.State.Revision }

                                                            let document =
                                                                { current.Document with
                                                                    Commands =
                                                                        FrozenList<MatchCommand>
                                                                            .Create
                                                                            commands
                                                                    ClientCommands =
                                                                        FrozenList<
                                                                            MatchClientCommandReceipt
                                                                         >.Create
                                                                            clientCommands }

                                                            let! write =
                                                                documents.Update(
                                                                    matchKey,
                                                                    current.DocumentRevision,
                                                                    JsonSerializer.Serialize(
                                                                        document,
                                                                        MatchJson.Options
                                                                    ),
                                                                    cancellationToken
                                                                )

                                                            match write with
                                                            | :? DocumentWriteResult.Written as written ->
                                                                let committed =
                                                                    { DocumentRevision =
                                                                        written.Revision
                                                                      Document = document
                                                                      State = advanced.State
                                                                      Events =
                                                                        FrozenList<MatchEvent>
                                                                            .Create
                                                                            events }

                                                                cachedMatch <- committed

                                                                return
                                                                    { View =
                                                                        toView committed displayName
                                                                      Error = null
                                                                      Presentation =
                                                                        toPresentation
                                                                            document
                                                                            displayName
                                                                            presentation }
                                                            | _ ->
                                                                return!
                                                                    reconcileActionConflict
                                                                        profile
                                                                        displayName
                                                                        request.CommandId
                                                                        payloadFingerprint
                                                                        cancellationToken
        }

    /// Deletes the saved battle and its history.
    member _.PurgeSavedMatches([<Optional>] cancellationToken: CancellationToken) =
        task {
            do! documents.Delete(matchKey, cancellationToken)
            do! documents.Delete(matchHistoryKey, cancellationToken)
            cachedMatch <- null
        }
        :> Task
