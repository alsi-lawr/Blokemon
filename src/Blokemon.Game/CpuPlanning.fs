namespace Blokemon.Game

open System
open System.Collections.Generic
open System.Collections.Immutable

module internal CpuPlanning =

    let private mix value =
        let mutable mixed = value
        mixed <- (mixed ^^^ (mixed >>> 30)) * 0xBF58476D1CE4E5B9UL
        mixed <- (mixed ^^^ (mixed >>> 27)) * 0x94D049BB133111EBUL
        mixed ^^^ (mixed >>> 31)

    let private combine state value =
        mix (state ^^^ value ^^^ 0x9E3779B97F4A7C15UL)

    let private stableString (value: string) seed =
        value |> Seq.fold (fun state character -> combine state (uint64 character)) seed

    let private mapArray mapper values =
        values |> Seq.map mapper |> ImmutableArray.CreateRange

    let private remapChoice (remap: CardInstanceId -> CardInstanceId) (choice: EffectChoice) =
        match choice with
        | EffectChoice.Amount _
        | EffectChoice.MechanicalType _
        | EffectChoice.Attack _ -> choice
        | EffectChoice.Cards(id, cards) -> EffectChoice.Cards(id, mapArray remap cards)
        | EffectChoice.Attachments(id, placements) ->
            EffectChoice.Attachments(
                id,
                mapArray
                    (fun (placement: VimAttachment) ->
                        { Vim = remap placement.Vim
                          Bloke = remap placement.Bloke })
                    placements
            )

    let private remapAction (remap: CardInstanceId -> CardInstanceId) (action: MatchAction) =
        match action with
        | MatchAction.ChooseMulliganBonus _
        | MatchAction.EndRound
        | MatchAction.ResolveEffectChoice
        | MatchAction.ResolveBarChitTrigger _
        | MatchAction.Resign -> action
        | MatchAction.ChooseOpening(oche, booth) ->
            MatchAction.ChooseOpening(remap oche, mapArray remap booth)
        | MatchAction.ChooseBonusPlacement booth ->
            MatchAction.ChooseBonusPlacement(mapArray remap booth)
        | MatchAction.AttachVim(vim, target) -> MatchAction.AttachVim(remap vim, remap target)
        | MatchAction.PlayBloke bloke -> MatchAction.PlayBloke(remap bloke)
        | MatchAction.Promote(promotion, target) ->
            MatchAction.Promote(remap promotion, remap target)
        | MatchAction.PlayKit(kit, target) ->
            MatchAction.PlayKit(remap kit, target |> ValueOption.map remap)
        | MatchAction.Taxi(booth, payment) -> MatchAction.Taxi(remap booth, mapArray remap payment)
        | MatchAction.UsePartyTrick(source, effect) ->
            MatchAction.UsePartyTrick(remap source, effect)
        | MatchAction.Attack(attacker, attack) -> MatchAction.Attack(remap attacker, attack)
        | MatchAction.ChuckFossil fossil -> MatchAction.ChuckFossil(remap fossil)
        | MatchAction.ChooseReplacement replacement ->
            MatchAction.ChooseReplacement(remap replacement)
        | MatchAction.ResolveKnockoutTrigger vim ->
            MatchAction.ResolveKnockoutTrigger(vim |> ValueOption.map remap)

    let private remapRequirement
        (remap: CardInstanceId -> CardInstanceId)
        (requirement: ChoiceRequirement)
        =
        { requirement with
            EligibleCards = mapArray remap requirement.EligibleCards
            EligibleTargets = mapArray remap requirement.EligibleTargets
            EligibleCardTypes =
                mapArray
                    (fun (value: CardMechanicalTypes) -> { value with Card = remap value.Card })
                    requirement.EligibleCardTypes }

    let private remapCommand (remap: CardInstanceId -> CardInstanceId) (command: MatchCommand) =
        { command with
            Choices = mapArray (remapChoice remap) command.Choices
            Action = remapAction remap command.Action }

    let private identityPool (catalog: AuthorityCatalog) =
        seq {
            for card in catalog.Manifest.Collectibles do
                yield MechanicalCardId card.Id, CardKind.Bloke

            for card in catalog.Manifest.Kits do
                yield MechanicalCardId card.Id, CardKind.Kit

            for card in catalog.Manifest.BasicVim do
                yield MechanicalCardId card.Id, CardKind.Vim
        }
        |> Seq.sortBy (fun (id, _) -> id.Value)
        |> Seq.toArray

    let private chooseIdentity
        (pool: (MechanicalCardId * CardKind) array)
        (seed: uint64)
        (sampleIndex: uint64)
        (owner: PlayerId)
        (zone: CardZone)
        (ordinal: int)
        =
        let key =
            seed
            |> combine sampleIndex
            |> stableString owner.Value
            |> combine (uint64 (int zone))
            |> combine (uint64 ordinal)

        pool[int (key % uint64 pool.Length)]

    let createFairState
        (catalog: AuthorityCatalog)
        (source: MatchState)
        (observation: CpuObservation)
        (seed: uint64)
        (sampleIndex: uint64)
        =
        let pool = identityPool catalog
        let known = observation.State.Cards |> Seq.map (fun card -> card.Id, card) |> dict

        let hidden =
            source.Cards
            |> Seq.filter (fun card -> not (known.ContainsKey card.Id))
            |> Seq.groupBy (fun card -> card.Owner, card.Zone)
            |> Seq.sortBy fst
            |> Seq.collect (fun ((owner, zone), cards) ->
                cards
                |> Seq.sortBy (fun card -> card.StackPosition, card.Id)
                |> Seq.mapi (fun ordinal card -> owner, zone, ordinal, card))
            |> Seq.toArray

        let remappedIds = Dictionary<CardInstanceId, CardInstanceId>()

        for owner, zone, ordinal, card in hidden do
            remappedIds.Add(
                card.Id,
                CardInstanceId $"cpu-sample:{owner.Value}:{int zone}:{ordinal}"
            )

        let remap id =
            match remappedIds.TryGetValue id with
            | true, value -> value
            | _ -> id

        let knownCards =
            observation.State.Cards
            |> Seq.map (fun card ->
                { card with
                    AttachedTo = card.AttachedTo |> ValueOption.map remap
                    Attachments = mapArray remap card.Attachments
                    UnderlyingCards = mapArray remap card.UnderlyingCards })
            |> Seq.toArray

        let hiddenCards =
            hidden
            |> Seq.map (fun (owner, zone, ordinal, card) ->
                let mechanicalId, kind = chooseIdentity pool seed sampleIndex owner zone ordinal

                { Id = remap card.Id
                  MechanicalId = mechanicalId
                  Owner = owner
                  Kind = kind
                  Zone = zone
                  IsFaceDown = zone = CardZone.BarChit
                  StackPosition = ordinal
                  AttachedTo = ValueNone
                  Attachments = ImmutableArray<_>.Empty
                  UnderlyingCards = ImmutableArray<_>.Empty
                  Damage = 0
                  RoughStates = ImmutableArray<_>.Empty
                  EnteredAtOwnerRound = 0
                  LastPromotedRound = -1 })
            |> Seq.toArray

        let cardIds = Seq.append knownCards hiddenCards |> Seq.map _.Id |> Set.ofSeq

        let players =
            observation.State.Players
            |> Seq.map (fun player ->
                let bonusDrawn =
                    source.Player(player.Id).BonusDrawn
                    |> Seq.map remap
                    |> Seq.filter cardIds.Contains
                    |> ImmutableArray.CreateRange

                { Id = player.Id
                  BarChitsRemaining = player.BarChitsRemaining
                  MulliganCount = player.MulliganCount
                  MulliganBonusAllowance = player.MulliganBonusAllowance
                  MulliganBonusChosen = player.MulliganBonusChosen
                  BonusDrawn = bonusDrawn
                  BonusPlacementChosen = player.BonusPlacementChosen
                  OpeningChosen = player.OpeningChosen
                  RoundsStarted = player.RoundsStarted })

        let effects =
            observation.State.Effects
            |> Seq.map (fun effect ->
                { effect with
                    SourceCard = remap effect.SourceCard
                    TargetCard = effect.TargetCard |> ValueOption.map remap
                    RelatedCards = effect.RelatedCards })

        let pendingEffect =
            source.PendingEffect
            |> ValueOption.map (fun pending ->
                { pending with
                    Command = remapCommand remap pending.Command
                    Source = remap pending.Source
                    Requirements = mapArray (remapRequirement remap) pending.Requirements })

        let pendingKnockout =
            source.PendingKnockout
            |> ValueOption.map (fun pending ->
                { pending with
                    KnockedOutCard = remap pending.KnockedOutCard
                    RemainingKnockouts = mapArray remap pending.RemainingKnockouts
                    TriggerSources = mapArray remap pending.TriggerSources
                    TriggerSource = remap pending.TriggerSource
                    EligibleVim = mapArray remap pending.EligibleVim
                    AttackingCard = remap pending.AttackingCard
                    AttackDamageTargets = mapArray remap pending.AttackDamageTargets })

        let randomSeed =
            seed
            |> combine sampleIndex
            |> combine (uint64 observation.State.Revision.Value)
            |> stableString observation.Actor.Value

        { Id = observation.State.Id
          AuthorityVersion = observation.State.AuthorityVersion
          Seed = MatchSeed randomSeed
          Random = MatchRandomState(randomSeed, 0)
          Revision = observation.State.Revision
          LastEventSequence = 0L
          Phase = observation.State.Phase
          OpeningPlayer = observation.State.OpeningPlayer
          ActivePlayer = observation.State.ActivePlayer
          RoundNumber = observation.State.RoundNumber
          Players = ImmutableArray.CreateRange players
          Cards =
            Seq.append knownCards hiddenCards
            |> Seq.sortBy _.Id
            |> ImmutableArray.CreateRange
          Effects = ImmutableArray.CreateRange effects
          ProcessedCommands = ImmutableArray<_>.Empty
          RoundUsage = observation.State.RoundUsage
          PendingEffect = pendingEffect
          PendingKnockout = pendingKnockout
          PendingBarChits =
            source.PendingBarChits
            |> Seq.map (fun pending ->
                { pending with
                    Card = remap pending.Card })
            |> ImmutableArray.CreateRange
          ReplacementPlayer = observation.State.ReplacementPlayer
          PendingRoundEnd = observation.State.PendingRoundEnd
          Winner = observation.State.Winner
          SuddenDeathCount = observation.State.SuddenDeathCount }
