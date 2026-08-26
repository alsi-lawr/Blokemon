namespace Blokemon.Differential.Tests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.ReferenceModel
open FsUnit
open TUnit.Core

[<RequireQualifiedAccess>]
module private CommonScenario =

    let card
        (authority: ReferenceAuthority)
        (id: string)
        (mechanicalId: string)
        (owner: string)
        (zone: string)
        (position: int)
        : CanonicalCard =
        { Id = id
          MechanicalId = mechanicalId
          Owner = owner
          Kind = string authority.Cards[mechanicalId].Kind
          Zone = zone
          IsFaceDown = zone = "BarChit"
          StackPosition = position
          AttachedTo = ""
          Attachments = [||]
          UnderlyingCards = [||]
          Damage = 0
          RoughStates = [||]
          EnteredAtOwnerRound = 1
          LastPromotedRound = -1 }

    let rough (name: string) (round: int) : CanonicalRoughState =
        { State = name
          AppliedAtOwnerRound = round }

    let update (id: string) (change: CanonicalCard -> CanonicalCard) (cards: CanonicalCard array) =
        cards |> Array.map (fun card -> if card.Id = id then change card else card)

    let attach (vim: string) (target: string) (cards: CanonicalCard array) =
        cards
        |> update vim (fun card ->
            { card with
                Zone = "Attached"
                StackPosition = -1
                AttachedTo = target })
        |> update target (fun card ->
            { card with
                Attachments = Array.append card.Attachments [| vim |] })

    let state
        (authority: ReferenceAuthority)
        (id: string)
        (seed: uint64)
        (active: string)
        (roundNumber: int)
        (cards: CanonicalCard array)
        : CanonicalState =
        let players =
            [| "first"; "second" |]
            |> Array.map (fun player ->
                { Id = player
                  BarChitsRemaining =
                    let count =
                        cards
                        |> Array.filter (fun card -> card.Owner = player && card.Zone = "BarChit")
                        |> Array.length

                    if count = 0 then 6 else count
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = [||]
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 2 })

        { MatchId = $"common:{id}"
          AuthorityVersion = authority.ManifestVersion
          Seed = seed
          Random = { State = seed; ConsumptionIndex = 0 }
          Transport =
            { Revision = 0L
              LastEventSequence = 0L
              ProcessedCommandIds = [||] }
          Phase = "Playing"
          OpeningPlayer = "first"
          ActivePlayer = active
          RoundNumber = roundNumber
          Players = players
          Cards = cards |> Array.sortBy _.Id
          Effects = [||]
          RoundUsage =
            { Player = active
              VimAttachments = 0
              MatesPlayed = 0
              LocalsPlayed = 0
              TaxisUsed = 0
              EffectsUsed = [||]
              KitsPlayed = [||] }
          PendingEffect = Canonical.emptyPendingEffect
          PendingKnockout = Canonical.emptyPendingKnockout
          PendingBarChits = [||]
          ReplacementPlayer = ""
          PendingRoundEnd = false
          Terminal =
            { IsComplete = false
              Winner = ""
              SuddenDeathCount = 0 } }

    let selection (actor: string) (key: string) : CommonFoundationSelection =
        { Actor = actor; StableKey = key }

    let transition root id state selections mutation =
        match DifferentialRunner.runCommonScenario root id state selections mutation with
        | CommonEquivalent(steps, finalState) -> steps, finalState
        | CommonDiverged divergence ->
            failwith
                $"{id} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."
        | other -> failwith $"{id} returned {other}."

