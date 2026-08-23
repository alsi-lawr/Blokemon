namespace Blokemon.Game

open System
open System.Collections.Immutable
open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectDamage

/// What a handler returns: either accepted, or a rejection plus the requirements the caller still
/// has to answer.
type internal HandlerResult =
    { Rejection: CommandRejectionCode voption
      Requirements: ImmutableArray<ChoiceRequirement> }

[<RequireQualifiedAccess>]
module internal HandlerResult =

    let accepted =
        { Rejection = ValueNone
          Requirements = ImmutableArray<_>.Empty }

    let reject (rejection: CommandRejectionCode) =
        { Rejection = ValueSome rejection
          Requirements = ImmutableArray<_>.Empty }

    let rejectWith
        (rejection: CommandRejectionCode)
        (requirements: ImmutableArray<ChoiceRequirement>)
        =
        { Rejection = ValueSome rejection
          Requirements = requirements }

/// The rule questions the engine asks that do not change anything: deck legality, turn legality,
/// what an effect costs and what it is worth after modifiers.
module internal MatchRules =

    let isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    /// A player who started over waits for the one who did not to finish setting up. Fewest
    /// mulligans places first, and an equal count leaves both free to place in either order.
    let mayPlaceOpening (state: MatchState) (actor: PlayerId) =
        let fewest = state.Players |> Seq.map _.MulliganCount |> Seq.min

        (state.Player actor).MulliganCount = fewest
        || state.Players
           |> Seq.filter (fun player -> player.MulliganCount = fewest)
           |> Seq.forall _.OpeningChosen

    let bonusBenchable (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        let player = state.Player actor

        if player.BonusPlacementChosen then
            Array.empty
        else
            let room =
                catalog.Manifest.BaseRules.Opening.BoothLimit
                - (state.CardsIn(actor, CardZone.Booth) |> Seq.length)

            if room <= 0 then
                Array.empty
            else
                player.BonusDrawn
                |> Seq.map state.Card
                |> Seq.filter (fun card ->
                    card.Zone = CardZone.Mitt
                    && card.Kind = CardKind.Bloke
                    && catalog.IsRegular card.MechanicalId)
                |> Seq.toArray

    let rec containsOpcode (program: BlokemonEffectInstruction array) (opcode: BlokemonOpcode) =
        program
        |> Array.exists (fun instruction ->
            instruction.Opcode = opcode
            || containsOpcode instruction.Then opcode
            || containsOpcode instruction.Otherwise opcode)

    let rec containsCondition
        (program: BlokemonEffectInstruction array)
        (condition: BlokemonCondition)
        =
        program
        |> Array.exists (fun instruction ->
            (instruction.Predicates
             |> Array.exists (fun predicate -> predicate.Condition = condition))
            || containsCondition instruction.Then condition
            || containsCondition instruction.Otherwise condition)

    let rec private flattenProgram (program: BlokemonEffectInstruction array) =
        program
        |> Seq.collect (fun instruction ->
            Seq.append
                (Seq.singleton instruction)
                (Seq.append
                    (flattenProgram instruction.Then)
                    (flattenProgram instruction.Otherwise)))

    let isDeclarativeHouseRule (rule: BlokemonHouseRule) =
        flattenProgram rule.Program
        |> Seq.forall (fun instruction ->
            instruction.Opcode = BlokemonOpcode.Conditional
            || instruction.Opcode = BlokemonOpcode.ContinuousPartyTrick)

    let effectiveStayingPower (catalog: AuthorityCatalog) (card: CardState) =
        catalog.StayingPower card

    let effectiveTaxiFare (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        let mutable fare = catalog.TaxiFare card

        for modifier in
            builder.Effects
            |> Seq.filter (fun effect ->
                (effect.TargetCard = ValueSome card.Id || effect.TargetCard.IsNone)
                && effect.Kind = TemporaryEffectKind.ModifyTaxiFare
                && effectMatchesCardRank catalog effect card)
            |> Seq.toArray do
            let continuous =
                match catalog.PartyTrick modifier.SourceEffect with
                | ValueSome trick -> trick.Trigger = BlokemonTrigger.Continuous
                | ValueNone -> false

            fare <-
                if modifier.MechanicalTypes.Length = 0 || continuous then
                    0
                else
                    max 0 (fare + modifier.Amount)

        fare

    let canPayAttack
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (attacker: CardState)
        (attack: BlokemonAttack)
        =
        if
            builder.Effects
            |> Seq.exists (fun effect ->
                effect.TargetCard = ValueSome attacker.Id
                && effect.Kind = TemporaryEffectKind.ModifyAttackCost
                && effectMatchesCardRank catalog effect attacker
                && effect.Amount < 0)
        then
            true
        else
            let costs = ResizeArray<BlokemonMechanicalType> attack.VimCost

            for effect in
                builder.Effects
                |> Seq.filter (fun effect ->
                    effect.TargetCard = ValueSome attacker.Id
                    && effect.Kind = TemporaryEffectKind.ModifyAttackCost
                    && effectMatchesCardRank catalog effect attacker
                    && effect.Amount > 0)
                |> Seq.toArray do
                if effect.MechanicalTypes.Length = 0 then
                    costs.AddRange(Seq.replicate effect.Amount BlokemonMechanicalType.Colorless)
                else
                    costs.AddRange effect.MechanicalTypes

            let available =
                ResizeArray<BlokemonMechanicalType>(
                    attacker.Attachments
                    |> Seq.map builder.Card
                    |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                    |> Seq.map (fun card -> (catalog.Vim card.MechanicalId).MechanicalType)
                )

            let mutable payable = true

            for typedCost in
                costs
                |> Seq.filter (fun cost -> cost <> BlokemonMechanicalType.Colorless)
                |> Seq.toArray do
                if payable then
                    let index = available.FindIndex(fun vim -> vim = typedCost)

                    if index < 0 then
                        payable <- false
                    else
                        available.RemoveAt index

            payable
            && available.Count
               >= (costs
                   |> Seq.filter (fun cost -> cost = BlokemonMechanicalType.Colorless)
                   |> Seq.length)

    let validatePlayingTurn (builder: MatchBuilder) (actor: PlayerId) =
        if builder.Phase <> MatchPhase.Playing then
            ValueSome CommandRejectionCode.WrongPhase
        elif builder.ActivePlayer <> actor then
            ValueSome CommandRejectionCode.NotActorsTurn
        else
            ValueNone

    let validateCommandBoundary
        (catalog: AuthorityCatalog)
        (state: MatchState)
        (command: MatchCommand)
        =
        if command.MatchId <> state.Id then
            ValueSome CommandRejectionCode.WrongMatch
        elif Seq.contains command.Id state.ProcessedCommands then
            ValueSome CommandRejectionCode.DuplicateCommand
        elif command.ExpectedRevision <> state.Revision then
            ValueSome CommandRejectionCode.StaleRevision
        elif
            not (
                StringComparer.Ordinal.Equals(
                    state.AuthorityVersion,
                    catalog.Manifest.ManifestVersion
                )
            )
        then
            ValueSome CommandRejectionCode.AuthorityMismatch
        elif not (state.Players |> Seq.exists (fun player -> player.Id = command.Actor)) then
            ValueSome CommandRejectionCode.UnknownActor
        elif state.Phase = MatchPhase.Complete then
            ValueSome CommandRejectionCode.MatchComplete
        else
            ValueNone

    let private validateDeck
        (catalog: AuthorityCatalog)
        (deck: FrozenDeckSnapshot)
        (issues: ResizeArray<DeckIssue>)
        =
        let issue code player card actual expected =
            { Code = code
              Player = player
              Card = card
              Actual = actual
              Expected = expected }

        if deck.Cards.Length <> catalog.Manifest.BaseRules.Stack.CardCount then
            issues.Add(
                issue
                    DeckIssueCode.WrongCardCount
                    (ValueSome deck.Owner)
                    ValueNone
                    deck.Cards.Length
                    catalog.Manifest.BaseRules.Stack.CardCount
            )

        for unknown in
            deck.Cards
            |> Seq.filter (fun card -> not (catalog.Contains card))
            |> Seq.distinct do
            issues.Add(
                issue
                    DeckIssueCode.UnknownMechanicalCard
                    (ValueSome deck.Owner)
                    (ValueSome unknown)
                    0
                    0
            )

        for group in deck.Cards |> Seq.filter catalog.Contains |> Seq.groupBy id do
            let card, copies = group
            let limit = catalog.CopyLimit card
            let count = Seq.length copies

            if count > limit then
                issues.Add(
                    issue
                        DeckIssueCode.TooManyCopies
                        (ValueSome deck.Owner)
                        (ValueSome card)
                        count
                        limit
                )

        if not (deck.Cards |> Seq.filter catalog.Contains |> Seq.exists catalog.IsRegular) then
            issues.Add(issue DeckIssueCode.MissingRegularBloke (ValueSome deck.Owner) ValueNone 0 1)

    let validateStart
        (catalog: AuthorityCatalog)
        (authorityIsValid: bool)
        (request: MatchStartRequest)
        =
        let issues = ResizeArray<DeckIssue>()

        if not authorityIsValid then
            issues.Add
                { Code = DeckIssueCode.AuthorityInvalid
                  Player = ValueNone
                  Card = ValueNone
                  Actual = 0
                  Expected = 0 }
        else
            if String.IsNullOrWhiteSpace request.MatchId.Value then
                issues.Add
                    { Code = DeckIssueCode.InvalidMatchId
                      Player = ValueNone
                      Card = ValueNone
                      Actual = 0
                      Expected = 0 }

            if
                String.IsNullOrWhiteSpace request.FirstDeck.Owner.Value
                || String.IsNullOrWhiteSpace request.SecondDeck.Owner.Value
            then
                issues.Add
                    { Code = DeckIssueCode.InvalidPlayerId
                      Player = ValueNone
                      Card = ValueNone
                      Actual = 0
                      Expected = 0 }

            if request.FirstDeck.Owner = request.SecondDeck.Owner then
                issues.Add
                    { Code = DeckIssueCode.DuplicatePlayer
                      Player = ValueSome request.FirstDeck.Owner
                      Card = ValueNone
                      Actual = 2
                      Expected = 1 }

            validateDeck catalog request.FirstDeck issues
            validateDeck catalog request.SecondDeck issues

        issues

    let createCards (catalog: AuthorityCatalog) (deck: FrozenDeckSnapshot) (playerNumber: int) =
        [| for index in 0 .. deck.Cards.Length - 1 ->
               let mechanicalId = deck.Cards[index]

               { Id = CardInstanceId $"C{playerNumber}-%03d{index + 1}"
                 MechanicalId = mechanicalId
                 Owner = deck.Owner
                 Kind = catalog.Kind mechanicalId
                 Zone = CardZone.Stack
                 IsFaceDown = false
                 StackPosition = index
                 AttachedTo = ValueNone
                 Attachments = ImmutableArray<_>.Empty
                 UnderlyingCards = ImmutableArray<_>.Empty
                 Damage = 0
                 RoughStates = ImmutableArray<_>.Empty
                 EnteredAtOwnerRound = 0
                 LastPromotedRound = -1 } |]
