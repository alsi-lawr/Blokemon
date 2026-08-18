namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

type MatchEvent =
    { Sequence: int64
      Revision: MatchRevision
      Kind: MatchEventKind
      Actor: PlayerId voption
      SourceCard: CardInstanceId voption
      TargetCards: ImmutableArray<CardInstanceId>
      Effect: EffectId voption
      RoughState: BlokemonRoughState voption
      DamageKind: DamageKind voption
      DrawReason: DrawReason voption
      Amount: int
      BadgeSide: bool voption
      StartRequest: MatchStartRequest voption
      Command: MatchCommand voption
      CommittedState: MatchState voption }

type DeckIssue =
    { Code: DeckIssueCode
      Player: PlayerId voption
      Card: MechanicalCardId voption
      Actual: int
      Expected: int }

type CommandRejection =
    { Code: CommandRejectionCode
      ChoiceRequirements: ImmutableArray<ChoiceRequirement> }

[<RequireQualifiedAccess>]
type MatchStartOutcome =
    | Started of state: MatchState * events: ImmutableArray<MatchEvent>
    | Rejected of issues: ImmutableArray<DeckIssue>

[<RequireQualifiedAccess>]
type CommandOutcome =
    | Applied of state: MatchState * events: ImmutableArray<MatchEvent>
    | Rejected of rejectedState: MatchState * rejection: CommandRejection

type ReplayIssueCode =
    | NoCommittedState = 0
    | NonIncreasingEventSequence = 1
    | RevisionWentBackwards = 2
    | DifferentMatch = 3
    | StateMismatch = 4
    | CommandRejected = 5

type ReplayIssue =
    { Code: ReplayIssueCode
      Position: int64 }

/// An event staged inside the builder before the commit stamps it with a sequence and revision.
/// The C# original declared eleven defaulted parameters; here the defaults live in
/// PendingMatchEvent.ofKind and every other member is set by copy-and-update at the call site.
type internal PendingMatchEvent =
    { Kind: MatchEventKind
      Actor: PlayerId voption
      SourceCard: CardInstanceId voption
      TargetCards: ImmutableArray<CardInstanceId>
      Effect: EffectId voption
      RoughState: BlokemonRoughState voption
      DamageKind: DamageKind voption
      DrawReason: DrawReason voption
      Amount: int
      BadgeSide: bool voption
      StartRequest: MatchStartRequest voption
      Command: MatchCommand voption }

[<RequireQualifiedAccess>]
module internal PendingMatchEvent =

    let ofKind (kind: MatchEventKind) =
        { Kind = kind
          Actor = ValueNone
          SourceCard = ValueNone
          TargetCards = ImmutableArray<_>.Empty
          Effect = ValueNone
          RoughState = ValueNone
          DamageKind = ValueNone
          DrawReason = ValueNone
          Amount = 0
          BadgeSide = ValueNone
          StartRequest = ValueNone
          Command = ValueNone }

    let forActor (kind: MatchEventKind) (actor: PlayerId) =
        { ofKind kind with
            Actor = ValueSome actor }

    let forCard (kind: MatchEventKind) (actor: PlayerId) (sourceCard: CardInstanceId) =
        { ofKind kind with
            Actor = ValueSome actor
            SourceCard = ValueSome sourceCard }

    let forCards
        (kind: MatchEventKind)
        (actor: PlayerId)
        (sourceCard: CardInstanceId)
        (targetCards: ImmutableArray<CardInstanceId>)
        =
        { ofKind kind with
            Actor = ValueSome actor
            SourceCard = ValueSome sourceCard
            TargetCards = targetCards }
