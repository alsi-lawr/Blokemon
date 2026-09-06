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

    // The index is rebound alone. The archived battles are data that exists: each keeps the
    // authority it was played under, and the game never reads one again.
    let private historyAuthorityTransition authority source =
        let target = currentHistory authority

        { Identity = identity "match-history" "authority" source target
          Source = source
          Target = target
          RebindsAuthority = true
          Apply =
            fun history ->
                history["authorityVersion"] <- JsonValue.Create target.Authority
                Ok() }

    let private parseRoot (json: string) =
        try
            match JsonNode.Parse json with
            | :? JsonObject as root -> Ok root
            | _ -> corrupt
        with :? JsonException ->
            corrupt

    let private run
        (registry: MatchMigrationTransition list)
        (supported: MatchMigrationVersion list)
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
        | Ok current when supported |> List.exists (sameVersion current) -> apply current []
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
        (current: string -> MatchMigrationVersion)
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

                match run registry (supportedSources current) target root with
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
        let registry = ordered current matchAuthorityTransition authority

        prepare
            current
            registry
            MatchDocumentNormalization.matchDocument
            matchPolicyPreflight
            authority
            json

    let prepareHistory authority json =
        let registry = ordered currentHistory historyAuthorityTransition authority

        // The index has no nested collection member to restore, and nothing to preflight.
        prepare currentHistory registry id (fun _ -> None) authority json

    /// Whether a stored history is one written before the index, with every archived battle
    /// inside it: schema 3, with a `matches` array.
    let isLegacyHistoryLayout (json: string) =
        match parseRoot json with
        | Ok root ->
            (match intMember "schemaVersion" root with
             | Ok 3 -> true
             | _ -> false)
            && (match root["matches"] with
                | :? JsonArray -> true
                | _ -> false)
        | Error _ -> false

    /// The authority a legacy history was written under, and each archived battle in it as its
    /// id and its own text, in the order they were archived.
    let legacyHistoryEntries
        (json: string)
        : Result<string * (string * string) list, MatchRecoveryReason> =
        match parseRoot json with
        | Error reason -> Error reason
        | Ok root ->
            match stringMember "authorityVersion" root, arrayMember "matches" root with
            | Error reason, _
            | _, Error reason -> Error reason
            | Ok authority, Ok matches ->
                let entries =
                    matches
                    |> Seq.map (fun archived ->
                        match archived with
                        | :? JsonObject as document ->
                            match document["start"] with
                            | :? JsonObject as start ->
                                match start["matchId"] with
                                | :? JsonObject as matchId ->
                                    match stringMember "value" matchId with
                                    | Ok id -> Ok(id, document.ToJsonString(MatchJson.Options))
                                    | Error reason -> Error reason
                                | _ -> corrupt
                            | _ -> corrupt
                        | _ -> corrupt)
                    |> Seq.toList

                match
                    entries
                    |> List.tryPick (function
                        | Error reason -> Some reason
                        | Ok _ -> None)
                with
                | Some reason -> Error reason
                | None ->
                    Ok(
                        authority,
                        entries
                        |> List.choose (function
                            | Ok entry -> Some entry
                            | Error _ -> None)
                    )

    /// The ids of the battles an index names, as far as it can be read. Only an index has
    /// battles stored beside it; a history written before the index holds its battles inside,
    /// and names none. An index that cannot be read names none.
    let archivedMatchIds (json: string) =
        match parseRoot json with
        | Error _ -> []
        | Ok root ->
            match root["matchIds"] with
            | :? JsonArray as ids ->
                ids
                |> Seq.choose (fun id ->
                    match id with
                    | null -> None
                    | value ->
                        try
                            Some(value.GetValue<string>())
                        with
                        | :? InvalidOperationException -> None
                        | :? FormatException -> None)
                |> Seq.toList
            | _ -> []
