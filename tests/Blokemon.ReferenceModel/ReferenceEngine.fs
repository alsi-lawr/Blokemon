namespace Blokemon.ReferenceModel

open System

[<RequireQualifiedAccess>]
module internal ReferenceState =

    let card (state: CanonicalState) (id: string) : CanonicalCard =
        state.Cards |> Array.find (fun card -> card.Id = id)

    let tryCard (state: CanonicalState) (id: string) : CanonicalCard option =
        state.Cards |> Array.tryFind (fun card -> card.Id = id)

    let player (state: CanonicalState) (id: string) : CanonicalPlayer =
        state.Players |> Array.find (fun player -> player.Id = id)

    let otherPlayer (state: CanonicalState) (id: string) =
        state.Players |> Array.find (fun player -> player.Id <> id) |> _.Id

    let cardsIn (state: CanonicalState) (owner: string) (zone: string) : CanonicalCard array =
        state.Cards
        |> Array.filter (fun card -> card.Owner = owner && card.Zone = zone)
        |> Array.sortBy (fun card -> card.StackPosition, card.Id)

    let updateCard (card: CanonicalCard) (state: CanonicalState) =
        { state with
            Cards =
                state.Cards
                |> Array.map (fun current -> if current.Id = card.Id then card else current) }

    let updatePlayer (player: CanonicalPlayer) (state: CanonicalState) =
        { state with
            Players =
                state.Players
                |> Array.map (fun current -> if current.Id = player.Id then player else current) }

[<RequireQualifiedAccess>]
module internal ReferenceEvents =

    let create (kind: string) : CanonicalEvent =
        { RelativeSequence = 0
          Revision = 0L
          Kind = kind
          Actor = ""
          SourceCard = ""
          TargetCards = [||]
          Effect = ""
          RoughState = ""
          DamageKind = ""
          DrawReason = ""
          Amount = 0
          HasBadgeSide = false
          BadgeSide = false
          Transport = Canonical.emptyEventTransport }

    let move (card: CanonicalCard) : CanonicalEvent =
        { create "CardMoved" with
            Actor = card.Owner
            SourceCard = card.Id
            TargetCards = [| card.Id |] }

    let commit (revision: int64) (state: CanonicalState) (events: CanonicalEvent array) =
        let lastEventSequence = state.Transport.LastEventSequence + int64 events.Length + 1L

        let committedState =
            { state with
                Transport =
                    { state.Transport with
                        Revision = revision
                        LastEventSequence = lastEventSequence } }

        let stamped =
            events
            |> Array.mapi (fun index event ->
                { event with
                    RelativeSequence = index + 1
                    Revision = revision })

        let committed =
            { create "StateCommitted" with
                RelativeSequence = events.Length + 1
                Revision = revision
                Transport =
                    { Canonical.emptyEventTransport with
                        HasCommittedState = true } }

        committedState, Array.append stamped [| committed |]

