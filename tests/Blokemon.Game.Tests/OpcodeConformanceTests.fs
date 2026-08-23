namespace Blokemon.Game.Tests

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private OpcodeConformanceScenarios =

    let applyAttack (engine: MatchEngine) (state: MatchState) (effect: string) =
        MatchScenario.AppliedWith(engine.Apply(state, MatchScenario.AttackCommand state effect))

    let attackDamage
        (engine: MatchEngine)
        (attacker: string)
        (defender: string)
        (vim: string list)
        (seed: uint64)
        (effect: string)
        =
        let state = MatchScenario.BattleState attacker defender vim seed
        let applied, _ = applyAttack engine state effect
        (applied.Card(CardInstanceId "defender")).Damage

    let mutateCollectible
        (cardId: string)
        (mutation: BlokemonCollectible -> BlokemonCollectible)
        (authority: BlokemonRuntimeManifest)
        =
        { authority with
            Collectibles =
                authority.Collectibles
                |> Array.map (fun card -> if card.Id = cardId then mutation card else card) }

    let mutateAttack
        (cardId: string)
        (effectId: string)
        (mutation: BlokemonAttack -> BlokemonAttack)
        (authority: BlokemonRuntimeManifest)
        =
        mutateCollectible
            cardId
            (fun card ->
                { card with
                    Attacks =
                        card.Attacks
                        |> Array.map (fun attack ->
                            if attack.MechanicalId = effectId then
                                mutation attack
                            else
                                attack) })
            authority

    let mutatePartyTrick
        (cardId: string)
        (effectId: string)
        (mutation: BlokemonPartyTrick -> BlokemonPartyTrick)
        (authority: BlokemonRuntimeManifest)
        =
        mutateCollectible
            cardId
            (fun card ->
                { card with
                    PartyTricks =
                        card.PartyTricks
                        |> Array.map (fun trick ->
                            if trick.MechanicalId = effectId then
                                mutation trick
                            else
                                trick) })
            authority

    let rec removeOpcode (opcode: BlokemonOpcode) (program: BlokemonEffectInstruction array) =
        program
        |> Array.choose (fun instruction ->
            if instruction.Opcode = opcode then
                None
            else
                Some
                    { instruction with
                        Then = removeOpcode opcode instruction.Then
                        Otherwise = removeOpcode opcode instruction.Otherwise })

    let clearRoughStateShouldHealClearAndReportEvents () =
        let roughStates =
            ImmutableArray.Create(
                MatchScenario.RoughState BlokemonRoughState.DodgyPint 2,
                MatchScenario.RoughState BlokemonRoughState.Singed 2
            )

        let state =
            MatchScenario.BattleStateWith
                "BLK-079"
                "BLK-006"
                [ "VIM-GEEKED" ]
                1009UL
                roughStates
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let state =
            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "attacker") with
                      Damage = 50 } ]

        let applied, events = applyAttack (MatchScenario.Engine()) state "BLK-079-B01"
        let attacker = applied.Card(CardInstanceId "attacker")

        attacker.Damage |> should equal 20
        attacker.RoughStates |> Seq.toList |> should be Empty

        events
        |> Seq.filter (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.DamageHealed
            || matchEvent.Kind = MatchEventKind.RoughStateCleared)
        |> Seq.map (fun matchEvent ->
            matchEvent.Kind,
            matchEvent.SourceCard,
            matchEvent.TargetCards |> Seq.toList,
            matchEvent.RoughState,
            matchEvent.Amount)
        |> Seq.toList
        |> should
            equal
            [ MatchEventKind.DamageHealed,
              ValueSome(CardInstanceId "attacker"),
              [ CardInstanceId "attacker" ],
              ValueNone,
              30
              MatchEventKind.RoughStateCleared,
              ValueNone,
              [ CardInstanceId "attacker" ],
              ValueSome BlokemonRoughState.DodgyPint,
              0
              MatchEventKind.RoughStateCleared,
              ValueNone,
              [ CardInstanceId "attacker" ],
              ValueSome BlokemonRoughState.Singed,
              0 ]

    let ignoreStubbornStreakShouldBypassAnAuthoritativeControl () =
        let engine = MatchScenario.Engine()

        let control =
            attackDamage
                engine
                "BLK-076"
                "BLK-065"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                1013UL
                "BLK-076-B01"

        let ignored =
            attackDamage
                engine
                "BLK-076"
                "BLK-065"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                1013UL
                "BLK-076-B02"

        control |> should equal 20
        ignored |> should equal 180

    let ignoreSoftSpotAndStubbornStreakShouldBypassBothModifiers () =
        let engine = MatchScenario.Engine()

        let authoritativeControl =
            attackDamage engine "BLK-134" "BLK-006" [ "VIM-SOBER" ] 1019UL "BLK-134-B01"

        let ignoredSoftSpot =
            attackDamage
                engine
                "BLK-120"
                "BLK-006"
                [ "VIM-SOBER"; "VIM-GEEKED" ]
                1019UL
                "BLK-120-B01"

        authoritativeControl |> should equal 60
        ignoredSoftSpot |> should equal 30

        let authorityWithWaterStubborn =
            MatchScenario.Authority
            |> mutateCollectible "BLK-148" (fun card ->
                { card with
                    StubbornStreaks =
                        [| { MechanicalType = BlokemonMechanicalType.Water
                             Modifier = "-30" } |] })

        let copiedEngine = MatchEngine authorityWithWaterStubborn

        let copiedControl =
            attackDamage copiedEngine "BLK-134" "BLK-148" [ "VIM-SOBER" ] 1021UL "BLK-134-B01"

        let ignoredStubborn =
            attackDamage
                copiedEngine
                "BLK-120"
                "BLK-148"
                [ "VIM-SOBER"; "VIM-GEEKED" ]
                1021UL
                "BLK-120-B01"

        copiedControl |> should equal 0
        ignoredStubborn |> should equal 30

    let repeatUntilBlankSideShouldExposeItsExactSeededTosses () =
        let observe seed =
            let state = MatchScenario.BattleState "BLK-075" "BLK-006" [ "VIM-LAIRY" ] seed

            let applied, events = applyAttack (MatchScenario.Engine()) state "BLK-075-B01"

            let tosses =
                events
                |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.BeerMatTossed)
                |> Seq.map (fun matchEvent ->
                    matchEvent.SourceCard, matchEvent.Effect, matchEvent.BadgeSide)
                |> Seq.toList

            (applied.Card(CardInstanceId "defender")).Damage, tosses

        observe 2UL
        |> should
            equal
            (0,
             [ ValueSome(CardInstanceId "attacker"),
               ValueSome(EffectId "BLK-075-B01"),
               ValueSome false ])

        observe 1UL
        |> should
            equal
            (80,
             [ ValueSome(CardInstanceId "attacker"),
               ValueSome(EffectId "BLK-075-B01"),
               ValueSome true
               ValueSome(CardInstanceId "attacker"),
               ValueSome(EffectId "BLK-075-B01"),
               ValueSome true
               ValueSome(CardInstanceId "attacker"),
               ValueSome(EffectId "BLK-075-B01"),
               ValueSome false ])

    let restrictLocalShouldApplyOnlyFromTheOche () =
        let table restrictionAtOche =
            let state = MatchScenario.BattleState "BLK-001" "BLK-001" [] 1031UL

            let restriction =
                MatchScenario.PlainCard
                    "restriction"
                    "KIT-002"
                    MatchScenario.FirstPlayer
                    (if restrictionAtOche then CardZone.Oche else CardZone.Booth)
                    -1

            let firstBloke =
                { state.Card(CardInstanceId "attacker") with
                    Zone = if restrictionAtOche then CardZone.Booth else CardZone.Oche }

            let local =
                MatchScenario.PlainCard
                    "local"
                    "KIT-006"
                    MatchScenario.SecondPlayer
                    CardZone.Mitt
                    -1

            { MatchScenario.WithCards state [ firstBloke; restriction; local ] with
                ActivePlayer = MatchScenario.SecondPlayer
                RoundUsage = RoundUsage.Empty MatchScenario.SecondPlayer }

        let restricted = table true
        let control = table false

        let command =
            MatchScenario.Command
                restricted
                "play-local"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.PlayKit(CardInstanceId "local", ValueNone))

        let rejectedState, rejection =
            MatchScenario.Rejected((MatchScenario.Engine()).Apply(restricted, command))

        rejection.Code |> should equal CommandRejectionCode.EffectUnavailable
        rejectedState |> should equal restricted

        let applied =
            MatchScenario.Applied((MatchScenario.Engine()).Apply(control, command))

        (applied.Card(CardInstanceId "local")).Zone |> should equal CardZone.Local

    let restrictTaxiShouldBeTheOnlyDifferenceForTheSameTaxi () =
        let authorityWithoutRestriction =
            MatchScenario.Authority
            |> mutateAttack "BLK-024" "BLK-024-B01" (fun attack ->
                { attack with
                    Program = removeOpcode BlokemonOpcode.RestrictTaxi attack.Program })

        let table () =
            let state =
                MatchScenario.BattleState "BLK-024" "BLK-006" [ "VIM-DODGY"; "VIM-DODGY" ] 1033UL

            let replacement =
                MatchScenario.PlainCard
                    "replacement"
                    "BLK-004"
                    MatchScenario.SecondPlayer
                    CardZone.Booth
                    -1

            MatchScenario.WithCards state [ replacement ]

        let restrictedEngine = MatchScenario.Engine()
        let controlEngine = MatchEngine authorityWithoutRestriction
        let state = table ()

        let restricted, _ = applyAttack restrictedEngine state "BLK-024-B01"
        let control, _ = applyAttack controlEngine state "BLK-024-B01"

        let command =
            MatchScenario.Command
                restricted
                "same-taxi"
                MatchScenario.SecondPlayer
                ImmutableArray<_>.Empty
                (MatchAction.Taxi(CardInstanceId "replacement", ImmutableArray<_>.Empty))

        MatchScenario.RejectionCode(restrictedEngine.Apply(restricted, command))
        |> should equal CommandRejectionCode.EffectUnavailable

        let applied = MatchScenario.Applied(controlEngine.Apply(control, command))
        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Booth
        (applied.Card(CardInstanceId "replacement")).Zone |> should equal CardZone.Oche

    let triggeredPartyTrickShouldBeRequiredByPublicStartValidation () =
        let authorityWithoutMarker =
            MatchScenario.Authority
            |> mutatePartyTrick "BLK-107" "BLK-107-T01" (fun trick ->
                { trick with
                    Program = removeOpcode BlokemonOpcode.TriggeredPartyTrick trick.Program })

        let request = MatchScenario.StartRequest()
        MatchScenario.Started((MatchScenario.Engine()).Start request) |> ignore

        (MatchEngine authorityWithoutMarker).Start request
        |> MatchScenario.StartRejected
        |> Seq.map (fun issue -> issue.Code)
        |> Seq.toList
        |> should equal [ DeckIssueCode.AuthorityInvalid ]

type OpcodeConformanceTests() =

    [<Test>]
    member _.``every long-tail opcode should have an observable MatchEngine result``() =
        clearRoughStateShouldHealClearAndReportEvents ()
        ignoreStubbornStreakShouldBypassAnAuthoritativeControl ()
        ignoreSoftSpotAndStubbornStreakShouldBypassBothModifiers ()
        repeatUntilBlankSideShouldExposeItsExactSeededTosses ()
        restrictLocalShouldApplyOnlyFromTheOche ()
        restrictTaxiShouldBeTheOnlyDifferenceForTheSameTaxi ()
        triggeredPartyTrickShouldBeRequiredByPublicStartValidation ()
