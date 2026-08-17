namespace Blokemon.Core.SetDesign

type BlokemonTerminalClass =
    | NonTerminal = 0
    | ChallengeExpired = 1
    | RuleWin = 2
    | RuleDraw = 3
    | VoluntaryForfeit = 4
    | ActionTimeoutForfeit = 5
    | ReconnectForfeit = 6
    | CancelledMutual = 7
    | CancelledBothAbsent = 8
    | CancelledErasure = 9
    | CancelledAuthorizedRecovery = 10
    | CancelledUnrecoverableFailure = 11

type BlokemonReservationEffect =
    | None = 0
    | ReleaseBothDecks = 1

type BlokemonRatingEffect =
    | None = 0
    | WinLoss = 1
    | Draw = 2

type BlokemonStakeEffect =
    | None = 0
    | PayWinner = 1
    | RefundBoth = 2

type BlokemonHistoryEffect =
    | None = 0
    | RecordWinLoss = 1
    | RecordDraw = 2
    | RecordCancellation = 3

type BlokemonSettlementOwner =
    | None = 0
    | ProductFinalizer = 1

type BlokemonTimingClock =
    | None = 0
    | ChallengeEnabledTime = 1
    | ActionConnectedEnabledTime = 2
    | ReconnectEnabledTime = 3

type BlokemonTimingPauseCondition =
    | None = 0
    | ParticipantDisconnected = 1
    | ProcessStopped = 2
    | FeatureDisabled = 3
    | RecoverableSystemPause = 4

type BlokemonTimingRaceAuthority =
    | NotApplicable = 0
    | PersistedTimeoutRevision = 1
    | PersistedDisconnectRevision = 2
    | EarliestActiveReconnectGrace = 3

type BlokemonTimingEvent =
    | ChallengeDeadline = 0
    | RequiredActionAssigned = 1
    | ParticipantDisconnected = 2
    | TimeoutRevisionCommittedFirst = 3
    | DisconnectRevisionCommittedFirst = 4
    | SingleReconnectGraceDeadline = 5
    | EarliestReconnectGraceDeadlineBothAbsent = 6
    | ProcessRestarted = 7
    | FeatureDisabled = 8
    | RecoverableSystemPause = 9
    | RuleWinDeclared = 10
    | VoluntaryForfeitDeclared = 11
    | ActionTimeoutDeclared = 12
    | ReconnectForfeitDeclared = 13
    | RuleDrawDeclared = 14
    | MutualCancellationDeclared = 15
    | BothAbsentCancellationDeclared = 16
    | ViewerErasureDeclared = 17
    | AuthorisedRecoveryCancellationDeclared = 18
    | UnrecoverableFailureDeclared = 19

type BlokemonTimingOutcome =
    | ChallengeExpiresNoReservation = 0
    | ActionClockStartsWithNinetySeconds = 1
    | ActionClockPausesReconnectGraceStarts = 2
    | ActionTimeoutForfeitIntent = 3
    | ReconnectGraceControls = 4
    | AbsentParticipantForfeitIntent = 5
    | BothAbsentCancellationIntent = 6
    | ResumeExactRemainingTime = 7
    | RuleWinIntent = 8
    | VoluntaryForfeitIntent = 9
    | RuleDrawIntent = 10
    | MutualCancellationIntent = 11
    | ViewerErasureCancellationIntent = 12
    | AuthorisedRecoveryCancellationIntent = 13
    | UnrecoverableFailureCancellationIntent = 14

type BlokemonTimingIdempotencyKey =
    | ChallengeRevisionAndDeadline = 0
    | BattleRevisionAndActionOwner = 1
    | BattleRevisionAndParticipant = 2
    | BattleRevisionAndDeadline = 3
    | BattleFinalizationKey = 4
    | PauseRevision = 5

type BlokemonTimingRule =
    { Id: string
      Event: BlokemonTimingEvent
      Clock: BlokemonTimingClock
      DurationSeconds: int
      PauseConditions: BlokemonTimingPauseCondition array
      RaceAuthority: BlokemonTimingRaceAuthority
      TerminalClass: BlokemonTerminalClass
      Reservations: BlokemonReservationEffect
      Rating: BlokemonRatingEffect
      Stakes: BlokemonStakeEffect
      History: BlokemonHistoryEffect
      SettlementOwner: BlokemonSettlementOwner
      Outcome: BlokemonTimingOutcome
      IdempotencyKey: BlokemonTimingIdempotencyKey }
