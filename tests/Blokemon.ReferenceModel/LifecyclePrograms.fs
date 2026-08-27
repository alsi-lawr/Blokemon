namespace Blokemon.ReferenceModel

open System

type ReferenceLifecycleMutation =
    | NoLifecycleMutation
    | SkipLifecycleUsage
    | SkipContinuousEffectRegistration

type ReferenceLifecycleObligation =
    { Input: ReferenceObligationInput
      InitialState: CanonicalState }

[<RequireQualifiedAccess>]
module ReferenceLifecyclePrograms =

    let acceptedRoutes =
        Set
            [ "activated-decline"
              "activated-trigger"
              "activated-unavailable"
              "continuous-refresh"
              "kit-condition"
              "local-decline"
              "local-trigger"
              "play-kit"
              "promotion-decline" ]

    let acceptedObligationIds =
        Set
            [ "kit-kit-013-predicate-true"
              "kit-kit-013-predicate-false"
              "kit-kit-014-predicate-true"
              "kit-kit-014-predicate-false"
              "continuous-blk-009-t01-applies"
              "continuous-blk-014-t01-applies"
              "continuous-blk-027-t01-applies"
              "continuous-blk-141-t01-applies"
              "continuous-blk-149-t01-applies"
              "continuous-kit-001-t01-applies"
              "continuous-kit-003-t01-applies"
              "continuous-blk-021-t01-true"
              "continuous-blk-021-t01-false"
              "continuous-blk-034-t01-true"
              "continuous-blk-034-t01-false"
              "continuous-blk-104-t01-true"
              "continuous-blk-104-t01-false"
              "continuous-blk-122-t01-true"
              "continuous-blk-122-t01-false"
              "continuous-blk-139-t01-true"
              "continuous-blk-139-t01-false"
              "continuous-blk-144-t01-true"
              "continuous-blk-144-t01-false"
              "continuous-blk-145-t01-true"
              "continuous-blk-145-t01-false"
              "continuous-blk-146-t01-true"
              "continuous-blk-146-t01-false"
              "continuous-kit-002-t01-true"
              "continuous-kit-002-t01-false"
              "play-kit-001-r01"
              "play-kit-002-r01"
              "play-kit-003-r01"
              "play-kit-004-r01"
              "play-kit-007-r01"
              "play-kit-012-r01"
              "activated-decline-blk-003-t01"
              "activated-decline-blk-041-t01"
              "activated-decline-blk-053-t01"
              "activated-decline-blk-085-t01"
              "activated-decline-blk-121-t01"
              "activated-decline-blk-132-t01"
              "activated-decline-blk-143-t01"
              "activated-decline-blk-151-t01"
              "play-kit-005-r01-boundary"
              "play-kit-008-r01-boundary"
              "play-kit-009-r01-boundary"
              "play-kit-010-r01-boundary"
              "play-kit-011-r01-boundary"
              "promotion-decline-blk-044-t01"
              "promotion-decline-blk-045-t01"
              "promotion-decline-blk-093-t01"
              "promotion-decline-blk-097-t01"
              "local-decline-kit-006-r01"
              "golf-club-gary-finds-mate"
              "steve-first-round-replaces-himself"
              "howard-heals-sixty"
              "balaclava-reveals-opponent-mitt"
              "marathon-draws-and-takes-counter"
              "fish-lips-places-two-and-chucks-self"
              "big-dave-recovers-bar-kit"
              "the-lads-refill-empty-mitt"
              "eight-card-audition-selects-zero"
              "eight-card-audition-takes-eight"
              "pub-trade-chucks-one-and-draws-one"
              "badge-side-lift-attaches-basic-vim"
              "continuous-blk-009-blk-009-t01-out-of-play-nonfire"
              "continuous-blk-014-blk-014-t01-out-of-play-nonfire"
              "continuous-blk-027-blk-027-t01-out-of-play-nonfire"
              "continuous-blk-141-blk-141-t01-out-of-play-nonfire"
              "continuous-blk-149-blk-149-t01-out-of-play-nonfire"
              "continuous-kit-001-kit-001-t01-out-of-play-nonfire"
              "continuous-kit-003-kit-003-t01-out-of-play-nonfire"
              "activated-unavailable-blk-003-booth"
              "activated-unavailable-blk-041-booth"
              "activated-unavailable-blk-132-booth-first-round"
              "activated-unavailable-blk-132-later-round" ]

    [<Literal>]
    let AcceptedRouteCount = 9

    [<Literal>]
    let AcceptedObligationCount = 76

    let private actionOrder = ReferenceEngine.actionOrder
    let private action = ReferenceEngine.action

    let private containsOpcode (opcode: ReferenceOpcode) (program: ReferenceInstruction array) =
        let rec contains (values: ReferenceInstruction array) =
            values
            |> Array.exists (fun instruction ->
                instruction.Opcode = opcode
                || contains instruction.Then
                || contains instruction.Otherwise)

        contains program

    let private isDeclarativeHouseRule (rule: ReferenceHouseRule) =
        let rec flatten (program: ReferenceInstruction array) =
            program
            |> Array.collect (fun instruction ->
                Array.concat
                    [ [| instruction |]; flatten instruction.Then; flatten instruction.Otherwise ])

        flatten rule.Program
        |> Array.forall (fun instruction ->
            instruction.Opcode = ReferenceOpcode.Conditional
            || instruction.Opcode = ReferenceOpcode.ContinuousPartyTrick)

    let private executableHouseRules (definition: ReferenceCard) =
        definition.HouseRules
        |> Array.filter (fun rule ->
            not (containsOpcode ReferenceOpcode.OncePerRound rule.Program)
            && not (isDeclarativeHouseRule rule))

    let private card
        (authority: ReferenceAuthority)
        (id: string)
        (mechanicalId: string)
        (owner: string)
        (zone: string)
        (position: int)
        : CanonicalCard =
        let definition =
            authority.Cards.TryFind mechanicalId
            |> Option.defaultWith (fun () ->
                invalidOp $"The lifecycle setup names unknown card {mechanicalId}.")

        { Id = id
          MechanicalId = mechanicalId
          Owner = owner
          Kind = string definition.Kind
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

    let private replaceCard (value: CanonicalCard) (cards: CanonicalCard array) =
        cards
        |> Array.filter (fun current -> current.Id <> value.Id)
        |> Array.append [| value |]

    let private updateCard
        (id: string)
        (change: CanonicalCard -> CanonicalCard)
        (cards: CanonicalCard array)
        =
        cards |> Array.map (fun value -> if value.Id = id then change value else value)

    let private attachCard (vim: string) (target: string) (cards: CanonicalCard array) =
        cards
        |> updateCard vim (fun value ->
            { value with
                Zone = "Attached"
                StackPosition = -1
                AttachedTo = target })
        |> updateCard target (fun value ->
            { value with
                Attachments = Array.append value.Attachments [| vim |] })

    let private basicVimFor
        (authority: ReferenceAuthority)
        (mechanicalType: ReferenceMechanicalType)
        =
        authority.Cards
        |> Seq.map _.Value
        |> Seq.filter (fun value -> value.Kind = ReferenceCardKind.Vim)
        |> Seq.find (fun value -> value.VimType = ValueSome mechanicalType)
        |> _.Id

    let private lifecycleState (authority: ReferenceAuthority) (input: ReferenceObligationInput) =
        let route = input.InitialState.Route.Value
        let parameters = input.InitialState.Parameters

        let attackerMechanicalId, defenderMechanicalId =
            match route with
            | "promotion-decline"
            | "promotion-trigger" -> parameters[2], "BLK-001"
            | "play-kit" -> "BLK-003", "BLK-001"
            | "local-decline"
            | "local-trigger" -> "BLK-001", "BLK-150"
            | "kit-condition" -> parameters[2], "BLK-001"
            | _ -> parameters[0], "BLK-001"

        let mutable cards =
            [| card authority "attacker" attackerMechanicalId "first" "Oche" -1
               card authority "defender" defenderMechanicalId "second" "Oche" -1
               card
                   authority
                   "first-draw"
                   (basicVimFor authority ReferenceMechanicalType.Fire)
                   "first"
                   "Stack"
                   0
               card
                   authority
                   "second-draw"
                   (basicVimFor authority ReferenceMechanicalType.Water)
                   "second"
                   "Stack"
                   0 |]

        let add owner id mechanicalId zone position =
            cards <- replaceCard (card authority id mechanicalId owner zone position) cards

        let attach id target = cards <- attachCard id target cards

        let mutable firstRounds = 2
        let mutable firstBarChits = 6

        match route with
        | "continuous-refresh" ->
            let setup = parameters[2]

            let vimType =
                match setup with
                | "Water" -> Some ReferenceMechanicalType.Water
                | "Lightning" -> Some ReferenceMechanicalType.Lightning
                | "Fire" -> Some ReferenceMechanicalType.Fire
                | "unequal" -> Some ReferenceMechanicalType.Water
                | _ -> None

            match vimType with
            | Some mechanicalType ->
                add "first" "vim-0" (basicVimFor authority mechanicalType) "Mitt" -1
                attach "vim-0" "attacker"
            | None -> ()

            let moveSourceToBooth named =
                cards <- updateCard "attacker" (fun value -> { value with Zone = "Booth" }) cards

                add "first" "own-oche" named "Oche" -1

            match setup with
            | "booth" -> moveSourceToBooth "BLK-001"
            | "out-of-play" ->
                cards <- updateCard "attacker" (fun value -> { value with Zone = "Mitt" }) cards

                add "first" "own-oche" "BLK-001" "Oche" -1
            | "no-vim-self" ->
                add
                    "first"
                    "vim-sentinel"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "Mitt"
                    -1
            | value when value.StartsWith("booth-named-", StringComparison.Ordinal) ->
                moveSourceToBooth value["booth-named-".Length ..]
            | value when value.StartsWith("named-", StringComparison.Ordinal) ->
                add "first" "named-condition" value["named-".Length ..] "Booth" -1
            | "first-round" -> firstRounds <- 1
            | _ -> ()
        | "activated-decline"
        | "activated-trigger" ->
            let setup = parameters[2]
            let candidate = parameters[3]

            match setup with
            | "damaged-self" ->
                cards <- updateCard "attacker" (fun value -> { value with Damage = 60 }) cards
            | "opponent-mitt" -> add "second" candidate "BLK-004" "Mitt" -1
            | "stack-kit" -> add "first" candidate "KIT-010" "Stack" 1
            | "stack-bloke-first-round" ->
                add "first" candidate "BLK-001" "Stack" 1
                firstRounds <- 1
            | "empties-kit" ->
                add "first" candidate "KIT-012" "EmptiesTray" -1

                if route = "activated-trigger" then
                    add "first" "candidate-2" "KIT-012" "EmptiesTray" -1
            | "three-card-draw" ->
                add "first" "mitt-sentinel" "KIT-001" "Mitt" -1

                add
                    "first"
                    "effect-draw-1"
                    (basicVimFor authority ReferenceMechanicalType.Fire)
                    "Stack"
                    1

                add
                    "first"
                    "effect-draw-2"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "Stack"
                    2
            | "with-own-booth" -> add "first" "own-booth" "BLK-004" "Booth" -1
            | "fixed-draw-sentinel" -> add "first" "mitt-sentinel" "KIT-001" "Mitt" -1
            | "default" -> ()
            | unknown -> invalidOp $"Unknown lifecycle activation setup {unknown}."
        | "activated-unavailable" ->
            let setup = parameters[2]

            if setup = "booth" || setup = "booth-first-round" then
                cards <- updateCard "attacker" (fun value -> { value with Zone = "Booth" }) cards

                add "first" "own-oche" "BLK-001" "Oche" -1

            if setup = "booth-first-round" then
                firstRounds <- 1
        | "promotion-decline"
        | "promotion-trigger" ->
            add "first" "promotion" parameters[0] "Mitt" -1

            if route = "promotion-decline" then
                add
                    "first"
                    "retained-vim"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "Mitt"
                    -1

                attach "retained-vim" "attacker"

                cards <-
                    updateCard
                        "attacker"
                        (fun value ->
                            { value with
                                Damage = 20
                                RoughStates =
                                    [| { State = "DodgyPint"
                                         AppliedAtOwnerRound = 1 } |] })
                        cards
            else
                for index in 1 .. (if parameters[0] = "BLK-045" then 8 else 4) do
                    add
                        "first"
                        $"top-{index}"
                        (basicVimFor authority ReferenceMechanicalType.Fire)
                        "Stack"
                        index

                if parameters[0] = "BLK-093" then
                    add "second" "opponent-supporter" "KIT-005" "EmptiesTray" -1
        | "local-decline"
        | "local-trigger" ->
            add "first" "local-under-test" "KIT-006" "Local" -1

            add "first" "mitt-vim" (basicVimFor authority ReferenceMechanicalType.Water) "Mitt" -1

            if route = "local-trigger" then
                add "first" "mitt-sentinel" "KIT-001" "Mitt" -1

                add
                    "first"
                    "effect-draw-2"
                    (basicVimFor authority ReferenceMechanicalType.Fire)
                    "Stack"
                    1

                add
                    "first"
                    "effect-draw-3"
                    (basicVimFor authority ReferenceMechanicalType.Lightning)
                    "Stack"
                    2
        | "play-kit" ->
            let kit = parameters[0]
            let mode = parameters[2]
            add "first" "kit-under-test" kit "Mitt" -1

            match kit with
            | "KIT-007" ->
                add
                    "first"
                    "effect-draw-1"
                    (basicVimFor authority ReferenceMechanicalType.Fire)
                    "Stack"
                    1

                add
                    "first"
                    "prize"
                    (basicVimFor authority ReferenceMechanicalType.Lightning)
                    "BarChit"
                    0

                firstBarChits <- 1
            | "KIT-009" -> add "second" "other-mitt-bloke" "BLK-004" "Mitt" -1
            | "KIT-010" ->
                add
                    "second"
                    "other-vim"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "Mitt"
                    -1

                add "first" "own-vim" (basicVimFor authority ReferenceMechanicalType.Fire) "Mitt" -1

                attach "other-vim" "defender"
            | "KIT-011" -> add "second" "other-mitt-bloke" "BLK-004" "Mitt" -1
            | "KIT-008" when mode = "badge" ->
                add "first" "own-booth" "BLK-004" "Booth" -1

                add
                    "first"
                    "candidate"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "EmptiesTray"
                    -1
            | "KIT-005" when mode.StartsWith("search-", StringComparison.Ordinal) ->
                cards <-
                    updateCard
                        "first-draw"
                        (fun value ->
                            { value with
                                MechanicalId = "BLK-001"
                                Kind = "Bloke" })
                        cards

                for index in 1..7 do
                    add "first" $"top-{index}" "BLK-001" "Stack" index
            | _ -> ()
        | "kit-condition" ->
            add "first" "kit-under-test" parameters[0] "Mitt" -1

            add
                "second"
                "other-vim-0"
                (basicVimFor authority ReferenceMechanicalType.Grass)
                "Mitt"
                -1

            add
                "second"
                "other-vim-1"
                (basicVimFor authority ReferenceMechanicalType.Water)
                "Mitt"
                -1

            attach "other-vim-0" "defender"
            attach "other-vim-1" "defender"
        | unknown -> invalidOp $"Unowned lifecycle route {unknown}."

        for explicitCard in input.InitialState.Cards do
            add
                explicitCard.Owner
                explicitCard.CardId
                explicitCard.MechanicalId
                (string explicitCard.Zone)
                -1

        for count in input.InitialState.ZoneCounts do
            for index in 0 .. count.Count - 1 do
                add
                    count.Owner
                    $"input-{count.Owner}-{count.Zone}-{index}"
                    (basicVimFor authority ReferenceMechanicalType.Grass)
                    (string count.Zone)
                    index

        let playerInputs =
            input.InitialState.Players
            |> Seq.map (fun value -> value.Player, value)
            |> Map.ofSeq

        let players =
            [| "first"; "second" |]
            |> Array.map (fun id ->
                { Id = id
                  BarChitsRemaining =
                    playerInputs.TryFind id
                    |> Option.map _.BarChitsRemaining
                    |> Option.defaultValue (if id = "first" then firstBarChits else 6)
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = [||]
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = if id = "first" then firstRounds else 2 })

        { MatchId = $"obligation:{input.Id}"
          AuthorityVersion = authority.ManifestVersion
          Seed = input.RandomSeed
          Random =
            { State = input.RandomSeed
              ConsumptionIndex = 0 }
          Transport =
            { Revision = 0L
              LastEventSequence = 0L
              ProcessedCommandIds = [||] }
          Phase = "Playing"
          OpeningPlayer = "second"
          ActivePlayer = "first"
          RoundNumber = 4
          Players = players
          Cards = cards |> Array.sortBy _.Id
          Effects = [||]
          RoundUsage =
            { Player = "first"
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

    let reconcile (inventory: ReferenceObligationInventory) =
        let selected =
            inventory.Obligations
            |> Array.filter (fun value -> acceptedRoutes.Contains value.InitialState.Route.Value)

        let selectedRoutes = selected |> Seq.map _.InitialState.Route.Value |> Set.ofSeq
        let selectedIds = selected |> Seq.map _.Id |> Set.ofSeq

        let duplicateIds =
            selected
            |> Seq.countBy _.Id
            |> Seq.choose (fun (id, count) -> if count = 1 then None else Some id)
            |> Seq.toArray

        if selected.Length <> AcceptedObligationCount then
            invalidOp
                $"The BLOKEMON-137 ledger contains {selected.Length} obligations instead of {AcceptedObligationCount}."

        if selectedRoutes <> acceptedRoutes || selectedRoutes.Count <> AcceptedRouteCount then
            invalidOp "The BLOKEMON-137 route ledger is missing, stale, duplicated, or borrowed."

        if
            selectedIds <> acceptedObligationIds
            || acceptedObligationIds.Count <> AcceptedObligationCount
        then
            invalidOp
                "The BLOKEMON-137 obligation ledger is missing, stale, duplicated, or borrowed."

        if duplicateIds.Length <> 0 then
            invalidOp
                $"The BLOKEMON-137 obligation ledger contains duplicate identities: {String.Join(',', duplicateIds)}."

        selected

    let materialize authority inventory =
        reconcile inventory
        |> Array.map (fun input ->
            { Input = input
              InitialState = lifecycleState authority input })

    let materializeInput authority input = lifecycleState authority input

    let private otherPlayer (state: CanonicalState) (actor: string) =
        ReferenceState.otherPlayer state actor

    let private predicateAllowsActivation
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (source: CanonicalCard)
        (predicate: ReferencePredicate)
        =
        match predicate.Condition with
        | ReferenceCondition.Optional -> true
        | ReferenceCondition.SelfIsAtOche -> source.Zone = "Oche"
        | ReferenceCondition.SelfIsInBooth -> source.Zone = "Booth"
        | ReferenceCondition.SelfHasDamage -> source.Damage > 0
        | ReferenceCondition.SelfHasVim -> source.Attachments.Length > 0
        | ReferenceCondition.SelfHasRoughState ->
            predicate.RoughState
            |> ValueOption.exists (fun rough ->
                source.RoughStates |> Array.exists (fun value -> value.State = string rough))
        | ReferenceCondition.OwnersFirstRound ->
            (ReferenceState.player state source.Owner).RoundsStarted = 1
        | ReferenceCondition.OwnMittIsEmpty ->
            ReferenceState.cardsIn state source.Owner "Mitt" |> Array.isEmpty
        | ReferenceCondition.NamedBlokeInPlay ->
            predicate.RelatedId
            |> ValueOption.exists (fun id ->
                state.Cards
                |> Array.exists (fun value ->
                    value.Owner = source.Owner
                    && value.MechanicalId = id
                    && ReferenceDeterministicPrograms.inPlay value))
        | ReferenceCondition.NamedBlokeInBooth ->
            predicate.RelatedId
            |> ValueOption.exists (fun id ->
                ReferenceState.cardsIn state source.Owner "Booth"
                |> Array.exists (fun value -> value.MechanicalId = id))
        | ReferenceCondition.OpenedSecond -> state.OpeningPlayer <> source.Owner
        | ReferenceCondition.AttachedVimCountsAreEqual ->
            let other = otherPlayer state source.Owner

            let otherCount =
                ReferenceState.cardsIn state other "Oche"
                |> Array.tryHead
                |> Option.map _.Attachments.Length
                |> Option.defaultValue 0

            source.Attachments.Length = otherCount
        | _ -> true

    let private activationCanAct
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (source: CanonicalCard)
        (effect: string)
        (program: ReferenceInstruction array)
        (isHouseRule: bool)
        =
        let rec instructionCanAct (instruction: ReferenceInstruction) =
            match instruction.Opcode with
            | ReferenceOpcode.Conditional ->
                let passed =
                    instruction.Predicates
                    |> Array.forall (predicateAllowsActivation authority state source)

                if passed then
                    programCanAct instruction.Then
                else
                    programCanAct instruction.Otherwise
            | ReferenceOpcode.OncePerRound -> false
            | ReferenceOpcode.HealDamage ->
                ReferenceDeterministicPrograms.candidatesFor
                    authority
                    state
                    source.Owner
                    source
                    instruction
                |> Array.exists (fun value -> value.Damage > 0)
            | ReferenceOpcode.DrawFromStack
            | ReferenceOpcode.SearchStack
            | ReferenceOpcode.RevealCards
            | ReferenceOpcode.MoveCards
            | ReferenceOpcode.ChuckCards
            | ReferenceOpcode.AttachVim
            | ReferenceOpcode.MoveVim
            | ReferenceOpcode.ChuckVim
            | ReferenceOpcode.SwapOche
            | ReferenceOpcode.PlaceDamageCounters ->
                ReferenceDeterministicPrograms.candidatesFor
                    authority
                    state
                    source.Owner
                    source
                    instruction
                |> Array.isEmpty
                |> not
            | ReferenceOpcode.ChuckSelf -> not isHouseRule
            | _ -> true

        and programCanAct instructions =
            instructions |> Array.exists instructionCanAct

        not (Array.contains effect state.RoundUsage.EffectsUsed)
        && programCanAct program

    let private partyTrickActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        state.Cards
        |> Array.filter (fun source ->
            source.Owner = actor && ReferenceDeterministicPrograms.inPlay source)
        |> Array.collect (fun source ->
            authority.Cards[source.MechanicalId].PartyTricks
            |> Array.filter (fun (trick: ReferencePartyTrick) ->
                trick.Trigger = ReferenceTrigger.Activated
                && activationCanAct authority state source trick.MechanicalId trick.Program false)
            |> Array.map (fun (trick: ReferencePartyTrick) ->
                let key = $"trick:{source.Id}:{trick.MechanicalId}"

                action
                    state
                    "UsePartyTrick"
                    actor
                    key
                    key
                    $"source={source.Id};effect={trick.MechanicalId}"
                    (ReferenceDeterministicPrograms.requirementsForProgram
                        authority
                        state
                        actor
                        source
                        trick.MechanicalId
                        "root/"
                        trick.Program)
                    [||]))

    let private localActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        state.Cards
        |> Array.filter (fun source -> source.Kind = "Kit" && source.Zone = "Local")
        |> Array.collect (fun source ->
            authority.Cards[source.MechanicalId].HouseRules
            |> Array.filter (fun (rule: ReferenceHouseRule) ->
                containsOpcode ReferenceOpcode.OncePerRound rule.Program
                && activationCanAct authority state source rule.MechanicalId rule.Program true)
            |> Array.map (fun (rule: ReferenceHouseRule) ->
                let key = $"local:{source.Id}:{rule.MechanicalId}"

                action
                    state
                    "UsePartyTrick"
                    actor
                    key
                    key
                    $"source={source.Id};effect={rule.MechanicalId}"
                    (ReferenceDeterministicPrograms.requirementsForProgram
                        authority
                        state
                        actor
                        source
                        rule.MechanicalId
                        "root/"
                        rule.Program)
                    [||]))

    let private kitRestricted (state: CanonicalState) (actor: string) (kind: ReferenceKitKind) =
        state.Effects
        |> Array.exists (fun (effect: CanonicalTemporaryEffect) ->
            effect.Owner <> actor
            && ((effect.Kind = "RestrictKit" && kind = ReferenceKitKind.BarBit)
                || (effect.Kind = "RestrictLocal" && kind = ReferenceKitKind.Local)))

    let private kitCategoryAllows
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (kit: ReferenceCard)
        (target: CanonicalCard option)
        =
        let kind = kit.KitKind |> ValueOption.get

        if kitRestricted state actor kind then
            false
        else
            match kind with
            | ReferenceKitKind.BarBit -> true
            | ReferenceKitKind.Mate ->
                state.RoundUsage.MatesPlayed < authority.BaseRules.Kit.MatesPerRound
                && (authority.BaseRules.Opening.OpeningParticipantMayPlayMate
                    || actor <> state.OpeningPlayer
                    || (ReferenceState.player state actor).RoundsStarted <> 1)
            | ReferenceKitKind.Local ->
                if state.RoundUsage.LocalsPlayed >= authority.BaseRules.Kit.LocalsPerRound then
                    false
                else
                    match ReferenceState.cardsIn state actor "Local" |> Array.tryExactlyOne with
                    | Some current when
                        authority.BaseRules.Kit.SameMechanicalLocalCannotReplace
                        && current.MechanicalId = kit.Id
                        ->
                        false
                    | Some _ when
                        authority.BaseRules.Kit.OneLocalInPlay
                        && not authority.BaseRules.Kit.NewLocalChucksOld
                        ->
                        false
                    | _ -> true
            | ReferenceKitKind.BarKit ->
                match target with
                | None -> false
                | Some target when
                    target.Owner <> actor || not (ReferenceDeterministicPrograms.inPlay target)
                    ->
                    false
                | Some target ->
                    target.Attachments
                    |> Array.map (ReferenceState.card state)
                    |> Array.filter (fun attachment ->
                        attachment.Kind = "Kit"
                        && authority.Cards[attachment.MechanicalId].KitKind = ValueSome
                            ReferenceKitKind.BarKit)
                    |> Array.length
                    |> (>) authority.BaseRules.Kit.BarKitsPerBloke
            | other -> invalidOp $"Unsupported lifecycle kit kind {other}."

    let private playKitActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        ReferenceState.cardsIn state actor "Mitt"
        |> Array.filter (fun value -> value.Kind = "Kit")
        |> Array.collect (fun (kitCard: CanonicalCard) ->
            let definition = authority.Cards[kitCard.MechanicalId]
            let kind = definition.KitKind |> ValueOption.get

            let targets =
                if kind = ReferenceKitKind.BarKit then
                    state.Cards
                    |> Array.filter (fun value ->
                        value.Owner = actor && ReferenceDeterministicPrograms.inPlay value)
                    |> Array.map Some
                else
                    [| None |]

            let requirements =
                executableHouseRules definition
                |> Array.collect (fun (rule: ReferenceHouseRule) ->
                    ReferenceDeterministicPrograms.requirementsForProgram
                        authority
                        state
                        actor
                        kitCard
                        rule.MechanicalId
                        "root/"
                        rule.Program)
                |> Array.distinctBy _.Id

            targets
            |> Array.filter (kitCategoryAllows authority state actor definition)
            |> Array.map (fun target ->
                let targetId = target |> Option.map _.Id |> Option.defaultValue ""
                let suffix = if String.IsNullOrEmpty targetId then "none" else targetId
                let key = $"kit:{kitCard.Id}:{suffix}"

                action
                    state
                    "PlayKit"
                    actor
                    key
                    key
                    $"kit={kitCard.Id};target={targetId}"
                    requirements
                    [||]))

    let private promotionEligible
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (promotion: CanonicalCard)
        (target: CanonicalCard)
        =
        let player = ReferenceState.player state actor
        let rules = authority.BaseRules.Promotion

        let firstRoundException =
            authority.Cards[target.MechanicalId].PartyTricks
            |> Array.filter (fun (trick: ReferencePartyTrick) ->
                trick.Trigger = ReferenceTrigger.Continuous)
            |> Array.filter (fun (trick: ReferencePartyTrick) ->
                let rec has condition instructions =
                    instructions
                    |> Array.exists (fun instruction ->
                        (instruction.Predicates
                         |> Array.exists (fun predicate -> predicate.Condition = condition))
                        || has condition instruction.Then
                        || has condition instruction.Otherwise)

                has ReferenceCondition.OpenedSecond trick.Program
                && has ReferenceCondition.OwnersFirstRound trick.Program)
            |> Array.exists (fun (trick: ReferencePartyTrick) ->
                state.Effects
                |> Array.exists (fun effect ->
                    effect.SourceEffect = trick.MechanicalId
                    && effect.SourceCard = target.Id
                    && effect.Kind = "ContinuousPartyTrick"))

        promotion.Owner = actor
        && target.Owner = actor
        && promotion.Kind = "Bloke"
        && promotion.Zone = "Mitt"
        && ReferenceDeterministicPrograms.inPlay target
        && (firstRoundException
            || ((not rules.NotOnEitherFirstRound || player.RoundsStarted > 1)
                && (not rules.NotFirstRoundInPlay
                    || target.EnteredAtOwnerRound <> player.RoundsStarted)))
        && (not rules.NotTwiceInRound || target.LastPromotedRound <> state.RoundNumber)
        && (not rules.ExactMechanicalEdgeRequired
            || authority.Cards[promotion.MechanicalId].PromotesFromId = ValueSome
                target.MechanicalId)

    let private projectPromotion
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (promotion: CanonicalCard)
        (target: CanonicalCard)
        =
        let rules = authority.BaseRules.Promotion

        let cards =
            state.Cards
            |> Array.map (fun (card: CanonicalCard) ->
                if card.Id = target.Id then
                    { card with
                        Zone = "Attached"
                        AttachedTo = promotion.Id
                        Attachments = [||]
                        RoughStates = [||] }
                elif card.Id = promotion.Id then
                    { card with
                        Zone = target.Zone
                        StackPosition = target.StackPosition
                        Damage =
                            if rules.RetainDamageAndAttachedCards then
                                target.Damage
                            else
                                0
                        Attachments =
                            if rules.RetainDamageAndAttachedCards then
                                target.Attachments
                            else
                                [||]
                        UnderlyingCards = Array.append target.UnderlyingCards [| target.Id |]
                        RoughStates =
                            if rules.ClearRoughStatesAndAttackEffects then
                                [||]
                            else
                                target.RoughStates
                        EnteredAtOwnerRound = target.EnteredAtOwnerRound
                        LastPromotedRound = state.RoundNumber }
                elif Array.contains card.Id target.Attachments then
                    if rules.RetainDamageAndAttachedCards then
                        { card with AttachedTo = promotion.Id }
                    else
                        { card with
                            Zone = "EmptiesTray"
                            AttachedTo = "" }
                else
                    card)

        { state with Cards = cards }

    let private projectedRequirements
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (source: CanonicalCard)
        (effect: string)
        (program: ReferenceInstruction array)
        =
        let requirements = ResizeArray<CanonicalChoiceRequirement>()

        let rec inspect parent optionalDependency (instructions: ReferenceInstruction array) =
            for index in 0 .. instructions.Length - 1 do
                let instruction = instructions[index]
                let path = $"{parent}{index}"

                let own =
                    ReferenceDeterministicPrograms.instructionRequirements
                        authority
                        state
                        actor
                        source
                        effect
                        path
                        instruction

                let dependency =
                    own
                    |> Array.tryFind (fun value -> value.Kind = "Optional")
                    |> Option.map _.Id
                    |> Option.defaultValue optionalDependency

                for requirement in own do
                    requirements.Add
                        { requirement with
                            DependsOnOptional =
                                if requirement.Kind = "Optional" then
                                    optionalDependency
                                else
                                    dependency }

                inspect $"{path}/then/" dependency instruction.Then
                inspect $"{path}/otherwise/" optionalDependency instruction.Otherwise

        inspect "root/" "" program
        requirements.ToArray() |> Array.distinctBy _.Id

    let private promoteActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let targets =
            state.Cards
            |> Array.filter (fun value ->
                value.Owner = actor && ReferenceDeterministicPrograms.inPlay value)

        ReferenceState.cardsIn state actor "Mitt"
        |> Array.filter (fun value -> value.Kind = "Bloke")
        |> Array.collect (fun (promotion: CanonicalCard) ->
            targets
            |> Array.filter (promotionEligible authority state actor promotion)
            |> Array.map (fun (target: CanonicalCard) ->
                let projected = projectPromotion authority state promotion target

                let requirements =
                    authority.Cards[promotion.MechanicalId].PartyTricks
                    |> Array.filter (fun (trick: ReferencePartyTrick) ->
                        trick.Trigger = ReferenceTrigger.OnPromotionFromMitt)
                    |> Array.collect (fun (trick: ReferencePartyTrick) ->
                        projectedRequirements
                            authority
                            projected
                            actor
                            (ReferenceState.card projected promotion.Id)
                            trick.MechanicalId
                            trick.Program)
                    |> Array.distinctBy _.Id

                let key = $"promote:{promotion.Id}:{target.Id}"

                action
                    state
                    "Promote"
                    actor
                    key
                    key
                    $"promotion={promotion.Id};promoted={target.Id}"
                    requirements
                    [||]))

    let private supportedKinds =
        Set [ "Promote"; "PlayKit"; "UsePartyTrick"; "Attack"; "EndRound" ]

    let legalActions (authority: ReferenceAuthority) (state: CanonicalState) (actor: string) =
        if state.PendingEffect.Present then
            ReferenceDeterministicPrograms.legalResolutionAction state actor
        elif state.Phase <> "Playing" || state.ActivePlayer <> actor then
            [||]
        else
            let refreshed, _ =
                ReferenceCommonFoundation.refreshContinuousEffects authority state

            let inherited =
                ReferenceDeterministicPrograms.legalActions authority refreshed actor
                |> Array.filter (fun value ->
                    supportedKinds.Contains value.Kind && value.Kind <> "Promote")

            Array.concat
                [ inherited
                  promoteActions authority refreshed actor
                  playKitActions authority refreshed actor
                  partyTrickActions authority refreshed actor
                  localActions authority refreshed actor ]
            |> Array.sortWith (fun left right ->
                let byKind = compare actionOrder[left.Kind] actionOrder[right.Kind]

                if byKind <> 0 then
                    byKind
                else
                    String.CompareOrdinal(left.StableKey, right.StableKey))

    let private inputChoices (selected: CanonicalAction) (input: ReferenceActionInput) =
        let requirementIds = selected.Requirements |> Seq.map _.Id |> Set.ofSeq

        input.Choices
        |> Array.filter (fun value ->
            (not value.WhenAvailable || requirementIds.Contains value.RequirementId)
            && (selected.Requirements
                |> Array.exists (fun requirement ->
                    requirement.Id = value.RequirementId && requirement.Chooser = input.Actor)))
        |> Array.map ReferenceDeterministicPrograms.canonicalChoice

    let selectAction
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (input: ReferenceActionInput)
        (commandIndex: int)
        =
        let legal = legalActions authority state input.Actor

        let stableKey =
            match input.Kind with
            | ReferenceInputActionKind.Attack -> $"attack:{input.SourceCard}:{input.EffectId}"
            | ReferenceInputActionKind.EndRound -> "end"
            | ReferenceInputActionKind.UsePartyTrick ->
                let prefix =
                    match ReferenceState.tryCard state input.SourceCard with
                    | Some source when source.Zone = "Local" -> "local"
                    | _ -> "trick"

                $"{prefix}:{input.SourceCard}:{input.EffectId}"
            | ReferenceInputActionKind.Promote -> $"promote:{input.SourceCard}:{input.TargetCard}"
            | ReferenceInputActionKind.PlayKit ->
                let target =
                    if String.IsNullOrEmpty input.TargetCard then
                        "none"
                    else
                        input.TargetCard

                $"kit:{input.SourceCard}:{target}"
            | other -> invalidOp $"Unsupported lifecycle input action {other}."

        let selected =
            legal
            |> Array.tryFind (fun value -> value.StableKey = stableKey)
            |> Option.defaultWith (fun () ->
                invalidOp
                    $"The lifecycle selector could not select {stableKey} from its independently derived legal actions.")

        { selected with
            CommandId = $"obligation:{commandIndex}"
            StableKey = ""
            Choices = inputChoices selected input }

    let legalResolutionAction (state: CanonicalState) (actor: string) =
        ReferenceDeterministicPrograms.legalResolutionAction state actor

    let selectResolution (state: CanonicalState) (input: ReferenceActionInput) (commandIndex: int) =
        ReferenceDeterministicPrograms.selectResolution state input commandIndex

    let private rejection
        (state: CanonicalState)
        (code: string)
        (requirements: CanonicalChoiceRequirement array)
        : CanonicalTransition =
        { State = state
          Events = [||]
          Rejection =
            [| { Code = code
                 ChoiceRequirements = requirements } |] }

    let private commit
        (state: CanonicalState)
        (selected: CanonicalAction)
        (refreshEvents: CanonicalEvent array)
        (semanticEvents: CanonicalEvent array)
        : CanonicalTransition =
        let submitted =
            { ReferenceEvents.create "CommandApplied" with
                Actor = selected.Actor
                Transport =
                    { Canonical.emptyEventTransport with
                        HasCommand = true } }

        let beforeCommit =
            { state with
                Transport =
                    { state.Transport with
                        ProcessedCommandIds =
                            Array.append
                                state.Transport.ProcessedCommandIds
                                [| selected.CommandId |] } }

        let committed, events =
            ReferenceEvents.commit
                (selected.ExpectedRevision + 1L)
                beforeCommit
                (Array.concat [ [| submitted |]; refreshEvents; semanticEvents ])

        { State = committed
          Events = events
          Rejection = [||] }

    let private boundary
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        : string option =
        if selected.MatchId <> state.MatchId then
            Some "WrongMatch"
        elif Array.contains selected.CommandId state.Transport.ProcessedCommandIds then
            Some "DuplicateCommand"
        elif selected.ExpectedRevision <> state.Transport.Revision then
            Some "StaleRevision"
        elif state.AuthorityVersion <> authority.ManifestVersion then
            Some "AuthorityMismatch"
        elif not (state.Players |> Array.exists (fun value -> value.Id = selected.Actor)) then
            Some "UnknownActor"
        elif state.Phase = "Complete" then
            Some "MatchComplete"
        else
            None

    let private programFor
        (authority: ReferenceAuthority)
        (source: CanonicalCard)
        (effect: string)
        : ReferenceInstruction array * bool =
        let definition = authority.Cards[source.MechanicalId]

        match
            definition.PartyTricks
            |> Array.tryFind (fun value -> value.MechanicalId = effect)
        with
        | Some trick -> trick.Program, false
        | None ->
            definition.HouseRules
            |> Array.tryFind (fun value -> value.MechanicalId = effect)
            |> Option.map (fun rule -> rule.Program, true)
            |> Option.defaultWith (fun () ->
                invalidOp $"The lifecycle source {source.Id} has no effect {effect}.")

    let private prepareKit
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        : CanonicalState * CanonicalEvent array * ReferenceLifecycleMutation =
        let parts = selected.Payload.Split(';')
        let kitId = parts[0].Substring(4)
        let targetId = parts[1].Substring(7)
        let kitCard = ReferenceState.card state kitId
        let definition = authority.Cards[kitCard.MechanicalId]
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        match definition.KitKind with
        | ValueSome ReferenceKitKind.BarKit ->
            let target = ReferenceState.card next targetId
            let moved, movedEvent = ReferenceEngine.moveCard kitId "Attached" targetId next

            next <-
                ReferenceState.updateCard
                    { ReferenceState.card moved targetId with
                        Attachments = Array.append target.Attachments [| kitId |] }
                    moved

            events.Add movedEvent
        | ValueSome ReferenceKitKind.Local ->
            match ReferenceState.cardsIn next selected.Actor "Local" |> Array.tryExactlyOne with
            | Some current when authority.BaseRules.Kit.NewLocalChucksOld ->
                let moved, movedEvent = ReferenceEngine.moveCard current.Id "EmptiesTray" "" next

                next <- moved
                events.Add movedEvent
            | _ -> ()

            let moved, movedEvent = ReferenceEngine.moveCard kitId "Local" "" next
            next <- moved
            events.Add movedEvent
        | ValueSome ReferenceKitKind.BarBit
        | ValueSome ReferenceKitKind.Mate -> ()
        | ValueSome other -> invalidOp $"Unsupported lifecycle kit kind {other}."
        | ValueNone -> ()

        next, events.ToArray(), mutation

    let private finishKit
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (selected: CanonicalAction)
        (state: CanonicalState)
        (events: CanonicalEvent array)
        : CanonicalState * CanonicalEvent array =
        let parts = selected.Payload.Split(';')
        let kitId = parts[0].Substring(4)
        let kit = ReferenceState.card state kitId
        let definition = authority.Cards[kit.MechanicalId]
        let mutable next = state
        let semanticEvents = ResizeArray<CanonicalEvent>(events)

        if kit.Zone = "Mitt" then
            let moved, movedEvent = ReferenceEngine.moveCard kitId "EmptiesTray" "" next
            next <- moved
            semanticEvents.Add movedEvent

        if mutation <> SkipLifecycleUsage then
            let usage = next.RoundUsage

            next <-
                { next with
                    RoundUsage =
                        { usage with
                            KitsPlayed = Array.append usage.KitsPlayed [| kit.MechanicalId |]
                            MatesPlayed =
                                usage.MatesPlayed
                                + (if definition.KitKind = ValueSome ReferenceKitKind.Mate then
                                       1
                                   else
                                       0)
                            LocalsPlayed =
                                usage.LocalsPlayed
                                + (if definition.KitKind = ValueSome ReferenceKitKind.Local then
                                       1
                                   else
                                       0) } }

        next, semanticEvents.ToArray()

    let private settleVoluntarySource
        (sourceBefore: CanonicalCard)
        (state: CanonicalState)
        : CanonicalState =
        match ReferenceState.tryCard state sourceBefore.Id with
        | Some sourceAfter when
            sourceBefore.Zone = "Oche"
            && not (ReferenceDeterministicPrograms.inPlay sourceAfter)
            && not (ReferenceState.cardsIn state sourceBefore.Owner "Booth" |> Array.isEmpty)
            ->
            { state with
                Phase = "AwaitingReplacement"
                ReplacementPlayer = sourceBefore.Owner }
        | _ -> state

    let private applyProgram
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        (source: CanonicalCard)
        (effect: string)
        (program: ReferenceInstruction array)
        (isHouseRule: bool)
        (isKit: bool)
        (refreshEvents: CanonicalEvent array)
        : CanonicalTransition =
        match
            ReferenceDeterministicPrograms.validateOwnedChoices
                authority
                selected.Actor
                selected.Choices
                selected.Requirements
        with
        | Error(code, requirements) -> rejection state code requirements
        | Ok() ->
            let (planned, plannedEvents, _, _, deferred, beerMats, plannedRejection, _) =
                ReferenceDeterministicPrograms.executeStandaloneProgram
                    authority
                    NoProgramMutation
                    selected
                    selected.Actor
                    source
                    effect
                    program
                    isHouseRule
                    [||]
                    state

            match plannedRejection with
            | Some(code, requirements) -> rejection state code requirements
            | None when deferred.Length <> 0 ->
                let chooser = deferred |> Array.map _.Chooser |> Array.distinct |> Array.exactlyOne

                let suspended =
                    ReferenceCommonFoundation.suspendForEffect
                        { Canonical.emptyPendingEffect with
                            Present = true
                            Action =
                                [| { selected with
                                       Affordability = "Submitted"
                                       Requirements = [||] } |]
                            Source = source.Id
                            Effect = effect
                            Chooser = chooser
                            Requirements = deferred
                            BeerMatResults = beerMats
                            AttackStarted = false }
                        { state with Random = planned.Random }

                let requested =
                    { ReferenceEvents.create "EffectChoiceRequested" with
                        Actor = chooser
                        SourceCard = source.Id
                        Effect = effect }

                let beerMatEvents =
                    plannedEvents |> Array.filter (fun value -> value.Kind = "BeerMatTossed")

                commit suspended selected refreshEvents (Array.append beerMatEvents [| requested |])
            | None ->
                let prepared, prepareEvents =
                    if isKit then
                        let next, events, _ = prepareKit authority mutation state selected
                        next, events
                    else
                        state, [||]

                let preparedSource = ReferenceState.card prepared source.Id

                let (executed, effectEvents, _, _, remaining, _, executionRejection, _) =
                    ReferenceDeterministicPrograms.executeStandaloneProgram
                        authority
                        NoProgramMutation
                        selected
                        selected.Actor
                        preparedSource
                        effect
                        program
                        isHouseRule
                        [||]
                        prepared

                match executionRejection with
                | Some(code, requirements) -> rejection state code requirements
                | None when remaining.Length <> 0 ->
                    invalidOp "The lifecycle plan and execution disagreed about deferred choices."
                | None ->
                    let next, semantic =
                        if isKit then
                            finishKit
                                authority
                                mutation
                                selected
                                executed
                                (Array.append prepareEvents effectEvents)
                        else
                            settleVoluntarySource source executed, effectEvents

                    commit next selected refreshEvents semantic

    let private applyPlayKit
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        (refreshEvents: CanonicalEvent array)
        : CanonicalTransition =
        let parts = selected.Payload.Split(';')
        let kitId = parts[0].Substring(4)
        let targetId = parts[1].Substring(7)

        match ReferenceState.tryCard state kitId with
        | None -> rejection state "CardNotFound" [||]
        | Some kitCard when kitCard.Owner <> selected.Actor -> rejection state "CardNotOwned" [||]
        | Some kitCard when kitCard.Kind <> "Kit" || kitCard.Zone <> "Mitt" ->
            rejection state "WrongZone" [||]
        | Some kitCard ->
            let definition = authority.Cards[kitCard.MechanicalId]

            let target =
                if String.IsNullOrEmpty targetId then
                    None
                else
                    ReferenceState.tryCard state targetId

            if not (kitCategoryAllows authority state selected.Actor definition target) then
                rejection state "RuleLimitReached" [||]
            else
                match executableHouseRules definition with
                | [||] ->
                    let prepared, prepareEvents, _ = prepareKit authority mutation state selected

                    let next, semantic =
                        finishKit authority mutation selected prepared prepareEvents

                    commit next selected refreshEvents semantic
                | rules ->
                    let rule = rules[0]

                    applyProgram
                        authority
                        mutation
                        state
                        selected
                        kitCard
                        rule.MechanicalId
                        rule.Program
                        true
                        true
                        refreshEvents

    let private applyUsePartyTrick
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        (refreshEvents: CanonicalEvent array)
        : CanonicalTransition =
        let parts = selected.Payload.Split(';')
        let sourceId = parts[0].Substring(7)
        let effect = parts[1].Substring(7)

        match ReferenceState.tryCard state sourceId with
        | None -> rejection state "EffectNotFound" [||]
        | Some source ->
            let program, isHouseRule = programFor authority source effect

            let available =
                if isHouseRule then
                    source.Kind = "Kit"
                    && source.Zone = "Local"
                    && containsOpcode ReferenceOpcode.OncePerRound program
                else
                    source.Owner = selected.Actor
                    && ReferenceDeterministicPrograms.inPlay source
                    && (authority.Cards[source.MechanicalId].PartyTricks
                        |> Array.exists (fun (trick: ReferencePartyTrick) ->
                            trick.MechanicalId = effect
                            && trick.Trigger = ReferenceTrigger.Activated))

            if
                not available
                || Array.contains effect state.RoundUsage.EffectsUsed
                || not (activationCanAct authority state source effect program isHouseRule)
            then
                rejection state "EffectUnavailable" [||]
            else
                applyProgram
                    authority
                    mutation
                    state
                    selected
                    source
                    effect
                    program
                    isHouseRule
                    false
                    refreshEvents

    let private mutateContinuous
        (mutation: ReferenceLifecycleMutation)
        (transition: CanonicalTransition)
        : CanonicalTransition =
        if mutation <> SkipContinuousEffectRegistration then
            transition
        else
            { transition with
                State =
                    { transition.State with
                        Effects =
                            transition.State.Effects
                            |> Array.filter (fun effect -> effect.Kind <> "ContinuousPartyTrick") }
                Events =
                    transition.Events
                    |> Array.filter (fun event -> event.Kind <> "EffectRegistered") }

    let apply
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        : CanonicalTransition =
        match boundary authority state selected with
        | Some code -> rejection state code [||]
        | None when selected.Kind = "Promote" ->
            match
                ReferenceDeterministicPrograms.validateOwnedChoices
                    authority
                    selected.Actor
                    selected.Choices
                    selected.Requirements
            with
            | Error(code, requirements) -> rejection state code requirements
            | Ok() ->
                ReferenceCommonFoundation.applyCommon authority NoReferenceMutation state selected
                |> mutateContinuous mutation
        | None when selected.Kind = "EndRound" ->
            ReferenceCommonFoundation.applyCommon authority NoReferenceMutation state selected
            |> mutateContinuous mutation
        | None when selected.Kind = "Attack" ->
            ReferenceDeterministicPrograms.apply authority NoProgramMutation state selected
            |> mutateContinuous mutation
        | None ->
            if state.Phase <> "Playing" then
                rejection state "WrongPhase" [||]
            elif state.ActivePlayer <> selected.Actor then
                rejection state "NotActorsTurn" [||]
            else
                let refreshed, refreshEvents =
                    ReferenceCommonFoundation.refreshContinuousEffects authority state

                match selected.Kind with
                | "PlayKit" ->
                    applyPlayKit authority mutation refreshed selected refreshEvents
                    |> mutateContinuous mutation
                | "UsePartyTrick" ->
                    applyUsePartyTrick authority mutation refreshed selected refreshEvents
                    |> mutateContinuous mutation
                | _ -> rejection state "WrongPhase" [||]

    let resolveEffectChoice
        (authority: ReferenceAuthority)
        (mutation: ReferenceLifecycleMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        : CanonicalTransition =
        match boundary authority state selected with
        | Some code -> rejection state code state.PendingEffect.Requirements
        | None when not state.PendingEffect.Present || state.Phase <> "AwaitingEffectChoice" ->
            rejection state "WrongPhase" [||]
        | None when state.PendingEffect.Chooser <> selected.Actor ->
            rejection state "WrongChooser" state.PendingEffect.Requirements
        | None ->
            match
                ReferenceDeterministicPrograms.validateOwnedChoices
                    authority
                    selected.Actor
                    selected.Choices
                    state.PendingEffect.Requirements
            with
            | Error(code, requirements) -> rejection state code requirements
            | Ok() ->
                let refreshed, refreshEvents =
                    ReferenceCommonFoundation.refreshContinuousEffects authority state

                let original = refreshed.PendingEffect.Action |> Array.exactlyOne

                let resumed =
                    { original with
                        Choices = Array.append original.Choices selected.Choices }

                let source = ReferenceState.card refreshed refreshed.PendingEffect.Source

                let program, isHouseRule =
                    programFor authority source refreshed.PendingEffect.Effect

                let playing =
                    { refreshed with
                        Phase = "Playing"
                        PendingEffect = Canonical.emptyPendingEffect }

                let prepared, prepareEvents =
                    if original.Kind = "PlayKit" then
                        let next, events, _ = prepareKit authority mutation playing resumed
                        next, events
                    else
                        playing, [||]

                let preparedSource = ReferenceState.card prepared source.Id

                let (executed, effectEvents, _, _, deferred, beerMats, executionRejection, _) =
                    ReferenceDeterministicPrograms.executeStandaloneProgram
                        authority
                        NoProgramMutation
                        resumed
                        original.Actor
                        preparedSource
                        refreshed.PendingEffect.Effect
                        program
                        isHouseRule
                        refreshed.PendingEffect.BeerMatResults
                        prepared

                match executionRejection with
                | Some(code, requirements) -> rejection state code requirements
                | None when deferred.Length <> 0 ->
                    let chooser =
                        deferred |> Array.map _.Chooser |> Array.distinct |> Array.exactlyOne

                    let suspended =
                        ReferenceCommonFoundation.suspendForEffect
                            { Canonical.emptyPendingEffect with
                                Present = true
                                Action = [| { resumed with Requirements = [||] } |]
                                Source = source.Id
                                Effect = refreshed.PendingEffect.Effect
                                Chooser = chooser
                                Requirements = deferred
                                BeerMatResults = beerMats
                                AttackStarted = false }
                            executed

                    let requested =
                        { ReferenceEvents.create "EffectChoiceRequested" with
                            Actor = chooser
                            SourceCard = source.Id
                            Effect = refreshed.PendingEffect.Effect }

                    commit
                        suspended
                        selected
                        refreshEvents
                        (Array.concat [ prepareEvents; effectEvents; [| requested |] ])
                    |> mutateContinuous mutation
                | None ->
                    let next, semantic =
                        if original.Kind = "PlayKit" then
                            finishKit
                                authority
                                mutation
                                resumed
                                executed
                                (Array.append prepareEvents effectEvents)
                        else
                            settleVoluntarySource source executed, effectEvents

                    commit next selected refreshEvents semantic |> mutateContinuous mutation
