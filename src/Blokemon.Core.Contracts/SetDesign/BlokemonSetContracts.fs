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

type BlokemonRoughState =
    | DodgyPint = 0
    | NoddedOff = 1
    | Legless = 2
    | Muddled = 3

type BlokemonOpcode =
    | DealPrintedDamage = 0
    | AdjustDamage = 1
    | ScaleDamage = 2
    | DealBoothDamage = 3
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
    | ChuckVim = 17
    | SwapOche = 18
    | PreventDamage = 19
    | PreventEffects = 20
    | ReduceDamage = 21
    | ModifyTaxiFare = 23
    | ModifySoftSpot = 25
    | RestrictAttack = 28
    | RestrictTaxi = 29
    | RestrictKit = 30
    | ReflectAttackDamage = 34
    | BeerMatToss = 35
    | RepeatUntilBlankSide = 36
    | Conditional = 37
    | CopyAttack = 40
    | PlayAsBloke = 44
    | OncePerRound = 48
    | EnergyTrans = 50
    | EnergyBurn = 51
    | RainDance = 52
    | Shift = 53
    | Peek = 54
    | DamageSwap = 55
    | Cowardice = 56
    | StrangeBehavior = 57
    | ToxicGas = 58
    | Curse = 59
    | Buzzap = 60
    | InvisibleWall = 61
    | Transform = 62
    | Clairvoyance = 63
    | KabutoArmor = 64
    | PrehistoricPower = 65
    | ThickSkinned = 66
    | ChangeResistance = 67
    | Devolve = 68
    | HalfRemainingHpDamage = 69
    | BoostNextAttack = 70
    | PreventDamageUpTo = 71
    | FlipAttachedEnergy = 72
    | MirrorMove = 73
    | ReduceDamageFromDefender = 74
    | RearrangeTopDeck = 75
    | HealFromDamage = 76
    | ReturnDefenderToHand = 77
    | RequireDefenderCondition = 78
    | Wildfire = 79
    | DevolutionSpray = 80
    | PokemonBreeder = 81
    | ScoopUp = 82
    | AttachDefender = 83
    | AttachPlusPower = 84
    | PokemonCenter = 85
    | Revive = 86
    | SuperPotion = 87
    | Potion = 88
    | DestinyBond = 89

type BlokemonCondition =
    | SelfIsInBooth = 3
    | TargetHasDamage = 20

type BlokemonTarget =
    | Self = 0
    | OwnOche = 1
    | OwnBoothChosen = 2
    | OwnBlokeChosen = 3
    | OtherOche = 5
    | OtherBoothChosen = 6
    | OtherBoothAll = 7
    | OtherBlokeChosen = 8
    | OwnMitt = 10
    | OtherMitt = 11
    | OwnStack = 12
    | OtherStack = 13
    | OwnEmptiesTray = 14
    | OtherEmptiesTray = 15

type BlokemonSelection =
    | Chosen = 0
    | OtherSideChosen = 2
    | UpTo = 4
    | All = 5
    | BeerMat = 7
    | UntilBlankSide = 8

type BlokemonValueSource =
    | Fixed = 0
    | PrintedDamage = 1
    | SelfDamageCounters = 2
    | OtherOcheDamageCounters = 3
    | OtherAttachedVim = 6
    | BadgeSides = 7
    | OwnBoothCount = 12
    | ExtraTypedEnergy = 13
    | NamedPokemonInPlay = 14

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
    | OwnEmptiesTray = 9
    | TopOfOwnStack = 11

type BlokemonEffectCardFilter =
    { Categories: BlokemonCardCategory array
      Ranks: BlokemonRank array
      BasicVimOnly: bool
      DifferentMechanicalTypes: bool
      ExcludedRelatedIds: string array }

type BlokemonTrigger =
    | Activated = 0
    | Continuous = 1

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
      ProductBucket: BlokemonProductBucket
      StackCopyLimit: int }

