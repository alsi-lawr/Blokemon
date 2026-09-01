namespace Blokemon.App

open System
open System.Text.Json.Nodes
open Blokemon.App.MatchFailures

type internal MatchMigrationVersion = { Schema: int; Authority: string }

type internal MatchMigrationTransition =
    { Identity: string
      Source: MatchMigrationVersion
      Target: MatchMigrationVersion
      RebindsAuthority: bool
      Apply: JsonObject -> Result<unit, MatchRecoveryReason> }

/// The persisted schema and authority pairs found in source history, and the only ordered routes
/// from them into the checked-out match format.
module internal MatchMigrationRegistry =

    let sameVersion left right =
        left.Schema = right.Schema
        && String.Equals(left.Authority, right.Authority, StringComparison.Ordinal)

    let identity kind change source target =
        $"{kind}-{change}-{source.Schema}-{source.Authority}-to-{target.Schema}-{target.Authority}"

    let schemaOneCandidate12 =
        { Schema = 1
          Authority = "sv151-candidate.12" }

    let schemaOneCandidate14 =
        { Schema = 1
          Authority = "sv151-candidate.14" }

    let schemaTwo authority = { Schema = 2; Authority = authority }

    let current authority =
        { Schema = matchSchemaVersion
          Authority = authority }

    let supportedSources authority =
        [ schemaOneCandidate12
          schemaOneCandidate14
          schemaTwo "sv151-candidate.14"
          schemaTwo "sv151-candidate.15"
          schemaTwo "sv151-candidate.16"
          schemaTwo "sv151-candidate.17"
          schemaTwo authority
          current "sv151-candidate.14"
          current "sv151-candidate.15"
          current "sv151-candidate.16"
          current "sv151-candidate.17" ]

    let ordered schemaOneTransition schemaTwoTransition authorityTransition authority =
        [ schemaOneTransition schemaOneCandidate12
          schemaOneTransition schemaOneCandidate14
          schemaTwoTransition (schemaTwo "sv151-candidate.12")
          schemaTwoTransition (schemaTwo "sv151-candidate.14")
          schemaTwoTransition (schemaTwo "sv151-candidate.15")
          schemaTwoTransition (schemaTwo "sv151-candidate.16")
          schemaTwoTransition (schemaTwo "sv151-candidate.17")
          schemaTwoTransition (schemaTwo authority)
          authorityTransition authority (current "sv151-candidate.12")
          authorityTransition authority (current "sv151-candidate.14")
          authorityTransition authority (current "sv151-candidate.15")
          authorityTransition authority (current "sv151-candidate.16")
          authorityTransition authority (current "sv151-candidate.17") ]
