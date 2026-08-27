namespace Blokemon.ReferenceModel

open System

[<RequireQualifiedAccess>]
module ReferenceCommonFoundation =

    let private actionOrder = ReferenceEngine.actionOrder
    let private action = ReferenceEngine.action
    let private shuffle = ReferenceEngine.shuffle
    let private moveCard = ReferenceEngine.moveCard
    let private draw = ReferenceEngine.draw

    let private isInPlay (card: CanonicalCard) =
        card.Zone = "Oche" || card.Zone = "Booth"

    type private CommonAttackProgram =
        { PrintedDamage: int
          ExtraKnockoutBarChits: int }

    let private hasNoInstructionMetadata (instruction: ReferenceInstruction) =
        instruction.Sources.Length = 0
        && instruction.Destination.IsNone
        && instruction.SourceTopCount.IsNone
        && instruction.CardFilter.IsNone
        && instruction.MechanicalTypes.Length = 0
        && instruction.RoughStates.Length = 0
        && instruction.RelatedIds.Length = 0

    let private isPrintedDamageInstruction
        (attack: ReferenceAttack)
        (instruction: ReferenceInstruction)
        =
        instruction.Opcode = ReferenceOpcode.DealPrintedDamage
        && instruction.Amount = attack.PrintedDamage
        && instruction.ValueSource = ReferenceValueSource.PrintedDamage
        && instruction.Targets = [| ReferenceLocation.OtherOche |]
        && instruction.Selection = ReferenceSelection.All
        && instruction.TargetCount = 1
        && instruction.Predicates.Length = 0
        && instruction.Then.Length = 0
        && instruction.Otherwise.Length = 0
        && hasNoInstructionMetadata instruction

    let private tryExtraKnockoutBarChits (instruction: ReferenceInstruction) =
        let isKnockoutPredicate (predicate: ReferencePredicate) =
            predicate.Condition = ReferenceCondition.OtherSentHomeByThisAttackDamage
            && predicate.MechanicalType.IsNone
            && predicate.RelatedId.IsNone
            && predicate.RoughState.IsNone
            && predicate.Value = 0

        let isExtraBarChitInstruction (nested: ReferenceInstruction) =
            nested.Opcode = ReferenceOpcode.TakeExtraBarChit
            && nested.Amount > 0
            && nested.ValueSource = ReferenceValueSource.Fixed
            && nested.Targets = [| ReferenceLocation.BarChits |]
            && nested.Selection = ReferenceSelection.All
            && nested.TargetCount = 1
            && nested.Predicates.Length = 0
            && nested.Then.Length = 0
            && nested.Otherwise.Length = 0
            && hasNoInstructionMetadata nested

        match instruction.Predicates, instruction.Then with
        | [| predicate |], [| nested |] when
            instruction.Opcode = ReferenceOpcode.Conditional
            && instruction.Amount = 0
            && instruction.ValueSource = ReferenceValueSource.Fixed
            && instruction.Targets.Length = 0
            && instruction.Selection = ReferenceSelection.All
            && instruction.TargetCount = 1
            && instruction.Otherwise.Length = 0
            && hasNoInstructionMetadata instruction
            && isKnockoutPredicate predicate
            && isExtraBarChitInstruction nested
            ->
            ValueSome nested.Amount
        | _ -> ValueNone

    let private tryCommonAttackProgram (attack: ReferenceAttack) =
        if attack.VariablePrintedDamage then
            ValueNone
        else
            match attack.Program with
            | [| damage |] when isPrintedDamageInstruction attack damage ->
                ValueSome
                    { PrintedDamage = attack.PrintedDamage
                      ExtraKnockoutBarChits = 0 }
            | [| damage; conditional |] when isPrintedDamageInstruction attack damage ->
                match tryExtraKnockoutBarChits conditional with
                | ValueSome count ->
                    ValueSome
                        { PrintedDamage = attack.PrintedDamage
                          ExtraKnockoutBarChits = count }
                | ValueNone -> ValueNone
            | _ -> ValueNone

    let private isRequiredCompanionLegalAttack (owner: ReferenceCard) (attack: ReferenceAttack) =
        let hasExtraKnockoutBarChitSibling =
            owner.Attacks
            |> Array.exists (fun sibling ->
                match tryCommonAttackProgram sibling with
                | ValueSome program -> program.ExtraKnockoutBarChits > 0
                | ValueNone -> false)

        match attack.Program with
        | [| instruction |] ->
            not attack.VariablePrintedDamage
            && instruction.Opcode = ReferenceOpcode.SwapOche
            && instruction.Amount = 1
            && instruction.ValueSource = ReferenceValueSource.Fixed
            && instruction.Targets = [| ReferenceLocation.OtherBoothChosen |]
            && instruction.Selection = ReferenceSelection.Chosen
            && instruction.TargetCount = 1
            && instruction.Predicates.Length = 0
            && instruction.Then.Length = 0
            && instruction.Otherwise.Length = 0
            && hasNoInstructionMetadata instruction
            && hasExtraKnockoutBarChitSibling
        | _ -> false

    let internal refreshContinuousEffects (authority: ReferenceAuthority) (state: CanonicalState) =
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        let register
            (source: CanonicalCard)
            (effect: string)
            (kind: string)
            (instruction: ReferenceInstruction)
            (target: CanonicalCard)
            =
            let registered =
                { SourceEffect = effect
                  SourceCard = source.Id
                  Owner = source.Owner
                  TargetCard = target.Id
                  Kind = kind
                  Amount = instruction.Amount
                  MechanicalTypes = instruction.MechanicalTypes |> Array.map string
                  RoughStates = instruction.RoughStates |> Array.map string
                  RelatedCards = instruction.RelatedIds
                  Conditions = instruction.Predicates |> Array.map (_.Condition >> string)
                  Duration =
                    if kind = "ContinuousPartyTrick" || kind = "ModifySoftSpot" then
                        "WhileSourceInPlay"
                    else
                        "UntilEndOfOpponentsNextRound"
                  AppliesFromRound = next.RoundNumber
                  ExpiresAfterRound = next.RoundNumber + 2 }

            next <-
                { next with
                    Effects = Array.append next.Effects [| registered |] }

            events.Add
                { ReferenceEvents.create "EffectRegistered" with
                    Actor = source.Owner
                    SourceCard = source.Id
                    TargetCards = [| target.Id |]
                    Effect = effect
                    Amount = instruction.Amount }

        let predicate (source: CanonicalCard) (value: ReferencePredicate) =
            match value.Condition with
            | ReferenceCondition.AttachedVimCountsAreEqual ->
                let own = source.Attachments.Length

                let other =
                    ReferenceState.cardsIn
                        next
                        (ReferenceState.otherPlayer next source.Owner)
                        "Oche"
                    |> Array.tryHead
                    |> Option.map _.Attachments.Length
                    |> Option.defaultValue 0

                own = other
            | ReferenceCondition.NamedBlokeInPlay ->
                value.RelatedId
                |> ValueOption.exists (fun id ->
                    next.Cards
                    |> Array.exists (fun card -> card.MechanicalId = id && isInPlay card))
            | ReferenceCondition.OpenedSecond -> next.OpeningPlayer <> source.Owner
            | ReferenceCondition.OwnersFirstRound ->
                (ReferenceState.player next source.Owner).RoundsStarted = 1
            | ReferenceCondition.SelfHasVim -> source.Attachments.Length <> 0
            | ReferenceCondition.SelfIsAtOche -> source.Zone = "Oche"
            | ReferenceCondition.SelfIsInBooth -> source.Zone = "Booth"
            | unsupported ->
                invalidOp
                    $"The reference continuous support prerequisite cannot evaluate {unsupported}."

        let targets (source: CanonicalCard) (instruction: ReferenceInstruction) =
            let resolved =
                if instruction.Targets.Length = 0 then
                    [| source |]
                else
                    instruction.Targets
                    |> Array.collect (fun location ->
                        match location with
                        | ReferenceLocation.Self -> [| source |]
                        | ReferenceLocation.OtherOche ->
                            ReferenceState.cardsIn
                                next
                                (ReferenceState.otherPlayer next source.Owner)
                                "Oche"
                        | ReferenceLocation.OwnBlokesAll ->
                            next.Cards
                            |> Array.filter (fun card ->
                                card.Owner = source.Owner && card.Kind = "Bloke" && isInPlay card)
                        | unsupported ->
                            invalidOp
                                $"The reference continuous support prerequisite cannot target {unsupported}.")
                |> Array.filter isInPlay
                |> Array.filter (fun target ->
                    instruction.RelatedIds.Length = 0
                    || Array.contains target.MechanicalId instruction.RelatedIds)

            if resolved.Length <> 0 then
                resolved
            elif not (String.IsNullOrEmpty source.AttachedTo) then
                [| ReferenceState.card next source.AttachedTo |]
            elif isInPlay source then
                [| source |]
            else
                [||]

        let rec execute source effect (instructions: ReferenceInstruction array) =
            for instruction in instructions do
                match instruction.Opcode with
                | ReferenceOpcode.Conditional ->
                    if instruction.Predicates |> Array.forall (predicate source) then
                        execute source effect instruction.Then
                    else
                        execute source effect instruction.Otherwise
                | ReferenceOpcode.ContinuousPartyTrick ->
                    for target in targets source instruction do
                        register source effect "ContinuousPartyTrick" instruction target
                | ReferenceOpcode.PreventDamage
                | ReferenceOpcode.PreventEffects
                | ReferenceOpcode.ReduceDamage
                | ReferenceOpcode.ScaleDamage
                | ReferenceOpcode.ModifyAttackCost
                | ReferenceOpcode.ModifyTaxiFare
                | ReferenceOpcode.ModifySoftSpot
                | ReferenceOpcode.RestrictTaxi
                | ReferenceOpcode.RestrictLocal
                | ReferenceOpcode.RestrictEmptiesRecovery ->
                    for target in targets source instruction do
                        let kind =
                            if instruction.Opcode = ReferenceOpcode.ScaleDamage then
                                "ScaleNextAttackDamage"
                            else
                                string instruction.Opcode

                        register source effect kind instruction target
                | ReferenceOpcode.AttachVim -> ()
                | unsupported ->
                    invalidOp
                        $"The reference continuous support prerequisite does not implement {unsupported}."

        for source in state.Cards |> Array.filter isInPlay |> Array.sortBy _.Id do
            for trick in
                authority.Cards[source.MechanicalId].PartyTricks
                |> Array.filter (fun trick -> trick.Trigger = ReferenceTrigger.Continuous) do
                next <-
                    { next with
                        Effects =
                            next.Effects
                            |> Array.filter (fun existing ->
                                existing.SourceEffect <> trick.MechanicalId
                                || existing.SourceCard <> source.Id) }

                execute source trick.MechanicalId trick.Program

        let rec containsOptional (instructions: ReferenceInstruction array) =
            instructions
            |> Array.exists (fun instruction ->
                (instruction.Predicates
                 |> Array.exists (fun predicate ->
                     predicate.Condition = ReferenceCondition.Optional))
                || containsOptional instruction.Then
                || containsOptional instruction.Otherwise)

        for source in
            state.Cards
            |> Array.filter (fun card -> card.Kind = "Kit" && card.Zone = "Attached")
            |> Array.sortBy _.Id do
            for rule in
                authority.Cards[source.MechanicalId].HouseRules
                |> Array.filter (fun rule -> not (containsOptional rule.Program)) do
                next <-
                    { next with
                        Effects =
                            next.Effects
                            |> Array.filter (fun existing ->
                                existing.SourceEffect <> rule.MechanicalId
                                || existing.SourceCard <> source.Id) }

                execute source rule.MechanicalId rule.Program

        next, events.ToArray()

    let internal attachedVim
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (card: CanonicalCard)
        =
        card.Attachments
        |> Array.map (ReferenceState.card state)
        |> Array.filter (fun attachment -> attachment.Kind = "Vim")
        |> Array.map (fun attachment ->
            attachment,
            authority.Cards[attachment.MechanicalId].VimType
            |> ValueOption.defaultWith (fun () ->
                invalidOp $"The reference Vim {attachment.MechanicalId} has no mechanical type."))

    let internal canPayAttack
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (attacker: CanonicalCard)
        (attack: ReferenceAttack)
        =
        let modifiers =
            state.Effects
            |> Array.filter (fun effect ->
                effect.TargetCard = attacker.Id && effect.Kind = "ModifyAttackCost")

        if modifiers |> Array.exists (fun effect -> effect.Amount < 0) then
            true
        else
            let available = ResizeArray(attachedVim authority state attacker |> Array.map snd)
            let costs = ResizeArray(attack.VimCost)

            for modifier in modifiers |> Array.filter (fun effect -> effect.Amount > 0) do
                if modifier.MechanicalTypes.Length = 0 then
                    costs.AddRange(Seq.replicate modifier.Amount ReferenceMechanicalType.Colorless)
                else
                    costs.AddRange(
                        modifier.MechanicalTypes |> Seq.map Enum.Parse<ReferenceMechanicalType>
                    )

            let mutable payable = true

            for typedCost in
                costs |> Seq.filter (fun value -> value <> ReferenceMechanicalType.Colorless) do
                if payable then
                    let index = available.FindIndex(fun value -> value = typedCost)

                    if index < 0 then
                        payable <- false
                    else
                        available.RemoveAt index

            payable
            && available.Count
               >= (costs
                   |> Seq.filter (fun value -> value = ReferenceMechanicalType.Colorless)
                   |> Seq.length)

    let private promotionEligible
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        (promotion: CanonicalCard)
        (target: CanonicalCard)
        =
        let player = ReferenceState.player state actor
        let rules = authority.BaseRules.Promotion

        promotion.Owner = actor
        && target.Owner = actor
        && promotion.Kind = "Bloke"
        && promotion.Zone = "Mitt"
        && isInPlay target
        && (not rules.NotOnEitherFirstRound || player.RoundsStarted > 1)
        && (not rules.NotFirstRoundInPlay
            || target.EnteredAtOwnerRound <> player.RoundsStarted)
        && (not rules.NotTwiceInRound || target.LastPromotedRound <> state.RoundNumber)
        && (not rules.ExactMechanicalEdgeRequired
            || authority.Cards[promotion.MechanicalId].PromotesFromId = ValueSome
                target.MechanicalId)

    let internal roughPrevents
        (authority: ReferenceAuthority)
        (predicate: ReferenceRoughStateRule -> bool)
        (card: CanonicalCard)
        =
        card.RoughStates
        |> Array.exists (fun entry ->
            let parsed = Enum.Parse<ReferenceRoughState>(entry.State)
            authority.BaseRules.RoughStates[parsed] |> predicate)

    let private attachVimActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        if
            state.RoundUsage.VimAttachments
            >= authority.BaseRules.Vim.NormalAttachmentPerRound
        then
            [||]
        else
            let targets =
                state.Cards |> Array.filter (fun card -> card.Owner = actor && isInPlay card)

            ReferenceState.cardsIn state actor "Mitt"
            |> Array.filter (fun card -> card.Kind = "Vim")
            |> Array.collect (fun vim ->
                targets
                |> Array.map (fun target ->
                    let key = $"attach:{vim.Id}:{target.Id}"

                    action
                        state
                        "AttachVim"
                        actor
                        key
                        key
                        $"vim={vim.Id};target={target.Id}"
                        [||]
                        [||]))

    let private playBlokeActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let boothCount = ReferenceState.cardsIn state actor "Booth" |> Array.length

        if boothCount >= authority.BaseRules.Opening.BoothLimit then
            [||]
        else
            ReferenceState.cardsIn state actor "Mitt"
            |> Array.filter (fun card ->
                card.Kind = "Bloke"
                && authority.Cards[card.MechanicalId].Rank = ValueSome ReferenceRank.Regular)
            |> Array.map (fun card ->
                let key = $"play:{card.Id}"
                action state "PlayBloke" actor key key $"bloke={card.Id}" [||] [||])

    let private promoteActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let targets =
            state.Cards |> Array.filter (fun card -> card.Owner = actor && isInPlay card)

        ReferenceState.cardsIn state actor "Mitt"
        |> Array.filter (fun card -> card.Kind = "Bloke")
        |> Array.collect (fun promotion ->
            targets
            |> Array.filter (promotionEligible authority state actor promotion)
            |> Array.map (fun target ->
                let key = $"promote:{promotion.Id}:{target.Id}"

                action
                    state
                    "Promote"
                    actor
                    key
                    key
                    $"promotion={promotion.Id};promoted={target.Id}"
                    [||]
                    [||]))

    let private attackActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        state.Cards
        |> Array.filter (fun card -> card.Owner = actor && isInPlay card)
        |> Array.collect (fun attacker ->
            let owner = authority.Cards[attacker.MechanicalId]

            owner.Attacks
            |> Array.filter (fun attack ->
                (tryCommonAttackProgram attack).IsSome
                || isRequiredCompanionLegalAttack owner attack)
            |> Array.filter (fun attack ->
                (attacker.Zone = "Oche" || (attacker.Zone = "Booth" && attack.CanBeUsedFromBench))
                && (authority.BaseRules.Opening.OpeningParticipantMayAttack
                    || actor <> state.OpeningPlayer
                    || (ReferenceState.player state actor).RoundsStarted <> 1)
                && not (roughPrevents authority _.PreventsAttack attacker)
                && canPayAttack authority state attacker attack)
            |> Array.map (fun attack ->
                let key = $"attack:{attacker.Id}:{attack.MechanicalId}"

                action
                    state
                    "Attack"
                    actor
                    key
                    key
                    $"attacker={attacker.Id};effect={attack.MechanicalId}"
                    [||]
                    [||]))

    let private taxiActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        match ReferenceState.cardsIn state actor "Oche" with
        | [||] -> [||]
        | ocheCards ->
            let outgoing = ocheCards[0]

            if
                state.RoundUsage.TaxisUsed >= authority.BaseRules.Taxi.PerRound
                || roughPrevents authority _.PreventsTaxi outgoing
                || (outgoing.Kind = "Kit"
                    && authority.BaseRules.Fossil.KitIds.Contains outgoing.MechanicalId
                    && authority.BaseRules.Fossil.CannotTaxi)
            then
                [||]
            else
                let fare =
                    if outgoing.Kind = "Bloke" then
                        authority.Cards[outgoing.MechanicalId].TaxiFare
                    else
                        Int32.MaxValue

                let vim =
                    attachedVim authority state outgoing
                    |> Array.truncate fare
                    |> Array.map (fst >> _.Id)

                let affordability =
                    if vim.Length >= fare then
                        "Payable"
                    else
                        $"ShortOfTaxiFare:{fare}"

                ReferenceState.cardsIn state actor "Booth"
                |> Array.map (fun booth ->
                    let key = $"taxi:{booth.Id}"
                    let vimPayload = String.concat "," vim

                    { action
                          state
                          "Taxi"
                          actor
                          key
                          key
                          $"booth={booth.Id};vim={vimPayload}"
                          [||]
                          [||] with
                        Affordability = affordability })

    let private chuckFossilActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        state.Cards
        |> Array.filter (fun card ->
            card.Owner = actor
            && card.Kind = "Kit"
            && isInPlay card
            && authority.BaseRules.Fossil.KitIds.Contains card.MechanicalId
            && authority.BaseRules.Fossil.MayChuckFromPlayDuringOwnersRound)
        |> Array.map (fun card ->
            let key = $"chuck:{card.Id}"
            action state "ChuckFossil" actor key key $"fossil={card.Id}" [||] [||])

    let private playingCommonActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        if state.ActivePlayer <> actor then
            [||]
        else
            Array.concat
                [ attachVimActions authority state actor
                  playBlokeActions authority state actor
                  promoteActions authority state actor
                  attackActions authority state actor
                  taxiActions authority state actor
                  chuckFossilActions authority state actor
                  [| action state "EndRound" actor "end" "end" "end" [||] [||] |] ]

    let private replacementActions (state: CanonicalState) (actor: string) =
        if state.ReplacementPlayer <> actor then
            [||]
        else
            ReferenceState.cardsIn state actor "Booth"
            |> Array.map (fun card ->
                let key = $"replacement:{card.Id}"

                action state "ChooseReplacement" actor key key $"replacement={card.Id}" [||] [||])

    let legalCommonActions
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (actor: string)
        =
        if
            state.Phase = "Complete"
            || not (state.Players |> Array.exists (fun player -> player.Id = actor))
        then
            [||]
        else
            let legalState =
                if state.Phase = "Playing" then
                    refreshContinuousEffects authority state |> fst
                else
                    state

            let phaseActions =
                match legalState.Phase with
                | "Playing" -> playingCommonActions authority legalState actor
                | "AwaitingReplacement" -> replacementActions state actor
                | "AwaitingEffectChoice"
                | "AwaitingTriggerChoice" when mutation = AllowBaseActionWhilePending ->
                    if legalState.ActivePlayer = actor then
                        [| action legalState "EndRound" actor "end" "end" "end" [||] [||] |]
                    else
                        [||]
                | _ -> [||]

            let resignation =
                match mutation with
                | OmitResignFromLegalActions -> [||]
                | _ ->
                    [| action
                           legalState
                           "Resign"
                           actor
                           $"resign:{actor}"
                           "resign"
                           "resign"
                           [||]
                           [||] |]

            Array.append phaseActions resignation
            |> Array.sortWith (fun left right ->
                let byKind = compare actionOrder[left.Kind] actionOrder[right.Kind]

                if byKind <> 0 then
                    byKind
                else
                    String.CompareOrdinal(left.StableKey, right.StableKey))

    let suspendForEffect (pending: CanonicalPendingEffect) (state: CanonicalState) =
        { state with
            Phase = "AwaitingEffectChoice"
            PendingEffect = pending }

    let suspendForKnockout (pending: CanonicalPendingKnockout) (state: CanonicalState) =
        { state with
            Phase = "AwaitingTriggerChoice"
            PendingKnockout = pending }

    let queueBarChit (pending: CanonicalPendingBarChit) (state: CanonicalState) =
        { state with
            Phase = "AwaitingTriggerChoice"
            PendingBarChits = Array.append state.PendingBarChits [| pending |] }

    let private removeEffectsFor
        (cardId: string)
        (preserveDelayedTarget: bool)
        (state: CanonicalState)
        =
        { state with
            Effects =
                state.Effects
                |> Array.filter (fun effect ->
                    not (
                        (effect.SourceCard = cardId
                         && effect.Kind <> "EndRoundEffect"
                         && effect.Kind <> "ForceBeerMatBlank"
                         && effect.Duration <> "WhileTargetInPlay")
                        || (effect.TargetCard = cardId
                            && (not preserveDelayedTarget || effect.Kind <> "EndRoundEffect"))
                    )) }

    let private chuckCardPile (id: string) (state: CanonicalState) =
        let card = ReferenceState.card state id

        let chucked =
            Array.concat [ card.Attachments; card.UnderlyingCards; [| id |] ]
            |> Array.distinct

        chucked
        |> Array.fold
            (fun next cardId ->
                let current = ReferenceState.card next cardId

                next
                |> ReferenceState.updateCard
                    { current with
                        Zone = "EmptiesTray"
                        IsFaceDown = false
                        StackPosition = -1
                        AttachedTo = ""
                        Attachments = [||]
                        UnderlyingCards = [||]
                        RoughStates = [||] }
                |> removeEffectsFor cardId false)
            state

    let private assignReplacement
        (mutation: ReferenceMutation)
        (player: string)
        (state: CanonicalState)
        =
        if
            mutation <> SkipReplacementAssignment
            && (ReferenceState.cardsIn state player "Oche" |> Array.isEmpty)
            && not (ReferenceState.cardsIn state player "Booth" |> Array.isEmpty)
        then
            { state with
                Phase = "AwaitingReplacement"
                ReplacementPlayer =
                    if String.IsNullOrEmpty state.ReplacementPlayer then
                        player
                    else
                        state.ReplacementPlayer }
        else
            state

    let private takeBarChits
        (mutation: ReferenceMutation)
        (player: string)
        (count: int)
        (source: string)
        (state: CanonicalState)
        =
        if mutation = SkipBarChitAward then
            state, [||], [||]
        else
            let ids =
                ReferenceState.cardsIn state player "BarChit"
                |> Array.truncate count
                |> Array.map _.Id

            let mutable next = state
            let events = ResizeArray<CanonicalEvent>()

            for id in ids do
                let moved, movedEvent = moveCard id "Mitt" "" next
                next <- moved
                events.Add movedEvent

            let current = ReferenceState.player next player

            next <-
                ReferenceState.updatePlayer
                    { current with
                        BarChitsRemaining = current.BarChitsRemaining - ids.Length }
                    next

            events.Add
                { ReferenceEvents.create "BarChitsTaken" with
                    Actor = player
                    SourceCard = source
                    TargetCards = ids
                    Amount = ids.Length }

            next, events.ToArray(), ids

    let private resetBarChits
        (authority: ReferenceAuthority)
        (random: ReferenceRandom)
        (player: string)
        (count: int)
        (state: CanonicalState)
        =
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        let mutable nextPosition =
            ReferenceState.cardsIn next player "Stack"
            |> Array.fold (fun beneath card -> max beneath (card.StackPosition + 1)) 0

        for card in ReferenceState.cardsIn next player "BarChit" do
            let moved, movedEvent = moveCard card.Id "Stack" "" next

            next <-
                ReferenceState.updateCard
                    { ReferenceState.card moved card.Id with
                        StackPosition = nextPosition }
                    moved

            nextPosition <- nextPosition + 1
            events.Add movedEvent

        next <- shuffle player random next

        events.Add
            { ReferenceEvents.create "CardsShuffled" with
                Actor = player }

        let ids =
            ReferenceState.cardsIn next player "Stack"
            |> Array.truncate count
            |> Array.map _.Id

        for index in 0 .. ids.Length - 1 do
            let moved, movedEvent = moveCard ids[index] "BarChit" "" next

            next <-
                ReferenceState.updateCard
                    { ReferenceState.card moved ids[index] with
                        StackPosition = index }
                    moved

            events.Add movedEvent

        next <-
            ReferenceState.updatePlayer
                { ReferenceState.player next player with
                    BarChitsRemaining = ids.Length }
                next

        next, events.ToArray()

    let private resolveWins
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (failedRequiredDraw: string)
        (state: CanonicalState)
        =
        let supportedConditions =
            Set [ "TakeLastBarChit"; "LeaveOtherSideNoBloke"; "OtherSideFailsRequiredDraw" ]

        if Set.ofArray authority.BaseRules.Win.Conditions <> supportedConditions then
            invalidOp "The reference win-condition authority is unsupported."

        let methods =
            state.Players
            |> Array.map (fun player ->
                let mutable count = 0
                let other = ReferenceState.otherPlayer state player.Id

                for condition in authority.BaseRules.Win.Conditions do
                    match condition with
                    | "TakeLastBarChit" when player.BarChitsRemaining = 0 -> count <- count + 1
                    | "LeaveOtherSideNoBloke" when
                        not (
                            state.Cards
                            |> Array.exists (fun card -> card.Owner = other && isInPlay card)
                        )
                        ->
                        count <- count + 1
                    | "OtherSideFailsRequiredDraw" when failedRequiredDraw = other ->
                        count <- count + 1
                    | _ -> ()

                player.Id, count)
            |> Array.filter (snd >> ((<) 0))

        if methods.Length = 0 then
            state, [||]
        else
            let ordered = methods |> Array.sortByDescending snd
            let uniqueWinner = ordered.Length = 1 || snd ordered[0] <> snd ordered[1]

            if
                uniqueWinner
                && authority.BaseRules.Win.MoreMethodsWins = "Immediate"
                && mutation <> ForceSuddenDeathForWinner
            then
                let winner = fst ordered[0]

                { state with
                    Phase = "Complete"
                    ReplacementPlayer = ""
                    Terminal =
                        { state.Terminal with
                            IsComplete = true
                            Winner = winner } },
                [| { ReferenceEvents.create "MatchWon" with
                       Actor = winner } |]
            else
                if
                    authority.BaseRules.Win.OneMethodEach <> "SuddenDeath"
                    || not authority.BaseRules.Win.RepeatUntilWinner
                then
                    invalidOp "The reference sudden-death authority is unsupported."

                let mutable next =
                    { state with
                        Terminal =
                            { state.Terminal with
                                SuddenDeathCount = state.Terminal.SuddenDeathCount + 1 } }

                let events = ResizeArray<CanonicalEvent>()

                for player in next.Players |> Array.map _.Id do
                    let reset, resetEvents =
                        resetBarChits
                            authority
                            random
                            player
                            authority.BaseRules.Win.SuddenDeathBarChits
                            next

                    next <- reset
                    events.AddRange resetEvents

                events.Add(ReferenceEvents.create "SuddenDeathStarted")
                { next with Random = random.Snapshot }, events.ToArray()

    let private barChitsFor (authority: ReferenceAuthority) (card: CanonicalCard) =
        if card.Kind = "Bloke" then
            if authority.BaseRules.BigHitterIds.Contains card.MechanicalId then
                authority.BaseRules.SendHome.BigHitterBarChits
            else
                authority.BaseRules.SendHome.NormalBarChits
        elif
            authority.BaseRules.Fossil.KitIds.Contains card.MechanicalId
            && authority.BaseRules.Fossil.SentHomeAwardsOneBarChit
        then
            1
        else
            0

    let private stayingPower (authority: ReferenceAuthority) (card: CanonicalCard) =
        if card.Kind = "Bloke" then
            authority.Cards[card.MechanicalId].StayingPower
        else
            authority.BaseRules.Fossil.PlayAsRegularLocalStayingPower

    let internal resolveKnockoutsWithForced
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (attackingCard: string)
        (attackDamageTargets: string array)
        (forcedSendHome: string array)
        (extraBarChits: int)
        (state: CanonicalState)
        =
        if not authority.BaseRules.SendHome.ChuckBlokeAndAttachedCards then
            invalidOp "The reference send-home cleanup authority is unsupported."

        let ordered =
            state.Cards
            |> Array.filter (fun card ->
                isInPlay card
                && (Array.contains card.Id forcedSendHome
                    || (authority.BaseRules.SendHome.DamageAtLeastStayingPower
                        && card.Damage >= stayingPower authority card)))
            |> Array.sortBy (fun card -> card.Owner, card.Id)
            |> fun cards ->
                if mutation = ReverseKnockoutOrder then
                    Array.rev cards
                else
                    cards

        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()
        let mutable extraAwarded = false

        for identified in ordered do
            match ReferenceState.tryCard next identified.Id with
            | Some current when isInPlay current ->
                let wasOche = current.Zone = "Oche"
                next <- chuckCardPile current.Id next

                events.Add
                    { ReferenceEvents.create "BlokeSentHome" with
                        Actor = ReferenceState.otherPlayer next current.Owner
                        SourceCard = current.Id
                        TargetCards = [| current.Id |] }

                let takingPlayer = ReferenceState.otherPlayer next current.Owner

                let awarded, awardEvents, _ =
                    takeBarChits
                        mutation
                        takingPlayer
                        (barChitsFor authority current)
                        current.Id
                        next

                next <- awarded
                events.AddRange awardEvents

                if wasOche && authority.BaseRules.SendHome.OwnerPromotesFromBooth then
                    next <- assignReplacement mutation current.Owner next

                if
                    not extraAwarded
                    && extraBarChits > 0
                    && not (String.IsNullOrEmpty attackingCard)
                    && Array.contains current.Id attackDamageTargets
                    && current.Damage >= stayingPower authority current
                then
                    let extra, extraEvents, _ =
                        takeBarChits
                            mutation
                            (ReferenceState.card next attackingCard).Owner
                            extraBarChits
                            attackingCard
                            next

                    next <- extra
                    events.AddRange extraEvents
                    extraAwarded <- true
            | _ -> ()

        let won, winEvents = resolveWins authority mutation random "" next
        events.AddRange winEvents
        { won with Random = random.Snapshot }, events.ToArray()

    let internal resolveKnockouts
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (attackingCard: string)
        (attackDamageTargets: string array)
        (extraBarChits: int)
        (state: CanonicalState)
        =
        resolveKnockoutsWithForced
            authority
            mutation
            random
            attackingCard
            attackDamageTargets
            [||]
            extraBarChits
            state

    let private expireEffects (completedRound: int) (state: CanonicalState) =
        { state with
            Effects =
                state.Effects
                |> Array.filter (fun effect ->
                    effect.Duration = "WhileSourceInPlay"
                    || effect.Duration = "WhileTargetInPlay"
                    || effect.Duration = "CurrentResolution"
                    || effect.ExpiresAfterRound > completedRound) }

    let private startCommonRound
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (player: string)
        (state: CanonicalState)
        =
        let current = ReferenceState.player state player

        let mutable next =
            state
            |> ReferenceState.updatePlayer
                { current with
                    RoundsStarted = current.RoundsStarted + 1 }
            |> fun value ->
                { value with
                    ActivePlayer = player
                    RoundNumber = value.RoundNumber + 1
                    Phase = "Playing"
                    RoundUsage =
                        { Player = player
                          VimAttachments = 0
                          MatesPlayed = 0
                          LocalsPlayed = 0
                          TaxisUsed = 0
                          EffectsUsed = [||]
                          KitsPlayed = [||] } }

        let events =
            ResizeArray<CanonicalEvent>(
                [| { ReferenceEvents.create "RoundStarted" with
                       Actor = player } |]
            )

        if authority.BaseRules.Round.RequiredOpeningDraw then
            if ReferenceState.cardsIn next player "Stack" |> Array.isEmpty then
                if authority.BaseRules.RequiredRoundDrawFromEmptyStack <> "LoseBout" then
                    invalidOp "The reference required-draw authority is unsupported."

                let won, winEvents = resolveWins authority mutation random player next
                next <- won
                events.AddRange winEvents
            else
                let drawn, drawEvents, _ = draw player 1 "RequiredRoundDraw" next
                next <- drawn
                events.AddRange drawEvents

        { next with Random = random.Snapshot }, events.ToArray()

    let private clearRoughState
        (actor: string)
        (targetId: string)
        (stateName: string)
        (state: CanonicalState)
        =
        let target = ReferenceState.card state targetId

        if target.RoughStates |> Array.exists (fun entry -> entry.State = stateName) then
            ReferenceState.updateCard
                { target with
                    RoughStates =
                        target.RoughStates |> Array.filter (fun entry -> entry.State <> stateName) }
                state,
            [| { ReferenceEvents.create "RoughStateCleared" with
                   Actor = actor
                   TargetCards = [| targetId |]
                   RoughState = stateName } |]
        else
            state, [||]

    let private runCheckup
        (authority: ReferenceAuthority)
        (random: ReferenceRandom)
        (state: CanonicalState)
        =
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        for player in next.Players |> Array.map _.Id do
            match ReferenceState.cardsIn next player "Oche" with
            | [||] -> ()
            | ocheCards ->
                let ocheId = ocheCards[0].Id

                for roughState in authority.BaseRules.Checkup.RoughStateOrder do
                    let current = ReferenceState.card next ocheId
                    let stateName = string roughState

                    if
                        current.RoughStates |> Array.exists (fun entry -> entry.State = stateName)
                    then
                        let rule = authority.BaseRules.RoughStates[roughState]

                        if rule.CheckupDamageCounters > 0 then
                            let amount = rule.CheckupDamageCounters * 10

                            next <-
                                ReferenceState.updateCard
                                    { current with
                                        Damage = current.Damage + amount }
                                    next

                            events.Add
                                { ReferenceEvents.create "DamagePlaced" with
                                    Actor = player
                                    TargetCards = [| current.Id |]
                                    DamageKind = "RoughState"
                                    Amount = amount }

                        if rule.CheckupBeerMat then
                            let badge = random.NextInt(2) = 1

                            events.Add
                                { ReferenceEvents.create "BeerMatTossed" with
                                    Actor = player
                                    SourceCard = current.Id
                                    HasBadgeSide = true
                                    BadgeSide = badge }

                            if rule.BadgeSideRecovers && badge then
                                let cleared, clearEvents =
                                    clearRoughState player current.Id stateName next

                                next <- cleared
                                events.AddRange clearEvents
                        elif
                            rule.RecoversAfterOwnersNextRound = ValueSome true
                            && (ReferenceState.player next player).RoundsStarted > (current.RoughStates
                                                                                    |> Array.find
                                                                                        (fun entry ->
                                                                                            entry.State = stateName))
                                .AppliedAtOwnerRound
                        then
                            let cleared, clearEvents =
                                clearRoughState player current.Id stateName next

                            next <- cleared
                            events.AddRange clearEvents

        { next with Random = random.Snapshot }, events.ToArray()

    let internal reorderRoundStartBeforeCheckup
        (mutation: ReferenceMutation)
        (events: CanonicalEvent array)
        =
        if mutation <> StartNextRoundBeforeCheckup then
            events
        else
            match
                events |> Array.tryFindIndex (fun event -> event.Kind = "RoundEnded"),
                events |> Array.tryFindIndex (fun event -> event.Kind = "RoundStarted")
            with
            | Some ended, Some started when started > ended + 1 ->
                Array.concat
                    [ events[0..ended]
                      [| events[started] |]
                      events[ended + 1 .. started - 1]
                      events[started + 1 ..] ]
            | _ -> events

    let private completeCommonRound
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (state: CanonicalState)
        =
        if
            not authority.BaseRules.Checkup.CannotInterleave
            || not authority.BaseRules.Checkup.SendHomeAfterBothChecks
            || not authority.BaseRules.Checkup.OtherEffectsOutsideWholeBlock
        then
            invalidOp "The reference checkup ordering authority is unsupported."

        let completedPlayer = state.ActivePlayer
        let events = ResizeArray<CanonicalEvent>()

        events.Add
            { ReferenceEvents.create "RoundEnded" with
                Actor = completedPlayer }

        let checkedState, checkupEvents = runCheckup authority random state
        events.AddRange checkupEvents

        let knockedOut, knockoutEvents =
            resolveKnockouts authority mutation random "" [||] 0 checkedState

        events.AddRange knockoutEvents

        let mutable next = knockedOut

        if next.Phase = "Complete" then
            next <- { next with PendingRoundEnd = false }
        elif not (String.IsNullOrEmpty next.ReplacementPlayer) then
            next <-
                { next with
                    Phase = "AwaitingReplacement" }
        else
            let expired =
                { expireEffects state.RoundNumber next with
                    PendingRoundEnd = false }

            let begun, begunEvents =
                startCommonRound
                    authority
                    mutation
                    random
                    (ReferenceState.otherPlayer state completedPlayer)
                    expired

            next <- begun
            events.AddRange begunEvents

        { next with Random = random.Snapshot },
        events.ToArray() |> reorderRoundStartBeforeCheckup mutation

    let internal finishOrPendCommonRound
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (random: ReferenceRandom)
        (state: CanonicalState)
        =
        if state.Phase = "Complete" then
            state, [||]
        else
            let pending = { state with PendingRoundEnd = true }

            if not (String.IsNullOrEmpty pending.ReplacementPlayer) then
                { pending with
                    Phase = "AwaitingReplacement" },
                [||]
            else
                completeCommonRound authority mutation random pending

    let private attachVimCommon
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let parts = selected.Payload.Split(';')
            let vimId = parts[0].Substring(4)
            let targetId = parts[1].Substring(7)

            match ReferenceState.tryCard state vimId, ReferenceState.tryCard state targetId with
            | None, _
            | _, None -> Error "CardNotFound"
            | Some vim, Some target when
                vim.Owner <> selected.Actor || target.Owner <> selected.Actor
                ->
                Error "CardNotOwned"
            | Some vim, Some target when
                vim.Kind <> "Vim"
                || vim.Zone <> "Mitt"
                || not (isInPlay target)
                || state.RoundUsage.VimAttachments
                   >= authority.BaseRules.Vim.NormalAttachmentPerRound
                ->
                Error "RuleLimitReached"
            | Some vim, Some target ->
                let moved, movedEvent = moveCard vim.Id "Attached" target.Id state

                let attached =
                    ReferenceState.updateCard
                        { target with
                            Attachments = Array.append target.Attachments [| vim.Id |] }
                        moved

                Ok(
                    { attached with
                        RoundUsage =
                            { attached.RoundUsage with
                                VimAttachments = attached.RoundUsage.VimAttachments + 1 } },
                    [| movedEvent |]
                )

    let private playBlokeCommon
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let id = selected.Payload.Substring(6)

            match ReferenceState.tryCard state id with
            | None -> Error "CardNotFound"
            | Some card when card.Owner <> selected.Actor -> Error "CardNotOwned"
            | Some card when
                card.Kind <> "Bloke"
                || card.Zone <> "Mitt"
                || authority.Cards[card.MechanicalId].Rank <> ValueSome ReferenceRank.Regular
                || ReferenceState.cardsIn state selected.Actor "Booth" |> Array.length
                   >= authority.BaseRules.Opening.BoothLimit
                ->
                Error "RuleLimitReached"
            | Some card ->
                let moved, movedEvent = moveCard card.Id "Booth" "" state

                let entered =
                    ReferenceState.updateCard
                        { ReferenceState.card moved card.Id with
                            EnteredAtOwnerRound =
                                (ReferenceState.player moved selected.Actor).RoundsStarted }
                        moved

                Ok(entered, [| movedEvent |])

    let private promoteCommon
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let parts = selected.Payload.Split(';')
            let promotionId = parts[0].Substring(10)
            let targetId = parts[1].Substring(9)

            match
                ReferenceState.tryCard state promotionId, ReferenceState.tryCard state targetId
            with
            | None, _
            | _, None -> Error "CardNotFound"
            | Some promotion, Some target when
                not (promotionEligible authority state selected.Actor promotion target)
                ->
                Error "IneligiblePromotion"
            | Some promotion, Some target ->
                let rules = authority.BaseRules.Promotion

                let underlying =
                    { target with
                        Zone = "Attached"
                        AttachedTo = promotion.Id
                        Attachments = [||]
                        RoughStates = [||] }

                let promoted =
                    { promotion with
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

                let mutable next =
                    state
                    |> ReferenceState.updateCard underlying
                    |> ReferenceState.updateCard promoted

                for attachmentId in target.Attachments do
                    if rules.RetainDamageAndAttachedCards then
                        next <-
                            ReferenceState.updateCard
                                { ReferenceState.card next attachmentId with
                                    AttachedTo = promotion.Id }
                                next
                    else
                        let detached, _ = moveCard attachmentId "EmptiesTray" "" next
                        next <- detached

                next <-
                    if rules.ClearRoughStatesAndAttackEffects then
                        removeEffectsFor target.Id false next
                    else
                        { next with
                            Effects =
                                next.Effects
                                |> Array.map (fun effect ->
                                    { effect with
                                        SourceCard =
                                            if effect.SourceCard = target.Id then
                                                promotion.Id
                                            else
                                                effect.SourceCard
                                        TargetCard =
                                            if effect.TargetCard = target.Id then
                                                promotion.Id
                                            else
                                                effect.TargetCard }) }

                Ok(next, [||])

    let private resolvePrintedDamage
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (attacker: CanonicalCard)
        (target: CanonicalCard)
        (printed: int)
        =
        let mutable resolved = printed

        for step in authority.BaseRules.DamageOrder do
            match step with
            | "PrintedOrProgramBaseDamage"
            | "EffectsOnAttackingBlokeBeforeSoftSpotAndStubbornStreak"
            | "EffectsOnDefendingBlokeAfterSoftSpotAndStubbornStreak" -> ()
            | "SoftSpot" when mutation <> SkipDamageModifiers ->
                let attackerTypes = authority.Cards[attacker.MechanicalId].MechanicalTypes
                let targetRules = authority.Cards[target.MechanicalId].SoftSpotMultipliers

                let multiplier =
                    attackerTypes
                    |> Array.choose targetRules.TryFind
                    |> Array.tryHead
                    |> Option.defaultValue 1

                resolved <- resolved * multiplier
            | "StubbornStreak" when mutation <> SkipDamageModifiers ->
                let attackerTypes = authority.Cards[attacker.MechanicalId].MechanicalTypes
                let targetRules = authority.Cards[target.MechanicalId].StubbornStreakReductions

                let reduction =
                    attackerTypes
                    |> Array.choose targetRules.TryFind
                    |> Array.tryHead
                    |> Option.defaultValue 0

                resolved <- resolved - reduction
            | "SoftSpot"
            | "StubbornStreak" -> ()
            | "ClampAtZeroAndPlaceCounters" -> resolved <- max 0 resolved
            | unknown -> invalidOp $"Unsupported reference damage-order step {unknown}."

        resolved

    let private attackCommon
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let parts = selected.Payload.Split(';')
            let attackerId = parts[0].Substring(9)
            let attackId = parts[1].Substring(7)
            let attacker = ReferenceState.tryCard state attackerId

            let attack =
                attacker
                |> Option.bind (fun source ->
                    authority.Cards[source.MechanicalId].Attacks
                    |> Array.tryFind (fun value -> value.MechanicalId = attackId))

            match attacker, attack with
            | None, _
            | _, None -> Error "EffectNotFound"
            | Some attacker, Some attack when
                attacker.Owner <> selected.Actor
                || (tryCommonAttackProgram attack).IsNone
                || (attacker.Zone <> "Oche"
                    && not (attacker.Zone = "Booth" && attack.CanBeUsedFromBench))
                || (not authority.BaseRules.Opening.OpeningParticipantMayAttack
                    && selected.Actor = state.OpeningPlayer
                    && (ReferenceState.player state selected.Actor).RoundsStarted = 1)
                || roughPrevents authority _.PreventsAttack attacker
                ->
                Error "EffectUnavailable"
            | Some attacker, Some attack when not (canPayAttack authority state attacker attack) ->
                Error "InsufficientVim"
            | Some attacker, Some attack ->
                let program =
                    tryCommonAttackProgram attack
                    |> ValueOption.defaultWith (fun () ->
                        invalidOp
                            $"The reference common attack program {attack.MechanicalId} is unsupported.")

                let random = ReferenceRandom state.Random
                let events = ResizeArray<CanonicalEvent>()
                let mutable next = state
                let mutable continueResolution = true
                let mutable attackTargets = [||]
                let mutable plannedDamage = 0
                let mutable extraKnockoutBarChits = 0

                for step in authority.BaseRules.AttackOrder do
                    if continueResolution then
                        match step with
                        | "ValidateDeclaredAttackAndVim" ->
                            events.Add
                                { ReferenceEvents.create "AttackDeclared" with
                                    Actor = selected.Actor
                                    SourceCard = attacker.Id
                                    Effect = attack.MechanicalId }
                        | "ApplyEffectsThatAlterOrCancelAttack"
                        | "MakeRequiredChoices"
                        | "PayOrPerformUseRequirements"
                        | "ApplyBeforeDamageEffects" ->
                            match
                                ReferenceState.cardsIn
                                    next
                                    (ReferenceState.otherPlayer next selected.Actor)
                                    "Oche"
                            with
                            | [||] -> ()
                            | targets ->
                                let target = targets[0]

                                plannedDamage <-
                                    resolvePrintedDamage
                                        authority
                                        mutation
                                        attacker
                                        target
                                        program.PrintedDamage

                                if
                                    target.Damage + plannedDamage >= stayingPower authority target
                                then
                                    extraKnockoutBarChits <- program.ExtraKnockoutBarChits
                        | "ResolveOtherEffects" -> ()
                        | "ResolveMuddledCheck" ->
                            let muddled =
                                attacker.RoughStates
                                |> Array.tryFind (fun entry ->
                                    let parsed = Enum.Parse<ReferenceRoughState>(entry.State)

                                    authority.BaseRules.RoughStates[parsed].BeforeAttackBeerMat = ValueSome
                                        true)

                            match muddled with
                            | None -> ()
                            | Some rough ->
                                let badge = random.NextInt(2) = 1

                                events.Add
                                    { ReferenceEvents.create "BeerMatTossed" with
                                        Actor = selected.Actor
                                        SourceCard = attacker.Id
                                        Effect = attack.MechanicalId
                                        HasBadgeSide = true
                                        BadgeSide = badge }

                                if not badge then
                                    let parsed = Enum.Parse<ReferenceRoughState>(rough.State)

                                    let amount =
                                        authority.BaseRules.RoughStates[parsed]
                                            .BlankSideCancelsAndSelfDamageCounters
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
                                                Actor = selected.Actor
                                                SourceCard = attacker.Id
                                                TargetCards = [| attacker.Id |]
                                                DamageKind = "PlacedCounter"
                                                Amount = amount }

                                    events.Add
                                        { ReferenceEvents.create "AttackCancelled" with
                                            Actor = selected.Actor
                                            SourceCard = attacker.Id
                                            Effect = attack.MechanicalId }

                                    let resolved, resolvedEvents =
                                        resolveKnockouts authority mutation random "" [||] 0 next

                                    next <- resolved
                                    events.AddRange resolvedEvents

                                    let finished, finishEvents =
                                        finishOrPendCommonRound authority mutation random next

                                    next <- finished
                                    events.AddRange finishEvents
                                    continueResolution <- false
                        | "CalculateAndPlaceDamage" ->
                            match
                                ReferenceState.cardsIn
                                    next
                                    (ReferenceState.otherPlayer next selected.Actor)
                                    "Oche"
                            with
                            | [||] -> ()
                            | targets ->
                                let target = targets[0]

                                if plannedDamage > 0 then
                                    next <-
                                        ReferenceState.updateCard
                                            { target with
                                                Damage = target.Damage + plannedDamage }
                                            next

                                    events.Add
                                        { ReferenceEvents.create "DamagePlaced" with
                                            Actor = selected.Actor
                                            SourceCard = attacker.Id
                                            TargetCards = [| target.Id |]
                                            DamageKind = "Attack"
                                            Amount = plannedDamage }

                                    attackTargets <- [| target.Id |]
                        | "CheckAllSentHome" -> ()
                        | "TakeBarChitsAndPromote" ->
                            let knockedOut, knockoutEvents =
                                resolveKnockouts
                                    authority
                                    mutation
                                    random
                                    attacker.Id
                                    attackTargets
                                    extraKnockoutBarChits
                                    next

                            next <- knockedOut
                            events.AddRange knockoutEvents
                        | "EndRound" when authority.BaseRules.Round.AttackEndsRound ->
                            let finished, finishEvents =
                                finishOrPendCommonRound authority mutation random next

                            next <- finished
                            events.AddRange finishEvents
                        | "EndRound" -> ()
                        | unknown -> invalidOp $"Unsupported reference attack-order step {unknown}."

                Ok(
                    { next with Random = random.Snapshot },
                    events.ToArray() |> reorderRoundStartBeforeCheckup mutation
                )

    let private taxiCommon
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let parts = selected.Payload.Split(';')
            let boothId = parts[0].Substring(6)

            let vimIds = parts[1].Substring(4).Split(',', StringSplitOptions.RemoveEmptyEntries)

            match
                ReferenceState.cardsIn state selected.Actor "Oche",
                ReferenceState.tryCard state boothId
            with
            | [||], _
            | _, None -> Error "CardNotFound"
            | outgoingCards, Some incoming ->
                let outgoing = outgoingCards[0]

                if incoming.Owner <> selected.Actor || incoming.Zone <> "Booth" then
                    Error "WrongZone"
                elif
                    state.RoundUsage.TaxisUsed >= authority.BaseRules.Taxi.PerRound
                    || roughPrevents authority _.PreventsTaxi outgoing
                    || (outgoing.Kind = "Kit"
                        && authority.BaseRules.Fossil.KitIds.Contains outgoing.MechanicalId
                        && authority.BaseRules.Fossil.CannotTaxi)
                then
                    Error "EffectUnavailable"
                else
                    let fare =
                        if outgoing.Kind = "Bloke" then
                            authority.Cards[outgoing.MechanicalId].TaxiFare
                        else
                            Int32.MaxValue

                    let available = attachedVim authority state outgoing |> Array.map (fst >> _.Id)

                    if
                        vimIds.Length <> fare
                        || (vimIds |> Array.distinct |> Array.length) <> vimIds.Length
                        || vimIds |> Array.exists (fun id -> not (Array.contains id available))
                    then
                        Error "InvalidTaxiFare"
                    else
                        let mutable next = state
                        let events = ResizeArray<CanonicalEvent>()

                        if authority.BaseRules.Taxi.ChuckVimPerFareSymbol then
                            for id in vimIds do
                                let target = ReferenceState.card next outgoing.Id

                                next <-
                                    ReferenceState.updateCard
                                        { target with
                                            Attachments =
                                                target.Attachments |> Array.filter ((<>) id) }
                                        next

                                let moved, movedEvent = moveCard id "EmptiesTray" "" next
                                next <- moved
                                events.Add movedEvent

                        let movedOutgoing, outgoingEvent = moveCard outgoing.Id "Booth" "" next
                        next <- movedOutgoing
                        events.Add outgoingEvent

                        if
                            authority.BaseRules.Taxi.MovingToBoothClearsRoughStatesAndAttackEffects
                        then
                            let rough = (ReferenceState.card next outgoing.Id).RoughStates

                            next <-
                                ReferenceState.updateCard
                                    { ReferenceState.card next outgoing.Id with
                                        RoughStates = [||] }
                                    next

                            for entry in rough do
                                events.Add
                                    { ReferenceEvents.create "RoughStateCleared" with
                                        Actor = selected.Actor
                                        TargetCards = [| outgoing.Id |]
                                        RoughState = entry.State }

                            next <- removeEffectsFor outgoing.Id true next

                        if not authority.BaseRules.Taxi.AttachedCardsAndDamageRemain then
                            let moved = ReferenceState.card next outgoing.Id

                            for attachment in moved.Attachments do
                                let detached, detachedEvent =
                                    moveCard attachment "EmptiesTray" "" next

                                next <- detached
                                events.Add detachedEvent

                            next <-
                                ReferenceState.updateCard
                                    { ReferenceState.card next outgoing.Id with
                                        Damage = 0
                                        Attachments = [||] }
                                    next

                        let movedIncoming, incomingEvent = moveCard incoming.Id "Oche" "" next
                        next <- movedIncoming
                        events.Add incomingEvent

                        Ok(
                            { next with
                                RoundUsage =
                                    { next.RoundUsage with
                                        TaxisUsed = next.RoundUsage.TaxisUsed + 1 } },
                            events.ToArray()
                        )

    let private chuckFossilCommon
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> selected.Actor then
            Error "NotActorsTurn"
        else
            let id = selected.Payload.Substring(7)

            match ReferenceState.tryCard state id with
            | Some fossil when
                fossil.Owner = selected.Actor
                && fossil.Kind = "Kit"
                && authority.BaseRules.Fossil.KitIds.Contains fossil.MechanicalId
                && isInPlay fossil
                && authority.BaseRules.Fossil.MayChuckFromPlayDuringOwnersRound
                ->
                let chucked = chuckCardPile fossil.Id state

                let next =
                    if fossil.Zone = "Oche" then
                        assignReplacement mutation selected.Actor chucked
                    else
                        chucked

                Ok(next, [||])
            | _ -> Error "EffectUnavailable"

    let private endRoundCommon
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (actor: string)
        =
        if state.Phase <> "Playing" then
            Error "WrongPhase"
        elif state.ActivePlayer <> actor then
            Error "NotActorsTurn"
        else
            let random = ReferenceRandom state.Random
            let next, events = finishOrPendCommonRound authority mutation random state
            Ok({ next with Random = random.Snapshot }, events)

    let private nextReplacement (state: CanonicalState) =
        state.Players
        |> Array.map _.Id
        |> Array.tryFind (fun player ->
            ReferenceState.cardsIn state player "Oche" |> Array.isEmpty
            && not (ReferenceState.cardsIn state player "Booth" |> Array.isEmpty))
        |> Option.defaultValue ""

    let private chooseReplacementCommon
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if
            state.Phase <> "AwaitingReplacement"
            || state.ReplacementPlayer <> selected.Actor
        then
            Error "WrongPhase"
        else
            let id = selected.Payload.Substring(12)

            match ReferenceState.tryCard state id with
            | Some replacement when replacement.Owner = selected.Actor && replacement.Zone = "Booth" ->
                let moved, movedEvent = moveCard replacement.Id "Oche" "" state
                let replacementPlayer = nextReplacement moved

                let mutable next =
                    { moved with
                        ReplacementPlayer = replacementPlayer }

                let events = ResizeArray<CanonicalEvent>([| movedEvent |])

                if String.IsNullOrEmpty replacementPlayer then
                    if next.PendingRoundEnd then
                        let random = ReferenceRandom next.Random

                        let completed, completedEvents =
                            completeCommonRound
                                authority
                                mutation
                                random
                                { next with PendingRoundEnd = false }

                        next <- completed
                        events.AddRange completedEvents
                    else
                        next <- { next with Phase = "Playing" }

                Ok(next, events.ToArray())
            | _ -> Error "WrongZone"

    let private commonRejection
        (state: CanonicalState)
        (code: string)
        (requirements: CanonicalChoiceRequirement array)
        =
        { State = state
          Events = [||]
          Rejection =
            [| { Code = code
                 ChoiceRequirements = requirements } |] }

    let private commonResign (state: CanonicalState) (actor: string) =
        let winner = ReferenceState.otherPlayer state actor

        Ok(
            { state with
                Phase = "Complete"
                PendingEffect = Canonical.emptyPendingEffect
                PendingKnockout = Canonical.emptyPendingKnockout
                PendingBarChits = [||]
                ReplacementPlayer = ""
                PendingRoundEnd = false
                Terminal =
                    { state.Terminal with
                        IsComplete = true
                        Winner = winner } },
            [| { ReferenceEvents.create "MatchWon" with
                   Actor = winner } |]
        )

    let applyCommon
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        let boundary =
            if selected.MatchId <> state.MatchId then
                ValueSome "WrongMatch"
            elif Array.contains selected.CommandId state.Transport.ProcessedCommandIds then
                ValueSome "DuplicateCommand"
            elif selected.ExpectedRevision <> state.Transport.Revision then
                ValueSome "StaleRevision"
            elif state.AuthorityVersion <> authority.ManifestVersion then
                ValueSome "AuthorityMismatch"
            elif not (state.Players |> Array.exists (fun player -> player.Id = selected.Actor)) then
                ValueSome "UnknownActor"
            elif state.Phase = "Complete" then
                ValueSome "MatchComplete"
            else
                ValueNone

        match boundary with
        | ValueSome code -> commonRejection state code [||]
        | ValueNone ->
            let refreshed, refreshEvents = refreshContinuousEffects authority state

            let result =
                match selected.Kind with
                | "AttachVim" -> attachVimCommon authority refreshed selected
                | "PlayBloke" -> playBlokeCommon authority refreshed selected
                | "Promote" -> promoteCommon authority refreshed selected
                | "Attack" -> attackCommon authority mutation refreshed selected
                | "Taxi" -> taxiCommon authority refreshed selected
                | "ChuckFossil" -> chuckFossilCommon authority mutation refreshed selected
                | "EndRound" -> endRoundCommon authority mutation refreshed selected.Actor
                | "ChooseReplacement" ->
                    chooseReplacementCommon authority mutation refreshed selected
                | "Resign" -> commonResign refreshed selected.Actor
                | _ -> Error "WrongPhase"

            match result with
            | Error code -> commonRejection state code [||]
            | Ok(next, semanticEvents) ->
                let submitted =
                    { ReferenceEvents.create "CommandApplied" with
                        Actor = selected.Actor
                        Transport =
                            { Canonical.emptyEventTransport with
                                HasCommand = true } }

                let beforeCommit =
                    { next with
                        Transport =
                            { next.Transport with
                                ProcessedCommandIds =
                                    Array.append
                                        next.Transport.ProcessedCommandIds
                                        [| selected.CommandId |] } }

                let committed, events =
                    ReferenceEvents.commit
                        (state.Transport.Revision + 1L)
                        beforeCommit
                        (Array.concat [ [| submitted |]; refreshEvents; semanticEvents ])

                { State = committed
                  Events = events
                  Rejection = [||] }
