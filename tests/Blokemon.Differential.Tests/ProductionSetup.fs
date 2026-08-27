namespace Blokemon.Differential.Tests

open System
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game
open Blokemon.ReferenceModel

type ProductionZoneCountInput =
    { Owner: PlayerId
      Zone: CardZone
      Count: int }

type ProductionChoiceInput =
    { Choice: EffectChoice
      WhenAvailable: bool }

type ProductionActionInput =
    { Command: MatchCommand
      DeclaredTargetCard: string
      DeclaredEffectId: string
      Choices: ProductionChoiceInput array }

type ProductionObligationInput =
    { Id: string
      ProgramKey: string
      Route: string
      Parameters: string array
      Cards: CardState array
      ZoneCounts: ProductionZoneCountInput array
      Players: PlayerState array
      Actions: ProductionActionInput array
      Seed: MatchSeed }

[<RequireQualifiedAccess>]
module ProductionSetup =

    let private zone value =
        match value with
        | ReferenceZone.Stack -> CardZone.Stack
        | ReferenceZone.Mitt -> CardZone.Mitt
        | ReferenceZone.Oche -> CardZone.Oche
        | ReferenceZone.Booth -> CardZone.Booth
        | ReferenceZone.Attached -> CardZone.Attached
        | ReferenceZone.EmptiesTray -> CardZone.EmptiesTray
        | ReferenceZone.Local -> CardZone.Local
        | ReferenceZone.BarChit -> CardZone.BarChit
        | other -> invalidOp $"Unsupported reference zone {other}."

    let private kind (mechanicalId: string) =
        if mechanicalId.StartsWith("VIM-", StringComparison.Ordinal) then
            CardKind.Vim
        elif mechanicalId.StartsWith("KIT-", StringComparison.Ordinal) then
            CardKind.Kit
        else
            CardKind.Bloke

    let private choice (input: ReferenceChoiceInput) =
        let id = EffectChoiceId input.RequirementId

        let value =
            match input.Value with
            | ReferenceChoiceValue.Optional accepted -> EffectChoice.Optional(id, accepted)
            | ReferenceChoiceValue.Amount amount -> EffectChoice.Amount(id, amount)
            | ReferenceChoiceValue.Cards cards ->
                EffectChoice.Cards(
                    id,
                    cards |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                )
            | ReferenceChoiceValue.MechanicalType mechanicalType ->
                EffectChoice.MechanicalType(
                    id,
                    Enum.Parse<BlokemonMechanicalType>(string mechanicalType)
                )
            | ReferenceChoiceValue.Attack effect -> EffectChoice.Attack(id, EffectId effect)
            | ReferenceChoiceValue.Distribution allocations ->
                EffectChoice.Distribution(
                    id,
                    allocations
                    |> Seq.map (fun allocation ->
                        ({ Card = CardInstanceId allocation.Card
                           Counters = allocation.Counters }
                        : DamageAllocation))
                    |> ImmutableArray.CreateRange
                )
            | ReferenceChoiceValue.Attachments placements ->
                EffectChoice.Attachments(
                    id,
                    placements
                    |> Seq.map (fun placement ->
                        ({ Vim = CardInstanceId placement.Vim
                           Bloke = CardInstanceId placement.Bloke }
                        : VimAttachment))
                    |> ImmutableArray.CreateRange
                )

        { Choice = value
          WhenAvailable = input.WhenAvailable }

    let private canonicalChoice (input: CanonicalChoice) =
        let id = EffectChoiceId input.Id

        match input.Kind, input.Values with
        | "Optional", [| value |] -> EffectChoice.Optional(id, Boolean.Parse value)
        | "Amount", [| value |] -> EffectChoice.Amount(id, Int32.Parse value)
        | "Cards", values ->
            EffectChoice.Cards(id, values |> Seq.map CardInstanceId |> ImmutableArray.CreateRange)
        | "MechanicalType", [| value |] ->
            EffectChoice.MechanicalType(id, Enum.Parse<BlokemonMechanicalType> value)
        | "Attack", [| value |] -> EffectChoice.Attack(id, EffectId value)
        | "Distribution", values ->
            EffectChoice.Distribution(
                id,
                values
                |> Seq.map (fun value ->
                    let parts = value.Split(':')

                    ({ Card = CardInstanceId parts[0]
                       Counters = Int32.Parse parts[1] }
                    : DamageAllocation))
                |> ImmutableArray.CreateRange
            )
        | "Attachments", values ->
            EffectChoice.Attachments(
                id,
                values
                |> Seq.map (fun value ->
                    let parts = value.Split("->", StringSplitOptions.None)

                    ({ Vim = CardInstanceId parts[0]
                       Bloke = CardInstanceId parts[1] }
                    : VimAttachment))
                |> ImmutableArray.CreateRange
            )
        | kind, _ -> invalidOp $"Unsupported canonical program choice {kind}."

    let private action matchId index (input: ReferenceActionInput) =
        let action =
            match input.Kind with
            | ReferenceInputActionKind.Attack ->
                MatchAction.Attack(CardInstanceId input.SourceCard, EffectId input.EffectId)
            | ReferenceInputActionKind.EndRound -> MatchAction.EndRound
            | ReferenceInputActionKind.UsePartyTrick ->
                MatchAction.UsePartyTrick(CardInstanceId input.SourceCard, EffectId input.EffectId)
            | ReferenceInputActionKind.Promote ->
                MatchAction.Promote(
                    CardInstanceId input.SourceCard,
                    CardInstanceId input.TargetCard
                )
            | ReferenceInputActionKind.PlayKit ->
                MatchAction.PlayKit(
                    CardInstanceId input.SourceCard,
                    if String.IsNullOrEmpty input.TargetCard then
                        ValueNone
                    else
                        ValueSome(CardInstanceId input.TargetCard)
                )
            | ReferenceInputActionKind.ResolveKnockoutTrigger ->
                MatchAction.ResolveKnockoutTrigger(
                    if String.IsNullOrEmpty input.TargetCard then
                        ValueNone
                    else
                        ValueSome(CardInstanceId input.TargetCard)
                )
            | ReferenceInputActionKind.ResolveBarChitTrigger ->
                MatchAction.ResolveBarChitTrigger(input.TargetCard = "Booth")
            | other -> invalidOp $"Unsupported reference action input {other}."

        let choices = input.Choices |> Array.map choice

        { Command =
            { Id = CommandId $"obligation:{index}"
              MatchId = matchId
              Actor = PlayerId input.Actor
              ExpectedRevision = MatchRevision(int64 index)
              Choices = ImmutableArray<_>.Empty
              Action = action }
          DeclaredTargetCard = input.TargetCard
          DeclaredEffectId = input.EffectId
          Choices = choices }

    let materialize (input: ReferenceObligationInput) =
        let matchId = MatchId $"obligation:{input.Id}"

        { Id = input.Id
          ProgramKey = input.ProgramKey
          Route = input.InitialState.Route.Value
          Parameters = input.InitialState.Parameters
          Cards =
            input.InitialState.Cards
            |> Array.mapi (fun index card ->
                { Id = CardInstanceId card.CardId
                  MechanicalId = MechanicalCardId card.MechanicalId
                  Owner = PlayerId card.Owner
                  Kind = kind card.MechanicalId
                  Zone = zone card.Zone
                  IsFaceDown = card.Zone = ReferenceZone.BarChit
                  StackPosition = index
                  AttachedTo = ValueNone
                  Attachments = ImmutableArray<_>.Empty
                  UnderlyingCards = ImmutableArray<_>.Empty
                  Damage = 0
                  RoughStates = ImmutableArray<_>.Empty
                  EnteredAtOwnerRound = 0
                  LastPromotedRound = -1 })
          ZoneCounts =
            input.InitialState.ZoneCounts
            |> Array.map (fun count ->
                { Owner = PlayerId count.Owner
                  Zone = zone count.Zone
                  Count = count.Count })
          Players =
            input.InitialState.Players
            |> Array.map (fun player ->
                { Id = PlayerId player.Player
                  BarChitsRemaining = player.BarChitsRemaining
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = ImmutableArray<_>.Empty
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 1 })
          Actions = input.Actions |> Array.mapi (fun index value -> action matchId index value)
          Seed = MatchSeed input.RandomSeed }

    let private collectible (manifest: BlokemonRuntimeManifest) (mechanicalId: string) =
        manifest.Collectibles
        |> Array.tryFind (fun value -> value.Id = mechanicalId)
        |> Option.defaultWith (fun () ->
            invalidOp $"The production setup names unknown Bloke card {mechanicalId}.")

    let private attackFor (manifest: BlokemonRuntimeManifest) (input: ReferenceObligationInput) =
        let attacks =
            manifest.Collectibles
            |> Array.tryFind (fun value -> value.Id = input.ReviewedProgram.OwnerId)
            |> Option.map _.Attacks
            |> Option.orElseWith (fun () ->
                manifest.Kits
                |> Array.tryFind (fun value -> value.Id = input.ReviewedProgram.OwnerId)
                |> Option.map _.Attacks)
            |> Option.defaultWith (fun () ->
                invalidOp
                    $"Obligation {input.Id} names unknown production attack owner {input.ReviewedProgram.OwnerId}.")

        attacks
        |> Array.tryFind (fun value -> value.MechanicalId = input.ReviewedProgram.MechanicalId)
        |> Option.defaultWith (fun () ->
            invalidOp
                $"Obligation {input.Id} names unknown production attack {input.ReviewedProgram.MechanicalId}.")

    let private basicVimFor
        (manifest: BlokemonRuntimeManifest)
        (mechanicalType: BlokemonMechanicalType)
        =
        manifest.BasicVim
        |> Array.tryFind (fun value -> value.MechanicalType = mechanicalType)
        |> Option.map _.Id
        |> Option.defaultWith (fun () ->
            invalidOp $"The production setup cannot find basic {mechanicalType} Vim.")

    let private knownBlokeId (manifest: BlokemonRuntimeManifest) (candidates: string array) =
        candidates
        |> Array.tryFind (fun candidate ->
            candidate.StartsWith("BLK-", StringComparison.Ordinal)
            && candidate.Length = 7
            && (manifest.Collectibles |> Array.exists (fun value -> value.Id = candidate)))

    let private cardKind (manifest: BlokemonRuntimeManifest) mechanicalId =
        if manifest.Collectibles |> Array.exists (fun value -> value.Id = mechanicalId) then
            CardKind.Bloke
        elif manifest.Kits |> Array.exists (fun value -> value.Id = mechanicalId) then
            CardKind.Kit
        elif manifest.BasicVim |> Array.exists (fun value -> value.Id = mechanicalId) then
            CardKind.Vim
        else
            invalidOp $"The production setup names unknown card {mechanicalId}."

    let private setupCard
        manifest
        (id: string)
        (mechanicalId: string)
        (owner: string)
        (cardZone: CardZone)
        position
        : CardState =
        { Id = CardInstanceId id
          MechanicalId = MechanicalCardId mechanicalId
          Owner = PlayerId owner
          Kind = cardKind manifest mechanicalId
          Zone = cardZone
          IsFaceDown = cardZone = CardZone.BarChit
          StackPosition = position
          AttachedTo = ValueNone
          Attachments = ImmutableArray<_>.Empty
          UnderlyingCards = ImmutableArray<_>.Empty
          Damage = 0
          RoughStates = ImmutableArray<_>.Empty
          EnteredAtOwnerRound = 1
          LastPromotedRound = -1 }

    let private updateSetupCard
        (id: CardInstanceId)
        (change: CardState -> CardState)
        (cards: CardState array)
        =
        cards |> Array.map (fun value -> if value.Id = id then change value else value)

    let private attachSetupCard
        (vim: CardInstanceId)
        (target: CardInstanceId)
        (cards: CardState array)
        =
        cards
        |> updateSetupCard vim (fun value ->
            { value with
                Zone = CardZone.Attached
                StackPosition = -1
                AttachedTo = ValueSome target })
        |> updateSetupCard target (fun value ->
            { value with
                Attachments = value.Attachments.Add vim })

    let deterministicState
        (manifest: BlokemonRuntimeManifest)
        (input: ReferenceObligationInput)
        : MatchState =
        let setup = materialize input
        let attack = attackFor manifest input
        let parameters = setup.Parameters

        let defenderMechanicalId =
            match setup.Route with
            | "ignore-modifier" when parameters.Length >= 3 -> parameters[2]
            | "day-two-forced-blank" -> "BLK-001"
            | _ -> knownBlokeId manifest parameters[1..] |> Option.defaultValue "BLK-003"

        let mutable cards =
            [| setupCard manifest "attacker" input.ReviewedProgram.OwnerId "first" CardZone.Oche -1
               setupCard manifest "defender" defenderMechanicalId "second" CardZone.Oche -1 |]

        for index in 0 .. attack.VimCost.Length - 1 do
            let requiredType = attack.VimCost[index]

            let payableType =
                if requiredType = BlokemonMechanicalType.Colorless then
                    BlokemonMechanicalType.Grass
                else
                    requiredType

            let id = CardInstanceId $"vim-{index}"

            cards <-
                Array.append
                    cards
                    [| setupCard
                           manifest
                           id.Value
                           (basicVimFor manifest payableType)
                           "first"
                           CardZone.Mitt
                           -1 |]

            cards <- attachSetupCard id (CardInstanceId "attacker") cards

        let add owner id mechanicalId cardZone position =
            let card = setupCard manifest id mechanicalId owner cardZone position

            if not (cards |> Array.exists (fun existing -> existing.Id = card.Id)) then
                cards <- Array.append cards [| card |]

        let addStack owner prefix count =
            for index in 0 .. count - 1 do
                add
                    owner
                    $"{prefix}-{index}"
                    (basicVimFor manifest BlokemonMechanicalType.Grass)
                    CardZone.Stack
                    index

        let addBarChits owner prefix =
            for index in 0..5 do
                add
                    owner
                    $"{prefix}-{index}"
                    (basicVimFor manifest BlokemonMechanicalType.Grass)
                    CardZone.BarChit
                    index

        addStack "first" "own-stack" 5
        addStack "second" "other-stack" 5
        addBarChits "first" "first-bar"
        addBarChits "second" "second-bar"

        let ensureOwnBooth id =
            add "first" id "BLK-003" CardZone.Booth -1

        let ensureOtherBooth id =
            add "second" id "BLK-003" CardZone.Booth -1

        match setup.Route with
        | "booth-all-own-swap" ->
            ensureOwnBooth "a-own-swap"
            ensureOtherBooth "a-other-booth"
        | "chuck-vim-booth" -> ensureOtherBooth "a-other-booth"
        | "damage-attach-vim" ->
            ensureOwnBooth "own-booth"

            add
                "first"
                "recovered-vim"
                (basicVimFor manifest BlokemonMechanicalType.Grass)
                CardZone.EmptiesTray
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
                cards <-
                    updateSetupCard
                        (CardInstanceId "a-booth-0")
                        (fun value -> { value with Damage = 10 })
                        cards
        | "damage-chuck-cards" ->
            if parameters |> Array.contains "OtherMitt" then
                add "second" "other-mitt-0" "KIT-001" CardZone.Mitt -1
                add "second" "other-mitt-1" "KIT-002" CardZone.Mitt -1

            ensureOtherBooth "other-reserve"
        | "damage-chuck-vim" ->
            if parameters |> Array.contains "other" then
                let mechanicalId =
                    parameters
                    |> Array.tryFind (fun value ->
                        value.StartsWith("VIM-", StringComparison.Ordinal))
                    |> Option.defaultValue (basicVimFor manifest BlokemonMechanicalType.Grass)

                let count = Int32.Parse parameters[parameters.Length - 1]

                for index in 0 .. count - 1 do
                    let id = CardInstanceId $"other-vim-{index}"
                    add "second" id.Value mechanicalId CardZone.Mitt -1
                    cards <- attachSetupCard id (CardInstanceId "defender") cards

            ensureOtherBooth "other-reserve"
        | "damage-heal" ->
            cards <-
                updateSetupCard
                    (CardInstanceId "attacker")
                    (fun value -> { value with Damage = 60 })
                    cards

            ensureOtherBooth "other-reserve"
        | "damage-move-vim" ->
            let mechanicalId =
                parameters
                |> Array.tryFind (fun value -> value.StartsWith("VIM-", StringComparison.Ordinal))
                |> Option.defaultValue (basicVimFor manifest BlokemonMechanicalType.Water)

            add "second" "other-vim-0" mechanicalId CardZone.Mitt -1

            cards <-
                attachSetupCard (CardInstanceId "other-vim-0") (CardInstanceId "defender") cards

            ensureOtherBooth "other-reserve"
        | "damage-rough" ->
            if
                parameters
                |> Array.exists (fun value ->
                    value.Contains("Singed", StringComparison.Ordinal)
                    || value.Contains("NoddedOff", StringComparison.Ordinal))
            then
                let stayingPower = (collectible manifest defenderMechanicalId).StayingPower

                cards <-
                    updateSetupCard
                        (CardInstanceId "defender")
                        (fun value -> { value with Damage = stayingPower })
                        cards

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
                add "second" "other-kit-0" "KIT-001" CardZone.Mitt -1
                add "second" "other-kit-1" "KIT-002" CardZone.Mitt -1

            ensureOtherBooth "other-reserve"
        | "heal-clear" ->
            cards <-
                updateSetupCard
                    (CardInstanceId "attacker")
                    (fun value ->
                        { value with
                            Damage = 60
                            RoughStates =
                                ImmutableArray.Create(
                                    { State = BlokemonRoughState.DodgyPint
                                      AppliedAtOwnerRound = 1 }
                                    : RoughStateEntry
                                ) })
                    cards

            ensureOtherBooth "other-reserve"
        | "trivial-chuck" ->
            if parameters |> Array.contains "local" then
                add "second" "local-under-test" "KIT-001" CardZone.Local -1

            ensureOtherBooth "other-reserve"
        | "trivial-copy" -> ensureOtherBooth "other-reserve"
        | "trivial-distribution" ->
            ensureOtherBooth "a-other-booth"
            ensureOtherBooth "b-other-booth"
        | "trivial-draw" -> ensureOtherBooth "other-reserve"
        | "trivial-rough" ->
            let stayingPower = (collectible manifest defenderMechanicalId).StayingPower

            cards <-
                updateSetupCard
                    (CardInstanceId "defender")
                    (fun value -> { value with Damage = stayingPower })
                    cards

            ensureOtherBooth "other-reserve"
        | "trivial-soft-spot" -> ensureOtherBooth "other-reserve"
        | "trivial-swap" -> ensureOtherBooth "a-other-booth"
        | "dynamic-adjust" ->
            let units = Int32.Parse parameters[6]

            match parameters[5] with
            | "OwnBoothCount" ->
                for index in 0 .. units - 1 do
                    ensureOwnBooth $"own-count-{index}"
            | "OtherBoothCount" ->
                for index in 0 .. units - 1 do
                    ensureOtherBooth $"other-count-{index}"
            | "OtherAttachedVim" ->
                for index in 0 .. units - 1 do
                    let id = CardInstanceId $"other-count-vim-{index}"

                    add
                        "second"
                        id.Value
                        (basicVimFor manifest BlokemonMechanicalType.Water)
                        CardZone.Mitt
                        -1

                    cards <- attachSetupCard id (CardInstanceId "defender") cards
            | "SelfDamageCounters" ->
                cards <-
                    updateSetupCard
                        (CardInstanceId "attacker")
                        (fun value -> { value with Damage = units * 10 })
                        cards
            | "OtherOcheDamageCounters" ->
                cards <-
                    updateSetupCard
                        (CardInstanceId "defender")
                        (fun value -> { value with Damage = units * 10 })
                        cards
            | "OwnAttachedVim" -> ()
            | unknown -> invalidOp $"Unknown production dynamic source {unknown}."

            ensureOtherBooth "other-reserve"
        | "coin-branch" ->
            add
                "second"
                "other-vim-0"
                (basicVimFor manifest BlokemonMechanicalType.Water)
                CardZone.Mitt
                -1

            cards <-
                attachSetupCard (CardInstanceId "other-vim-0") (CardInstanceId "defender") cards

            ensureOtherBooth "other-reserve"
        | "conditional-adjust" ->
            if parameters[6] = "true" then
                match parameters[5] with
                | "SelfHasDamage" ->
                    cards <-
                        updateSetupCard
                            (CardInstanceId "attacker")
                            (fun value -> { value with Damage = 10 })
                            cards
                | "OtherOcheHasDamage" ->
                    cards <-
                        updateSetupCard
                            (CardInstanceId "defender")
                            (fun value -> { value with Damage = 10 })
                            cards
                | "NamedBlokeInBooth" ->
                    add "first" "named-condition" parameters[7] CardZone.Booth -1
                | _ -> ()
            elif parameters[5] = "MittCountsAreEqual" || parameters[5] = "OwnMittIsEmpty" then
                add
                    "first"
                    "own-mitt-condition"
                    (basicVimFor manifest BlokemonMechanicalType.Grass)
                    CardZone.Mitt
                    -1

            if parameters[5] <> "OtherBoothExists" || parameters[6] = "true" then
                ensureOtherBooth "other-reserve"
        | "conditional-demote" ->
            add "second" "lower-stage" parameters[3] CardZone.Attached -1

            cards <-
                cards
                |> updateSetupCard (CardInstanceId "lower-stage") (fun value ->
                    { value with
                        AttachedTo = ValueSome(CardInstanceId "defender") })
                |> updateSetupCard (CardInstanceId "defender") (fun value ->
                    { value with
                        UnderlyingCards = ImmutableArray.Create(CardInstanceId "lower-stage")
                        LastPromotedRound = 2 })
        | "conditional-extra-bar" ->
            cards <-
                updateSetupCard
                    (CardInstanceId "defender")
                    (fun value -> { value with Damage = 20 })
                    cards
        | "conditional-rough" ->
            if parameters[3] = "true" then
                let id, rough =
                    if parameters[2] = "self" then
                        CardInstanceId "attacker", BlokemonRoughState.Muddled
                    else
                        CardInstanceId "defender", BlokemonRoughState.NoddedOff

                cards <-
                    updateSetupCard
                        id
                        (fun value ->
                            { value with
                                RoughStates =
                                    ImmutableArray.Create(
                                        { State = rough
                                          AppliedAtOwnerRound = 1 }
                                        : RoughStateEntry
                                    ) })
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
                (basicVimFor manifest BlokemonMechanicalType.Grass)
                CardZone.Stack
                1

            ensureOtherBooth "other-reserve"
        | "coin-swap" -> ensureOtherBooth "a-other-booth"
        | "full-booth-search" ->
            for index in 0..4 do
                add "first" $"full-booth-{index}" "BLK-004" CardZone.Booth index

            add "first" "search-card" parameters[2] CardZone.Stack 0
        | "booth-search" ->
            for index in 1 .. Int32.Parse parameters[4] do
                add "first" $"candidate-{index}" parameters[2] CardZone.Stack index
        | "coin-search" ->
            for index in 1 .. Int32.Parse parameters[3] do
                add
                    "first"
                    $"candidate-{index}"
                    (basicVimFor manifest BlokemonMechanicalType.Water)
                    CardZone.Stack
                    index

            ensureOtherBooth "other-reserve"
        | "optional-zero"
        | "optional-decline" ->
            let setupName = parameters[2]
            let candidate = parameters[3]

            if candidate <> "none" && candidate <> "first-draw" then
                let mechanicalId, cardZone =
                    match setupName with
                    | "recover-water" ->
                        basicVimFor manifest BlokemonMechanicalType.Water, CardZone.EmptiesTray
                    | "recover-bloke" -> "BLK-001", CardZone.EmptiesTray
                    | "recover-barbit" -> "KIT-001", CardZone.EmptiesTray
                    | "recover-fire" ->
                        basicVimFor manifest BlokemonMechanicalType.Fire, CardZone.EmptiesTray
                    | "mitt-water" ->
                        basicVimFor manifest BlokemonMechanicalType.Water, CardZone.Mitt
                    | "stack-bloke" -> "BLK-001", CardZone.Stack
                    | unknown -> invalidOp $"Unknown production optional setup {unknown}."

                add
                    "first"
                    candidate
                    mechanicalId
                    cardZone
                    (if cardZone = CardZone.Stack then 1 else -1)

            ensureOtherBooth "other-reserve"
        | "optional-max" ->
            let setupName = parameters[2]
            let count = Int32.Parse parameters[3]
            let firstIndex = if input.ReviewedProgram.OwnerId = "BLK-022" then 1 else 0

            if input.ReviewedProgram.OwnerId = "BLK-022" then
                add "first" "first-draw" "BLK-001" CardZone.Stack 0

            let mechanicalId index =
                match setupName with
                | "recover-water"
                | "mitt-water" -> basicVimFor manifest BlokemonMechanicalType.Water
                | "recover-bloke"
                | "stack-bloke" -> "BLK-001"
                | "stack-distinct-bloke" -> [| "BLK-001"; "BLK-004"; "BLK-007" |][index - 1]
                | "recover-barbit" -> "KIT-001"
                | "recover-fire" -> basicVimFor manifest BlokemonMechanicalType.Fire
                | unknown -> invalidOp $"Unknown production optional maximum setup {unknown}."

            let cardZone =
                match setupName with
                | "recover-water"
                | "recover-bloke"
                | "recover-barbit"
                | "recover-fire" -> CardZone.EmptiesTray
                | "mitt-water" -> CardZone.Mitt
                | "stack-bloke"
                | "stack-distinct-bloke" -> CardZone.Stack
                | unknown -> invalidOp $"Unknown production optional maximum zone {unknown}."

            for index in 1 .. count - firstIndex do
                add
                    "first"
                    $"candidate-{index}"
                    (mechanicalId index)
                    cardZone
                    (if cardZone = CardZone.Stack then index else -1)

            ensureOtherBooth "other-reserve"
        | "optional-invalid-duplicate" ->
            add "first" "candidate-1" "BLK-001" CardZone.Stack 1
            add "first" "candidate-2" "BLK-010" CardZone.Stack 2
            ensureOtherBooth "other-reserve"
        | "optional-bar-kit" ->
            add "first" "bar-kit" "KIT-004" CardZone.Attached -1
            add "first" "own-booth" "BLK-003" CardZone.Booth -1
            add "first" "bar-kit-2" "KIT-004" CardZone.Attached -1

            cards <-
                cards
                |> updateSetupCard (CardInstanceId "bar-kit") (fun value ->
                    { value with
                        AttachedTo = ValueSome(CardInstanceId "attacker") })
                |> updateSetupCard (CardInstanceId "attacker") (fun value ->
                    { value with
                        Attachments = value.Attachments.Add(CardInstanceId "bar-kit") })
                |> updateSetupCard (CardInstanceId "bar-kit-2") (fun value ->
                    { value with
                        AttachedTo = ValueSome(CardInstanceId "own-booth") })
                |> updateSetupCard (CardInstanceId "own-booth") (fun value ->
                    { value with
                        Attachments = ImmutableArray.Create(CardInstanceId "bar-kit-2") })

            ensureOtherBooth "other-reserve"
        | "search-all" ->
            add "first" parameters[3] parameters[2] CardZone.Stack 1
            ensureOtherBooth "other-reserve"
        | "top-qualifying" ->
            for index in 1..4 do
                let mechanicalId =
                    if parameters |> Array.contains "zero" then
                        basicVimFor manifest BlokemonMechanicalType.Grass
                    elif index = 1 then
                        "BLK-001"
                    else
                        basicVimFor manifest BlokemonMechanicalType.Grass

                add "first" $"top-{index}" mechanicalId CardZone.Stack index

            ensureOtherBooth "other-reserve"
        | "gone-smoke" ->
            ensureOwnBooth "own-booth"
            ensureOtherBooth "other-booth"
        | "day-two-forced-blank" ->
            add
                "second"
                "other-vim-0"
                (basicVimFor manifest BlokemonMechanicalType.Grass)
                CardZone.Mitt
                -1

            add
                "second"
                "other-vim-1"
                (basicVimFor manifest BlokemonMechanicalType.Water)
                CardZone.Mitt
                -1

            cards <-
                attachSetupCard (CardInstanceId "other-vim-0") (CardInstanceId "defender") cards

            cards <-
                attachSetupCard (CardInstanceId "other-vim-1") (CardInstanceId "defender") cards
        | unknown -> invalidOp $"Unowned production deterministic route {unknown}."

        for explicitCard in setup.Cards do
            add
                explicitCard.Owner.Value
                explicitCard.Id.Value
                explicitCard.MechanicalId.Value
                explicitCard.Zone
                -1

        for count in setup.ZoneCounts do
            for index in 0 .. count.Count - 1 do
                add
                    count.Owner.Value
                    $"input-{count.Owner.Value}-{count.Zone}-{index}"
                    (basicVimFor manifest BlokemonMechanicalType.Grass)
                    count.Zone
                    index

        let players =
            [| "first"; "second" |]
            |> Array.map (fun id ->
                let barChits =
                    setup.Players
                    |> Array.tryFind (fun value -> value.Id = PlayerId id)
                    |> Option.map _.BarChitsRemaining
                    |> Option.defaultValue 6

                ({ Id = PlayerId id
                   BarChitsRemaining = barChits
                   MulliganCount = 0
                   MulliganBonusAllowance = 0
                   MulliganBonusChosen = true
                   BonusDrawn = ImmutableArray<_>.Empty
                   BonusPlacementChosen = true
                   OpeningChosen = true
                   RoundsStarted = 2 }
                : PlayerState))
            |> ImmutableArray.CreateRange

        let usedActivatedEffects =
            manifest.Collectibles
            |> Array.tryFind (fun value -> value.Id = input.ReviewedProgram.OwnerId)
            |> Option.map _.PartyTricks
            |> Option.orElseWith (fun () ->
                manifest.Kits
                |> Array.tryFind (fun value -> value.Id = input.ReviewedProgram.OwnerId)
                |> Option.map _.PartyTricks)
            |> Option.defaultValue [||]
            |> Seq.filter (fun value -> value.Trigger = BlokemonTrigger.Activated)
            |> Seq.map (fun value -> EffectId value.MechanicalId)
            |> ImmutableArray.CreateRange

        let players =
            if
                setup.Route = "conditional-adjust"
                && parameters[5] = "OwnBarChitCountIsGreater"
                && parameters[6] = "true"
            then
                players
                |> Seq.map (fun value ->
                    if value.Id = PlayerId "second" then
                        { value with BarChitsRemaining = 5 }
                    else
                        value)
                |> ImmutableArray.CreateRange
            else
                players

        if
            setup.Route = "still-coming-up-promoted"
            || setup.Route = "still-coming-up-not-promoted"
        then
            add "first" "own-lower-stage" "BLK-079" CardZone.Attached -1

            cards <-
                cards
                |> updateSetupCard (CardInstanceId "own-lower-stage") (fun value ->
                    { value with
                        AttachedTo = ValueSome(CardInstanceId "attacker") })
                |> updateSetupCard (CardInstanceId "attacker") (fun value ->
                    { value with
                        UnderlyingCards = ImmutableArray.Create(CardInstanceId "own-lower-stage")
                        LastPromotedRound =
                            if setup.Route = "still-coming-up-promoted" then 3 else 2 })

        let roundUsage: RoundUsage =
            if
                setup.Route = "conditional-adjust"
                && parameters[5] = "MatePlayedThisRound"
                && parameters[6] = "true"
            then
                { Player = PlayerId "first"
                  VimAttachments = 0
                  MatesPlayed = 1
                  LocalsPlayed = 0
                  TaxisUsed = 0
                  EffectsUsed = usedActivatedEffects
                  KitsPlayed = ImmutableArray.Create(MechanicalCardId parameters[7]) }
            else
                { Player = PlayerId "first"
                  VimAttachments = 0
                  MatesPlayed = 0
                  LocalsPlayed = 0
                  TaxisUsed = 0
                  EffectsUsed = usedActivatedEffects
                  KitsPlayed = ImmutableArray<_>.Empty }

        { Id = MatchId $"obligation:{setup.Id}"
          AuthorityVersion = manifest.ManifestVersion
          Seed = setup.Seed
          Random = MatchRandomState(setup.Seed.Value, 0)
          Revision = MatchRevision 0L
          LastEventSequence = 0L
          Phase = MatchPhase.Playing
          OpeningPlayer = PlayerId "first"
          ActivePlayer = PlayerId "first"
          RoundNumber = 3
          Players = players
          Cards = cards |> Array.sortBy _.Id.Value |> ImmutableArray.CreateRange
          Effects = ImmutableArray<_>.Empty
          ProcessedCommands = ImmutableArray<_>.Empty
          RoundUsage = roundUsage
          PendingEffect = ValueNone
          PendingKnockout = ValueNone
          PendingBarChits = ImmutableArray<_>.Empty
          ReplacementPlayer = ValueNone
          PendingRoundEnd = false
          Winner = ValueNone
          SuddenDeathCount = 0 }

    let lifecycleState
        (manifest: BlokemonRuntimeManifest)
        (input: ReferenceObligationInput)
        : MatchState =
        let setup = materialize input
        let parameters = setup.Parameters

        let attackerMechanicalId, defenderMechanicalId =
            match setup.Route with
            | "promotion-decline" -> parameters[2], "BLK-001"
            | "play-kit" -> "BLK-003", "BLK-001"
            | "local-decline"
            | "local-trigger" -> "BLK-001", "BLK-150"
            | "kit-condition" -> parameters[2], "BLK-001"
            | _ -> parameters[0], "BLK-001"

        let mutable cards =
            [| setupCard manifest "attacker" attackerMechanicalId "first" CardZone.Oche -1
               setupCard manifest "defender" defenderMechanicalId "second" CardZone.Oche -1
               setupCard
                   manifest
                   "first-draw"
                   (basicVimFor manifest BlokemonMechanicalType.Fire)
                   "first"
                   CardZone.Stack
                   0
               setupCard
                   manifest
                   "second-draw"
                   (basicVimFor manifest BlokemonMechanicalType.Water)
                   "second"
                   CardZone.Stack
                   0 |]

        let add owner id mechanicalId cardZone position =
            let value = setupCard manifest id mechanicalId owner cardZone position

            cards <-
                cards
                |> Array.filter (fun current -> current.Id <> value.Id)
                |> Array.append [| value |]

        let attach id target =
            cards <- attachSetupCard (CardInstanceId id) (CardInstanceId target) cards

        let mutable firstRounds = 2
        let mutable firstBarChits = 6

        match setup.Route with
        | "continuous-refresh" ->
            let setupName = parameters[2]

            let vimType =
                match setupName with
                | "Water" -> Some BlokemonMechanicalType.Water
                | "Lightning" -> Some BlokemonMechanicalType.Lightning
                | "Fire" -> Some BlokemonMechanicalType.Fire
                | "unequal" -> Some BlokemonMechanicalType.Water
                | _ -> None

            match vimType with
            | Some mechanicalType ->
                add "first" "vim-0" (basicVimFor manifest mechanicalType) CardZone.Mitt -1

                attach "vim-0" "attacker"
            | None -> ()

            let moveSourceToBooth named =
                cards <-
                    updateSetupCard
                        (CardInstanceId "attacker")
                        (fun value -> { value with Zone = CardZone.Booth })
                        cards

                add "first" "own-oche" named CardZone.Oche -1

            match setupName with
            | "booth" -> moveSourceToBooth "BLK-001"
            | "out-of-play" ->
                cards <-
                    updateSetupCard
                        (CardInstanceId "attacker")
                        (fun value -> { value with Zone = CardZone.Mitt })
                        cards

                add "first" "own-oche" "BLK-001" CardZone.Oche -1
            | "no-vim-self" ->
                add
                    "first"
                    "vim-sentinel"
                    (basicVimFor manifest BlokemonMechanicalType.Water)
                    CardZone.Mitt
                    -1
            | value when value.StartsWith("booth-named-", StringComparison.Ordinal) ->
                moveSourceToBooth value["booth-named-".Length ..]
            | value when value.StartsWith("named-", StringComparison.Ordinal) ->
                add "first" "named-condition" value["named-".Length ..] CardZone.Booth -1
            | "first-round" -> firstRounds <- 1
            | _ -> ()
        | "activated-decline"
        | "activated-trigger" ->
            let setupName = parameters[2]
            let candidate = parameters[3]

            match setupName with
            | "damaged-self" ->
                cards <-
                    updateSetupCard
                        (CardInstanceId "attacker")
                        (fun value -> { value with Damage = 60 })
                        cards
            | "opponent-mitt" -> add "second" candidate "BLK-004" CardZone.Mitt -1
            | "stack-kit" -> add "first" candidate "KIT-010" CardZone.Stack 1
            | "stack-bloke-first-round" ->
                add "first" candidate "BLK-001" CardZone.Stack 1
                firstRounds <- 1
            | "empties-kit" ->
                add "first" candidate "KIT-012" CardZone.EmptiesTray -1

                if setup.Route = "activated-trigger" then
                    add "first" "candidate-2" "KIT-012" CardZone.EmptiesTray -1
            | "three-card-draw" ->
                add "first" "mitt-sentinel" "KIT-001" CardZone.Mitt -1

                add
                    "first"
                    "effect-draw-1"
                    (basicVimFor manifest BlokemonMechanicalType.Fire)
                    CardZone.Stack
                    1

                add
                    "first"
                    "effect-draw-2"
                    (basicVimFor manifest BlokemonMechanicalType.Water)
                    CardZone.Stack
                    2
            | "with-own-booth" -> add "first" "own-booth" "BLK-004" CardZone.Booth -1
            | "fixed-draw-sentinel" -> add "first" "mitt-sentinel" "KIT-001" CardZone.Mitt -1
            | "default" -> ()
            | unknown -> invalidOp $"Unknown production lifecycle activation setup {unknown}."
        | "activated-unavailable" ->
            let setupName = parameters[2]

            if setupName = "booth" || setupName = "booth-first-round" then
                cards <-
                    updateSetupCard
                        (CardInstanceId "attacker")
                        (fun value -> { value with Zone = CardZone.Booth })
                        cards

                add "first" "own-oche" "BLK-001" CardZone.Oche -1

            if setupName = "booth-first-round" then
                firstRounds <- 1
        | "promotion-decline" ->
            add "first" "promotion" parameters[0] CardZone.Mitt -1

            add
                "first"
                "retained-vim"
                (basicVimFor manifest BlokemonMechanicalType.Water)
                CardZone.Mitt
                -1

            attach "retained-vim" "attacker"

            cards <-
                updateSetupCard
                    (CardInstanceId "attacker")
                    (fun value ->
                        { value with
                            Damage = 20
                            RoughStates =
                                ImmutableArray.Create(
                                    { State = BlokemonRoughState.DodgyPint
                                      AppliedAtOwnerRound = 1 }
                                    : RoughStateEntry
                                ) })
                    cards
        | "local-decline"
        | "local-trigger" ->
            add "first" "local-under-test" "KIT-006" CardZone.Local -1

            add
                "first"
                "mitt-vim"
                (basicVimFor manifest BlokemonMechanicalType.Water)
                CardZone.Mitt
                -1

            if setup.Route = "local-trigger" then
                add "first" "mitt-sentinel" "KIT-001" CardZone.Mitt -1

                add
                    "first"
                    "effect-draw-2"
                    (basicVimFor manifest BlokemonMechanicalType.Fire)
                    CardZone.Stack
                    1

                add
                    "first"
                    "effect-draw-3"
                    (basicVimFor manifest BlokemonMechanicalType.Lightning)
                    CardZone.Stack
                    2
        | "play-kit" ->
            let kit = parameters[0]
            let mode = parameters[2]
            add "first" "kit-under-test" kit CardZone.Mitt -1

            match kit with
            | "KIT-007" ->
                add
                    "first"
                    "effect-draw-1"
                    (basicVimFor manifest BlokemonMechanicalType.Fire)
                    CardZone.Stack
                    1

                add
                    "first"
                    "prize"
                    (basicVimFor manifest BlokemonMechanicalType.Lightning)
                    CardZone.BarChit
                    0

                firstBarChits <- 1
            | "KIT-009" -> add "second" "other-mitt-bloke" "BLK-004" CardZone.Mitt -1
            | "KIT-010" ->
                add
                    "second"
                    "other-vim"
                    (basicVimFor manifest BlokemonMechanicalType.Water)
                    CardZone.Mitt
                    -1

                add
                    "first"
                    "own-vim"
                    (basicVimFor manifest BlokemonMechanicalType.Fire)
                    CardZone.Mitt
                    -1

                attach "other-vim" "defender"
            | "KIT-011" -> add "second" "other-mitt-bloke" "BLK-004" CardZone.Mitt -1
            | "KIT-008" when mode = "badge" ->
                add "first" "own-booth" "BLK-004" CardZone.Booth -1

                add
                    "first"
                    "candidate"
                    (basicVimFor manifest BlokemonMechanicalType.Water)
                    CardZone.EmptiesTray
                    -1
            | "KIT-005" when mode.StartsWith("search-", StringComparison.Ordinal) ->
                cards <-
                    updateSetupCard
                        (CardInstanceId "first-draw")
                        (fun value ->
                            { value with
                                MechanicalId = MechanicalCardId "BLK-001"
                                Kind = CardKind.Bloke })
                        cards

                for index in 1..7 do
                    add "first" $"top-{index}" "BLK-001" CardZone.Stack index
            | _ -> ()
        | "kit-condition" ->
            add "first" "kit-under-test" parameters[0] CardZone.Mitt -1

            add
                "second"
                "other-vim-0"
                (basicVimFor manifest BlokemonMechanicalType.Grass)
                CardZone.Mitt
                -1

            add
                "second"
                "other-vim-1"
                (basicVimFor manifest BlokemonMechanicalType.Water)
                CardZone.Mitt
                -1

            attach "other-vim-0" "defender"
            attach "other-vim-1" "defender"
        | unknown -> invalidOp $"Unowned production lifecycle route {unknown}."

        for explicitCard in setup.Cards do
            add
                explicitCard.Owner.Value
                explicitCard.Id.Value
                explicitCard.MechanicalId.Value
                explicitCard.Zone
                -1

        for count in setup.ZoneCounts do
            for index in 0 .. count.Count - 1 do
                add
                    count.Owner.Value
                    $"input-{count.Owner.Value}-{count.Zone}-{index}"
                    (basicVimFor manifest BlokemonMechanicalType.Grass)
                    count.Zone
                    index

        let players =
            [| "first"; "second" |]
            |> Array.map (fun id ->
                let barChits =
                    setup.Players
                    |> Array.tryFind (fun value -> value.Id = PlayerId id)
                    |> Option.map _.BarChitsRemaining
                    |> Option.defaultValue (if id = "first" then firstBarChits else 6)

                ({ Id = PlayerId id
                   BarChitsRemaining = barChits
                   MulliganCount = 0
                   MulliganBonusAllowance = 0
                   MulliganBonusChosen = true
                   BonusDrawn = ImmutableArray<_>.Empty
                   BonusPlacementChosen = true
                   OpeningChosen = true
                   RoundsStarted = if id = "first" then firstRounds else 2 }
                : PlayerState))
            |> ImmutableArray.CreateRange

        { Id = MatchId $"obligation:{setup.Id}"
          AuthorityVersion = manifest.ManifestVersion
          Seed = setup.Seed
          Random = MatchRandomState(setup.Seed.Value, 0)
          Revision = MatchRevision 0L
          LastEventSequence = 0L
          Phase = MatchPhase.Playing
          OpeningPlayer = PlayerId "second"
          ActivePlayer = PlayerId "first"
          RoundNumber = 4
          Players = players
          Cards = cards |> Array.sortBy _.Id.Value |> ImmutableArray.CreateRange
          Effects = ImmutableArray<_>.Empty
          ProcessedCommands = ImmutableArray<_>.Empty
          RoundUsage = RoundUsage.Empty(PlayerId "first")
          PendingEffect = ValueNone
          PendingKnockout = ValueNone
          PendingBarChits = ImmutableArray<_>.Empty
          ReplacementPlayer = ValueNone
          PendingRoundEnd = false
          Winner = ValueNone
          SuddenDeathCount = 0 }

    let programCommand (selected: CanonicalAction) =
        let parts = selected.Payload.Split(';')

        if selected.Kind <> "Attack" || parts.Length <> 2 then
            invalidOp $"Unsupported deterministic production action {selected.Kind}."

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = selected.Choices |> Seq.map canonicalChoice |> ImmutableArray.CreateRange
          Action =
            MatchAction.Attack(
                CardInstanceId(parts[0].Substring(9)),
                EffectId(parts[1].Substring(7))
            ) }

    let lifecycleCommand (selected: CanonicalAction) =
        let parts = selected.Payload.Split(';')

        let action =
            match selected.Kind with
            | "Attack" ->
                MatchAction.Attack(
                    CardInstanceId(parts[0].Substring(9)),
                    EffectId(parts[1].Substring(7))
                )
            | "EndRound" -> MatchAction.EndRound
            | "Promote" ->
                MatchAction.Promote(
                    CardInstanceId(parts[0].Substring(10)),
                    CardInstanceId(parts[1].Substring(9))
                )
            | "PlayKit" ->
                let target = parts[1].Substring(7)

                MatchAction.PlayKit(
                    CardInstanceId(parts[0].Substring(4)),
                    if String.IsNullOrEmpty target then
                        ValueNone
                    else
                        ValueSome(CardInstanceId target)
                )
            | "UsePartyTrick" ->
                MatchAction.UsePartyTrick(
                    CardInstanceId(parts[0].Substring(7)),
                    EffectId(parts[1].Substring(7))
                )
            | other -> invalidOp $"Unsupported lifecycle production action {other}."

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = selected.Choices |> Seq.map canonicalChoice |> ImmutableArray.CreateRange
          Action = action }

    let structuredProgramCommand (selected: CanonicalAction) (input: ProductionActionInput) =
        let choices =
            input.Choices
            |> Array.filter (fun value ->
                let id =
                    match value.Choice with
                    | EffectChoice.Optional(id, _)
                    | EffectChoice.Amount(id, _)
                    | EffectChoice.Cards(id, _)
                    | EffectChoice.MechanicalType(id, _)
                    | EffectChoice.Attack(id, _)
                    | EffectChoice.Distribution(id, _)
                    | EffectChoice.Attachments(id, _) -> id.Value

                (not value.WhenAvailable
                 || (selected.Requirements |> Array.exists (fun requirement -> requirement.Id = id)))
                && (selected.Requirements
                    |> Array.exists (fun requirement ->
                        requirement.Id = id && requirement.Chooser = input.Command.Actor.Value)))
            |> Seq.map _.Choice
            |> ImmutableArray.CreateRange

        { input.Command with Choices = choices }

    let resolutionCommand (selected: CanonicalAction) =
        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = selected.Choices |> Seq.map canonicalChoice |> ImmutableArray.CreateRange
          Action = MatchAction.ResolveEffectChoice }

    let structuredResolutionCommand (selected: CanonicalAction) (input: ProductionActionInput) =
        let requirements = selected.Requirements |> Array.map _.Id |> Set.ofArray

        let choices =
            input.Choices
            |> Array.filter (fun value ->
                let id =
                    match value.Choice with
                    | EffectChoice.Optional(id, _)
                    | EffectChoice.Amount(id, _)
                    | EffectChoice.Cards(id, _)
                    | EffectChoice.MechanicalType(id, _)
                    | EffectChoice.Attack(id, _)
                    | EffectChoice.Distribution(id, _)
                    | EffectChoice.Attachments(id, _) -> id.Value

                requirements.Contains id)
            |> Seq.map _.Choice
            |> ImmutableArray.CreateRange

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = choices
          Action = MatchAction.ResolveEffectChoice }

    let private ids (text: string) =
        text.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Seq.map CardInstanceId
        |> ImmutableArray.CreateRange

    let commonCommand (selected: CanonicalAction) =
        if selected.Choices.Length <> 0 then
            invalidOp "Common-foundation commands cannot dispatch program choices."

        let action =
            match selected.Kind with
            | "AttachVim" ->
                let parts = selected.Payload.Split(';')

                MatchAction.AttachVim(
                    CardInstanceId(parts[0].Substring(4)),
                    CardInstanceId(parts[1].Substring(7))
                )
            | "PlayBloke" -> MatchAction.PlayBloke(CardInstanceId(selected.Payload.Substring(6)))
            | "Promote" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Promote(
                    CardInstanceId(parts[0].Substring(10)),
                    CardInstanceId(parts[1].Substring(9))
                )
            | "Attack" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Attack(
                    CardInstanceId(parts[0].Substring(9)),
                    EffectId(parts[1].Substring(7))
                )
            | "Taxi" ->
                let parts = selected.Payload.Split(';')

                MatchAction.Taxi(CardInstanceId(parts[0].Substring(6)), ids (parts[1].Substring(4)))
            | "ChuckFossil" ->
                MatchAction.ChuckFossil(CardInstanceId(selected.Payload.Substring(7)))
            | "EndRound" -> MatchAction.EndRound
            | "ChooseReplacement" ->
                MatchAction.ChooseReplacement(CardInstanceId(selected.Payload.Substring(12)))
            | "Resign" -> MatchAction.Resign
            | other -> invalidOp $"Unsupported common-foundation production action {other}."

        { Id = CommandId selected.CommandId
          MatchId = MatchId selected.MatchId
          Actor = PlayerId selected.Actor
          ExpectedRevision = MatchRevision selected.ExpectedRevision
          Choices = ImmutableArray<_>.Empty
          Action = action }

    let commonState (value: CanonicalState) : MatchState =
        let optionText (projection: string -> 'value) (text: string) : 'value voption =
            if String.IsNullOrEmpty text then
                ValueNone
            else
                ValueSome(projection text)

        let pendingEffect: PendingEffectResolution voption =
            if value.PendingEffect.Present then
                if value.PendingEffect.Action.Length <> 1 then
                    invalidOp
                        "A canonical pending effect must retain exactly one suspended command."

                ValueSome
                    { Command = commonCommand value.PendingEffect.Action[0]
                      Source = CardInstanceId value.PendingEffect.Source
                      Effect = EffectId value.PendingEffect.Effect
                      Chooser = PlayerId value.PendingEffect.Chooser
                      Requirements = ImmutableArray<_>.Empty
                      BeerMatResults = ImmutableArray.CreateRange value.PendingEffect.BeerMatResults
                      AttackStarted = value.PendingEffect.AttackStarted }
            else
                ValueNone

        let pendingKnockout: PendingKnockoutResolution voption =
            if value.PendingKnockout.Present then
                ValueSome
                    { KnockedOutCard = CardInstanceId value.PendingKnockout.KnockedOutCard
                      RemainingKnockouts =
                        value.PendingKnockout.RemainingKnockouts
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      TriggerSources =
                        value.PendingKnockout.TriggerSources
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      TriggerSource = CardInstanceId value.PendingKnockout.TriggerSource
                      TriggerEffect = EffectId value.PendingKnockout.TriggerEffect
                      Chooser = PlayerId value.PendingKnockout.Chooser
                      EligibleVim =
                        value.PendingKnockout.EligibleVim
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      AttackingCard = CardInstanceId value.PendingKnockout.AttackingCard
                      FinishRoundAfterResolution = value.PendingKnockout.FinishRoundAfterResolution
                      AttackDamageTargets =
                        value.PendingKnockout.AttackDamageTargets
                        |> Seq.map CardInstanceId
                        |> ImmutableArray.CreateRange
                      ExtraBarChits = value.PendingKnockout.ExtraBarChits }
            else
                ValueNone

        let players =
            value.Players
            |> Seq.map (fun player ->
                ({ Id = PlayerId player.Id
                   BarChitsRemaining = player.BarChitsRemaining
                   MulliganCount = player.MulliganCount
                   MulliganBonusAllowance = player.MulliganBonusAllowance
                   MulliganBonusChosen = player.MulliganBonusChosen
                   BonusDrawn =
                     player.BonusDrawn |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   BonusPlacementChosen = player.BonusPlacementChosen
                   OpeningChosen = player.OpeningChosen
                   RoundsStarted = player.RoundsStarted }
                : PlayerState))
            |> ImmutableArray.CreateRange

        let cards =
            value.Cards
            |> Seq.map (fun card ->
                let roughStates =
                    card.RoughStates
                    |> Seq.map (fun rough ->
                        ({ State = Enum.Parse<BlokemonRoughState>(rough.State)
                           AppliedAtOwnerRound = rough.AppliedAtOwnerRound }
                        : RoughStateEntry))
                    |> ImmutableArray.CreateRange

                ({ Id = CardInstanceId card.Id
                   MechanicalId = MechanicalCardId card.MechanicalId
                   Owner = PlayerId card.Owner
                   Kind = Enum.Parse<CardKind>(card.Kind)
                   Zone = Enum.Parse<CardZone>(card.Zone)
                   IsFaceDown = card.IsFaceDown
                   StackPosition = card.StackPosition
                   AttachedTo = card.AttachedTo |> optionText CardInstanceId
                   Attachments =
                     card.Attachments |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   UnderlyingCards =
                     card.UnderlyingCards |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
                   Damage = card.Damage
                   RoughStates = roughStates
                   EnteredAtOwnerRound = card.EnteredAtOwnerRound
                   LastPromotedRound = card.LastPromotedRound }
                : CardState))
            |> ImmutableArray.CreateRange

        let effects =
            value.Effects
            |> Seq.map (fun effect ->
                ({ SourceEffect = EffectId effect.SourceEffect
                   SourceCard = CardInstanceId effect.SourceCard
                   Owner = PlayerId effect.Owner
                   TargetCard = effect.TargetCard |> optionText CardInstanceId
                   Kind = Enum.Parse<TemporaryEffectKind>(effect.Kind)
                   Amount = effect.Amount
                   MechanicalTypes =
                     effect.MechanicalTypes
                     |> Seq.map (fun item -> Enum.Parse<BlokemonMechanicalType>(item))
                     |> ImmutableArray.CreateRange
                   RoughStates =
                     effect.RoughStates
                     |> Seq.map (fun item -> Enum.Parse<BlokemonRoughState>(item))
                     |> ImmutableArray.CreateRange
                   RelatedCards =
                     effect.RelatedCards |> Seq.map MechanicalCardId |> ImmutableArray.CreateRange
                   Conditions =
                     effect.Conditions
                     |> Seq.map (fun item -> Enum.Parse<BlokemonCondition>(item))
                     |> ImmutableArray.CreateRange
                   Duration = Enum.Parse<EffectDuration>(effect.Duration)
                   AppliesFromRound = effect.AppliesFromRound
                   ExpiresAfterRound = effect.ExpiresAfterRound }
                : TemporaryEffect))
            |> ImmutableArray.CreateRange

        let roundUsage: RoundUsage =
            { Player = PlayerId value.RoundUsage.Player
              VimAttachments = value.RoundUsage.VimAttachments
              MatesPlayed = value.RoundUsage.MatesPlayed
              LocalsPlayed = value.RoundUsage.LocalsPlayed
              TaxisUsed = value.RoundUsage.TaxisUsed
              EffectsUsed =
                value.RoundUsage.EffectsUsed |> Seq.map EffectId |> ImmutableArray.CreateRange
              KitsPlayed =
                value.RoundUsage.KitsPlayed
                |> Seq.map MechanicalCardId
                |> ImmutableArray.CreateRange }

        let pendingBarChits =
            value.PendingBarChits
            |> Seq.map (fun pending ->
                ({ Player = PlayerId pending.Player
                   Card = CardInstanceId pending.Card
                   Effect = EffectId pending.Effect
                   FinishRoundAfterResolution = pending.FinishRoundAfterResolution }
                : PendingBarChitResolution))
            |> ImmutableArray.CreateRange

        { Id = MatchId value.MatchId
          AuthorityVersion = value.AuthorityVersion
          Seed = MatchSeed value.Seed
          Random = MatchRandomState(value.Random.State, value.Random.ConsumptionIndex)
          Revision = MatchRevision value.Transport.Revision
          LastEventSequence = value.Transport.LastEventSequence
          Phase = Enum.Parse<MatchPhase>(value.Phase)
          OpeningPlayer = PlayerId value.OpeningPlayer
          ActivePlayer = PlayerId value.ActivePlayer
          RoundNumber = value.RoundNumber
          Players = players
          Cards = cards
          Effects = effects
          ProcessedCommands =
            value.Transport.ProcessedCommandIds
            |> Seq.map CommandId
            |> ImmutableArray.CreateRange
          RoundUsage = roundUsage
          PendingEffect = pendingEffect
          PendingKnockout = pendingKnockout
          PendingBarChits = pendingBarChits
          ReplacementPlayer = value.ReplacementPlayer |> optionText PlayerId
          PendingRoundEnd = value.PendingRoundEnd
          Winner = value.Terminal.Winner |> optionText PlayerId
          SuddenDeathCount = value.Terminal.SuddenDeathCount }
