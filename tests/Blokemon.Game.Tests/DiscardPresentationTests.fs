namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open Blokemon.App
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Game
open FsUnit
open TUnit.Core

type DiscardPresentationTests() =

    [<Test>]
    [<Arguments(true)>]
    [<Arguments(false)>]
    member _.``an attack cost should identify each discarded attachment before its damage cue``
        (local: bool)
        =
        let initial =
            MatchScenario.BattleState
                "BLK-006"
                "BLK-006"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED"; "VIM-BLAZED" ]
                1601UL

        let engine = MatchScenario.Engine()

        let burn =
            engine.GetLegalActions(initial, MatchScenario.FirstPlayer)
            |> Seq.find (fun action ->
                action.Command.Action = MatchAction.UsePartyTrick(
                    CardInstanceId "attacker",
                    EffectId "BLK-006-T01"
                ))

        let state = MatchScenario.Applied(engine.Apply(initial, burn.Command))

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.find (fun action ->
                action.Command.Action = MatchAction.Attack(
                    CardInstanceId "attacker",
                    EffectId "BLK-006-B01"
                ))

        let requirement = action.ChoiceRequirements |> Seq.exactlyOne

        let outcome =
            engine.Apply(
                state,
                { action.Command with
                    Choices =
                        ImmutableArray.Create(
                            EffectChoice.Cards(
                                requirement.Id,
                                ImmutableArray.Create(
                                    CardInstanceId "vim-0",
                                    CardInstanceId "vim-1"
                                )
                            )
                        ) }
            )

        let settled, events =
            match outcome with
            | CommandOutcome.Applied(settled, events) -> settled, events
            | CommandOutcome.Rejected(_, error) -> failwithf "Attack was rejected: %A" error

        let catalogue =
            BlokemonCatalogue.FromBootstrapJson(
                IO.File.ReadAllText(
                    IO.Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
                )
            )

        let viewer =
            if local then
                MatchScenario.FirstPlayer
            else
                MatchScenario.SecondPlayer

        let cues =
            events
            |> Seq.map (fun event ->
                MatchCueProjection.cue catalogue engine settled viewer "Player" event events)
            |> Seq.choose Option.ofObj
            |> Seq.toArray

        let discarded =
            cues |> Array.filter (fun cue -> cue.Kind = MatchAnimationKindView.Discard)

        discarded
        |> Array.map _.SourceCardInstanceId
        |> should equal [| "vim-0"; "vim-1" |]

        discarded
        |> Array.map _.ActorIsLocalPlayer
        |> should equal [| Nullable local; Nullable local |]

        let damage =
            cues |> Array.find (fun cue -> cue.Kind = MatchAnimationKindView.Damage)

        discarded
        |> Array.forall (fun cue -> cue.Sequence < damage.Sequence)
        |> should be True

        let projected =
            MatchCardProjection.cardInstance
                catalogue
                state
                viewer
                "Player"
                (CardInstanceId "attacker")

        projected.AttachedEnergy
        |> Array.map _.Id
        |> should equal [| "vim-0"; "vim-1"; "vim-2"; "vim-3" |]

        let remaining =
            MatchCardProjection.cardInstance
                catalogue
                settled
                viewer
                "Player"
                (CardInstanceId "attacker")

        remaining.AttachedEnergy
        |> Array.map _.Id
        |> should equal [| "vim-2"; "vim-3" |]
