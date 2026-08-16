namespace Blokemon.Core.SetDesign;

public enum BlokemonPresentationStatus
{
    Accepted,
}

public enum BlokemonMechanicalType
{
    Grass,
    Fire,
    Water,
    Lightning,
    Psychic,
    Fighting,
    Darkness,
    Colorless,
    Dragon,
    Metal,
}

public enum BlokemonApprovedType
{
    Blazed,
    Curry,
    Sober,
    Beer,
    Geeked,
    Lairy,
    Dodgy,
    Local,
    Legend,
}

public enum BlokemonApprovedMechanicalLabel
{
    Blazed,
    Curry,
    Sober,
    Beer,
    Geeked,
    Lairy,
    Dodgy,
    Local,
    Legend,
    Roadie,
}

public sealed record BlokemonMechanicalDisplayMapping(
    BlokemonMechanicalType MechanicalType,
    BlokemonApprovedMechanicalLabel ApprovedLabel
);

public enum BlokemonRank
{
    Regular,
    Seasoned,
    Landlord,
}

public enum BlokemonProductBucket
{
    Common,
    Uncommon,
    Rare,
}

public enum BlokemonKitKind
{
    BarBit,
    Mate,
    Local,
    BarKit,
}

public enum BlokemonRoughState
{
    DodgyPint,
    Singed,
    NoddedOff,
    Legless,
    Muddled,
}

public enum BlokemonOpcode
{
    DealPrintedDamage,
    AdjustDamage,
    ScaleDamage,
    DealBoothDamage,
    PlaceDamageCounters,
    DealSelfDamage,
    HealDamage,
    ApplyRoughState,
    ClearRoughState,
    DrawFromStack,
    SearchStack,
    ShuffleStack,
    RevealCards,
    MoveCards,
    ChuckCards,
    AttachVim,
    MoveVim,
    ChuckVim,
    SwapOche,
    PreventDamage,
    PreventEffects,
    ReduceDamage,
    ModifyAttackCost,
    ModifyTaxiFare,
    ModifyStayingPower,
    ModifySoftSpot,
    IgnoreStubbornStreak,
    IgnoreSoftSpotAndStubbornStreak,
    RestrictAttack,
    RestrictTaxi,
    RestrictKit,
    RestrictLocal,
    RestrictEmptiesRecovery,
    ForceBeerMatBlank,
    ReflectAttackDamage,
    BeerMatToss,
    RepeatUntilBlankSide,
    Conditional,
    SendHome,
    RecoverFromSendHome,
    CopyAttack,
    Demote,
    TransformFromStack,
    TakeExtraBarChit,
    PlayAsBloke,
    ChuckSelf,
    TriggeredPartyTrick,
    ContinuousPartyTrick,
    OncePerRound,
    EndRoundEffect,
    BigHitterBarChits,
}

public enum BlokemonCondition
{
    Optional,
    FirstBeerMatIsBlankSide,
    SelfIsAtOche,
    SelfIsInBooth,
    SelfHasDamage,
    SelfHasVim,
    SelfHasSpecialVim,
    SelfHasRoughState,
    OwnMittIsEmpty,
    MittCountsAreEqual,
    MatePlayedThisRound,
    NamedBlokeInPlay,
    NamedBlokeInBooth,
    OtherOcheHasMechanicalType,
    OtherOcheHasDamage,
    OtherOcheHasRoughState,
    OtherOcheIsPromoted,
    OtherOcheIsBigHitter,
    AttachedVimCountsAreEqual,
    OwnBarChitCountIsGreater,
    TargetHasDamage,
    OtherBoothExists,
    BoothHasSpace,
    OwnBlokeSentHomeByOtherAttackDamage,
    OtherSentHomeByThisAttackDamage,
    OwnersFirstRound,
    OpenedSecond,
    PromotedFromMittThisRound,
    SourceIsRegular,
    TargetIsRegular,
    TargetIsSeasoned,
    TargetIsLandlord,
}

public enum BlokemonTarget
{
    Self,
    OwnOche,
    OwnBoothChosen,
    OwnBlokeChosen,
    OwnBlokesAll,
    OtherOche,
    OtherBoothChosen,
    OtherBoothAll,
    OtherBlokeChosen,
    OtherBlokesAll,
    OwnMitt,
    OtherMitt,
    OwnStack,
    OtherStack,
    OwnEmptiesTray,
    OtherEmptiesTray,
    OwnAttachedBarKits,
    OwnOcheAttachedVim,
    OtherOcheAttachedVim,
    KnockedOutBlokeAttachedVim,
    AttackingBloke,
    BarChits,
    LocalInPlay,
}

