namespace Blokemon.Game

open System
open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign

/// The mutable staging area one command is applied through. It is seeded from the current state,
/// mutated in place by the handlers and the interpreter, and snapshotted once at the commit — so
/// applying an action stays a single-command transition and never re-walks the command log.
type internal MatchBuilder(state: MatchState, catalog: AuthorityCatalog) =
    let players = ResizeArray<PlayerState> state.Players
    let cards = ResizeArray<CardState> state.Cards
    let effects = ResizeArray<TemporaryEffect> state.Effects
    let processedCommands = ResizeArray<CommandId> state.ProcessedCommands
    let pendingBarChits = ResizeArray<PendingBarChitResolution> state.PendingBarChits
    let events = ResizeArray<PendingMatchEvent>()
    let random = DeterministicRandom state.Random

    member val Revision = state.Revision with get, set
    member val LastEventSequence = state.LastEventSequence with get, set
    member val Phase = state.Phase with get, set
    member val ActivePlayer = state.ActivePlayer with get, set
    member val RoundNumber = state.RoundNumber with get, set
    member val RoundUsage = state.RoundUsage with get, set
    member val PendingEffect = state.PendingEffect with get, set
    member val PendingKnockout = state.PendingKnockout with get, set
    member val ReplacementPlayer = state.ReplacementPlayer with get, set
    member val PendingRoundEnd = state.PendingRoundEnd with get, set
    member val Winner = state.Winner with get, set
    member val SuddenDeathCount = state.SuddenDeathCount with get, set

    member _.Id = state.Id
    member _.AuthorityVersion = state.AuthorityVersion
    member _.Seed = state.Seed
    member _.OpeningPlayer = state.OpeningPlayer
    member _.Random = random
    member _.Events = events
    member _.Players = players :> seq<PlayerState>
    member _.Cards = cards :> seq<CardState>
    member _.Effects = effects :> seq<TemporaryEffect>
    member _.ProcessedCommands = processedCommands :> seq<CommandId>
    member _.PendingBarChits = pendingBarChits :> seq<PendingBarChitResolution>

    member _.QueueBarChit(pending: PendingBarChitResolution) = pendingBarChits.Add pending

    member _.RemoveBarChit(pending: PendingBarChitResolution) =
        pendingBarChits.Remove pending |> ignore

    member _.RecordCommand(command: CommandId) = processedCommands.Add command

    member _.Player(id: PlayerId) =
        players |> Seq.find (fun player -> player.Id = id)

    member _.SetPlayer(player: PlayerState) =
        players[players.FindIndex(fun candidate -> candidate.Id = player.Id)] <- player

    member _.Other(player: PlayerId) =
        (players |> Seq.find (fun candidate -> candidate.Id <> player)).Id

    member _.Card(id: CardInstanceId) =
        cards |> Seq.find (fun card -> card.Id = id)

    member _.FindCard(id: CardInstanceId) =
        cards |> Seq.tryFind (fun card -> card.Id = id) |> ValueOption.ofOption

    member _.CardsIn(player: PlayerId, zone: CardZone) =
        cards
        |> Seq.filter (fun card -> card.Owner = player && card.Zone = zone)
        |> Seq.sortBy (fun card -> card.StackPosition, card.Id)
        |> Seq.toArray
        :> seq<CardState>

    member _.Oche(player: PlayerId) =
        cards
        |> Seq.tryFind (fun card -> card.Owner = player && card.Zone = CardZone.Oche)
        |> ValueOption.ofOption

    member _.SetCard(card: CardState) =
        cards[cards.FindIndex(fun candidate -> candidate.Id = card.Id)] <- card

    member _.RemoveEffect(effect: TemporaryEffect) = effects.Remove effect |> ignore

    member _.RemoveEffects(sourceEffect: EffectId, sourceCard: CardInstanceId) =
        effects.RemoveAll(fun effect ->
            effect.SourceEffect = sourceEffect && effect.SourceCard = sourceCard)
        |> ignore

    member _.ExpireEffects(completedRound: int) =
        effects.RemoveAll(fun effect ->
            effect.Duration <> EffectDuration.WhileSourceInPlay
            && effect.Duration <> EffectDuration.WhileTargetInPlay
            && effect.Duration <> EffectDuration.CurrentResolution
            && effect.ExpiresAfterRound <= completedRound)
        |> ignore

    member _.RemoveEffectsFor(card: CardInstanceId, preserveDelayedTarget: bool) =
        effects.RemoveAll(fun effect ->
            (effect.SourceCard = card
             && effect.Kind <> TemporaryEffectKind.EndRoundEffect
             && effect.Kind <> TemporaryEffectKind.ForceBeerMatBlank
             && effect.Duration <> EffectDuration.WhileTargetInPlay)
            || (effect.TargetCard = ValueSome card
                && (not preserveDelayedTarget || effect.Kind <> TemporaryEffectKind.EndRoundEffect)))
        |> ignore

    member this.RemoveEffectsFor(card: CardInstanceId) = this.RemoveEffectsFor(card, false)

    member this.AddEffect(effect: TemporaryEffect) =
        effects.Add effect

        events.Add
            { PendingMatchEvent.ofKind MatchEventKind.EffectRegistered with
                Actor = ValueSome effect.Owner
                SourceCard = ValueSome effect.SourceCard
                TargetCards =
                    match effect.TargetCard with
                    | ValueSome target -> ImmutableArray.Create target
                    | ValueNone -> ImmutableArray<_>.Empty
                Effect = ValueSome effect.SourceEffect
                Amount = effect.Amount }

    member this.MoveCard(id: CardInstanceId, zone: CardZone, attachedTo: CardInstanceId voption) =
        let card = this.Card id

        // A card arriving on the Booth stands at the end of it, because a Booth is in the order its
        // cards were put down and nothing else. Left at -1 they all tie, and the tiebreak is the
        // card's identity - which is its position in the Deck it was built from, so a Booth read
        // itself out in the order the deck list was written rather than the order it was played.
        //
        // Every other zone keeps -1. The Deck and the Bar Chits number themselves where they are
        // dealt, and nowhere else has an order worth reading.
        this.SetCard
            { card with
                Zone = zone
                IsFaceDown = zone = CardZone.BarChit
                StackPosition =
                    if zone = CardZone.Booth then
                        this.Beneath(card.Owner, CardZone.Booth)
                    else
                        -1
                AttachedTo = attachedTo }

        events.Add(
            PendingMatchEvent.forCards
                MatchEventKind.CardMoved
                card.Owner
                id
                (ImmutableArray.Create id)
        )

    member this.MoveCard(id: CardInstanceId, zone: CardZone) = this.MoveCard(id, zone, ValueNone)

    member this.Draw(player: PlayerId, count: int, reason: DrawReason) =
        let drawn =
            this.CardsIn(player, CardZone.Stack)
            |> Seq.truncate count
            |> Seq.map (fun card -> card.Id)
            |> Seq.toArray

        for id in drawn do
            this.MoveCard(id, CardZone.Mitt)

        if drawn.Length > 0 then
            events.Add
                { PendingMatchEvent.ofKind MatchEventKind.CardsDrawn with
                    Actor = ValueSome player
                    TargetCards = ImmutableArray.CreateRange drawn
                    DrawReason = ValueSome reason
                    Amount = drawn.Length }

        ImmutableArray.CreateRange drawn

    // A shuffle answers to the cards in the Deck and to the random stream, and to nothing else.
    //
    // It used to answer to their POSITIONS as well. The cards were taken in the order CardsIn gives
    // them, which is by position, and a Fisher-Yates walk over a differently ordered input deals a
    // different Deck out of the same random draws. That made where a card had been sitting before a
    // shuffle decide what the shuffle produced - so correcting any position written before one, even
    // a position nothing reads and the shuffle is about to overwrite, silently changed the deal for
    // every seed. Ordering the input by identity instead settles that: a Deck holding the same cards
    // shuffles the same way whatever order it was holding them in.
    member this.Shuffle(player: PlayerId, excludedCards: ImmutableArray<CardInstanceId>) =
        let stack =
            this.CardsIn(player, CardZone.Stack)
            |> Seq.filter (fun card -> not (Seq.contains card.Id excludedCards))
            |> Seq.sortBy (fun card -> card.Id)
            |> Seq.toArray

        for index in stack.Length - 1 .. -1 .. 1 do
            let swapIndex = random.NextInt(index + 1)
            let held = stack[index]
            stack[index] <- stack[swapIndex]
            stack[swapIndex] <- held

        for index in 0 .. stack.Length - 1 do
            this.SetCard
                { stack[index] with
                    StackPosition = index }

        events.Add(PendingMatchEvent.forActor MatchEventKind.CardsShuffled player)

    member this.Shuffle(player: PlayerId) =
        this.Shuffle(player, ImmutableArray<_>.Empty)

    // Where a card put UNDER the Deck belongs: one past the last card still in it.
    //
    // That is not the number of cards in it, and the two only agree for a Deck nobody has drawn
    // from. Drawing takes cards off the front, so a Deck of sixty dealt seven has fifty-three cards
    // left occupying positions seven to fifty-nine - and its count, fifty-three, is a position six
    // cards are still sitting on. Written there a card interleaves with the bottom of the Deck
    // instead of going beneath it, and is drawn before cards it was put underneath.
    //
    // Folding from zero is also what answers an empty Deck, which is reachable: a player may hold
    // their whole Deck, and asking an empty Deck for its last position has no answer to give.
    member this.BeneathStack(player: PlayerId) = this.Beneath(player, CardZone.Stack)

    // The same question asked of any zone that keeps an order: one past the last card in it.
    member private this.Beneath(player: PlayerId, zone: CardZone) =
        this.CardsIn(player, zone)
        |> Seq.fold (fun beneath card -> max beneath (card.StackPosition + 1)) 0

    member this.ReturnMittToStack(player: PlayerId) =
        let mutable nextPosition = this.BeneathStack player

        for card in this.CardsIn(player, CardZone.Mitt) |> Seq.toArray do
            this.SetCard
                { card with
                    Zone = CardZone.Stack
                    StackPosition = nextPosition }

            nextPosition <- nextPosition + 1

    member this.SetAsideBarChits(player: PlayerId, count: int) =
        let cards =
            this.CardsIn(player, CardZone.Stack) |> Seq.truncate count |> Seq.toArray

        for index in 0 .. cards.Length - 1 do
            this.MoveCard(cards[index].Id, CardZone.BarChit)

            this.SetCard
                { this.Card cards[index].Id with
                    StackPosition = index }

        let current = this.Player player

        this.SetPlayer
            { current with
                BarChitsRemaining = cards.Length }

    member this.TakeBarChits(player: PlayerId, count: int, source: CardInstanceId) =
        let cards =
            this.CardsIn(player, CardZone.BarChit) |> Seq.truncate count |> Seq.toArray

        for card in cards do
            this.MoveCard(card.Id, CardZone.Mitt)

        let current = this.Player player

        this.SetPlayer
            { current with
                BarChitsRemaining = current.BarChitsRemaining - cards.Length }

        let taken = ImmutableArray.CreateRange(cards |> Array.map (fun card -> card.Id))

        events.Add
            { PendingMatchEvent.forCards MatchEventKind.BarChitsTaken player source taken with
                Amount = cards.Length }

        taken

    member this.ResetBarChits(player: PlayerId, count: int) =
        let mutable nextPosition = this.BeneathStack player

        for card in this.CardsIn(player, CardZone.BarChit) |> Seq.toArray do
            this.MoveCard(card.Id, CardZone.Stack)

            this.SetCard
                { this.Card card.Id with
                    StackPosition = nextPosition }

            nextPosition <- nextPosition + 1

        this.Shuffle player
        this.SetAsideBarChits(player, count)

    member this.Attach(attachmentId: CardInstanceId, targetId: CardInstanceId) =
        let target = this.Card targetId
        this.MoveCard(attachmentId, CardZone.Attached, ValueSome targetId)

        this.SetCard
            { target with
                Attachments =
                    ImmutableArray.CreateRange(Seq.append target.Attachments [ attachmentId ]) }

    member this.DetachTo(attachmentId: CardInstanceId, zone: CardZone) =
        match (this.Card attachmentId).AttachedTo with
        | ValueSome targetId ->
            let target = this.Card targetId

            this.SetCard
                { target with
                    Attachments =
                        ImmutableArray.CreateRange(
                            target.Attachments |> Seq.filter (fun id -> id <> attachmentId)
                        ) }
        | ValueNone -> ()

        this.MoveCard(attachmentId, zone)

    member this.PlaceDamage
        (
            actor: PlayerId,
            targetId: CardInstanceId,
            damage: int,
            kind: DamageKind,
            source: CardInstanceId voption
        ) =
        if damage > 0 then
            let target = this.Card targetId

            this.SetCard
                { target with
                    Damage = target.Damage + damage }

            events.Add
                { PendingMatchEvent.ofKind MatchEventKind.DamagePlaced with
                    Actor = ValueSome actor
                    SourceCard = source
                    TargetCards = ImmutableArray.Create targetId
                    DamageKind = ValueSome kind
                    Amount = damage }

    member this.PlaceDamage
        (actor: PlayerId, targetId: CardInstanceId, damage: int, kind: DamageKind)
        =
        this.PlaceDamage(actor, targetId, damage, kind, ValueNone)

    member this.Heal
        (actor: PlayerId, targetId: CardInstanceId, amount: int, source: CardInstanceId voption)
        =
        let target = this.Card targetId
        let healed = min amount target.Damage

        if healed > 0 then
            this.SetCard
                { target with
                    Damage = target.Damage - healed }

            events.Add
                { PendingMatchEvent.ofKind MatchEventKind.DamageHealed with
                    Actor = ValueSome actor
                    SourceCard = source
                    TargetCards = ImmutableArray.Create targetId
                    Amount = healed }

    member this.ApplyRoughState
        (
            actor: PlayerId,
            targetId: CardInstanceId,
            state: BlokemonRoughState,
            source: CardInstanceId voption
        ) =
        let target = this.Card targetId

        if
            target.Zone = CardZone.Oche
            && not (target.Kind = CardKind.Kit && catalog.IsFossil target.MechanicalId)
        then
            let rotated = catalog.Manifest.BaseRules.RoughStateCoexistence.RotatedGroup

            let states =
                ResizeArray<RoughStateEntry>(
                    target.RoughStates |> Seq.filter (fun entry -> entry.State <> state)
                )

            if Array.contains state rotated then
                states.RemoveAll(fun entry -> Array.contains entry.State rotated) |> ignore

            states.Add
                { State = state
                  AppliedAtOwnerRound = (this.Player target.Owner).RoundsStarted }

            this.SetCard
                { target with
                    RoughStates = ImmutableArray.CreateRange states }

            events.Add
                { PendingMatchEvent.ofKind MatchEventKind.RoughStateApplied with
                    Actor = ValueSome actor
                    SourceCard = source
                    TargetCards = ImmutableArray.Create targetId
                    RoughState = ValueSome state }

    member this.ClearRoughStates
        (actor: PlayerId, targetId: CardInstanceId, state: BlokemonRoughState voption)
        =
        let target = this.Card targetId

        let cleared =
            match state with
            | ValueNone -> target.RoughStates |> Seq.toArray
            | ValueSome value ->
                target.RoughStates
                |> Seq.filter (fun entry -> entry.State = value)
                |> Seq.toArray

        if cleared.Length > 0 then
            this.SetCard
                { target with
                    RoughStates =
                        match state with
                        | ValueNone -> ImmutableArray<_>.Empty
                        | ValueSome value ->
                            ImmutableArray.CreateRange(
                                target.RoughStates |> Seq.filter (fun entry -> entry.State <> value)
                            ) }

            for entry in cleared do
                events.Add
                    { PendingMatchEvent.ofKind MatchEventKind.RoughStateCleared with
                        Actor = ValueSome actor
                        TargetCards = ImmutableArray.Create targetId
                        RoughState = ValueSome entry.State }

    member this.ClearRoughStates(actor: PlayerId, targetId: CardInstanceId) =
        this.ClearRoughStates(actor, targetId, ValueNone)

    member this.TossBeerMat(player: PlayerId, applyPlayerEffects: bool) =
        let badge = random.NextInt 2 = 1

        if
            applyPlayerEffects
            && effects
               |> Seq.exists (fun effect ->
                   effect.Owner <> player
                   && effect.Kind = TemporaryEffectKind.ForceBeerMatBlank
                   && effect.AppliesFromRound <= this.RoundNumber)
        then
            false
        else
            badge

    member this.TossBeerMat(player: PlayerId) = this.TossBeerMat(player, true)

    member this.ChuckBloke(id: CardInstanceId) =
        let card = this.Card id

        let chucked =
            Seq.append (Seq.append card.Attachments card.UnderlyingCards) [ id ]
            |> Seq.distinct
            |> Seq.toArray

        for cardId in chucked do
            let current = this.Card cardId

            this.SetCard
                { current with
                    Zone = CardZone.EmptiesTray
                    StackPosition = -1
                    AttachedTo = ValueNone
                    Attachments = ImmutableArray<_>.Empty
                    UnderlyingCards = ImmutableArray<_>.Empty
                    RoughStates = ImmutableArray<_>.Empty }

            this.RemoveEffectsFor cardId

        ImmutableArray.CreateRange chucked

    member this.Snapshot() : MatchState =
        { Id = state.Id
          AuthorityVersion = state.AuthorityVersion
          Seed = state.Seed
          Random = random.Snapshot
          Revision = this.Revision
          LastEventSequence = this.LastEventSequence
          Phase = this.Phase
          OpeningPlayer = state.OpeningPlayer
          ActivePlayer = this.ActivePlayer
          RoundNumber = this.RoundNumber
          Players = ImmutableArray.CreateRange players
          Cards = ImmutableArray.CreateRange(cards |> Seq.sortBy (fun card -> card.Id))
          Effects =
            ImmutableArray.CreateRange(
                effects
                |> Seq.sortWith (fun left right ->
                    match
                        String.CompareOrdinal(left.SourceEffect.Value, right.SourceEffect.Value)
                    with
                    | 0 -> compare left.SourceCard right.SourceCard
                    | order -> order)
            )
          ProcessedCommands = ImmutableArray.CreateRange processedCommands
          RoundUsage = this.RoundUsage
          PendingEffect = this.PendingEffect
          PendingKnockout = this.PendingKnockout
          PendingBarChits = ImmutableArray.CreateRange pendingBarChits
          ReplacementPlayer = this.ReplacementPlayer
          PendingRoundEnd = this.PendingRoundEnd
          Winner = this.Winner
          SuddenDeathCount = this.SuddenDeathCount }
