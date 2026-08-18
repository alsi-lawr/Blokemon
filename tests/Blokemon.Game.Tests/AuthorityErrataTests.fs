namespace Blokemon.Game.Tests

open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private AuthorityErrataFixtures =

    let applyAttack (engine: MatchEngine) (state: MatchState) (actor: PlayerId) (effect: string) =
        let action =
            engine.GetLegalActions(state, actor)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Attack
                && (match candidate.Command.Action with
                    | MatchAction.Attack(_, attackId) -> attackId = EffectId effect
                    | _ -> false))
            |> Seq.exactlyOne

        MatchScenario.AppliedWith(engine.Apply(state, action.Command))

    let attachToDefender (state: MatchState) (vimIds: string seq) =
        let attachments =
            vimIds
            |> Seq.mapi (fun index mechanicalId ->
                MatchScenario.AttachedCard
                    $"other-vim-{index}"
                    mechanicalId
                    MatchScenario.SecondPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "defender"))
            |> Seq.toList

        let defender = state.Card(CardInstanceId "defender")

        let defender =
            { defender with
                Attachments =
                    FrozenList<CardInstanceId>
                        .Create(
                            Seq.append defender.Attachments (attachments |> Seq.map (fun c -> c.Id))
                        ) }

        MatchScenario.WithCards state (defender :: attachments)

    let namedMateState (attacker: string) (mate: string) (seed: uint64) =
        let state = MatchScenario.BattleState attacker "BLK-150" [ "VIM-BLAZED" ] seed

        { state with
            RoundUsage =
                { state.RoundUsage with
                    MatesPlayed = 1
                    KitsPlayed = FrozenList<MechanicalCardId>.Create(MechanicalCardId mate) } }

    let seedForBadge () =
        let rec search (seed: uint64) =
            if seed >= 1000UL then failwith "No badge-side seed found."
            elif BlokemonSeededRandom(seed).NextInt 2 = 1 then seed
            else search (seed + 1UL)

        search 0UL

    let seedForTwoTosses (allBadges: bool) =
        let rec search (seed: uint64) =
            if seed >= 1000UL then
                failwith "No matching two-toss seed found."
            else
                let random = BlokemonSeededRandom seed
                let result = random.NextInt 2 = 1 && random.NextInt 2 = 1
                if result = allBadges then seed else search (seed + 1UL)

        search 0UL

    let hasEffectOn (state: MatchState) (target: CardInstanceId) kind =
        state.Effects
        |> Seq.exists (fun effect -> effect.TargetCard = ValueSome target && effect.Kind = kind)

    let canAttackWith (engine: MatchEngine) (state: MatchState) (actor: PlayerId) (effect: string) =
        engine.GetLegalActions(state, actor)
        |> Seq.exists (fun action ->
            match action.Command.Action with
            | MatchAction.Attack(_, attackId) -> attackId = EffectId effect
            | _ -> false)

