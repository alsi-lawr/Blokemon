namespace Blokemon.Core.SetDesign

open System

type BlokemonPresentationStatus =
    | Accepted = 0

type BlokemonMechanicalType =
    | Grass = 0
    | Fire = 1
    | Water = 2
    | Lightning = 3
    | Psychic = 4
    | Fighting = 5
    | Darkness = 6
    | Colorless = 7
    | Dragon = 8
    | Metal = 9

type BlokemonApprovedType =
    | Blazed = 0
    | Curry = 1
    | Sober = 2
    | Beer = 3
    | Geeked = 4
    | Lairy = 5
    | Dodgy = 6
    | Local = 7
    | Legend = 8

type BlokemonApprovedMechanicalLabel =
    | Blazed = 0
    | Curry = 1
    | Sober = 2
    | Beer = 3
    | Geeked = 4
    | Lairy = 5
    | Dodgy = 6
    | Local = 7
    | Legend = 8
    | Roadie = 9

type BlokemonMechanicalDisplayMapping =
    { MechanicalType: BlokemonMechanicalType
      ApprovedLabel: BlokemonApprovedMechanicalLabel }

type BlokemonRank =
    | Regular = 0
    | Seasoned = 1
    | Landlord = 2

type BlokemonProductBucket =
    | Common = 0
    | Uncommon = 1
    | Rare = 2

type BlokemonKitKind =
    | BarBit = 0
    | Mate = 1
    | Local = 2
    | BarKit = 3

type BlokemonRoughState =
    | DodgyPint = 0
    | Singed = 1
    | NoddedOff = 2
    | Legless = 3
    | Muddled = 4

type BlokemonOpcode =
    | DealPrintedDamage = 0
    | AdjustDamage = 1
    | ScaleDamage = 2
    | DealBoothDamage = 3
    | PlaceDamageCounters = 4
    | DealSelfDamage = 5
    | HealDamage = 6
    | ApplyRoughState = 7
    | ClearRoughState = 8
    | DrawFromStack = 9
    | SearchStack = 10
    | ShuffleStack = 11
    | RevealCards = 12
    | MoveCards = 13
    | ChuckCards = 14
    | AttachVim = 15
    | MoveVim = 16
    | ChuckVim = 17
    | SwapOche = 18
    | PreventDamage = 19
    | PreventEffects = 20
    | ReduceDamage = 21
    | ModifyAttackCost = 22
    | ModifyTaxiFare = 23
    | ModifyStayingPower = 24
    | ModifySoftSpot = 25
    | IgnoreStubbornStreak = 26
    | IgnoreSoftSpotAndStubbornStreak = 27
    | RestrictAttack = 28
    | RestrictTaxi = 29
    | RestrictKit = 30
    | RestrictLocal = 31
    | RestrictEmptiesRecovery = 32
    | ForceBeerMatBlank = 33
    | ReflectAttackDamage = 34
    | BeerMatToss = 35
    | RepeatUntilBlankSide = 36
    | Conditional = 37
    | SendHome = 38
    | RecoverFromSendHome = 39
    | CopyAttack = 40
    | Demote = 41
    | TransformFromStack = 42
    | TakeExtraBarChit = 43
    | PlayAsBloke = 44
    | ChuckSelf = 45
    | TriggeredPartyTrick = 46
    | ContinuousPartyTrick = 47
    | OncePerRound = 48
    | EndRoundEffect = 49

type BlokemonCondition =
    | Optional = 0
    | FirstBeerMatIsBlankSide = 1
    | SelfIsAtOche = 2
    | SelfIsInBooth = 3
    | SelfHasDamage = 4
    | SelfHasVim = 5
    | SelfHasSpecialVim = 6
    | SelfHasRoughState = 7
    | OwnMittIsEmpty = 8
    | MittCountsAreEqual = 9
    | MatePlayedThisRound = 10
    | NamedBlokeInPlay = 11
    | NamedBlokeInBooth = 12
    | OtherOcheHasMechanicalType = 13
    | OtherOcheHasDamage = 14
    | OtherOcheHasRoughState = 15
    | OtherOcheIsPromoted = 16
    | OtherOcheIsBigHitter = 17
    | AttachedVimCountsAreEqual = 18
    | OwnBarChitCountIsGreater = 19
    | TargetHasDamage = 20
    | OtherBoothExists = 21
    | BoothHasSpace = 22
    | OwnBlokeSentHomeByOtherAttackDamage = 23
    | OtherSentHomeByThisAttackDamage = 24
    | OwnersFirstRound = 25
    | OpenedSecond = 26
    | PromotedFromMittThisRound = 27
    | SourceIsRegular = 28
    | TargetIsRegular = 29
    | TargetIsSeasoned = 30
    | TargetIsLandlord = 31

