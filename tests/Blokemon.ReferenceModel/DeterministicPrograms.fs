namespace Blokemon.ReferenceModel

open System

type ReferenceProgramMutation =
    | NoProgramMutation
    | SkipProgramDamagePlacement
    | SkipProgramCardMovement

type ReferenceDeterministicObligation =
    { Input: ReferenceObligationInput
      InitialState: CanonicalState }

[<RequireQualifiedAccess>]
module ReferenceDeterministicPrograms =

    let acceptedRoutes =
        Set
            [ "booth-all-own-swap"
              "chuck-vim-booth"
              "damage-attach-vim"
              "damage-booth-spread"
              "damage-chuck-cards"
              "damage-chuck-vim"
              "damage-effect"
              "damage-effects"
              "damage-heal"
              "damage-move-vim"
              "damage-rough"
              "damage-rough-effects"
              "damage-self"
              "damage-swap"
              "hand-kit-scale"
              "heal-clear"
              "ignore-modifier"
              "trivial-chuck"
              "trivial-copy"
              "trivial-damage"
              "trivial-distribution"
              "trivial-draw"
              "trivial-rough"
              "trivial-soft-spot"
              "trivial-swap" ]

    let acceptedObligationIds =
        Set
            [ "back-to-the-same-seat-moves-attached-vim"
              "check-their-pockets-two-kit-cards"
              "check-their-pockets-zero-kit-cards"
              "creeping-regret-registers-delayed-counters"
              "damage-booth-spread-blk-105-b01-normal"
              "damage-booth-spread-blk-110-b01-normal"
              "damage-booth-spread-blk-139-b01-normal"
              "damage-booth-spread-blk-144-b01-normal"
              "damage-booth-spread-blk-145-b01-predicate-false"
              "damage-booth-spread-blk-145-b01-predicate-true"
              "damage-chuck-cards-blk-024-b02"
              "damage-chuck-cards-blk-067-b01"
              "damage-chuck-cards-blk-068-b01"
              "damage-chuck-cards-blk-099-b01"
              "damage-chuck-cards-blk-149-b01"
              "damage-chuck-vim-blk-005-b02"
              "damage-chuck-vim-blk-006-b02"
              "damage-chuck-vim-blk-058-b01"
              "damage-chuck-vim-blk-059-b02"
              "damage-chuck-vim-blk-130-b01"
              "damage-chuck-vim-blk-150-b02"
              "damage-effect-blk-105-b02"
              "damage-effect-blk-108-b01"
              "damage-effect-blk-148-b02"
              "damage-effect-blk-150-b01"
              "damage-effects-blk-024-b01"
              "damage-effects-blk-028-b01"
              "damage-effects-blk-069-b02"
              "damage-effects-blk-088-b01"
              "damage-effects-blk-089-b01"
              "damage-heal-blk-001-b01"
              "damage-heal-blk-002-b01"
              "damage-heal-blk-134-b01"
              "damage-heal-blk-141-b01"
              "damage-rough-blk-003-b01"
              "damage-rough-blk-029-b01"
              "damage-rough-blk-034-b01"
              "damage-rough-blk-038-b01"
              "damage-rough-blk-046-b02"
              "damage-rough-blk-057-b01"
              "damage-rough-blk-073-b01"
              "damage-rough-blk-109-b01"
              "damage-rough-blk-124-b02"
              "damage-self-blk-026-b01"
              "damage-self-blk-081-b02"
              "damage-self-blk-084-b01"
              "damage-self-blk-143-b01"
              "damage-swap-blk-012-b01"
              "damage-swap-blk-064-b01"
              "damage-swap-blk-078-b02"
              "damage-swap-blk-111-b01"
              "disappear-upstairs-full"
              "disappear-upstairs-split"
              "disappear-upstairs-zero"
              "heal-clear-blk-079-b01"
              "heat-rises-pays-two-curry-and-hits-chosen-booth"
              "ignore-modifier-blk-076-b02"
              "ignore-modifier-blk-120-b01"
              "lasting-attack-effects-blk-031-b01"
              "lasting-attack-effects-blk-074-b01"
              "lasting-attack-effects-blk-090-b01"
              "lasting-attack-effects-blk-091-b01"
              "lasting-attack-effects-blk-117-b01"
              "lend-a-blade-no-recovery-target"
              "lend-a-blade-recovers-basic-grass-vim"
              "next-damage-blk-076-b01"
              "next-damage-blk-107-b01"
              "to-me-then-away-all-opponents-own-swap"
              "trivial-chuck-blk-004-b01"
              "trivial-chuck-blk-066-b01"
              "trivial-copy-blk-151-b01"
              "trivial-damage-blk-002-b02"
              "trivial-damage-blk-004-b02"
              "trivial-damage-blk-005-b01"
              "trivial-damage-blk-007-b02"
              "trivial-damage-blk-008-b02"
              "trivial-damage-blk-011-b01"
              "trivial-damage-blk-013-b01"
              "trivial-damage-blk-013-b02"
              "trivial-damage-blk-014-b01"
              "trivial-damage-blk-015-b02"
              "trivial-damage-blk-016-b02"
              "trivial-damage-blk-017-b01"
              "trivial-damage-blk-018-b01"
              "trivial-damage-blk-021-b01"
              "trivial-damage-blk-022-b02"
              "trivial-damage-blk-025-b02"
              "trivial-damage-blk-027-b01"
              "trivial-damage-blk-030-b02"
              "trivial-damage-blk-031-b02"
              "trivial-damage-blk-032-b01"
              "trivial-damage-blk-033-b01"
              "trivial-damage-blk-033-b02"
              "trivial-damage-blk-035-b02"
              "trivial-damage-blk-041-b01"
              "trivial-damage-blk-043-b01"
              "trivial-damage-blk-044-b01"
              "trivial-damage-blk-045-b01"
              "trivial-damage-blk-046-b01"
              "trivial-damage-blk-047-b02"
              "trivial-damage-blk-048-b01"
              "trivial-damage-blk-048-b02"
              "trivial-damage-blk-049-b02"
              "trivial-damage-blk-050-b01"
              "trivial-damage-blk-050-b02"
              "trivial-damage-blk-051-b01"
              "trivial-damage-blk-051-b02"
              "trivial-damage-blk-052-b02"
              "trivial-damage-blk-053-b01"
              "trivial-damage-blk-054-b02"
              "trivial-damage-blk-055-b02"
              "trivial-damage-blk-061-b01"
              "trivial-damage-blk-063-b01"
              "trivial-damage-blk-065-b02"
              "trivial-damage-blk-066-b02"
              "trivial-damage-blk-069-b01"
              "trivial-damage-blk-070-b01"
              "trivial-damage-blk-070-b02"
              "trivial-damage-blk-071-b01"
              "trivial-damage-blk-072-b01"
              "trivial-damage-blk-072-b02"
              "trivial-damage-blk-074-b02"
              "trivial-damage-blk-075-b02"
              "trivial-damage-blk-077-b02"
              "trivial-damage-blk-079-b02"
              "trivial-damage-blk-081-b01"
              "trivial-damage-blk-082-b02"
              "trivial-damage-blk-083-b02"
              "trivial-damage-blk-086-b01"
              "trivial-damage-blk-087-b02"
              "trivial-damage-blk-089-b02"
              "trivial-damage-blk-092-b01"
              "trivial-damage-blk-093-b01"
              "trivial-damage-blk-095-b02"
              "trivial-damage-blk-096-b01"
              "trivial-damage-blk-097-b01"
              "trivial-damage-blk-098-b02"
              "trivial-damage-blk-099-b02"
              "trivial-damage-blk-101-b02"
              "trivial-damage-blk-103-b02"
              "trivial-damage-blk-106-b02"
              "trivial-damage-blk-111-b02"
              "trivial-damage-blk-112-b01"
              "trivial-damage-blk-113-b01"
              "trivial-damage-blk-116-b01"
              "trivial-damage-blk-116-b02"
              "trivial-damage-blk-118-b02"
              "trivial-damage-blk-121-b01"
              "trivial-damage-blk-123-b02"
              "trivial-damage-blk-125-b02"
              "trivial-damage-blk-126-b01"
              "trivial-damage-blk-127-b01"
              "trivial-damage-blk-131-b02"
              "trivial-damage-blk-132-b01"
              "trivial-damage-blk-133-b02"
              "trivial-damage-blk-142-b01"
              "trivial-damage-blk-147-b01"
              "trivial-damage-blk-147-b02"
              "trivial-damage-blk-148-b01"
              "trivial-distribution-blk-122-b01-full"
              "trivial-distribution-blk-122-b01-split"
              "trivial-distribution-blk-122-b01-zero"
              "trivial-draw-blk-077-b01"
              "trivial-draw-blk-083-b01"
              "trivial-draw-blk-115-b01"
              "trivial-rough-blk-040-b01"
              "trivial-rough-blk-078-b01"
              "trivial-rough-blk-080-b01"
              "trivial-soft-spot-blk-137-b01"
              "trivial-swap-blk-036-b01"
              "wrong-way-round-damage-confusion-item-lock" ]

    [<Literal>]
    let AcceptedRouteCount = 25

    [<Literal>]
    let AcceptedObligationCount = 171

    let private actionOrder = ReferenceEngine.actionOrder
    let private action = ReferenceEngine.action
    let private moveCard = ReferenceEngine.moveCard
    let private draw = ReferenceEngine.draw

    let private otherPlayer (state: CanonicalState) actor = ReferenceState.otherPlayer state actor

    let private card (authority: ReferenceAuthority) id mechanicalId owner zone position =
        let definition =
            authority.Cards.TryFind mechanicalId
            |> Option.defaultWith (fun () ->
                invalidOp $"The deterministic setup names unknown card {mechanicalId}.")

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

    let private updateCard
        (id: string)
        (change: CanonicalCard -> CanonicalCard)
        (cards: CanonicalCard array)
        =
        cards |> Array.map (fun value -> if value.Id = id then change value else value)

    let private attach (vim: string) (target: string) (cards: CanonicalCard array) =
        cards
        |> updateCard vim (fun value ->
            { value with
                Zone = "Attached"
                StackPosition = -1
                AttachedTo = target })
        |> updateCard target (fun value ->
            { value with
                Attachments = Array.append value.Attachments [| vim |] })

    let private basicVimFor (authority: ReferenceAuthority) mechanicalType =
        authority.Cards
        |> Seq.map _.Value
        |> Seq.filter (fun value -> value.Kind = ReferenceCardKind.Vim)
        |> Seq.find (fun value -> value.VimType = ValueSome mechanicalType)
        |> _.Id

    let private attackFor (authority: ReferenceAuthority) (input: ReferenceObligationInput) =
        let owner =
            authority.Cards.TryFind input.ReviewedProgram.OwnerId
            |> Option.defaultWith (fun () ->
                invalidOp
                    $"Obligation {input.Id} names unknown attack owner {input.ReviewedProgram.OwnerId}.")

        owner.Attacks
        |> Array.tryFind (fun value -> value.MechanicalId = input.ReviewedProgram.MechanicalId)
        |> Option.defaultWith (fun () ->
            invalidOp
                $"Obligation {input.Id} names unknown attack {input.ReviewedProgram.MechanicalId}.")

    let private existingCardId (authority: ReferenceAuthority) (candidates: string array) =
        candidates
        |> Array.tryFind (fun value ->
            value.StartsWith("BLK-", StringComparison.Ordinal)
            && value.Length = 7
            && authority.Cards.ContainsKey value)

    let private addIfMissing (value: CanonicalCard) (values: CanonicalCard array) =
        if values |> Seq.exists (fun existing -> existing.Id = value.Id) then
            values
        else
            Array.append values [| value |]

    let private standardState (authority: ReferenceAuthority) (input: ReferenceObligationInput) =
        let attack = attackFor authority input
        let route = input.InitialState.Route.Value
        let parameters = input.InitialState.Parameters

        let defenderMechanicalId =
            match route with
            | "ignore-modifier" when parameters.Length >= 3 -> parameters[2]
            | _ -> existingCardId authority parameters[1..] |> Option.defaultValue "BLK-003"

        let mutable cards =
            [| card authority "attacker" input.ReviewedProgram.OwnerId "first" "Oche" -1
               card authority "defender" defenderMechanicalId "second" "Oche" -1 |]

        let attachedCosts = ResizeArray<string>()

        for index in 0 .. attack.VimCost.Length - 1 do
            let mechanicalType = attack.VimCost[index]

            let payableType =
                if mechanicalType = ReferenceMechanicalType.Colorless then
                    ReferenceMechanicalType.Grass
                else
                    mechanicalType

            let id = $"vim-{index}"

            cards <-
                Array.append
                    cards
                    [| card authority id (basicVimFor authority payableType) "first" "Mitt" -1 |]

            cards <- attach id "attacker" cards
            attachedCosts.Add id

        let add (owner: string) (id: string) (mechanicalId: string) (zone: string) (position: int) =
            cards <- addIfMissing (card authority id mechanicalId owner zone position) cards

        let addStack (owner: string) (prefix: string) (count: int) =
            for index in 0 .. count - 1 do
                add
                    owner
                    $"{prefix}-{index}"
                    (basicVimFor authority ReferenceMechanicalType.Grass)
                    "Stack"
                    index

        let addBarChits (owner: string) (prefix: string) =
            for index in 0..5 do
                add
                    owner
                    $"{prefix}-{index}"
                    (basicVimFor authority ReferenceMechanicalType.Grass)
                    "BarChit"
                    index

        addStack "first" "own-stack" 5
        addStack "second" "other-stack" 5
        addBarChits "first" "first-bar"
        addBarChits "second" "second-bar"

        let ensureOwnBooth (id: string) = add "first" id "BLK-003" "Booth" -1
        let ensureOtherBooth (id: string) = add "second" id "BLK-003" "Booth" -1

        match route with
        | "booth-all-own-swap" ->
            ensureOwnBooth "a-own-swap"
            ensureOtherBooth "a-other-booth"
        | "chuck-vim-booth" -> ensureOtherBooth "a-other-booth"
        | "damage-attach-vim" ->
            ensureOwnBooth "own-booth"

            add
                "first"
                "recovered-vim"
                (basicVimFor authority ReferenceMechanicalType.Grass)
                "EmptiesTray"
                -1
        | "damage-booth-spread" ->
            let boothCount =
                if parameters.Length >= 6 then
                    Int32.Parse parameters[5]
                else
                    1

            for index in 0 .. boothCount - 1 do
                ensureOtherBooth $"a-booth-{index}"

            if parameters |> Array.contains "predicate-true" then
                cards <- updateCard "a-booth-0" (fun value -> { value with Damage = 10 }) cards
        | "damage-chuck-cards" ->
            if parameters |> Array.contains "OtherMitt" then
                add "second" "other-mitt-0" "KIT-001" "Mitt" -1
                add "second" "other-mitt-1" "KIT-002" "Mitt" -1

            ensureOtherBooth "other-reserve"
        | "damage-chuck-vim" ->
            if parameters |> Array.contains "other" then
                let mechanicalId =
                    parameters
                    |> Array.tryFind (fun value ->
                        value.StartsWith("VIM-", StringComparison.Ordinal))
                    |> Option.defaultValue (basicVimFor authority ReferenceMechanicalType.Grass)

                let count = Int32.Parse parameters[parameters.Length - 1]

                for index in 0 .. count - 1 do
                    let id = $"other-vim-{index}"
                    add "second" id mechanicalId "Mitt" -1
                    cards <- attach id "defender" cards

            ensureOtherBooth "other-reserve"
        | "damage-heal" ->
            cards <- updateCard "attacker" (fun value -> { value with Damage = 60 }) cards
            ensureOtherBooth "other-reserve"
        | "damage-move-vim" ->
            let mechanicalId =
                parameters
                |> Array.tryFind (fun value -> value.StartsWith("VIM-", StringComparison.Ordinal))
                |> Option.defaultValue (basicVimFor authority ReferenceMechanicalType.Water)

            add "second" "other-vim-0" mechanicalId "Mitt" -1
            cards <- attach "other-vim-0" "defender" cards
            ensureOtherBooth "other-reserve"
        | "damage-rough" ->
            if
                parameters
                |> Array.exists (fun value ->
                    value.Contains("Singed") || value.Contains("NoddedOff"))
            then
                let stayingPower = authority.Cards[defenderMechanicalId].StayingPower

                cards <-
                    updateCard "defender" (fun value -> { value with Damage = stayingPower }) cards

            ensureOtherBooth "other-reserve"
        | "damage-rough-effects"
        | "damage-self"
        | "damage-effect"
        | "damage-effects"
        | "ignore-modifier"
        | "trivial-damage" -> ensureOtherBooth "other-reserve"
        | "damage-swap" ->
            if parameters |> Array.contains "own" then
                ensureOwnBooth "a-own-swap"
            else
                ensureOtherBooth "a-other-swap"
        | "hand-kit-scale" ->
            if not (parameters |> Array.contains "zero") then
                add "second" "other-kit-0" "KIT-001" "Mitt" -1
                add "second" "other-kit-1" "KIT-002" "Mitt" -1

            ensureOtherBooth "other-reserve"
        | "heal-clear" ->
            cards <-
                updateCard
                    "attacker"
                    (fun value ->
                        { value with
                            Damage = 60
                            RoughStates =
                                [| { State = "DodgyPint"
                                     AppliedAtOwnerRound = 1 } |] })
                    cards

            ensureOtherBooth "other-reserve"
        | "trivial-chuck" ->
            if parameters |> Array.contains "local" then
                add "second" "local-under-test" "KIT-001" "Local" -1

            ensureOtherBooth "other-reserve"
        | "trivial-copy" -> ensureOtherBooth "other-reserve"
        | "trivial-distribution" ->
            ensureOtherBooth "a-other-booth"
            ensureOtherBooth "b-other-booth"
        | "trivial-draw" -> ensureOtherBooth "other-reserve"
        | "trivial-rough" ->
            let stayingPower = authority.Cards[defenderMechanicalId].StayingPower
            cards <- updateCard "defender" (fun value -> { value with Damage = stayingPower }) cards
            ensureOtherBooth "other-reserve"
        | "trivial-soft-spot" -> ensureOtherBooth "other-reserve"
        | "trivial-swap" -> ensureOtherBooth "a-other-booth"
        | unknown -> invalidOp $"Unowned deterministic route {unknown}."

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

        let playerInput =
            input.InitialState.Players
            |> Seq.map (fun value -> value.Player, value)
            |> Map.ofSeq

        let players =
            [| "first"; "second" |]
            |> Array.map (fun id ->
                { Id = id
                  BarChitsRemaining =
                    playerInput.TryFind id
                    |> Option.map _.BarChitsRemaining
                    |> Option.defaultValue 6
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = [||]
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 2 })

        let usedActivatedEffects =
            authority.Cards[input.ReviewedProgram.OwnerId].PartyTricks
            |> Array.filter (fun value -> value.Trigger = ReferenceTrigger.Activated)
            |> Array.map _.MechanicalId

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
          OpeningPlayer = "first"
          ActivePlayer = "first"
          RoundNumber = 3
          Players = players
          Cards = cards |> Array.sortBy _.Id
          Effects = [||]
          RoundUsage =
            { Player = "first"
              VimAttachments = 0
              MatesPlayed = 0
              LocalsPlayed = 0
              TaxisUsed = 0
              EffectsUsed = usedActivatedEffects
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
            |> Seq.filter (snd >> ((<>) 1))
            |> Seq.map fst
            |> Seq.toArray

        if selected.Length <> AcceptedObligationCount then
            invalidOp
                $"The BLOKEMON-135 ledger contains {selected.Length} obligations instead of {AcceptedObligationCount}."

        if selectedRoutes <> acceptedRoutes || selectedRoutes.Count <> AcceptedRouteCount then
            invalidOp "The BLOKEMON-135 route ledger is missing, stale, duplicated, or borrowed."

        if
            selectedIds <> acceptedObligationIds
            || acceptedObligationIds.Count <> AcceptedObligationCount
        then
            invalidOp
                "The BLOKEMON-135 obligation ledger is missing, stale, duplicated, or borrowed."

        if duplicateIds.Length <> 0 then
            invalidOp
                $"The BLOKEMON-135 obligation ledger contains duplicate identities: {String.Join(',', duplicateIds)}."

        selected

    let materialize (authority: ReferenceAuthority) (inventory: ReferenceObligationInventory) =
        reconcile inventory
        |> Array.map (fun input ->
            { Input = input
              InitialState = standardState authority input })

    let private inPlay (card: CanonicalCard) =
        card.Zone = "Oche" || card.Zone = "Booth"

    let private targetsFor
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (location: ReferenceLocation)
        : CanonicalCard array =
        let other = otherPlayer state actor

        let cards (owner: string) (zone: string) = ReferenceState.cardsIn state owner zone

        let values =
            match location with
            | ReferenceLocation.AttackingBloke
            | ReferenceLocation.Self -> [| attacker |]
            | ReferenceLocation.LocalInPlay ->
                state.Cards |> Array.filter (fun value -> value.Zone = "Local")
            | ReferenceLocation.OtherBlokeChosen
            | ReferenceLocation.OtherBlokesAll ->
                state.Cards
                |> Array.filter (fun (value: CanonicalCard) ->
                    value.Owner = other && value.Kind = "Bloke" && inPlay value)
            | ReferenceLocation.OtherBoothAll
            | ReferenceLocation.OtherBoothChosen -> cards other "Booth"
            | ReferenceLocation.OtherMitt -> cards other "Mitt"
            | ReferenceLocation.OtherOche -> cards other "Oche"
            | ReferenceLocation.OtherStack -> cards other "Stack"
            | ReferenceLocation.OwnBlokeChosen
            | ReferenceLocation.OwnBlokesAll ->
                state.Cards
                |> Array.filter (fun (value: CanonicalCard) ->
                    value.Owner = actor && value.Kind = "Bloke" && inPlay value)
            | ReferenceLocation.OwnBoothChosen -> cards actor "Booth"
            | ReferenceLocation.OwnEmptiesTray -> cards actor "EmptiesTray"
            | ReferenceLocation.OwnMitt -> cards actor "Mitt"
            | ReferenceLocation.OwnOche -> cards actor "Oche"
            | ReferenceLocation.OwnStack -> cards actor "Stack"
            | ReferenceLocation.OtherOcheAttachedVim ->
                cards other "Oche"
                |> Array.collect (fun (value: CanonicalCard) ->
                    value.Attachments |> Array.map (ReferenceState.card state))
                |> Array.filter (fun (value: CanonicalCard) -> value.Kind = "Vim")
            | ReferenceLocation.OwnAttachedBarKits
            | ReferenceLocation.BarChits
            | ReferenceLocation.KnockedOutBlokeAttachedVim
            | ReferenceLocation.OtherEmptiesTray ->
                invalidOp $"The deterministic target prerequisite cannot resolve {location}."
            | other ->
                invalidOp
                    $"The deterministic target prerequisite received unknown location {int other}."

        values |> Array.sortBy _.Id

    let private filteredCards
        (authority: ReferenceAuthority)
        (instruction: ReferenceInstruction)
        (values: CanonicalCard array)
        =
        let byMechanicalType (value: CanonicalCard) =
            if
                instruction.Opcode <> ReferenceOpcode.ChuckVim
                && instruction.Opcode <> ReferenceOpcode.MoveVim
            then
                true
            elif instruction.MechanicalTypes.Length = 0 then
                true
            elif value.Kind = "Vim" then
                authority.Cards[value.MechanicalId].VimType
                |> ValueOption.exists (fun item -> Array.contains item instruction.MechanicalTypes)
            else
                authority.Cards[value.MechanicalId].MechanicalTypes
                |> Array.exists (fun item -> Array.contains item instruction.MechanicalTypes)

        let byFilter (value: CanonicalCard) =
            match instruction.CardFilter with
            | ValueNone -> true
            | ValueSome filter ->
                let definition = authority.Cards[value.MechanicalId]

                (filter.Categories.Length = 0 || Array.contains definition.Kind filter.Categories)
                && (filter.Ranks.Length = 0
                    || definition.Rank
                       |> ValueOption.exists (fun item -> Array.contains item filter.Ranks))
                && (filter.KitKinds.Length = 0
                    || definition.KitKind
                       |> ValueOption.exists (fun item -> Array.contains item filter.KitKinds))
                && (not filter.BasicVimOnly || definition.Kind = ReferenceCardKind.Vim)
                && not (Array.contains value.MechanicalId filter.ExcludedRelatedIds)

        values |> Array.filter (fun value -> byMechanicalType value && byFilter value)

    let private predicateAllows
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (predicate: ReferencePredicate)
        =
        match predicate.Condition with
        | ReferenceCondition.TargetHasDamage -> target.Damage > 0
        | ReferenceCondition.SourceIsRegular ->
            authority.Cards[attacker.MechanicalId].Rank = ValueSome ReferenceRank.Regular
        | other -> invalidOp $"The deterministic predicate prerequisite cannot evaluate {other}."

    let private registersPersistentEffect opcode =
        match opcode with
        | ReferenceOpcode.ContinuousPartyTrick
        | ReferenceOpcode.EndRoundEffect
        | ReferenceOpcode.PreventDamage
        | ReferenceOpcode.PreventEffects
        | ReferenceOpcode.ReduceDamage
        | ReferenceOpcode.ModifyAttackCost
        | ReferenceOpcode.ModifyTaxiFare
        | ReferenceOpcode.ModifySoftSpot
        | ReferenceOpcode.RestrictAttack
        | ReferenceOpcode.RestrictTaxi
        | ReferenceOpcode.RestrictKit
        | ReferenceOpcode.RestrictLocal
        | ReferenceOpcode.RestrictEmptiesRecovery
        | ReferenceOpcode.ForceBeerMatBlank
        | ReferenceOpcode.ReflectAttackDamage -> true
        | _ -> false

    let private candidatesFor
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (instruction: ReferenceInstruction)
        : CanonicalCard array =
        let locations =
            if instruction.Sources.Length <> 0 then
                instruction.Sources
            else
                instruction.Targets

        let located = locations |> Array.collect (targetsFor authority state actor attacker)

        let candidates =
            match instruction.Opcode with
            | ReferenceOpcode.ChuckVim
            | ReferenceOpcode.MoveVim ->
                located
                |> Array.collect (fun value ->
                    value.Attachments |> Array.map (ReferenceState.card state))
                |> Array.filter (fun value -> value.Kind = "Vim")
            | _ -> located

        candidates
        |> Array.distinctBy _.Id
        |> filteredCards authority instruction
        |> Array.filter (fun value ->
            registersPersistentEffect instruction.Opcode
            || (instruction.Predicates
                |> Array.forall (predicateAllows authority state attacker value)))
        |> Array.sortBy _.Id

    let private emptyRequirement
        (id: string)
        (kind: string)
        (chooser: string)
        (minimum: int)
        (maximum: int)
        =
        { Id = id
          Kind = kind
          Chooser = chooser
          Minimum = minimum
          Maximum = maximum
          EligibleCards = [||]
          EligibleMechanicalTypes = [||]
          EligibleEffects = [||]
          DependsOnOptional = ""
          EligibleTargets = [||]
          RequireDifferentMechanicalTypes = false
          EligibleCardTypes = [||] }

    let private instructionRequirements
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (effect: string)
        (path: string)
        (instruction: ReferenceInstruction)
        =
        let id suffix = $"{effect}:{path}:{suffix}"
        let candidates = candidatesFor authority state actor attacker instruction
        let cardIds = candidates |> Array.map _.Id

        let cardTypes =
            candidates
            |> Array.map (fun value ->
                let definition = authority.Cards[value.MechanicalId]

                let mechanicalTypes =
                    if definition.Kind = ReferenceCardKind.Bloke then
                        definition.MechanicalTypes |> Array.map string
                    else
                        [| string ReferenceMechanicalType.Colorless |]

                { Card = value.Id
                  MechanicalTypes = mechanicalTypes })

        let chooser =
            if instruction.Selection = ReferenceSelection.OtherSideChosen then
                otherPlayer state actor
            else
                actor

        let optional =
            if
                instruction.Predicates
                |> Array.exists (fun value -> value.Condition = ReferenceCondition.Optional)
            then
                [| emptyRequirement (id "optional") "Optional" actor 0 1 |]
            else
                [||]

        let selection =
            match instruction.Opcode, instruction.Selection with
            | ReferenceOpcode.CopyAttack, ReferenceSelection.Chosen ->
                let attacks =
                    candidates
                    |> Array.collect (fun value -> authority.Cards[value.MechanicalId].Attacks)
                    |> Array.map _.MechanicalId
                    |> Array.distinct
                    |> Array.sort

                [| { emptyRequirement (id "attack") "Attack" chooser 1 1 with
                       EligibleEffects = attacks } |]
            | ReferenceOpcode.ModifySoftSpot, ReferenceSelection.Chosen ->
                [| { emptyRequirement (id "type") "MechanicalType" chooser 1 1 with
                       EligibleMechanicalTypes = instruction.MechanicalTypes |> Array.map string } |]
            | ReferenceOpcode.PlaceDamageCounters, ReferenceSelection.AnyDistribution ->
                [| { emptyRequirement
                         (id "distribution")
                         "Distribution"
                         chooser
                         0
                         instruction.Amount with
                       EligibleCards = cardIds } |]
            | ReferenceOpcode.AttachVim, ReferenceSelection.AnyDistribution ->
                let targets =
                    instruction.Targets
                    |> Array.collect (targetsFor authority state actor attacker)
                    |> Array.map _.Id
                    |> Array.distinct
                    |> Array.sort

                if cardIds.Length = 0 || targets.Length = 0 then
                    [||]
                else
                    [| { emptyRequirement
                             (id "attachments")
                             "Attachments"
                             chooser
                             instruction.Amount
                             instruction.Amount with
                           EligibleCards = cardIds
                           EligibleTargets = targets } |]
            | _, ReferenceSelection.Chosen
            | _, ReferenceSelection.OtherSideChosen when cardIds.Length <> 0 ->
                [| { emptyRequirement
                         (id "cards")
                         "Cards"
                         chooser
                         instruction.TargetCount
                         instruction.TargetCount with
                       EligibleCards = cardIds
                       EligibleCardTypes = cardTypes } |]
            | _ -> [||]

        Array.append optional selection

    let private requirementsForProgram
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (effect: string)
        (prefix: string)
        (instructions: ReferenceInstruction array)
        =
        instructions
        |> Array.mapi (fun index instruction ->
            let path = $"{prefix}{index}"

            instructionRequirements authority state actor attacker effect path instruction)
        |> Array.concat

    let private attackActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        state.Cards
        |> Array.filter (fun (attacker: CanonicalCard) -> attacker.Owner = actor && inPlay attacker)
        |> Array.collect (fun (attacker: CanonicalCard) ->
            let owner = authority.Cards[attacker.MechanicalId]

            owner.Attacks
            |> Array.filter (fun attack ->
                (attacker.Zone = "Oche" || (attacker.Zone = "Booth" && attack.CanBeUsedFromBench))
                && (authority.BaseRules.Opening.OpeningParticipantMayAttack
                    || actor <> state.OpeningPlayer
                    || (ReferenceState.player state actor).RoundsStarted <> 1)
                && not (
                    ReferenceCommonFoundation.roughPrevents authority _.PreventsAttack attacker
                )
                && ReferenceCommonFoundation.canPayAttack authority state attacker attack)
            |> Array.map (fun attack ->
                let key = $"attack:{attacker.Id}:{attack.MechanicalId}"

                action
                    state
                    "Attack"
                    actor
                    key
                    key
                    $"attacker={attacker.Id};effect={attack.MechanicalId}"
                    (requirementsForProgram
                        authority
                        state
                        actor
                        attacker
                        attack.MechanicalId
                        "root/"
                        attack.Program)
                    [||]))

    let legalActions (authority: ReferenceAuthority) (state: CanonicalState) (actor: string) =
        let refreshed =
            if state.Phase = "Playing" then
                ReferenceCommonFoundation.refreshContinuousEffects authority state |> fst
            else
                state

        let common =
            ReferenceCommonFoundation.legalCommonActions authority NoReferenceMutation state actor
            |> Array.filter (fun value -> value.Kind <> "Attack")

        let attacks =
            if refreshed.Phase = "Playing" && refreshed.ActivePlayer = actor then
                attackActions authority refreshed actor
            else
                [||]

        Array.append common attacks
        |> Array.sortWith (fun left right ->
            let byKind = compare actionOrder[left.Kind] actionOrder[right.Kind]

            if byKind <> 0 then
                byKind
            else
                String.CompareOrdinal(left.StableKey, right.StableKey))

    let private canonicalChoice (input: ReferenceChoiceInput) =
        let values =
            match input.Value with
            | ReferenceChoiceValue.Optional accepted -> [| accepted.ToString().ToLowerInvariant() |]
            | ReferenceChoiceValue.Amount amount -> [| string amount |]
            | ReferenceChoiceValue.Cards cards -> cards
            | ReferenceChoiceValue.MechanicalType mechanicalType -> [| string mechanicalType |]
            | ReferenceChoiceValue.Attack effect -> [| effect |]
            | ReferenceChoiceValue.Distribution allocations ->
                allocations |> Array.map (fun value -> $"{value.Card}:{value.Counters}")
            | ReferenceChoiceValue.Attachments placements ->
                placements |> Array.map (fun value -> $"{value.Vim}->{value.Bloke}")

        { Kind =
            match input.Value with
            | ReferenceChoiceValue.Optional _ -> "Optional"
            | ReferenceChoiceValue.Amount _ -> "Amount"
            | ReferenceChoiceValue.Cards _ -> "Cards"
            | ReferenceChoiceValue.MechanicalType _ -> "MechanicalType"
            | ReferenceChoiceValue.Attack _ -> "Attack"
            | ReferenceChoiceValue.Distribution _ -> "Distribution"
            | ReferenceChoiceValue.Attachments _ -> "Attachments"
          Id = input.RequirementId
          Values = values }

    let selectAction
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (input: ReferenceActionInput)
        (commandIndex: int)
        =
        let legal = legalActions authority state input.Actor
        let stableKey = $"attack:{input.SourceCard}:{input.EffectId}"

        let selected =
            legal
            |> Array.tryFind (fun value -> value.Kind = "Attack" && value.StableKey = stableKey)
            |> Option.defaultWith (fun () ->
                invalidOp
                    $"The reference action selector could not select {stableKey} from its independently derived legal actions.")

        let requirementIds = selected.Requirements |> Seq.map _.Id |> Set.ofSeq

        let choices =
            input.Choices
            |> Array.filter (fun value ->
                (not value.WhenAvailable || requirementIds.Contains value.RequirementId)
                && (selected.Requirements
                    |> Array.exists (fun requirement ->
                        requirement.Id = value.RequirementId && requirement.Chooser = input.Actor)))
            |> Array.map canonicalChoice

        { selected with
            CommandId = $"obligation:{commandIndex}"
            StableKey = ""
            Choices = choices }

    let private choiceBy (id: string) (kind: string) (selected: CanonicalAction) =
        selected.Choices
        |> Array.tryFind (fun value -> value.Id = id && value.Kind = kind)

    let private selectedCards (id: string) (selected: CanonicalAction) =
        choiceBy id "Cards" selected |> Option.map _.Values |> Option.defaultValue [||]

    let private destinationZone
        (actor: string)
        (state: CanonicalState)
        (destination: ReferenceDestination)
        =
        match destination with
        | ReferenceDestination.OtherBooth -> otherPlayer state actor, "Booth"
        | ReferenceDestination.OtherMitt -> otherPlayer state actor, "Mitt"
        | ReferenceDestination.OtherStack
        | ReferenceDestination.BottomOfOtherStack -> otherPlayer state actor, "Stack"
        | ReferenceDestination.OwnBooth -> actor, "Booth"
        | ReferenceDestination.OwnMitt -> actor, "Mitt"
        | ReferenceDestination.OwnStack -> actor, "Stack"
        | other ->
            invalidOp
                $"The deterministic destination prerequisite received unknown value {int other}."

    let private registerEffect
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (effect: string)
        (duration: string)
        (kind: string)
        (instruction: ReferenceInstruction)
        (target: CanonicalCard)
        =
        let registered =
            { SourceEffect = effect
              SourceCard = attacker.Id
              Owner = actor
              TargetCard = target.Id
              Kind = kind
              Amount = instruction.Amount
              MechanicalTypes = instruction.MechanicalTypes |> Array.map string
              RoughStates = instruction.RoughStates |> Array.map string
              RelatedCards = instruction.RelatedIds
              Conditions = instruction.Predicates |> Array.map (_.Condition >> string)
              Duration = duration
              AppliesFromRound = state.RoundNumber
              ExpiresAfterRound = state.RoundNumber + 2 }

        { state with
            Effects = Array.append state.Effects [| registered |] },
        [| { ReferenceEvents.create "EffectRegistered" with
               Actor = actor
               SourceCard = attacker.Id
               TargetCards = [| target.Id |]
               Effect = effect
               Amount = instruction.Amount } |]

    let private attackDamage
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (attack: ReferenceAttack)
        =
        let mutable amount = attack.PrintedDamage

        for instruction in attack.Program do
            if attack.VariablePrintedDamage && instruction.Opcode = ReferenceOpcode.ScaleDamage then
                amount <-
                    match instruction.ValueSource with
                    | ReferenceValueSource.Fixed -> instruction.Amount
                    | ReferenceValueSource.KitCardsInOtherMitt ->
                        ReferenceState.cardsIn state target.Owner "Mitt"
                        |> Array.filter (fun value -> value.Kind = "Kit")
                        |> Array.length
                        |> (*) instruction.Amount
                    | other -> invalidOp $"The deterministic damage scaler cannot read {other}."

        let ignoreStubborn =
            attack.Program
            |> Array.exists (fun value ->
                value.Opcode = ReferenceOpcode.IgnoreStubbornStreak
                || value.Opcode = ReferenceOpcode.IgnoreSoftSpotAndStubbornStreak)

        let ignoreSoftSpot =
            attack.Program
            |> Array.exists (fun value ->
                value.Opcode = ReferenceOpcode.IgnoreSoftSpotAndStubbornStreak)

        for step in authority.BaseRules.DamageOrder do
            match step with
            | "PrintedOrProgramBaseDamage"
            | "EffectsOnAttackingBlokeBeforeSoftSpotAndStubbornStreak"
            | "EffectsOnDefendingBlokeAfterSoftSpotAndStubbornStreak" -> ()
            | "SoftSpot" when not ignoreSoftSpot ->
                let attackerTypes = authority.Cards[attacker.MechanicalId].MechanicalTypes
                let modifiers = authority.Cards[target.MechanicalId].SoftSpotMultipliers

                amount <-
                    attackerTypes
                    |> Array.choose modifiers.TryFind
                    |> Array.tryHead
                    |> Option.defaultValue 1
                    |> (*) amount
            | "StubbornStreak" when not ignoreStubborn ->
                let attackerTypes = authority.Cards[attacker.MechanicalId].MechanicalTypes
                let modifiers = authority.Cards[target.MechanicalId].StubbornStreakReductions

                amount <-
                    amount
                    - (attackerTypes
                       |> Array.choose modifiers.TryFind
                       |> Array.tryHead
                       |> Option.defaultValue 0)
            | "SoftSpot"
            | "StubbornStreak" -> ()
            | "ClampAtZeroAndPlaceCounters" -> amount <- max 0 amount
            | other -> invalidOp $"Unsupported deterministic damage-order step {other}."

        amount

    let private validateChoices (selected: CanonicalAction) =
        let supplied = selected.Choices |> Seq.map _.Id |> Set.ofSeq

        let missing =
            selected.Requirements
            |> Array.filter (fun requirement ->
                requirement.Chooser = selected.Actor && not (supplied.Contains requirement.Id))

        if missing.Length <> 0 then
            Error("ChoiceRequired", missing)
        else
            Ok()

    let private applyDamage
        (mutation: ReferenceProgramMutation)
        (actor: string)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (kind: string)
        (amount: int)
        (state: CanonicalState)
        =
        if amount <= 0 || mutation = SkipProgramDamagePlacement then
            state, [||], [||]
        else
            let current = ReferenceState.card state target.Id

            let next =
                ReferenceState.updateCard
                    { current with
                        Damage = current.Damage + amount }
                    state

            next,
            [| { ReferenceEvents.create "DamagePlaced" with
                   Actor = actor
                   SourceCard = attacker.Id
                   TargetCards = [| target.Id |]
                   DamageKind = kind
                   Amount = amount } |],
            [| target.Id |]

    let rec private executeProgram
        (authority: ReferenceAuthority)
        (mutation: ReferenceProgramMutation)
        (selected: CanonicalAction)
        (actor: string)
        (attacker: CanonicalCard)
        (effect: string)
        (attack: ReferenceAttack)
        (depth: int)
        (state: CanonicalState)
        =
        if depth > 8 then
            invalidOp "CopyAttack exceeded the deterministic recursive dispatch bound."

        let events = ResizeArray<CanonicalEvent>()
        let attackTargets = ResizeArray<string>()
        let damageInstructions = ResizeArray<string * ReferenceInstruction>()
        let mutable next = state
        let mutable delayed = false

        let rec execute prefix (instructions: ReferenceInstruction array) =
            for index in 0 .. instructions.Length - 1 do
                let instruction = instructions[index]
                let path = $"{prefix}{index}"
                let requirementId suffix = $"{effect}:{path}:{suffix}"
                let candidates = candidatesFor authority next actor attacker instruction

                let selectedTargets =
                    match instruction.Selection with
                    | ReferenceSelection.Chosen when
                        instruction.Opcode = ReferenceOpcode.ModifySoftSpot
                        ->
                        candidates
                    | ReferenceSelection.Chosen
                    | ReferenceSelection.OtherSideChosen ->
                        selectedCards (requirementId "cards") selected
                        |> Array.choose (ReferenceState.tryCard next)
                    | ReferenceSelection.Top -> candidates |> Array.truncate instruction.Amount
                    | _ -> candidates

                match instruction.Opcode with
                | ReferenceOpcode.DealPrintedDamage
                | ReferenceOpcode.DealBoothDamage
                | ReferenceOpcode.DealSelfDamage -> damageInstructions.Add(path, instruction)
                | ReferenceOpcode.PlaceDamageCounters when not delayed ->
                    damageInstructions.Add(path, instruction)
                | ReferenceOpcode.PlaceDamageCounters -> ()
                | ReferenceOpcode.ScaleDamage when attack.VariablePrintedDamage ->
                    damageInstructions.Add(path, instruction)
                | ReferenceOpcode.ScaleDamage ->
                    let registered, registeredEvents =
                        registerEffect
                            next
                            actor
                            attacker
                            effect
                            "UntilEndOfOpponentsNextRound"
                            "ScaleNextAttackDamage"
                            instruction
                            attacker

                    next <-
                        { registered with
                            Effects =
                                registered.Effects
                                |> Array.mapi (fun index value ->
                                    if index = registered.Effects.Length - 1 then
                                        { value with
                                            AppliesFromRound = next.RoundNumber + 2
                                            ExpiresAfterRound = next.RoundNumber + 2 }
                                    else
                                        value) }

                    events.AddRange registeredEvents
                | ReferenceOpcode.HealDamage ->
                    for target in selectedTargets do
                        let current = ReferenceState.card next target.Id
                        let amount = min instruction.Amount current.Damage

                        if amount > 0 then
                            next <-
                                ReferenceState.updateCard
                                    { current with
                                        Damage = current.Damage - amount }
                                    next

                            events.Add
                                { ReferenceEvents.create "DamageHealed" with
                                    Actor = actor
                                    SourceCard = attacker.Id
                                    TargetCards = [| target.Id |]
                                    Amount = amount }
                | ReferenceOpcode.ApplyRoughState ->
                    for target in selectedTargets do
                        for rough in instruction.RoughStates do
                            let current = ReferenceState.card next target.Id

                            if
                                not (
                                    current.RoughStates
                                    |> Array.exists (fun value -> value.State = string rough)
                                )
                            then
                                next <-
                                    ReferenceState.updateCard
                                        { current with
                                            RoughStates =
                                                Array.append
                                                    current.RoughStates
                                                    [| { State = string rough
                                                         AppliedAtOwnerRound =
                                                           (ReferenceState.player next target.Owner)
                                                               .RoundsStarted } |] }
                                        next

                                events.Add
                                    { ReferenceEvents.create "RoughStateApplied" with
                                        Actor = actor
                                        SourceCard = attacker.Id
                                        TargetCards = [| target.Id |]
                                        RoughState = string rough }
                | ReferenceOpcode.ClearRoughState ->
                    for target in selectedTargets do
                        let current = ReferenceState.card next target.Id

                        for rough in current.RoughStates do
                            events.Add
                                { ReferenceEvents.create "RoughStateCleared" with
                                    Actor = actor
                                    TargetCards = [| target.Id |]
                                    RoughState = rough.State }

                        next <- ReferenceState.updateCard { current with RoughStates = [||] } next
                | ReferenceOpcode.DrawFromStack ->
                    let drawn, drawEvents, _ = draw actor instruction.Amount "Effect" next
                    next <- drawn
                    events.AddRange drawEvents
                | ReferenceOpcode.RevealCards ->
                    events.Add
                        { ReferenceEvents.create "CardsRevealed" with
                            Actor = actor
                            SourceCard = attacker.Id
                            TargetCards = selectedTargets |> Array.map _.Id
                            Effect = effect }
                | ReferenceOpcode.MoveCards ->
                    if mutation <> SkipProgramCardMovement then
                        let destination =
                            instruction.Destination
                            |> ValueOption.defaultWith (fun () ->
                                invalidOp $"MoveCards {effect}:{path} has no destination.")

                        let _, zone = destinationZone actor next destination

                        for target in selectedTargets do
                            let attachedTo = target.AttachedTo
                            let moved, movedEvent = moveCard target.Id zone "" next

                            next <-
                                if String.IsNullOrEmpty attachedTo then
                                    moved
                                else
                                    let parent = ReferenceState.card moved attachedTo

                                    ReferenceState.updateCard
                                        { parent with
                                            Attachments =
                                                parent.Attachments |> Array.filter ((<>) target.Id) }
                                        moved

                            events.Add movedEvent
                | ReferenceOpcode.ChuckCards ->
                    if mutation <> SkipProgramCardMovement then
                        for target in selectedTargets do
                            let moved, movedEvent = moveCard target.Id "EmptiesTray" "" next
                            next <- moved
                            events.Add movedEvent
                | ReferenceOpcode.AttachVim ->
                    match choiceBy (requirementId "attachments") "Attachments" selected with
                    | None -> ()
                    | Some choice when mutation <> SkipProgramCardMovement ->
                        for placement in choice.Values do
                            let parts = placement.Split("->", StringSplitOptions.None)
                            let vim = ReferenceState.card next parts[0]
                            let target = ReferenceState.card next parts[1]
                            let moved, movedEvent = moveCard vim.Id "Attached" target.Id next

                            next <-
                                ReferenceState.updateCard
                                    { target with
                                        Attachments = Array.append target.Attachments [| vim.Id |] }
                                    moved

                            events.Add movedEvent
                    | Some _ -> ()
                | ReferenceOpcode.MoveVim
                | ReferenceOpcode.ChuckVim ->
                    if mutation <> SkipProgramCardMovement then
                        for target in selectedTargets do
                            let attachedTo = target.AttachedTo
                            let moved, movedEvent = moveCard target.Id "EmptiesTray" "" next

                            next <-
                                if String.IsNullOrEmpty attachedTo then
                                    moved
                                else
                                    let owner = ReferenceState.card moved attachedTo

                                    ReferenceState.updateCard
                                        { owner with
                                            Attachments =
                                                owner.Attachments |> Array.filter ((<>) target.Id) }
                                        moved

                            events.Add movedEvent
                | ReferenceOpcode.SwapOche ->
                    if mutation <> SkipProgramCardMovement then
                        for incoming in selectedTargets do
                            match ReferenceState.cardsIn next incoming.Owner "Oche" with
                            | [||] -> ()
                            | outgoing ->
                                let outgoingCard = outgoing[0]
                                let booth, boothEvent = moveCard outgoingCard.Id "Booth" "" next
                                let oche, ocheEvent = moveCard incoming.Id "Oche" "" booth
                                next <- oche
                                events.Add boothEvent
                                events.Add ocheEvent

                                events.Add
                                    { ReferenceEvents.create "OcheSwapped" with
                                        Actor = incoming.Owner
                                        SourceCard = attacker.Id
                                        TargetCards = [| incoming.Id; outgoingCard.Id |]
                                        Effect = effect }
                | ReferenceOpcode.CopyAttack ->
                    match choiceBy (requirementId "attack") "Attack" selected with
                    | None -> ()
                    | Some choice ->
                        let copied =
                            candidates
                            |> Array.collect (fun value ->
                                authority.Cards[value.MechanicalId].Attacks)
                            |> Array.find (fun value -> value.MechanicalId = choice.Values[0])

                        let copiedState, copiedEvents, copiedTargets =
                            executeProgram
                                authority
                                mutation
                                selected
                                actor
                                attacker
                                effect
                                copied
                                (depth + 1)
                                next

                        next <- copiedState
                        events.AddRange copiedEvents
                        attackTargets.AddRange copiedTargets
                | ReferenceOpcode.ContinuousPartyTrick ->
                    let registered, registeredEvents =
                        registerEffect
                            next
                            actor
                            attacker
                            effect
                            "WhileSourceInPlay"
                            "ContinuousPartyTrick"
                            instruction
                            attacker

                    next <- registered
                    events.AddRange registeredEvents
                | ReferenceOpcode.EndRoundEffect ->
                    delayed <- true

                    for target in selectedTargets do
                        let registered, registeredEvents =
                            registerEffect
                                next
                                actor
                                attacker
                                effect
                                "UntilEndOfOpponentsNextRound"
                                "EndRoundEffect"
                                instruction
                                target

                        next <-
                            { registered with
                                Effects =
                                    registered.Effects
                                    |> Array.mapi (fun index value ->
                                        if index = registered.Effects.Length - 1 then
                                            { value with
                                                ExpiresAfterRound = next.RoundNumber + 1 }
                                        else
                                            value) }

                        events.AddRange registeredEvents
                | ReferenceOpcode.PreventDamage
                | ReferenceOpcode.PreventEffects
                | ReferenceOpcode.ReduceDamage
                | ReferenceOpcode.ModifyAttackCost
                | ReferenceOpcode.ModifyTaxiFare
                | ReferenceOpcode.ModifySoftSpot
                | ReferenceOpcode.RestrictAttack
                | ReferenceOpcode.RestrictTaxi
                | ReferenceOpcode.RestrictKit
                | ReferenceOpcode.RestrictLocal
                | ReferenceOpcode.RestrictEmptiesRecovery
                | ReferenceOpcode.ForceBeerMatBlank
                | ReferenceOpcode.ReflectAttackDamage ->
                    let kind =
                        if
                            instruction.Opcode = ReferenceOpcode.RestrictAttack
                            && instruction.Selection = ReferenceSelection.BeerMat
                        then
                            "RestrictAttackOnBeerMat"
                        else
                            string instruction.Opcode

                    let duration =
                        if instruction.Opcode = ReferenceOpcode.ModifySoftSpot then
                            "WhileTargetInPlay"
                        else
                            "UntilEndOfOpponentsNextRound"

                    let registeredInstruction =
                        if instruction.Opcode = ReferenceOpcode.ModifySoftSpot then
                            match choiceBy (requirementId "type") "MechanicalType" selected with
                            | Some choice ->
                                { instruction with
                                    MechanicalTypes =
                                        [| Enum.Parse<ReferenceMechanicalType> choice.Values[0] |] }
                            | None -> instruction
                        else
                            instruction

                    for target in selectedTargets do
                        let registered, registeredEvents =
                            registerEffect
                                next
                                actor
                                attacker
                                effect
                                duration
                                kind
                                registeredInstruction
                                target

                        next <- registered
                        events.AddRange registeredEvents
                | ReferenceOpcode.IgnoreStubbornStreak
                | ReferenceOpcode.IgnoreSoftSpotAndStubbornStreak -> ()
                | other -> invalidOp $"The BLOKEMON-135 interpreter cannot execute {other}."

                execute $"{path}/then/" instruction.Then

        execute "root/" attack.Program

        for path, instruction in damageInstructions do
            let requirementId suffix = $"{effect}:{path}:{suffix}"
            let candidates = candidatesFor authority next actor attacker instruction

            let selectedTargets =
                match instruction.Selection with
                | ReferenceSelection.Chosen
                | ReferenceSelection.OtherSideChosen ->
                    selectedCards (requirementId "cards") selected
                    |> Array.choose (ReferenceState.tryCard next)
                | ReferenceSelection.Top -> candidates |> Array.truncate instruction.Amount
                | _ -> candidates

            match instruction.Opcode with
            | ReferenceOpcode.DealPrintedDamage
            | ReferenceOpcode.ScaleDamage ->
                match targetsFor authority next actor attacker ReferenceLocation.OtherOche with
                | [||] -> ()
                | targets ->
                    let target = targets[0]
                    let amount = attackDamage authority next attacker target attack

                    let changed, damageEvents, damaged =
                        applyDamage mutation actor attacker target "Attack" amount next

                    next <- changed
                    events.AddRange damageEvents
                    attackTargets.AddRange damaged
            | ReferenceOpcode.DealBoothDamage ->
                for target in selectedTargets do
                    let damageKind = if target.Zone = "Oche" then "Attack" else "BoothAttack"

                    let changed, damageEvents, damaged =
                        applyDamage
                            mutation
                            actor
                            attacker
                            target
                            damageKind
                            instruction.Amount
                            next

                    next <- changed
                    events.AddRange damageEvents

                    if damageKind = "Attack" then
                        attackTargets.AddRange damaged
            | ReferenceOpcode.PlaceDamageCounters ->
                match choiceBy (requirementId "distribution") "Distribution" selected with
                | Some choice ->
                    for allocation in choice.Values do
                        let parts = allocation.Split(':')
                        let target = ReferenceState.card next parts[0]
                        let amount = Int32.Parse(parts[1]) * 10

                        let changed, damageEvents, _ =
                            applyDamage mutation actor attacker target "PlacedCounter" amount next

                        next <- changed
                        events.AddRange damageEvents
                | None ->
                    for target in selectedTargets do
                        let changed, damageEvents, _ =
                            applyDamage
                                mutation
                                actor
                                attacker
                                target
                                "PlacedCounter"
                                (instruction.Amount * 10)
                                next

                        next <- changed
                        events.AddRange damageEvents
            | ReferenceOpcode.DealSelfDamage ->
                let changed, damageEvents, _ =
                    applyDamage
                        mutation
                        actor
                        attacker
                        attacker
                        "SelfDamage"
                        instruction.Amount
                        next

                next <- changed
                events.AddRange damageEvents
            | other -> invalidOp $"Deferred deterministic damage cannot execute {other}."

        next, events.ToArray(), attackTargets.ToArray() |> Array.distinct

    let private rejection
        (state: CanonicalState)
        (code: string)
        (requirements: CanonicalChoiceRequirement array)
        =
        { State = state
          Events = [||]
          Rejection =
            [| { Code = code
                 ChoiceRequirements = requirements } |] }

    let apply
        (authority: ReferenceAuthority)
        (mutation: ReferenceProgramMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        let boundary =
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
            elif state.Phase <> "Playing" then
                Some "WrongPhase"
            elif state.ActivePlayer <> selected.Actor then
                Some "NotActorsTurn"
            else
                None

        match boundary with
        | Some code -> rejection state code [||]
        | None ->
            match validateChoices selected with
            | Error(code, requirements) -> rejection state code requirements
            | Ok() ->
                let refreshed, refreshEvents =
                    ReferenceCommonFoundation.refreshContinuousEffects authority state

                let parts = selected.Payload.Split(';')
                let attackerId = parts[0].Substring(9)
                let attackId = parts[1].Substring(7)

                match ReferenceState.tryCard refreshed attackerId with
                | None -> rejection state "EffectNotFound" [||]
                | Some attacker ->
                    match
                        authority.Cards[attacker.MechanicalId].Attacks
                        |> Array.tryFind (fun value -> value.MechanicalId = attackId)
                    with
                    | None -> rejection state "EffectNotFound" [||]
                    | Some attack when
                        attacker.Owner <> selected.Actor
                        || (attacker.Zone <> "Oche"
                            && not (attacker.Zone = "Booth" && attack.CanBeUsedFromBench))
                        || ReferenceCommonFoundation.roughPrevents
                            authority
                            _.PreventsAttack
                            attacker
                        ->
                        rejection state "EffectUnavailable" [||]
                    | Some attack when
                        not (
                            ReferenceCommonFoundation.canPayAttack
                                authority
                                refreshed
                                attacker
                                attack
                        )
                        ->
                        rejection state "InsufficientVim" [||]
                    | Some attack when
                        selected.Requirements
                        |> Array.exists (fun requirement -> requirement.Chooser <> selected.Actor)
                        ->
                        let deferredRequirements =
                            selected.Requirements
                            |> Array.filter (fun requirement ->
                                requirement.Chooser <> selected.Actor)

                        let chooser =
                            deferredRequirements
                            |> Array.map _.Chooser
                            |> Array.distinct
                            |> function
                                | [| value |] -> value
                                | values ->
                                    let rendered = String.Join(",", values)

                                    invalidOp
                                        $"The deterministic pending seam requires one chooser, but received {rendered}."

                        let pendingAction =
                            { selected with
                                Affordability = "Submitted"
                                Requirements = [||] }

                        let suspended =
                            ReferenceCommonFoundation.suspendForEffect
                                { Canonical.emptyPendingEffect with
                                    Present = true
                                    Action = [| pendingAction |]
                                    Source = attacker.Id
                                    Effect = attack.MechanicalId
                                    Chooser = chooser
                                    Requirements = deferredRequirements
                                    AttackStarted = true }
                                refreshed

                        let submitted =
                            { ReferenceEvents.create "CommandApplied" with
                                Actor = selected.Actor
                                Transport =
                                    { Canonical.emptyEventTransport with
                                        HasCommand = true } }

                        let requested =
                            { ReferenceEvents.create "EffectChoiceRequested" with
                                Actor = chooser
                                SourceCard = attacker.Id
                                Effect = attack.MechanicalId }

                        let declared =
                            { ReferenceEvents.create "AttackDeclared" with
                                Actor = selected.Actor
                                SourceCard = attacker.Id
                                Effect = attack.MechanicalId }

                        let beforeCommit =
                            { suspended with
                                Transport =
                                    { suspended.Transport with
                                        ProcessedCommandIds =
                                            Array.append
                                                suspended.Transport.ProcessedCommandIds
                                                [| selected.CommandId |] } }

                        let committed, events =
                            ReferenceEvents.commit
                                (state.Transport.Revision + 1L)
                                beforeCommit
                                (Array.concat
                                    [ [| submitted |]; refreshEvents; [| declared; requested |] ])

                        { State = committed
                          Events = events
                          Rejection = [||] }
                    | Some attack ->
                        let semanticEvents = ResizeArray<CanonicalEvent>()

                        semanticEvents.Add
                            { ReferenceEvents.create "AttackDeclared" with
                                Actor = selected.Actor
                                SourceCard = attacker.Id
                                Effect = attack.MechanicalId }

                        let executed, effectEvents, attackTargets =
                            executeProgram
                                authority
                                mutation
                                selected
                                selected.Actor
                                attacker
                                attack.MechanicalId
                                attack
                                0
                                refreshed

                        semanticEvents.AddRange effectEvents
                        let random = ReferenceRandom executed.Random

                        let knockedOut, knockoutEvents =
                            ReferenceCommonFoundation.resolveKnockouts
                                authority
                                NoReferenceMutation
                                random
                                attacker.Id
                                attackTargets
                                0
                                executed

                        semanticEvents.AddRange knockoutEvents

                        let finished, finishEvents =
                            ReferenceCommonFoundation.finishOrPendCommonRound
                                authority
                                NoReferenceMutation
                                random
                                knockedOut

                        semanticEvents.AddRange finishEvents

                        let submitted =
                            { ReferenceEvents.create "CommandApplied" with
                                Actor = selected.Actor
                                Transport =
                                    { Canonical.emptyEventTransport with
                                        HasCommand = true } }

                        let beforeCommit =
                            { finished with
                                Random = random.Snapshot
                                Transport =
                                    { finished.Transport with
                                        ProcessedCommandIds =
                                            Array.append
                                                finished.Transport.ProcessedCommandIds
                                                [| selected.CommandId |] } }

                        let committed, events =
                            ReferenceEvents.commit
                                (state.Transport.Revision + 1L)
                                beforeCommit
                                (Array.concat
                                    [ [| submitted |]
                                      refreshEvents
                                      semanticEvents.ToArray()
                                      |> ReferenceCommonFoundation.reorderRoundStartBeforeCheckup
                                          NoReferenceMutation ])

                        { State = committed
                          Events = events
                          Rejection = [||] }
