namespace Blokemon.App.Contracts;

public sealed record ApiError(string Code, string Message);

public sealed record ApiResponse<T>(bool Succeeded, T? Value, ApiError? Error);

public sealed record ProfileView(
    Guid Id,
    string DisplayName,
    long Revision,
    string? StarterDeckId,
    // Both allowances are null in the unlimited economy and set in the classic one.
    int? RemainingPacks = null,
    bool? StarterClaimUsed = null
);

public enum CardKindView
{
    Blokemon,
    Kit,
    BasicVim,
}

public enum CardRuleKindView
{
    Ability,
    Attack,
    Rule,
    Energy,
}

public sealed record CardRuleView(
    CardRuleKindView Kind,
    string Name,
    string? Text,
    string[] EnergyCost,
    int? Damage
);

public sealed record CardView(
    string Id,
    string Name,
    CardKindView Kind,
    string Type,
    string Detail,
    string FaceHtml,
    CardRuleView[] Rules,
    int OwnedQuantity,
    bool FreelyAvailable
);

public sealed record PackReceiptView(Guid Id, int Sequence, CardView[] Cards);

public sealed record DeckEntryView(string CardId, int Quantity);

public sealed record DeckView(
    Guid Id,
    string Name,
    long Revision,
    DeckEntryView[] Entries,
    bool IsLegal,
    string[] Errors,
    string[] Warnings
)
{
    public int CardCount => Entries.Sum(static entry => entry.Quantity);
}

public sealed record StarterDeckView(
    string Id,
    string Name,
    string Type,
    string Role,
    string Description,
    CardView Leader,
    DeckEntryView[] Entries,
    int BlokemonCount,
    int TrainerCount,
    int EnergyCount,
    bool IsClaimed
);

public sealed record PackStockPresentationView(
    string BoosterSvgMarkup,
    string StarterDeckSvgMarkup,
    string StarterDeckTraySvgMarkup
);

public sealed record PackPresentationView(
    PackStockPresentationView Gloss,
    PackStockPresentationView Kraft
);

public sealed record MatchSideView(
    string Name,
    string DeckName,
    int DeckCount,
    int HandCount,
    int PrizeCards,
    MatchCardInstanceView? Active,
    MatchCardInstanceView[] Bench,
    MatchCardInstanceView[] Hand,
    // A Kit played to the table stays in play as a public card, so it is projected like any
    // other card on the board. The zone is a single match-wide slot, so at most one side
    // carries one at a time.
    MatchCardInstanceView[] InPlayKits,
    // Everything this side has chucked, oldest first. The Empties Tray is a public zone - either
    // player may read either tray, and what is in one decides real choices, from what is left to
    // draw to whether a card that fetches from it has anything to fetch. The engine has always
    // had the zone and moved cards into it; it was simply never handed to the table, so a whole
    // public pile existed that no player could see.
    MatchCardInstanceView[] EmptiesTray,
    bool HasTurn
);

public enum MatchChoiceKindView
{
    Optional,
    Amount,
    Cards,
    MechanicalType,
    Attack,
    Distribution,
    Attachments,
}

public sealed record MatchChooserView(string Id, string Name, bool IsLocalPlayer);

public sealed record MatchCardInstanceView(
    string Id,
    CardView Card,
    string OwnerName,
    string Zone,
    int Damage,
    int HitPoints,
    CardView[] AttachedEnergy,
    CardView[] AttachedTools,
    CardView[] UnderlyingCards,
    string[] Conditions
);

public sealed record MatchMechanicalTypeOptionView(string Value, string Label);

public sealed record MatchEffectOptionView(string Id, string Label);

public sealed record MatchCardTypesView(string CardInstanceId, string[] MechanicalTypes);

public sealed record MatchChoiceRequirementView(
    string Id,
    MatchChoiceKindView Kind,
    string Label,
    MatchChooserView Chooser,
    int Minimum,
    int Maximum,
    MatchCardInstanceView[] EligibleCards,
    MatchMechanicalTypeOptionView[] EligibleMechanicalTypes,
    MatchEffectOptionView[] EligibleEffects,
    string? DependsOnOptional,
    MatchCardInstanceView[] EligibleTargets,
    bool RequireDifferentMechanicalTypes,
    MatchCardTypesView[] EligibleCardTypes
);

public sealed record MatchActionView(
    string Id,
    MatchActionKindView Kind,
    string Label,
    bool Primary,
    string? SourceCardInstanceId,
    string? TargetCardInstanceId,
    string? EffectId,
    MatchChoiceRequirementView[] ChoiceRequirements,
    string? DisabledReason
);

public enum MatchActionKindView
{
    ChooseMulliganBonus,
    ChooseOpening,
    ChooseBonusPlacement,
    ChooseReplacement,
    AttachEnergy,
    PlayBlokemon,
    Evolve,
    PlayTrainer,
    UseAbility,
    Attack,
    Retreat,
    DiscardFossil,
    EndTurn,
    ResolveChoice,
    ResolveKnockout,
    TakePrize,
    Resign,
}

