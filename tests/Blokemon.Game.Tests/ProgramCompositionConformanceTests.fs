namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open System.Security.Cryptography
open System.Text.Json
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core
open ConformanceCensus

[<AutoOpen>]
module internal ProgramCompositionConformanceFixtures =

    let private withFirstPlayerRounds rounds (state: MatchState) =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = MatchScenario.FirstPlayer then
                            { player with RoundsStarted = rounds }
                        else
                            player)
                ) }

    let private executionBytes (execution: Execution) =
        Array.concat
            [ JsonSerializer.SerializeToUtf8Bytes(execution.State, MatchJson.Options)
              JsonSerializer.SerializeToUtf8Bytes(execution.Events, MatchJson.Options) ]

    let private activatedState (row: ProgramRow) =
        let state = richBattleState row.OwnerId

        match row.MechanicalId with
        | "BLK-053-T01" ->
            let mate =
                { state.Card(CardInstanceId "own-stack-kit") with
                    MechanicalId = MechanicalCardId "KIT-010" }

            MatchScenario.WithCards state [ mate ]
        | "BLK-132-T01" -> withFirstPlayerRounds 1 state
        | "BLK-151-T01" ->
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card ->
                            card.Id <> CardInstanceId "own-mitt-bloke"
                            && card.Id <> CardInstanceId "own-mitt-kit")
                    ) }
        | _ -> state

    let private compositionBytes (row: ProgramRow) =
        if declarativeKitStructuralProgramIds.Contains row.MechanicalId then
            raise (
                InvalidOperationException(
                    $"Declarative program row {row.MechanicalId} has no MatchEngine composition route."
                )
            )
        else
            match row.Kind, row.Trigger with
            | ProgramKind.Attack, _ ->
                executeAttack (richBattleState row.OwnerId) row.MechanicalId |> executionBytes
            | ProgramKind.HouseRule, _ ->
                executeAction (kitState row.OwnerId) (playsKit (CardInstanceId "kit-under-test"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.Activated ->
                executeAction (activatedState row) (usesPartyTrick row.MechanicalId)
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.Continuous ->
                executeAction
                    (continuousState row.OwnerId)
                    (playsBloke (CardInstanceId "own-mitt-bloke"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.OnPromotionFromMitt ->
                let promotion =
                    MatchScenario.Authority.Collectibles
                    |> Array.find (fun card -> card.Id = row.OwnerId)

                executeAction
                    (promotionState promotion)
                    (promotes (CardInstanceId "promotion") (CardInstanceId "attacker"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome trigger ->
                observeReactiveTrigger (MatchScenario.Engine()) trigger |> BitConverter.GetBytes
            | ProgramKind.PartyTrick, ValueNone ->
                failwith $"Party Trick {row.MechanicalId} had no trigger."

    let compositionHash row =
        row
        |> compositionBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let currentCompositionHashes () =
        executableNontrivialPrograms
        |> Array.map (fun (row, _) ->
            { MechanicalId = row.MechanicalId
              Sha256 = compositionHash row })

type ProgramCompositionConformanceTests() =

    [<Test>]
    member _.``every executable recursive nontrivial program should preserve its MatchEngine semantic composition``
        ()
        =
        let expected = (ConformanceFixture.load ()).CompositionHashes

        let executableIds =
            executableNontrivialPrograms
            |> Seq.map (fun (row, _) -> row.MechanicalId)
            |> Set.ofSeq

        expected.Length |> should equal 178
        expected |> Seq.map _.MechanicalId |> Set.ofSeq |> should equal executableIds
        currentCompositionHashes () |> should equal expected
