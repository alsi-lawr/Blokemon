namespace Blokemon.App

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.App.Contracts
open Blokemon.App.MatchFailures
open Blokemon.App.MatchIdentity
open Blokemon.App.MatchMigrationRegistry
open Blokemon.Game

/// Converts only persisted schema and authority pairs found in source history. The registry order
/// is part of the migration: reshape schema 1, pin schema 2 to its legacy CPU policy, then bind the
/// result to the checked-out authority before it is deserialised and replayed.
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

    let private uint64Member (name: string) (value: JsonObject) =
        match value[name] with
        | null -> corrupt
        | memberValue ->
            try
                Ok(memberValue.GetValue<uint64>())
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

    let private policyNode (policy: CpuPolicyDocument) =
        JsonSerializer.SerializeToNode(policy, MatchJson.Options)

    let private migrateCpuPolicy (document: JsonObject) =
        let startNode: JsonNode | null = document["start"]
        let startCommandNode: JsonNode | null = document["startCommand"]
        let commandsNode: JsonNode | null = document["commands"]

        match startNode, startCommandNode, commandsNode with
        | (:? JsonObject as start), (:? JsonObject as startCommand), (:? JsonArray as commands) when
            not (document.ContainsKey "cpuPolicy")
            && not (startCommand.ContainsKey "cpuPolicy")
            ->
            let seedNode: JsonNode | null = start["seed"]

            match seedNode with
            | :? JsonObject as seedValue ->
                match
                    uint64Member "value" seedValue,
                    stringMember "clientCommandId" startCommand,
                    stringMember "deckId" startCommand
                with
                | Ok seed, Ok commandIdValue, Ok deckIdValue ->
                    match Guid.TryParse commandIdValue, Guid.TryParse deckIdValue with
                    | (true, commandId), (true, deckId) ->
                        try
                            let gameStart =
                                start.Deserialize<Blokemon.Game.MatchStartRequest>(
                                    MatchJson.Options
                                )

                            match gameStart with
                            | null -> corrupt
                            | gameStart ->
                                let cpuCommands =
                                    commands
                                    |> Seq.filter (fun command ->
                                        match command with
                                        | :? JsonObject as value ->
                                            match value["actor"] with
                                            | :? JsonObject as actor ->
                                                match stringMember "value" actor with
                                                | Ok value ->
                                                    String.Equals(
                                                        value,
                                                        cpuPlayerId,
                                                        StringComparison.Ordinal
                                                    )
                                                | Error _ -> false
                                            | _ -> false
                                        | _ -> false)
                                    |> Seq.length

                                let initial = MatchCpuPolicy.legacy seed 0UL
                                let current = MatchCpuPolicy.legacy seed (uint64 cpuCommands)

                                let request =
                                    Blokemon.App.Contracts.StartMatchRequest(
                                        commandId,
                                        deckId,
                                        CpuDifficultyView.Normal
                                    )

                                startCommand["fingerprint"] <-
                                    JsonValue.Create(startFingerprint request initial)

                                startCommand["startRequestFingerprint"] <-
                                    JsonValue.Create(gameStartFingerprint gameStart initial)

                                startCommand["cpuPolicy"] <- policyNode initial
                                document["cpuPolicy"] <- policyNode current
                                Ok()
                        with
                        | :? JsonException -> corrupt
                        | :? NotSupportedException -> corrupt
                        | :? InvalidOperationException -> corrupt
                    | _ -> corrupt
                | Error reason, _, _
                | _, Error reason, _
                | _, _, Error reason -> Error reason
            | _ -> corrupt
        | _ -> corrupt

    let private matchSchemaOneTransition source =
        let target = schemaTwo source.Authority

        { Identity = identity "match" "schema" source target
          Source = source
          Target = target
          RebindsAuthority = false
          Apply =
            fun document ->
                match migrateCommands document with
                | Ok() ->
                    document["schemaVersion"] <- JsonValue.Create target.Schema
                    Ok()
                | Error reason -> Error reason }

    let private matchSchemaTwoTransition source =
        let target = current source.Authority

        { Identity = identity "match" "cpu-policy" source target
          Source = source
          Target = target
          RebindsAuthority = false
          Apply =
            fun document ->
                match migrateCpuPolicy document with
                | Ok() ->
                    document["schemaVersion"] <- JsonValue.Create target.Schema
                    Ok()
                | Error reason -> Error reason }

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

    let private migrateHistoryCommands source target (history: JsonObject) =
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
                            match migrateCommands document with
                            | Ok() -> document["schemaVersion"] <- JsonValue.Create target.Schema
                            | Error reason -> failure <- Some reason
                        | Ok _ -> failure <- Some MatchRecoveryReason.Corrupt
                        | Error reason -> failure <- Some reason
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None ->
                history["schemaVersion"] <- JsonValue.Create target.Schema
                Ok()

    let private migrateHistoryCpuPolicy source target (history: JsonObject) =
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
                            match migrateCpuPolicy document with
                            | Ok() -> document["schemaVersion"] <- JsonValue.Create target.Schema
                            | Error reason -> failure <- Some reason
                        | Ok _ -> failure <- Some MatchRecoveryReason.Corrupt
                        | Error reason -> failure <- Some reason
                    | _ -> failure <- Some MatchRecoveryReason.Corrupt

            match failure with
            | Some reason -> Error reason
            | None ->
                history["schemaVersion"] <- JsonValue.Create target.Schema
                Ok()

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

    let private historySchemaOneTransition source =
        let target = schemaTwo source.Authority

        { Identity = identity "match-history" "schema" source target
          Source = source
          Target = target
          RebindsAuthority = false
          Apply = migrateHistoryCommands source target }

    let private historySchemaTwoTransition source =
        let target = current source.Authority

        { Identity = identity "match-history" "cpu-policy" source target
          Source = source
          Target = target
          RebindsAuthority = false
          Apply = migrateHistoryCpuPolicy source target }

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
        (authority: string)
        (json: string)
        : MatchMigrationPreparation<'Document> =
        match parseRoot json with
        | Error reason -> MatchMigrationPreparation.RecoveryRequired reason
        | Ok root ->
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
                        let candidateJson = JsonSerializer.Serialize(document, MatchJson.Options)

                        MatchMigrationPreparation.Candidate
                            { Document = document
                              Json = candidateJson
                              Identity = applied |> List.map _.Identity |> String.concat "+"
                              ReboundAuthority = applied |> List.exists _.RebindsAuthority }

    let prepareMatch authority json =
        let registry =
            ordered
                matchSchemaOneTransition
                matchSchemaTwoTransition
                matchAuthorityTransition
                authority

        prepare registry MatchDocumentNormalization.matchDocument authority json

    let prepareHistory authority json =
        let registry =
            ordered
                historySchemaOneTransition
                historySchemaTwoTransition
                historyAuthorityTransition
                authority

        prepare registry MatchDocumentNormalization.historyDocument authority json