public enum BlokemonSelection
{
    Chosen,
    SeededRandom,
    OtherSideChosen,
    AnyDistribution,
    UpTo,
    All,
    Top,
    BeerMat,
    UntilBlankSide,
}

public enum BlokemonValueSource
{
    Fixed,
    PrintedDamage,
    SelfDamageCounters,
    OtherOcheDamageCounters,
    OtherBoothCount,
    OwnAttachedVim,
    OtherAttachedVim,
    BadgeSides,
    CardsChuckedByEffect,
    KitCardsInOtherMitt,
    QualifyingChuckedCards,
    MittCardsNeeded,
}

public enum BlokemonCardCategory
{
    Bloke,
    Vim,
    Kit,
}

public enum BlokemonEffectDestination
{
    Unspecified,
    OwnMitt,
    OtherMitt,
    OwnBooth,
    OtherBooth,
    OwnStack,
    OtherStack,
    BottomOfOwnStack,
    BottomOfOtherStack,
    OwnEmptiesTray,
    OtherEmptiesTray,
}

public sealed record BlokemonEffectCardFilter(
    BlokemonCardCategory[] Categories,
    BlokemonRank[] Ranks,
    BlokemonKitKind[] KitKinds,
    bool BasicVimOnly,
    bool DifferentMechanicalTypes,
    string[] ExcludedRelatedIds
);

public enum BlokemonTrigger
{
    Activated,
    Continuous,
    OnPromotionFromMitt,
    OnOwnBlokeSentHomeByOtherAttackDamage,
    BeforeSelfSentHomeByAttackDamage,
    AfterSelfDamagedByAttack,
    AfterSelfSentHomeByAttackDamage,
    OnBarChitTaken,
}

public sealed record BlokemonEffectPredicate(
    BlokemonCondition Condition,
    int Value,
    BlokemonMechanicalType? MechanicalType,
    BlokemonRoughState? RoughState,
    string? RelatedId
);

public sealed record BlokemonEffectInstruction(
    BlokemonOpcode Opcode,
    int Amount,
    BlokemonValueSource ValueSource,
    BlokemonTarget[] Targets,
    BlokemonSelection Selection,
    int TargetCount,
    BlokemonEffectPredicate[] Predicates,
    BlokemonMechanicalType[] MechanicalTypes,
    BlokemonRoughState[] RoughStates,
    string[] RelatedIds,
    BlokemonEffectInstruction[] Then,
    BlokemonEffectInstruction[] Otherwise,
    BlokemonTarget[]? Sources = null,
    BlokemonEffectDestination Destination = BlokemonEffectDestination.Unspecified,
    BlokemonEffectCardFilter? CardFilter = null,
    int SourceTopCount = 0
);

public sealed record BlokemonPartyTrick(
    string MechanicalId,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonTrigger Trigger,
    BlokemonEffectInstruction[] Program
);

public sealed record BlokemonAttack(
    string MechanicalId,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonMechanicalType[] VimCost,
    int PrintedDamage,
    bool VariablePrintedDamage,
    bool CanBeUsedFromBench,
    BlokemonEffectInstruction[] Program
);

internal static class BlokemonAttackSemantics
{
    internal static bool IsPureDamageAttack(BlokemonAttack attack) =>
        !attack.CanBeUsedFromBench
        && !attack.VariablePrintedDamage
        && attack.Program
            is [
                {
                    Opcode: BlokemonOpcode.DealPrintedDamage,
                    Amount: var amount,
                    ValueSource: BlokemonValueSource.PrintedDamage,
                    Targets: [BlokemonTarget.OtherOche],
                    Selection: BlokemonSelection.All,
                    TargetCount: 1,
                    Predicates.Length: 0,
                    MechanicalTypes.Length: 0,
                    RoughStates.Length: 0,
                    RelatedIds.Length: 0,
                    Then.Length: 0,
                    Otherwise.Length: 0,
                },
            ]
        && amount == attack.PrintedDamage;
}

public sealed record BlokemonHouseRule(
    string MechanicalId,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonEffectInstruction[] Program
);

public sealed record BlokemonMechanicalTypeModifier(
    BlokemonMechanicalType MechanicalType,
    string Modifier
);

