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

    /// A union member a stored document never wrote, or wrote as an explicit null, arrives here as
    /// a null reference: System.Text.Json leaves an unmentioned member alone, and it never calls a
    /// converter for a null token, so neither shape is refused by the deserialiser. Reading the
    /// case tag of one throws a NullReferenceException past the JsonException handlers around the
    /// deserialise, so the guards test the reference before they match on it. The nested records a
    /// choice payload carries are read the same way and get the same test.
    let effectChoiceIsStructurallyValid (choice: EffectChoice | null) =
        match choice with
        | null -> false
        | value when not (hasValue value.Id.Value) -> false
        | EffectChoice.Cards(_, cards) -> cards |> Seq.forall cardIdHasValue
        | EffectChoice.Attack(_, attack) -> hasValue attack.Value
        | EffectChoice.Attachments(_, placements) ->
            placements
            |> Seq.forall (fun item ->
                not (isMissing item) && hasValue item.Vim.Value && hasValue item.Bloke.Value)
        | EffectChoice.Amount _
        | EffectChoice.MechanicalType _ -> true

    let commandIsStructurallyValid (command: MatchCommand | null) =
        match command with
        | null -> false
        | value when
            not (hasValue value.Id.Value)
            || not (hasValue value.MatchId.Value)
            || not (hasValue value.Actor.Value)
            || value.ExpectedRevision.Value < 0L
            || isMissing value.Action
            || value.Choices
               |> Seq.exists (fun choice -> not (effectChoiceIsStructurallyValid choice))
            ->
            false
        | value ->
            match value.Action with
            | MatchAction.ChooseOpening(oche, booth) ->
                hasValue oche.Value && Seq.forall cardIdHasValue booth
            | MatchAction.ChooseBonusPlacement bonusBooth -> Seq.forall cardIdHasValue bonusBooth
            | MatchAction.AttachVim(vim, bloke) -> hasValue vim.Value && hasValue bloke.Value
            | MatchAction.PlayBloke bloke -> hasValue bloke.Value
            | MatchAction.Promote(promotion, promoted) ->
                hasValue promotion.Value && hasValue promoted.Value
            | MatchAction.PlayKit(kit, target) ->
                hasValue kit.Value && (target.IsNone || hasValue target.Value.Value)
            | MatchAction.Taxi(boothBloke, vimToChuck) ->
                hasValue boothBloke.Value && Seq.forall cardIdHasValue vimToChuck
            | MatchAction.UsePartyTrick(source, effect) ->
                hasValue source.Value && hasValue effect.Value
            | MatchAction.Attack(attacker, attackId) ->
                hasValue attacker.Value && hasValue attackId.Value
            | MatchAction.ChuckFossil fossil -> hasValue fossil.Value
            | MatchAction.ChooseReplacement replacement -> hasValue replacement.Value
            | MatchAction.ResolveKnockoutTrigger vim -> vim.IsNone || hasValue vim.Value.Value
            | MatchAction.ChooseMulliganBonus _
            | MatchAction.EndRound
            | MatchAction.ResolveEffectChoice
            | MatchAction.ResolveBarChitTrigger _
            | MatchAction.Resign -> true

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