type BlokemonTarget =
    | Self = 0
    | OwnOche = 1
    | OwnBoothChosen = 2
    | OwnBlokeChosen = 3
    | OwnBlokesAll = 4
    | OtherOche = 5
    | OtherBoothChosen = 6
    | OtherBoothAll = 7
    | OtherBlokeChosen = 8
    | OtherBlokesAll = 9
    | OwnMitt = 10
    | OtherMitt = 11
    | OwnStack = 12
    | OtherStack = 13
    | OwnEmptiesTray = 14
    | OtherEmptiesTray = 15
    | OwnAttachedBarKits = 16
    | OwnOcheAttachedVim = 17
    | OtherOcheAttachedVim = 18
    | KnockedOutBlokeAttachedVim = 19
    | AttackingBloke = 20
    | BarChits = 21
    | LocalInPlay = 22

type BlokemonSelection =
    | Chosen = 0
    | SeededRandom = 1
    | OtherSideChosen = 2
    | AnyDistribution = 3
    | UpTo = 4
    | All = 5
    | Top = 6
    | BeerMat = 7
    | UntilBlankSide = 8

type BlokemonValueSource =
    | Fixed = 0
    | PrintedDamage = 1
    | SelfDamageCounters = 2
    | OtherOcheDamageCounters = 3
    | OtherBoothCount = 4
    | OwnAttachedVim = 5
    | OtherAttachedVim = 6
    | BadgeSides = 7
    | CardsChuckedByEffect = 8
    | KitCardsInOtherMitt = 9
    | QualifyingChuckedCards = 10
    | MittCardsNeeded = 11

type BlokemonCardCategory =
    | Bloke = 0
    | Vim = 1
    | Kit = 2

type BlokemonEffectDestination =
    | Unspecified = 0
    | OwnMitt = 1
    | OtherMitt = 2
    | OwnBooth = 3
    | OtherBooth = 4
    | OwnStack = 5
    | OtherStack = 6
    | BottomOfOwnStack = 7
    | BottomOfOtherStack = 8
    | OwnEmptiesTray = 9
    | OtherEmptiesTray = 10

type BlokemonEffectCardFilter =
    { Categories: BlokemonCardCategory array
      Ranks: BlokemonRank array
      KitKinds: BlokemonKitKind array
      BasicVimOnly: bool
      DifferentMechanicalTypes: bool
      ExcludedRelatedIds: string array }

type BlokemonTrigger =
    | Activated = 0
    | Continuous = 1
    | OnPromotionFromMitt = 2
    | OnOwnBlokeSentHomeByOtherAttackDamage = 3
    | BeforeSelfSentHomeByAttackDamage = 4
    | AfterSelfDamagedByAttack = 5
    | AfterSelfSentHomeByAttackDamage = 6
    | OnBarChitTaken = 7

type BlokemonEffectPredicate =
    { Condition: BlokemonCondition
      Value: int
      MechanicalType: Nullable<BlokemonMechanicalType>
      RoughState: Nullable<BlokemonRoughState>
      RelatedId: string | null }

// [<CLIMutable>] on exactly the three ragged-JSON contract types is the requiredness convention
// ruled for this migration: the authority files omit these instructions' trailing properties, and
// RespectRequiredConstructorParameters = true would otherwise make every one of them mandatory
// because an F# record cannot declare a parameter default. The parameterless constructor and
// property setters CLIMutable adds are for System.Text.Json alone; nothing constructs or mutates
// these records that way.
[<CLIMutable>]
type BlokemonEffectInstruction =
    { Opcode: BlokemonOpcode
      Amount: int
      ValueSource: BlokemonValueSource
      Targets: BlokemonTarget array
      Selection: BlokemonSelection
      TargetCount: int
      Predicates: BlokemonEffectPredicate array
      MechanicalTypes: BlokemonMechanicalType array
      RoughStates: BlokemonRoughState array
      RelatedIds: string array
      Then: BlokemonEffectInstruction array
      Otherwise: BlokemonEffectInstruction array
      Sources: (BlokemonTarget array) | null
      Destination: BlokemonEffectDestination
      CardFilter: BlokemonEffectCardFilter | null
      SourceTopCount: int }

type BlokemonPartyTrick =
    { MechanicalId: string
      PresentationStatus: BlokemonPresentationStatus
      Trigger: BlokemonTrigger
      Program: BlokemonEffectInstruction array }

type BlokemonAttack =
    { MechanicalId: string
      PresentationStatus: BlokemonPresentationStatus
      VimCost: BlokemonMechanicalType array
      PrintedDamage: int
      VariablePrintedDamage: bool
      CanBeUsedFromBench: bool
      Program: BlokemonEffectInstruction array }

type BlokemonHouseRule =
    { MechanicalId: string
      PresentationStatus: BlokemonPresentationStatus
      Program: BlokemonEffectInstruction array }