public sealed record BlokemonCollectible(
    string Id,
    string ApprovedName,
    BlokemonApprovedType ApprovedType,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonRank Rank,
    int StayingPower,
    BlokemonMechanicalType[] MechanicalTypes,
    string? PromotesFromId,
    string[] PromotesToIds,
    BlokemonPartyTrick[] PartyTricks,
    BlokemonAttack[] Attacks,
    BlokemonHouseRule[] HouseRules,
    BlokemonMechanicalTypeModifier[] SoftSpots,
    BlokemonMechanicalTypeModifier[] StubbornStreaks,
    int TaxiFare,
    int BarChitsWhenSentHome,
    BlokemonProductBucket ProductBucket,
    int StackCopyLimit
);

public sealed record BlokemonKit(
    string Id,
    BlokemonKitKind Kind,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonPartyTrick[] PartyTricks,
    BlokemonAttack[] Attacks,
    BlokemonHouseRule[] HouseRules,
    bool FreelyAvailable,
    bool Owned,
    bool Pulled,
    bool Traded,
    int StackCopyLimit
);

public sealed record BlokemonBasicVim(
    string Id,
    BlokemonMechanicalType MechanicalType,
    BlokemonPresentationStatus PresentationStatus,
    bool FreelyAvailable,
    bool Owned,
    bool Pulled,
    bool Traded,
    int StackCopyLimit
);

public sealed record BlokemonOdds(int Numerator, int Denominator, double? Probability = null);

public sealed record BlokemonSingleProduct(
    int Count,
    string Selection,
    BlokemonOdds NamedIdentityOdds
);

public sealed record BlokemonProductSlot(BlokemonProductBucket Bucket, int Count, int PoolSize);

public sealed record BlokemonBucketOdds(
    BlokemonOdds Rare,
    BlokemonOdds Uncommon,
    BlokemonOdds Common
);

public sealed record BlokemonElevenProduct(
    int Count,
    BlokemonProductSlot[] Slots,
    bool WithoutReplacementWithinPack,
    bool Pity,
    bool DuplicatesAcrossPacks,
    BlokemonBucketOdds NamedIdentityInclusionOdds
);

public sealed record BlokemonProducts(BlokemonSingleProduct Single, BlokemonElevenProduct Eleven);

public sealed record BlokemonStackRules(
    int CardCount,
    int MechanicalCopyLimit,
    bool BasicVimExempt,
    bool RequiresRegularBloke
);

public sealed record BlokemonOpeningRules(
    bool OpeningParticipantSampledBeforeShuffle,
    int MittSize,
    int OcheRegularCount,
    int BoothLimit,
    string Mulligans,
    bool BothMulliganNoBonus,
    bool OtherSideBonusPerExtraMulligan,
    bool OtherSideBonusOptional,
    int BarChitCount,
    bool OpeningParticipantMayAttack,
    bool OpeningParticipantMayPlayMate
);

public sealed record BlokemonRoundRules(
    bool RequiredOpeningDraw,
    bool AttackEndsRound,
    bool PartyTricksAreNotAttacks
);

public sealed record BlokemonPromotionRules(
    bool ExactMechanicalEdgeRequired,
    bool NotOnEitherFirstRound,
    bool NotFirstRoundInPlay,
    bool NotTwiceInRound,
    bool RetainDamageAndAttachedCards,
    bool ClearRoughStatesAndAttackEffects
);

public sealed record BlokemonVimRules(
    int NormalAttachmentPerRound,
    bool CostNotChuckedUnlessSpecified,
    bool LocalSatisfiedByAnyVim
);

public sealed record BlokemonKitRules(
    string BarBitsPerRound,
    string BarKitsPerRound,
    int BarKitsPerBloke,
    int MatesPerRound,
    int LocalsPerRound,
    bool OneLocalInPlay,
    bool SameMechanicalLocalCannotReplace,
    bool NewLocalChucksOld
);

public sealed record BlokemonTaxiRules(
    int PerRound,
    bool ChuckVimPerFareSymbol,
    bool RequiresBooth,
    bool NoddedOffCannotTaxi,
    bool LeglessCannotTaxi,
    bool MovingToBoothClearsRoughStatesAndAttackEffects,
    bool AttachedCardsAndDamageRemain
);

public sealed record BlokemonDamageRules(
    bool BoothDamageUsesSoftSpotOrStubbornStreak,
    bool PlacedCountersUseDamageModifiers
);

public enum BlokemonAttackResolutionStep
{
    ValidateDeclaredAttackAndVim,
    ApplyEffectsThatAlterOrCancelAttack,
    ResolveMuddledCheck,
    MakeRequiredChoices,
    PayOrPerformUseRequirements,
    ApplyBeforeDamageEffects,
    CalculateAndPlaceDamage,
    ResolveOtherEffects,
    CheckAllSentHome,
    TakeBarChitsAndPromote,
    EndRound,
}

