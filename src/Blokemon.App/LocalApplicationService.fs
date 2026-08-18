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
open Blokemon.App.ApiResponses
open Blokemon.Core.SetDesign
open Blokemon.Product

// The persisted profile document carries no JsonRequired annotation, so it stays a plain F#
// record: BLOKEMON-066 proved this exact shape round-trips byte-identically and defaults every
// absent member.
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
type ProductDocument =
    { SchemaVersion: int
      CreationCommandId: Guid
      Profile: LocalProfileSnapshot }

type internal WebLocalIds =
    { Profile: Guid
      Decks: IReadOnlyDictionary<DeckId, Guid>
      PackReceipts: IReadOnlyDictionary<PackReceiptId, Guid> }

    static member TryCreate(profile: LocalProfile) : WebLocalIds | null =
        match Guid.TryParse profile.Id.Value with
        | false, _ -> null
        | true, profileId ->
            let decks = Dictionary<DeckId, Guid>()
            let packReceipts = Dictionary<PackReceiptId, Guid>()

            let deckIdsParsed =
                profile.SavedDecks.Keys
                |> Seq.forall (fun deckId ->
                    match Guid.TryParse deckId.Value with
                    | true, parsed ->
                        decks.Add(deckId, parsed)
                        true
                    | _ -> false)

            let receiptIdsParsed =
                deckIdsParsed
                && profile.PackReceipts.Keys
                   |> Seq.forall (fun receiptId ->
                       match Guid.TryParse receiptId.Value with
                       | true, parsed ->
                           packReceipts.Add(receiptId, parsed)
                           true
                       | _ -> false)

            if receiptIdsParsed then
                { Profile = profileId
                  Decks = decks
                  PackReceipts = packReceipts }
            else
                null

type internal LoadedProfile =
    { Revision: int64
      Document: ProductDocument
      Profile: LocalProfile
      Ids: WebLocalIds }

type internal ProfileLoad =
    { Profile: LoadedProfile | null
      Error: ApiError | null }

