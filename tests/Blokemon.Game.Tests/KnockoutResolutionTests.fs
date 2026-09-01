namespace Blokemon.Game.Tests

open Blokemon.Game
open FsUnit
open TUnit.Core

/// One resolution can take more than one bloke off the table, and the last bloke a player has can
/// leave with nobody on the booth to replace it. Both are decided inside resolveSendHome and the
/// win resolution that closes it.
type KnockoutResolutionTests() =

    /// Far past any printed staying power, so the resolution treats the card as knocked out
    /// whatever the authority says that card can take.
    let Lethal = 999

    let LeftTheOche (state: MatchState) (card: string) =
        (state.Card(CardInstanceId card)).Zone <> CardZone.Oche

    /// The blokes the resolution took off the table, in the order it took them.
    let SentHome (events: MatchEvent seq) =
        events
        |> Seq.filter (fun event -> event.Kind = MatchEventKind.BlokeSentHome)
        |> Seq.map (fun event -> event.SourceCard.Value)
        |> Seq.toList

    /// What the resolution decided, in the order it decided it, ignoring everything the rest of
    /// the command went on to do.
    let Decisions (events: MatchEvent seq) =
        events
        |> Seq.map (fun event -> event.Kind)
        |> Seq.filter (fun kind ->
            kind = MatchEventKind.BlokeSentHome
            || kind = MatchEventKind.SuddenDeathStarted
            || kind = MatchEventKind.MatchWon)
        |> Seq.toList

    [<Test>]
    /// Advanced Rulebook v1, p. 16: every Knocked Out Pokémon awards exactly one Prize.
    member _.``a Knock Out should award exactly one Prize``() =
        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-003"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                29UL

        let defender =
            { state.Card(CardInstanceId "defender") with
                Damage = Lethal }

        let barChits =
            [ for index in 0..5 ->
                  MatchScenario.PlainCard
                      $"bar-chit-{index}"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      index ]

        let state = MatchScenario.WithCards state (defender :: barChits)

        let applied, events =
            MatchScenario.AppliedWith(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        let award =
            events
            |> Seq.find (fun matchEvent ->
                matchEvent.Kind = MatchEventKind.BarChitsTaken
                && matchEvent.SourceCard = ValueSome defender.Id)

        (award.Amount, (applied.Player MatchScenario.FirstPlayer).BarChitsRemaining)
        |> should equal (1, 5)

    [<Test>]
    member _.``knocking out both actives at once should send them home in owner order and tie rather than name a winner``
        ()
        =
        let engine = MatchScenario.Engine()

        let original =
            MatchScenario.BattleState "BLK-001" "BLK-003" [ "VIM-BLAZED"; "VIM-SOBER" ] 29UL

        let state =
            MatchScenario.WithCards
                original
                [ { original.Card(CardInstanceId "attacker") with
                      Damage = Lethal }
                  { original.Card(CardInstanceId "defender") with
                      Damage = Lethal } ]
            |> MatchScenario.WithRestartableDecks

        let applied, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        // Owner then identity: the attacker's owner sorts first, so the attacker leaves before the
        // bloke the attack was aimed at. Neither player is left with anything on the table, so both
        // win by the same method at the same moment, which is a tie and not a win.
        SentHome events
        |> List.truncate 2
        |> should equal [ CardInstanceId "attacker"; CardInstanceId "defender" ]

        Decisions events
        |> List.truncate 3
        |> should
            equal
            [ MatchEventKind.BlokeSentHome
              MatchEventKind.BlokeSentHome
              MatchEventKind.SuddenDeathStarted ]

        // Advanced Rulebook v1, p. 24: a tied game is followed by a completely new
        // one-Prize game, so the Knocked Out state does not survive the resolution.
        applied.Phase |> should equal MatchPhase.OpeningPlacement
        applied.Winner.IsNone |> should be True
        (applied.Card(CardInstanceId "attacker")).Damage |> should equal 0
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 0
        applied.SuddenDeathCount |> should be (greaterThan state.SuddenDeathCount)

    [<Test>]
    member _.``a retaliating defender should take the attacker home in the same resolution``() =
        let engine = MatchScenario.Engine()

        let original =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-110"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                41UL

        // A retaliatory double Knock Out is a tied game, so provide complete decks for the
        // fresh-game setup that follows it (Advanced Rulebook v1, p. 24).
        let state = MatchScenario.WithRestartableDecks original

        let applied, events =
            MatchScenario.AppliedWith(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B02")
            )

        // The retaliation names the attacker only once the defender it belongs to has already been
        // worked through, so the attacker leaves because the candidates grow while they are being
        // resolved rather than because it was one of them to begin with.
        LeftTheOche applied "defender" |> should be True
        LeftTheOche applied "attacker" |> should be True

        Decisions events
        |> List.truncate 2
        |> should equal [ MatchEventKind.BlokeSentHome; MatchEventKind.BlokeSentHome ]

    [<Test>]
    member _.``sending home the only bloke a player has left should win the match for the other player``
        ()
        =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-003" [ "VIM-BLAZED"; "VIM-SOBER" ] 31UL

        let state =
            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "defender") with
                      Damage = Lethal } ]

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        // Nothing on the booth to promote, so there is no replacement to wait for and the match is
        // decided instead.
        applied.ReplacementPlayer.IsNone |> should be True
        applied.Winner |> should equal (ValueSome MatchScenario.FirstPlayer)
        applied.Phase |> should equal MatchPhase.Complete

    [<Test>]
    member _.``sending home an active with a bloke on the booth should wait for the replacement instead``
        ()
        =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-003" [ "VIM-BLAZED"; "VIM-SOBER" ] 37UL

        let booth =
            MatchScenario.PlainCard "booth" "BLK-004" MatchScenario.SecondPlayer CardZone.Booth 0

        let state =
            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "defender") with
                      Damage = Lethal }
                  booth ]

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        applied.Winner.IsNone |> should be True
        applied.Phase |> should equal MatchPhase.AwaitingReplacement
        applied.ReplacementPlayer |> should equal (ValueSome MatchScenario.SecondPlayer)
