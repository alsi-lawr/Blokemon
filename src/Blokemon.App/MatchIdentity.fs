namespace Blokemon.App

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument
open Blokemon.App.MatchFailures
open Blokemon.Product
open Blokemon.Game

/// Who the players are, how a request fingerprints, and whether a stored or submitted shape is
/// structurally sound. Every guard here runs before the engine sees anything.
module internal MatchIdentity =

    let hasValue (value: string | null) = not (String.IsNullOrWhiteSpace value)

    let cardIdHasValue (card: CardInstanceId) = hasValue card.Value

    let humanPlayer (profile: LocalProfile) = PlayerId $"local:{profile.Id.Value}"

    let playerName (player: PlayerId) (human: PlayerId) (displayName: string) =
        if player = human then displayName else cpuName

    let fingerprint (payload: string) =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes payload)).ToLowerInvariant()

    let startFingerprint (request: StartMatchRequest) = fingerprint $"start:{request.DeckId:D}"

    let gameStartFingerprint (start: MatchStartRequest) =
        fingerprint (JsonSerializer.Serialize(start, MatchJson.Options))

    let matchSeedFor (profile: LocalProfile) (commandId: Guid) =
        let hash =
            SHA256.HashData(Encoding.UTF8.GetBytes $"{profile.Id.Value}:match:{commandId:D}")

        MatchSeed(BitConverter.ToUInt64 hash)

    let isClientCommand (command: GameCommandId) =
        command.Value.StartsWith("client:", StringComparison.Ordinal)
        && fst (Guid.TryParse(command.Value["client:".Length ..]))


    let startIsStructurallyValid (start: MatchStartRequest) =
        hasValue start.MatchId.Value
        && hasValue start.FirstDeck.Owner.Value
        && hasValue start.SecondDeck.Owner.Value
        && start.FirstDeck.Cards |> Seq.forall (fun card -> hasValue card.Value)
        && start.SecondDeck.Cards |> Seq.forall (fun card -> hasValue card.Value)

    let effectChoiceIsStructurallyValid (choice: EffectChoice | null) =
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

    let commandIsStructurallyValid (command: MatchCommand | null) =
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

    let choiceSubmissionIsStructurallyValid (choice: MatchChoiceSelectionRequest | null) =
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