public sealed record MatchAttackView(
    string SourceCardInstanceId,
    string EffectId,
    string Name,
    string[] EnergyCost,
    int PrintedDamage,
    string? ActionId,
    string? DisabledReason
);

// Where the match is in its own structure. A glowing card shows what may be touched and cannot
// show which situation the player is in, so the phase travels as the state it is rather than as a
// sentence about it, and the surface that names it decides the words.
public enum MatchPhaseView
{
    MulliganBonus,
    OpeningPlacement,
    Playing,
    AwaitingEffectChoice,
    AwaitingTriggerChoice,
    AwaitingReplacement,
    Complete,
    BonusPlacement,
}

public sealed record MatchFrameView(
    Guid Id,
    long Revision,
    int Round,
    MatchPhaseView Phase,
    MatchSideView Opponent,
    MatchSideView Player,
    bool IsComplete,
    string? Winner
);

public sealed record MatchView(
    MatchFrameView Frame,
    MatchActionView[] LegalActions,
    MatchAttackView[] Attacks,
    string[] RecentEvents
);

public enum MatchAnimationKindView
{
    Setup,
    Shuffle,
    Draw,
    Play,
    Attach,
    Evolve,
    Attack,
    Damage,
    Heal,
    Condition,
    Knockout,
    Prize,
    Turn,
    Coin,
    Victory,
    Reveal,
    Other,
}

public sealed record MatchEventCueView(
    long Sequence,
    MatchAnimationKindView Kind,
    string Label,
    string? SourceCardInstanceId,
    string[] TargetCardInstanceIds,
    int Amount,
    bool? BadgeSide,
    bool? ActorIsLocalPlayer,
    CardView[] RevealedCards
);

public sealed record MatchPresentationStepView(MatchFrameView Frame, MatchEventCueView[] Events);

public sealed record MatchPresentationView(MatchPresentationStepView[] Steps);

public enum MatchRecoveryKindView
{
    ActiveMatchUnsupportedVersion = 0,
    ActiveMatchIncompatibleWithCurrentRules = 1,
    MatchHistoryUnsupportedVersion = 2,
    MatchHistoryIncompatibleWithCurrentRules = 3,
}

public sealed record MatchRecoveryView(
    MatchRecoveryKindView Kind,
    long Revision,
    string ContentIdentity
);

public sealed record ApplicationView(
    ProfileView? Profile,
    CardView[] Cards,
    DeckView[] Decks,
    StarterDeckView[] StarterDecks,
    PackPresentationView PackPresentation,
    PackReceiptView? LastPack,
    MatchView? Match,
    ApiError? MatchError,
    MatchRecoveryView? MatchRecovery = null
);

public enum MatchMutationOutcomeView
{
    Applied = 0,
    RecoveryRequired = 1,
}

public sealed record MatchMutationView(
    ApplicationView Application,
    MatchPresentationView? Presentation,
    MatchMutationOutcomeView Outcome = MatchMutationOutcomeView.Applied
);

public sealed record CreateProfileRequest(Guid CommandId, string DisplayName);

public sealed record OpenPackRequest(Guid CommandId);

public sealed record ClaimStarterDeckRequest(Guid CommandId, string StarterDeckId);

public sealed record SaveDeckRequest(
    Guid CommandId,
    Guid? DeckId,
    long? ExpectedRevision,
    string Name,
    DeckEntryView[] Entries
);

public sealed record DeleteDeckRequest(Guid CommandId, Guid DeckId);

public sealed record StartMatchRequest(Guid CommandId, Guid DeckId);

public sealed record ApplyMatchActionRequest(
    Guid CommandId,
    long ExpectedRevision,
    string ActionId,
    MatchChoiceSelectionRequest[] Choices
);

public sealed record AbandonSavedMatchRequest(long ExpectedRevision, string ContentIdentity);

public sealed record DiscardMatchHistoryRequest(long ExpectedRevision, string ContentIdentity);

public sealed record MatchDamageAllocationRequest(string CardInstanceId, int Counters);

public sealed record MatchAttachmentRequest(string VimCardInstanceId, string BlokeCardInstanceId);

public sealed record MatchChoiceSelectionRequest(
    string Id,
    MatchChoiceKindView Kind,
    bool? Accepted,
    int? Amount,
    string[] CardInstanceIds,
    string? MechanicalType,
    string? EffectId,
    MatchDamageAllocationRequest[] Distribution,
    MatchAttachmentRequest[] Attachments
);

public interface IBlokemonApplication
{
    Task<ApiResponse<ApplicationView>> State(CancellationToken cancellationToken = default);

    Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> AbandonSavedMatch(
        AbandonSavedMatchRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> DiscardMatchHistory(
        DiscardMatchHistoryRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ApiResponse<ApplicationView>> PurgeData(CancellationToken cancellationToken = default);
}