type BlokemonMechanicalTypeModifier =
    { MechanicalType: BlokemonMechanicalType
      Modifier: string }

type BlokemonCollectible =
    { Id: string
      ApprovedName: string
      ApprovedType: BlokemonApprovedType
      PresentationStatus: BlokemonPresentationStatus
      Rank: BlokemonRank
      StayingPower: int
      MechanicalTypes: BlokemonMechanicalType array
      PromotesFromId: string | null
      PromotesToIds: string array
      PartyTricks: BlokemonPartyTrick array
      Attacks: BlokemonAttack array
      HouseRules: BlokemonHouseRule array
      SoftSpots: BlokemonMechanicalTypeModifier array
      StubbornStreaks: BlokemonMechanicalTypeModifier array
      TaxiFare: int
      BarChitsWhenSentHome: int
      ProductBucket: BlokemonProductBucket
      StackCopyLimit: int }

type BlokemonKit =
    { Id: string
      Kind: BlokemonKitKind
      PresentationStatus: BlokemonPresentationStatus
      PartyTricks: BlokemonPartyTrick array
      Attacks: BlokemonAttack array
      HouseRules: BlokemonHouseRule array
      FreelyAvailable: bool
      Owned: bool
      Pulled: bool
      Traded: bool
      StackCopyLimit: int }

type BlokemonBasicVim =
    { Id: string
      MechanicalType: BlokemonMechanicalType
      PresentationStatus: BlokemonPresentationStatus
      FreelyAvailable: bool
      Owned: bool
      Pulled: bool
      Traded: bool
      StackCopyLimit: int }

/// One of the three ragged-JSON contract types; see the note on BlokemonEffectInstruction.
[<CLIMutable>]
type BlokemonOdds =
    { Numerator: int
      Denominator: int
      Probability: Nullable<float> }

type BlokemonSingleProduct =
    { Count: int
      Selection: string
      NamedIdentityOdds: BlokemonOdds }

type BlokemonProductSlot =
    { Bucket: BlokemonProductBucket
      Count: int
      PoolSize: int }

type BlokemonBucketOdds =
    { Rare: BlokemonOdds
      Uncommon: BlokemonOdds
      Common: BlokemonOdds }

type BlokemonElevenProduct =
    { Count: int
      Slots: BlokemonProductSlot array
      WithoutReplacementWithinPack: bool
      Pity: bool
      DuplicatesAcrossPacks: bool
      NamedIdentityInclusionOdds: BlokemonBucketOdds }

type BlokemonProducts =
    { Single: BlokemonSingleProduct
      Eleven: BlokemonElevenProduct }

type BlokemonStackRules =
    { CardCount: int
      MechanicalCopyLimit: int
      BasicVimExempt: bool
      RequiresRegularBloke: bool }

type BlokemonOpeningRules =
    { OpeningParticipantSampledBeforeShuffle: bool
      MittSize: int
      OcheRegularCount: int
      BoothLimit: int
      Mulligans: string
      BothMulliganNoBonus: bool
      OtherSideBonusPerExtraMulligan: bool
      OtherSideBonusOptional: bool
      BarChitCount: int
      OpeningParticipantMayAttack: bool
      OpeningParticipantMayPlayMate: bool }

type BlokemonRoundRules =
    { RequiredOpeningDraw: bool
      AttackEndsRound: bool
      PartyTricksAreNotAttacks: bool }

type BlokemonPromotionRules =
    { ExactMechanicalEdgeRequired: bool
      NotOnEitherFirstRound: bool
      NotFirstRoundInPlay: bool
      NotTwiceInRound: bool
      RetainDamageAndAttachedCards: bool
      ClearRoughStatesAndAttackEffects: bool }

type BlokemonVimRules =
    { NormalAttachmentPerRound: int
      CostNotChuckedUnlessSpecified: bool
      LocalSatisfiedByAnyVim: bool }

type BlokemonKitRules =
    { BarBitsPerRound: string
      BarKitsPerRound: string
      BarKitsPerBloke: int
      MatesPerRound: int
      LocalsPerRound: int
      OneLocalInPlay: bool
      SameMechanicalLocalCannotReplace: bool
      NewLocalChucksOld: bool }

type BlokemonTaxiRules =
    { PerRound: int
      ChuckVimPerFareSymbol: bool
      RequiresBooth: bool
      NoddedOffCannotTaxi: bool
      LeglessCannotTaxi: bool
      MovingToBoothClearsRoughStatesAndAttackEffects: bool
      AttachedCardsAndDamageRemain: bool }

type BlokemonDamageRules =
    { BoothDamageUsesSoftSpotOrStubbornStreak: bool
      PlacedCountersUseDamageModifiers: bool }

