namespace Blokemon.App

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.App.MatchFailures

/// Converts only the two persisted schema generations the repository has actually shipped. The
/// registry order is part of the migration: reshape schema 1 first, then bind the result to the
/// checked-out authority before it is deserialised and replayed.
module internal MatchMigrationJson =

    type private MigrationStep =
        { Identity: string
          Apply: string -> JsonObject -> Result<bool, MatchRecoveryReason> }

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

    let private hasExactMembers (expected: string list) (value: JsonObject) =
        let expectedMembers = HashSet<string>(expected, StringComparer.Ordinal)

        value.Count = expectedMembers.Count
        && value
           |> Seq.forall (fun memberValue -> expectedMembers.Contains memberValue.Key)

    let private actionFields command =
        match command with
        | "chooseMulliganBonus" -> Some [ "cardsToDraw" ]
        | "chooseOpening" -> Some [ "oche"; "booth" ]
        | "attachVim" -> Some [ "vim"; "bloke" ]
        | "playBloke" -> Some [ "bloke" ]
        | "promote" -> Some [ "promotion"; "bloke" ]
        | "playKit" -> Some [ "kit"; "target" ]
        | "taxi" -> Some [ "boothBloke"; "vimToChuck" ]
        | "usePartyTrick" -> Some [ "source"; "effect" ]
        | "attack" -> Some [ "attacker"; "attackId" ]
        | "chuckFossil" -> Some [ "fossil" ]
        | "endRound" -> Some []
        | "chooseReplacement" -> Some [ "boothBloke" ]
        | "resolveEffectChoice" -> Some []
        | "resolveKnockoutTrigger" -> Some [ "vim" ]
        | "resolveBarChitTrigger" -> Some [ "putOntoBooth" ]
        | "resign" -> Some []
        | _ -> None

    let private migrateChoices (node: JsonNode | null) =
        match node with
        | :? JsonArray as choices ->
            let migrated = JsonArray()
            let mutable failure = None

            for choice in choices do
                if failure.IsNone then
                    match choice with
                    | :? JsonObject as value ->
                        let migratedChoice = value.DeepClone().AsObject()

                        match migratedChoice["id"], migratedChoice["choiceId"] with
                        | null, _ -> migrated.Add migratedChoice
                        | _, null -> failure <- Some MatchRecoveryReason.Corrupt
                        | id, choiceId when JsonNode.DeepEquals(id, choiceId) ->
                            migratedChoice.Remove "id" |> ignore
                            migrated.Add migratedChoice
                        | _ -> failure <- Some MatchRecoveryReason.Corrupt
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None -> Ok migrated
        | _ -> corrupt

    let private migrateCommand (node: JsonNode) =
        match node with
        | :? JsonObject as command ->
            match stringMember "$command" command with
            | Error reason -> Error reason
            | Ok discriminator ->
                match actionFields discriminator with
                | None -> corrupt
                | Some fields ->
                    let common =
                        [ "$command"; "id"; "matchId"; "actor"; "expectedRevision"; "choices" ]

                    if not (hasExactMembers (common @ fields) command) then
                        corrupt
                    else
                        match migrateChoices command["choices"] with
                        | Error reason -> Error reason
                        | Ok choices ->
                            let migrated = JsonObject()

                            let clone (name: string) =
                                match command[name] with
                                | null -> null
                                | value -> value.DeepClone()

                            for name in [ "id"; "matchId"; "actor"; "expectedRevision" ] do
                                migrated[name] <- clone name

                            migrated["choices"] <- choices
                            let action = JsonObject()
                            action["$command"] <- JsonValue.Create discriminator

                            for name in fields do
                                action[name] <- clone name

                            migrated["action"] <- action
                            Ok(migrated :> JsonNode)
        | _ -> corrupt

    let private migrateCommands (document: JsonObject) =
        match arrayMember "commands" document with
        | Error reason -> Error reason
        | Ok commands ->
            let migrated = JsonArray()
            let mutable failure = None

            for command in commands do
                if failure.IsNone then
                    match command with
                    | null -> failure <- Some MatchRecoveryReason.Corrupt
                    | value ->
                        match migrateCommand value with
                        | Ok transformed -> migrated.Add transformed
                        | Error reason -> failure <- Some reason

            match failure with
            | Some reason -> Error reason
            | None ->
                document["commands"] <- migrated
                Ok()

    let private matchSchemaStep =
        { Identity = "match-schema-1-to-2"
          Apply =
            fun _ document ->
                match intMember "schemaVersion" document with
                | Ok version when version = matchSchemaVersion -> Ok false
                | Ok 1 ->
                    match migrateCommands document with
                    | Ok() ->
                        document["schemaVersion"] <- JsonValue.Create matchSchemaVersion
                        Ok true
                    | Error reason -> Error reason
                | Ok _ -> Error MatchRecoveryReason.UnsupportedVersion
                | Error reason -> Error reason }

    let private authorityStep identity =
        { Identity = identity
          Apply =
            fun authority document ->
                match stringMember "authorityVersion" document with
                | Error reason -> Error reason
                | Ok current when String.Equals(current, authority, StringComparison.Ordinal) ->
                    Ok false
                | Ok _ ->
                    document["authorityVersion"] <- JsonValue.Create authority
                    Ok true }

    let private matchSteps =
        [ matchSchemaStep; authorityStep "match-authority-to-checked-out" ]

    let private migrateHistorySchema (_: string) (history: JsonObject) =
        match intMember "schemaVersion" history, arrayMember "matches" history with
        | Error reason, _
        | _, Error reason -> Error reason
        | Ok version, _ when version <> 1 && version <> matchHistorySchemaVersion ->
            Error MatchRecoveryReason.UnsupportedVersion
        | Ok version, Ok matches ->
            let requiredMatchVersion = if version = 1 then 1 else matchSchemaVersion
            let mutable failure = None

            for archived: JsonNode in matches do
                if failure.IsNone then
                    match archived with
                    | :? JsonObject as document ->
                        match intMember "schemaVersion" document with
                        | Ok nestedVersion when nestedVersion = requiredMatchVersion ->
                            if version = 1 then
                                match migrateCommands document with
                                | Ok() ->
                                    document["schemaVersion"] <- JsonValue.Create matchSchemaVersion
                                | Error reason -> failure <- Some reason
                        | Ok _ -> failure <- Some MatchRecoveryReason.Corrupt
                        | Error reason -> failure <- Some reason
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None when version = 1 ->
                history["schemaVersion"] <- JsonValue.Create matchHistorySchemaVersion
                Ok true
            | None -> Ok false

    let private migrateHistoryAuthority authority (history: JsonObject) =
        match stringMember "authorityVersion" history, arrayMember "matches" history with
        | Error reason, _
        | _, Error reason -> Error reason
        | Ok historyAuthority, Ok matches ->
            let mutable failure = None
            let mutable changed = false

            for archived: JsonNode in matches do
                if failure.IsNone then
                    match archived with
                    | :? JsonObject as document ->
                        match stringMember "authorityVersion" document with
                        | Ok nestedAuthority when
                            String.Equals(
                                nestedAuthority,
                                historyAuthority,
                                StringComparison.Ordinal
                            )
                            ->
                            if
                                not (
                                    String.Equals(
                                        nestedAuthority,
                                        authority,
                                        StringComparison.Ordinal
                                    )
                                )
                            then
                                document["authorityVersion"] <- JsonValue.Create authority
                                changed <- true
                        | Ok _ -> failure <- Some MatchRecoveryReason.Corrupt
                        | Error reason -> failure <- Some reason
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None when String.Equals(historyAuthority, authority, StringComparison.Ordinal) ->
                Ok changed
            | None ->
                history["authorityVersion"] <- JsonValue.Create authority
                Ok true

    let private historySteps =
        [ { Identity = "match-history-schema-1-to-2"
            Apply = migrateHistorySchema }
          { Identity = "match-history-authority-to-checked-out"
            Apply = migrateHistoryAuthority } ]

    let private parseRoot (json: string) =
        try
            match JsonNode.Parse json with
            | :? JsonObject as root -> Ok root
            | _ -> corrupt
        with :? JsonException ->
            corrupt

    let private run (steps: MigrationStep list) (authority: string) (root: JsonObject) =
        let applied = ResizeArray<string>()
        let mutable failure = None

        for step in steps do
            if failure.IsNone then
                match step.Apply authority root with
                | Ok true -> applied.Add step.Identity
                | Ok false -> ()
                | Error reason -> failure <- Some reason

        match failure with
        | Some reason -> Error reason
        | None -> Ok(List.ofSeq applied)

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
        (steps: MigrationStep list)
        (normalise: 'Document -> 'Document)
        (authority: string)
        (json: string)
        : MatchMigrationPreparation<'Document> =
        match parseRoot json with
        | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
        | Ok root ->
            match run steps authority root with
            | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
            | Ok applied ->
                match deserialize normalise root with
                | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
                | Ok document ->
                    match applied with
                    | [] -> MatchMigrationPreparation.Current document
                    | identities ->
                        let candidateJson = JsonSerializer.Serialize(document, MatchJson.Options)

                        MatchMigrationPreparation.Candidate
                            { Document = document
                              Json = candidateJson
                              Identity = String.Join("+", identities)
                              ReboundAuthority =
                                identities
                                |> List.exists (fun identity ->
                                    identity.Contains("authority", StringComparison.Ordinal)) }

    let prepareMatch authority json =
        prepare matchSteps MatchDocumentNormalization.matchDocument authority json

    let prepareHistory authority json =
        prepare historySteps MatchDocumentNormalization.historyDocument authority json
