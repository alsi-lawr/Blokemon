namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private FullSetBehaviorFixtures =

    type Execution =
        { State: MatchState
          Events: ImmutableArray<MatchEvent> }

    let addCards (state: MatchState) (added: CardState seq) =
        { state with
            Cards =
                ImmutableArray.CreateRange(
                    Seq.append state.Cards added |> Seq.sortBy (fun card -> card.Id)
                ) }

    let seedForBadge () =
        let rec search (seed: uint64) =
            if seed >= 100UL then failwith "No badge-side seed found."
            elif BlokemonSeededRandom(seed).NextInt 2 = 1 then seed
            else search (seed + 1UL)

        search 0UL

    let private chooserFor (state: MatchState) =
        match state.Phase with
        | MatchPhase.AwaitingEffectChoice -> state.PendingEffect.Value.Chooser
        | MatchPhase.AwaitingTriggerChoice ->
            match state.PendingKnockout with
            | ValueSome knockout -> knockout.Chooser
            | ValueNone -> state.PendingBarChits[0].Player
        | MatchPhase.AwaitingReplacement -> state.ReplacementPlayer.Value
        | other -> failwith $"Unexpected phase {other}."

    /// Applies the selected command and then keeps answering whatever the engine asks for until the
    /// caller's settled predicate holds, so a whole attack or play can be compared as one unit.
    let private drive (engine: MatchEngine) (initial: MatchState) (command: MatchCommand) settled =
        let events = ResizeArray<MatchEvent>()

        let apply state command =
            let applied, applied_events =
                MatchScenario.AppliedWith(engine.Apply(state, command))

            events.AddRange applied_events
            applied

        let mutable state = apply initial command
        let mutable resolutions = 0

        while not (settled state) do
            if resolutions >= 20 then
                failwith "Effect resolution did not settle."

            resolutions <- resolutions + 1
            let chooser = chooserFor state

            match engine.GetLegalActions(state, chooser) |> Seq.tryHead with
            | None -> failwith $"No legal resolution for {chooser.Value} in phase {state.Phase}."
            | Some resolution -> state <- apply state resolution.Command

        { State = state
          Events = ImmutableArray.CreateRange events }

    let offers (initial: MatchState) (select: LegalAction -> bool) =
        MatchScenario.Engine().GetLegalActions(initial, MatchScenario.FirstPlayer)
        |> Seq.exists select

    let executeAction (initial: MatchState) (select: LegalAction -> bool) =
        let engine = MatchScenario.Engine()

        let action =
            engine.GetLegalActions(initial, MatchScenario.FirstPlayer) |> Seq.find select

        drive engine initial action.Command (fun state ->
            state.Phase = MatchPhase.Playing || state.Phase = MatchPhase.Complete)

    let executeAttack (initial: MatchState) (attackId: string) =
        let engine = MatchScenario.Engine()

        let action =
            engine.GetLegalActions(initial, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Attack
                && (match candidate.Command.Action with
                    | MatchAction.Attack(_, effect) -> effect = EffectId attackId
                    | _ -> false))
            |> Seq.exactlyOne

        drive engine initial action.Command (fun state ->
            state.Phase = MatchPhase.Complete
            || (state.Phase = MatchPhase.Playing
                && state.ActivePlayer <> MatchScenario.FirstPlayer))

    let usesPartyTrick (effect: string) (action: LegalAction) =
        action.Kind = LegalActionKind.UsePartyTrick
        && (match action.Command.Action with
            | MatchAction.UsePartyTrick(_, trickEffect) -> trickEffect = EffectId effect
            | _ -> false)

    let playsKit (kit: CardInstanceId) (action: LegalAction) =
        action.Kind = LegalActionKind.PlayKit
        && (match action.Command.Action with
            | MatchAction.PlayKit(played, _) -> played = kit
            | _ -> false)

    let playsBloke (bloke: CardInstanceId) (action: LegalAction) =
        action.Kind = LegalActionKind.PlayBloke
        && (match action.Command.Action with
            | MatchAction.PlayBloke played -> played = bloke
            | _ -> false)

    let promotes (promotion: CardInstanceId) (promoted: CardInstanceId) (action: LegalAction) =
        action.Kind = LegalActionKind.Promote
        && (match action.Command.Action with
            | MatchAction.Promote(promotionCard, target) ->
                promotionCard = promotion && target = promoted
            | _ -> false)

    /// A deliberately crowded mid-match table: every zone populated, both blokes damaged and rough,
    /// and enough vim attached that no printed attack is priced out.
    let richBattleState (attacker: string) =
        let vim =
            MatchScenario.Authority.BasicVim
            |> Seq.collect (fun card -> Seq.replicate 4 card.Id)
            |> Seq.toArray

        let state = MatchScenario.BattleState attacker "BLK-150" vim 613UL

        let additionalCards =
            [ MatchScenario.PlainCard
                  "own-booth-1"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  -1
              MatchScenario.PlainCard
                  "own-booth-2"
                  "BLK-004"
                  MatchScenario.FirstPlayer
                  CardZone.Booth
                  -1
              MatchScenario.PlainCard
                  "other-booth-1"
                  "BLK-001"
                  MatchScenario.SecondPlayer
                  CardZone.Booth
                  -1
              MatchScenario.PlainCard
                  "other-booth-2"
                  "BLK-004"
                  MatchScenario.SecondPlayer
                  CardZone.Booth
                  -1
              MatchScenario.PlainCard
                  "own-mitt-vim"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "own-mitt-bloke"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "own-mitt-kit"
                  "KIT-012"
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "other-mitt-bloke"
                  "BLK-001"
                  MatchScenario.SecondPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "other-mitt-kit"
                  "KIT-012"
                  MatchScenario.SecondPlayer
                  CardZone.Mitt
                  -1
              MatchScenario.PlainCard
                  "own-stack-bloke"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  0
              MatchScenario.PlainCard
                  "own-stack-vim"
                  "VIM-BLAZED"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  1
              MatchScenario.PlainCard
                  "own-stack-kit"
                  "KIT-006"
                  MatchScenario.FirstPlayer
                  CardZone.Stack
                  2
              MatchScenario.PlainCard
                  "own-empties-bloke"
                  "BLK-001"
                  MatchScenario.FirstPlayer
                  CardZone.EmptiesTray
                  -1
              MatchScenario.PlainCard
                  "own-empties-vim"
                  "VIM-BLAZED"
                  MatchScenario.FirstPlayer
                  CardZone.EmptiesTray
                  -1
              MatchScenario.PlainCard
                  "own-empties-vim-2"
                  "VIM-BLAZED"
                  MatchScenario.FirstPlayer
                  CardZone.EmptiesTray
                  -1
              MatchScenario.PlainCard
                  "own-empties-kit"
                  "KIT-012"
                  MatchScenario.FirstPlayer
                  CardZone.EmptiesTray
                  -1
              MatchScenario.PlainCard "local" "KIT-006" MatchScenario.FirstPlayer CardZone.Local -1
              MatchScenario.AttachedCard
                  "other-vim"
                  "VIM-SOBER"
                  MatchScenario.SecondPlayer
                  CardZone.Attached
                  -1
                  (CardInstanceId "defender") ]

        let roughStates =
            ImmutableArray.Create(MatchScenario.RoughState BlokemonRoughState.DodgyPint 1)

        let attackerCard =
            { state.Card(CardInstanceId "attacker") with
                Damage = 10
                RoughStates = roughStates }

        let defenderCard =
            { state.Card(CardInstanceId "defender") with
                Damage = 10
                Attachments = ImmutableArray.Create(CardInstanceId "other-vim")
                RoughStates = roughStates }

        { state with
            Cards =
                ImmutableArray.CreateRange(
                    state.Cards
                    |> Seq.filter (fun card ->
                        card.Id.Value <> "first-draw"
                        && card.Id.Value <> "attacker"
                        && card.Id.Value <> "defender")
                    |> Seq.append (attackerCard :: defenderCard :: additionalCards)
                    |> Seq.sortBy (fun card -> card.Id)
                )
            RoundUsage =
                { Player = MatchScenario.FirstPlayer
                  VimAttachments = 0
                  MatesPlayed = 1
                  LocalsPlayed = 0
                  TaxisUsed = 0
                  EffectsUsed = ImmutableArray<_>.Empty
                  KitsPlayed = ImmutableArray<_>.Empty } }

    let kitState (kitId: string) =
        let state = richBattleState "BLK-001"

        let kit =
            MatchScenario.PlainCard
                "kit-under-test"
                kitId
                MatchScenario.FirstPlayer
                CardZone.Mitt
                -1

        { state with
            Cards =
                ImmutableArray.CreateRange(
                    state.Cards
                    |> Seq.filter (fun card -> card.Id.Value <> "local")
                    |> Seq.append [ kit ]
                    |> Seq.sortBy (fun card -> card.Id)
                )
            RoundUsage = RoundUsage.Empty MatchScenario.FirstPlayer }

    let promotionState (promotion: BlokemonCollectible) =
        let state =
            match promotion.PromotesFromId with
            | null -> failwith $"{promotion.Id} declares a promotion trigger but no lower stage."
            | promotesFrom -> richBattleState promotesFrom

        addCards
            state
            [ MatchScenario.PlainCard
                  "promotion"
                  promotion.Id
                  MatchScenario.FirstPlayer
                  CardZone.Mitt
                  -1 ]

    let continuousState (cardId: string) =
        let state = richBattleState cardId

        match cardId with
        | "BLK-021" ->
            { state with
                Players =
                    ImmutableArray.CreateRange(
                        state.Players
                        |> Seq.map (fun player ->
                            if player.Id = MatchScenario.FirstPlayer then
                                { player with RoundsStarted = 1 }
                            else
                                player)
                    ) }
        | "BLK-034" ->
            addCards
                state
                [ MatchScenario.PlainCard
                      "named-bloke"
                      "BLK-031"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | "BLK-104" ->
            let boothed =
                { state.Card(CardInstanceId "attacker") with
                    Zone = CardZone.Booth }

            MatchScenario.WithCards
                state
                [ boothed
                  MatchScenario.PlainCard
                      "replacement-active"
                      "BLK-001"
                      MatchScenario.FirstPlayer
                      CardZone.Oche
                      -1
                  MatchScenario.PlainCard
                      "named-booth-bloke"
                      "BLK-105"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | "BLK-122" ->
            let attacker =
                { state.Card(CardInstanceId "attacker") with
                    Attachments = ImmutableArray.Create(CardInstanceId "vim-0") }

            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card ->
                            not (
                                card.Owner = MatchScenario.FirstPlayer
                                && card.Zone = CardZone.Attached
                                && card.Kind = CardKind.Vim
                                && card.Id.Value <> "vim-0"
                            )
                            && card.Id.Value <> "attacker")
                        |> Seq.append [ attacker ]
                        |> Seq.sortBy (fun card -> card.Id)
                    ) }
        | _ -> state

    let describe (mechanicalId: string) (exception': exn) =
        $"{mechanicalId}: {exception'.GetType().Name}: {exception'.Message}"

type FullSetBehaviorTests() =

    [<Test>]
    member _.``an attachment choice should materialise exactly one vim when several are eligible``
        ()
        =
        let bench =
            MatchScenario.PlainCard
                "own-booth"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let firstVim =
            MatchScenario.PlainCard
                "first-discarded-vim"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let secondVim =
            MatchScenario.PlainCard
                "second-discarded-vim"
                "VIM-BLAZED"
                MatchScenario.FirstPlayer
                CardZone.EmptiesTray
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-123" "BLK-150" [ "VIM-BLAZED" ] 607UL)
                [ bench; firstVim; secondVim ]

        let engine = MatchScenario.Engine()

        let action =
            engine.GetLegalActions(state, MatchScenario.FirstPlayer)
            |> Seq.filter (fun candidate ->
                candidate.Kind = LegalActionKind.Attack
                && (match candidate.Command.Action with
                    | MatchAction.Attack(_, effect) -> effect = EffectId "BLK-123-B01"
                    | _ -> false))
            |> Seq.exactlyOne

        let applied = MatchScenario.Applied(engine.Apply(state, action.Command))
        let eligible = [ firstVim.Id; secondVim.Id ]

        eligible
        |> List.filter (fun id -> (applied.Card id).AttachedTo = ValueSome bench.Id)
        |> List.length
        |> should equal 1

        eligible
        |> List.filter (fun id -> (applied.Card id).Zone = CardZone.EmptiesTray)
        |> List.length
        |> should equal 1

    [<Test>]
    member _.``a knockout bonus should be taken on the damage that lands after the soft spot``() =
        let replacement =
            MatchScenario.PlainCard
                "other-booth"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let barChits =
            [ 0..5 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"bar-chit-{index}"
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.BarChit
                    index)

        let state =
            addCards
                (MatchScenario.BattleState
                    "BLK-036"
                    "BLK-067"
                    [ "VIM-GEEKED"; "VIM-GEEKED"; "VIM-GEEKED" ]
                    611UL)
                (replacement :: barChits)

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-036-B02")
            )

        (applied.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 4

    [<Test>]
    member _.``a knockout bonus should not be taken when the defender recovers``() =
        let barChits =
            [ 0..5 ]
            |> List.map (fun index ->
                MatchScenario.PlainCard
                    $"recovery-bar-chit-{index}"
                    "VIM-SOBER"
                    MatchScenario.FirstPlayer
                    CardZone.BarChit
                    index)

        let state =
            MatchScenario.BattleState
                "BLK-036"
                "BLK-068"
                [ "VIM-GEEKED"; "VIM-GEEKED"; "VIM-GEEKED" ]
                (seedForBadge ())

        let defender =
            { state.Card(CardInstanceId "defender") with
                Damage = 150 }

        let state = MatchScenario.WithCards state (defender :: barChits)

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-036-B02")
            )

        (applied.Card(CardInstanceId "defender")).Zone |> should equal CardZone.Oche
        (applied.Player MatchScenario.FirstPlayer).BarChitsRemaining |> should equal 6

    [<Test>]
    member _.``a quadruple soft spot should apply to the defender's printed soft spot``() =
        let abilitySource =
            MatchScenario.PlainCard
                "ability-source"
                "BLK-141"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-001" "BLK-076" [ "VIM-BLAZED"; "VIM-SOBER" ] 617UL)
                [ abilitySource ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 80

    [<Test>]
    member _.``a quadruple soft spot should not apply when the printed soft spot does not match``
        ()
        =
        let abilitySource =
            MatchScenario.PlainCard
                "ability-source"
                "BLK-141"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            addCards
                (MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-SOBER" ] 618UL)
                [ abilitySource ]

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 20

    [<Test>]
    member _.``a quadruple soft spot should apply to a chosen replacement soft spot``() =
        let state =
            MatchScenario.BattleState "BLK-001" "BLK-150" [ "VIM-BLAZED"; "VIM-SOBER" ] 618UL

        let state =
            { state with
                Effects =
                    ImmutableArray.Create(
                        { SourceEffect = EffectId "BLK-137-B01"
                          SourceCard = CardInstanceId "chosen-weakness-source"
                          Owner = MatchScenario.FirstPlayer
                          TargetCard = ValueSome(CardInstanceId "defender")
                          Kind = TemporaryEffectKind.ModifySoftSpot
                          Amount = 1
                          MechanicalTypes = ImmutableArray.Create BlokemonMechanicalType.Grass
                          RoughStates = ImmutableArray<_>.Empty
                          RelatedCards = ImmutableArray<_>.Empty
                          Conditions = ImmutableArray<_>.Empty
                          Duration = EffectDuration.WhileTargetInPlay
                          AppliesFromRound = state.RoundNumber
                          ExpiresAfterRound = state.RoundNumber },
                        { SourceEffect = EffectId "BLK-141-T01"
                          SourceCard = CardInstanceId "quadruple-source"
                          Owner = MatchScenario.FirstPlayer
                          TargetCard = ValueSome(CardInstanceId "defender")
                          Kind = TemporaryEffectKind.ModifySoftSpot
                          Amount = 4
                          MechanicalTypes = ImmutableArray<_>.Empty
                          RoughStates = ImmutableArray<_>.Empty
                          RelatedCards = ImmutableArray<_>.Empty
                          Conditions = ImmutableArray<_>.Empty
                          Duration = EffectDuration.WhileSourceInPlay
                          AppliesFromRound = state.RoundNumber
                          ExpiresAfterRound = state.RoundNumber }
                    ) }

        let applied =
            MatchScenario.Applied(
                MatchScenario.Engine().Apply(state, MatchScenario.AttackCommand state "BLK-001-B01")
            )

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 80

    [<Test>]
    member _.``ability proof should stop an opposing ability from placing damage counters``() =
        let ownBench =
            MatchScenario.PlainCard
                "own-booth"
                "BLK-004"
                MatchScenario.FirstPlayer
                CardZone.Booth
                -1

        let state =
            addCards (MatchScenario.BattleState "BLK-121" "KIT-003" [] 619UL) [ ownBench ]

        let applied = (executeAction state (usesPartyTrick "BLK-121-T01")).State

        (applied.Card(CardInstanceId "defender")).Damage |> should equal 0

    [<Test>]
    member _.``an attack-effect shield should stop an attack from chucking attached vim``() =
        let opposingVim =
            MatchScenario.AttachedCard
                "opposing-vim"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let state =
            MatchScenario.BattleState
                "BLK-023"
                "BLK-014"
                [ "VIM-DODGY"; "VIM-DODGY" ]
                (seedForBadge ())

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = ImmutableArray.Create opposingVim.Id }

        let state = MatchScenario.WithCards state [ defender; opposingVim ]

        let applied = (executeAttack state "BLK-023-B01").State

        (applied.Card opposingVim.Id).AttachedTo
        |> should equal (ValueSome(CardInstanceId "defender"))

    [<Test>]
    member _.``every declared attack should execute deterministically in a rich battle state``() =
        let failures = ResizeArray<string>()

        for card in MatchScenario.Authority.Collectibles do
            for attack in card.Attacks do
                try
                    let state = richBattleState card.Id
                    let first = executeAttack state attack.MechanicalId
                    let repeated = executeAttack state attack.MechanicalId

                    if first <> repeated then
                        failures.Add $"{attack.MechanicalId}: repeated execution diverged"
                with exception' ->
                    failures.Add(describe attack.MechanicalId exception')

        failures |> Seq.toList |> should be Empty

    [<Test>]
    member _.``every activated party trick should execute deterministically in a rich battle state``
        ()
        =
        let failures = ResizeArray<string>()

        for card in MatchScenario.Authority.Collectibles do
            for trick in
                card.PartyTricks
                |> Array.filter (fun trick -> trick.Trigger = BlokemonTrigger.Activated) do
                try
                    let state = richBattleState card.Id

                    // The rich table does not stand every trick up: one whose conditions this
                    // table fails, or whose body has nothing here to work on, is not offered at
                    // all, and there is no execution to be deterministic about. Which tricks
                    // those are, and why, is what ActivatedEffectLegalityTests pins.
                    if offers state (usesPartyTrick trick.MechanicalId) then
                        let first = executeAction state (usesPartyTrick trick.MechanicalId)
                        let repeated = executeAction state (usesPartyTrick trick.MechanicalId)

                        if first <> repeated then
                            failures.Add $"{trick.MechanicalId}: repeated execution diverged"
                with exception' ->
                    failures.Add(describe trick.MechanicalId exception')

        failures |> Seq.toList |> should be Empty

    [<Test>]
    member _.``every kit play should execute deterministically in a rich battle state``() =
        let failures = ResizeArray<string>()
        let kitUnderTest = CardInstanceId "kit-under-test"

        for kit in MatchScenario.Authority.Kits do
            try
                let state = kitState kit.Id
                let first = executeAction state (playsKit kitUnderTest)
                let repeated = executeAction state (playsKit kitUnderTest)

                if first <> repeated then
                    failures.Add $"{kit.Id}: repeated execution diverged"
            with exception' ->
                failures.Add(describe kit.Id exception')

        failures |> Seq.toList |> should be Empty

    [<Test>]
    member _.``every promotion trigger should execute deterministically in a rich battle state``() =
        let failures = ResizeArray<string>()

        let promotesAttacker =
            promotes (CardInstanceId "promotion") (CardInstanceId "attacker")

        for promotion in MatchScenario.Authority.Collectibles do
            for trick in
                promotion.PartyTricks
                |> Array.filter (fun trick -> trick.Trigger = BlokemonTrigger.OnPromotionFromMitt) do
                try
                    let state = promotionState promotion
                    let first = executeAction state promotesAttacker
                    let repeated = executeAction state promotesAttacker

                    if first <> repeated then
                        failures.Add $"{trick.MechanicalId}: repeated execution diverged"
                with exception' ->
                    failures.Add(describe trick.MechanicalId exception')

        failures |> Seq.toList |> should be Empty

    [<Test>]
    member _.``every continuous party trick should refresh deterministically``() =
        let failures = ResizeArray<string>()
        let ownMittBloke = CardInstanceId "own-mitt-bloke"

        let tricks =
            Seq.append
                (MatchScenario.Authority.Collectibles
                 |> Seq.collect (fun card ->
                     card.PartyTricks |> Seq.map (fun trick -> card.Id, trick)))
                (MatchScenario.Authority.Kits
                 |> Seq.collect (fun card ->
                     card.PartyTricks |> Seq.map (fun trick -> card.Id, trick)))
            |> Seq.filter (fun (_, trick) -> trick.Trigger = BlokemonTrigger.Continuous)

        for cardId, trick in tricks do
            try
                let state = continuousState cardId
                let first = executeAction state (playsBloke ownMittBloke)
                let repeated = executeAction state (playsBloke ownMittBloke)

                if first <> repeated then
                    failures.Add $"{trick.MechanicalId}: repeated refresh diverged"
                elif
                    not (
                        first.State.Effects
                        |> Seq.exists (fun effect ->
                            effect.SourceEffect = EffectId trick.MechanicalId)
                    )
                then
                    failures.Add $"{trick.MechanicalId}: no continuous effect was registered"
            with exception' ->
                failures.Add(describe trick.MechanicalId exception')

        failures |> Seq.toList |> should be Empty
