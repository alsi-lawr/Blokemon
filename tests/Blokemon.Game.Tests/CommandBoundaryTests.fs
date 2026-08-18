namespace Blokemon.Game.Tests

open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private CommandBoundaryFixtures =

    let openingCommand () =
        let engine = MatchScenario.Engine()
        let state = MatchScenario.Started(engine.Start(MatchScenario.StartRequest()))

        let command =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action -> action.Kind = LegalActionKind.ChooseOpening)
            |> fun action -> action.Command

        engine, state, command

type CommandBoundaryTests() =

    [<Test>]
    member _.``replaying an accepted command should be refused without changing the state``() =
        let engine, state, command = openingCommand ()
        let accepted = MatchScenario.Applied(engine.Apply(state, command))

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(accepted, command))

        rejection.Code |> should equal CommandRejectionCode.DuplicateCommand
        obj.ReferenceEquals(rejectedState, accepted) |> should be True
        rejectedState |> should equal accepted

    [<Test>]
    member _.``reusing a command identity at the new revision should be refused without changing the state``
        ()
        =
        let engine, state, command = openingCommand ()
        let accepted = MatchScenario.Applied(engine.Apply(state, command))

        let repeated =
            { command with
                ExpectedRevision = accepted.Revision }

        let rejectedState, rejection =
            MatchScenario.Rejected(engine.Apply(accepted, repeated))

        rejection.Code |> should equal CommandRejectionCode.DuplicateCommand
        rejectedState |> should equal accepted

    [<Test>]
    member _.``a fresh command against a stale revision should be refused without changing the state``
        ()
        =
        let engine, state, command = openingCommand ()
        let accepted = MatchScenario.Applied(engine.Apply(state, command))

        let stale =
            { command with
                Id = CommandId "unique-stale-command" }

        let rejectedState, rejection = MatchScenario.Rejected(engine.Apply(accepted, stale))

        rejection.Code |> should equal CommandRejectionCode.StaleRevision
        rejectedState |> should equal accepted
