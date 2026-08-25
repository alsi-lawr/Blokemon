namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

module private BaseRuleScenarios =

    let validEngine (authority: BlokemonRuntimeManifest) =
        let validation = BlokemonSetValidator.ValidateRuntime authority
        validation.IsValid |> should be True
        MatchEngine authority

    let withRules change =
        let authority = MatchScenario.Authority

        { authority with
            BaseRules = change authority.BaseRules }

    let withFirstRounds rounds (state: MatchState) =
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

    let startRequest (cards: string seq) =
        { MatchId = MatchId "base-rule-start"
          Seed = MatchSeed 0xB451UL
          FirstDeck = FrozenDeckSnapshot.Create(MatchScenario.FirstPlayer, cards)
          SecondDeck = FrozenDeckSnapshot.Create(MatchScenario.SecondPlayer, cards) }

    let regularCards count =
        MatchScenario.Authority.Collectibles
        |> Seq.filter (fun card -> card.Rank = BlokemonRank.Regular)
        |> Seq.collect (fun card -> Seq.replicate 4 card.Id)
        |> Seq.truncate count
        |> Seq.toArray

    let command state id action =
        MatchScenario.Command state id MatchScenario.FirstPlayer ImmutableArray<_>.Empty action

    let withCard (state: MatchState) card = MatchScenario.WithCards state [ card ]

    let addPromotion (mechanicalId: string) (state: MatchState) =
        let promotion =
            MatchScenario.PlainCard
                "promotion"
                mechanicalId
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        withCard state promotion, promotion

    let promote (authority: BlokemonRuntimeManifest) (state: MatchState) promotion target =
        (validEngine authority)
            .Apply(
                state,
                command
                    state
                    $"promote:{promotion}:{target}"
                    (MatchAction.Promote(CardInstanceId promotion, CardInstanceId target))
            )

    let addTaxiBench (state: MatchState) =
        let bench =
            MatchScenario.PlainCard
                "taxi-bench"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        withCard state bench, bench

    let taxi authority state fare =
        (validEngine authority)
            .Apply(
                state,
                command
                    state
                    "taxi"
                    (MatchAction.Taxi(
                        CardInstanceId "taxi-bench",
                        ImmutableArray.CreateRange(
                            seq {
                                for index in 0 .. fare - 1 do
                                    yield CardInstanceId $"vim-{index}"
                            }
                        )
                    ))
            )

    let replaceRoughRule state change (rules: BlokemonBaseRules) =
        { rules with
            RoughStates =
                rules.RoughStates
                |> Array.map (fun rule -> if rule.State = state then change rule else rule) }

    let blankSeed () =
        Seq.initInfinite uint64
        |> Seq.find (fun seed ->
            let random = BlokemonSeededRandom seed
            random.NextInt 2 = 0)

    let badgeSeed () =
        Seq.initInfinite uint64
        |> Seq.find (fun seed ->
            let random = BlokemonSeededRandom seed
            random.NextInt 2 = 1)

    let battleWithRough state seed attachedVim =
        MatchScenario.BattleStateWith
            "BLK-003"
            "BLK-076"
            attachedVim
            seed
            (ImmutableArray.Create(MatchScenario.RoughState state 1))
            ImmutableArray<_>.Empty
            ImmutableArray<_>.Empty

    let hasRough state (card: CardState) =
        card.RoughStates |> Seq.exists (fun entry -> entry.State = state)

    let localReplacementState () : MatchState * CardState * CardState =
        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 1811UL

        let existing =
            MatchScenario.PlainCard
                "existing-local"
                "KIT-006"
                MatchScenario.FirstPlayer
                CardZone.Local
                -1

        let incoming =
            MatchScenario.PlainCard
                "incoming-local"
                "KIT-006"
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        MatchScenario.WithCards state [ existing; incoming ], existing, incoming

    let playIncomingLocal
        (authority: BlokemonRuntimeManifest)
        (state: MatchState)
        (incoming: CardState)
        =
        (validEngine authority)
            .Apply(
                state,
                command state "incoming-local" (MatchAction.PlayKit(incoming.Id, ValueNone))
            )

    let bigHitterIds =
        set
            [ "BLK-003"
              "BLK-006"
              "BLK-009"
              "BLK-024"
              "BLK-038"
              "BLK-065"
              "BLK-076"
              "BLK-115"
              "BLK-124"
              "BLK-145"
              "BLK-151" ]

    let withSendHomeAwards normalAward bigHitterAward authority =
        { authority with
            BaseRules =
                { authority.BaseRules with
                    SendHome =
                        { authority.BaseRules.SendHome with
                            NormalBarChits = normalAward
                            BigHitterBarChits = bigHitterAward } }
            Collectibles =
                authority.Collectibles
                |> Array.map (fun card ->
                    { card with
                        BarChitsWhenSentHome =
                            if bigHitterIds.Contains card.Id then
                                bigHitterAward
                            else
                                normalAward }) }

    let taxiState seed =
        MatchScenario.BattleState
            "BLK-003"
            "BLK-076"
            [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
            seed
        |> addTaxiBench
        |> fst


type BaseRuleBehaviorTests() =

    [<Test>]
    member _.``Stack card count should independently change start request legality``() =
        let fiftyNine = BaseRuleScenarios.regularCards 59

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Stack = { rules.Stack with CardCount = 59 } })

        (BaseRuleScenarios.validEngine authority).Start(BaseRuleScenarios.startRequest fiftyNine)
        |> MatchScenario.Started
        |> _.Cards.Length
        |> should equal 118

        MatchEngine(MatchScenario.Authority).Start(BaseRuleScenarios.startRequest fiftyNine)
        |> MatchScenario.StartRejected
        |> Seq.map _.Code
        |> should contain DeckIssueCode.WrongCardCount

    [<Test>]
    member _.``mechanical copy limit should independently change duplicate legality``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Stack =
                        { rules.Stack with
                            MechanicalCopyLimit = 3 } })

        (BaseRuleScenarios.validEngine authority).Start(MatchScenario.StartRequest())
        |> MatchScenario.StartRejected
        |> Seq.map _.Code
        |> should contain DeckIssueCode.TooManyCopies

    [<Test>]
    member _.``Basic Vim exemption should independently change duplicate legality``() =
        let regular = BaseRuleScenarios.regularCards 1
        let vimHeavy = Array.append regular (Array.create 59 "VIM-BLAZED")

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Stack =
                        { rules.Stack with
                            BasicVimExempt = false } })

        (BaseRuleScenarios.validEngine authority).Start(BaseRuleScenarios.startRequest vimHeavy)
        |> MatchScenario.StartRejected
        |> Seq.map _.Code
        |> should contain DeckIssueCode.TooManyCopies

    [<Test>]
    member _.``opening mitt size should independently change dealt Mitt cards``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Opening = { rules.Opening with MittSize = 6 } })

        let opening =
            (BaseRuleScenarios.validEngine authority).Start(MatchScenario.StartRequest())
            |> MatchScenario.Started

        opening.Players
        |> Seq.forall (fun player ->
            opening.CardsIn(player.Id, CardZone.Mitt) |> Seq.length |> (=) 6)
        |> should be True

    [<Test>]
    member _.``opening Booth limit should independently change opening choice capacity``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Opening = { rules.Opening with BoothLimit = 3 } })

        let engine = BaseRuleScenarios.validEngine authority
        let opening = engine.Start(MatchScenario.StartRequest()) |> MatchScenario.Started

        engine.GetLegalActions(opening, MatchScenario.FirstPlayer)
        |> Seq.filter (fun action -> action.Kind = LegalActionKind.ChooseOpening)
        |> Seq.collect _.ChoiceRequirements
        |> Seq.map _.Maximum
        |> Seq.max
        |> should equal 3

    [<Test>]
    member _.``opening Bar Chit count should independently change both players' counters``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Opening = { rules.Opening with BarChitCount = 4 } })

        (BaseRuleScenarios.validEngine authority).Start(MatchScenario.StartRequest())
        |> MatchScenario.Started
        |> _.Players
        |> Seq.map _.BarChitsRemaining
        |> should equal [ 4; 4 ]

    [<Test>]
    member _.``opening participant Attack permission should independently change Attack legality``
        ()
        =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-SOBER" ] 1701UL
            |> BaseRuleScenarios.withFirstRounds 1
            |> fun current ->
                { current with
                    OpeningPlayer = MatchScenario.FirstPlayer }

        MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.EffectUnavailable

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Opening =
                        { rules.Opening with
                            OpeningParticipantMayAttack = true } })

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.Applied
        |> ignore

    [<Test>]
    member _.``opening participant Mate permission should independently change Mate legality``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 1702UL
            |> BaseRuleScenarios.withFirstRounds 1
            |> fun current ->
                { current with
                    OpeningPlayer = MatchScenario.FirstPlayer }
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ MatchScenario.PlainCard
                          "mate"
                          "KIT-005"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]

        MatchScenario.Engine().GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            match action.Command.Action with
            | MatchAction.PlayKit(card, _) -> card = CardInstanceId "mate"
            | _ -> false)
        |> should be False

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Opening =
                        { rules.Opening with
                            OpeningParticipantMayPlayMate = true } })

        (BaseRuleScenarios.validEngine authority).GetLegalActions(state, MatchScenario.FirstPlayer)
        |> Seq.exists (fun action ->
            match action.Command.Action with
            | MatchAction.PlayKit(card, _) -> card = CardInstanceId "mate"
            | _ -> false)
        |> should be True

    [<Test>]
    member _.``required opening draw should independently change round-start draw behavior``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Round =
                        { rules.Round with
                            RequiredOpeningDraw = false } })

        let state = MatchScenario.BattleState "BLK-001" "BLK-150" [] 1703UL

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, BaseRuleScenarios.command state "end" MatchAction.EndRound)
        |> MatchScenario.Applied
        |> fun after -> after.CardsIn(MatchScenario.SecondPlayer, CardZone.Stack)
        |> Seq.map _.Id
        |> should contain (CardInstanceId "second-draw")

    [<Test>]
    member _.``Attack-ends-round should independently change active-player completion``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Round =
                        { rules.Round with
                            AttackEndsRound = false } })

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-SOBER" ] 1705UL

        let after, events =
            (BaseRuleScenarios.validEngine authority)
                .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            |> MatchScenario.AppliedWith

        after.ActivePlayer |> should equal MatchScenario.FirstPlayer
        events |> Seq.map _.Kind |> should not' (contain MatchEventKind.RoundEnded)

    [<Test>]
    member _.``exact promotion edge should independently change unrelated promotion legality``() =
        let state, promotion =
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 1707UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            ExactMechanicalEdgeRequired = false } })

        BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
        |> MatchScenario.Applied
        |> fun after -> (after.Card promotion.Id).Zone
        |> should equal CardZone.Oche

    [<Test>]
    member _.``first-round promotion rule should independently change promotion legality``() =
        let current, promotion =
            MatchScenario.BattleState "BLK-004" "BLK-150" [ "VIM-SOBER" ] 1709UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let state =
            { BaseRuleScenarios.withFirstRounds 1 current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with EnteredAtOwnerRound = 0 }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            NotOnEitherFirstRound = false } })

        BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
        |> MatchScenario.Applied
        |> ignore

    [<Test>]
    member _.``first-round-in-play rule should independently change promotion legality``() =
        let current, promotion =
            MatchScenario.BattleState "BLK-004" "BLK-150" [ "VIM-SOBER" ] 1710UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let state =
            { current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with EnteredAtOwnerRound = 2 }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            NotFirstRoundInPlay = false } })

        BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
        |> MatchScenario.Applied
        |> ignore

    [<Test>]
    member _.``once-per-round promotion rule should independently change repeat legality``() =
        let current, promotion =
            MatchScenario.BattleState "BLK-004" "BLK-150" [ "VIM-SOBER" ] 1712UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let state =
            { current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with
                                    LastPromotedRound = current.RoundNumber }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            NotTwiceInRound = false } })

        BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
        |> MatchScenario.Applied
        |> ignore

    [<Test>]
    member _.``promotion retention should independently change damage and attachment disposal``() =
        let current, promotion =
            MatchScenario.BattleState "BLK-004" "BLK-150" [ "VIM-SOBER" ] 1714UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let state =
            { current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with Damage = 20 }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            RetainDamageAndAttachedCards = false } })

        let after =
            BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
            |> MatchScenario.Applied

        (after.Card promotion.Id).Damage |> should equal 0
        (after.Card(CardInstanceId "vim-0")).Zone |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``promotion clearing should independently change rough-state retention``() =
        let current, promotion =
            MatchScenario.BattleState "BLK-004" "BLK-150" [ "VIM-SOBER" ] 1716UL
            |> BaseRuleScenarios.addPromotion "BLK-005"

        let state =
            { current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with
                                    RoughStates =
                                        ImmutableArray.Create(
                                            MatchScenario.RoughState BlokemonRoughState.DodgyPint 1
                                        ) }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Promotion =
                        { rules.Promotion with
                            ClearRoughStatesAndAttackEffects = false } })

        BaseRuleScenarios.promote authority state promotion.Id.Value "attacker"
        |> MatchScenario.Applied
        |> fun after -> (after.Card promotion.Id).RoughStates
        |> Seq.map _.State
        |> should contain BlokemonRoughState.DodgyPint

    [<Test>]
    member _.``normal Vim attachment limit should change public attachment legality``() =
        let baseState = MatchScenario.BattleState "BLK-001" "BLK-150" [] 1711UL

        let vimCards =
            [ MatchScenario.PlainCard
                  "new-vim-1"
                  "VIM-BLAZED"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "new-vim-2"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

        let vimState = MatchScenario.WithCards baseState vimCards

        let twoVim =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Vim =
                        { rules.Vim with
                            NormalAttachmentPerRound = 2 } })

        let vimEngine = BaseRuleScenarios.validEngine twoVim

        let afterFirst =
            vimEngine.Apply(
                vimState,
                BaseRuleScenarios.command
                    vimState
                    "vim-1"
                    (MatchAction.AttachVim(CardInstanceId "new-vim-1", CardInstanceId "attacker"))
            )
            |> MatchScenario.Applied

        let afterSecond =
            vimEngine.Apply(
                afterFirst,
                BaseRuleScenarios.command
                    afterFirst
                    "vim-2"
                    (MatchAction.AttachVim(CardInstanceId "new-vim-2", CardInstanceId "attacker"))
            )
            |> MatchScenario.Applied

        (afterSecond.Card(CardInstanceId "attacker")).Attachments.Length
        |> should equal 2

    [<Test>]
    member _.``Bar Kit per-Bloke limit should independently change attachment legality``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 1813UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ MatchScenario.PlainCard
                          "bar-kit"
                          "KIT-004"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit = { rules.Kit with BarKitsPerBloke = 0 } })

        (BaseRuleScenarios.validEngine authority)
            .Apply(
                state,
                BaseRuleScenarios.command
                    state
                    "bar-kit-limit"
                    (MatchAction.PlayKit(
                        CardInstanceId "bar-kit",
                        ValueSome(CardInstanceId "attacker")
                    ))
            )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

    [<Test>]
    member _.``Mate per-round limit should independently change Mate legality``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 1815UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ MatchScenario.PlainCard
                          "mate"
                          "KIT-005"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit = { rules.Kit with MatesPerRound = 0 } })

        (BaseRuleScenarios.validEngine authority)
            .Apply(
                state,
                BaseRuleScenarios.command
                    state
                    "mate-limit"
                    (MatchAction.PlayKit(CardInstanceId "mate", ValueNone))
            )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

    [<Test>]
    member _.``Local per-round limit should independently change Local legality``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 1817UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ MatchScenario.PlainCard
                          "local"
                          "KIT-006"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit = { rules.Kit with LocalsPerRound = 0 } })

        (BaseRuleScenarios.validEngine authority)
            .Apply(
                state,
                BaseRuleScenarios.command
                    state
                    "local-limit"
                    (MatchAction.PlayKit(CardInstanceId "local", ValueNone))
            )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

    [<Test>]
    member _.``one-local-in-play should independently control a non-replacing second Local``() =
        let state, _, incoming = BaseRuleScenarios.localReplacementState ()

        let oneLocalDisabled =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit =
                        { rules.Kit with
                            OneLocalInPlay = false
                            SameMechanicalLocalCannotReplace = false
                            NewLocalChucksOld = false } })

        let oneLocalEnabled =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit =
                        { rules.Kit with
                            OneLocalInPlay = true
                            SameMechanicalLocalCannotReplace = false
                            NewLocalChucksOld = false } })

        BaseRuleScenarios.playIncomingLocal oneLocalEnabled state incoming
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

        BaseRuleScenarios.playIncomingLocal oneLocalDisabled state incoming
        |> MatchScenario.Applied
        |> fun after -> after.CardsIn(MatchScenario.FirstPlayer, CardZone.Local) |> Seq.length
        |> should equal 2

    [<Test>]
    member _.``same-mechanical-local should independently control replacement legality``() =
        let state, existing, incoming = BaseRuleScenarios.localReplacementState ()

        let sameMechanicalAllowed =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit =
                        { rules.Kit with
                            SameMechanicalLocalCannotReplace = false } })

        BaseRuleScenarios.playIncomingLocal MatchScenario.Authority state incoming
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.RuleLimitReached

        let after =
            BaseRuleScenarios.playIncomingLocal sameMechanicalAllowed state incoming
            |> MatchScenario.Applied

        (after.Card existing.Id).Zone |> should equal CardZone.EmptiesTray
        (after.Card incoming.Id).Zone |> should equal CardZone.Local

    [<Test>]
    member _.``new-local-chucks-old should independently control old Local disposal``() =
        let state, existing, incoming = BaseRuleScenarios.localReplacementState ()

        let oldLocalRetained =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit =
                        { rules.Kit with
                            OneLocalInPlay = false
                            SameMechanicalLocalCannotReplace = false
                            NewLocalChucksOld = false } })

        let oldLocalChucked =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Kit =
                        { rules.Kit with
                            OneLocalInPlay = false
                            SameMechanicalLocalCannotReplace = false
                            NewLocalChucksOld = true } })

        BaseRuleScenarios.playIncomingLocal oldLocalChucked state incoming
        |> MatchScenario.Applied
        |> fun after -> (after.Card existing.Id).Zone
        |> should equal CardZone.EmptiesTray

        let after =
            BaseRuleScenarios.playIncomingLocal oldLocalRetained state incoming
            |> MatchScenario.Applied

        (after.Card existing.Id).Zone |> should equal CardZone.Local
        (after.Card incoming.Id).Zone |> should equal CardZone.Local

    [<Test>]
    member _.``Taxi per-round limit should independently change action legality``() =
        let state = BaseRuleScenarios.taxiState 1713UL

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Taxi = { rules.Taxi with PerRound = 0 } })

        BaseRuleScenarios.taxi authority state 4
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.EffectUnavailable

    [<Test>]
    member _.``Taxi fare chucking should independently change paid Vim retention``() =
        let state = BaseRuleScenarios.taxiState 1715UL

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Taxi =
                        { rules.Taxi with
                            ChuckVimPerFareSymbol = false } })

        BaseRuleScenarios.taxi authority state 4
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "vim-0")).Zone
        |> should equal CardZone.Attached

    [<Test>]
    member _.``Taxi retention should independently change outgoing pile and damage``() =
        let state = BaseRuleScenarios.taxiState 1717UL

        let attachedKit =
            MatchScenario.AttachedCard
                "retained-kit"
                "KIT-013"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let state =
            MatchScenario.WithCards
                state
                [ attachedKit
                  { state.Card(CardInstanceId "attacker") with
                      Damage = 20
                      Attachments =
                          (state.Card(CardInstanceId "attacker")).Attachments.Add attachedKit.Id } ]

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Taxi =
                        { rules.Taxi with
                            AttachedCardsAndDamageRemain = false } })

        let after = BaseRuleScenarios.taxi authority state 4 |> MatchScenario.Applied
        (after.Card attachedKit.Id).Zone |> should equal CardZone.EmptiesTray
        (after.Card(CardInstanceId "attacker")).Damage |> should equal 0

    [<Test>]
    member _.``Taxi clearing should independently change outgoing rough-state retention``() =
        let current = BaseRuleScenarios.taxiState 1719UL

        let state =
            { current with
                Cards =
                    ImmutableArray.CreateRange(
                        current.Cards
                        |> Seq.map (fun card ->
                            if card.Id = CardInstanceId "attacker" then
                                { card with
                                    RoughStates =
                                        ImmutableArray.Create(
                                            MatchScenario.RoughState BlokemonRoughState.DodgyPint 1
                                        ) }
                            else
                                card)
                    ) }

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Taxi =
                        { rules.Taxi with
                            MovingToBoothClearsRoughStatesAndAttackEffects = false } })

        BaseRuleScenarios.taxi authority state 4
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "attacker")).RoughStates
        |> Seq.map _.State
        |> should contain BlokemonRoughState.DodgyPint

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint, 2)>]
    [<Arguments(BlokemonRoughState.Singed, 3)>]
    [<Arguments(BlokemonRoughState.NoddedOff, 1)>]
    [<Arguments(BlokemonRoughState.Legless, 1)>]
    member _.``each checkup damage leaf should change placed rough-state damage``
        (roughState: BlokemonRoughState, counters: int)
        =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                BaseRuleScenarios.replaceRoughRule
                    roughState
                    (fun rule ->
                        { rule with
                            CheckupDamageCounters = counters })
                    rules)

        let state = BaseRuleScenarios.battleWithRough roughState 1801UL []

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, BaseRuleScenarios.command state "checkup-damage" MatchAction.EndRound)
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "attacker")).Damage
        |> should equal (counters * 10)

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    member _.``each checkup beer-mat leaf should control its public toss event``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                BaseRuleScenarios.replaceRoughRule
                    roughState
                    (fun rule ->
                        { rule with
                            CheckupBeerMat = not current.CheckupBeerMat
                            BadgeSideRecovers = not current.CheckupBeerMat })
                    rules)

        let state = BaseRuleScenarios.battleWithRough roughState 1803UL []

        let _, events =
            (BaseRuleScenarios.validEngine authority)
                .Apply(
                    state,
                    BaseRuleScenarios.command state "checkup-beer-mat" MatchAction.EndRound
                )
            |> MatchScenario.AppliedWith

        (events |> Seq.exists (fun event -> event.Kind = MatchEventKind.BeerMatTossed))
        |> should equal (not current.CheckupBeerMat)

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    member _.``each badge recovery leaf should change badge-side state recovery``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let recovers = not current.BadgeSideRecovers

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                BaseRuleScenarios.replaceRoughRule
                    roughState
                    (fun rule ->
                        { rule with
                            CheckupBeerMat = true
                            BadgeSideRecovers = recovers })
                    rules)

        let state =
            BaseRuleScenarios.battleWithRough roughState (BaseRuleScenarios.badgeSeed ()) []

        let after =
            (BaseRuleScenarios.validEngine authority)
                .Apply(state, BaseRuleScenarios.command state "badge-recovery" MatchAction.EndRound)
            |> MatchScenario.Applied

        BaseRuleScenarios.hasRough roughState (after.Card(CardInstanceId "attacker"))
        |> should equal (not recovers)

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    [<Arguments(BlokemonRoughState.Muddled)>]
    member _.``each prevents-attack leaf should change public Attack legality``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let prevents = not current.PreventsAttack

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                let changed =
                    BaseRuleScenarios.replaceRoughRule
                        roughState
                        (fun rule -> { rule with PreventsAttack = prevents })
                        rules

                { changed with
                    Round =
                        { changed.Round with
                            AttackEndsRound = false } })

        let state =
            BaseRuleScenarios.battleWithRough
                roughState
                (BaseRuleScenarios.badgeSeed ())
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]

        let outcome =
            (BaseRuleScenarios.validEngine authority)
                .Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")

        if prevents then
            MatchScenario.RejectionCode outcome
            |> should equal CommandRejectionCode.EffectUnavailable
        else
            outcome |> MatchScenario.Applied |> ignore

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    [<Arguments(BlokemonRoughState.Muddled)>]
    member _.``each prevents-taxi leaf should change public Taxi legality``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let prevents = not current.PreventsTaxi

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                BaseRuleScenarios.replaceRoughRule
                    roughState
                    (fun rule -> { rule with PreventsTaxi = prevents })
                    rules)

        let state =
            BaseRuleScenarios.battleWithRough
                roughState
                1805UL
                [ "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER"; "VIM-SOBER" ]
            |> BaseRuleScenarios.addTaxiBench
            |> fst

        let outcome = BaseRuleScenarios.taxi authority state 4

        if prevents then
            MatchScenario.RejectionCode outcome
            |> should equal CommandRejectionCode.EffectUnavailable
        else
            outcome |> MatchScenario.Applied |> ignore

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    member _.``each next-round recovery leaf should change timed rough-state recovery``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let recovers = not current.RecoversAfterOwnersNextRound.HasValue

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                BaseRuleScenarios.replaceRoughRule
                    roughState
                    (fun rule ->
                        { rule with
                            CheckupBeerMat = false
                            BadgeSideRecovers = false
                            RecoversAfterOwnersNextRound =
                                if recovers then Nullable true else Nullable() })
                    rules)

        let state = BaseRuleScenarios.battleWithRough roughState 1807UL []

        let after =
            (BaseRuleScenarios.validEngine authority)
                .Apply(state, BaseRuleScenarios.command state "round-recovery" MatchAction.EndRound)
            |> MatchScenario.Applied

        BaseRuleScenarios.hasRough roughState (after.Card(CardInstanceId "attacker"))
        |> should equal (not recovers)

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint)>]
    [<Arguments(BlokemonRoughState.Singed)>]
    [<Arguments(BlokemonRoughState.NoddedOff)>]
    [<Arguments(BlokemonRoughState.Legless)>]
    [<Arguments(BlokemonRoughState.Muddled)>]
    member _.``each before-attack beer-mat leaf should change attack cancellation``
        (roughState: BlokemonRoughState)
        =
        let current =
            MatchScenario.Authority.BaseRules.RoughStates
            |> Array.find (fun rule -> rule.State = roughState)

        let enabled = not current.BeforeAttackBeerMat.HasValue

        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                let changed =
                    BaseRuleScenarios.replaceRoughRule
                        roughState
                        (fun rule ->
                            { rule with
                                PreventsAttack = false
                                BeforeAttackBeerMat =
                                    if enabled then Nullable true else Nullable()
                                BlankSideCancelsAndSelfDamageCounters =
                                    if enabled then Nullable 3 else Nullable() })
                        rules

                { changed with
                    Round =
                        { changed.Round with
                            AttackEndsRound = false } })

        let state =
            MatchScenario.BattleStateWith
                "BLK-001"
                "BLK-150"
                [ "VIM-BLAZED"; "VIM-SOBER" ]
                (BaseRuleScenarios.blankSeed ())
                (ImmutableArray.Create(MatchScenario.RoughState roughState 1))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        let _, events =
            (BaseRuleScenarios.validEngine authority)
                .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            |> MatchScenario.AppliedWith

        (events |> Seq.exists (fun event -> event.Kind = MatchEventKind.AttackCancelled))
        |> should equal enabled

    [<Test>]
    [<Arguments(BlokemonRoughState.DodgyPint, 50)>]
    [<Arguments(BlokemonRoughState.Singed, 60)>]
    [<Arguments(BlokemonRoughState.NoddedOff, 40)>]
    [<Arguments(BlokemonRoughState.Legless, 40)>]
    [<Arguments(BlokemonRoughState.Muddled, 40)>]
    member _.``each blank-side counter leaf should change self damage``
        (roughState: BlokemonRoughState, expectedDamage: int)
        =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                let changed =
                    BaseRuleScenarios.replaceRoughRule
                        roughState
                        (fun rule ->
                            { rule with
                                PreventsAttack = false
                                BeforeAttackBeerMat = Nullable true
                                BlankSideCancelsAndSelfDamageCounters = Nullable 4 })
                        rules

                { changed with
                    Round =
                        { changed.Round with
                            AttackEndsRound = false } })

        let state =
            MatchScenario.BattleStateWith
                "BLK-001"
                "BLK-150"
                [ "VIM-BLAZED"; "VIM-SOBER" ]
                (BaseRuleScenarios.blankSeed ())
                (ImmutableArray.Create(MatchScenario.RoughState roughState 1))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "attacker")).Damage
        |> should equal expectedDamage

    [<Test>]
    member _.``rotated-state replacement should follow its own authority leaf``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Round =
                        { rules.Round with
                            AttackEndsRound = false }
                    RoughStateCoexistence =
                        { rules.RoughStateCoexistence with
                            LatestRotatedStateReplacesPrevious = false } })

        let state =
            MatchScenario.BattleStateWith
                "BLK-003"
                "BLK-076"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                1809UL
                ImmutableArray<_>.Empty
                (ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.Legless 1))
                ImmutableArray<_>.Empty

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "defender")).RoughStates
        |> Seq.map _.State
        |> should contain BlokemonRoughState.Legless

    [<Test>]
    member _.``normal send-home award should change non-Big-Hitter Bar Chits``() =
        let authority = MatchScenario.Authority |> BaseRuleScenarios.withSendHomeAwards 3 2

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-040"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                1721UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    ([ { current.Card(CardInstanceId "defender") with
                           Damage = 999 } ]
                     @ [ for index in 0..5 ->
                             MatchScenario.PlainCard
                                 $"bar-chit-{index}"
                                 "VIM-SOBER"
                                 MatchScenario.FirstPlayer
                                 CardZone.BarChit
                                 index ])

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Player MatchScenario.FirstPlayer).BarChitsRemaining
        |> should equal 3

    [<Test>]
    member _.``Big Hitter send-home award should use the independent BLK-076 scenario``() =
        let authority = MatchScenario.Authority |> BaseRuleScenarios.withSendHomeAwards 1 4

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-076"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                1722UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    ([ { current.Card(CardInstanceId "defender") with
                           Damage = 999 } ]
                     @ [ for index in 0..5 ->
                             MatchScenario.PlainCard
                                 $"bar-chit-{index}"
                                 "VIM-SOBER"
                                 MatchScenario.FirstPlayer
                                 CardZone.BarChit
                                 index ])

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Player MatchScenario.FirstPlayer).BarChitsRemaining
        |> should equal 2

    [<Test>]
    member _.``sudden-death Bar Chits should change the public tie reset``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    Win =
                        { rules.Win with
                            SuddenDeathBarChits = 3 } })

        let state =
            MatchScenario.BattleState "BLK-001" "BLK-003" [ "VIM-BLAZED"; "VIM-SOBER" ] 1723UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ { current.Card(CardInstanceId "attacker") with
                          Damage = 999 }
                      { current.Card(CardInstanceId "defender") with
                          Damage = 999 }
                      MatchScenario.PlainCard
                          "first-draw-2"
                          "VIM-BLAZED"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1
                      MatchScenario.PlainCard
                          "first-draw-3"
                          "VIM-BLAZED"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          2
                      MatchScenario.PlainCard
                          "second-draw-2"
                          "VIM-SOBER"
                          MatchScenario.SecondPlayer
                          CardZone.Stack
                          1
                      MatchScenario.PlainCard
                          "second-draw-3"
                          "VIM-SOBER"
                          MatchScenario.SecondPlayer
                          CardZone.Stack
                          2 ]

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.Applied
        |> _.Players
        |> Seq.map _.BarChitsRemaining
        |> should equal [ 3; 3 ]

    [<Test>]
    member _.``Fossil staying power should independently change send-home threshold``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    FossilKits =
                        { rules.FossilKits with
                            PlayAsRegularLocalStayingPower = 10 } })

        let state =
            MatchScenario.BattleState "BLK-001" "KIT-002" [ "VIM-BLAZED"; "VIM-SOBER" ] 1725UL

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Card(CardInstanceId "defender")).Zone
        |> should equal CardZone.EmptiesTray

    [<Test>]
    member _.``Fossil rough-state immunity should independently change applied state``() =
        let authority cannotHaveRoughStates =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    FossilKits =
                        { rules.FossilKits with
                            PlayAsRegularLocalStayingPower = 400
                            CannotHaveRoughStates = cannotHaveRoughStates } })

        let state =
            MatchScenario.BattleState
                "BLK-003"
                "KIT-002"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                1726UL

        let roughStates cannotHave =
            (BaseRuleScenarios.validEngine (authority cannotHave))
                .Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            |> MatchScenario.Applied
            |> fun after -> (after.Card(CardInstanceId "defender")).RoughStates
            |> Seq.map _.State
            |> Seq.toArray

        roughStates true |> should be Empty
        roughStates false |> should contain BlokemonRoughState.DodgyPint

    [<Test>]
    member _.``Fossil voluntary-chuck permission should independently change action legality``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    FossilKits =
                        { rules.FossilKits with
                            MayChuckFromPlayDuringOwnersRound = false } })

        let state = MatchScenario.BattleState "KIT-001" "BLK-150" [] 1727UL

        (BaseRuleScenarios.validEngine authority)
            .Apply(
                state,
                BaseRuleScenarios.command
                    state
                    "chuck-fossil"
                    (MatchAction.ChuckFossil(CardInstanceId "attacker"))
            )
        |> MatchScenario.RejectionCode
        |> should equal CommandRejectionCode.EffectUnavailable

    [<Test>]
    member _.``Fossil send-home award should independently suppress one Bar Chit``() =
        let authority =
            BaseRuleScenarios.withRules (fun rules ->
                { rules with
                    FossilKits =
                        { rules.FossilKits with
                            SentHomeAwardsOneBarChit = false } })

        let state =
            MatchScenario.BattleState "BLK-001" "KIT-002" [ "VIM-BLAZED"; "VIM-SOBER" ] 1729UL
            |> fun current ->
                MatchScenario.WithCards
                    current
                    [ { current.Card(CardInstanceId "defender") with
                          Damage = 999 } ]

        (BaseRuleScenarios.validEngine authority)
            .Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
        |> MatchScenario.Applied
        |> fun after -> (after.Player MatchScenario.FirstPlayer).BarChitsRemaining
        |> should equal 6
