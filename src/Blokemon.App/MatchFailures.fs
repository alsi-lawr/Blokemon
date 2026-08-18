namespace Blokemon.App

open Blokemon.App.Contracts
open Blokemon.Game

/// The tier's fixed document keys and every typed failure a match operation can return.
module internal MatchFailures =

    let matchKey = "match"
    let matchHistoryKey = "match-history"
    let matchSchemaVersion = 1
    let matchHistorySchemaVersion = 1
    let maximumCpuCommandsPerRequest = 256
    let cpuPlayerId = "cpu:local"
    let cpuName = "The Regular"

    let cpuPlayer = PlayerId cpuPlayerId

    let failed code message : MatchServiceResult =
        { View = null
          Error = ApiError(code, message)
          Presentation = null }

    let stateConflict () =
        failed "state.conflict" "The saved battle changed. Select the action again."

    let invalidChoice message : CommandMaterialization =
        { Command = null
          Error = ApiError("match.choice_invalid", message) }

    let requiredChoice () : CommandMaterialization =
        { Command = null
          Error = ApiError("match.choice_required", "Make each required choice.") }

    let invalidDocument code message : MatchLoad =
        { Match = null
          Error = ApiError(code, message) }

    let invalidReplayError () =
        ApiError("match.replay_invalid", "The saved battle is damaged. No data changed.")

    let invalidReplay () : MatchLoad =
        { Match = null
          Error = invalidReplayError () }

    let historyCorrupt () =
        ApiError("match.history_corrupt", "The saved battle history is damaged. No data changed.")

    let historyVersion () =
        ApiError(
            "match.history_version",
            "The saved battle history uses an unsupported version. No data changed."
        )

    let historyAuthorityChanged () =
        ApiError(
            "match.authority_changed",
            "The card rules changed after these battles were saved. No data changed."
        )


    let rejection (code: CommandRejectionCode) =
        match code with
        | CommandRejectionCode.StaleRevision ->
            ApiError("match.stale", "The battle changed. Choose the move again.")
        | CommandRejectionCode.ChoiceRequired ->
            ApiError("match.choice_required", "Make each required choice.")
        | CommandRejectionCode.InvalidChoice ->
            ApiError("match.choice_invalid", "This choice is not available.")
        | CommandRejectionCode.IllegalOpening ->
            ApiError("match.choice_invalid", "The selected opening placement is not legal.")
        | CommandRejectionCode.WrongChooser ->
            ApiError("match.choice_wrong_chooser", "The opponent must make this choice.")
        | CommandRejectionCode.DuplicateCommand ->
            ApiError("match.command_conflict", "This move was already used.")
        | _ -> ApiError("match.action_illegal", "You cannot use that move now.")