type AuthorityErrataTests() =

    [<Test>]
    member _.``parkrun should discard only basic sober vim for its additional damage``() =
        let engine = MatchScenario.Engine()

        let soberVim =
            MatchScenario.PlainCard
                "sober-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let soberBlokemon =
            MatchScenario.PlainCard
                "sober-blokemon"
                "BLK-061"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-009" "BLK-003" [ "VIM-SOBER"; "VIM-SOBER" ] 881UL)
                [ soberVim; soberBlokemon ]

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                match candidate.Command.Action with
                | MatchAction.Attack(_, attackId) -> attackId = EffectId "BLK-009-B01"
                | _ -> false)
            |> Seq.exactlyOne

        let requested = MatchScenario.Applied(engine.Apply(state, action.Command))

        let cards =
            requested.PendingEffect.Value.Requirements
            |> Seq.filter (fun requirement -> requirement.Kind = ChoiceRequirementKind.Cards)
            |> Seq.exactlyOne

        let applied =
            MatchScenario.Applied(
                engine.Apply(
                    requested,
                    MatchScenario.ResolveEffectChoiceCommand
                        requested
                        (FrozenList<EffectChoice>
                            .Create(
                                EffectChoice.Cards(
                                    cards.Id,
                                    FrozenList<CardInstanceId>.Create soberVim.Id
                                )
                            ))
                )
            )

        cards.EligibleCards |> Seq.toList |> should equal [ soberVim.Id ]
        (applied.Card soberVim.Id).Zone |> should equal CardZone.EmptiesTray
        (applied.Card soberBlokemon.Id).Zone |> should equal CardZone.Mitt
        (applied.Card(CardInstanceId "defender")).Damage |> should equal 140

    [<Test>]
    [<Arguments("BLK-018", "BLK-018-B02", "VIM-SOBER,VIM-SOBER,VIM-SOBER")>]
    [<Arguments("BLK-119", "BLK-119-B01", "VIM-SOBER")>]
    member _.``a protection attack should block the reply's damage and its effects``
        (protector: string, attack: string, vim: string)
        =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleState protector "BLK-003" (vim.Split ',') (seedForBadge ())

        let state = attachToDefender state [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]

        let protectedRound, _ = applyAttack engine state MatchScenario.FirstPlayer attack

        let retaliation, _ =
            applyAttack engine protectedRound MatchScenario.SecondPlayer "BLK-003-B01"

        let protectedCard = retaliation.Card(CardInstanceId "attacker")

        protectedCard.Damage |> should equal 0
        protectedCard.RoughStates.Count |> should equal 0

    [<Test>]
    member _.``day two should force the opponent's beer mat to the blank side``() =
        let engine = MatchScenario.Engine()

        let state =
            MatchScenario.BattleStateWith
                "BLK-054"
                "BLK-001"
                [ "VIM-SOBER" ]
                (seedForBadge ())
                FrozenList.empty
                (FrozenList<RoughStateEntry>
                    .Create(MatchScenario.RoughState BlokemonRoughState.Muddled 1))
                FrozenList.empty

        let state = attachToDefender state [ "VIM-BLAZED"; "VIM-SOBER" ]

        let dayTwoState, dayTwoEvents =
            applyAttack engine state MatchScenario.FirstPlayer "BLK-054-B01"

        let opponentState, opponentEvents =
            applyAttack engine dayTwoState MatchScenario.SecondPlayer "BLK-001-B01"

        dayTwoEvents
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.BeerMatTossed)
        |> should be False

        opponentEvents
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackCancelled)
        |> should be True

        (opponentState.Card(CardInstanceId "attacker")).Damage |> should equal 0

    [<Test>]
    member _.``day two should not change the checkup that runs before the opponent's round``() =
        let state =
            MatchScenario.BattleStateWith
                "BLK-054"
                "BLK-001"
                [ "VIM-SOBER" ]
                (seedForBadge ())
                FrozenList.empty
                (FrozenList<RoughStateEntry>
                    .Create(MatchScenario.RoughState BlokemonRoughState.Singed 1))
                FrozenList.empty

        let applied, _ =
            applyAttack (MatchScenario.Engine()) state MatchScenario.FirstPlayer "BLK-054-B01"

        let defender = applied.Card(CardInstanceId "defender")

        defender.RoughStates
        |> Seq.exists (fun entry -> entry.State = BlokemonRoughState.Singed)
        |> should be False

        defender.Damage |> should equal 20

    [<Test>]
    member _.``day two should stay in force after day two moves to the booth``() =
        let engine = MatchScenario.Engine()

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let matchmaker =
            MatchScenario.PlainCard
                "matchmaker"
                "KIT-009"
                MatchScenario.SecondPlayer
                CardZone.Mitt
                -1

        let state =
            MatchScenario.BattleStateWith
                "BLK-054"
                "BLK-001"
                [ "VIM-SOBER" ]
                (seedForBadge ())
                FrozenList.empty
                (FrozenList<RoughStateEntry>
                    .Create(MatchScenario.RoughState BlokemonRoughState.Muddled 1))
                FrozenList.empty

        let state =
            MatchScenario.WithCards
                (attachToDefender state [ "VIM-BLAZED"; "VIM-SOBER" ])
                [ replacement; matchmaker ]

        let dayTwoState, _ =
            applyAttack engine state MatchScenario.FirstPlayer "BLK-054-B01"

        let kitAction =
            engine.GetLegalActions(dayTwoState, MatchScenario.SecondPlayer)
            |> Seq.filter (fun action ->
                match action.Command.Action with
                | MatchAction.PlayKit(played, _) -> played = matchmaker.Id
                | _ -> false)
            |> Seq.exactlyOne

        let switched = MatchScenario.Applied(engine.Apply(dayTwoState, kitAction.Command))

        let replyState, replyEvents =
            applyAttack engine switched MatchScenario.SecondPlayer "BLK-001-B01"

        (replyState.Card(CardInstanceId "attacker")).Zone |> should equal CardZone.Booth

        replyEvents
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackCancelled)
        |> should be True

        (replyState.Card replacement.Id).Damage |> should equal 0

    [<Test>]
    member _.``ronnie pickering should leave itself muddled after attacking``() =
        let state = MatchScenario.BattleState "BLK-057" "BLK-150" [ "VIM-LAIRY" ] 907UL

        let applied, _ =
            applyAttack (MatchScenario.Engine()) state MatchScenario.FirstPlayer "BLK-057-B01"

        (applied.Card(CardInstanceId "attacker")).RoughStates
        |> Seq.map (fun entry -> entry.State)
        |> Seq.toList
        |> should contain BlokemonRoughState.Muddled

    [<Test>]
    member _.``fly tipper should raise the taxi fare without raising the attack cost``() =
        let engine = MatchScenario.Engine()

        let state =
            attachToDefender
                (MatchScenario.BattleState "BLK-088" "BLK-001" [ "VIM-DODGY" ] 911UL)
                [ "VIM-BLAZED"; "VIM-SOBER" ]

        let applied, _ = applyAttack engine state MatchScenario.FirstPlayer "BLK-088-B01"
        let target = CardInstanceId "defender"

        hasEffectOn applied target TemporaryEffectKind.ModifyTaxiFare |> should be True

        hasEffectOn applied target TemporaryEffectKind.ModifyAttackCost
        |> should be False

        canAttackWith engine applied MatchScenario.SecondPlayer "BLK-001-B01"
        |> should be True

    [<Test>]
    member _.``a pending waste licence should raise both the attack cost and the taxi fare``() =
        let engine = MatchScenario.Engine()

        let state =
            attachToDefender
                (MatchScenario.BattleState "BLK-089" "BLK-001" [ "VIM-DODGY" ] 917UL)
                [ "VIM-BLAZED"; "VIM-SOBER" ]

        let applied, _ = applyAttack engine state MatchScenario.FirstPlayer "BLK-089-B01"
        let target = CardInstanceId "defender"

        hasEffectOn applied target TemporaryEffectKind.ModifyAttackCost
        |> should be True

        hasEffectOn applied target TemporaryEffectKind.ModifyTaxiFare |> should be True

        canAttackWith engine applied MatchScenario.SecondPlayer "BLK-001-B01"
        |> should be False

    [<Test>]
    member _.``a pool cue should boost last orders while last orders is on the table``() =
        let engine = MatchScenario.Engine()

        let poolCue =
            MatchScenario.PlainCard "pool-cue" "BLK-104" MatchScenario.FirstPlayer CardZone.Booth -1

        let boothTarget =
            MatchScenario.PlainCard
                "booth-target"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState "BLK-105" "BLK-150" [ "VIM-LAIRY" ] 919UL)
                [ poolCue; boothTarget ]

        let applied, _ = applyAttack engine state MatchScenario.FirstPlayer "BLK-105-B01"

        (applied.Card boothTarget.Id).Damage |> should equal 60

    [<Test>]
    member _.``hotbox should require the matchmaker for its additional damage``() =
        let wrongMate = namedMateState "BLK-114" "KIT-010" 923UL
        let matchmaker = namedMateState "BLK-114" "KIT-009" 927UL
        let engine = MatchScenario.Engine()

        let wrongOutcome, _ =
            applyAttack engine wrongMate MatchScenario.FirstPlayer "BLK-114-B01"

        let matchmakerOutcome, _ =
            applyAttack engine matchmaker MatchScenario.FirstPlayer "BLK-114-B01"

        (wrongOutcome.Card(CardInstanceId "defender")).Damage |> should equal 10
        (matchmakerOutcome.Card(CardInstanceId "defender")).Damage |> should equal 70

    [<Test>]
    member _.``a one-year chip should defer both required beer mats until the opponent attacks``() =
        let engine = MatchScenario.Engine()

        let state =
            attachToDefender
                (MatchScenario.BattleState
                    "BLK-117"
                    "BLK-001"
                    [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
                    (seedForTwoTosses false))
                [ "VIM-BLAZED"; "VIM-SOBER" ]

        let firstAttackState, firstAttackEvents =
            applyAttack engine state MatchScenario.FirstPlayer "BLK-117-B01"

        let _, replyEvents =
            applyAttack engine firstAttackState MatchScenario.SecondPlayer "BLK-001-B01"

        firstAttackEvents
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.BeerMatTossed)
        |> Seq.length
        |> should equal 0

        replyEvents
        |> Seq.filter (fun matchEvent -> matchEvent.Kind = MatchEventKind.BeerMatTossed)
        |> Seq.length
        |> should equal 2

        replyEvents
        |> Seq.exists (fun matchEvent -> matchEvent.Kind = MatchEventKind.AttackCancelled)
        |> should be True

    [<Test>]
    member _.``an old habit should return the opposing vim without attaching vim from the mitt``() =
        let engine = MatchScenario.Engine()

        let ownVim =
            MatchScenario.PlainCard
                "own-mitt-vim"
                "VIM-SOBER"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        let state =
            attachToDefender
                (MatchScenario.BattleState "BLK-138" "BLK-150" [ "VIM-SOBER"; "VIM-SOBER" ] 929UL)
                [ "VIM-BEER" ]

        let state = MatchScenario.WithCards state [ ownVim ]

        let opposingVim =
            state.CardsIn(MatchScenario.SecondPlayer, CardZone.Attached) |> Seq.exactlyOne

        let applied, _ = applyAttack engine state MatchScenario.FirstPlayer "BLK-138-B01"

        (applied.Card opposingVim.Id).Zone |> should equal CardZone.Mitt
        (applied.Card ownVim.Id).Zone |> should equal CardZone.Mitt
        (applied.Card ownVim.Id).AttachedTo.IsNone |> should be True

    [<Test>]
    member _.``fat les should be able to target a boothed bloke of any type``() =
        let engine = MatchScenario.Engine()

        let boothTarget =
            MatchScenario.PlainCard
                "booth-target"
                "BLK-036"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            MatchScenario.WithCards
                (MatchScenario.BattleState
                    "BLK-146"
                    "BLK-150"
                    [ "VIM-CURRY"; "VIM-CURRY"; "VIM-CURRY" ]
                    933UL)
                [ boothTarget ]

        let applied, _ = applyAttack engine state MatchScenario.FirstPlayer "BLK-146-B01"

        (applied.Card boothTarget.Id).Damage |> should equal 120

    [<Test>]
    member _.``mirror the mood should reflect attack damage even when it is knocked out``() =
        let engine = MatchScenario.Engine()

        let replacement =
            MatchScenario.PlainCard
                "replacement"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            attachToDefender
                (MatchScenario.BattleState "BLK-150" "BLK-001" [ "VIM-SOBER"; "VIM-SOBER" ] 937UL)
                [ "VIM-BLAZED"; "VIM-SOBER" ]

        let attacker =
            { state.Card(CardInstanceId "attacker") with
                Damage = 110 }

        let state = MatchScenario.WithCards state [ attacker; replacement ]

        let mirrorState, _ =
            applyAttack engine state MatchScenario.FirstPlayer "BLK-150-B01"

        let replyState, replyEvents =
            applyAttack engine mirrorState MatchScenario.SecondPlayer "BLK-001-B01"

        (replyState.Card(CardInstanceId "attacker")).Zone
        |> should equal CardZone.EmptiesTray

        replyEvents
        |> Seq.exists (fun matchEvent ->
            matchEvent.Kind = MatchEventKind.DamagePlaced
            && matchEvent.DamageKind = ValueSome DamageKind.PlacedCounter
            && matchEvent.Amount = 20
            && Seq.contains (CardInstanceId "defender") matchEvent.TargetCards)
        |> should be True
