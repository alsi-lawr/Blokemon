namespace Blokemon.App

open Blokemon.App.Contracts

/// Which primary document must be recovered independently when automatic migration cannot prove
/// compatibility with the checked-out game.
[<RequireQualifiedAccess>]
type internal MatchRecoveryDocument =
    | ActiveMatch
    | MatchHistory

[<RequireQualifiedAccess>]
type internal MatchRecoveryReason =
    | Corrupt
    | UnsupportedVersion
    | IncompatibleWithCurrentRules

type internal MatchRecoveryRequirement =
    { Document: MatchRecoveryDocument
      Key: string
      Reason: MatchRecoveryReason }

type internal MatchMigrationReady<'Document> =
    { Stored: StoredDocument
      Document: 'Document }

[<RequireQualifiedAccess>]
type internal MatchMigrationOutcome<'Document> =
    | Ready of MatchMigrationReady<'Document>
    | RecoveryRequired of MatchRecoveryRequirement
    | Failed of ApiError

type internal MatchMigrationCandidate<'Document> =
    { Document: 'Document
      Json: string
      Identity: string
      ReboundAuthority: bool }

[<RequireQualifiedAccess>]
type internal MatchMigrationPreparation<'Document> =
    | Current of document: 'Document
    | Candidate of MatchMigrationCandidate<'Document>
    | RecoveryRequired of reason: MatchRecoveryReason
