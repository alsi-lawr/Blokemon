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

            let visibleTargets =
                matchEvent.TargetCards |> Seq.filter (canReveal state human) |> Seq.toArray

            // Reveal faces only for authorised cards the presentation would otherwise hide
            // (face-down Prize Cards, deck cards). Cards the viewer already sees - their own
            // hand, anything in a public zone - need no reveal overlay.
            let revealed: CardView array =
                if matchEvent.Kind = MatchEventKind.CardsRevealed then
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
