namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Game.CpuCandidateIds

module internal CpuObservations =

    let private isPublicZone zone =
        match zone with
        | CardZone.Oche
        | CardZone.Booth
        | CardZone.Attached
        | CardZone.EmptiesTray
        | CardZone.Local -> true
        | CardZone.Stack
        | CardZone.Mitt
        | CardZone.BarChit -> false
        | other -> failwithf "Unhandled card zone %A." other

    let private fairCardIsKnown
        (canRevealHand: PlayerId -> bool)
        (actor: PlayerId)
        (card: CardState)
        =
        not card.IsFaceDown
        && (isPublicZone card.Zone
            || (card.Zone = CardZone.Mitt && (card.Owner = actor || canRevealHand card.Owner)))

    let private redactRequirement
        (cardIsKnown: CardInstanceId -> bool)
        (requirement: ChoiceRequirement)
        =
        let eligibleCards =
            requirement.EligibleCards |> Seq.filter cardIsKnown |> Seq.toArray

        let eligibleTargets =
            requirement.EligibleTargets |> Seq.filter cardIsKnown |> Seq.toArray

        { Id = requirement.Id
          Kind = requirement.Kind
          Chooser = requirement.Chooser
          Minimum = requirement.Minimum
          Maximum = requirement.Maximum
          EligibleCards = ImmutableArray.CreateRange eligibleCards
          HiddenEligibleCardCount = requirement.EligibleCards.Length - eligibleCards.Length
          EligibleMechanicalTypes = requirement.EligibleMechanicalTypes
          EligibleEffects = requirement.EligibleEffects
          DependsOnOptional = requirement.DependsOnOptional
          EligibleTargets = ImmutableArray.CreateRange eligibleTargets
          HiddenEligibleTargetCount = requirement.EligibleTargets.Length - eligibleTargets.Length
          RequireDifferentMechanicalTypes = requirement.RequireDifferentMechanicalTypes
          EligibleCardTypes =
            ImmutableArray.CreateRange(
                requirement.EligibleCardTypes
                |> Seq.filter (fun value -> cardIsKnown value.Card)
            )
          PreserveCardOrder = requirement.PreserveCardOrder }

    let private redactChoice (cardIsKnown: CardInstanceId -> bool) choice =
        match choice with
        | EffectChoice.Amount(id, amount) -> CpuChoiceCandidate.Amount(id, amount)
        | EffectChoice.Cards(id, cards) ->
            let known = cards |> Seq.filter cardIsKnown |> Seq.toArray

            CpuChoiceCandidate.Cards(
                id,
                { KnownCards = ImmutableArray.CreateRange known
                  HiddenCardCount = cards.Length - known.Length }
            )
        | EffectChoice.MechanicalType(id, mechanicalType) ->
            CpuChoiceCandidate.MechanicalType(id, mechanicalType)
        | EffectChoice.Attack(id, effect) -> CpuChoiceCandidate.Attack(id, effect)
        | EffectChoice.Attachments(id, placements) ->
            CpuChoiceCandidate.Attachments(
                id,
                ImmutableArray.CreateRange(
                    placements
                    |> Seq.map (fun placement ->
                        { Vim =
                            if cardIsKnown placement.Vim then
                                ValueSome placement.Vim
                            else
                                ValueNone
                          Bloke =
                            if cardIsKnown placement.Bloke then
                                ValueSome placement.Bloke
                            else
                                ValueNone })
                )
            )

    let private publicState
        (state: MatchState)
        (actor: PlayerId)
        (canRevealHand: PlayerId -> bool)
        =
        let cardIsKnown = fairCardIsKnown canRevealHand actor
        let knownCards = state.Cards |> Seq.filter cardIsKnown |> Seq.toArray
        let knownIds = knownCards |> Seq.map _.Id |> Set.ofSeq

        { Id = state.Id
          AuthorityVersion = state.AuthorityVersion
          Revision = state.Revision
          Phase = state.Phase
          OpeningPlayer = state.OpeningPlayer
          ActivePlayer = state.ActivePlayer
          RoundNumber = state.RoundNumber
          Players =
            ImmutableArray.CreateRange(
                state.Players
                |> Seq.map (fun player ->
                    { Id = player.Id
                      BarChitsRemaining = player.BarChitsRemaining
                      MulliganCount = player.MulliganCount
                      MulliganBonusAllowance = player.MulliganBonusAllowance
                      MulliganBonusChosen = player.MulliganBonusChosen
                      BonusDrawnCount = player.BonusDrawn.Length
                      BonusPlacementChosen = player.BonusPlacementChosen
                      OpeningChosen = player.OpeningChosen
                      RoundsStarted = player.RoundsStarted })
            )
          Cards =
            ImmutableArray.CreateRange(
                knownCards
                |> Seq.sortBy (fun card -> card.Owner, card.Zone, card.StackPosition, card.Id)
            )
          HiddenZones =
            ImmutableArray.CreateRange(
                state.Cards
                |> Seq.filter (cardIsKnown >> not)
                |> Seq.groupBy (fun card -> card.Owner, card.Zone)
                |> Seq.map (fun ((owner, zone), cards) ->
                    { Owner = owner
                      Zone = zone
                      Count = Seq.length cards })
                |> Seq.sortBy (fun hidden -> hidden.Owner, hidden.Zone)
            )
          Effects =
            ImmutableArray.CreateRange(
                state.Effects
                |> Seq.filter (fun effect ->
                    knownIds.Contains effect.SourceCard
                    && (effect.TargetCard.IsNone || knownIds.Contains effect.TargetCard.Value))
            )
          RoundUsage = state.RoundUsage
          PendingEffectChooser = state.PendingEffect |> ValueOption.map _.Chooser
          PendingKnockoutChooser = state.PendingKnockout |> ValueOption.map _.Chooser
          PendingBarChitPlayer =
            state.PendingBarChits
            |> Seq.tryHead
            |> Option.map _.Player
            |> ValueOption.ofOption
          ReplacementPlayer = state.ReplacementPlayer
          PendingRoundEnd = state.PendingRoundEnd
          Winner = state.Winner
          SuddenDeathCount = state.SuddenDeathCount }

    let create
        (state: MatchState)
        (actor: PlayerId)
        (mode: CpuObservationMode)
        (canRevealHand: PlayerId -> bool)
        (actions: ImmutableArray<LegalAction>)
        =
        let knownForCandidate =
            match mode with
            | CpuObservationMode.Fair ->
                let knownIds =
                    state.Cards
                    |> Seq.filter (fairCardIsKnown canRevealHand actor)
                    |> Seq.map _.Id
                    |> Set.ofSeq

                knownIds.Contains
            | CpuObservationMode.Authoritative -> fun _ -> true

        { Actor = actor
          State = publicState state actor canRevealHand
          Candidates =
            ImmutableArray.CreateRange(
                actions
                |> Seq.mapi (fun index action ->
                    { Id = forIndex state index
                      Kind = action.Kind
                      Action = action.Command.Action
                      Choices =
                        ImmutableArray.CreateRange(
                            action.Command.Choices |> Seq.map (redactChoice knownForCandidate)
                        )
                      ChoiceRequirements =
                        ImmutableArray.CreateRange(
                            action.ChoiceRequirements
                            |> Seq.map (redactRequirement knownForCandidate)
                        )
                      Affordability = action.Affordability })
            )
          AuthoritativeState =
            match mode with
            | CpuObservationMode.Fair -> ValueNone
            | CpuObservationMode.Authoritative -> ValueSome state }
