namespace Blokemon.App

open System
open System.Collections.Immutable
open System.Text.Json.Serialization
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.Cpu

type internal GameCommandId = Blokemon.Game.CommandId

/// What a match operation produced: a view, a typed failure, and the steps to animate.
type MatchServiceResult =
    { View: MatchView | null
      Error: ApiError | null
      Presentation: MatchPresentationView | null }

    // The C# sealed record this replaces carried compiler-generated structural operators, and an
    // F# record emits none: a C# `==` against it would silently fall back to reference equality.
    static member op_Equality(left: MatchServiceResult, right: MatchServiceResult) =
        left.Equals right

    static member op_Inequality(left: MatchServiceResult, right: MatchServiceResult) =
        not (left.Equals right)

type internal MatchDocumentProjectionIdentity =
    { Revision: Nullable<int64>
      ContentIdentity: string | null }

type internal MatchProjectionResult =
    { View: MatchView | null
      Error: ApiError | null
      Recovery: MatchRecoveryView | null
      Presentation: MatchPresentationView | null
      DocumentIdentity: MatchDocumentProjectionIdentity }

// The persisted battle documents. [<CLIMutable>] is what lets these fields carry
// [<property: JsonRequired>]: System.Text.Json refuses a required property with no setter, and an
// immutable F# record has none. See .agent-workspace/068/probe-and-censuses.md leg (a0).
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
[<CLIMutable>]
type CpuPolicyDocument =
    { [<property: JsonRequired>]
      Version: int
      [<property: JsonRequired>]
      Difficulty: CpuDifficultyView
      [<property: JsonRequired>]
      Seed: uint64
      [<property: JsonRequired>]
      DecisionIndex: uint64
      [<property: JsonRequired>]
      Search: CpuSearchConfiguration }

[<CLIMutable>]
type MatchStartReceipt =
    { [<property: JsonRequired>]
      ClientCommandId: Guid
      [<property: JsonRequired>]
      DeckId: Guid
      [<property: JsonRequired>]
      Fingerprint: string
      [<property: JsonRequired>]
      StartRequestFingerprint: string
      [<property: JsonRequired>]
      CpuPolicy: CpuPolicyDocument }

[<CLIMutable>]
type MatchClientCommandReceipt =
    { [<property: JsonRequired>]
      ClientCommandId: Guid
      [<property: JsonRequired>]
      Fingerprint: string
      [<property: JsonRequired>]
      RequestPayload: string
      [<property: JsonRequired>]
      AppliedCommand: GameCommandId
      [<property: JsonRequired>]
      ResultRevision: MatchRevision }

[<CLIMutable>]
type MatchDocument =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      AuthorityVersion: string
      [<property: JsonRequired>]
      StartCommand: MatchStartReceipt
      [<property: JsonRequired>]
      Start: MatchStartRequest
      [<property: JsonRequired>]
      CpuPolicy: CpuPolicyDocument
      [<property: JsonRequired>]
      Commands: ImmutableArray<MatchCommand>
      [<property: JsonRequired>]
      ClientCommands: ImmutableArray<MatchClientCommandReceipt> }

[<CLIMutable>]
type MatchHistoryDocument =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      AuthorityVersion: string
      [<property: JsonRequired>]
      Matches: ImmutableArray<MatchDocument> }

[<CLIMutable>]
type MatchActionPayload =
    { [<property: JsonRequired>]
      MatchId: Guid
      [<property: JsonRequired>]
      ExpectedRevision: int64
      [<property: JsonRequired>]
      ActionId: string
      [<property: JsonRequired>]
      Choices: MatchChoiceSelectionRequest array }

// The in-memory carriers. None of these is persisted, so none needs CLIMutable.
type internal LoadedMatch =
    { DocumentRevision: int64
      DocumentContentIdentity: string
      Document: MatchDocument
      State: MatchState
      Events: ImmutableArray<MatchEvent> }

type internal MatchLoad =
    { Match: LoadedMatch | null
      Error: ApiError | null
      Recovery: MatchRecoveryRequirement option }

type internal CpuAdvance =
    { State: MatchState
      Policy: CpuPolicyDocument
      Error: ApiError | null }

type internal PendingPresentation =
    { State: MatchState
      Events: ImmutableArray<MatchEvent> }

type internal ActionSubjectView =
    { Source: string | null
      Target: string | null
      Effect: string | null }

type internal CommandMaterialization =
    { Command: MatchCommand | null
      Error: ApiError | null }


// The dependencies one service instance holds. Cached is the verified reconstruction of the
// stored document identified by DocumentRevision: it skips the O(history) deserialize-and-replay
// on every action, and any revision mismatch (another writer, cold load) falls back to the full
// verified replay.
type internal MatchContext =
    { Catalogue: BlokemonCatalogue
      Documents: IStateDocumentStore
      Keys: PlayerDocumentKeys
      Engine: MatchEngine
      Cpu: DeterministicCpu
      mutable Cached: LoadedMatch | null }