[<RequireQualifiedAccess>]
module ReferenceEngine =

    let internal actionOrder =
        Map
            [ "ChooseMulliganBonus", 0
              "ChooseOpening", 1
              "ChooseReplacement", 2
              "AttachVim", 3
              "PlayBloke", 4
              "Promote", 5
              "PlayKit", 6
              "UsePartyTrick", 7
              "Attack", 8
              "Taxi", 9
              "ChuckFossil", 10
              "EndRound", 11
              "ResolveEffectChoice", 12
              "ResolveKnockoutTrigger", 13
              "ResolveBarChitTrigger", 14
              "Resign", 15
              "ChooseBonusPlacement", 16 ]

    let private emptyRequirement
        (id: string)
        (kind: string)
        (chooser: string)
        (minimum: int)
        (maximum: int)
        (cards: string array)
        : CanonicalChoiceRequirement =
        { Id = id
          Kind = kind
          Chooser = chooser
          Minimum = minimum
          Maximum = maximum
          EligibleCards = cards
          EligibleMechanicalTypes = [||]
          EligibleEffects = [||]
          DependsOnOptional = ""
          EligibleTargets = [||]
          RequireDifferentMechanicalTypes = false
          EligibleCardTypes = [||] }

    let internal action
        (state: CanonicalState)
        (kind: string)
        (actor: string)
        (key: string)
        (stableKey: string)
        (payload: string)
        (requirements: CanonicalChoiceRequirement array)
        (choices: CanonicalChoice array)
        : CanonicalAction =
        { Kind = kind
          CommandId = $"cpu:{state.Transport.Revision}:{key}"
          MatchId = state.MatchId
          Actor = actor
          ExpectedRevision = state.Transport.Revision
          StableKey = stableKey
          Payload = payload
          Affordability = "Payable"
          Requirements = requirements
          Choices = choices }

    let submittedAction
        (state: CanonicalState)
        (commandId: string)
        (actor: string)
        (expectedRevision: int64)
        (kind: string)
        (payload: string)
        : CanonicalAction =
        { Kind = kind
          CommandId = commandId
          MatchId = state.MatchId
          Actor = actor
          ExpectedRevision = expectedRevision
          StableKey = ""
          Payload = payload
          Affordability = "Payable"
          Requirements = [||]
          Choices = [||] }

    let withMatchId (matchId: string) (action: CanonicalAction) = { action with MatchId = matchId }

    let private createCard
        (authority: ReferenceAuthority)
        (playerNumber: int)
        (owner: string)
        (index: int)
        (mechanicalId: string)
        : CanonicalCard =
        { Id = $"C{playerNumber}-%03d{index + 1}"
          MechanicalId = mechanicalId
          Owner = owner
          Kind = string authority.Cards[mechanicalId].Kind
          Zone = "Stack"
          IsFaceDown = false
          StackPosition = index
          AttachedTo = ""
          Attachments = [||]
          UnderlyingCards = [||]
          Damage = 0
          RoughStates = [||]
          EnteredAtOwnerRound = 0
          LastPromotedRound = -1 }

    let private deckIssues (authority: ReferenceAuthority) (deck: ReferenceDeck) =
        let rules = authority.BaseRules.Stack
        let issues = ResizeArray<ReferenceStartRejection>()

        if deck.Cards.Length <> rules.CardCount then
            issues.Add
                { Code = "WrongCardCount"
                  Player = deck.Owner
                  Card = ""
                  Actual = deck.Cards.Length
                  Expected = rules.CardCount }

        let unknown =
            deck.Cards
            |> Array.filter (authority.Cards.ContainsKey >> not)
            |> Array.distinct

        for card in unknown do
            issues.Add
                { Code = "UnknownMechanicalCard"
                  Player = deck.Owner
                  Card = card
                  Actual = 0
                  Expected = 0 }

        for cardId, copies in
            deck.Cards |> Array.filter authority.Cards.ContainsKey |> Array.countBy id do
            let card = authority.Cards[cardId]

            let limit =
                if rules.BasicVimExempt && card.Kind = ReferenceCardKind.Vim then
                    Int32.MaxValue
                else
                    min rules.MechanicalCopyLimit card.StackCopyLimit

            if copies > limit then
                issues.Add
                    { Code = "TooManyCopies"
                      Player = deck.Owner
                      Card = cardId
                      Actual = copies
                      Expected = limit }

        if
            rules.RequiresRegularBloke
            && not (
                deck.Cards
                |> Array.exists (fun id ->
                    authority.Cards.ContainsKey id
                    && authority.Cards[id].Rank = ValueSome ReferenceRank.Regular)
            )
        then
            issues.Add
                { Code = "MissingRegularBloke"
                  Player = deck.Owner
                  Card = ""
                  Actual = 0
                  Expected = 1 }

        issues.ToArray()

    let internal shuffle player (random: ReferenceRandom) (state: CanonicalState) =
        let stack = ReferenceState.cardsIn state player "Stack" |> Array.sortBy _.Id

        for index in stack.Length - 1 .. -1 .. 1 do
            let swapIndex = random.NextInt(index + 1)
            let held = stack[index]
            stack[index] <- stack[swapIndex]
            stack[swapIndex] <- held

        let positions =
            stack |> Array.mapi (fun index card -> card.Id, index) |> Map.ofArray

        { state with
            Cards =
                state.Cards
                |> Array.map (fun card ->
                    match positions.TryFind card.Id with
                    | Some position -> { card with StackPosition = position }
                    | None -> card) }

    let internal moveCard (id: string) (zone: string) (attachedTo: string) (state: CanonicalState) =
        let card = ReferenceState.card state id

        let boothPosition =
            ReferenceState.cardsIn state card.Owner "Booth"
            |> Array.fold (fun beneath current -> max beneath (current.StackPosition + 1)) 0

        let moved =
            { card with
                Zone = zone
                IsFaceDown = zone = "BarChit"
                StackPosition = if zone = "Booth" then boothPosition else -1
                AttachedTo = attachedTo }

        ReferenceState.updateCard moved state, ReferenceEvents.move card

    let internal draw (player: string) (count: int) (reason: string) (state: CanonicalState) =
        let drawn =
            ReferenceState.cardsIn state player "Stack"
            |> Array.truncate count
            |> Array.map _.Id

        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        for id in drawn do
            let moved, event = moveCard id "Mitt" "" next
            next <- moved
            events.Add event

        if drawn.Length > 0 then
            events.Add
                { ReferenceEvents.create "CardsDrawn" with
                    Actor = player
                    TargetCards = drawn
                    DrawReason = reason
                    Amount = drawn.Length }

        next, events.ToArray(), drawn

    let private returnMittToStack (player: string) (state: CanonicalState) =
        let nextPosition =
            ReferenceState.cardsIn state player "Stack"
            |> Array.fold (fun beneath card -> max beneath (card.StackPosition + 1)) 0

        let positions =
            ReferenceState.cardsIn state player "Mitt"
            |> Array.mapi (fun index card -> card.Id, nextPosition + index)
            |> Map.ofArray

        { state with
            Cards =
                state.Cards
                |> Array.map (fun card ->
                    match positions.TryFind card.Id with
                    | Some position ->
                        { card with
                            Zone = "Stack"
                            StackPosition = position }
                    | None -> card) }

    let private dealOpeningMitts
        (authority: ReferenceAuthority)
        (random: ReferenceRandom)
        (state: CanonicalState)
        =
        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        for player in next.Players |> Array.map _.Id do
            next <- shuffle player random next

            events.Add
                { ReferenceEvents.create "CardsShuffled" with
                    Actor = player }

        for player in next.Players |> Array.map _.Id do
            let drawnState, drawnEvents, _ =
                draw player authority.BaseRules.Opening.MittSize "OpeningMitt" next

            next <- drawnState
            events.AddRange drawnEvents

        let mutable settled = false

        while not settled do
            let mulliganPlayers =
                next.Players
                |> Array.map _.Id
                |> Array.filter (fun player ->
                    ReferenceState.cardsIn next player "Mitt"
                    |> Array.exists (fun card ->
                        card.Kind = "Bloke"
                        && authority.Cards[card.MechanicalId].Rank = ValueSome
                            ReferenceRank.Regular)
                    |> not)

            if mulliganPlayers.Length = 0 then
                settled <- true
            else
                for player in mulliganPlayers do
                    let mitt = ReferenceState.cardsIn next player "Mitt"

                    events.Add
                        { ReferenceEvents.create "CardsRevealed" with
                            Actor = player
                            TargetCards = mitt |> Array.map _.Id }

                    next <- returnMittToStack player next
                    let current = ReferenceState.player next player

                    next <-
                        ReferenceState.updatePlayer
                            { current with
                                MulliganCount = current.MulliganCount + 1 }
                            next

                for player in mulliganPlayers do
                    next <- shuffle player random next

                    events.Add
                        { ReferenceEvents.create "CardsShuffled" with
                            Actor = player }

                for player in mulliganPlayers do
                    let drawnState, drawnEvents, _ =
                        draw player authority.BaseRules.Opening.MittSize "OpeningMitt" next

                    next <- drawnState
                    events.AddRange drawnEvents

        let players = next.Players

        for current in players do
            let other = players |> Array.find (fun candidate -> candidate.Id <> current.Id)
            let allowance = max 0 (other.MulliganCount - current.MulliganCount)

            next <-
                ReferenceState.updatePlayer
                    { current with
                        MulliganBonusAllowance = allowance
                        MulliganBonusChosen = allowance = 0 }
                    next

        { next with Phase = "OpeningPlacement" }, events.ToArray()

    let start
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (request: ReferenceStartRequest)
        =
        let issues = ResizeArray<ReferenceStartRejection>()

        if String.IsNullOrWhiteSpace request.MatchId then
            issues.Add
                { Code = "InvalidMatchId"
                  Player = ""
                  Card = ""
                  Actual = 0
                  Expected = 0 }

        if
            String.IsNullOrWhiteSpace request.FirstDeck.Owner
            || String.IsNullOrWhiteSpace request.SecondDeck.Owner
        then
            issues.Add
                { Code = "InvalidPlayerId"
                  Player = ""
                  Card = ""
                  Actual = 0
                  Expected = 0 }

        if request.FirstDeck.Owner = request.SecondDeck.Owner then
            issues.Add
                { Code = "DuplicatePlayer"
                  Player = request.FirstDeck.Owner
                  Card = ""
                  Actual = 2
                  Expected = 1 }

        issues.AddRange(deckIssues authority request.FirstDeck)
        issues.AddRange(deckIssues authority request.SecondDeck)

        if issues.Count > 0 then
            StartRejected(issues.ToArray())
        else
            let random =
                ReferenceRandom
                    { State = request.Seed
                      ConsumptionIndex = 0 }

            let players = [| request.FirstDeck.Owner; request.SecondDeck.Owner |]
            let openingPlayer = players[random.NextInt players.Length]

            let cards =
                Array.append
                    (request.FirstDeck.Cards
                     |> Array.mapi (createCard authority 1 request.FirstDeck.Owner))
                    (request.SecondDeck.Cards
                     |> Array.mapi (createCard authority 2 request.SecondDeck.Owner))

            let initial =
                { MatchId = request.MatchId
                  AuthorityVersion = authority.ManifestVersion
                  Seed = request.Seed
                  Random = random.Snapshot
                  Transport =
                    { Revision = 0L
                      LastEventSequence = 0L
                      ProcessedCommandIds = [||] }
                  Phase = "OpeningPlacement"
                  OpeningPlayer = openingPlayer
                  ActivePlayer = openingPlayer
                  RoundNumber = 0
                  Players =
                    players
                    |> Array.map (fun player ->
                        { Id = player
                          BarChitsRemaining = authority.BaseRules.Opening.BarChitCount
                          MulliganCount = 0
                          MulliganBonusAllowance = 0
                          MulliganBonusChosen = false
                          BonusDrawn = [||]
                          BonusPlacementChosen = true
                          OpeningChosen = false
                          RoundsStarted = 0 })
                  Cards = cards
                  Effects = [||]
                  RoundUsage =
                    { Player = openingPlayer
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

            let dealt, dealEvents = dealOpeningMitts authority random initial
            let ready = { dealt with Random = random.Snapshot }

            let startEvent =
                { ReferenceEvents.create "MatchStarted" with
                    Transport =
                        { Canonical.emptyEventTransport with
                            HasStartRequest = true } }

            let committed, events =
                ReferenceEvents.commit 0L ready (Array.append [| startEvent |] dealEvents)

            Started
                { State = committed
                  Events = events
                  Rejection = [||] }

    let private mayPlaceOpening (state: CanonicalState) (actor: string) =
        let fewest = state.Players |> Array.map _.MulliganCount |> Array.min
        let current = ReferenceState.player state actor

        current.MulliganCount = fewest
        || state.Players
           |> Array.filter (fun player -> player.MulliganCount = fewest)
           |> Array.forall _.OpeningChosen

    let private bonusBenchable
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let player = ReferenceState.player state actor

        if player.BonusPlacementChosen then
            [||]
        else
            let room =
                authority.BaseRules.Opening.BoothLimit
                - (ReferenceState.cardsIn state actor "Booth" |> Array.length)

            if room <= 0 then
                [||]
            else
                player.BonusDrawn
                |> Array.map (ReferenceState.card state)
                |> Array.filter (fun card ->
                    card.Zone = "Mitt"
                    && card.Kind = "Bloke"
                    && authority.Cards[card.MechanicalId].Rank = ValueSome ReferenceRank.Regular)

    let private mulliganBonusActions (state: CanonicalState) (actor: string) =
        let player = ReferenceState.player state actor

        if player.MulliganBonusChosen || player.MulliganBonusAllowance = 0 then
            [||]
        else
            [| for count in 0 .. player.MulliganBonusAllowance ->
                   action
                       state
                       "ChooseMulliganBonus"
                       actor
                       $"bonus:{actor}:{count}"
                       $"bonus:%03d{count}"
                       $"cards={count}"
                       [||]
                       [||] |]

    let private openingActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let player = ReferenceState.player state actor

        if player.OpeningChosen || not (mayPlaceOpening state actor) then
            [||]
        else
            let regulars =
                ReferenceState.cardsIn state actor "Mitt"
                |> Array.filter (fun card ->
                    card.Kind = "Bloke"
                    && authority.Cards[card.MechanicalId].Rank = ValueSome ReferenceRank.Regular)

            regulars
            |> Array.map (fun oche ->
                let booth =
                    regulars |> Array.filter (fun card -> card.Id <> oche.Id) |> Array.map _.Id

                let requirement =
                    emptyRequirement
                        "opening:booth"
                        "Cards"
                        actor
                        0
                        (min authority.BaseRules.Opening.BoothLimit (regulars.Length - 1))
                        booth

                action
                    state
                    "ChooseOpening"
                    actor
                    $"opening:{actor}:{oche.Id}"
                    $"opening:{oche.Id}"
                    $"oche={oche.Id};booth="
                    [| requirement |]
                    [||])

    let private bonusPlacementActions
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (actor: string)
        =
        let benchable = bonusBenchable authority state actor
        let player = ReferenceState.player state actor

        if player.BonusPlacementChosen then
            [||]
        else
            let room =
                authority.BaseRules.Opening.BoothLimit
                - (ReferenceState.cardsIn state actor "Booth" |> Array.length)

            let requirement =
                emptyRequirement
                    "bonus:booth"
                    "Cards"
                    actor
                    0
                    (min room benchable.Length)
                    (benchable |> Array.map _.Id)

            [| action
                   state
                   "ChooseBonusPlacement"
                   actor
                   $"bonusbooth:{actor}"
                   "bonusbooth"
                   "booth="
                   [| requirement |]
                   [||] |]

    let private playingFoundationActions (state: CanonicalState) (actor: string) =
        if state.ActivePlayer <> actor then
            [||]
        else
            [| action state "EndRound" actor "end" "end" "end" [||] [||] |]

    let legalFoundationActions
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        actor
        =
        if
            state.Phase = "Complete"
            || not (state.Players |> Array.exists (fun player -> player.Id = actor))
        then
            [||]
        else
            let phaseActions =
                match state.Phase with
                | "MulliganBonus" -> mulliganBonusActions state actor
                | "OpeningPlacement" -> openingActions authority state actor
                | "BonusPlacement" -> bonusPlacementActions authority state actor
                | "Playing" -> playingFoundationActions state actor
                | _ -> [||]

            let resignation =
                match mutation with
                | OmitResignFromLegalActions -> [||]
                | _ ->
                    [| action state "Resign" actor $"resign:{actor}" "resign" "resign" [||] [||] |]

            Array.append phaseActions resignation
            |> Array.sortWith (fun left right ->
                let byKind = compare actionOrder[left.Kind] actionOrder[right.Kind]

                if byKind <> 0 then
                    byKind
                else
                    String.CompareOrdinal(left.StableKey, right.StableKey))

    let nextActor
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        =
        match state.Phase with
        | "Playing" -> state.ActivePlayer
        | "MulliganBonus"
        | "OpeningPlacement"
        | "BonusPlacement" ->
            state.Players
            |> Array.map _.Id
            |> Array.sort
            |> Array.tryFind (fun player ->
                legalFoundationActions authority mutation state player
                |> Array.exists (fun candidate -> candidate.Kind <> "Resign"))
            |> Option.defaultValue state.ActivePlayer
        | _ -> state.ActivePlayer

    let selectFoundationAction
        (authority: ReferenceAuthority)
        (state: CanonicalState)
        (completedEndRounds: int)
        (actions: CanonicalAction array)
        =
        match state.Phase with
        | "MulliganBonus" ->
            actions
            |> Array.filter (fun candidate -> candidate.Kind = "ChooseMulliganBonus")
            |> Array.maxBy (fun candidate -> Int32.Parse(candidate.Payload.Substring(6)))
        | "OpeningPlacement" ->
            let opening =
                actions |> Array.filter (fun candidate -> candidate.Kind = "ChooseOpening")

            let template =
                opening
                |> Array.filter (fun candidate ->
                    let mechanicalId =
                        (candidate.Payload.Split(';')).[0].Substring(5)
                        |> ReferenceState.card state
                        |> _.MechanicalId

                    authority.Cards[mechanicalId].PartyTricks.Length = 0)
                |> Array.tryHead
                |> Option.defaultWith (fun () -> opening |> Array.head)

            let booth =
                template.Requirements
                |> Array.collect _.EligibleCards
                |> Array.filter (fun id ->
                    let mechanicalId = (ReferenceState.card state id).MechanicalId
                    authority.Cards[mechanicalId].PartyTricks.Length = 0)
                |> Array.sort
                |> Array.truncate 1

            let oche = (template.Payload.Split(';')).[0]
            let boothText = String.concat "," booth

            { template with
                Payload = $"{oche};booth={boothText}" }
        | "BonusPlacement" ->
            let template =
                actions |> Array.find (fun candidate -> candidate.Kind = "ChooseBonusPlacement")

            let chosen =
                template.Requirements
                |> Array.collect _.EligibleCards
                |> Array.sort
                |> Array.truncate 1

            let chosenText = String.concat "," chosen

            { template with
                Payload = $"booth={chosenText}" }
        | "Playing" when completedEndRounds < 2 ->
            actions |> Array.find (fun candidate -> candidate.Kind = "EndRound")
        | _ -> actions |> Array.find (fun candidate -> candidate.Kind = "Resign")

    let private setAsideBarChits
        (authority: ReferenceAuthority)
        (player: string)
        (state: CanonicalState)
        =
        let ids =
            ReferenceState.cardsIn state player "Stack"
            |> Array.truncate authority.BaseRules.Opening.BarChitCount
            |> Array.map _.Id

        let mutable next = state
        let events = ResizeArray<CanonicalEvent>()

        for index in 0 .. ids.Length - 1 do
            let moved, event = moveCard ids[index] "BarChit" "" next

            next <-
                ReferenceState.updateCard
                    { ReferenceState.card moved ids[index] with
                        StackPosition = index }
                    moved

            events.Add event

        let playerState = ReferenceState.player next player

        next <-
            ReferenceState.updatePlayer
                { playerState with
                    BarChitsRemaining = ids.Length }
                next

        next, events.ToArray()

    let private startRound
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
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

        if
            authority.BaseRules.Round.RequiredOpeningDraw
            && mutation <> SkipRequiredOpeningDraw
        then
            let drawn, drawEvents, _ = draw player 1 "RequiredRoundDraw" next
            next <- drawn
            events.AddRange drawEvents

        next, events.ToArray()

    let private settleAfterBonus
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        =
        let benchable =
            state.Players
            |> Array.exists (fun current ->
                bonusBenchable authority state current.Id |> Array.isEmpty |> not)

        if benchable then
            { state with Phase = "BonusPlacement" }, [||]
        else
            startRound authority mutation state.OpeningPlayer state

    let private chooseMulliganBonus
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "MulliganBonus" then
            Error "WrongPhase"
        else
            let player = ReferenceState.player state selected.Actor
            let count = Int32.Parse(selected.Payload.Substring(6))

            if
                player.MulliganBonusChosen
                || player.MulliganBonusAllowance = 0
                || count < 0
                || count > player.MulliganBonusAllowance
            then
                Error "RuleLimitReached"
            else
                let drawnState, events, drawn = draw selected.Actor count "MulliganBonus" state
                let remaining = player.MulliganBonusAllowance - count
                let closed = count = 0 || remaining = 0

                let mutable next =
                    ReferenceState.updatePlayer
                        { player with
                            MulliganBonusAllowance = remaining
                            MulliganBonusChosen = closed
                            BonusDrawn = Array.append player.BonusDrawn drawn
                            BonusPlacementChosen = false }
                        drawnState

                if
                    next.Players
                    |> Array.forall (fun current ->
                        (ReferenceState.player next current.Id).MulliganBonusChosen)
                then
                    let settled, settledEvents = settleAfterBonus authority mutation next
                    next <- settled
                    Ok(next, Array.append events settledEvents)
                else
                    Ok(next, events)

    let private chooseBonusPlacement
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "BonusPlacement" then
            Error "WrongPhase"
        else
            let player = ReferenceState.player state selected.Actor
            let benchable = bonusBenchable authority state selected.Actor

            let room =
                authority.BaseRules.Opening.BoothLimit
                - (ReferenceState.cardsIn state selected.Actor "Booth" |> Array.length)

            let booth =
                selected.Payload.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries)

            let illegal =
                player.BonusPlacementChosen
                || booth.Length > room
                || (booth |> Array.distinct |> Array.length) <> booth.Length
                || booth
                   |> Array.exists (fun id ->
                       benchable |> Array.exists (fun card -> card.Id = id) |> not)

            if illegal then
                Error "IllegalOpening"
            else
                let mutable next = state
                let events = ResizeArray<CanonicalEvent>()

                for id in booth do
                    let moved, event = moveCard id "Booth" "" next
                    next <- moved
                    events.Add event

                    next <-
                        ReferenceState.updateCard
                            { ReferenceState.card next id with
                                EnteredAtOwnerRound =
                                    (ReferenceState.player next selected.Actor).RoundsStarted }
                            next

                next <-
                    ReferenceState.updatePlayer
                        { ReferenceState.player next selected.Actor with
                            BonusPlacementChosen = true }
                        next

                if
                    next.Players
                    |> Array.forall (fun current ->
                        (ReferenceState.player next current.Id).BonusPlacementChosen)
                then
                    let begun, begunEvents = startRound authority mutation next.OpeningPlayer next

                    Ok(begun, Array.append (events.ToArray()) begunEvents)
                else
                    Ok(next, events.ToArray())

    let private chooseOpening
        (authority: ReferenceAuthority)
        (mutation: ReferenceMutation)
        (state: CanonicalState)
        (selected: CanonicalAction)
        =
        if state.Phase <> "OpeningPlacement" then
            Error "WrongPhase"
        elif not (mayPlaceOpening state selected.Actor) then
            Error "WrongPhase"
        else
            let parts = selected.Payload.Split(';')
            let oche = parts[0].Substring(5)

            let booth = parts[1].Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries)

            let player = ReferenceState.player state selected.Actor
            let ocheCard = ReferenceState.tryCard state oche
            let boothCards = booth |> Array.map (ReferenceState.tryCard state)

            let illegal =
                player.OpeningChosen
                || ocheCard.IsNone
                || ocheCard.Value.Owner <> selected.Actor
                || ocheCard.Value.Zone <> "Mitt"
                || ocheCard.Value.Kind <> "Bloke"
                || authority.Cards[ocheCard.Value.MechanicalId].Rank
                   <> ValueSome ReferenceRank.Regular
                || booth.Length > authority.BaseRules.Opening.BoothLimit
                || (booth |> Array.distinct |> Array.length) <> booth.Length
                || Array.contains oche booth
                || boothCards
                   |> Array.exists (function
                       | None -> true
                       | Some card ->
                           card.Owner <> selected.Actor
                           || card.Zone <> "Mitt"
                           || card.Kind <> "Bloke"
                           || authority.Cards[card.MechanicalId].Rank
                              <> ValueSome ReferenceRank.Regular)

            if illegal then
                Error "IllegalOpening"
            else
                let mutable next, ocheEvent = moveCard oche "Oche" "" state
                let events = ResizeArray<CanonicalEvent>([| ocheEvent |])

                next <-
                    ReferenceState.updateCard
                        { ReferenceState.card next oche with
                            EnteredAtOwnerRound =
                                (ReferenceState.player next selected.Actor).RoundsStarted }
                        next

                for id in booth do
                    let moved, event = moveCard id "Booth" "" next
                    next <- moved
                    events.Add event

                    next <-
                        ReferenceState.updateCard
                            { ReferenceState.card next id with
                                EnteredAtOwnerRound =
                                    (ReferenceState.player next selected.Actor).RoundsStarted }
                            next

                next <-
                    ReferenceState.updatePlayer
                        { ReferenceState.player next selected.Actor with
                            OpeningChosen = true }
                        next

                if next.Players |> Array.forall _.OpeningChosen then
                    for current in next.Players |> Array.map _.Id do
                        let settled, barChitEvents = setAsideBarChits authority current next
                        next <- settled
                        events.AddRange barChitEvents

                    if
                        next.Players
                        |> Array.exists (fun current -> current.MulliganBonusAllowance > 0)
                    then
                        next <- { next with Phase = "MulliganBonus" }
                    else
                        let begun, roundEvents =
                            startRound authority mutation next.OpeningPlayer next

                        next <- begun
                        events.AddRange roundEvents

                Ok(next, events.ToArray())

    let private endRound
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
            let nextPlayer = ReferenceState.otherPlayer state actor
            let begun, begunEvents = startRound authority mutation nextPlayer state

            Ok(
                begun,
                Array.append
                    [| { ReferenceEvents.create "RoundEnded" with
                           Actor = actor } |]
                    begunEvents
            )

    let private resign (state: CanonicalState) (actor: string) =
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
        | ValueSome code -> rejection state code [||]
        | ValueNone ->
            let result =
                match selected.Kind with
                | "ChooseMulliganBonus" -> chooseMulliganBonus authority mutation state selected
                | "ChooseOpening" -> chooseOpening authority mutation state selected
                | "ChooseBonusPlacement" -> chooseBonusPlacement authority mutation state selected
                | "EndRound" -> endRound authority mutation state selected.Actor
                | "Resign" -> resign state selected.Actor
                | _ -> Error "WrongPhase"

            match result with
            | Error code -> rejection state code [||]
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
                        (Array.append [| submitted |] semanticEvents)

                { State = committed
                  Events = events
                  Rejection = [||] }