type BlokemonAttackResolutionStep =
    | ValidateDeclaredAttackAndVim = 0
    | ApplyEffectsThatAlterOrCancelAttack = 1
    | ResolveMuddledCheck = 2
    | MakeRequiredChoices = 3
    | PayOrPerformUseRequirements = 4
    | ApplyBeforeDamageEffects = 5
    | CalculateAndPlaceDamage = 6
    | ResolveOtherEffects = 7
    | CheckAllSentHome = 8
    | TakeBarChitsAndPromote = 9
    | EndRound = 10

type BlokemonDamageResolutionStep =
    | PrintedOrProgramBaseDamage = 0
    | EffectsOnAttackingBlokeBeforeSoftSpotAndStubbornStreak = 1
    | SoftSpot = 2
    | StubbornStreak = 3
    | EffectsOnDefendingBlokeAfterSoftSpotAndStubbornStreak = 4
    | ClampAtZeroAndPlaceCounters = 5

type BlokemonEffectDrawFromShortStack =
    | DrawAvailableCardsWithoutLosing = 0

type BlokemonRequiredRoundDrawFromEmptyStack =
    | LoseBout = 0

type BlokemonSelectionRules =
    { UpToCount: string
      AnyAmountOrNumber: string
      Optional: string }

type BlokemonCheckupRules =
    { RoughStateOrder: BlokemonRoughState array
      OtherEffectsOutsideWholeBlock: bool
      CannotInterleave: bool
      SendHomeAfterBothChecks: bool }

type BlokemonRoughStateRule =
    { State: BlokemonRoughState
      OcheOnly: bool
      CheckupDamageCounters: int
      CheckupBeerMat: bool
      BadgeSideRecovers: bool
      PreventsAttack: bool
      PreventsTaxi: bool
      RecoversAfterOwnersNextRound: Nullable<bool>
      BeforeAttackBeerMat: Nullable<bool>
      BlankSideCancelsAndSelfDamageCounters: Nullable<int> }

type BlokemonRoughStateCoexistenceRules =
    { RotatedGroup: BlokemonRoughState array
      LatestRotatedStateReplacesPrevious: bool
      MarkerGroup: BlokemonRoughState array
      MarkersCoexistWithEachOtherAndRotatedGroup: bool
      PromotionOrMoveToBoothClearsAll: bool }

type BlokemonSendHomeRules =
    { DamageAtLeastStayingPower: bool
      ChuckBlokeAndAttachedCards: bool
      NormalBarChits: int
      BigHitterBarChits: int
      OwnerPromotesFromBooth: bool }

type BlokemonWinRules =
    { Conditions: string array
      OneMethodEach: string
      MoreMethodsWins: string
      SuddenDeathBarChits: int
      RepeatUntilWinner: bool }

type BlokemonFossilKitRules =
    { KitIds: string array
      PlayAsRegularLocalStayingPower: int
      CannotHaveRoughStates: bool
      CannotTaxi: bool
      MayChuckFromPlayDuringOwnersRound: bool
      SentHomeAwardsOneBarChit: bool }

type BlokemonBigHitterRules =
    { BlokeIds: string array
      SentHomeBarChits: int }

type BlokemonBaseRules =
    { RulesVersion: string
      Stack: BlokemonStackRules
      Opening: BlokemonOpeningRules
      Round: BlokemonRoundRules
      Promotion: BlokemonPromotionRules
      Vim: BlokemonVimRules
      Kit: BlokemonKitRules
      Taxi: BlokemonTaxiRules
      AttackOrder: BlokemonAttackResolutionStep array
      DamageOrder: BlokemonDamageResolutionStep array
      Damage: BlokemonDamageRules
      SelectionRules: BlokemonSelectionRules
      EffectDrawFromShortStack: BlokemonEffectDrawFromShortStack
      RequiredRoundDrawFromEmptyStack: BlokemonRequiredRoundDrawFromEmptyStack
      Checkup: BlokemonCheckupRules
      RoughStates: BlokemonRoughStateRule array
      RoughStateCoexistence: BlokemonRoughStateCoexistenceRules
      SendHome: BlokemonSendHomeRules
      Win: BlokemonWinRules
      FossilKits: BlokemonFossilKitRules
      BigHitters: BlokemonBigHitterRules
      OpcodeInventory: BlokemonOpcode array
      TimingVersion: string
      TimingRows: BlokemonTimingRule array }

type BlokemonRuntimeManifest =
    { ManifestVersion: string
      PresentationStatus: BlokemonPresentationStatus
      ApprovedMechanicalDisplayMap: BlokemonMechanicalDisplayMapping array
      Collectibles: BlokemonCollectible array
      Kits: BlokemonKit array
      BasicVim: BlokemonBasicVim array
      Products: BlokemonProducts
      BaseRules: BlokemonBaseRules }
