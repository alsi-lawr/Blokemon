namespace Blokemon.ReferenceModel

open System

type ReferenceProgramMutation =
    | NoProgramMutation
    | SkipProgramDamagePlacement
    | SkipProgramCardMovement
    | ReverseProgramCandidateOrder
    | FlipProgramBeerMatResult
    | InvertProgramBranch

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
            | "day-two-forced-blank" -> "BLK-001"
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
        | "dynamic-adjust" ->
            let source = parameters[5]
            let units = Int32.Parse parameters[6]

            match source with
            | "OwnBoothCount" ->
                for index in 0 .. units - 1 do
                    ensureOwnBooth $"own-count-{index}"
            | "OtherBoothCount" ->
                for index in 0 .. units - 1 do
                    ensureOtherBooth $"other-count-{index}"
            | "OtherAttachedVim" ->
                for index in 0 .. units - 1 do
                    let id = $"other-count-vim-{index}"
                    add "second" id (basicVimFor authority ReferenceMechanicalType.Water) "Mitt" -1
                    cards <- attach id "defender" cards
            | "SelfDamageCounters" ->
                cards <-
                    updateCard "attacker" (fun value -> { value with Damage = units * 10 }) cards
            | "OtherOcheDamageCounters" ->
                cards <-
                    updateCard "defender" (fun value -> { value with Damage = units * 10 }) cards
            | "OwnAttachedVim" -> ()
            | unknown -> invalidOp $"Unknown dynamic value source {unknown}."

            ensureOtherBooth "other-reserve"
        | "coin-branch" ->
            add
                "second"
                "other-vim-0"
                (basicVimFor authority ReferenceMechanicalType.Water)
                "Mitt"
                -1

            cards <- attach "other-vim-0" "defender" cards
            ensureOtherBooth "other-reserve"
        | "conditional-adjust" ->
            let condition = parameters[5]
            let enabled = parameters[6] = "true"
            let related = parameters[7]

            if enabled then
                match condition with
                | "SelfHasDamage" ->
                    cards <- updateCard "attacker" (fun value -> { value with Damage = 10 }) cards
                | "OtherOcheHasDamage" ->
                    cards <- updateCard "defender" (fun value -> { value with Damage = 10 }) cards
                | "NamedBlokeInBooth" -> add "first" "named-condition" related "Booth" -1
                | _ -> ()
            elif condition = "MittCountsAreEqual" || condition = "OwnMittIsEmpty" then
                add
                    "first"
                    "own-mitt-condition"
                    (basicVimFor authority ReferenceMechanicalType.Grass)
                    "Mitt"
                    -1

            if condition <> "OtherBoothExists" || enabled then
                ensureOtherBooth "other-reserve"
        | "conditional-demote" ->
            let lowerStage = parameters[3]
            add "second" "lower-stage" lowerStage "Attached" -1

            cards <-
                cards
                |> updateCard "lower-stage" (fun value -> { value with AttachedTo = "defender" })
                |> updateCard "defender" (fun value ->
                    { value with
                        UnderlyingCards = [| "lower-stage" |]
                        LastPromotedRound = 2 })
        | "conditional-extra-bar" ->
            cards <- updateCard "defender" (fun value -> { value with Damage = 20 }) cards
        | "conditional-rough" ->
            if parameters[3] = "true" then
                let target, rough =
                    if parameters[2] = "self" then
                        "attacker", "Muddled"
                    else
                        "defender", "NoddedOff"

                cards <-
                    updateCard
                        target
                        (fun value ->
                            { value with
                                RoughStates =
                                    [| { State = rough
                                         AppliedAtOwnerRound = 1 } |] })
                        cards

            ensureOtherBooth "other-reserve"
        | "multi-toss-damage"
        | "repeat-damage"
        | "coin-effects"
        | "shirt-off-badge"
        | "shirt-off-blank"
        | "still-coming-up-promoted"
        | "still-coming-up-not-promoted" -> ensureOtherBooth "other-reserve"
        | "repeat-draw" ->
            add
                "first"
                "effect-draw-1"
                (basicVimFor authority ReferenceMechanicalType.Grass)
                "Stack"
                1

            ensureOtherBooth "other-reserve"
        | "coin-swap" -> ensureOtherBooth "a-other-booth"
        | "full-booth-search" ->
            for index in 0..4 do
                add "first" $"full-booth-{index}" "BLK-004" "Booth" index

            add "first" "search-card" parameters[2] "Stack" 0
        | "booth-search" ->
            let count = Int32.Parse parameters[4]

            for index in 1..count do
                add "first" $"candidate-{index}" parameters[2] "Stack" index
        | "coin-search" ->
            let count = Int32.Parse parameters[3]

            for index in 1..count do
                add
                    "first"
                    $"candidate-{index}"
                    (basicVimFor authority ReferenceMechanicalType.Water)
                    "Stack"
                    index

            ensureOtherBooth "other-reserve"
        | "optional-zero"
        | "optional-decline" ->
            let setup = parameters[2]
            let candidate = parameters[3]

            if candidate <> "none" && candidate <> "first-draw" then
                let mechanicalId, zone =
                    match setup with
                    | "recover-water" ->
                        basicVimFor authority ReferenceMechanicalType.Water, "EmptiesTray"
                    | "recover-bloke" -> "BLK-001", "EmptiesTray"
                    | "recover-barbit" -> "KIT-001", "EmptiesTray"
                    | "recover-fire" ->
                        basicVimFor authority ReferenceMechanicalType.Fire, "EmptiesTray"
                    | "mitt-water" -> basicVimFor authority ReferenceMechanicalType.Water, "Mitt"
                    | "stack-bloke" -> "BLK-001", "Stack"
                    | unknown -> invalidOp $"Unknown optional setup {unknown}."

                add "first" candidate mechanicalId zone (if zone = "Stack" then 1 else -1)

            ensureOtherBooth "other-reserve"
        | "optional-max" ->
            let setup = parameters[2]
            let count = Int32.Parse parameters[3]
            let owner = input.ReviewedProgram.OwnerId
            let firstIndex = if owner = "BLK-022" then 1 else 0

            if owner = "BLK-022" then
                add "first" "first-draw" "BLK-001" "Stack" 0

            let mechanicalId index =
                match setup with
                | "recover-water"
                | "mitt-water" -> basicVimFor authority ReferenceMechanicalType.Water
                | "recover-bloke"
                | "stack-bloke" -> "BLK-001"
                | "stack-distinct-bloke" -> [| "BLK-001"; "BLK-004"; "BLK-007" |][index - 1]
                | "recover-barbit" -> "KIT-001"
                | "recover-fire" -> basicVimFor authority ReferenceMechanicalType.Fire
                | unknown -> invalidOp $"Unknown optional maximum setup {unknown}."

            let zone =
                match setup with
                | "recover-water"
                | "recover-bloke"
                | "recover-barbit"
                | "recover-fire" -> "EmptiesTray"
                | "mitt-water" -> "Mitt"
                | "stack-bloke"
                | "stack-distinct-bloke" -> "Stack"
                | unknown -> invalidOp $"Unknown optional maximum zone {unknown}."

            for index in 1 .. count - firstIndex do
                add
                    "first"
                    $"candidate-{index}"
                    (mechanicalId index)
                    zone
                    (if zone = "Stack" then index else -1)

            ensureOtherBooth "other-reserve"
        | "optional-invalid-duplicate" ->
            add "first" "candidate-1" "BLK-001" "Stack" 1
            add "first" "candidate-2" "BLK-010" "Stack" 2
            ensureOtherBooth "other-reserve"
        | "optional-bar-kit" ->
            add "first" "bar-kit" "KIT-004" "Attached" -1
            add "first" "own-booth" "BLK-003" "Booth" -1
            add "first" "bar-kit-2" "KIT-004" "Attached" -1

            cards <-
                cards
                |> updateCard "bar-kit" (fun value -> { value with AttachedTo = "attacker" })
                |> updateCard "attacker" (fun value ->
                    { value with
                        Attachments = Array.append value.Attachments [| "bar-kit" |] })
                |> updateCard "bar-kit-2" (fun value -> { value with AttachedTo = "own-booth" })
                |> updateCard "own-booth" (fun value ->
                    { value with
                        Attachments = [| "bar-kit-2" |] })

            ensureOtherBooth "other-reserve"
        | "search-all" ->
            add "first" parameters[3] parameters[2] "Stack" 1
            ensureOtherBooth "other-reserve"
        | "top-qualifying" ->
            for index in 1..4 do
                add
                    "first"
                    $"top-{index}"
                    (if parameters |> Array.contains "zero" then
                         basicVimFor authority ReferenceMechanicalType.Grass
                     else if index = 1 then
                         "BLK-001"
                     else
                         basicVimFor authority ReferenceMechanicalType.Grass)
                    "Stack"
                    index

            ensureOtherBooth "other-reserve"
        | "gone-smoke" ->
            ensureOwnBooth "own-booth"
            ensureOtherBooth "other-booth"
        | "day-two-forced-blank" ->
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

            cards <- attach "other-vim-0" "defender" cards
            cards <- attach "other-vim-1" "defender" cards
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

        let roundUsage =
            if
                route = "conditional-adjust"
                && parameters[5] = "MatePlayedThisRound"
                && parameters[6] = "true"
            then
                { Player = "first"
                  VimAttachments = 0
                  MatesPlayed = 1
                  LocalsPlayed = 0
                  TaxisUsed = 0
                  EffectsUsed = usedActivatedEffects
                  KitsPlayed = [| parameters[7] |] }
            else
                { Player = "first"
                  VimAttachments = 0
                  MatesPlayed = 0
                  LocalsPlayed = 0
                  TaxisUsed = 0
                  EffectsUsed = usedActivatedEffects
                  KitsPlayed = [||] }

        let players =
            if
                route = "conditional-adjust"
                && parameters[5] = "OwnBarChitCountIsGreater"
                && parameters[6] = "true"
            then
                players
                |> Array.map (fun value ->
                    if value.Id = "second" then
                        { value with BarChitsRemaining = 5 }
                    else
                        value)
            else
                players

        let cards =
            if route = "still-coming-up-promoted" || route = "still-coming-up-not-promoted" then
                add "first" "own-lower-stage" "BLK-079" "Attached" -1

                cards
                |> updateCard "own-lower-stage" (fun value ->
                    { value with AttachedTo = "attacker" })
                |> updateCard "attacker" (fun value ->
                    { value with
                        UnderlyingCards = [| "own-lower-stage" |]
                        LastPromotedRound = if route = "still-coming-up-promoted" then 3 else 2 })
            else
                cards

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
          RoundUsage = roundUsage
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

    let materializeInput authority input = standardState authority input

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
            | ReferenceLocation.OwnAttachedBarKits ->
                state.Cards
                |> Array.filter (fun value -> value.Owner = actor && inPlay value)
                |> Array.collect (fun value ->
                    value.Attachments |> Array.map (ReferenceState.card state))
                |> Array.filter (fun value ->
                    value.Kind = "Kit"
                    && authority.Cards[value.MechanicalId].KitKind = ValueSome
                        ReferenceKitKind.BarKit)
            | ReferenceLocation.BarChits -> cards actor "BarChit"
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
                && instruction.Opcode <> ReferenceOpcode.SearchStack
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

        let ordered =
            if
                locations
                |> Array.exists (fun location ->
                    location = ReferenceLocation.OwnStack
                    || location = ReferenceLocation.OtherStack)
            then
                candidates |> Array.sortBy (fun value -> value.StackPosition, value.Id)
            else
                candidates |> Array.sortBy _.Id

        ordered
        |> Array.distinctBy _.Id
        |> filteredCards authority instruction
        |> Array.filter (fun value ->
            registersPersistentEffect instruction.Opcode
            || (instruction.Predicates
                |> Array.forall (predicateAllows authority state attacker value)))

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

        let candidates =
            candidatesFor authority state actor attacker instruction |> Array.sortBy _.Id

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

        let ownsCardChoice =
            if
                instruction.Opcode = ReferenceOpcode.MoveCards
                && instruction.Destination.IsSome
                && instruction.Sources.Length = 0
            then
                false
            else
                match instruction.Selection with
                | ReferenceSelection.Chosen
                | ReferenceSelection.OtherSideChosen
                | ReferenceSelection.UpTo ->
                    match instruction.Opcode with
                    | ReferenceOpcode.DealBoothDamage
                    | ReferenceOpcode.PlaceDamageCounters
                    | ReferenceOpcode.HealDamage
                    | ReferenceOpcode.ApplyRoughState
                    | ReferenceOpcode.SearchStack
                    | ReferenceOpcode.MoveCards
                    | ReferenceOpcode.ChuckCards
                    | ReferenceOpcode.AttachVim
                    | ReferenceOpcode.MoveVim
                    | ReferenceOpcode.ChuckVim
                    | ReferenceOpcode.SwapOche
                    | ReferenceOpcode.SendHome
                    | ReferenceOpcode.TransformFromStack -> true
                    | _ -> false
                | _ ->
                    instruction.Opcode = ReferenceOpcode.ChuckCards
                    && Array.contains ReferenceLocation.OtherMitt instruction.Targets

        let boothCapacity =
            match instruction.Destination with
            | ValueSome ReferenceDestination.OwnBooth ->
                max
                    0
                    (authority.BaseRules.Opening.BoothLimit
                     - (ReferenceState.cardsIn state actor "Booth").Length)
                |> ValueSome
            | ValueSome ReferenceDestination.OtherBooth ->
                max
                    0
                    (authority.BaseRules.Opening.BoothLimit
                     - (ReferenceState.cardsIn state (otherPlayer state actor) "Booth").Length)
                |> ValueSome
            | _ -> ValueNone

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
            | _ when
                ownsCardChoice
                && not (
                    instruction.Opcode = ReferenceOpcode.SearchStack && boothCapacity = ValueSome 0
                )
                ->
                let selectionMaximum =
                    match instruction.Selection with
                    | ReferenceSelection.Chosen
                    | ReferenceSelection.OtherSideChosen ->
                        min instruction.TargetCount cardIds.Length
                    | ReferenceSelection.UpTo
                    | ReferenceSelection.All -> min instruction.Amount cardIds.Length
                    | _ -> 0

                let maximum =
                    match boothCapacity with
                    | ValueSome capacity -> min selectionMaximum capacity
                    | ValueNone -> selectionMaximum

                if maximum <> 0 || instruction.Selection = ReferenceSelection.UpTo then
                    [| { emptyRequirement
                             (id "cards")
                             "Cards"
                             chooser
                             (if instruction.Selection = ReferenceSelection.UpTo then
                                  0
                              else
                                  maximum)
                             maximum with
                           EligibleCards = cardIds
                           EligibleCardTypes = cardTypes
                           RequireDifferentMechanicalTypes =
                               instruction.CardFilter
                               |> ValueOption.exists _.DifferentMechanicalTypes } |]
                else
                    [||]
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
        let requirements = ResizeArray<CanonicalChoiceRequirement>()

        let rec inspect
            (parent: string)
            (optionalDependency: string)
            (program: ReferenceInstruction array)
            =
            for index in 0 .. program.Length - 1 do
                let instruction = program[index]
                let path = $"{parent}{index}"

                let own =
                    instructionRequirements authority state actor attacker effect path instruction

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

                let deferBranches =
                    instruction.Opcode = ReferenceOpcode.BeerMatToss
                    || instruction.Opcode = ReferenceOpcode.RepeatUntilBlankSide
                    || (instruction.Opcode = ReferenceOpcode.MoveCards
                        && instruction.Then.Length > 0)
                    || instruction.Opcode = ReferenceOpcode.Conditional

                if not deferBranches then
                    inspect $"{path}/then/" dependency instruction.Then
                    inspect $"{path}/otherwise/" optionalDependency instruction.Otherwise

        inspect prefix "" instructions
        requirements.ToArray() |> Array.distinctBy _.Id

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

    let private optionalChoice (id: string) (selected: CanonicalAction) =
        choiceBy id "Optional" selected
        |> Option.bind (fun choice -> choice.Values |> Array.tryExactlyOne)
        |> Option.map Boolean.Parse

    let private conditionAllows
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (selected: CanonicalAction)
        (path: string)
        (firstBeerMatIsBlank: bool)
        (pendingAttackDamage: int)
        (predicate: ReferencePredicate)
        =
        let other = otherPlayer state actor
        let otherOche = ReferenceState.cardsIn state other "Oche" |> Array.tryHead

        match predicate.Condition with
        | ReferenceCondition.Optional ->
            let payloadParts = selected.Payload.Split(';')
            let effectId = payloadParts[1].Substring(7)

            optionalChoice $"{effectId}:{path}:optional" selected
            |> Option.defaultValue false
        | ReferenceCondition.FirstBeerMatIsBlankSide -> firstBeerMatIsBlank
        | ReferenceCondition.SelfIsAtOche -> attacker.Zone = "Oche"
        | ReferenceCondition.SelfIsInBooth -> attacker.Zone = "Booth"
        | ReferenceCondition.SelfHasDamage -> attacker.Damage > 0
        | ReferenceCondition.SelfHasVim -> attacker.Attachments.Length > 0
        | ReferenceCondition.SelfHasRoughState ->
            predicate.RoughState
            |> ValueOption.exists (fun rough ->
                attacker.RoughStates |> Array.exists (fun value -> value.State = string rough))
        | ReferenceCondition.OwnMittIsEmpty ->
            ReferenceState.cardsIn state actor "Mitt" |> Array.isEmpty
        | ReferenceCondition.MittCountsAreEqual ->
            let ownMittCount = (ReferenceState.cardsIn state actor "Mitt").Length
            let otherMittCount = (ReferenceState.cardsIn state other "Mitt").Length
            ownMittCount = otherMittCount
        | ReferenceCondition.MatePlayedThisRound ->
            state.RoundUsage.MatesPlayed > 0
            && (predicate.RelatedId
                |> ValueOption.forall (fun id -> Array.contains id state.RoundUsage.KitsPlayed))
        | ReferenceCondition.NamedBlokeInPlay ->
            predicate.RelatedId
            |> ValueOption.exists (fun id ->
                state.Cards
                |> Array.exists (fun value ->
                    value.Owner = actor && value.MechanicalId = id && inPlay value))
        | ReferenceCondition.NamedBlokeInBooth ->
            predicate.RelatedId
            |> ValueOption.exists (fun id ->
                ReferenceState.cardsIn state actor "Booth"
                |> Array.exists (fun value -> value.MechanicalId = id))
        | ReferenceCondition.OtherOcheHasMechanicalType ->
            otherOche
            |> Option.exists (fun otherCard ->
                predicate.MechanicalType
                |> ValueOption.forall (fun mechanicalType ->
                    Array.contains
                        mechanicalType
                        authority.Cards[otherCard.MechanicalId].MechanicalTypes))
        | ReferenceCondition.OtherOcheHasDamage ->
            otherOche |> Option.exists (fun value -> value.Damage > 0)
        | ReferenceCondition.OtherOcheHasRoughState ->
            match otherOche, predicate.RoughState with
            | Some card, ValueSome rough ->
                card.RoughStates |> Array.exists (fun value -> value.State = string rough)
            | _ -> false
        | ReferenceCondition.OtherOcheIsPromoted ->
            otherOche |> Option.exists (fun value -> value.UnderlyingCards.Length > 0)
        | ReferenceCondition.OtherOcheIsBigHitter ->
            otherOche
            |> Option.exists (fun value ->
                authority.BaseRules.BigHitterIds.Contains value.MechanicalId)
        | ReferenceCondition.AttachedVimCountsAreEqual ->
            let ownCount =
                ReferenceState.cardsIn state actor "Oche"
                |> Array.tryHead
                |> Option.map _.Attachments.Length
                |> Option.defaultValue 0

            let otherCount =
                otherOche |> Option.map _.Attachments.Length |> Option.defaultValue 0

            ownCount = otherCount
        | ReferenceCondition.OwnBarChitCountIsGreater ->
            let ownBarChits = (ReferenceState.player state actor).BarChitsRemaining
            let otherBarChits = (ReferenceState.player state other).BarChitsRemaining
            ownBarChits > otherBarChits
        | ReferenceCondition.OtherBoothExists ->
            ReferenceState.cardsIn state other "Booth" |> Array.isEmpty |> not
        | ReferenceCondition.BoothHasSpace ->
            (ReferenceState.cardsIn state actor "Booth").Length < authority.BaseRules.Opening.BoothLimit
        | ReferenceCondition.OtherSentHomeByThisAttackDamage ->
            otherOche
            |> Option.exists (fun value ->
                value.Damage + pendingAttackDamage
                >= authority.Cards[value.MechanicalId].StayingPower)
        | ReferenceCondition.PromotedFromMittThisRound ->
            attacker.LastPromotedRound = state.RoundNumber
        | ReferenceCondition.SourceIsRegular ->
            authority.Cards[attacker.MechanicalId].Rank = ValueSome ReferenceRank.Regular
        | ReferenceCondition.TargetHasDamage -> false
        | _ -> false

    let private resolvedValue
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (attacker: CanonicalCard)
        (badgeSides: int)
        (cardsChucked: int)
        (qualifyingChucked: int)
        (instruction: ReferenceInstruction)
        =
        let other = otherPlayer state actor

        match instruction.ValueSource with
        | ReferenceValueSource.Fixed -> 1
        | ReferenceValueSource.PrintedDamage -> instruction.Amount
        | ReferenceValueSource.SelfDamageCounters -> attacker.Damage / 10
        | ReferenceValueSource.OtherOcheDamageCounters ->
            ReferenceState.cardsIn state other "Oche"
            |> Array.tryHead
            |> Option.map (fun value -> value.Damage / 10)
            |> Option.defaultValue 0
        | ReferenceValueSource.OtherBoothCount ->
            (ReferenceState.cardsIn state other "Booth").Length
        | ReferenceValueSource.OwnBoothCount -> (ReferenceState.cardsIn state actor "Booth").Length
        | ReferenceValueSource.OwnAttachedVim ->
            attacker.Attachments
            |> Array.map (ReferenceState.card state)
            |> Array.filter (fun value ->
                value.Kind = "Vim"
                && (instruction.MechanicalTypes.Length = 0
                    || authority.Cards[value.MechanicalId].VimType
                       |> ValueOption.exists (fun vimType ->
                           Array.contains vimType instruction.MechanicalTypes)))
            |> Array.length
        | ReferenceValueSource.OtherAttachedVim ->
            ReferenceState.cardsIn state other "Oche"
            |> Array.tryHead
            |> Option.map _.Attachments.Length
            |> Option.defaultValue 0
        | ReferenceValueSource.BadgeSides -> badgeSides
        | ReferenceValueSource.CardsChuckedByEffect -> cardsChucked
        | ReferenceValueSource.KitCardsInOtherMitt ->
            ReferenceState.cardsIn state other "Mitt"
            |> Array.filter (fun value -> value.Kind = "Kit")
            |> Array.length
        | ReferenceValueSource.QualifyingChuckedCards -> qualifyingChucked
        | ReferenceValueSource.MittCardsNeeded ->
            max 0 (instruction.Amount - (ReferenceState.cardsIn state actor "Mitt").Length)
        | other -> invalidOp $"The branching value source is unsupported: {int other}."

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

    let private orderedAttackDamage
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (attack: ReferenceAttack)
        (baseDamage: int)
        =
        let mutable amount = baseDamage

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

    let private attackDamage
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (attack: ReferenceAttack)
        =
        let mutable baseDamage = attack.PrintedDamage

        for instruction in attack.Program do
            if attack.VariablePrintedDamage && instruction.Opcode = ReferenceOpcode.ScaleDamage then
                baseDamage <-
                    match instruction.ValueSource with
                    | ReferenceValueSource.Fixed -> instruction.Amount
                    | ReferenceValueSource.KitCardsInOtherMitt ->
                        ReferenceState.cardsIn state target.Owner "Mitt"
                        |> Array.filter (fun value -> value.Kind = "Kit")
                        |> Array.length
                        |> (*) instruction.Amount
                    | other -> invalidOp $"The deterministic damage scaler cannot read {other}."

        orderedAttackDamage authority state attacker target attack baseDamage

    let private validateOwnedChoices
        (authority: ReferenceAuthority)
        (actor: string)
        (choices: CanonicalChoice array)
        (requirements: CanonicalChoiceRequirement array)
        =
        let owned =
            requirements |> Array.filter (fun requirement -> requirement.Chooser = actor)

        let mutable error: (string * CanonicalChoiceRequirement array) option = None
        let missing = ResizeArray<CanonicalChoiceRequirement>()

        let differentTypes requirement values =
            let mutable used = Set.empty

            values
            |> Array.forall (fun card ->
                match
                    requirement.EligibleCardTypes |> Array.tryFind (fun value -> value.Card = card)
                with
                | None -> false
                | Some types ->
                    let current = types.MechanicalTypes |> Set.ofArray
                    let valid = not current.IsEmpty && Set.intersect used current |> Set.isEmpty
                    used <- Set.union used current
                    valid)

        for requirement in owned do
            if error.IsNone then
                let declined =
                    if String.IsNullOrEmpty requirement.DependsOnOptional then
                        false
                    else
                        choices
                        |> Array.tryFind (fun value ->
                            value.Id = requirement.DependsOnOptional && value.Kind = "Optional")
                        |> Option.bind (fun value -> value.Values |> Array.tryExactlyOne)
                        |> Option.map (Boolean.Parse >> not)
                        |> Option.defaultValue false

                if not declined then
                    let matching = choices |> Array.filter (fun value -> value.Id = requirement.Id)

                    if matching.Length = 0 then
                        missing.Add requirement
                    elif matching.Length <> 1 then
                        error <- Some("InvalidChoice", [| requirement |])
                    else
                        let choice = matching[0]

                        let valid =
                            match requirement.Kind, choice.Kind with
                            | "Optional", "Optional" -> choice.Values.Length = 1
                            | "Cards", "Cards" ->
                                choice.Values.Length >= requirement.Minimum
                                && choice.Values.Length <= requirement.Maximum
                                && (choice.Values |> Array.distinct).Length = choice.Values.Length
                                && choice.Values
                                   |> Array.forall (fun value ->
                                       Array.contains value requirement.EligibleCards)
                                && (not requirement.RequireDifferentMechanicalTypes
                                    || differentTypes requirement choice.Values)
                            | "MechanicalType", "MechanicalType" ->
                                choice.Values.Length = 1
                                && Array.contains
                                    choice.Values[0]
                                    requirement.EligibleMechanicalTypes
                            | "Attack", "Attack" ->
                                choice.Values.Length = 1
                                && Array.contains choice.Values[0] requirement.EligibleEffects
                            | "Distribution", "Distribution" ->
                                let allocations =
                                    choice.Values
                                    |> Array.map (fun value ->
                                        let parts = value.Split(':')
                                        parts[0], Int32.Parse parts[1])

                                (allocations |> Array.sumBy snd) = requirement.Maximum
                                && (allocations |> Array.map fst |> Array.distinct).Length = allocations.Length
                                && allocations
                                   |> Array.forall (fun (card, counters) ->
                                       counters >= 0
                                       && Array.contains card requirement.EligibleCards)
                            | "Attachments", "Attachments" ->
                                let placements =
                                    choice.Values
                                    |> Array.map (fun value ->
                                        value.Split("->", StringSplitOptions.None))

                                placements.Length >= requirement.Minimum
                                && placements.Length <= requirement.Maximum
                                && (placements
                                    |> Array.map (fun value -> value[0])
                                    |> Array.distinct)
                                    .Length = placements.Length
                                && placements
                                   |> Array.forall (fun value ->
                                       Array.contains value[0] requirement.EligibleCards
                                       && Array.contains value[1] requirement.EligibleTargets)
                            | _ -> false

                        if not valid then
                            error <- Some("InvalidChoice", [| requirement |])

        if error.IsNone then
            let ownedIds = owned |> Array.map _.Id |> Set.ofArray

            if choices |> Array.exists (fun choice -> not (ownedIds.Contains choice.Id)) then
                error <- Some("InvalidChoice", owned)

        match error with
        | Some value -> Error value
        | None when missing.Count <> 0 -> Error("ChoiceRequired", missing.ToArray())
        | None -> Ok()

    let private validateChoices authority (selected: CanonicalAction) =
        validateOwnedChoices authority selected.Actor selected.Choices selected.Requirements

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
        (recordedBeerMats: bool array)
        (state: CanonicalState)
        =
        if depth > 8 then
            invalidOp "CopyAttack exceeded the deterministic recursive dispatch bound."

        let events = ResizeArray<CanonicalEvent>()
        let attackTargets = ResizeArray<string>()
        let forcedSendHome = ResizeArray<string>()
        let damageInstructions = ResizeArray<string * ReferenceInstruction>()
        let resolvedDamageInstructions = ResizeArray<string>()
        let beerMatResults = ResizeArray<bool>(recordedBeerMats)
        let random = ReferenceRandom(state.Random)
        let mutable replayedBeerMats = 0
        let mutable next = state
        let mutable delayed = false
        let mutable lastSelectedCards: string array = [||]
        let mutable hasCardSelection = false
        let mutable badgeSides = 0
        let mutable tossCount = 0
        let mutable firstBeerMatIsBlank = false
        let mutable beerMatGateParent = ""
        let mutable cardsChucked = 0
        let mutable qualifyingChucked = 0
        let mutable aggregateAttackDamage = 0
        let mutable aggregateDamage = false
        let mutable aggregateDamageResolved = false
        let mutable deferredRequirements: CanonicalChoiceRequirement array = [||]

        let mutable executionRejection: (string * CanonicalChoiceRequirement array) option =
            None

        let mutable extraBarChits = 0

        let playerForcesBlank () =
            next.Effects
            |> Array.exists (fun value ->
                value.Owner <> actor
                && value.Kind = "ForceBeerMatBlank"
                && value.AppliesFromRound <= next.RoundNumber)

        let nextBeerMat () =
            let replayed = replayedBeerMats < recordedBeerMats.Length

            let badge =
                if replayed then
                    recordedBeerMats[replayedBeerMats]
                else
                    let raw = random.NextInt(2) = 1
                    let actual = if playerForcesBlank () then false else raw

                    if mutation = FlipProgramBeerMatResult then
                        not actual
                    else
                        actual

            replayedBeerMats <- replayedBeerMats + 1

            if not replayed then
                beerMatResults.Add badge

                events.Add
                    { ReferenceEvents.create "BeerMatTossed" with
                        Actor = actor
                        SourceCard = attacker.Id
                        Effect = effect
                        HasBadgeSide = true
                        BadgeSide = badge }

            badge

        let shuffleStack owner excluded =
            let excluded = excluded |> Set.ofArray

            let stack =
                ReferenceState.cardsIn next owner "Stack"
                |> Array.filter (fun value -> not (excluded.Contains value.Id))
                |> Array.sortBy _.Id

            for index in stack.Length - 1 .. -1 .. 1 do
                let swapIndex = random.NextInt(index + 1)
                let held = stack[index]
                stack[index] <- stack[swapIndex]
                stack[swapIndex] <- held

            let positions =
                stack |> Array.mapi (fun index value -> value.Id, index) |> Map.ofArray

            next <-
                { next with
                    Cards =
                        next.Cards
                        |> Array.map (fun value ->
                            match positions.TryFind value.Id with
                            | Some position -> { value with StackPosition = position }
                            | None -> value) }

            events.Add
                { ReferenceEvents.create "CardsShuffled" with
                    Actor = owner }

        let detachAndMove id zone =
            let current = ReferenceState.card next id
            let attachedTo = current.AttachedTo
            let moved, movedEvent = moveCard id zone "" next

            next <-
                if String.IsNullOrEmpty attachedTo then
                    moved
                else
                    let parent = ReferenceState.card moved attachedTo

                    ReferenceState.updateCard
                        { parent with
                            Attachments = parent.Attachments |> Array.filter ((<>) id) }
                        moved

            events.Add movedEvent

        let moveBlokeToStack (value: CanonicalCard) =
            let related =
                Array.concat [ value.Attachments; value.UnderlyingCards; [| value.Id |] ]
                |> Array.distinct

            for id in related do
                let moved, movedEvent = moveCard id "Stack" "" next
                let current = ReferenceState.card moved id

                next <-
                    ReferenceState.updateCard
                        { current with
                            Attachments = [||]
                            UnderlyingCards = [||]
                            Damage = 0
                            RoughStates = [||] }
                        moved

                events.Add movedEvent

        let branchRequirements path branch =
            requirementsForProgram authority next actor attacker effect path branch

        let branchChoices (requirements: CanonicalChoiceRequirement array) =
            let ids = requirements |> Array.map _.Id |> Set.ofArray
            selected.Choices |> Array.filter (fun value -> ids.Contains value.Id)

        let rec execute prefix (instructions: ReferenceInstruction array) =
            let mutable index = 0

            while (index < instructions.Length
                   && deferredRequirements.Length = 0
                   && executionRejection.IsNone) do
                let instruction = instructions[index]
                let path = $"{prefix}{index}"
                let requirementId suffix = $"{effect}:{path}:{suffix}"

                if
                    String.IsNullOrEmpty beerMatGateParent
                    || beerMatGateParent <> prefix
                    || badgeSides <> 0
                    || instruction.Opcode = ReferenceOpcode.BeerMatToss
                then
                    let candidates =
                        let values = candidatesFor authority next actor attacker instruction

                        if mutation = ReverseProgramCandidateOrder then
                            Array.rev values
                        else
                            values

                    let chosenCards () =
                        selectedCards (requirementId "cards") selected
                        |> Array.choose (ReferenceState.tryCard next)

                    let selectedTargets =
                        match instruction.Selection with
                        | ReferenceSelection.Chosen when
                            instruction.Opcode = ReferenceOpcode.ModifySoftSpot
                            ->
                            candidates
                        | ReferenceSelection.Chosen
                        | ReferenceSelection.OtherSideChosen
                        | ReferenceSelection.UpTo -> chosenCards ()
                        | ReferenceSelection.Top -> candidates |> Array.truncate instruction.Amount
                        | ReferenceSelection.BeerMat when
                            badgeSides = 0 && instruction.Opcode <> ReferenceOpcode.RestrictAttack
                            ->
                            [||]
                        | ReferenceSelection.All when
                            instruction.Opcode = ReferenceOpcode.SearchStack
                            ->
                            candidates |> Array.truncate instruction.Amount
                        | ReferenceSelection.All when instruction.TargetCount > 1 ->
                            candidates |> Array.truncate instruction.TargetCount
                        | _ -> candidates

                    let mutable runThen = true

                    match instruction.Opcode with
                    | ReferenceOpcode.DealPrintedDamage ->
                        damageInstructions.Add(path, instruction)
                        aggregateAttackDamage <- aggregateAttackDamage + instruction.Amount
                    | ReferenceOpcode.AdjustDamage ->
                        aggregateDamage <- true

                        aggregateAttackDamage <-
                            aggregateAttackDamage
                            + instruction.Amount
                              * resolvedValue
                                  authority
                                  next
                                  actor
                                  (ReferenceState.card next attacker.Id)
                                  badgeSides
                                  cardsChucked
                                  qualifyingChucked
                                  instruction
                    | ReferenceOpcode.DealBoothDamage
                    | ReferenceOpcode.DealSelfDamage -> damageInstructions.Add(path, instruction)
                    | ReferenceOpcode.PlaceDamageCounters when not delayed ->
                        damageInstructions.Add(path, instruction)
                    | ReferenceOpcode.PlaceDamageCounters -> ()
                    | ReferenceOpcode.ScaleDamage when attack.VariablePrintedDamage ->
                        aggregateDamage <- true

                        if
                            not (
                                instruction.ValueSource = ReferenceValueSource.Fixed
                                && (instruction.Selection = ReferenceSelection.BeerMat
                                    || instruction.Selection = ReferenceSelection.UntilBlankSide)
                            )
                        then
                            aggregateAttackDamage <-
                                aggregateAttackDamage
                                + instruction.Amount
                                  * resolvedValue
                                      authority
                                      next
                                      actor
                                      (ReferenceState.card next attacker.Id)
                                      badgeSides
                                      cardsChucked
                                      qualifyingChucked
                                      instruction
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
                                    current.RoughStates
                                    |> Array.exists (fun value -> value.State = string rough)
                                    |> not
                                then
                                    next <-
                                        ReferenceState.updateCard
                                            { current with
                                                RoughStates =
                                                    Array.append
                                                        current.RoughStates
                                                        [| { State = string rough
                                                             AppliedAtOwnerRound =
                                                               (ReferenceState.player
                                                                   next
                                                                   target.Owner)
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

                            next <-
                                ReferenceState.updateCard { current with RoughStates = [||] } next
                    | ReferenceOpcode.DrawFromStack ->
                        let count =
                            if instruction.Selection = ReferenceSelection.UntilBlankSide then
                                badgeSides * instruction.Amount
                            else
                                instruction.Amount

                        let drawn, drawEvents, _ = draw actor count "Effect" next
                        next <- drawn
                        events.AddRange drawEvents
                    | ReferenceOpcode.SearchStack ->
                        let remainingCapacity =
                            match instruction.Destination with
                            | ValueSome ReferenceDestination.OwnBooth ->
                                ValueSome(
                                    max
                                        0
                                        (authority.BaseRules.Opening.BoothLimit
                                         - (ReferenceState.cardsIn next actor "Booth").Length)
                                )
                            | ValueSome ReferenceDestination.OtherBooth ->
                                ValueSome(
                                    max
                                        0
                                        (authority.BaseRules.Opening.BoothLimit
                                         - (ReferenceState.cardsIn
                                             next
                                             (otherPlayer next actor)
                                             "Booth")
                                             .Length)
                                )
                            | _ -> ValueNone

                        if remainingCapacity <> ValueSome 0 then
                            let selectedTargets =
                                remainingCapacity
                                |> ValueOption.map (fun capacity ->
                                    selectedTargets |> Array.truncate capacity)
                                |> ValueOption.defaultValue selectedTargets

                            lastSelectedCards <- selectedTargets |> Array.map _.Id
                            hasCardSelection <- true

                            match instruction.Destination with
                            | ValueSome destination when mutation <> SkipProgramCardMovement ->
                                let _, zone = destinationZone actor next destination

                                for target in selectedTargets do
                                    let moved, movedEvent = moveCard target.Id zone "" next

                                    next <-
                                        if zone = "Booth" then
                                            ReferenceState.updateCard
                                                { ReferenceState.card moved target.Id with
                                                    EnteredAtOwnerRound =
                                                        (ReferenceState.player moved target.Owner)
                                                            .RoundsStarted }
                                                moved
                                        else
                                            moved

                                    events.Add movedEvent
                            | _ -> ()
                    | ReferenceOpcode.ShuffleStack ->
                        let owner =
                            if
                                Array.contains ReferenceLocation.OtherStack instruction.Targets
                                || Array.contains ReferenceLocation.OtherStack instruction.Sources
                            then
                                otherPlayer next actor
                            else
                                actor

                        if hasCardSelection then
                            shuffleStack owner lastSelectedCards
                    | ReferenceOpcode.RevealCards ->
                        let revealed =
                            if hasCardSelection then
                                lastSelectedCards |> Array.choose (ReferenceState.tryCard next)
                            else
                                selectedTargets

                        if not hasCardSelection then
                            lastSelectedCards <- revealed |> Array.map _.Id
                            hasCardSelection <- true

                        events.Add
                            { ReferenceEvents.create "CardsRevealed" with
                                Actor = actor
                                SourceCard = attacker.Id
                                TargetCards = revealed |> Array.map _.Id
                                Effect = effect }
                    | ReferenceOpcode.MoveCards ->
                        let moving =
                            if instruction.Sources.Length <> 0 || not hasCardSelection then
                                selectedTargets
                            else
                                lastSelectedCards |> Array.choose (ReferenceState.tryCard next)

                        if mutation <> SkipProgramCardMovement then
                            let destination =
                                instruction.Destination
                                |> ValueOption.defaultWith (fun () ->
                                    invalidOp $"MoveCards {effect}:{path} has no destination.")

                            let _, zone = destinationZone actor next destination

                            for target in moving |> Array.truncate instruction.Amount do
                                if zone = "Stack" && target.Kind = "Bloke" && inPlay target then
                                    moveBlokeToStack target
                                else
                                    detachAndMove target.Id zone

                        lastSelectedCards <- moving |> Array.map _.Id
                        hasCardSelection <- true
                        runThen <- false

                        if moving.Length > 0 then
                            let branchPath = $"{path}/then/"
                            let requirements = branchRequirements branchPath instruction.Then

                            match
                                validateOwnedChoices
                                    authority
                                    actor
                                    (branchChoices requirements)
                                    requirements
                            with
                            | Error("ChoiceRequired", _) -> deferredRequirements <- requirements
                            | Error(code, rejected) -> executionRejection <- Some(code, rejected)
                            | Ok() -> execute branchPath instruction.Then
                    | ReferenceOpcode.ChuckCards ->
                        if mutation <> SkipProgramCardMovement then
                            for target in selectedTargets |> Array.truncate instruction.Amount do
                                detachAndMove target.Id "EmptiesTray"
                                cardsChucked <- cardsChucked + 1

                                if
                                    target.Kind = "Bloke"
                                    && authority.Cards[target.MechanicalId].TaxiFare = 4
                                then
                                    qualifyingChucked <- qualifyingChucked + 1
                    | ReferenceOpcode.AttachVim ->
                        match choiceBy (requirementId "attachments") "Attachments" selected with
                        | Some choice when mutation <> SkipProgramCardMovement ->
                            for placement in choice.Values do
                                let parts = placement.Split("->", StringSplitOptions.None)
                                let targetId = parts[1]
                                let target = ReferenceState.card next targetId
                                let moved, movedEvent = moveCard parts[0] "Attached" targetId next
                                let currentTarget = ReferenceState.card moved targetId

                                next <-
                                    ReferenceState.updateCard
                                        { currentTarget with
                                            Attachments =
                                                Array.append
                                                    currentTarget.Attachments
                                                    [| parts[0] |] }
                                        moved

                                events.Add movedEvent
                        | Some _ -> ()
                        | None when mutation <> SkipProgramCardMovement ->
                            let sources =
                                if instruction.Sources.Length <> 0 then
                                    selectedTargets
                                elif hasCardSelection then
                                    lastSelectedCards |> Array.choose (ReferenceState.tryCard next)
                                else
                                    selectedTargets

                            let targets =
                                instruction.Targets
                                |> Array.collect (targetsFor authority next actor attacker)
                                |> Array.filter inPlay

                            if targets.Length > 0 then
                                for index in 0 .. min instruction.Amount sources.Length - 1 do
                                    let vim = sources[index]
                                    let target = targets[index % targets.Length]

                                    let moved, movedEvent =
                                        moveCard vim.Id "Attached" target.Id next

                                    let currentTarget = ReferenceState.card moved target.Id

                                    next <-
                                        ReferenceState.updateCard
                                            { currentTarget with
                                                Attachments =
                                                    Array.append
                                                        currentTarget.Attachments
                                                        [| vim.Id |] }
                                            moved

                                    events.Add movedEvent
                        | None -> ()
                    | ReferenceOpcode.MoveVim
                    | ReferenceOpcode.ChuckVim ->
                        if mutation <> SkipProgramCardMovement then
                            for target in selectedTargets |> Array.truncate instruction.Amount do
                                detachAndMove
                                    target.Id
                                    (if instruction.Opcode = ReferenceOpcode.MoveVim then
                                         "Mitt"
                                     else
                                         "EmptiesTray")

                                if instruction.Opcode = ReferenceOpcode.MoveVim then
                                    let current = ReferenceState.card next target.Id

                                    let moved, movedEvent =
                                        moveCard current.Id "Attached" attacker.Id next

                                    next <-
                                        ReferenceState.updateCard
                                            { ReferenceState.card moved attacker.Id with
                                                Attachments =
                                                    Array.append
                                                        (ReferenceState.card moved attacker.Id)
                                                            .Attachments
                                                        [| current.Id |] }
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
                    | ReferenceOpcode.BeerMatToss ->
                        badgeSides <- 0
                        tossCount <- instruction.Amount
                        firstBeerMatIsBlank <- false

                        beerMatGateParent <-
                            if instruction.Then.Length = 0 && instruction.Otherwise.Length = 0 then
                                prefix
                            else
                                ""

                        for toss in 0 .. instruction.Amount - 1 do
                            let badge = nextBeerMat ()

                            if badge then
                                badgeSides <- badgeSides + 1

                            if toss = 0 then
                                firstBeerMatIsBlank <- not badge

                        let branch, branchPath =
                            if (badgeSides > 0) <> (mutation = InvertProgramBranch) then
                                instruction.Then, $"{path}/then/"
                            else
                                instruction.Otherwise, $"{path}/otherwise/"

                        let requirements = branchRequirements branchPath branch
                        runThen <- false

                        match
                            validateOwnedChoices
                                authority
                                actor
                                (branchChoices requirements)
                                requirements
                        with
                        | Error("ChoiceRequired", _) -> deferredRequirements <- requirements
                        | Error(code, rejected) -> executionRejection <- Some(code, rejected)
                        | Ok() -> execute branchPath branch
                    | ReferenceOpcode.RepeatUntilBlankSide ->
                        badgeSides <- 0
                        tossCount <- 0
                        let mutable running = true

                        while running do
                            let badge = nextBeerMat ()
                            tossCount <- tossCount + 1

                            if badge then
                                badgeSides <- badgeSides + 1
                            else
                                firstBeerMatIsBlank <- tossCount = 1
                                running <- false

                        let branchPath = $"{path}/then/"
                        let requirements = branchRequirements branchPath instruction.Then
                        runThen <- false

                        match
                            validateOwnedChoices
                                authority
                                actor
                                (branchChoices requirements)
                                requirements
                        with
                        | Error("ChoiceRequired", _) -> deferredRequirements <- requirements
                        | Error(code, rejected) -> executionRejection <- Some(code, rejected)
                        | Ok() -> execute branchPath instruction.Then
                    | ReferenceOpcode.Conditional ->
                        let currentAttacker = ReferenceState.card next attacker.Id

                        let passed =
                            instruction.Predicates
                            |> Array.forall (
                                conditionAllows
                                    authority
                                    next
                                    actor
                                    currentAttacker
                                    selected
                                    path
                                    firstBeerMatIsBlank
                                    aggregateAttackDamage
                            )

                        let passed =
                            if mutation = InvertProgramBranch then
                                not passed
                            else
                                passed

                        let branch, branchPath =
                            if passed then
                                instruction.Then, $"{path}/then/"
                            else
                                instruction.Otherwise, $"{path}/otherwise/"

                        let requirements = branchRequirements branchPath branch
                        runThen <- false

                        match
                            validateOwnedChoices
                                authority
                                actor
                                (branchChoices requirements)
                                requirements
                        with
                        | Error("ChoiceRequired", _) -> deferredRequirements <- requirements
                        | Error(code, rejected) -> executionRejection <- Some(code, rejected)
                        | Ok() -> execute branchPath branch
                    | ReferenceOpcode.Demote ->
                        for target in selectedTargets do
                            if target.UnderlyingCards.Length > 0 then
                                let pendingAttackDamage =
                                    damageInstructions
                                    |> Seq.exists (fun (damagePath, damageInstruction) ->
                                        not (resolvedDamageInstructions.Contains damagePath)
                                        && (damageInstruction.Opcode = ReferenceOpcode.DealPrintedDamage
                                            || damageInstruction.Opcode = ReferenceOpcode.ScaleDamage))

                                if pendingAttackDamage && not aggregateDamageResolved then
                                    let amount =
                                        if aggregateDamage then
                                            orderedAttackDamage
                                                authority
                                                next
                                                attacker
                                                target
                                                attack
                                                aggregateAttackDamage
                                        else
                                            attackDamage authority next attacker target attack

                                    let changed, damageEvents, damaged =
                                        applyDamage
                                            mutation
                                            actor
                                            attacker
                                            target
                                            "Attack"
                                            amount
                                            next

                                    next <- changed
                                    events.AddRange damageEvents
                                    attackTargets.AddRange damaged
                                    aggregateDamageResolved <- true

                                    damageInstructions
                                    |> Seq.filter (fun (_, damageInstruction) ->
                                        damageInstruction.Opcode = ReferenceOpcode.DealPrintedDamage
                                        || damageInstruction.Opcode = ReferenceOpcode.ScaleDamage)
                                    |> Seq.iter (fst >> resolvedDamageInstructions.Add)

                                let target = ReferenceState.card next target.Id

                                let underlyingId =
                                    target.UnderlyingCards[target.UnderlyingCards.Length - 1]

                                let underlying = ReferenceState.card next underlyingId
                                let moved, movedEvent = moveCard target.Id "Mitt" "" next

                                next <-
                                    ReferenceState.updateCard
                                        { ReferenceState.card moved target.Id with
                                            Attachments = [||]
                                            UnderlyingCards = [||]
                                            Damage = 0
                                            RoughStates = [||] }
                                        moved

                                next <-
                                    ReferenceState.updateCard
                                        { underlying with
                                            Zone = target.Zone
                                            Damage = target.Damage
                                            Attachments = target.Attachments
                                            UnderlyingCards =
                                                target.UnderlyingCards
                                                |> Array.take (target.UnderlyingCards.Length - 1)
                                            AttachedTo = "" }
                                        next

                                for attachmentId in target.Attachments do
                                    next <-
                                        ReferenceState.updateCard
                                            { ReferenceState.card next attachmentId with
                                                AttachedTo = underlyingId }
                                            next

                                events.Add movedEvent
                    | ReferenceOpcode.TakeExtraBarChit ->
                        extraBarChits <- extraBarChits + instruction.Amount
                    | ReferenceOpcode.SendHome ->
                        forcedSendHome.AddRange(selectedTargets |> Array.map _.Id)
                    | ReferenceOpcode.CopyAttack ->
                        match choiceBy (requirementId "attack") "Attack" selected with
                        | None -> ()
                        | Some choice ->
                            let copied =
                                candidates
                                |> Array.collect (fun value ->
                                    authority.Cards[value.MechanicalId].Attacks)
                                |> Array.find (fun value -> value.MechanicalId = choice.Values[0])

                            let (copiedState,
                                 copiedEvents,
                                 copiedTargets,
                                 copiedForced,
                                 copiedDeferred,
                                 copiedBeer,
                                 copiedRejection,
                                 copiedExtra) =
                                executeProgram
                                    authority
                                    mutation
                                    selected
                                    actor
                                    attacker
                                    effect
                                    copied
                                    (depth + 1)
                                    [||]
                                    next

                            next <- copiedState
                            events.AddRange copiedEvents
                            attackTargets.AddRange copiedTargets
                            forcedSendHome.AddRange copiedForced
                            deferredRequirements <- copiedDeferred
                            beerMatResults.AddRange copiedBeer
                            executionRejection <- copiedRejection
                            extraBarChits <- extraBarChits + copiedExtra
                    | ReferenceOpcode.ContinuousPartyTrick ->
                        for target in selectedTargets do
                            let registered, registeredEvents =
                                registerEffect
                                    next
                                    actor
                                    attacker
                                    effect
                                    "WhileSourceInPlay"
                                    "ContinuousPartyTrick"
                                    instruction
                                    target

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
                    | ReferenceOpcode.ForceBeerMatBlank ->
                        let registered =
                            { SourceEffect = effect
                              SourceCard = attacker.Id
                              Owner = actor
                              TargetCard = ""
                              Kind = "ForceBeerMatBlank"
                              Amount = 0
                              MechanicalTypes = [||]
                              RoughStates = [||]
                              RelatedCards = [||]
                              Conditions = [||]
                              Duration = "UntilEndOfOpponentsNextRound"
                              AppliesFromRound = next.RoundNumber + 1
                              ExpiresAfterRound = next.RoundNumber + 1 }

                        next <-
                            { next with
                                Effects = Array.append next.Effects [| registered |] }

                        events.Add
                            { ReferenceEvents.create "EffectRegistered" with
                                Actor = actor
                                SourceCard = attacker.Id
                                Effect = effect
                                Amount = 0 }
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
                    | other -> invalidOp $"The BLOKEMON-136 interpreter cannot execute {other}."

                    if runThen then
                        execute $"{path}/then/" instruction.Then

                index <- index + 1

        execute "root/" attack.Program

        if deferredRequirements.Length = 0 && executionRejection.IsNone then
            if aggregateDamage && not aggregateDamageResolved then
                match targetsFor authority next actor attacker ReferenceLocation.OtherOche with
                | [||] -> ()
                | targets ->
                    let target = targets[0]

                    let amount =
                        orderedAttackDamage
                            authority
                            next
                            attacker
                            target
                            attack
                            aggregateAttackDamage

                    let changed, damageEvents, damaged =
                        applyDamage mutation actor attacker target "Attack" amount next

                    next <- changed
                    events.AddRange damageEvents
                    attackTargets.AddRange damaged

            for path, instruction in damageInstructions do
                if
                    not (resolvedDamageInstructions.Contains path)
                    && not (
                        aggregateDamage && instruction.Opcode = ReferenceOpcode.DealPrintedDamage
                    )
                then
                    let requirementId suffix = $"{effect}:{path}:{suffix}"
                    let candidates = candidatesFor authority next actor attacker instruction

                    let selectedTargets =
                        match instruction.Selection with
                        | ReferenceSelection.Chosen
                        | ReferenceSelection.OtherSideChosen
                        | ReferenceSelection.UpTo ->
                            selectedCards (requirementId "cards") selected
                            |> Array.choose (ReferenceState.tryCard next)
                        | ReferenceSelection.Top -> candidates |> Array.truncate instruction.Amount
                        | ReferenceSelection.BeerMat when
                            badgeSides = 0 && instruction.Opcode <> ReferenceOpcode.RestrictAttack
                            ->
                            [||]
                        | _ -> candidates

                    match instruction.Opcode with
                    | ReferenceOpcode.DealPrintedDamage
                    | ReferenceOpcode.ScaleDamage ->
                        match
                            targetsFor authority next actor attacker ReferenceLocation.OtherOche
                        with
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
                            let damageKind =
                                if target.Zone = "Oche" then "Attack" else "BoothAttack"

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
                                    applyDamage
                                        mutation
                                        actor
                                        attacker
                                        target
                                        "PlacedCounter"
                                        amount
                                        next

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

        let next = { next with Random = random.Snapshot }

        next,
        events.ToArray(),
        (attackTargets.ToArray() |> Array.distinct),
        (forcedSendHome.ToArray() |> Array.distinct),
        deferredRequirements,
        beerMatResults.ToArray(),
        executionRejection,
        extraBarChits

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

    let private resolveMuddledCheck
        (authority: ReferenceAuthority)
        (actor: string)
        (attacker: CanonicalCard)
        (effect: string)
        (state: CanonicalState)
        =
        let muddled =
            attacker.RoughStates
            |> Array.tryFind (fun entry ->
                let parsed = Enum.Parse<ReferenceRoughState>(entry.State)
                authority.BaseRules.RoughStates[parsed].BeforeAttackBeerMat = ValueSome true)

        match muddled with
        | None -> state, [||], true
        | Some rough ->
            let random = ReferenceRandom(state.Random)
            let badge = random.NextInt(2) = 1
            let events = ResizeArray<CanonicalEvent>()

            events.Add
                { ReferenceEvents.create "BeerMatTossed" with
                    Actor = actor
                    SourceCard = attacker.Id
                    Effect = effect
                    HasBadgeSide = true
                    BadgeSide = badge }

            let mutable next = { state with Random = random.Snapshot }

            if not badge then
                let parsed = Enum.Parse<ReferenceRoughState>(rough.State)

                let amount =
                    authority.BaseRules.RoughStates[parsed].BlankSideCancelsAndSelfDamageCounters
                    |> ValueOption.defaultValue 0
                    |> (*) 10

                if amount > 0 then
                    let current = ReferenceState.card next attacker.Id

                    next <-
                        ReferenceState.updateCard
                            { current with
                                Damage = current.Damage + amount }
                            next

                    events.Add
                        { ReferenceEvents.create "DamagePlaced" with
                            Actor = actor
                            SourceCard = attacker.Id
                            TargetCards = [| attacker.Id |]
                            DamageKind = "PlacedCounter"
                            Amount = amount }

                events.Add
                    { ReferenceEvents.create "AttackCancelled" with
                        Actor = actor
                        SourceCard = attacker.Id
                        Effect = effect }

            next, events.ToArray(), badge

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
            match validateChoices authority selected with
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
                                    invalidOp
                                        $"The deterministic pending seam requires one chooser, but received {String.Join(',', values)}."

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

                        let declared =
                            { ReferenceEvents.create "AttackDeclared" with
                                Actor = selected.Actor
                                SourceCard = attacker.Id
                                Effect = attack.MechanicalId }

                        let requested =
                            { ReferenceEvents.create "EffectChoiceRequested" with
                                Actor = chooser
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

                        let muddledState, muddledEvents, attackContinues =
                            resolveMuddledCheck
                                authority
                                selected.Actor
                                attacker
                                attack.MechanicalId
                                refreshed

                        semanticEvents.AddRange muddledEvents

                        let (executed,
                             effectEvents,
                             attackTargets,
                             forcedSendHome,
                             deferredRequirements,
                             beerMatResults,
                             executionRejection,
                             extraBarChits) =
                            if attackContinues then
                                executeProgram
                                    authority
                                    mutation
                                    selected
                                    selected.Actor
                                    attacker
                                    attack.MechanicalId
                                    attack
                                    0
                                    [||]
                                    muddledState
                            else
                                muddledState, [||], [||], [||], [||], [||], None, 0

                        semanticEvents.AddRange effectEvents

                        match executionRejection with
                        | Some(code, requirements) -> rejection state code requirements
                        | None when deferredRequirements.Length <> 0 ->
                            let chooser =
                                deferredRequirements
                                |> Array.map _.Chooser
                                |> Array.distinct
                                |> function
                                    | [| value |] -> value
                                    | values ->
                                        invalidOp
                                            $"The branching pending seam requires one chooser, but received {String.Join(',', values)}."

                            let suspended =
                                ReferenceCommonFoundation.suspendForEffect
                                    { Canonical.emptyPendingEffect with
                                        Present = true
                                        Action =
                                            [| { selected with
                                                   Affordability = "Submitted"
                                                   Requirements = [||] } |]
                                        Source = attacker.Id
                                        Effect = attack.MechanicalId
                                        Chooser = chooser
                                        Requirements = deferredRequirements
                                        BeerMatResults = beerMatResults
                                        AttackStarted = true }
                                    executed

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
                                        [ [| submitted |]
                                          refreshEvents
                                          semanticEvents.ToArray()
                                          [| requested |] ])

                            { State = committed
                              Events = events
                              Rejection = [||] }
                        | None ->
                            let random = ReferenceRandom(executed.Random)

                            let knockedOut, knockoutEvents =
                                ReferenceCommonFoundation.resolveKnockoutsWithForced
                                    authority
                                    NoReferenceMutation
                                    random
                                    attacker.Id
                                    attackTargets
                                    forcedSendHome
                                    extraBarChits
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

    let legalResolutionAction (state: CanonicalState) (actor: string) =
        if state.PendingEffect.Present && state.PendingEffect.Chooser = actor then
            let original = state.PendingEffect.Action |> Array.exactlyOne
            let key = $"choice:{original.CommandId}"

            [| action
                   state
                   "ResolveEffectChoice"
                   actor
                   key
                   key
                   "resolve-effect-choice"
                   state.PendingEffect.Requirements
                   [||] |]
        else
            [||]

    let selectResolution (state: CanonicalState) (input: ReferenceActionInput) (commandIndex: int) =
        let legal =
            legalResolutionAction state state.PendingEffect.Chooser |> Array.exactlyOne

        let requirementIds = legal.Requirements |> Array.map _.Id |> Set.ofArray

        let choices =
            input.Choices
            |> Array.filter (fun value -> requirementIds.Contains value.RequirementId)
            |> Array.map canonicalChoice

        { legal with
            CommandId = $"obligation:{commandIndex}"
            StableKey = ""
            Choices = choices }

    let resolveEffectChoice
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
            elif not state.PendingEffect.Present || state.Phase <> "AwaitingEffectChoice" then
                Some "WrongPhase"
            elif state.PendingEffect.Chooser <> selected.Actor then
                Some "WrongChooser"
            else
                None

        match boundary with
        | Some code -> rejection state code state.PendingEffect.Requirements
        | None ->
            match
                validateOwnedChoices
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

                let attacker = ReferenceState.card refreshed refreshed.PendingEffect.Source

                let attack =
                    authority.Cards[attacker.MechanicalId].Attacks
                    |> Array.find (fun value -> value.MechanicalId = refreshed.PendingEffect.Effect)

                let playing =
                    { refreshed with
                        Phase = "Playing"
                        PendingEffect = Canonical.emptyPendingEffect }

                let (executed,
                     effectEvents,
                     attackTargets,
                     forcedSendHome,
                     deferredRequirements,
                     beerMatResults,
                     executionRejection,
                     extraBarChits) =
                    executeProgram
                        authority
                        mutation
                        resumed
                        original.Actor
                        attacker
                        attack.MechanicalId
                        attack
                        0
                        refreshed.PendingEffect.BeerMatResults
                        playing

                match executionRejection with
                | Some(code, requirements) -> rejection state code requirements
                | None when deferredRequirements.Length <> 0 ->
                    let chooser =
                        deferredRequirements
                        |> Array.map _.Chooser
                        |> Array.distinct
                        |> Array.exactlyOne

                    let suspended =
                        ReferenceCommonFoundation.suspendForEffect
                            { Canonical.emptyPendingEffect with
                                Present = true
                                Action = [| { resumed with Requirements = [||] } |]
                                Source = attacker.Id
                                Effect = attack.MechanicalId
                                Chooser = chooser
                                Requirements = deferredRequirements
                                BeerMatResults = beerMatResults
                                AttackStarted = true }
                            executed

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
                                [ [| submitted |]; refreshEvents; effectEvents; [| requested |] ])

                    { State = committed
                      Events = events
                      Rejection = [||] }
                | None ->
                    let semanticEvents = ResizeArray<CanonicalEvent>(effectEvents)
                    let random = ReferenceRandom(executed.Random)

                    let knockedOut, knockoutEvents =
                        ReferenceCommonFoundation.resolveKnockoutsWithForced
                            authority
                            NoReferenceMutation
                            random
                            attacker.Id
                            attackTargets
                            forcedSendHome
                            extraBarChits
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
