using System.Diagnostics;
using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public enum MatchEventKind
{
    MatchStarted,
    CommandApplied,
    CardsShuffled,
    CardsDrawn,
    CardMoved,
    BeerMatTossed,
    DamagePlaced,
    DamageHealed,
    RoughStateApplied,
    RoughStateCleared,
    EffectRegistered,
    EffectChoiceRequested,
    TriggerQueued,
    TriggerResolved,
    AttackDeclared,
    AttackCancelled,
    BlokeSentHome,
    BarChitsTaken,
    RoundStarted,
    RoundEnded,
    SuddenDeathStarted,
    MatchWon,
    StateCommitted,
}

public sealed record MatchEvent(
    long Sequence,
    MatchRevision Revision,
    MatchEventKind Kind,
    PlayerId? Actor,
    CardInstanceId? SourceCard,
    FrozenList<CardInstanceId> TargetCards,
    EffectId? Effect,
    BlokemonRoughState? RoughState,
    DamageKind? DamageKind,
    DrawReason? DrawReason,
    int Amount,
    bool? BadgeSide,
    MatchStartRequest? StartRequest,
    MatchCommand? Command,
    MatchState? CommittedState
);

public enum DeckIssueCode
{
    InvalidMatchId,
    InvalidPlayerId,
    DuplicatePlayer,
    WrongCardCount,
    UnknownMechanicalCard,
    TooManyCopies,
    MissingRegularBloke,
    AuthorityInvalid,
}

public sealed record DeckIssue(
    DeckIssueCode Code,
    PlayerId? Player,
    MechanicalCardId? Card,
    int Actual,
    int Expected
);

public abstract record MatchStartOutcome
{
    private MatchStartOutcome() { }

    public sealed record Started(MatchState State, FrozenList<MatchEvent> Events)
        : MatchStartOutcome;

    public sealed record Rejected(FrozenList<DeckIssue> Issues) : MatchStartOutcome;

    public TResult Match<TResult>(
        Func<Started, TResult> started,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Started value => started(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };
}

public enum CommandRejectionCode
{
    WrongMatch,
    StaleRevision,
    UnknownActor,
    WrongPhase,
    NotActorsTurn,
    CardNotFound,
    CardNotOwned,
    WrongZone,
    WrongCardKind,
    RuleLimitReached,
    IllegalOpening,
    IneligiblePromotion,
    InsufficientVim,
    InvalidTaxiFare,
    EffectNotFound,
    EffectUnavailable,
    ChoiceRequired,
    InvalidChoice,
    MatchComplete,
    AuthorityMismatch,
    DuplicateCommand,
    WrongChooser,
}

public sealed record CommandRejection(
    CommandRejectionCode Code,
    FrozenList<ChoiceRequirement> ChoiceRequirements
);

public abstract record CommandOutcome
{
    private CommandOutcome() { }

    public sealed record Applied(MatchState State, FrozenList<MatchEvent> Events) : CommandOutcome;

    public sealed record Rejected(MatchState State, CommandRejection Rejection) : CommandOutcome;

    public TResult Match<TResult>(
        Func<Applied, TResult> applied,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Applied value => applied(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };
}

public enum ReplayIssueCode
{
    NoCommittedState,
    NonIncreasingEventSequence,
    RevisionWentBackwards,
    DifferentMatch,
    StateMismatch,
    CommandRejected,
}

public sealed record ReplayIssue(ReplayIssueCode Code, long Position);

public abstract record ReplayOutcome
{
    private ReplayOutcome() { }

    public sealed record Replayed(MatchState State) : ReplayOutcome;

    public sealed record Rejected(ReplayIssue Issue) : ReplayOutcome;

    public TResult Match<TResult>(
        Func<Replayed, TResult> replayed,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Replayed value => replayed(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };
}

internal sealed record PendingMatchEvent(
    MatchEventKind Kind,
    PlayerId? Actor = null,
    CardInstanceId? SourceCard = null,
    FrozenList<CardInstanceId> TargetCards = default,
    EffectId? Effect = null,
    BlokemonRoughState? RoughState = null,
    DamageKind? DamageKind = null,
    DrawReason? DrawReason = null,
    int Amount = 0,
    bool? BadgeSide = null,
    MatchStartRequest? StartRequest = null,
    MatchCommand? Command = null
);
