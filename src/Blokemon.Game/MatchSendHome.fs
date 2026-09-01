namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchWins

/// Taking one bloke off the table: the bar chits that follow, and the retaliation that can take the
/// attacker with it.
module internal MatchSendHome =

    let sendHomeOne
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (builder: MatchBuilder)
        (current: CardState)
        (attackingCard: CardInstanceId voption)
        (finishRoundAfterResolution: bool)
        =
        let destinyBond =
            attackingCard.IsSome
            && builder.Effects
               |> Seq.exists (fun effect ->
                   effect.TargetCard = ValueSome current.Id
                   && effect.Kind = TemporaryEffectKind.DestinyBond
                   && effect.AppliesFromRound <= builder.RoundNumber)

        let wasOche = current.Zone = CardZone.Oche
        builder.ChuckBloke current.Id |> ignore

        builder.Events.Add(
            PendingMatchEvent.forCards
                MatchEventKind.BlokeSentHome
                (builder.Other current.Owner)
                current.Id
                (ImmutableArray.Create current.Id)
        )

        let takingPlayer = builder.Other current.Owner

        builder.TakeBarChits(takingPlayer, catalog.BarChits current, current.Id)
        |> ignore

        let retaliates = destinyBond

        if wasOche then
            assignReplacement catalog builder current.Owner

        retaliates