public enum BlokemonDamageResolutionStep
{
    PrintedOrProgramBaseDamage,
    EffectsOnAttackingBlokeBeforeSoftSpotAndStubbornStreak,
    SoftSpot,
    StubbornStreak,
    EffectsOnDefendingBlokeAfterSoftSpotAndStubbornStreak,
    ClampAtZeroAndPlaceCounters,
}

public enum BlokemonEffectDrawFromShortStack
{
    DrawAvailableCardsWithoutLosing,
}

public enum BlokemonRequiredRoundDrawFromEmptyStack
{
    LoseBout,
}

public sealed record BlokemonSelectionRules(
    string UpToCount,
    string AnyAmountOrNumber,
    string Optional
);

public sealed record BlokemonCheckupRules(
    BlokemonRoughState[] RoughStateOrder,
    bool OtherEffectsOutsideWholeBlock,
    bool CannotInterleave,
    bool SendHomeAfterBothChecks
);

public sealed record BlokemonRoughStateRule(
    BlokemonRoughState State,
    bool OcheOnly,
    int CheckupDamageCounters,
    bool CheckupBeerMat,
    bool BadgeSideRecovers,
    bool PreventsAttack,
    bool PreventsTaxi,
    bool? RecoversAfterOwnersNextRound,
    bool? BeforeAttackBeerMat,
    int? BlankSideCancelsAndSelfDamageCounters
);

public sealed record BlokemonRoughStateCoexistenceRules(
    BlokemonRoughState[] RotatedGroup,
    bool LatestRotatedStateReplacesPrevious,
    BlokemonRoughState[] MarkerGroup,
    bool MarkersCoexistWithEachOtherAndRotatedGroup,
    bool PromotionOrMoveToBoothClearsAll
);

public sealed record BlokemonSendHomeRules(
    bool DamageAtLeastStayingPower,
    bool ChuckBlokeAndAttachedCards,
    int NormalBarChits,
    int BigHitterBarChits,
    bool OwnerPromotesFromBooth
);

public sealed record BlokemonWinRules(
    string[] Conditions,
    string OneMethodEach,
    string MoreMethodsWins,
    int SuddenDeathBarChits,
    bool RepeatUntilWinner
);

public sealed record BlokemonFossilKitRules(
    string[] KitIds,
    int PlayAsRegularLocalStayingPower,
    bool CannotHaveRoughStates,
    bool CannotTaxi,
    bool MayChuckFromPlayDuringOwnersRound,
    bool SentHomeAwardsOneBarChit
);

public sealed record BlokemonBigHitterRules(string[] BlokeIds, int SentHomeBarChits);

public sealed record BlokemonBaseRules(
    string RulesVersion,
    BlokemonStackRules Stack,
    BlokemonOpeningRules Opening,
    BlokemonRoundRules Round,
    BlokemonPromotionRules Promotion,
    BlokemonVimRules Vim,
    BlokemonKitRules Kit,
    BlokemonTaxiRules Taxi,
    BlokemonAttackResolutionStep[] AttackOrder,
    BlokemonDamageResolutionStep[] DamageOrder,
    BlokemonDamageRules Damage,
    BlokemonSelectionRules SelectionRules,
    BlokemonEffectDrawFromShortStack EffectDrawFromShortStack,
    BlokemonRequiredRoundDrawFromEmptyStack RequiredRoundDrawFromEmptyStack,
    BlokemonCheckupRules Checkup,
    BlokemonRoughStateRule[] RoughStates,
    BlokemonRoughStateCoexistenceRules RoughStateCoexistence,
    BlokemonSendHomeRules SendHome,
    BlokemonWinRules Win,
    BlokemonFossilKitRules FossilKits,
    BlokemonBigHitterRules BigHitters,
    BlokemonOpcode[] OpcodeInventory,
    string TimingVersion,
    BlokemonTimingRule[] TimingRows
);

public sealed record BlokemonRuntimeManifest(
    string ManifestVersion,
    BlokemonPresentationStatus PresentationStatus,
    BlokemonMechanicalDisplayMapping[] ApprovedMechanicalDisplayMap,
    BlokemonCollectible[] Collectibles,
    BlokemonKit[] Kits,
    BlokemonBasicVim[] BasicVim,
    BlokemonProducts Products,
    BlokemonBaseRules BaseRules
);
