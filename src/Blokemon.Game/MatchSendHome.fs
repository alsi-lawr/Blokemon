namespace Blokemon.Game

open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins

/// Taking one bloke off the table: the bar chits that follow, and the retaliation that can take the
/// attacker with it.
module internal MatchSendHome =

    let queueBarChitTriggers
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (player: PlayerId)
        (cards: FrozenList<CardInstanceId>)
        (finishRoundAfterResolution: bool)
        =
        for cardId in cards do
            let card = builder.Card cardId

            let trick =
                catalog.PartyTricks card
                |> Seq.tryFind (fun value -> value.Trigger = BlokemonTrigger.OnBarChitTaken)

            match trick with
            | Some trick when
                (builder.CardsIn(player, CardZone.Booth) |> Seq.length) < catalog.Manifest.BaseRules.Opening.BoothLimit
                ->
                let pending =
                    { Player = player
                      Card = cardId
                      Effect = EffectId trick.MechanicalId
                      FinishRoundAfterResolution = finishRoundAfterResolution }

                builder.QueueBarChit pending

                builder.Events.Add
                    { PendingMatchEvent.forCard MatchEventKind.TriggerQueued player cardId with
                        Effect = ValueSome pending.Effect }
            | _ -> ()

    let takeExtraBarChits
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (attackingCard: CardInstanceId)
        (count: int)
        (finishRoundAfterResolution: bool)
        =
        if count > 0 then
            let attacker = builder.Card attackingCard
            let taken = builder.TakeBarChits(attacker.Owner, count, attackingCard)
            queueBarChitTriggers catalog builder attacker.Owner taken finishRoundAfterResolution

    let sendHomeOne
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (current: CardState)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        =
        let retaliation =
            if attackingCard.IsSome && current.Zone = CardZone.Oche then
                catalog.PartyTricks current
                |> Seq.tryFind (fun trick ->
                    trick.Trigger = BlokemonTrigger.AfterSelfSentHomeByAttackDamage)
            else
                None

        let wasOche = current.Zone = CardZone.Oche
        builder.ChuckBloke current.Id |> ignore

        builder.Events.Add(
            PendingMatchEvent.forCards
                MatchEventKind.BlokeSentHome
                (builder.Other current.Owner)
                current.Id
                (FrozenList<CardInstanceId>.Create current.Id)
        )

        let takingPlayer = builder.Other current.Owner
        let taken = builder.TakeBarChits(takingPlayer, catalog.BarChits current, current.Id)
        queueBarChitTriggers catalog builder takingPlayer taken finishRoundAfterResolution

        let retaliates =
            match retaliation, attackingCard with
            | Some retaliation, ValueSome attacker ->
                let effect = EffectId retaliation.MechanicalId

                let execution =
                    interpreter.ExecuteTriggered(
                        builder,
                        current.Owner,
                        current,
                        effect,
                        retaliation.Program,
                        FrozenList.empty,
                        ValueSome
                            { KnockedOutBloke = ValueSome current.Id
                              AttackingBloke = ValueSome attacker }
                    )

                builder.Events.Add
                    { PendingMatchEvent.forCards
                          MatchEventKind.TriggerResolved
                          current.Owner
                          current.Id
                          execution.ForcedSendHome with
                        Effect = ValueSome effect }

                Seq.contains attacker execution.ForcedSendHome
            | _ -> false

        if wasOche then
            assignReplacement builder current.Owner

        retaliates
