namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins
open Blokemon.Game.MatchSendHome

/// Working through everything the damage just knocked out, in a fixed order, pausing whenever a
/// trigger needs an answer.
module internal MatchKnockouts =

    let private tryRecover
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (card: CardState)
        (attackingCard: CardInstanceId)
        =
        match
            catalog.PartyTricks card
            |> Seq.tryFind (fun trick ->
                trick.Trigger = BlokemonTrigger.BeforeSelfSentHomeByAttackDamage)
        with
        | None -> false
        | Some recovery ->
            let effect = EffectId recovery.MechanicalId

            let execution =
                interpreter.ExecuteTriggered(
                    builder,
                    card.Owner,
                    card,
                    effect,
                    recovery.Program,
                    ImmutableArray<_>.Empty,
                    ValueSome
                        { KnockedOutBloke = ValueSome card.Id
                          AttackingBloke = ValueSome attackingCard }
                )

            if not execution.IsApplied then
                false
            else
                builder.Events.Add
                    { PendingMatchEvent.forCard MatchEventKind.TriggerResolved card.Owner card.Id with
                        Effect = ValueSome effect }

                let recovered = builder.Card card.Id
                recovered.Damage < effectiveStayingPower catalog recovered

    /// The opponent's own blokes may want to react to the knockout. Only the first such source is
    /// asked now; the rest are parked on the pending resolution and asked in turn.
    let private queueKnockoutTrigger
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (knockedOut: CardState)
        (attackingCard: CardInstanceId)
        (remainingKnockouts: ImmutableArray<CardInstanceId>)
        (finishRoundAfterResolution: bool)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        (extraBarChits: int)
        =
        let attacker = builder.Card attackingCard

        if
            attacker.Owner = knockedOut.Owner
            || knockedOut.Damage < effectiveStayingPower catalog knockedOut
        then
            false
        else

            let sources =
                builder.Cards
                |> Seq.filter (fun card ->
                    card.Owner = knockedOut.Owner && card.Id <> knockedOut.Id && isInPlay card)
                |> Seq.filter (fun card ->
                    catalog.PartyTricks card
                    |> Seq.exists (fun trick ->
                        trick.Trigger = BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage))
                |> Seq.sortBy (fun card -> card.Id)
                |> Seq.toArray

            if sources.Length = 0 then
                false
            else

                let first = sources[0]

                let trick =
                    catalog.PartyTricks first
                    |> Seq.find (fun value ->
                        value.Trigger = BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage)

                let effect = EffectId trick.MechanicalId

                let context =
                    { KnockedOutBloke = ValueSome knockedOut.Id
                      AttackingBloke = ValueSome attackingCard }

                let requirements =
                    interpreter.InspectChoices(
                        builder,
                        knockedOut.Owner,
                        first,
                        effect,
                        trick.Program,
                        ValueSome context
                    )

                let optional =
                    requirements
                    |> Seq.find (fun requirement ->
                        requirement.Kind = ChoiceRequirementKind.Optional)

                let branch =
                    interpreter.Plan(
                        builder,
                        knockedOut.Owner,
                        first,
                        effect,
                        trick.Program,
                        ImmutableArray.Create(EffectChoice.Optional(optional.Id, true)),
                        false,
                        false,
                        ImmutableArray<_>.Empty,
                        ValueSome context
                    )

                let eligibleVim =
                    branch.Requirements
                    |> Seq.filter (fun requirement ->
                        requirement.Kind = ChoiceRequirementKind.Cards)
                    |> Seq.collect (fun requirement -> requirement.EligibleCards)
                    |> Seq.distinct
                    |> Seq.sort
                    |> Seq.toArray

                if eligibleVim.Length = 0 then
                    false
                else
                    builder.PendingKnockout <-
                        ValueSome
                            { KnockedOutCard = knockedOut.Id
                              RemainingKnockouts = remainingKnockouts
                              TriggerSources =
                                ImmutableArray.CreateRange(
                                    sources |> Seq.skip 1 |> Seq.map (fun card -> card.Id)
                                )
                              TriggerSource = first.Id
                              TriggerEffect = effect
                              Chooser = knockedOut.Owner
                              EligibleVim = ImmutableArray.CreateRange eligibleVim
                              AttackingCard = attackingCard
                              FinishRoundAfterResolution = finishRoundAfterResolution
                              AttackDamageTargets = attackDamageTargets
                              ExtraBarChits = extraBarChits }

                    builder.Phase <- MatchPhase.AwaitingTriggerChoice

                    builder.Events.Add
                        { PendingMatchEvent.forCards
                              MatchEventKind.TriggerQueued
                              knockedOut.Owner
                              first.Id
                              (ImmutableArray.Create knockedOut.Id) with
                            Effect = ValueSome effect }

                    true

    let findSendHomeCandidates
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (forcedSendHome: ImmutableArray<CardInstanceId>)
        =
        ImmutableArray.CreateRange(
            builder.Cards
            |> Seq.filter isInPlay
            |> Seq.filter (fun card ->
                Seq.contains card.Id forcedSendHome
                || card.Damage >= effectiveStayingPower catalog card)
            |> Seq.sortBy (fun card -> card.Owner, card.Id)
            |> Seq.map (fun card -> card.Id)
        )

    /// Returns false when a trigger parked the match: the caller must stop and wait for the answer
    /// rather than carrying on with the rest of the command.
    let resolveIdentifiedSendHome
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (identified: ImmutableArray<CardInstanceId>)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        (extraBarChits: int)
        =
        let mutable extraBarChitsAwarded = false
        let mutable pended = false

        // Retaliation can put the attacker into the same list, so the candidates grow while they are
        // being worked through.
        let candidates =
            ResizeArray<CardState>(
                identified
                |> Seq.choose (fun cardId ->
                    match builder.FindCard cardId with
                    | ValueSome card when isInPlay card -> Some card
                    | _ -> None)
            )

        let mutable index = 0

        while not pended && index < candidates.Count do
            match builder.FindCard candidates[index].Id with
            | ValueSome current when isInPlay current ->
                let knockedOutByAttackDamage =
                    attackingCard.IsSome
                    && Seq.contains current.Id attackDamageTargets
                    && current.Damage >= effectiveStayingPower catalog current

                let damageAttacker =
                    if knockedOutByAttackDamage then
                        attackingCard
                    else
                        ValueNone

                let recovered =
                    match damageAttacker with
                    | ValueSome recoverAttacker ->
                        tryRecover catalog interpreter builder current recoverAttacker
                    | ValueNone -> false

                if not recovered then
                    let queued =
                        match damageAttacker with
                        | ValueSome attacker ->
                            queueKnockoutTrigger
                                catalog
                                interpreter
                                builder
                                current
                                attacker
                                (ImmutableArray.CreateRange(
                                    candidates
                                    |> Seq.skip (index + 1)
                                    |> Seq.map (fun card -> card.Id)
                                ))
                                finishRoundAfterResolution
                                attackDamageTargets
                                (if extraBarChitsAwarded then 0 else extraBarChits)
                        | ValueNone -> false

                    if queued then
                        pended <- true
                    else
                        let reflected =
                            sendHomeOne
                                catalog
                                interpreter
                                builder
                                current
                                damageAttacker
                                finishRoundAfterResolution

                        match (if reflected then damageAttacker else ValueNone) with
                        | ValueSome attackerId ->
                            match builder.FindCard attackerId with
                            | ValueSome attackerState when isInPlay attackerState ->
                                candidates.Add attackerState
                            | _ -> ()
                        | ValueNone -> ()

                        if knockedOutByAttackDamage && not extraBarChitsAwarded then
                            takeExtraBarChits
                                catalog
                                builder
                                attackingCard.Value
                                extraBarChits
                                finishRoundAfterResolution

                            extraBarChitsAwarded <- true
            | _ -> ()

            index <- index + 1

        if pended then
            false
        else
            resolveWins catalog builder ValueNone

            if
                builder.Phase <> MatchPhase.Complete
                && not (Seq.isEmpty builder.PendingBarChits)
            then
                builder.Phase <- MatchPhase.AwaitingTriggerChoice
                false
            else
                true

    let resolveSendHome
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (forcedSendHome: ImmutableArray<CardInstanceId>)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        (extraBarChits: int)
        =
        resolveIdentifiedSendHome
            catalog
            interpreter
            builder
            (findSendHomeCandidates catalog builder forcedSendHome)
            attackingCard
            finishRoundAfterResolution
            attackDamageTargets
            extraBarChits

    let resolveReactiveAttackTriggers
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (attacker: CardState)
        (defenderBefore: CardState voption)
        (damageBefore: int)
        (attackDamageTargets: ImmutableArray<CardInstanceId>)
        =
        match defenderBefore with
        | ValueNone -> ()
        | ValueSome defenderBefore ->
            match builder.FindCard defenderBefore.Id with
            | ValueSome defender when
                defender.Damage > damageBefore && Seq.contains defender.Id attackDamageTargets
                ->
                for trick in
                    catalog.PartyTricks defender
                    |> Seq.filter (fun trick ->
                        trick.Trigger = BlokemonTrigger.AfterSelfDamagedByAttack)
                    |> Seq.toArray do
                    let effect = EffectId trick.MechanicalId

                    interpreter.ExecuteTriggered(
                        builder,
                        defender.Owner,
                        defender,
                        effect,
                        trick.Program,
                        ImmutableArray<_>.Empty,
                        ValueSome
                            { KnockedOutBloke = ValueSome defender.Id
                              AttackingBloke = ValueSome attacker.Id }
                    )
                    |> ignore

                    builder.Events.Add
                        { PendingMatchEvent.forCards
                              MatchEventKind.TriggerResolved
                              defender.Owner
                              defender.Id
                              (ImmutableArray.Create attacker.Id) with
                            Effect = ValueSome effect }
            | _ -> ()

    let resolveVoluntarySourceChuck
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (sourceBeforeExecution: CardState)
        (execution: InterpreterExecution)
        =
        if execution.SourceChucked then
            if sourceBeforeExecution.Zone = CardZone.Oche then
                assignReplacement builder sourceBeforeExecution.Owner

            resolveWins catalog builder ValueNone