type CommonFoundationTests() =

    let root = Checkout.repositoryRoot ()
    let authority = ReferenceAuthority.load (Checkout.rawAuthorityPath root)

    let required description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The test mutation could not find {description}."
        | value -> value

    let temporaryJson (node: JsonNode) action =
        let path = Path.Combine(Path.GetTempPath(), $"blokemon-134-{Guid.NewGuid():N}.json")

        try
            File.WriteAllText(path, node.ToJsonString())
            action path
        finally
            File.Delete path

    let barChits owner count mechanicalId =
        Array.init count (fun index ->
            CommonScenario.card
                authority
                $"{owner}-chit-{index}"
                mechanicalId
                owner
                "BarChit"
                index)

    let stack owner id mechanicalId position =
        CommonScenario.card authority id mechanicalId owner "Stack" position

    let playingCards () =
        [| CommonScenario.card authority "F-Oche" "BLK-032" "first" "Oche" -1
           CommonScenario.card authority "F-Booth" "BLK-013" "first" "Booth" 0
           CommonScenario.card authority "F-Vim" "VIM-DODGY" "first" "Mitt" -1
           CommonScenario.card authority "F-Play" "BLK-043" "first" "Mitt" -1
           CommonScenario.card authority "F-Promote" "BLK-033" "first" "Mitt" -1
           CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1
           stack "first" "F-Stack" "VIM-BLAZED" 0
           stack "second" "S-Stack" "VIM-CURRY" 0 |]

    let knockoutCards targetMechanical targetDamage includeBooth firstBarChits secondBarChits =
        let baseCards =
            [| CommonScenario.card authority "F-Attacker" "BLK-051" "first" "Oche" -1
               CommonScenario.card authority "F-Vim-1" "VIM-LAIRY" "first" "Mitt" -1
               CommonScenario.card authority "F-Vim-2" "VIM-LAIRY" "first" "Mitt" -1
               { CommonScenario.card authority "S-Target" targetMechanical "second" "Oche" -1 with
                   Damage = targetDamage }
               stack "first" "F-Stack" "VIM-BLAZED" 0
               stack "second" "S-Stack" "VIM-CURRY" 0 |]
            |> CommonScenario.attach "F-Vim-1" "F-Attacker"
            |> CommonScenario.attach "F-Vim-2" "F-Attacker"

        let booth =
            if includeBooth then
                [| CommonScenario.card authority "S-Booth" "BLK-032" "second" "Booth" 0 |]
            else
                [||]

        Array.concat
            [ baseCards
              booth
              barChits "first" firstBarChits "VIM-BLAZED"
              barChits "second" secondBarChits "VIM-CURRY" ]

    let extraBarChitKnockoutCards () =
        [| CommonScenario.card authority "F-Attacker" "BLK-036" "first" "Oche" -1
           CommonScenario.card authority "F-Vim-1" "VIM-GEEKED" "first" "Mitt" -1
           CommonScenario.card authority "F-Vim-2" "VIM-GEEKED" "first" "Mitt" -1
           CommonScenario.card authority "F-Vim-3" "VIM-GEEKED" "first" "Mitt" -1
           CommonScenario.card authority "S-Target" "BLK-067" "second" "Oche" -1 |]
        |> CommonScenario.attach "F-Vim-1" "F-Attacker"
        |> CommonScenario.attach "F-Vim-2" "F-Attacker"
        |> CommonScenario.attach "F-Vim-3" "F-Attacker"
        |> fun cards ->
            Array.concat [ cards; barChits "first" 3 "VIM-BLAZED"; barChits "second" 3 "VIM-CURRY" ]

    let replaceCard (card: ReferenceCard) (candidate: ReferenceAuthority) =
        { candidate with
            Cards = candidate.Cards |> Map.add card.Id card }

    let legalAttackKeys (candidate: ReferenceAuthority) (attackerMechanicalId: string) =
        let cards =
            extraBarChitKnockoutCards ()
            |> CommonScenario.update "F-Attacker" (fun card ->
                { card with
                    MechanicalId = attackerMechanicalId })

        CommonScenario.state candidate "companion-drift" 62UL "first" 5 cards
        |> fun state ->
            ReferenceCommonFoundation.legalCommonActions candidate NoReferenceMutation state "first"
        |> Array.choose (fun action ->
            if action.Kind = "Attack" then
                Some action.StableKey
            else
                None)

    let rec containsOpcode (opcode: ReferenceOpcode) (instructions: ReferenceInstruction array) =
        instructions
        |> Array.exists (fun instruction ->
            instruction.Opcode = opcode
            || containsOpcode opcode instruction.Then
            || containsOpcode opcode instruction.Otherwise)

    [<Test>]
    member _.``checkup schema drift should fail before common foundation execution``() =
        let authorityNode =
            JsonNode.Parse(File.ReadAllText(Checkout.rawAuthorityPath root))
            |> required "raw authority root"

        let checkup =
            authorityNode["baseRules"]
            |> required "base rules"
            |> fun value -> value["checkup"]
            |> required "checkup"
            |> _.AsObject()

        checkup["unexpectedOrderingRule"] <- JsonValue.Create true

        temporaryJson authorityNode (fun path ->
            (fun () -> ReferenceAuthority.load path |> ignore)
            |> should throw typeof<JsonException>)

    [<Test>]
    member _.``common moves should retain promotion state and resolve printed damage in rule order``
        ()
        =
        let cards =
            playingCards ()
            |> CommonScenario.update "F-Oche" (fun card ->
                { card with
                    Damage = 10
                    RoughStates = [| CommonScenario.rough "Singed" 1 |] })

        let state =
            { CommonScenario.state authority "play-promote-attack" 41UL "first" 3 cards with
                Effects =
                    [| { SourceEffect = "foundation-effect"
                         SourceCard = "F-Oche"
                         Owner = "first"
                         TargetCard = "F-Oche"
                         Kind = "RestrictKit"
                         Amount = 0
                         MechanicalTypes = [||]
                         RoughStates = [||]
                         RelatedCards = [||]
                         Conditions = [||]
                         Duration = "UntilEndOfRound"
                         AppliesFromRound = 3
                         ExpiresAfterRound = 3 } |] }

        let steps, finalState =
            CommonScenario.transition
                root
                "play-promote-attack"
                state
                [| CommonScenario.selection "first" "attach:F-Vim:F-Oche"
                   CommonScenario.selection "first" "play:F-Play"
                   CommonScenario.selection "first" "promote:F-Promote:F-Oche"
                   CommonScenario.selection "first" "attack:F-Promote:BLK-033-B01" |]
                NoReferenceMutation

        steps
        |> Array.forall (fun step -> step.LegalActions.Length > 0)
        |> should equal true

        steps
        |> Array.map _.SelectedAction.Kind
        |> should equal [| "AttachVim"; "PlayBloke"; "Promote"; "Attack" |]

        let promoted = finalState.Cards |> Array.find (fun card -> card.Id = "F-Promote")
        let underlying = finalState.Cards |> Array.find (fun card -> card.Id = "F-Oche")
        let vim = finalState.Cards |> Array.find (fun card -> card.Id = "F-Vim")
        let defender = finalState.Cards |> Array.find (fun card -> card.Id = "S-Oche")

        promoted.Zone |> should equal "Oche"
        promoted.Damage |> should equal 10
        promoted.Attachments |> should equal [| "F-Vim" |]
        promoted.UnderlyingCards |> should equal [| "F-Oche" |]
        promoted.RoughStates |> should equal [||]
        underlying.Zone |> should equal "Attached"
        vim.AttachedTo |> should equal "F-Promote"
        finalState.Effects |> should equal [||]
        defender.Damage |> should equal 30
        finalState.ActivePlayer |> should equal "second"

        steps[3].Events
        |> Array.map _.Kind
        |> should
            equal
            [| "CommandApplied"
               "AttackDeclared"
               "DamagePlaced"
               "RoundEnded"
               "RoundStarted"
               "CardMoved"
               "CardsDrawn"
               "StateCommitted" |]

    [<Test>]
    member _.``taxi should expose short fares and retain only the printed post-move state``() =
        let cards =
            [| { CommonScenario.card authority "F-Oche" "BLK-013" "first" "Oche" -1 with
                   Damage = 20
                   RoughStates =
                       [| CommonScenario.rough "Singed" 1; CommonScenario.rough "DodgyPint" 1 |] }
               CommonScenario.card authority "F-Vim" "VIM-BLAZED" "first" "Mitt" -1
               CommonScenario.card authority "F-Booth" "BLK-032" "first" "Booth" 0
               CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1 |]
            |> CommonScenario.attach "F-Vim" "F-Oche"

        let baseState = CommonScenario.state authority "taxi" 19UL "first" 4 cards

        let shortState =
            { baseState with
                Cards =
                    baseState.Cards
                    |> CommonScenario.update "F-Vim" (fun card ->
                        { card with
                            Zone = "Mitt"
                            AttachedTo = "" })
                    |> CommonScenario.update "F-Oche" (fun card ->
                        { card with
                            Attachments = [||]
                            RoughStates = [||] }) }

        let shortLegal =
            ReferenceCommonFoundation.legalCommonActions
                authority
                NoReferenceMutation
                shortState
                "first"
            |> Array.find (fun action -> action.Kind = "Taxi")

        shortLegal.Affordability |> should equal "ShortOfTaxiFare:1"

        let state =
            { baseState with
                Effects =
                    [| { SourceEffect = "taxi-effect"
                         SourceCard = "F-Oche"
                         Owner = "first"
                         TargetCard = "F-Oche"
                         Kind = "RestrictKit"
                         Amount = 0
                         MechanicalTypes = [||]
                         RoughStates = [||]
                         RelatedCards = [||]
                         Conditions = [||]
                         Duration = "UntilEndOfRound"
                         AppliesFromRound = 4
                         ExpiresAfterRound = 4 } |] }

        let steps, finalState =
            CommonScenario.transition
                root
                "taxi"
                state
                [| CommonScenario.selection "first" "taxi:F-Booth" |]
                NoReferenceMutation

        steps[0].SelectedAction.Affordability |> should equal "Payable"

        (finalState.Cards |> Array.find (fun card -> card.Id = "F-Vim")).Zone
        |> should equal "EmptiesTray"

        let outgoing = finalState.Cards |> Array.find (fun card -> card.Id = "F-Oche")
        outgoing.Zone |> should equal "Booth"
        outgoing.Damage |> should equal 20
        outgoing.RoughStates |> should equal [||]
        finalState.Effects |> should equal [||]

        (finalState.Cards |> Array.find (fun card -> card.Id = "F-Booth")).Zone
        |> should equal "Oche"

        finalState.RoundUsage.TaxisUsed |> should equal 1

    [<Test>]
    member _.``fossil lifecycle prerequisite should compare chuck and replacement publicly``() =
        let cards =
            [| CommonScenario.card authority "F-Fossil" "KIT-001" "first" "Oche" -1
               CommonScenario.card authority "F-Vim" "VIM-BLAZED" "first" "Mitt" -1
               CommonScenario.card authority "F-Booth" "BLK-013" "first" "Booth" 0
               CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1 |]
            |> CommonScenario.attach "F-Vim" "F-Fossil"

        let state = CommonScenario.state authority "fossil-replacement" 23UL "first" 3 cards

        let steps, finalState =
            CommonScenario.transition
                root
                "fossil-replacement"
                state
                [| CommonScenario.selection "first" "chuck:F-Fossil"
                   CommonScenario.selection "first" "replacement:F-Booth" |]
                NoReferenceMutation

        steps[0].LegalActions
        |> Array.map (fun action -> action.Kind, action.StableKey)
        |> should equal [| "ChuckFossil", "chuck:F-Fossil"; "EndRound", "end"; "Resign", "resign" |]

        steps[0].State.Phase |> should equal "AwaitingReplacement"
        steps[0].State.ReplacementPlayer |> should equal "first"
        steps[0].State.Effects |> should equal [||]
        steps[0].State.PendingEffect |> should equal Canonical.emptyPendingEffect
        steps[0].State.PendingKnockout |> should equal Canonical.emptyPendingKnockout
        steps[0].State.PendingBarChits |> should equal [||]
        steps[0].State.Random |> should equal state.Random
        steps[0].Rejection |> should equal [||]

        steps[0].Events
        |> Array.map _.Kind
        |> should
            equal
            [| "CommandApplied"; "EffectRegistered"; "EffectRegistered"; "StateCommitted" |]

        steps[1].LegalActions
        |> Array.map (fun action -> action.Kind, action.StableKey)
        |> should equal [| "ChooseReplacement", "replacement:F-Booth"; "Resign", "resign" |]

        steps[1].Events
        |> Array.map _.Kind
        |> should equal [| "CommandApplied"; "CardMoved"; "StateCommitted" |]

        steps[1].Rejection |> should equal [||]

        (finalState.Cards |> Array.find (fun card -> card.Id = "F-Fossil")).Zone
        |> should equal "EmptiesTray"

        (finalState.Cards |> Array.find (fun card -> card.Id = "F-Vim")).Zone
        |> should equal "EmptiesTray"

        (finalState.Cards |> Array.find (fun card -> card.Id = "F-Booth")).Zone
        |> should equal "Oche"

        finalState.Phase |> should equal "Playing"
        finalState.ReplacementPlayer |> should equal ""
        finalState.Random |> should equal state.Random

    [<Test>]
    member _.``attack knockout should award a normal chit then finish after replacement``() =
        let cards = knockoutCards "BLK-013" 0 true 3 3
        let state = CommonScenario.state authority "normal-knockout" 31UL "first" 5 cards

        let steps, finalState =
            CommonScenario.transition
                root
                "normal-knockout"
                state
                [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02"
                   CommonScenario.selection "second" "replacement:S-Booth" |]
                NoReferenceMutation

        steps[0].State.Phase |> should equal "AwaitingReplacement"
        steps[0].State.PendingRoundEnd |> should equal true

        (steps[0].State.Players |> Array.find (fun player -> player.Id = "first")).BarChitsRemaining
        |> should equal 2

        (steps[0].State.Cards |> Array.find (fun card -> card.Id = "S-Target")).Zone
        |> should equal "EmptiesTray"

        finalState.ActivePlayer |> should equal "second"
        finalState.PendingRoundEnd |> should equal false

    [<Test>]
    member _.``big hitter knockout should award two chits and resolve the winner``() =
        let cards = knockoutCards "BLK-065" 280 false 3 3
        let state = CommonScenario.state authority "big-hitter-win" 37UL "first" 6 cards

        let steps, finalState =
            CommonScenario.transition
                root
                "big-hitter-win"
                state
                [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02" |]
                NoReferenceMutation

        (finalState.Players |> Array.find (fun player -> player.Id = "first")).BarChitsRemaining
        |> should equal 1

        finalState.Terminal.IsComplete |> should equal true
        finalState.Terminal.Winner |> should equal "first"

        steps[0].Events
        |> Array.filter (fun event -> event.Kind = "BarChitsTaken")
        |> Array.exactlyOne
        |> _.Amount
        |> should equal 2

    [<Test>]
    member _.``one method each should enter sudden death before the remaining method wins``() =
        let cards = knockoutCards "BLK-013" 0 false 2 0
        let initial = CommonScenario.state authority "sudden-death" 43UL "first" 7 cards

        let state =
            { initial with
                Players =
                    initial.Players
                    |> Array.map (fun player ->
                        if player.Id = "second" then
                            { player with BarChitsRemaining = 0 }
                        else
                            player) }

        let steps, finalState =
            CommonScenario.transition
                root
                "sudden-death"
                state
                [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02" |]
                NoReferenceMutation

        finalState.Terminal.SuddenDeathCount |> should equal 1
        finalState.Terminal.IsComplete |> should equal true
        finalState.Terminal.Winner |> should equal "first"

        let kinds = steps[0].Events |> Array.map _.Kind

        Array.findIndex ((=) "SuddenDeathStarted") kinds
        |> should be (lessThan (Array.findIndex ((=) "RoundEnded") kinds))

        Array.findIndex ((=) "RoundEnded") kinds
        |> should be (lessThan (Array.findIndex ((=) "MatchWon") kinds))

        finalState.Random.ConsumptionIndex > 0 |> should equal true

    [<Test>]
    member _.``checkup should finish both knockouts before ordered replacement choices``() =
        let cards =
            Array.concat
                [ [| { CommonScenario.card authority "F-Oche" "BLK-013" "first" "Oche" -1 with
                         Damage = 40
                         RoughStates = [| CommonScenario.rough "DodgyPint" 1 |] }
                     CommonScenario.card authority "F-Booth" "BLK-032" "first" "Booth" 0
                     { CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1 with
                         Damage = 40
                         RoughStates = [| CommonScenario.rough "DodgyPint" 1 |] }
                     CommonScenario.card authority "S-Booth" "BLK-043" "second" "Booth" 0
                     stack "first" "F-Stack" "VIM-BLAZED" 0
                     stack "second" "S-Stack" "VIM-CURRY" 0 |]
                  barChits "first" 3 "VIM-BLAZED"
                  barChits "second" 3 "VIM-CURRY" ]

        let steps, finalState =
            CommonScenario.transition
                root
                "checkup-knockouts"
                (CommonScenario.state authority "checkup-knockouts" 47UL "first" 8 cards)
                [| CommonScenario.selection "first" "end"
                   CommonScenario.selection "first" "replacement:F-Booth"
                   CommonScenario.selection "second" "replacement:S-Booth" |]
                NoReferenceMutation

        steps[0].State.ReplacementPlayer |> should equal "first"

        steps[0].Events
        |> Array.filter (fun event -> event.Kind = "BlokeSentHome")
        |> Array.map _.SourceCard
        |> should equal [| "F-Oche"; "S-Oche" |]

        steps[1].State.ReplacementPlayer |> should equal "second"
        finalState.ReplacementPlayer |> should equal ""
        finalState.ActivePlayer |> should equal "second"

    [<Test>]
    member _.``pending data should suspend base moves and resignation should clear every queue``() =
        let state =
            CommonScenario.state authority "pending" 53UL "first" 4 (playingCards ())

        let suspendedAction =
            ReferenceEngine.submittedAction
                state
                "pending:command"
                "first"
                state.Transport.Revision
                "EndRound"
                "end"
            |> fun action ->
                { action with
                    Affordability = "Submitted" }

        let pending =
            { Canonical.emptyPendingEffect with
                Present = true
                Action = [| suspendedAction |]
                Source = "F-Oche"
                Effect = "pending-effect"
                Chooser = "second" }

        let suspended = ReferenceCommonFoundation.suspendForEffect pending state

        let queued =
            ReferenceCommonFoundation.queueBarChit
                { Player = "first"
                  Card = "F-Stack"
                  Effect = "pending-bar"
                  FinishRoundAfterResolution = true }
                suspended

        queued.PendingBarChits |> Array.map _.Card |> should equal [| "F-Stack" |]

        let steps, finalState =
            CommonScenario.transition
                root
                "pending"
                suspended
                [| CommonScenario.selection "first" "resign" |]
                NoReferenceMutation

        steps[0].LegalActions |> Array.map _.Kind |> should equal [| "Resign" |]
        finalState.PendingEffect |> should equal Canonical.emptyPendingEffect
        finalState.PendingKnockout |> should equal Canonical.emptyPendingKnockout
        finalState.PendingBarChits |> should equal [||]
        finalState.Terminal.Winner |> should equal "second"

    [<Test>]
    member _.``explicit invalid base commands should preserve state and exact rejection``() =
        let baseState =
            CommonScenario.state authority "rejections" 59UL "first" 3 (playingCards ())

        let atLimit =
            { baseState with
                RoundUsage =
                    { baseState.RoundUsage with
                        VimAttachments = authority.BaseRules.Vim.NormalAttachmentPerRound } }

        let firstRound =
            { baseState with
                Players =
                    baseState.Players
                    |> Array.map (fun player ->
                        if player.Id = "first" then
                            { player with RoundsStarted = 1 }
                        else
                            player) }

        let cases =
            [| "attach-limit", atLimit, "AttachVim", "vim=F-Vim;target=F-Oche", "RuleLimitReached"
               "promotion-first-round",
               firstRound,
               "Promote",
               "promotion=F-Promote;promoted=F-Oche",
               "IneligiblePromotion"
               "attack-unpaid",
               baseState,
               "Attack",
               "attacker=F-Oche;effect=BLK-032-B01",
               "InsufficientVim"
               "taxi-unpaid", baseState, "Taxi", "booth=F-Booth;vim=", "InvalidTaxiFare"
               "replacement-phase",
               baseState,
               "ChooseReplacement",
               "replacement=F-Booth",
               "WrongPhase"
               "opponent-bloke", baseState, "PlayBloke", "bloke=S-Oche", "CardNotOwned"
               "non-fossil", baseState, "ChuckFossil", "fossil=F-Oche", "EffectUnavailable" |]

        for id, state, kind, payload, code in cases do
            let submitted =
                ReferenceEngine.submittedAction
                    state
                    $"reject:{id}"
                    "first"
                    state.Transport.Revision
                    kind
                    payload

            match DifferentialRunner.runCommonRejection root id state submitted with
            | CommonRejectionEquivalent transition ->
                transition.State |> should equal state
                transition.Events |> should equal [||]
                transition.Rejection |> Array.exactlyOne |> _.Code |> should equal code
            | CommonDiverged divergence ->
                failwith
                    $"{id} diverged at {divergence.Stage}. Reference: {divergence.ReferenceFact}. Production: {divergence.ProductionFact}."
            | other -> failwith $"{id} returned {other}."

    [<Test>]
    member _.``BLK-036-B02 support prerequisite should compare extra knockout Bar Chit without 136 137 or 138 route credit``
        ()
        =
        let state =
            CommonScenario.state
                authority
                "BLK-036-B02-extra-award"
                61UL
                "first"
                5
                (extraBarChitKnockoutCards ())

        let steps, finalState =
            CommonScenario.transition
                root
                "BLK-036-B02-extra-award"
                state
                [| CommonScenario.selection "first" "attack:F-Attacker:BLK-036-B02" |]
                NoReferenceMutation

        steps[0].LegalActions
        |> Array.map (fun action -> action.Kind, action.StableKey)
        |> should
            equal
            [| "Attack", "attack:F-Attacker:BLK-036-B01"
               "Attack", "attack:F-Attacker:BLK-036-B02"
               "EndRound", "end"
               "Resign", "resign" |]

        let companionOwner =
            authority.Cards
            |> Map.toSeq
            |> Seq.map snd
            |> Seq.filter (fun card ->
                card.Attacks
                |> Array.exists (fun attack ->
                    match attack.Program with
                    | [| instruction |] -> instruction.Opcode = ReferenceOpcode.SwapOche
                    | _ -> false)
                && card.Attacks
                   |> Array.exists (fun attack ->
                       containsOpcode ReferenceOpcode.TakeExtraBarChit attack.Program))
            |> Seq.exactlyOne

        let companion =
            companionOwner.Attacks
            |> Array.find (fun attack ->
                match attack.Program with
                | [| instruction |] -> instruction.Opcode = ReferenceOpcode.SwapOche
                | _ -> false)

        let sibling =
            companionOwner.Attacks
            |> Array.find (fun attack ->
                attack.Program
                |> Array.exists (fun instruction ->
                    instruction.Opcode = ReferenceOpcode.Conditional))

        legalAttackKeys authority companionOwner.Id
        |> should equal [| "attack:F-Attacker:BLK-036-B01"; "attack:F-Attacker:BLK-036-B02" |]

        let otherOwner =
            authority.Cards
            |> Map.toSeq
            |> Seq.map snd
            |> Seq.find (fun card ->
                card.Id <> companionOwner.Id
                && card.Kind = ReferenceCardKind.Bloke
                && card.Attacks.Length > 0
                && (card.Attacks
                    |> Array.forall (fun attack ->
                        not (containsOpcode ReferenceOpcode.TakeExtraBarChit attack.Program))))

        let movedCompanionAuthority =
            authority
            |> replaceCard
                { companionOwner with
                    Attacks = [| sibling |] }
            |> replaceCard
                { otherOwner with
                    Attacks = Array.append otherOwner.Attacks [| companion |] }

        legalAttackKeys movedCompanionAuthority otherOwner.Id
        |> Array.contains "attack:F-Attacker:BLK-036-B01"
        |> should equal false

        let conditional = sibling.Program[1]
        let extraBarChit = conditional.Then[0]

        let changedSibling =
            { sibling with
                Program =
                    [| sibling.Program[0]
                       { conditional with
                           Then = [| { extraBarChit with Amount = 0 } |] } |] }

        authority
        |> replaceCard
            { companionOwner with
                Attacks = [| companion; changedSibling |] }
        |> fun candidate -> legalAttackKeys candidate companionOwner.Id
        |> Array.contains "attack:F-Attacker:BLK-036-B01"
        |> should equal false

        let changedCompanion =
            { companion with
                Program =
                    [| { companion.Program[0] with
                           TargetCount = 2 } |] }

        authority
        |> replaceCard
            { companionOwner with
                Attacks = [| changedCompanion; sibling |] }
        |> fun candidate -> legalAttackKeys candidate companionOwner.Id
        |> Array.contains "attack:F-Attacker:BLK-036-B01"
        |> should equal false

        steps[0].SelectedAction.Payload
        |> should equal "attacker=F-Attacker;effect=BLK-036-B02"

        steps[0].Rejection |> should equal [||]
        finalState.PendingEffect |> should equal Canonical.emptyPendingEffect
        finalState.PendingKnockout |> should equal Canonical.emptyPendingKnockout
        finalState.PendingBarChits |> should equal [||]
        finalState.ReplacementPlayer |> should equal ""
        finalState.PendingRoundEnd |> should equal false
        finalState.Random |> should equal state.Random
        finalState.Terminal.IsComplete |> should equal true
        finalState.Terminal.Winner |> should equal "first"

        (finalState.Cards |> Array.find (fun card -> card.Id = "S-Target")).Zone
        |> should equal "EmptiesTray"

        (finalState.Cards |> Array.find (fun card -> card.Id = "S-Target")).Damage
        |> should equal 100

        (finalState.Players |> Array.find (fun player -> player.Id = "first")).BarChitsRemaining
        |> should equal 1

        steps[0].Events
        |> Array.map _.Kind
        |> should
            equal
            [| "CommandApplied"
               "AttackDeclared"
               "DamagePlaced"
               "BlokeSentHome"
               "CardMoved"
               "BarChitsTaken"
               "CardMoved"
               "BarChitsTaken"
               "MatchWon"
               "StateCommitted" |]

        steps[0].Events
        |> Array.filter (fun event -> event.Kind = "BarChitsTaken")
        |> Array.map (fun event -> event.SourceCard, event.Amount)
        |> should equal [| "S-Target", 1; "F-Attacker", 1 |]

        (DifferentialRunner.bootstrap root).ProgramRouteAcceptance
        |> Set.count
        |> should equal 0

    [<Test>]
    member _.``empty-stack required-draw support prerequisite should lose through the public round action``
        ()
        =
        let cards =
            Array.concat
                [ [| CommonScenario.card authority "F-Oche" "BLK-013" "first" "Oche" -1
                     CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1 |]
                  barChits "first" 3 "VIM-BLAZED"
                  barChits "second" 3 "VIM-CURRY" ]

        let state =
            CommonScenario.state authority "empty-required-draw" 63UL "first" 5 cards

        let steps, finalState =
            CommonScenario.transition
                root
                "empty-required-draw"
                state
                [| CommonScenario.selection "first" "end" |]
                NoReferenceMutation

        steps[0].LegalActions
        |> Array.map (fun action -> action.Kind, action.StableKey)
        |> should equal [| "EndRound", "end"; "Resign", "resign" |]

        steps[0].Rejection |> should equal [||]
        finalState.ActivePlayer |> should equal "second"
        finalState.RoundNumber |> should equal 6
        finalState.PendingEffect |> should equal Canonical.emptyPendingEffect
        finalState.PendingKnockout |> should equal Canonical.emptyPendingKnockout
        finalState.PendingBarChits |> should equal [||]
        finalState.ReplacementPlayer |> should equal ""
        finalState.PendingRoundEnd |> should equal false
        finalState.Random |> should equal state.Random
        finalState.Terminal.IsComplete |> should equal true
        finalState.Terminal.Winner |> should equal "first"

        steps[0].Events
        |> Array.map _.Kind
        |> should
            equal
            [| "CommandApplied"
               "RoundEnded"
               "RoundStarted"
               "MatchWon"
               "StateCommitted" |]

        steps[0].Events
        |> Array.exists (fun event -> event.DrawReason = "RequiredRoundDraw")
        |> should equal false

    [<Test>]
    member _.``targeted foundation mutants should fail at their owned ordering boundary``() =
        let damageCards =
            [| CommonScenario.card authority "F-Attacker" "BLK-013" "first" "Oche" -1
               CommonScenario.card authority "F-Vim-1" "VIM-BLAZED" "first" "Mitt" -1
               CommonScenario.card authority "F-Vim-2" "VIM-CURRY" "first" "Mitt" -1
               CommonScenario.card authority "S-Target" "BLK-050" "second" "Oche" -1
               stack "second" "S-Stack" "VIM-CURRY" 0 |]
            |> CommonScenario.attach "F-Vim-1" "F-Attacker"
            |> CommonScenario.attach "F-Vim-2" "F-Attacker"

        let damageState =
            CommonScenario.state authority "damage-mutant" 67UL "first" 4 damageCards

        let damageMove =
            [| CommonScenario.selection "first" "attack:F-Attacker:BLK-013-B02" |]

        let checkupCards =
            Array.concat
                [ [| { CommonScenario.card authority "F-Oche" "BLK-013" "first" "Oche" -1 with
                         Damage = 40
                         RoughStates = [| CommonScenario.rough "DodgyPint" 1 |] }
                     CommonScenario.card authority "F-Booth" "BLK-032" "first" "Booth" 0
                     { CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1 with
                         Damage = 40
                         RoughStates = [| CommonScenario.rough "DodgyPint" 1 |] }
                     CommonScenario.card authority "S-Booth" "BLK-043" "second" "Booth" 0 |]
                  barChits "first" 3 "VIM-BLAZED"
                  barChits "second" 3 "VIM-CURRY" ]

        let checkupState =
            CommonScenario.state authority "knockout-mutant" 71UL "first" 5 checkupCards

        let knockoutState =
            CommonScenario.state
                authority
                "award-mutant"
                73UL
                "first"
                5
                (knockoutCards "BLK-013" 0 true 3 3)

        let winnerState =
            CommonScenario.state
                authority
                "winner-mutant"
                79UL
                "first"
                5
                (knockoutCards "BLK-065" 280 false 3 3)

        let roundCards =
            [| { CommonScenario.card authority "F-Oche" "BLK-013" "first" "Oche" -1 with
                   RoughStates = [| CommonScenario.rough "DodgyPint" 1 |] }
               CommonScenario.card authority "S-Oche" "BLK-050" "second" "Oche" -1
               stack "second" "S-Stack" "VIM-CURRY" 0 |]

        let roundState =
            CommonScenario.state authority "round-mutant" 83UL "first" 5 roundCards

        let pendingState =
            let state =
                CommonScenario.state authority "pending-mutant" 89UL "first" 5 (playingCards ())

            ReferenceCommonFoundation.suspendForEffect
                { Canonical.emptyPendingEffect with
                    Present = true
                    Action =
                        [| ReferenceEngine.submittedAction
                               state
                               "pending:mutant"
                               "first"
                               0L
                               "EndRound"
                               "end"
                           |> fun action ->
                               { action with
                                   Affordability = "Submitted" } |]
                    Source = "F-Oche"
                    Effect = "pending-effect"
                    Chooser = "second" }
                state

        let cases =
            [| "damage", damageState, damageMove, SkipDamageModifiers
               "knockout-order",
               checkupState,
               [| CommonScenario.selection "first" "end" |],
               ReverseKnockoutOrder
               "award",
               knockoutState,
               [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02" |],
               SkipBarChitAward
               "replacement",
               knockoutState,
               [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02" |],
               SkipReplacementAssignment
               "win",
               winnerState,
               [| CommonScenario.selection "first" "attack:F-Attacker:BLK-051-B02" |],
               ForceSuddenDeathForWinner
               "pending",
               pendingState,
               [| CommonScenario.selection "first" "resign" |],
               AllowBaseActionWhilePending
               "round-order",
               roundState,
               [| CommonScenario.selection "first" "end" |],
               StartNextRoundBeforeCheckup |]

        for id, state, moves, mutation in cases do
            match DifferentialRunner.runCommonScenario root id state moves mutation with
            | CommonDiverged divergence ->
                (divergence.Stage.StartsWith("legal-actions", StringComparison.Ordinal)
                 || divergence.Stage.StartsWith("transition", StringComparison.Ordinal))
                |> should equal true
            | result -> failwith $"The targeted {id} mutant survived: {result}."
