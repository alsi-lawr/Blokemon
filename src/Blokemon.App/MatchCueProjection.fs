namespace Blokemon.App

open System
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.MatchCardProjection
open Blokemon.App.MatchLabels
open Blokemon.App.MatchViewProjection
open Blokemon.Game

/// The animation the client plays back for one applied step: the cue per public event, and the
/// frames those cues run against.
module internal MatchCueProjection =

    let cue
        (catalogue: BlokemonCatalogue)
        (state: MatchState)
        (human: PlayerId)
        (displayName: string)
        (matchEvent: MatchEvent)
        (stepEvents: MatchEvent seq)
        : MatchEventCueView | null =
        let eventLabel = eventLabel catalogue

        let kind = animationKind matchEvent

        if not kind.HasValue then
            null
        else

            let eventSource =
                if matchEvent.SourceCard.IsSome then
                    matchEvent.SourceCard
                else
                    match matchEvent.Command with
                    | ValueSome command -> commandSource command
                    | ValueNone -> ValueNone

            let source: string | null =
                if eventSource.IsSome && canReveal state human eventSource.Value then
                    eventSource.Value.Value
                else
                    null

            // A reveal the rules themselves require authorises sight of the cards it names - by the
            // time anything draws a returned hand it is back inside the Deck and would be filtered
            // out as unseeable. A reveal a card effect performs keeps the authorisation it has
            // always had: what a card may show is the card's business, and widening every reveal at
            // once turns an effect that looks at cards into a window onto the opponent's hand.
            //
            // BOTH hands, the player's own included. The rulebook only compels the half of the
            // disclosure a table cannot perform by itself - you have already seen your own seven -
            // so reading its silence as permission to skip yours leaves the player watching their
            // Deck reshuffle with nothing on screen to account for it.
            let rulesReveal =
                matchEvent.Kind = MatchEventKind.CardsRevealed && matchEvent.Effect.IsNone

            let visibleTargets =
                if rulesReveal then
                    matchEvent.TargetCards |> Seq.toArray
                else
                    matchEvent.TargetCards |> Seq.filter (canReveal state human) |> Seq.toArray

            // A returned hand is nowhere any more, so where its cards have since landed is the
            // wrong question to ask of it: the reshuffle deals some of them straight back out, and
            // filtering by zone silently dropped those.
            //
            // An effect's reveal is the other case. It names cards that are still where it found
            // them, so there the filter is what keeps the overlay off cards already in sight.
            let revealed: CardView array =
                if rulesReveal then
                    visibleTargets
                    |> Array.map (fun card -> catalogue.Card (state.Card card).MechanicalId.Value)
                elif matchEvent.Kind = MatchEventKind.CardsRevealed && matchEvent.Effect.IsSome then
                    visibleTargets
                    |> Array.filter (fun card ->
                        match (state.Card card).Zone with
                        | CardZone.Stack
                        | CardZone.BarChit -> true
                        | _ -> false)
                    |> Array.map (fun card -> catalogue.Card (state.Card card).MechanicalId.Value)
                else
                    Array.empty

            MatchEventCueView(
                matchEvent.Sequence,
                kind.Value,
                eventLabel state human displayName matchEvent,
                source,
                visibleTargets |> Array.map _.Value,
                (if matchEvent.Kind = MatchEventKind.AttackDeclared then
                     resolvedAttackDamage matchEvent stepEvents
                 else
                     matchEvent.Amount),
                (match matchEvent.BadgeSide with
                 | ValueSome badge -> Nullable badge
                 | ValueNone -> Nullable()),
                (match matchEvent.Actor with
                 | ValueSome eventActor ->
                     let isHuman = eventActor = human
                     Nullable isHuman
                 | ValueNone -> Nullable()),
                revealed
            )

    let toPresentation
        (context: MatchContext)
        (document: MatchDocument)
        (displayName: string)
        (pending: PendingPresentation seq)
        =
        let frame = frame context
        let cue = cue context.Catalogue

        let human = document.Start.FirstDeck.Owner

        MatchPresentationView(
            pending
            |> Seq.map (fun step ->
                MatchPresentationStepView(
                    frame document step.State displayName,
                    step.Events
                    |> Seq.map (fun matchEvent ->
                        cue step.State human displayName matchEvent step.Events)
                    |> Seq.choose Option.ofObj
                    |> Seq.toArray
                ))
            |> Seq.toArray
        )
