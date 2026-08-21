namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open System.IO
open Blokemon.Core.SetDesign
open Blokemon.Game

/// The shared table every test starts from: the printed authority, a legal deck, and a mid-match
/// state posed so a single attack can be inspected in isolation.
module MatchScenario =

    let FirstPlayer = PlayerId "first"
    let SecondPlayer = PlayerId "second"

    let Authority =
        BlokemonSetJson.RuntimeManifest(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")
            )
        )

    let Engine () = MatchEngine Authority

    let RegularDeck (owner: PlayerId) =
        let cards =
            Authority.Collectibles
            |> Seq.filter (fun card -> card.Rank = BlokemonRank.Regular)
            |> Seq.truncate 15
            |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

        FrozenDeckSnapshot.Create(owner, cards)

    let StartRequestWithSeed (seed: uint64) =
        { MatchId = MatchId "match"
          Seed = MatchSeed seed
          FirstDeck = RegularDeck FirstPlayer
          SecondDeck = RegularDeck SecondPlayer }

    let StartRequest () = StartRequestWithSeed 0xB10CEUL

    let Card
        (id: string)
        (mechanicalId: string)
        (owner: PlayerId)
        (zone: CardZone)
        (stackPosition: int)
        (attachments: ImmutableArray<CardInstanceId>)
        (roughStates: ImmutableArray<RoughStateEntry>)
        (attachedTo: CardInstanceId voption)
        =
        let kind =
            if Authority.Collectibles |> Array.exists (fun card -> card.Id = mechanicalId) then
                CardKind.Bloke
            elif Authority.Kits |> Array.exists (fun card -> card.Id = mechanicalId) then
                CardKind.Kit
            else
                CardKind.Vim

        { Id = CardInstanceId id
          MechanicalId = MechanicalCardId mechanicalId
          Owner = owner
          Kind = kind
          Zone = zone
          IsFaceDown = zone = CardZone.BarChit
          StackPosition = stackPosition
          AttachedTo = attachedTo
          Attachments = attachments
          UnderlyingCards = ImmutableArray<_>.Empty
          Damage = 0
          RoughStates = roughStates
          EnteredAtOwnerRound = 1
          LastPromotedRound = -1 }

    let PlainCard (id: string) (mechanicalId: string) (owner: PlayerId) (zone: CardZone) position =
        Card
            id
            mechanicalId
            owner
            zone
            position
            ImmutableArray<_>.Empty
            ImmutableArray<_>.Empty
            ValueNone

    let AttachedCard
        (id: string)
        (mechanicalId: string)
        (owner: PlayerId)
        (zone: CardZone)
        position
        (attachedTo: CardInstanceId)
        =
        Card
            id
            mechanicalId
            owner
            zone
            position
            ImmutableArray<_>.Empty
            ImmutableArray<_>.Empty
            (ValueSome attachedTo)

    let RoughState (state: BlokemonRoughState) (appliedAtOwnerRound: int) =
        { State = state
          AppliedAtOwnerRound = appliedAtOwnerRound }

    /// Replaces the cards with the given identities and keeps the collection in identity order, which
    /// is the ordering every state the engine produces is already in.
    let WithCards (state: MatchState) (replaced: CardState seq) =
        let replaced = replaced |> Seq.toArray
        let replacedIds = replaced |> Array.map (fun card -> card.Id) |> Set.ofArray

        { state with
            Cards =
                ImmutableArray.CreateRange(
                    state.Cards
                    |> Seq.filter (fun card -> not (replacedIds.Contains card.Id))
                    |> Seq.append replaced
                    |> Seq.sortBy (fun card -> card.Id)
                ) }

    let BattleStateWith
        (attacker: string)
        (defender: string)
        (attachedVim: string seq)
        (randomSeed: uint64)
        (attackerRoughStates: ImmutableArray<RoughStateEntry>)
        (defenderRoughStates: ImmutableArray<RoughStateEntry>)
        (effects: ImmutableArray<TemporaryEffect>)
        =
        let attachedVim = attachedVim |> Seq.toArray

        let attackerCard =
            Card
                "attacker"
                attacker
                FirstPlayer
                CardZone.Oche
                -1
                (ImmutableArray.CreateRange(
                    attachedVim |> Seq.mapi (fun index _ -> CardInstanceId $"vim-{index}")
                ))
                attackerRoughStates
                ValueNone

        let defenderCard =
            Card
                "defender"
                defender
                SecondPlayer
                CardZone.Oche
                -1
                ImmutableArray<_>.Empty
                defenderRoughStates
                ValueNone

        let vim =
            attachedVim
            |> Seq.mapi (fun index mechanicalId ->
                Card
                    $"vim-{index}"
                    mechanicalId
                    FirstPlayer
                    CardZone.Attached
                    -1
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    (ValueSome(CardInstanceId "attacker")))

        let cards =
            Seq.append
                [ attackerCard
                  defenderCard
                  PlainCard "first-draw" "VIM-BLAZED" FirstPlayer CardZone.Stack 0
                  PlainCard "second-draw" "VIM-SOBER" SecondPlayer CardZone.Stack 0 ]
                vim

        { Id = MatchId "battle"
          AuthorityVersion = Authority.ManifestVersion
          Seed = MatchSeed randomSeed
          Random =
            { State = randomSeed
              ConsumptionIndex = 0 }
          Revision = MatchRevision 7
          LastEventSequence = 0L
          Phase = MatchPhase.Playing
          OpeningPlayer = SecondPlayer
          ActivePlayer = FirstPlayer
          RoundNumber = 4
          Players =
            ImmutableArray.Create(
                { Id = FirstPlayer
                  BarChitsRemaining = 6
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = ImmutableArray<_>.Empty
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 2 },
                { Id = SecondPlayer
                  BarChitsRemaining = 6
                  MulliganCount = 0
                  MulliganBonusAllowance = 0
                  MulliganBonusChosen = true
                  BonusDrawn = ImmutableArray<_>.Empty
                  BonusPlacementChosen = true
                  OpeningChosen = true
                  RoundsStarted = 2 }
            )
          Cards = ImmutableArray.CreateRange(cards |> Seq.sortBy (fun card -> card.Id))
          Effects = effects
          ProcessedCommands = ImmutableArray<_>.Empty
          RoundUsage = RoundUsage.Empty FirstPlayer
          PendingEffect = ValueNone
          PendingKnockout = ValueNone
          PendingBarChits = ImmutableArray<_>.Empty
          ReplacementPlayer = ValueNone
          PendingRoundEnd = false
          Winner = ValueNone
          SuddenDeathCount = 0 }

    let BattleState attacker defender attachedVim randomSeed =
        BattleStateWith
            attacker
            defender
            attachedVim
            randomSeed
            ImmutableArray<_>.Empty
            ImmutableArray<_>.Empty
            ImmutableArray<_>.Empty

    let WithBarChits (state: MatchState) (player: PlayerId) (remaining: int) =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun current ->
                        if current.Id = player then
                            { current with
                                BarChitsRemaining = remaining }
                        else
                            current)
                ) }

    let Chosen (decision: CpuDecision) =
        match decision with
        | CpuDecision.Selected action -> action
        | CpuDecision.NoLegalAction -> failwith "Expected a legal action to be available."

    let Command (state: MatchState) (id: string) (actor: PlayerId) choices action =
        { Id = CommandId id
          MatchId = state.Id
          Actor = actor
          ExpectedRevision = state.Revision
          Choices = choices
          Action = action }

    let AttackCommandWith (state: MatchState) (effect: string) choices =
        Command
            state
            $"command:{effect}"
            FirstPlayer
            choices
            (MatchAction.Attack(CardInstanceId "attacker", EffectId effect))

    let AttackCommand (state: MatchState) (effect: string) =
        AttackCommandWith state effect ImmutableArray<_>.Empty

    let ResolveEffectChoiceCommandBy (state: MatchState) choices (actor: PlayerId) =
        Command
            state
            $"resolve:{state.Revision.Value}"
            actor
            choices
            MatchAction.ResolveEffectChoice

    let ResolveEffectChoiceCommand (state: MatchState) choices =
        ResolveEffectChoiceCommandBy state choices state.PendingEffect.Value.Chooser

    let Applied (outcome: CommandOutcome) =
        match outcome with
        | CommandOutcome.Applied(state, _) -> state
        | CommandOutcome.Rejected(_, rejection) ->
            failwith $"The command was rejected with {rejection.Code}."

    let AppliedWith (outcome: CommandOutcome) =
        match outcome with
        | CommandOutcome.Applied(state, events) -> state, events
        | CommandOutcome.Rejected(_, rejection) ->
            failwith $"The command was rejected with {rejection.Code}."

    let Rejected (outcome: CommandOutcome) =
        match outcome with
        | CommandOutcome.Rejected(state, rejection) -> state, rejection
        | CommandOutcome.Applied _ -> failwith "Expected the command to be rejected."

    let RejectionCode outcome = (Rejected outcome |> snd).Code

    let StartRejected (outcome: MatchStartOutcome) =
        match outcome with
        | MatchStartOutcome.Rejected issues -> issues
        | MatchStartOutcome.Started _ -> failwith "Expected the start to be rejected."

    let Started (outcome: MatchStartOutcome) =
        match outcome with
        | MatchStartOutcome.Started(state, _) -> state
        | MatchStartOutcome.Rejected issues -> failwith $"The start was rejected: {issues.Length}."
