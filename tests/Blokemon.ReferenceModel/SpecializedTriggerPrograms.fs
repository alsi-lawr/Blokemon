namespace Blokemon.ReferenceModel

open System

type ReferenceSpecializedTriggerMutation =
    | NoSpecializedTriggerMutation
    | SkipSpecializedTriggerDispatch
    | DropSpecializedPendingResolution
    | ReverseSpecializedTriggerSourceOrder
    | TreatBoothDamageAsAttack
    | SkipSpecializedPromotionTrigger

type ReferenceSpecializedTriggerObligation =
    { Input: ReferenceObligationInput
      InitialState: CanonicalState }

[<RequireQualifiedAccess>]
module ReferenceSpecializedTriggerPrograms =

    let acceptedRoutes =
        Set
            [ "bar-chit-trigger"
              "bar-chit-trigger-blank"
              "bar-chit-trigger-decline"
              "bar-chit-trigger-full-booth"
              "knockout-trigger"
              "knockout-trigger-decline"
              "paul-chuckle-trigger-fire"
              "paul-chuckle-trigger-nonfire"
              "promotion-trigger"
              "reactive-trigger"
              "trigger-nonfire"
              "trivial-booth-damage" ]

    let acceptedObligationIds =
        Set
            [ "paul-chuckle-trigger-fire"
              "paul-chuckle-trigger-nonfire"
              "trivial-booth-damage-blk-042-b01"
              "trivial-booth-damage-blk-087-b01"
              "trivial-booth-damage-blk-135-b01"
              "never-written-off-promotion-discards-top-five"
              "doormans-grit-recovers-on-badge"
              "one-spark-too-many-retaliates-on-badge"
              "knockout-trigger-blk-026-fire"
              "bar-chit-trigger-blk-113-badge"
              "promotion-makes-three-vim-offer"
              "promotion-makes-eight-vim-offer"
              "promotion-positive-blk-093-t01"
              "promotion-positive-blk-097-t01"
              "trigger-nonfire-blk-026-blk-026-t01"
              "trigger-nonfire-blk-044-blk-044-t01"
              "trigger-nonfire-blk-045-blk-045-t01"
              "trigger-nonfire-blk-068-blk-068-t01"
              "trigger-nonfire-blk-093-blk-093-t01"
              "trigger-nonfire-blk-097-blk-097-t01"
              "trigger-nonfire-blk-110-blk-110-t01"
              "trigger-nonfire-blk-113-blk-113-t01"
              "trigger-nonfire-blk-130-blk-130-t01"
              "knockout-trigger-blk-026-decline"
              "paul-chuckle-booth-damage-does-not-fire"
              "bar-chit-trigger-blk-113-full-booth-nonfire"
              "bar-chit-trigger-blk-113-decline"
              "bar-chit-trigger-blk-113-blank"
              "doormans-grit-recovers-on-blank"
              "one-spark-too-many-retaliates-on-blank" ]

    [<Literal>]
    let AcceptedRouteCount = 12

    [<Literal>]
    let AcceptedObligationCount = 30

    let private action = ReferenceEngine.action
    let private actionOrder = ReferenceEngine.actionOrder

    let private card
        (authority: ReferenceAuthority)
        (id: string)
        (mechanicalId: string)
        (owner: string)
        (zone: string)
        position
        =
        let definition =
            authority.Cards.TryFind mechanicalId
            |> Option.defaultWith (fun () ->
                invalidOp $"The specialised setup names unknown card {mechanicalId}.")

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

    let private updateCard id change (cards: CanonicalCard array) =
        cards |> Array.map (fun value -> if value.Id = id then change value else value)

    let private attachCard attachment target (cards: CanonicalCard array) =
        cards
        |> updateCard attachment (fun value ->
            { value with
                Zone = "Attached"
                StackPosition = -1
                AttachedTo = target })
        |> updateCard target (fun value ->
            { value with
                Attachments = Array.append value.Attachments [| attachment |] })

    let private battleState
        (authority: ReferenceAuthority)
        (input: ReferenceObligationInput)
        attackerMechanicalId
        defenderMechanicalId
        (attachedVim: string array)
        =
        let mutable cards =
            [| card authority "attacker" attackerMechanicalId "first" "Oche" -1
               card authority "defender" defenderMechanicalId "second" "Oche" -1
               card authority "first-draw" "VIM-BLAZED" "first" "Stack" 0
               card authority "second-draw" "VIM-SOBER" "second" "Stack" 0 |]

        for index in 0 .. attachedVim.Length - 1 do
            let id = $"vim-{index}"
            cards <- replaceCard (card authority id attachedVim[index] "first" "Mitt" -1) cards
            cards <- attachCard id "attacker" cards

        let players =
            [| { Id = "first"
                 BarChitsRemaining = 6
                 MulliganCount = 0
                 MulliganBonusAllowance = 0
                 MulliganBonusChosen = true
                 BonusDrawn = [||]
                 BonusPlacementChosen = true
                 OpeningChosen = true
                 RoundsStarted = 2 }
               { Id = "second"
                 BarChitsRemaining = 6
                 MulliganCount = 0
                 MulliganBonusAllowance = 0
                 MulliganBonusChosen = true
                 BonusDrawn = [||]
                 BonusPlacementChosen = true
                 OpeningChosen = true
                 RoundsStarted = 2 } |]

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

    let private specialisedState (authority: ReferenceAuthority) (input: ReferenceObligationInput) =
        let route = input.InitialState.Route.Value
        let parameters = input.InitialState.Parameters

        if route = "promotion-trigger" then
            ReferenceLifecyclePrograms.materializeInput authority input
        else
            let attacker, defender, vim =
                match route with
                | "reactive-trigger" ->
                    parameters[2], parameters[0], [| "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" |]
                | "knockout-trigger"
                | "knockout-trigger-decline"
                | "bar-chit-trigger"
                | "bar-chit-trigger-blank"
                | "bar-chit-trigger-decline"
                | "bar-chit-trigger-full-booth" ->
                    "BLK-003", "BLK-001", [| "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" |]
                | "paul-chuckle-trigger-fire" -> "BLK-076", "BLK-107", [| "VIM-LAIRY" |]
                | "paul-chuckle-trigger-nonfire" -> "BLK-040", "BLK-107", [| "VIM-SOBER" |]
                | "trigger-nonfire" ->
                    if parameters[2] = "bar-chit" then
                        "BLK-001", "BLK-001", [||]
                    else
                        parameters[0], "BLK-001", [||]
                | "trivial-booth-damage" ->
                    let allVim =
                        [| "VIM-DODGY"
                           "VIM-LAIRY"
                           "VIM-CURRY"
                           "VIM-BLAZED"
                           "VIM-BEER"
                           "VIM-GEEKED"
                           "VIM-SOBER" |]
                        |> Array.collect (fun id -> Array.create 4 id)

                    parameters[0], "BLK-003", allVim
                | unknown -> invalidOp $"Unowned specialised setup route {unknown}."

            let mutable state = battleState authority input attacker defender vim

            let add owner id mechanicalId zone position =
                state <-
                    { state with
                        Cards =
                            state.Cards
                            |> replaceCard (card authority id mechanicalId owner zone position)
                            |> Array.sortBy _.Id }

            match route with
            | "knockout-trigger"
            | "knockout-trigger-decline" ->
                add "second" "trigger-source" "BLK-026" "Booth" -1
                add "second" "movable-vim" "VIM-BEER" "Mitt" -1

                state <-
                    { state with
                        Cards = attachCard "movable-vim" "defender" state.Cards |> Array.sortBy _.Id }

                add "first" "prize" "VIM-LAIRY" "BarChit" 0

                state <-
                    { state with
                        Players =
                            state.Players
                            |> Array.map (fun player ->
                                if player.Id = "first" then
                                    { player with BarChitsRemaining = 1 }
                                else
                                    player) }
            | "bar-chit-trigger"
            | "bar-chit-trigger-blank"
            | "bar-chit-trigger-decline"
            | "bar-chit-trigger-full-booth" ->
                add "first" "triggered-prize" "BLK-113" "BarChit" 0
                add "first" "extra-prize" "VIM-LAIRY" "BarChit" 1
                add "second" "defender-bench" "BLK-004" "Booth" -1

                if route = "bar-chit-trigger-full-booth" then
                    for index in 0..4 do
                        add "first" $"full-booth-{index}" "BLK-004" "Booth" index

                state <-
                    { state with
                        Players =
                            state.Players
                            |> Array.map (fun player ->
                                if player.Id = "first" then
                                    { player with BarChitsRemaining = 2 }
                                else
                                    player) }
            | "trigger-nonfire" when parameters[2] = "bar-chit" ->
                state <-
                    { state with
                        Cards =
                            state.Cards
                            |> updateCard "attacker" (fun value ->
                                { value with
                                    MechanicalId = parameters[0]
                                    Zone = "BarChit"
                                    IsFaceDown = true
                                    StackPosition = 0 })
                            |> Array.sortBy _.Id
                        Players =
                            state.Players
                            |> Array.map (fun player ->
                                if player.Id = "first" then
                                    { player with BarChitsRemaining = 1 }
                                else
                                    player) }

                add "first" "own-oche" "BLK-001" "Oche" -1
            | "trivial-booth-damage" ->
                let benchOwner = if parameters.Length = 4 then parameters[3] else "BLK-003"

                add "second" "a-other-booth" benchOwner "Booth" -1
            | "paul-chuckle-trigger-fire" ->
                state <-
                    { state with
                        Cards =
                            state.Cards
                            |> updateCard "defender" (fun value -> { value with Damage = 70 }) }
            | _ -> ()

            for explicitCard in input.InitialState.Cards do
                add
                    explicitCard.Owner
                    explicitCard.CardId
                    explicitCard.MechanicalId
                    (string explicitCard.Zone)
                    -1

            state

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
                $"The BLOKEMON-138 ledger contains {selected.Length} obligations instead of {AcceptedObligationCount}."

        if selectedRoutes <> acceptedRoutes || selectedRoutes.Count <> AcceptedRouteCount then
            invalidOp "The BLOKEMON-138 route ledger is missing, stale, duplicated, or borrowed."

        if
            selectedIds <> acceptedObligationIds
            || selectedIds.Count <> AcceptedObligationCount
        then
            invalidOp
                "The BLOKEMON-138 obligation ledger is missing, stale, duplicated, or borrowed."

        if duplicateIds.Length <> 0 then
            invalidOp
                $"The BLOKEMON-138 obligation ledger contains duplicate identities: {String.Join(',', duplicateIds)}."

        selected

    let materialize authority inventory =
        reconcile inventory
        |> Array.map (fun input ->
            { Input = input
              InitialState = specialisedState authority input })

    let materializeInput authority input = specialisedState authority input

    let private pendingKnockoutActions (state: CanonicalState) actor =
        let pending = state.PendingKnockout

        if not pending.Present || pending.Chooser <> actor then
            [||]
        else
            Array.append [| "" |] pending.EligibleVim
            |> Array.map (fun vim ->
                let suffix = if String.IsNullOrEmpty vim then "decline" else vim

                let stableKey =
                    if String.IsNullOrEmpty vim then
                        "trigger:1:decline"
                    else
                        $"trigger:0:{vim}"

                action
                    state
                    "ResolveKnockoutTrigger"
                    actor
                    $"trigger:{pending.TriggerEffect}:{suffix}"
                    stableKey
                    $"vim={vim}"
                    [||]
                    [||])
            |> Array.sortBy _.StableKey

    let private pendingBarChitActions (state: CanonicalState) actor =
        match state.PendingBarChits |> Array.tryHead with
        | Some pending when pending.Player = actor ->
            [| true; false |]
            |> Array.map (fun booth ->
                let zone = if booth then "booth" else "mitt"
                let stableKey = if booth then "bar-chit:0:booth" else "bar-chit:1:mitt"

                action
                    state
                    "ResolveBarChitTrigger"
                    actor
                    $"bar-chit:{pending.Card}:{zone}"
                    stableKey
                    $"booth={booth.ToString().ToLowerInvariant()}"
                    [||]
                    [||])
        | _ -> [||]

    let legalActions (authority: ReferenceAuthority) (state: CanonicalState) actor =
        if state.PendingKnockout.Present then
            pendingKnockoutActions state actor
        elif state.PendingBarChits.Length <> 0 then
            pendingBarChitActions state actor
        elif
            state.Cards
            |> Array.exists (fun value ->
                value.Owner = actor && value.Zone = "Mitt" && value.Kind = "Bloke")
        then
            ReferenceLifecyclePrograms.legalActions authority state actor
        else
            ReferenceDeterministicPrograms.legalActions authority state actor
            |> Array.filter (fun value ->
                value.Kind = "Attack" || value.Kind = "EndRound" || value.Kind = "Promote")

    let private inputChoices (selected: CanonicalAction) (input: ReferenceActionInput) =
        let requirements = selected.Requirements |> Array.map _.Id |> Set.ofArray

        input.Choices
        |> Array.filter (fun value ->
            (not value.WhenAvailable || requirements.Contains value.RequirementId)
            && (selected.Requirements
                |> Array.exists (fun requirement ->
                    requirement.Id = value.RequirementId && requirement.Chooser = input.Actor)))
        |> Array.map ReferenceDeterministicPrograms.canonicalChoice

    let selectAction
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (input: ReferenceActionInput)
        commandIndex
        =
        let legal = legalActions authority state input.Actor

        let selected =
            match input.Kind with
            | ReferenceInputActionKind.ResolveKnockoutTrigger ->
                legal
                |> Array.find (fun value ->
                    value.Kind = "ResolveKnockoutTrigger"
                    && value.Payload = $"vim={input.TargetCard}")
            | ReferenceInputActionKind.ResolveBarChitTrigger ->
                let booth = (input.TargetCard = "Booth").ToString().ToLowerInvariant()

                legal
                |> Array.find (fun value ->
                    value.Kind = "ResolveBarChitTrigger" && value.Payload = $"booth={booth}")
            | ReferenceInputActionKind.Promote ->
                legal
                |> Array.find (fun value ->
                    value.Kind = "Promote"
                    && value.StableKey = $"promote:{input.SourceCard}:{input.TargetCard}")
            | ReferenceInputActionKind.EndRound ->
                legal |> Array.find (fun value -> value.Kind = "EndRound")
            | ReferenceInputActionKind.Attack ->
                legal
                |> Array.find (fun value ->
                    value.Kind = "Attack"
                    && value.StableKey = $"attack:{input.SourceCard}:{input.EffectId}")
            | other -> invalidOp $"Unsupported specialised input action {other}."

        { selected with
            CommandId = $"obligation:{commandIndex}"
            StableKey = ""
            Choices = inputChoices selected input }

    let private rejection state code requirements =
        { State = state
          Events = [||]
          Rejection =
            [| { Code = code
                 ChoiceRequirements = requirements } |] }

    let private boundary
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if selected.MatchId <> state.MatchId then
            Some "WrongMatch"
        elif Array.contains selected.CommandId state.Transport.ProcessedCommandIds then
            Some "DuplicateCommand"
        elif selected.ExpectedRevision <> state.Transport.Revision then
            Some "StaleRevision"
        elif state.AuthorityVersion <> authority.ManifestVersion then
            Some "AuthorityMismatch"
        elif not (state.Players |> Array.exists (fun player -> player.Id = selected.Actor)) then
            Some "UnknownActor"
        elif state.Phase = "Complete" then
            Some "MatchComplete"
        else
            None

    let private commit
        (stateBefore: CanonicalState)
        (selected: CanonicalAction)
        (state: CanonicalState)
        (semanticEvents: CanonicalEvent array)
        =
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
                (stateBefore.Transport.Revision + 1L)
                beforeCommit
                (Array.append [| submitted |] semanticEvents)

        { State = committed
          Events = events
          Rejection = [||] }

    let private triggerOf kind (authority: ReferenceAuthority) (source: CanonicalCard) =
        authority.Cards[source.MechanicalId].PartyTricks
        |> Array.tryFind (fun trick -> trick.Trigger = kind)

    let private triggerAction
        (selected: CanonicalAction)
        (effect: string)
        (choices: CanonicalChoice array)
        =
        { selected with
            Choices = choices
            Requirements = [||]
            Payload = $"trigger={effect}" }

    let private executeTrigger
        authority
        (selected: CanonicalAction)
        actor
        (source: CanonicalCard)
        (trick: ReferencePartyTrick)
        choices
        state
        =
        ReferenceDeterministicPrograms.executeStandaloneProgram
            authority
            NoProgramMutation
            (triggerAction selected trick.MechanicalId choices)
            actor
            source
            trick.MechanicalId
            trick.Program
            false
            [||]
            state

    let private queueBarChitTriggers
        (authority: ReferenceAuthority)
        mutation
        player
        (cards: string array)
        finishRound
        (state: CanonicalState)
        =
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        if mutation <> DropSpecializedPendingResolution then
            for cardId in cards do
                let card = ReferenceState.card next cardId

                match triggerOf ReferenceTrigger.OnBarChitTaken authority card with
                | Some trick when
                    (ReferenceState.cardsIn next player "Booth").Length < authority.BaseRules.Opening.BoothLimit
                    ->
                    next <-
                        ReferenceCommonFoundation.queueBarChit
                            { Player = player
                              Card = cardId
                              Effect = trick.MechanicalId
                              FinishRoundAfterResolution = finishRound }
                            next

                    events.Add
                        { ReferenceEvents.create "TriggerQueued" with
                            Actor = player
                            SourceCard = cardId
                            Effect = trick.MechanicalId }
                | _ -> ()

        next, events.ToArray()

    let private takeBarChits
        (authority: ReferenceAuthority)
        mutation
        player
        count
        source
        finishRound
        state
        =
        let taken, takeEvents, ids =
            ReferenceCommonFoundation.takeBarChits NoReferenceMutation player count source state

        let queued, queueEvents =
            queueBarChitTriggers authority mutation player ids finishRound taken

        queued, Array.append takeEvents queueEvents, ids

    let private sendHomeOne
        (authority: ReferenceAuthority)
        mutation
        (selected: CanonicalAction)
        (attackingCard: string)
        attackedByDamage
        finishRound
        (current: CanonicalCard)
        state
        =
        let retaliation =
            if attackedByDamage && current.Zone = "Oche" then
                triggerOf ReferenceTrigger.AfterSelfSentHomeByAttackDamage authority current
            else
                None

        let next = ReferenceCommonFoundation.chuckCardPile current.Id state
        let events = ResizeArray<CanonicalEvent>()

        events.Add
            { ReferenceEvents.create "BlokeSentHome" with
                Actor = ReferenceState.otherPlayer next current.Owner
                SourceCard = current.Id
                TargetCards = [| current.Id |] }

        let takingPlayer = ReferenceState.otherPlayer next current.Owner

        let mutable next, awardEvents, _ =
            takeBarChits
                authority
                mutation
                takingPlayer
                (ReferenceCommonFoundation.barChitsFor authority current)
                current.Id
                finishRound
                next

        events.AddRange awardEvents

        let mutable reflected = [||]

        match retaliation with
        | Some trick when mutation <> SkipSpecializedTriggerDispatch ->
            let source = ReferenceState.card next current.Id

            let (executed, triggerEvents, _, forced, deferred, _, executionRejection, _) =
                executeTrigger authority selected current.Owner source trick [||] next

            if deferred.Length <> 0 || executionRejection.IsSome then
                invalidOp $"The inline retaliation {trick.MechanicalId} did not execute."

            let forced =
                forced
                |> Array.map (fun target ->
                    if target = current.Id && not (String.IsNullOrEmpty attackingCard) then
                        attackingCard
                    else
                        target)
                |> Array.distinct

            next <- executed
            events.AddRange triggerEvents
            reflected <- forced

            events.Add
                { ReferenceEvents.create "TriggerResolved" with
                    Actor = current.Owner
                    SourceCard = current.Id
                    TargetCards = forced
                    Effect = trick.MechanicalId }
        | _ -> ()

        next <- ReferenceCommonFoundation.assignReplacement NoReferenceMutation current.Owner next
        next, events.ToArray(), reflected

    let private knockoutTriggerSources
        (authority: ReferenceAuthority)
        mutation
        (knockedOut: CanonicalCard)
        (state: CanonicalState)
        =
        state.Cards
        |> Array.filter (fun card ->
            card.Owner = knockedOut.Owner
            && card.Id <> knockedOut.Id
            && ReferenceDeterministicPrograms.inPlay card
            && (triggerOf ReferenceTrigger.OnOwnBlokeSentHomeByOtherAttackDamage authority card)
                .IsSome)
        |> Array.sortBy _.Id
        |> fun values ->
            if mutation = ReverseSpecializedTriggerSourceOrder then
                Array.rev values
            else
                values

    let private lightningVim
        (authority: ReferenceAuthority)
        (knockedOut: CanonicalCard)
        (state: CanonicalState)
        =
        knockedOut.Attachments
        |> Array.map (ReferenceState.card state)
        |> Array.filter (fun card ->
            card.Kind = "Vim"
            && authority.Cards[card.MechanicalId].VimType = ValueSome
                ReferenceMechanicalType.Lightning)
        |> Array.map _.Id
        |> Array.sort

    let private queueKnockout
        authority
        mutation
        selected
        attackingCard
        (attackTargets: string array)
        extraBarChits
        (remaining: string array)
        (knockedOut: CanonicalCard)
        state
        =
        if
            mutation = DropSpecializedPendingResolution
            || mutation = SkipSpecializedTriggerDispatch
            || (ReferenceState.card state attackingCard).Owner = knockedOut.Owner
            || knockedOut.Damage < ReferenceCommonFoundation.stayingPower authority knockedOut
        then
            None
        else
            let sources = knockoutTriggerSources authority mutation knockedOut state
            let eligible = lightningVim authority knockedOut state

            match sources, eligible with
            | [||], _
            | _, [||] -> None
            | _ ->
                let first = sources[0]

                let trick =
                    triggerOf ReferenceTrigger.OnOwnBlokeSentHomeByOtherAttackDamage authority first
                    |> Option.get

                let suspended =
                    ReferenceCommonFoundation.suspendForKnockout
                        { Present = true
                          KnockedOutCard = knockedOut.Id
                          RemainingKnockouts = remaining
                          TriggerSources = sources[1..] |> Array.map _.Id
                          TriggerSource = first.Id
                          TriggerEffect = trick.MechanicalId
                          Chooser = knockedOut.Owner
                          EligibleVim = eligible
                          AttackingCard = attackingCard
                          FinishRoundAfterResolution = true
                          AttackDamageTargets = attackTargets
                          ExtraBarChits = extraBarChits }
                        state

                let queued =
                    { ReferenceEvents.create "TriggerQueued" with
                        Actor = knockedOut.Owner
                        SourceCard = first.Id
                        TargetCards = [| knockedOut.Id |]
                        Effect = trick.MechanicalId }

                Some(suspended, queued)

    let private tryRecover authority mutation selected (current: CanonicalCard) state =
        if mutation = SkipSpecializedTriggerDispatch then
            state, [||], false
        else
            match triggerOf ReferenceTrigger.BeforeSelfSentHomeByAttackDamage authority current with
            | None -> state, [||], false
            | Some trick ->
                let (executed, events, _, _, deferred, _, executionRejection, _) =
                    executeTrigger authority selected current.Owner current trick [||] state

                if deferred.Length <> 0 || executionRejection.IsSome then
                    invalidOp $"The recovery trigger {trick.MechanicalId} did not execute."

                let recovered = ReferenceState.card executed current.Id

                let didRecover =
                    recovered.Damage < ReferenceCommonFoundation.stayingPower authority recovered

                let semantic =
                    Array.append
                        events
                        [| { ReferenceEvents.create "TriggerResolved" with
                               Actor = current.Owner
                               SourceCard = current.Id
                               Effect = trick.MechanicalId } |]

                executed, semantic, didRecover

    let private resolveIdentifiedKnockouts
        authority
        mutation
        selected
        attackingCard
        (attackTargets: string array)
        extraBarChits
        (resolvedTriggerCards: Set<string>)
        (identified: string array)
        state
        =
        let candidates = ResizeArray<string>(identified)
        let events = ResizeArray<CanonicalEvent>()
        let mutable next = state
        let mutable index = 0
        let mutable pended = false
        let mutable extraAwarded = false

        while not pended && index < candidates.Count do
            match ReferenceState.tryCard next candidates[index] with
            | Some current when ReferenceDeterministicPrograms.inPlay current ->
                let attackedByDamage =
                    not (String.IsNullOrEmpty attackingCard)
                    && Array.contains current.Id attackTargets
                    && current.Damage >= ReferenceCommonFoundation.stayingPower authority current

                let recoveredState, recoveryEvents, recovered =
                    if attackedByDamage then
                        tryRecover authority mutation selected current next
                    else
                        next, [||], false

                next <- recoveredState
                events.AddRange recoveryEvents

                if not recovered then
                    match
                        if attackedByDamage && not (resolvedTriggerCards.Contains current.Id) then
                            queueKnockout
                                authority
                                mutation
                                selected
                                attackingCard
                                attackTargets
                                (if extraAwarded then 0 else extraBarChits)
                                (candidates |> Seq.skip (index + 1) |> Seq.toArray)
                                (ReferenceState.card next current.Id)
                                next
                        else
                            None
                    with
                    | Some(suspended, queued) ->
                        next <- suspended
                        events.Add queued
                        pended <- true
                    | None ->
                        let sent, sentEvents, reflected =
                            sendHomeOne
                                authority
                                mutation
                                selected
                                attackingCard
                                attackedByDamage
                                true
                                (ReferenceState.card next current.Id)
                                next

                        next <- sent
                        events.AddRange sentEvents

                        for reflectedId in reflected do
                            match ReferenceState.tryCard next reflectedId with
                            | Some card when ReferenceDeterministicPrograms.inPlay card ->
                                candidates.Add reflectedId
                            | _ -> ()

                        if attackedByDamage && not extraAwarded && extraBarChits > 0 then
                            let attacker = ReferenceState.card next attackingCard

                            let awarded, awardEvents, _ =
                                takeBarChits
                                    authority
                                    mutation
                                    attacker.Owner
                                    extraBarChits
                                    attackingCard
                                    true
                                    next

                            next <- awarded
                            events.AddRange awardEvents
                            extraAwarded <- true
            | _ -> ()

            index <- index + 1

        if pended then
            next, events.ToArray(), false
        else
            let random = ReferenceRandom(next.Random)

            let resolved, winEvents =
                ReferenceCommonFoundation.resolveWins authority NoReferenceMutation random "" next

            events.AddRange winEvents

            let resolved =
                if resolved.Phase <> "Complete" && resolved.PendingBarChits.Length <> 0 then
                    { resolved with
                        Phase = "AwaitingTriggerChoice" }
                else
                    resolved

            { resolved with
                Random = random.Snapshot },
            events.ToArray(),
            true

    let private resolveAfterDamageTriggers
        authority
        mutation
        (attacker: CanonicalCard)
        (before: CanonicalState)
        (attackTargets: string array)
        (state: CanonicalState)
        =
        let events = ResizeArray<CanonicalEvent>()
        let mutable next = state

        if mutation <> SkipSpecializedTriggerDispatch then
            let targeted =
                if mutation = TreatBoothDamageAsAttack then
                    next.Cards
                    |> Array.filter (fun card ->
                        let previous = ReferenceState.card before card.Id
                        card.Damage > previous.Damage)
                    |> Array.map _.Id
                else
                    attackTargets

            for targetId in targeted do
                let beforeTarget = ReferenceState.card before targetId
                let afterTarget = ReferenceState.card next targetId

                if afterTarget.Damage > beforeTarget.Damage then
                    match
                        triggerOf ReferenceTrigger.AfterSelfDamagedByAttack authority afterTarget
                    with
                    | None -> ()
                    | Some trick ->
                        let rec counterAmount (instructions: ReferenceInstruction array) =
                            instructions
                            |> Array.tryPick (fun instruction ->
                                if
                                    instruction.Opcode = ReferenceOpcode.PlaceDamageCounters
                                    && Array.contains
                                        ReferenceLocation.AttackingBloke
                                        instruction.Targets
                                then
                                    Some instruction.Amount
                                else
                                    counterAmount instruction.Then
                                    |> Option.orElseWith (fun () ->
                                        counterAmount instruction.Otherwise))

                        let sourceAllows =
                            mutation = TreatBoothDamageAsAttack || afterTarget.Zone = "Oche"

                        match counterAmount trick.Program with
                        | Some counters when sourceAllows && counters > 0 ->
                            let target = ReferenceState.card next attacker.Id
                            let amount = counters * 10

                            next <-
                                ReferenceState.updateCard
                                    { target with
                                        Damage = target.Damage + amount }
                                    next

                            events.Add
                                { ReferenceEvents.create "DamagePlaced" with
                                    Actor = afterTarget.Owner
                                    SourceCard = afterTarget.Id
                                    TargetCards = [| attacker.Id |]
                                    DamageKind = "PlacedCounter"
                                    Amount = amount }

                            events.Add
                                { ReferenceEvents.create "TriggerResolved" with
                                    Actor = afterTarget.Owner
                                    SourceCard = afterTarget.Id
                                    TargetCards = [| attacker.Id |]
                                    Effect = trick.MechanicalId }
                        | _ -> ()

        next, events.ToArray()

    let private applyAttack authority mutation (state: CanonicalState) (selected: CanonicalAction) =
        match boundary authority state selected with
        | Some code -> rejection state code [||]
        | None when state.Phase <> "Playing" -> rejection state "WrongPhase" [||]
        | None when state.ActivePlayer <> selected.Actor -> rejection state "NotActorsTurn" [||]
        | None ->
            match
                ReferenceDeterministicPrograms.validateOwnedChoices
                    authority
                    selected.Actor
                    selected.Choices
                    selected.Requirements
            with
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
                        |> Array.tryFind (fun attack -> attack.MechanicalId = attackId)
                    with
                    | None -> rejection state "EffectNotFound" [||]
                    | Some attack when
                        attacker.Owner <> selected.Actor
                        || not (ReferenceDeterministicPrograms.inPlay attacker)
                        || not (
                            ReferenceCommonFoundation.canPayAttack
                                authority
                                refreshed
                                attacker
                                attack
                        )
                        ->
                        rejection state "EffectUnavailable" [||]
                    | Some attack ->
                        let (executed,
                             effectEvents,
                             attackTargets,
                             forced,
                             deferred,
                             _,
                             executionRejection,
                             extraBarChits) =
                            ReferenceDeterministicPrograms.executeAttackProgram
                                authority
                                NoProgramMutation
                                selected
                                selected.Actor
                                attacker
                                attack
                                [||]
                                refreshed

                        match executionRejection with
                        | Some(code, requirements) -> rejection state code requirements
                        | None when deferred.Length <> 0 ->
                            invalidOp
                                $"The specialised attack {attack.MechanicalId} unexpectedly deferred a choice."
                        | None ->
                            let semantic = ResizeArray<CanonicalEvent>()
                            semantic.AddRange refreshEvents

                            semantic.Add
                                { ReferenceEvents.create "AttackDeclared" with
                                    Actor = selected.Actor
                                    SourceCard = attacker.Id
                                    Effect = attack.MechanicalId }

                            semantic.AddRange effectEvents

                            let reacted, reactionEvents =
                                resolveAfterDamageTriggers
                                    authority
                                    mutation
                                    attacker
                                    refreshed
                                    attackTargets
                                    executed

                            semantic.AddRange reactionEvents

                            let identified =
                                reacted.Cards
                                |> Array.filter ReferenceDeterministicPrograms.inPlay
                                |> Array.filter (fun card ->
                                    Array.contains card.Id forced
                                    || card.Damage
                                       >= ReferenceCommonFoundation.stayingPower authority card)
                                |> Array.sortBy (fun card -> card.Owner, card.Id)
                                |> Array.map _.Id

                            let knockedOut, knockoutEvents, completed =
                                resolveIdentifiedKnockouts
                                    authority
                                    mutation
                                    selected
                                    attacker.Id
                                    attackTargets
                                    extraBarChits
                                    Set.empty
                                    identified
                                    reacted

                            semantic.AddRange knockoutEvents

                            let mutable next = knockedOut

                            if
                                completed
                                && next.Phase <> "Complete"
                                && next.PendingBarChits.Length = 0
                            then
                                let random = ReferenceRandom(next.Random)

                                let finished, finishEvents =
                                    ReferenceCommonFoundation.finishOrPendCommonRound
                                        authority
                                        NoReferenceMutation
                                        random
                                        next

                                next <-
                                    { finished with
                                        Random = random.Snapshot }

                                semantic.AddRange finishEvents

                            commit state selected next (semantic.ToArray())

    let private resolveKnockout
        authority
        mutation
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        match boundary authority state selected with
        | Some code -> rejection state code [||]
        | None when state.Phase <> "AwaitingTriggerChoice" || not state.PendingKnockout.Present ->
            rejection state "WrongPhase" [||]
        | None when state.PendingKnockout.Chooser <> selected.Actor ->
            rejection state "WrongChooser" [||]
        | None ->
            let pending = state.PendingKnockout
            let vim = selected.Payload.Substring(4)

            if not (String.IsNullOrEmpty vim) && not (Array.contains vim pending.EligibleVim) then
                rejection state "InvalidChoice" [||]
            else
                let events = ResizeArray<CanonicalEvent>()
                let mutable next = state

                if not (String.IsNullOrEmpty vim) then
                    let knockedOut = ReferenceState.card next pending.KnockedOutCard
                    let source = ReferenceState.card next pending.TriggerSource
                    let movedToMitt, firstMove = ReferenceEngine.moveCard vim "Mitt" "" next

                    next <-
                        ReferenceState.updateCard
                            { ReferenceState.card movedToMitt knockedOut.Id with
                                Attachments =
                                    knockedOut.Attachments
                                    |> Array.filter (fun value -> value <> vim) }
                            movedToMitt

                    let attached, secondMove =
                        ReferenceEngine.moveCard vim "Attached" source.Id next

                    next <-
                        ReferenceState.updateCard
                            { ReferenceState.card attached source.Id with
                                Attachments = Array.append source.Attachments [| vim |] }
                            attached

                    events.Add firstMove
                    events.Add secondMove

                events.Add
                    { ReferenceEvents.create "TriggerResolved" with
                        Actor = selected.Actor
                        SourceCard = pending.TriggerSource
                        TargetCards = if String.IsNullOrEmpty vim then [||] else [| vim |]
                        Effect = pending.TriggerEffect }

                let remainingVim =
                    pending.EligibleVim
                    |> Array.filter (fun id ->
                        (ReferenceState.card next id).AttachedTo = pending.KnockedOutCard)

                if pending.TriggerSources.Length <> 0 && remainingVim.Length <> 0 then
                    let nextSource = pending.TriggerSources[0]
                    let source = ReferenceState.card next nextSource

                    let trick =
                        triggerOf
                            ReferenceTrigger.OnOwnBlokeSentHomeByOtherAttackDamage
                            authority
                            source
                        |> Option.get

                    next <-
                        { next with
                            PendingKnockout =
                                { pending with
                                    TriggerSources = pending.TriggerSources[1..]
                                    TriggerSource = nextSource
                                    TriggerEffect = trick.MechanicalId
                                    EligibleVim = remainingVim } }
                else
                    next <-
                        { next with
                            Phase = "Playing"
                            PendingKnockout = Canonical.emptyPendingKnockout }

                    let resolved, knockoutEvents, completed =
                        resolveIdentifiedKnockouts
                            authority
                            mutation
                            selected
                            pending.AttackingCard
                            pending.AttackDamageTargets
                            pending.ExtraBarChits
                            (Set.singleton pending.KnockedOutCard)
                            (Array.append [| pending.KnockedOutCard |] pending.RemainingKnockouts)
                            next

                    next <- resolved
                    events.AddRange knockoutEvents

                    if
                        completed
                        && pending.FinishRoundAfterResolution
                        && next.Phase <> "Complete"
                        && next.PendingBarChits.Length = 0
                    then
                        let random = ReferenceRandom(next.Random)

                        let finished, finishEvents =
                            ReferenceCommonFoundation.finishOrPendCommonRound
                                authority
                                NoReferenceMutation
                                random
                                next

                        next <-
                            { finished with
                                Random = random.Snapshot }

                        events.AddRange finishEvents

                commit state selected next (events.ToArray())

    let private resolveBarChit
        authority
        mutation
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        match boundary authority state selected with
        | Some code -> rejection state code [||]
        | None when state.Phase <> "AwaitingTriggerChoice" || state.PendingBarChits.Length = 0 ->
            rejection state "WrongPhase" [||]
        | None ->
            let pending = state.PendingBarChits[0]

            if pending.Player <> selected.Actor then
                rejection state "WrongChooser" [||]
            else
                let putOntoBooth = Boolean.Parse(selected.Payload.Substring(6))
                let source = ReferenceState.card state pending.Card

                if
                    putOntoBooth
                    && (source.Zone <> "Mitt"
                        || (ReferenceState.cardsIn state selected.Actor "Booth").Length
                           >= authority.BaseRules.Opening.BoothLimit)
                then
                    rejection state "InvalidChoice" [||]
                else
                    let trick =
                        triggerOf ReferenceTrigger.OnBarChitTaken authority source
                        |> Option.defaultWith (fun () ->
                            invalidOp $"Pending Bar Chit source {source.Id} lost {pending.Effect}.")

                    let optionalId = $"{trick.MechanicalId}:root/0:optional"

                    let choices =
                        [| { Kind = "Optional"
                             Id = optionalId
                             Values = [| putOntoBooth.ToString().ToLowerInvariant() |] } |]

                    let withoutPending =
                        { state with
                            PendingBarChits = state.PendingBarChits[1..] }

                    let (executed,
                         triggerEvents,
                         _,
                         _,
                         deferred,
                         _,
                         executionRejection,
                         extraBarChits) =
                        executeTrigger
                            authority
                            selected
                            selected.Actor
                            source
                            trick
                            choices
                            withoutPending

                    if deferred.Length <> 0 || executionRejection.IsSome then
                        invalidOp $"The Bar Chit trigger {trick.MechanicalId} did not execute."

                    let events = ResizeArray<CanonicalEvent>(triggerEvents)
                    let mutable next = executed

                    if extraBarChits > 0 then
                        let awarded, awardEvents, _ =
                            takeBarChits
                                authority
                                mutation
                                selected.Actor
                                extraBarChits
                                source.Id
                                true
                                next

                        next <- awarded
                        events.AddRange awardEvents

                    events.Add
                        { ReferenceEvents.create "TriggerResolved" with
                            Actor = selected.Actor
                            SourceCard = source.Id
                            Effect = trick.MechanicalId
                            Amount = if putOntoBooth then 1 else 0 }

                    let random = ReferenceRandom(next.Random)

                    let resolved, winEvents =
                        ReferenceCommonFoundation.resolveWins
                            authority
                            NoReferenceMutation
                            random
                            ""
                            next

                    next <-
                        { resolved with
                            Random = random.Snapshot }

                    events.AddRange winEvents

                    if next.Phase <> "Complete" then
                        if next.PendingBarChits.Length <> 0 then
                            next <-
                                { next with
                                    Phase = "AwaitingTriggerChoice" }
                        else
                            next <- { next with Phase = "Playing" }

                            if pending.FinishRoundAfterResolution then
                                let finishRandom = ReferenceRandom(next.Random)

                                let finished, finishEvents =
                                    ReferenceCommonFoundation.finishOrPendCommonRound
                                        authority
                                        NoReferenceMutation
                                        finishRandom
                                        next

                                next <-
                                    { finished with
                                        Random = finishRandom.Snapshot }

                                events.AddRange finishEvents

                    commit state selected next (events.ToArray())

    let private applyPromotionTriggers
        (authority: ReferenceAuthority)
        (selected: CanonicalAction)
        (transition: CanonicalTransition)
        =
        if transition.Rejection.Length <> 0 then
            transition
        else
            let promotionId = (selected.Payload.Split(';')[0]).Substring(10)
            let mutable next = transition.State
            let semanticEvents = ResizeArray<CanonicalEvent>()

            for trick in
                authority.Cards[(ReferenceState.card next promotionId).MechanicalId].PartyTricks
                |> Array.filter (fun trick -> trick.Trigger = ReferenceTrigger.OnPromotionFromMitt) do
                let source = ReferenceState.card next promotionId

                let (executed, events, _, forcedSendHome, deferred, _, rejection, extraBarChits) =
                    ReferenceDeterministicPrograms.executeStandaloneProgram
                        authority
                        NoProgramMutation
                        selected
                        selected.Actor
                        source
                        trick.MechanicalId
                        trick.Program
                        false
                        [||]
                        next

                if deferred.Length <> 0 || rejection.IsSome then
                    invalidOp
                        $"The inspected promotion trigger {trick.MechanicalId} did not execute with its selected choices."

                if forcedSendHome.Length <> 0 || extraBarChits <> 0 then
                    invalidOp
                        $"The promotion trigger {trick.MechanicalId} crossed the specialised trigger boundary."

                next <- executed
                semanticEvents.AddRange events

            if semanticEvents.Count = 0 then
                transition
            else
                let revision = transition.State.Transport.Revision

                let beforeCommit =
                    Array.append
                        transition.Events[0 .. transition.Events.Length - 2]
                        (semanticEvents.ToArray())

                let committedEvent = transition.Events[transition.Events.Length - 1]

                let events =
                    Array.append beforeCommit [| committedEvent |]
                    |> Array.mapi (fun index event ->
                        { event with
                            RelativeSequence = index + 1
                            Revision = revision })

                { transition with
                    State =
                        { next with
                            Transport =
                                { next.Transport with
                                    LastEventSequence =
                                        transition.State.Transport.LastEventSequence
                                        + int64 semanticEvents.Count } }
                    Events = events }

    let apply
        (authority: ReferenceAuthority)
        mutation
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        match selected.Kind with
        | "Attack" -> applyAttack authority mutation state selected
        | "ResolveKnockoutTrigger" -> resolveKnockout authority mutation state selected
        | "ResolveBarChitTrigger" -> resolveBarChit authority mutation state selected
        | "Promote" ->
            let promoted =
                ReferenceLifecyclePrograms.apply authority NoLifecycleMutation state selected

            if mutation = SkipSpecializedPromotionTrigger then
                promoted
            else
                applyPromotionTriggers authority selected promoted
        | "EndRound" ->
            ReferenceCommonFoundation.applyCommon authority NoReferenceMutation state selected
        | _ -> rejection state "WrongPhase" [||]
