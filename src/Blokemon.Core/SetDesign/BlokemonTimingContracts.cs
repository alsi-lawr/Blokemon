namespace Blokemon.Core.SetDesign;

public enum BlokemonTerminalClass
{
    NonTerminal,
    ChallengeExpired,
    RuleWin,
    RuleDraw,
    VoluntaryForfeit,
    ActionTimeoutForfeit,
    ReconnectForfeit,
    CancelledMutual,
    CancelledBothAbsent,
    CancelledErasure,
    CancelledAuthorizedRecovery,
    CancelledUnrecoverableFailure,
}

public enum BlokemonReservationEffect
{
    None,
    ReleaseBothDecks,
}

public enum BlokemonRatingEffect
{
    None,
    WinLoss,
    Draw,
}

public enum BlokemonStakeEffect
{
    None,
    PayWinner,
    RefundBoth,
}

public enum BlokemonHistoryEffect
{
    None,
    RecordWinLoss,
    RecordDraw,
    RecordCancellation,
}

public enum BlokemonSettlementOwner
{
    None,
    ProductFinalizer,
}

public enum BlokemonTimingClock
{
    None,
    ChallengeEnabledTime,
    ActionConnectedEnabledTime,
    ReconnectEnabledTime,
}

public enum BlokemonTimingPauseCondition
{
    None,
    ParticipantDisconnected,
    ProcessStopped,
    FeatureDisabled,
    RecoverableSystemPause,
}

public enum BlokemonTimingRaceAuthority
{
    NotApplicable,
    PersistedTimeoutRevision,
    PersistedDisconnectRevision,
    EarliestActiveReconnectGrace,
}

public enum BlokemonTimingEvent
{
    ChallengeDeadline,
    RequiredActionAssigned,
    ParticipantDisconnected,
    TimeoutRevisionCommittedFirst,
    DisconnectRevisionCommittedFirst,
    SingleReconnectGraceDeadline,
    EarliestReconnectGraceDeadlineBothAbsent,
    ProcessRestarted,
    FeatureDisabled,
    RecoverableSystemPause,
    RuleWinDeclared,
    VoluntaryForfeitDeclared,
    ActionTimeoutDeclared,
    ReconnectForfeitDeclared,
    RuleDrawDeclared,
    MutualCancellationDeclared,
    BothAbsentCancellationDeclared,
    ViewerErasureDeclared,
    AuthorisedRecoveryCancellationDeclared,
    UnrecoverableFailureDeclared,
}

public enum BlokemonTimingOutcome
{
    ChallengeExpiresNoReservation,
    ActionClockStartsWithNinetySeconds,
    ActionClockPausesReconnectGraceStarts,
    ActionTimeoutForfeitIntent,
    ReconnectGraceControls,
    AbsentParticipantForfeitIntent,
    BothAbsentCancellationIntent,
    ResumeExactRemainingTime,
    RuleWinIntent,
    VoluntaryForfeitIntent,
    RuleDrawIntent,
    MutualCancellationIntent,
    ViewerErasureCancellationIntent,
    AuthorisedRecoveryCancellationIntent,
    UnrecoverableFailureCancellationIntent,
}

public enum BlokemonTimingIdempotencyKey
{
    ChallengeRevisionAndDeadline,
    BattleRevisionAndActionOwner,
    BattleRevisionAndParticipant,
    BattleRevisionAndDeadline,
    BattleFinalizationKey,
    PauseRevision,
}

public sealed record BlokemonTimingRule(
    string Id,
    BlokemonTimingEvent Event,
    BlokemonTimingClock Clock,
    int DurationSeconds,
    BlokemonTimingPauseCondition[] PauseConditions,
    BlokemonTimingRaceAuthority RaceAuthority,
    BlokemonTerminalClass TerminalClass,
    BlokemonReservationEffect Reservations,
    BlokemonRatingEffect Rating,
    BlokemonStakeEffect Stakes,
    BlokemonHistoryEffect History,
    BlokemonSettlementOwner SettlementOwner,
    BlokemonTimingOutcome Outcome,
    BlokemonTimingIdempotencyKey IdempotencyKey
);
