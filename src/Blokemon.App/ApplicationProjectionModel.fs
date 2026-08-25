namespace Blokemon.App

open System
open Blokemon.App.Contracts

[<Flags>]
type internal ApplicationProjectionDependency =
    | None = 0
    | Catalogue = 1
    | ProfileSummary = 2
    | CardUniverseAndOwnership = 4
    | SavedDecksAndOwnership = 8
    | StarterClaimsAndOwnership = 16
    | PackHistoryAndOwnership = 32
    | MatchProfile = 64
    | MatchDocument = 128
    | All = 255

type internal ApplicationProjectionSegment =
    | Profile = 0
    | Cards = 1
    | Decks = 2
    | StarterDecks = 3
    | PackPresentation = 4
    | LastPack = 5
    | Match = 6
    | MatchError = 7
    | MatchRecovery = 8

type internal ApplicationProjectionOperation =
    | State = 0
    | CreateProfile = 1
    | OpenPack = 2
    | ClaimStarterDeck = 3
    | SaveDeck = 4
    | DeleteDeck = 5
    | StartMatch = 6
    | ApplyMatchAction = 7
    | AbandonSavedMatch = 8
    | DiscardMatchHistory = 9
    | PurgeData = 10

type internal MatchProjectionSource =
    | LoadSavedMatch = 0
    | UseCommittedMatch = 1
    | NoMatch = 2

type internal ApplicationProjectionFieldRow =
    { Segment: ApplicationProjectionSegment
      Dependencies: ApplicationProjectionDependency }

type internal ApplicationProjectionOperationRow =
    { Operation: ApplicationProjectionOperation
      MatchSource: MatchProjectionSource }

module internal ApplicationProjectionMatrix =

    let fields =
        [| { Segment = ApplicationProjectionSegment.Profile
             Dependencies = ApplicationProjectionDependency.ProfileSummary }
           { Segment = ApplicationProjectionSegment.Cards
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.CardUniverseAndOwnership }
           { Segment = ApplicationProjectionSegment.Decks
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.SavedDecksAndOwnership }
           { Segment = ApplicationProjectionSegment.StarterDecks
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.StarterClaimsAndOwnership }
           { Segment = ApplicationProjectionSegment.PackPresentation
             Dependencies = ApplicationProjectionDependency.Catalogue }
           { Segment = ApplicationProjectionSegment.LastPack
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.PackHistoryAndOwnership }
           { Segment = ApplicationProjectionSegment.Match
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument }
           { Segment = ApplicationProjectionSegment.MatchError
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument }
           { Segment = ApplicationProjectionSegment.MatchRecovery
             Dependencies =
               ApplicationProjectionDependency.Catalogue
               ||| ApplicationProjectionDependency.MatchProfile
               ||| ApplicationProjectionDependency.MatchDocument } |]

    let operations =
        [| { Operation = ApplicationProjectionOperation.State
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.CreateProfile
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.OpenPack
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.ClaimStarterDeck
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.SaveDeck
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.DeleteDeck
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.StartMatch
             MatchSource = MatchProjectionSource.UseCommittedMatch }
           { Operation = ApplicationProjectionOperation.ApplyMatchAction
             MatchSource = MatchProjectionSource.UseCommittedMatch }
           { Operation = ApplicationProjectionOperation.AbandonSavedMatch
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.DiscardMatchHistory
             MatchSource = MatchProjectionSource.LoadSavedMatch }
           { Operation = ApplicationProjectionOperation.PurgeData
             MatchSource = MatchProjectionSource.NoMatch } |]

    let dependencies (segment: ApplicationProjectionSegment) = fields[int segment].Dependencies

    let operation (value: ApplicationProjectionOperation) = operations[int value]

type internal ApplicationProjectionRequest =
    { Generation: int64
      Operation: ApplicationProjectionOperation }

type internal ProfileProjectionIdentities =
    { Summary: string
      Cards: string
      Decks: string
      StarterDecks: string
      LastPack: string
      MatchProfile: string }

type internal ProfileProjectionIdentityPublication =
    | RetainProfileProjectionIdentities
    | ReplaceProfileProjectionIdentities of int64 * string * ProfileProjectionIdentities
    | ClearProfileProjectionIdentities

type internal ProfileProjectionIdentityResult =
    { Identities: ProfileProjectionIdentities
      Publication: ProfileProjectionIdentityPublication }

type internal ApplicationProjectionKeys =
    { Catalogue: string
      ProfileSummary: string
      Cards: string
      Decks: string
      StarterDecks: string
      LastPack: string
      MatchProfile: string
      MatchDocument: string }

type internal ApplicationProjectionBuilders =
    { Profile: unit -> ProfileView | null
      Cards: unit -> CardView array
      Decks: unit -> DeckView array
      StarterDecks: unit -> StarterDeckView array
      PackPresentation: unit -> PackPresentationView
      LastPack: unit -> PackReceiptView | null
      Match: unit -> MatchView | null
      MatchError: unit -> ApiError | null
      MatchRecovery: unit -> MatchRecoveryView | null }

[<Sealed>]
type internal ApplicationProjectionBuildCounts
    (
        profile: int64,
        cards: int64,
        decks: int64,
        starterDecks: int64,
        packPresentation: int64,
        lastPack: int64,
        matchView: int64,
        matchError: int64,
        matchRecovery: int64
    ) =

    member _.Profile = profile
    member _.Cards = cards
    member _.Decks = decks
    member _.StarterDecks = starterDecks
    member _.PackPresentation = packPresentation
    member _.LastPack = lastPack
    member _.Match = matchView
    member _.MatchError = matchError
    member _.MatchRecovery = matchRecovery

[<Sealed>]
type internal ApplicationProjectionHooks() =
    member val AfterGateAcquired: Action | null = null with get, set

    member val AfterProfileIdentityConstruction: Action | null = null with get, set

    member val AfterSegmentConstruction: Action<ApplicationProjectionSegment> | null =
        null with get, set

    member val AfterTemplateConstruction: Action | null = null with get, set

    member val BeforeTemplatePublication: Action | null = null with get, set

type internal CachedApplicationProjection =
    { Keys: ApplicationProjectionKeys
      View: ApplicationView }