type BlokemonKit =
    { Id: string
      PresentationStatus: BlokemonPresentationStatus
      PartyTricks: BlokemonPartyTrick array
      Attacks: BlokemonAttack array
      HouseRules: BlokemonHouseRule array
      FreelyAvailable: bool
      Owned: bool
      Pulled: bool
      Traded: bool
      StackCopyLimit: int
      StayingPower: int }

type BlokemonBasicVim =
    { Id: string
      MechanicalType: BlokemonMechanicalType
      Provides: BlokemonMechanicalType array
      IsBasic: bool
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
      PrizeCardCount: int
      OpeningParticipantMayAttack: bool }

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
      ColorlessSatisfiedByAnyEnergy: bool }

type BlokemonTrainerRules =
    { UnlimitedPerTurn: bool
      ResolveTextThenDiscard: bool }

type BlokemonPokemonPowerRules =
    { NotAttacks: bool
      UsableFromBench: bool
      DisabledBy: BlokemonRoughState array }

type BlokemonTaxiRules =
    { PerRound: int
      ChuckVimPerFareSymbol: bool
      RequiresBooth: bool
      MovingToBoothClearsRoughStatesAndAttackEffects: bool
      AttachedCardsAndDamageRemain: bool }

type BlokemonDamageRules =
    { BoothDamageUsesSoftSpotOrStubbornStreak: bool
      PlacedCountersUseDamageModifiers: bool }

type BlokemonAttackResolutionStep =
    | ValidateDeclaredAttackAndVim = 0
    | ResolveMuddledCheck = 1
    | MakeRequiredChoices = 2
    | PayOrPerformUseRequirements = 3
    | ApplyEffectsThatAlterOrCancelAttack = 4
    | ApplyBeforeDamageEffects = 5
    | CalculateAndPlaceDamage = 6
    | ResolveOtherEffects = 7
    | CheckAllSentHome = 8
    | TakeBarChitsAndPromote = 9
    | EndRound = 10

type BlokemonDamageResolutionStep =
    | PrintedOrProgramBaseDamage = 0
    | EffectsOnAttackingBloke = 1
    | StopWhenDamageIsZero = 2
    | Weakness = 3
    | Resistance = 4
    | TrainerEffects = 5
    | PokemonPowers = 6
    | PlaceDamageCounters = 7
    | EffectsAfterDamage = 8

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
      BlankSideCancelsAndSelfDamageCounters: Nullable<int>
      BeforeTaxiBeerMat: Nullable<bool>
      BlankSideConsumesTaxi: Nullable<bool> }

type BlokemonRoughStateCoexistenceRules =
    { RotatedGroup: BlokemonRoughState array
      LatestRotatedStateReplacesPrevious: bool
      MarkerGroup: BlokemonRoughState array
      MarkersCoexistWithEachOtherAndRotatedGroup: bool }

type BlokemonSendHomeRules =
    { DamageAtLeastStayingPower: bool
      ChuckBlokeAndAttachedCards: bool
      PrizeCardsPerKnockout: int
      OwnerPromotesFromBooth: bool }

type BlokemonWinRules =
    { Conditions: string array
      SimultaneousWin: string
      SuddenDeathPrizeCards: int
      SuddenDeathStartsFreshGame: bool
      RepeatUntilWinner: bool }

type BlokemonBaseRules =
    { RulesVersion: string
      Stack: BlokemonStackRules
      Opening: BlokemonOpeningRules
      Round: BlokemonRoundRules
      Promotion: BlokemonPromotionRules
      Vim: BlokemonVimRules
      Trainer: BlokemonTrainerRules
      PokemonPower: BlokemonPokemonPowerRules
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
      OpcodeInventory: BlokemonOpcode array }

type BlokemonRuntimeManifest =
    { ManifestVersion: string
      PresentationStatus: BlokemonPresentationStatus
      ApprovedMechanicalDisplayMap: BlokemonMechanicalDisplayMapping array
      Collectibles: BlokemonCollectible array
      Kits: BlokemonKit array
      BasicVim: BlokemonBasicVim array
      Products: BlokemonProducts
      BaseRules: BlokemonBaseRules }
