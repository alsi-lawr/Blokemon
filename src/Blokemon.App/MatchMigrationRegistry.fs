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

/// The current schema at earlier authority revisions, and the only ordered routes into the
/// checked-out match format.
module internal MatchMigrationRegistry =

    let sameVersion left right =
        left.Schema = right.Schema
        && String.Equals(left.Authority, right.Authority, StringComparison.Ordinal)

    let identity kind change source target =
        $"{kind}-{change}-{source.Schema}-{source.Authority}-to-{target.Schema}-{target.Authority}"

    let current authority =
        { Schema = matchSchemaVersion
          Authority = authority }

    let supportedSources authority =
        [ current "sv151-candidate.14"
          current "sv151-candidate.15"
          current "sv151-candidate.16"
          current "sv151-candidate.17" ]

    let ordered authorityTransition authority =
        [ authorityTransition authority (current "sv151-candidate.12")
          authorityTransition authority (current "sv151-candidate.14")
          authorityTransition authority (current "sv151-candidate.15")
          authorityTransition authority (current "sv151-candidate.16")
          authorityTransition authority (current "sv151-candidate.17") ]