[<Sealed>]
type LocalApplicationService
    (
        catalogue: BlokemonCatalogue,
        documents: IStateDocumentStore,
        matches: LocalMatchService,
        economy: EconomyRules
    ) =

    static let profileKey = "profile"

    // Version 3 dropped the starter claim's deck snapshot. Older documents fail the version
    // check in LoadProfile and take the damaged-document recovery path; there is no migration.
    static let productSchemaVersion = 3

    static let json =
        JsonSerializerOptions(
            JsonSerializerDefaults.Web,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        )

    static let conflict () =
        ApiError("state.conflict", "The saved data changed. Select the action again.")

    static let invalidStateError () =
        ApiError("state.invalid", "The saved player data is damaged. No data changed.")

    static let invalidState () : ProfileLoad =
        { Profile = null
          Error = invalidStateError () }

    static let required (result: DomainResult<'TValue, TextValueFailure>) =
        match result with
        | DomainResult.Succeeded value -> value
        | DomainResult.Failed _ -> raise (UnreachableException())

    static let deckIssue (issue: DeckValidationIssue) =
        match issue with
        | DeckValidationIssue.QuantityMustBePositive(cardId, _) ->
            $"{cardId.Value} must have a positive quantity."
        | DeckValidationIssue.WrongCardCount(actual, requiredCount) ->
            $"The deck has {actual} cards. It must have {requiredCount} cards."
        | DeckValidationIssue.UnknownCard cardId ->
            $"{cardId.Value} is not in the current card set."
        | DeckValidationIssue.MechanicalCopyLimitExceeded(cardId, actual, allowed) ->
            $"{cardId.Value} has {actual} copies. The limit is {allowed}."
        | DeckValidationIssue.RegularCollectibleRequired ->
            "The deck needs at least one Regular Blokemon."
        | DeckValidationIssue.CollectibleQuantityNotOwned(cardId, requested, owned) ->
            $"{cardId.Value} requests {requested} copies, but only {owned} are owned."
        | DeckValidationIssue.CatalogueCardNotFree cardId ->
            $"{cardId.Value} is not freely available."

    static let deckFailure (reason: DeckSaveFailure) =
        match reason with
        | DeckSaveFailure.AlreadyExists _ -> ApiError("deck.exists", "That deck already exists.")
        | DeckSaveFailure.NotFound _ ->
            ApiError("deck.not_found", "The saved deck no longer exists.")
        | DeckSaveFailure.StaleRevision _ ->
            ApiError("deck.stale", "The saved deck changed. Reload the page.")
        | DeckSaveFailure.InvalidDeck issues ->
            ApiError("deck.invalid", String.Join(" ", issues |> Seq.map deckIssue))
        | DeckSaveFailure.RevisionExhausted _ ->
            ApiError("deck.revision", "The saved deck changed. Reload the page.")

    static let starterFailure (reason: StarterDeckClaimFailure) =
        match reason with
        | StarterDeckClaimFailure.CommandConflict _ ->
            ApiError(
                "starter.command_conflict",
                "This request conflicts with a saved choice. Choose the starter deck again."
            )
        | StarterDeckClaimFailure.AllowanceExhausted _ ->
            ApiError(
                "starter.already_claimed",
                "This player already opened its Starter Deck. This game allows one."
            )
        | StarterDeckClaimFailure.InvalidDeck issues ->
            ApiError("starter.invalid", String.Join(" ", issues |> Seq.map deckIssue))

    static let packFailure (reason: PackOpenFailure) =
        match reason with
        | PackOpenFailure.ReceiptIdAlreadyUsed ->
            ApiError("pack.receipt", "This pack was already opened.")
        | PackOpenFailure.ElevenCardPackUnavailable ->
            ApiError("pack.authority", "The current card set cannot supply an 11-card pack.")
        | PackOpenFailure.AuthorityVersionMismatch ->
            ApiError(
                "pack.authority_changed",
                "The card set changed. Reload the page before you open a pack."
            )
        | PackOpenFailure.PackAllowanceExhausted ->
            ApiError("pack.allowance", "You have opened every pack this player is allowed.")
        | _ -> raise (ArgumentOutOfRangeException(nameof reason))

    static let packSeed (profileId: ProfileId) (commandId: CommandId) =
        let bytes =
            SHA256.HashData(Encoding.UTF8.GetBytes $"{profileId.Value}:{commandId.Value}")

        BitConverter.ToUInt64 bytes

    static let sameDeck (existing: SavedDeck) (name: DeckName) (selections: DeckCardSelection seq) =
        let requested =
            selections
            |> Seq.groupBy _.CardId
            |> Seq.map (fun (cardId, rows) -> cardId, rows |> Seq.sumBy _.Quantity)
            |> dict

        existing.Name = name
        && existing.Cards.Count = requested.Count
        && existing.Cards
           |> Seq.forall (fun entry ->
               let requestedQuantity =
                   match requested.TryGetValue entry.Key with
                   | true, quantity -> quantity
                   | _ -> 0

               requestedQuantity = entry.Value)

    static let starterDefinition (starter: StarterDeck) =
        StarterDeckDefinition(
            required (StarterDeckId.Create starter.Id),
            required (DeckId.Create(starter.SavedDeckId.ToString "D")),
            required (DeckName.Create starter.Name),
            starter.Entries
            |> Seq.map (fun entry ->
                { CardId = required (CardId.Create entry.CardId)
                  Quantity = entry.Quantity })
        )

    // CardView is an App.Contracts C# record, so F# cannot copy-and-update it (FS0786): the
    // owned-quantity overlay is written as an explicit construction.
    let currentCard
        (id: string)
        (ownership: IReadOnlyDictionary<string, int>)
        (currentCards: IReadOnlyDictionary<string, CardView>)
        =
        match currentCards.TryGetValue id with
        | true, current ->
            CardView(
                current.Id,
                current.Name,
                current.Kind,
                current.Type,
                current.Detail,
                current.FaceHtml,
                current.Rules,
                ownership.GetValueOrDefault(id, 0),
                current.FreelyAvailable
            )
        | _ ->
            CardView(
                id,
                "Unavailable card",
                CardKindView.Blokemon,
                "Historical",
                "Not in the current card set",
                catalogue.ReverseFaceHtml,
                Array.empty,
                ownership.GetValueOrDefault(id, 0),
                false
            )

    let deckWarnings (deck: SavedDeck) =
        let includedEnergy =
            deck.Cards.Keys
            |> Seq.map _.Value
            |> Seq.filter (fun id -> id.StartsWith("VIM-", StringComparison.Ordinal))
            |> Seq.map (fun id ->
                catalogue.Mechanics.BasicVim.Single(fun card ->
                    String.Equals(card.Id, id, StringComparison.Ordinal)))
            |> Seq.map _.MechanicalType
            |> HashSet

        if includedEnergy.Count = 0 then
            [| "This deck has no Basic Energy. Its Blokemon cannot attack." |]
        else
            let hasPayableAttack =
                deck.Cards.Keys
                |> Seq.map _.Value
                |> Seq.filter (fun id -> id.StartsWith("BLK-", StringComparison.Ordinal))
                |> Seq.map (fun id ->
                    catalogue.Mechanics.Collectibles.Single(fun card ->
                        String.Equals(card.Id, id, StringComparison.Ordinal)))
                |> Seq.collect _.Attacks
                |> Seq.exists (fun attack ->
                    attack.VimCost
                    |> Array.forall (fun cost ->
                        cost = BlokemonMechanicalType.Colorless || includedEnergy.Contains cost))

            if hasPayableAttack then
                Array.empty
            else
                [| "The Basic Energy in this deck cannot pay for an attack." |]

    let deckView (profile: LocalProfile) (deck: SavedDeck) (deckId: Guid) =
        let validation =
            DeckValidator.Validate
                profile
                catalogue.Mechanics
                (deck.Cards
                 |> Seq.map (fun entry ->
                     { CardId = entry.Key
                       Quantity = entry.Value }))

        let issues =
            match validation with
            | DeckValidationResult.Invalid invalid -> invalid |> Seq.map deckIssue |> Seq.toArray
            | DeckValidationResult.Valid _ -> Array.empty

        let warnings = if issues.Length = 0 then deckWarnings deck else Array.empty

        DeckView(
            deckId,
            deck.Name.Value,
            deck.Revision.Value,
            deck.Cards
                .OrderBy((fun entry -> entry.Key.Value), StringComparer.Ordinal)
                .Select(fun entry -> DeckEntryView(entry.Key.Value, entry.Value))
                .ToArray(),
            issues.Length = 0,
            issues,
            warnings
        )

    let starterViews (claimedIds: IReadOnlySet<string>) (cards: IReadOnlyCollection<CardView>) =
        let currentCards = Dictionary<string, CardView>(StringComparer.Ordinal)

        for card in cards do
            currentCards.Add(card.Id, card)

        catalogue.StarterDecks.Decks
            .OrderBy((fun deck -> deck.Id), StringComparer.Ordinal)
            .Select(fun deck ->
                StarterDeckView(
                    deck.Id,
                    deck.Name,
                    deck.Type,
                    deck.Role,
                    deck.Description,
                    currentCards[deck.LeaderCardId],
                    deck.Entries
                    |> Seq.map (fun entry -> DeckEntryView(entry.CardId, entry.Quantity))
                    |> Seq.toArray,
                    deck.Entries
                    |> Seq.filter (fun entry ->
                        currentCards[entry.CardId].Kind = CardKindView.Blokemon)
                    |> Seq.sumBy _.Quantity,
                    deck.Entries
                    |> Seq.filter (fun entry -> currentCards[entry.CardId].Kind = CardKindView.Kit)
                    |> Seq.sumBy _.Quantity,
                    deck.Entries
                    |> Seq.filter (fun entry ->
                        currentCards[entry.CardId].Kind = CardKindView.BasicVim)
                    |> Seq.sumBy _.Quantity,
                    claimedIds.Contains deck.Id
                ))
            .ToArray()

    let toView
        (loaded: LoadedProfile | null)
        (cancellationToken: CancellationToken)
        (knownMatch: MatchServiceResult | null)
        =
        task {
            match loaded with
            | null ->
                let emptyCards = catalogue.CardsWithOwnership(Dictionary<string, int>())

                return
                    ApplicationView(
                        null,
                        emptyCards,
                        Array.empty,
                        starterViews (HashSet<string>(StringComparer.Ordinal)) emptyCards,
                        catalogue.PackPresentation,
                        null,
                        null,
                        null
                    )
            | profile ->
                let ownership = Dictionary<string, int>(StringComparer.Ordinal)

                for entry in profile.Profile.CollectibleOwnership do
                    ownership.Add(entry.Key.Value, entry.Value)

                let currentCards = Dictionary<string, CardView>(StringComparer.Ordinal)

                for card in catalogue.Cards do
                    currentCards.Add(card.Id, card)

                let cards =
                    currentCards.Keys
                        .Concat(ownership.Keys)
                        .Concat(
                            profile.Profile.SavedDecks.Values
                            |> Seq.collect (fun deck -> deck.Cards.Keys |> Seq.map _.Value)
                        )
                        .Concat(
                            profile.Profile.PackReceipts.Values
                            |> Seq.collect (fun receipt ->
                                receipt.SampledCollectibleIds |> Seq.map _.Value)
                        )
                        .Distinct(StringComparer.Ordinal)
                        .Select(fun id -> currentCard id ownership currentCards)
                        .OrderBy(fun card -> card.Kind)
                        .ThenBy((fun card -> card.Id), StringComparer.Ordinal)
                        .ToArray()

                let decks =
                    profile.Profile.SavedDecks.Values
                        .OrderBy(fun deck -> deck.Name.Value)
                        .Select(fun deck ->
                            deckView profile.Profile deck profile.Ids.Decks[deck.Id])
                        .ToArray()

                let lastPack =
                    profile.Profile.PackReceipts.Values
                        .OrderByDescending(fun receipt -> receipt.Sequence)
                        .Select(fun receipt ->
                            PackReceiptView(
                                profile.Ids.PackReceipts[receipt.Id],
                                receipt.Sequence,
                                receipt.SampledCollectibleIds
                                |> Seq.map (fun id -> currentCard id.Value ownership currentCards)
                                |> Seq.toArray
                            ))
                        .FirstOrDefault()

                let! resolvedMatch =
                    task {
                        match knownMatch with
                        | null ->
                            return!
                                matches.State(
                                    profile.Profile,
                                    profile.Profile.DisplayName.Value,
                                    cancellationToken
                                )
                        | known -> return known
                    }

                return
                    ApplicationView(
                        ProfileView(
                            profile.Ids.Profile,
                            profile.Profile.DisplayName.Value,
                            profile.Revision,
                            (match profile.Profile.LatestStarterDeckClaim with
                             | null -> null
                             | claim -> claim.Id.Value),
                            profile.Profile.RemainingPackAllowance,
                            (let remaining = profile.Profile.RemainingStarterDeckClaimAllowance

                             if remaining.HasValue then
                                 Nullable(remaining.Value = 0)
                             else
                                 Nullable())
                        ),
                        cards,
                        decks,
                        starterViews
                            (profile.Profile.StarterDeckClaims
                             |> Seq.map _.Id.Value
                             |> fun ids -> HashSet<string>(ids, StringComparer.Ordinal))
                            cards,
                        catalogue.PackPresentation,
                        lastPack,
                        resolvedMatch.View,
                        resolvedMatch.Error
                    )
        }

    let loadProfile (cancellationToken: CancellationToken) =
        task {
            let! stored = documents.Read(profileKey, cancellationToken)

            match stored with
            | null -> return { Profile = null; Error = null }
            | document ->
                let parsed =
                    try
                        Ok(JsonSerializer.Deserialize<ProductDocument>(document.Json, json))
                    with :? JsonException ->
                        Error()

                match parsed with
                | Error() -> return invalidState ()
                | Ok Null -> return invalidState ()
                | Ok(NonNull value) ->
                    if value.SchemaVersion <> productSchemaVersion then
                        return invalidState ()
                    else
                        match LocalProfile.Restore(value.Profile, catalogue.Mechanics) with
                        | DomainResult.Failed _ -> return invalidState ()
                        | DomainResult.Succeeded restored ->
                            match WebLocalIds.TryCreate restored with
                            | null -> return invalidState ()
                            | ids ->
                                return
                                    { Profile =
                                        { Revision = document.Revision
                                          Document = value
                                          Profile = restored
                                          Ids = ids }
                                      Error = null }
        }

    let save (loaded: LoadedProfile) (cancellationToken: CancellationToken) =
        task {
            match WebLocalIds.TryCreate loaded.Profile with
            | null -> return failed<ApplicationView> (invalidStateError ())
            | ids ->
                let! write =
                    documents.Update(
                        profileKey,
                        loaded.Revision,
                        JsonSerializer.Serialize(loaded.Document, json),
                        cancellationToken
                    )

                match write with
                | :? DocumentWriteResult.Written as written ->
                    let! view =
                        toView
                            { loaded with
                                Revision = written.Revision
                                Ids = ids }
                            cancellationToken
                            null

                    return succeeded view
                | _ -> return failed<ApplicationView> (conflict ())
        }

    /// Everything the client draws: the profile, its cards, decks and the saved battle.
    member _.State([<Optional>] cancellationToken: CancellationToken) =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | null ->
                let! view = toView loaded.Profile cancellationToken null
                return succeeded view
            | error -> return failed<ApplicationView> error
        }

    /// Creates this machine's local profile.
    member _.CreateProfile
        (request: CreateProfileRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | NonNull existing ->
                    if existing.Document.CreationCommandId = request.CommandId then
                        let! view = toView existing cancellationToken null
                        return succeeded view
                    else
                        return
                            failed<ApplicationView> (
                                ApiError(
                                    "profile.exists",
                                    "This machine already has a local profile."
                                )
                            )
                | Null ->

                    match DisplayName.Create request.DisplayName with
                    | DomainResult.Failed invalidName ->
                        return
                            failed<ApplicationView> (
                                ApiError(
                                    "profile.display_name",
                                    if invalidName = DisplayNameCreationFailure.TooLong then
                                        "The display name must be 32 characters or fewer."
                                    else
                                        "Enter a display name."
                                )
                            )
                    | DomainResult.Succeeded displayName ->

                        let persistedProfileId = Guid.NewGuid()
                        let profileId = required (ProfileId.Create(persistedProfileId.ToString "D"))

                        match
                            LocalProfile.Create(
                                profileId,
                                displayName,
                                catalogue.Mechanics,
                                economy
                            )
                        with
                        | DomainResult.Failed _ ->
                            return
                                failed<ApplicationView> (
                                    ApiError(
                                        "profile.authority",
                                        "The current card set does not contain a starter Blokemon."
                                    )
                                )
                        | DomainResult.Succeeded profile ->

                            let document =
                                { SchemaVersion = productSchemaVersion
                                  CreationCommandId = request.CommandId
                                  Profile = profile.ToSnapshot() }

                            let! write =
                                documents.Create(
                                    profileKey,
                                    JsonSerializer.Serialize(document, json),
                                    cancellationToken
                                )

                            match write with
                            | :? DocumentWriteResult.Written as written ->
                                let! view =
                                    toView
                                        { Revision = written.Revision
                                          Document = document
                                          Profile = profile
                                          Ids =
                                            { Profile = persistedProfileId
                                              Decks = Dictionary<DeckId, Guid>()
                                              PackReceipts = Dictionary<PackReceiptId, Guid>() } }
                                        cancellationToken
                                        null

                                return succeeded view
                            | _ -> return failed<ApplicationView> (conflict ())
        }

    /// Opens one pack for this profile.
    member _.OpenPack(request: OpenPackRequest, [<Optional>] cancellationToken: CancellationToken) =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before opening a pack."
                            )
                        )
                | current ->

                    let commandId = required (CommandId.Create(request.CommandId.ToString "D"))
                    let receiptId = required (PackReceiptId.Create(request.CommandId.ToString "D"))

                    let transition =
                        current.Profile.OpenPack(
                            commandId,
                            receiptId,
                            catalogue.Mechanics,
                            BlokemonSeededRandom(packSeed current.Profile.Id commandId)
                        )

                    match transition with
                    | DomainResult.Failed reason ->
                        return failed<ApplicationView> (packFailure reason)
                    | DomainResult.Succeeded opened ->
                        if opened.Disposition = PackOpenDisposition.AlreadyOpened then
                            let! view = toView current cancellationToken null
                            return succeeded view
                        else
                            let updated =
                                { current with
                                    Profile = opened.Profile
                                    Document =
                                        { current.Document with
                                            Profile = opened.Profile.ToSnapshot() } }

                            return! save updated cancellationToken
        }

    /// Claims one of the catalogue's starter decks.
    member _.ClaimStarterDeck
        (request: ClaimStarterDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError(
                                "profile.required",
                                "Create a player before you choose a starter deck."
                            )
                        )
                | current ->

                    if request.CommandId = Guid.Empty then
                        return
                            failed<ApplicationView> (
                                ApiError("starter.command_id", "Choose the starter deck again.")
                            )
                    else

                        match catalogue.StarterDecks.Find request.StarterDeckId with
                        | null ->
                            return
                                failed<ApplicationView> (
                                    ApiError(
                                        "starter.not_found",
                                        "Choose one of the available starter decks."
                                    )
                                )
                        | selected ->

                            let commandId =
                                required (CommandId.Create(request.CommandId.ToString "D"))

                            let definition = starterDefinition selected

                            match
                                current.Profile.ClaimStarterDeck(
                                    commandId,
                                    definition,
                                    catalogue.Mechanics
                                )
                            with
                            | DomainResult.Failed reason ->
                                return failed<ApplicationView> (starterFailure reason)
                            | DomainResult.Succeeded(StarterDeckClaimOutcome.AlreadyClaimed _) ->
                                let! view = toView current cancellationToken null
                                return succeeded view
                            | DomainResult.Succeeded(StarterDeckClaimOutcome.Claimed(claimed, _)) ->
                                let updated =
                                    { current with
                                        Profile = claimed
                                        Document =
                                            { current.Document with
                                                Profile = claimed.ToSnapshot() } }

                                return! save updated cancellationToken
        }

    /// Saves a new deck, or revises a saved one.
    member _.SaveDeck(request: SaveDeckRequest, [<Optional>] cancellationToken: CancellationToken) =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError("profile.required", "Create a player before you save a deck.")
                        )
                | current ->

                    match DeckName.Create request.Name with
                    | DomainResult.Failed _ ->
                        return failed<ApplicationView> (ApiError("deck.name", "Enter a deck name."))
                    | DomainResult.Succeeded name ->

                        let deckId =
                            required (
                                DeckId.Create(
                                    (if request.DeckId.HasValue then
                                         request.DeckId.Value
                                     else
                                         request.CommandId)
                                        .ToString
                                        "D"
                                )
                            )

                        let selections = List<DeckCardSelection>(request.Entries.Length)
                        let mutable unknownCard = false

                        for entry in request.Entries do
                            if not unknownCard then
                                match CardId.Create entry.CardId with
                                | DomainResult.Failed _ -> unknownCard <- true
                                | DomainResult.Succeeded cardId ->
                                    selections.Add
                                        { CardId = cardId
                                          Quantity = entry.Quantity }

                        if unknownCard then
                            return
                                failed<ApplicationView> (
                                    ApiError("deck.card_id", "The deck contains an unknown card.")
                                )
                        else

                            let alreadySaved =
                                match current.Profile.SavedDecks.TryGetValue deckId with
                                | true, existing -> sameDeck existing name selections
                                | _ -> false

                            if alreadySaved then
                                let! view = toView current cancellationToken null
                                return succeeded view
                            else

                                let transition =
                                    if not request.DeckId.HasValue then
                                        Ok(
                                            current.Profile.CreateDeck(
                                                deckId,
                                                name,
                                                selections,
                                                catalogue.Mechanics
                                            )
                                        )
                                    elif not request.ExpectedRevision.HasValue then
                                        Error(
                                            ApiError(
                                                "deck.revision",
                                                "The saved deck changed. Reload the page."
                                            )
                                        )
                                    else
                                        match
                                            DeckRevision.Create request.ExpectedRevision.Value
                                        with
                                        | DomainResult.Failed _ ->
                                            Error(
                                                ApiError(
                                                    "deck.revision",
                                                    "The saved deck changed. Reload the page."
                                                )
                                            )
                                        | DomainResult.Succeeded revision ->
                                            Ok(
                                                current.Profile.ReviseDeck(
                                                    deckId,
                                                    revision,
                                                    name,
                                                    selections,
                                                    catalogue.Mechanics
                                                )
                                            )

                                match transition with
                                | Error error -> return failed<ApplicationView> error
                                | Ok(DomainResult.Failed reason) ->
                                    return failed<ApplicationView> (deckFailure reason)
                                | Ok(DomainResult.Succeeded saved) ->
                                    let updated =
                                        { current with
                                            Profile = saved.Profile
                                            Document =
                                                { current.Document with
                                                    Profile = saved.Profile.ToSnapshot() } }

                                    return! save updated cancellationToken
        }

    /// Deletes a saved deck.
    member _.DeleteDeck
        (request: DeleteDeckRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<ApplicationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<ApplicationView> (
                            ApiError(
                                "profile.required",
                                "Create a player before you delete a deck."
                            )
                        )
                | current ->

                    let deckId = required (DeckId.Create(request.DeckId.ToString "D"))

                    match current.Profile.DeleteDeck deckId with
                    | DomainResult.Failed _ ->
                        return
                            failed<ApplicationView> (
                                ApiError("deck.not_found", "The saved deck no longer exists.")
                            )
                    | DomainResult.Succeeded deleted ->
                        let updated =
                            { current with
                                Profile = deleted.Profile
                                Document =
                                    { current.Document with
                                        Profile = deleted.Profile.ToSnapshot() } }

                        return! save updated cancellationToken
        }

    /// Starts a battle for this profile.
    member _.StartMatch
        (request: StartMatchRequest, [<Optional>] cancellationToken: CancellationToken)
        =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<MatchMutationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<MatchMutationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before starting a match."
                            )
                        )
                | current ->

                    let! played =
                        matches.Start(
                            current.Profile,
                            current.Profile.DisplayName.Value,
                            request,
                            cancellationToken
                        )

                    match played.Error with
                    | NonNull error -> return failed<MatchMutationView> error
                    | Null ->
                        let! view = toView current cancellationToken played
                        return succeeded (MatchMutationView(view, played.Presentation))
        }

    /// Applies one move to the saved battle.
    member _.ApplyMatchAction
        (
            matchId: Guid,
            request: ApplyMatchActionRequest,
            [<Optional>] cancellationToken: CancellationToken
        ) =
        task {
            let! loaded = loadProfile cancellationToken

            match loaded.Error with
            | NonNull error -> return failed<MatchMutationView> error
            | Null ->

                match loaded.Profile with
                | null ->
                    return
                        failed<MatchMutationView> (
                            ApiError(
                                "profile.required",
                                "Create a local profile before playing a match."
                            )
                        )
                | current ->

                    let! played =
                        matches.Apply(
                            current.Profile,
                            current.Profile.DisplayName.Value,
                            matchId,
                            request,
                            cancellationToken
                        )

                    match played.Error with
                    | NonNull error -> return failed<MatchMutationView> error
                    | Null ->
                        let! view = toView current cancellationToken played
                        return succeeded (MatchMutationView(view, played.Presentation))
        }

    /// Deletes every saved document this machine holds.
    member _.PurgeData([<Optional>] cancellationToken: CancellationToken) =
        task {
            do! matches.PurgeSavedMatches cancellationToken
            do! documents.Delete(profileKey, cancellationToken)
            let! view = toView null cancellationToken null
            return succeeded view
        }

    interface IBlokemonApplication with
        member this.State cancellationToken = this.State cancellationToken

        member this.CreateProfile(request, cancellationToken) =
            this.CreateProfile(request, cancellationToken)

        member this.OpenPack(request, cancellationToken) =
            this.OpenPack(request, cancellationToken)

        member this.ClaimStarterDeck(request, cancellationToken) =
            this.ClaimStarterDeck(request, cancellationToken)

        member this.SaveDeck(request, cancellationToken) =
            this.SaveDeck(request, cancellationToken)

        member this.DeleteDeck(request, cancellationToken) =
            this.DeleteDeck(request, cancellationToken)

        member this.StartMatch(request, cancellationToken) =
            this.StartMatch(request, cancellationToken)

        member this.ApplyMatchAction(matchId, request, cancellationToken) =
            this.ApplyMatchAction(matchId, request, cancellationToken)

        member this.PurgeData cancellationToken = this.PurgeData cancellationToken
