namespace Blokemon.Game.Tests

open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

/// A fare nobody can pay used to delete the retreat rather than refuse it: the generator proposed
/// a taxi carrying whatever vim happened to be attached, the handler refused it, and the engine
/// dropped it on the floor. The affordance did not grey out, it ceased to exist. What is posed
/// here is the reported table - Howard Marks at the Oche with a fare of four and nothing attached
/// to pay it with - from both sides of it.
type TaxiFareTests() =

    let Howard = "BLK-003" // fare 4
    let Weedman = "BLK-001" // fare 2
    let Nobody = "BLK-004" // whoever is waiting on the bench

    let First = MatchScenario.FirstPlayer
    let Second = MatchScenario.SecondPlayer

    let Bench (owner: PlayerId) =
        MatchScenario.PlainCard $"bench:{owner.Value}" Nobody owner CardZone.Booth -1

    /// The first player at the Oche with the given vim attached and somebody to retreat to.
    let Table (attached: string list) =
        let state = MatchScenario.BattleState Howard Weedman attached 93UL
        MatchScenario.WithCards state [ Bench First ]

    /// The same table from the other side: it is the computer's turn, and it is the computer's
    /// own Active that cannot pay its fare.
    let CpuTable () =
        let state = MatchScenario.BattleState Weedman Howard [] 97UL

        { MatchScenario.WithCards state [ Bench Second ] with
            ActivePlayer = Second
            RoundUsage = RoundUsage.Empty Second }

    let TaxiOffered (state: MatchState) (actor: PlayerId) =
        MatchScenario.Engine().GetLegalActions(state, actor)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.Taxi)
        |> Seq.exactlyOne

    /// The computer's turn taken the way the match takes it: choose, apply, repeat until it stops
    /// choosing. Every choice it makes has to be one the engine accepts, because a rejected choice
    /// is where a live match settles with an invalid move rather than a finished turn.
    let PlayOutCpuTurn (state: MatchState) =
        let engine = MatchScenario.Engine()
        let cpu = DeterministicCpu()
        let mutable current = state
        let mutable settled = false
        let mutable steps = 0

        while not settled && steps < 32 do
            match cpu.Choose(engine, current, Second) with
            | CpuDecision.Selected action ->
                current <- MatchScenario.Applied(engine.Apply(current, action.Command))
                steps <- steps + 1
            | CpuDecision.NoLegalAction -> settled <- true

        settled |> should be True
        current

    [<Test>]
    member _.``a taxi the fare outruns should still be offered, naming the fare it needs``() =
        // The reported position: four to pay and nothing attached to pay it with. The move is
        // offered so the table can show it, and it is refused so nobody can take it.
        let state = Table []
        let taxi = TaxiOffered state First

        taxi.Affordability |> should equal (ActionAffordability.ShortOfTaxiFare 4)

        MatchScenario.RejectionCode(MatchScenario.Engine().Apply(state, taxi.Command))
        |> should equal CommandRejectionCode.InvalidTaxiFare

    [<Test>]
    member _.``a taxi the attached vim covers should stay payable and should bring the bench bloke on``
        ()
        =
        let state = Table [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
        let taxi = TaxiOffered state First

        taxi.Affordability |> should equal ActionAffordability.Payable

        let retreated =
            MatchScenario.Applied(MatchScenario.Engine().Apply(state, taxi.Command))

        (retreated.Card (Bench First).Id).Zone |> should equal CardZone.Oche

    [<Test>]
    member _.``the computer should finish its turn although its own active cannot pay the fare``() =
        // The taxi outranks ending the round in the computer's policy, so an unpayable one it
        // could still see would be preferred to every move behind it and rejected on the spot.
        let state = CpuTable()

        TaxiOffered state Second
        |> _.Affordability
        |> should not' (equal ActionAffordability.Payable)

        let played = PlayOutCpuTurn state

        played.ActivePlayer |> should equal First
