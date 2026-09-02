namespace Blokemon.App

open System
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.App.MatchFailures
open Blokemon.App.MatchMigrationRegistry
open Blokemon.Cpu

/// Converts only current-policy documents at persisted authority revisions found in source
/// history. Older CPU policies are rejected before any migration candidate can be written.
module internal MatchMigrationJson =

    let private corrupt = Error MatchRecoveryReason.Corrupt

    let private arrayMember (name: string) (value: JsonObject) =
        match value[name] with
        | :? JsonArray as memberValue -> Ok memberValue
        | _ -> corrupt

    let private stringMember (name: string) (value: JsonObject) =
        match value[name] with
        | null -> corrupt
        | memberValue ->
            try
                Ok(memberValue.GetValue<string>())
            with
            | :? InvalidOperationException -> corrupt
            | :? FormatException -> corrupt

    let private intMember (name: string) (value: JsonObject) =
        match value[name] with
        | null -> corrupt
        | memberValue ->
            try
                Ok(memberValue.GetValue<int>())
            with
            | :? InvalidOperationException -> corrupt
            | :? FormatException -> corrupt

    let private version (value: JsonObject) =
        match intMember "schemaVersion" value, stringMember "authorityVersion" value with
        | Ok schema, Ok authority ->
            Ok
                { Schema = schema
                  Authority = authority }
        | Error reason, _
        | _, Error reason -> Error reason

    let private policyVersion (node: JsonNode | null) =
        match node with
        | :? JsonObject as policy ->
            match policy["version"] with
            | null -> None
            | value ->
                try
                    Some(value.GetValue<int>())
                with
                | :? InvalidOperationException -> None
                | :? FormatException -> None
        | _ -> None

    let private documentUsesUnsupportedPolicy (document: JsonObject) =
        let startPolicy =
            match document["startCommand"] with
            | :? JsonObject as startCommand -> policyVersion startCommand["cpuPolicy"]
            | _ -> None

        [ startPolicy; policyVersion document["cpuPolicy"] ]
        |> List.exists (Option.exists ((<>) CpuPolicyVersion.active))

    let private matchPolicyPreflight (root: JsonObject) =
        if documentUsesUnsupportedPolicy root then
            Some MatchRecoveryReason.UnsupportedCpuPolicy
        else
            None

    let private historyPolicyPreflight (root: JsonObject) =
        match root["matches"] with
        | :? JsonArray as matches when
            matches
            |> Seq.exists (function
                | :? JsonObject as document -> documentUsesUnsupportedPolicy document
                | _ -> false)
            ->
            Some MatchRecoveryReason.UnsupportedCpuPolicy
        | _ -> None

    let private matchAuthorityTransition authority source =
        let target = current authority

        { Identity = identity "match" "authority" source target
          Source = source
          Target = target
          RebindsAuthority = true
          Apply =
            fun document ->
                document["authorityVersion"] <- JsonValue.Create target.Authority
                Ok() }

    let private migrateHistoryAuthority source target (history: JsonObject) =
        match arrayMember "matches" history with
        | Error reason -> Error reason
        | Ok matches ->
            let mutable failure = None

            for archived: JsonNode in matches do
                if failure.IsNone then
                    match archived with
                    | :? JsonObject as document ->
                        match version document with
                        | Ok nested when sameVersion nested source ->
                            document["authorityVersion"] <- JsonValue.Create target.Authority
                        | Ok _ -> failure <- Some MatchRecoveryReason.Corrupt
                        | Error reason -> failure <- Some reason
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None ->
                history["authorityVersion"] <- JsonValue.Create target.Authority
                Ok()

    let private historyAuthorityTransition authority source =
        let target = current authority

        { Identity = identity "match-history" "authority" source target
          Source = source
          Target = target
          RebindsAuthority = true
          Apply = migrateHistoryAuthority source target }

    let private parseRoot (json: string) =
        try
            match JsonNode.Parse json with
            | :? JsonObject as root -> Ok root
            | _ -> corrupt
        with :? JsonException ->
            corrupt

    let private run
        (registry: MatchMigrationTransition list)
        (target: MatchMigrationVersion)
        (root: JsonObject)
        =
        let rec apply current applied =
            if sameVersion current target then
                Ok(List.rev applied)
            else
                match
                    registry
                    |> List.tryFind (fun transition -> sameVersion transition.Source current)
                with
                | None -> Error MatchRecoveryReason.UnsupportedVersion
                | Some transition ->
                    match transition.Apply root with
                    | Error reason -> Error reason
                    | Ok() ->
                        match version root with
                        | Ok next when sameVersion next transition.Target ->
                            apply next (transition :: applied)
                        | Ok _ -> corrupt
                        | Error reason -> Error reason

        match version root with
        | Error reason -> Error reason
        | Ok current when sameVersion current target -> Ok []
        | Ok current when supportedSources target.Authority |> List.exists (sameVersion current) ->
            apply current []
        | Ok _ -> Error MatchRecoveryReason.UnsupportedVersion

    let private deserialize<'Document> (normalise: 'Document -> 'Document) (root: JsonObject) =
        try
            match root.Deserialize<'Document>(MatchJson.Options) with
            | null -> corrupt
            | value -> Ok(normalise value)
        with
        | :? JsonException -> corrupt
        | :? NotSupportedException -> corrupt
        | :? InvalidOperationException -> corrupt

    let private prepare<'Document>
        (registry: MatchMigrationTransition list)
        (normalise: 'Document -> 'Document)
        (policyPreflight: JsonObject -> MatchRecoveryReason option)
        (authority: string)
        (json: string)
        : MatchMigrationPreparation<'Document> =
        match parseRoot json with
        | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
        | Ok root ->
            match policyPreflight root with
            | Some reason -> MatchMigrationPreparation.RecoveryRequired reason
            | None ->
                let target = current authority

                match run registry target root with
                | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
                | Ok applied ->
                    match deserialize normalise root with
                    | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
                    | Ok document ->
                        match applied with
                        | [] -> MatchMigrationPreparation.Current document
                        | applied ->
                            let candidateJson =
                                JsonSerializer.Serialize(document, MatchJson.Options)

                            MatchMigrationPreparation.Candidate
                                { Document = document
                                  Json = candidateJson
                                  Identity = applied |> List.map _.Identity |> String.concat "+"
                                  ReboundAuthority = applied |> List.exists _.RebindsAuthority }

    let prepareMatch authority json =
        let registry = ordered matchAuthorityTransition authority

        prepare
            registry
            MatchDocumentNormalization.matchDocument
            matchPolicyPreflight
            authority
            json

    let prepareHistory authority json =
        let registry = ordered historyAuthorityTransition authority

        prepare
            registry
            MatchDocumentNormalization.historyDocument
            historyPolicyPreflight
            authority
            json
