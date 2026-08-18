namespace Blokemon.Game

open System.Linq
open Blokemon.Core.SetDesign
open Blokemon.Game.MatchRules
open Blokemon.Game.MatchLegalChoices

/// What the active player could submit during their own turn: everything the phase switch delegates
/// to once the match is actually being played.
module internal MatchPlayingActions =

    let private vimActions (state: MatchState) (actor: PlayerId) (inPlay: CardState array) =
        state.CardsIn(actor, CardZone.Mitt)
        |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
        |> Seq.collect (fun vim ->
            inPlay
            |> Seq.map (fun target ->
                simple
                    LegalActionKind.AttachVim
                    state
                    actor
                    $"attach:{vim.Id.Value}:{target.Id.Value}"
                    (MatchAction.AttachVim(vim.Id, target.Id))))

    let private promoteActions
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        (inPlay: CardState array)
        =
        state.CardsIn(actor, CardZone.Mitt)
        |> Seq.filter (fun card -> card.Kind = CardKind.Bloke)
        |> Seq.collect (fun promotion ->
            inPlay
            |> Seq.map (fun target ->
                let requirements =
                    promotionChoiceRequirements catalog interpreter state actor promotion target

                let key = $"promote:{promotion.Id.Value}:{target.Id.Value}"

                legal
                    LegalActionKind.Promote
                    state
                    actor
                    key
                    key
                    requirements
                    (stableChoices requirements)
                    (MatchAction.Promote(promotion.Id, target.Id))))

    let private playBlokeActions (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        state.CardsIn(actor, CardZone.Mitt)
        |> Seq.filter (fun card ->
            card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)
        |> Seq.map (fun bloke ->
            simple
                LegalActionKind.PlayBloke
                state
                actor
                $"play:{bloke.Id.Value}"
                (MatchAction.PlayBloke bloke.Id))

    let private playKitActions
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        (inPlay: CardState array)
        =
        state.CardsIn(actor, CardZone.Mitt)
        |> Seq.filter (fun card -> card.Kind = CardKind.Kit)
        |> Seq.collect (fun kitCard ->
            let kit = catalog.Kit kitCard.MechanicalId

            let targets =
                if kit.Kind = BlokemonKitKind.BarKit then
                    inPlay |> Seq.map (fun card -> ValueSome card.Id)
                else
                    Seq.singleton ValueNone

            let requirements =
                FrozenList<ChoiceRequirement>
                    .Create(
                        kit.HouseRules
                        |> Seq.filter (fun rule ->
                            not (containsOpcode rule.Program BlokemonOpcode.OncePerRound)
                            && not (isDeclarativeHouseRule rule))
                        |> Seq.collect (fun rule ->
                            invocationRequirements
                                interpreter
                                state
                                actor
                                kitCard.Id
                                (EffectId rule.MechanicalId))
                        |> fun values -> values.DistinctBy(fun requirement -> requirement.Id)
                    )

            let choices = stableChoices requirements

            targets
            |> Seq.map (fun target ->
                let suffix =
                    match target with
                    | ValueSome value -> value.Value
                    | ValueNone -> "none"

                let key = $"kit:{kitCard.Id.Value}:{suffix}"

                legal
                    LegalActionKind.PlayKit
                    state
                    actor
                    key
                    key
                    requirements
                    choices
                    (MatchAction.PlayKit(kitCard.Id, target))))

    let private inPlayEffectActions
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        (inPlay: CardState array)
        =
        inPlay
        |> Seq.collect (fun source ->
            let tricks =
                catalog.PartyTricks source
                |> Seq.filter (fun trick -> trick.Trigger = BlokemonTrigger.Activated)
                |> Seq.map (fun trick ->
                    let effect = EffectId trick.MechanicalId

                    let requirements =
                        invocationRequirements interpreter state actor source.Id effect

                    let key = $"trick:{source.Id.Value}:{effect.Value}"

                    legal
                        LegalActionKind.UsePartyTrick
                        state
                        actor
                        key
                        key
                        requirements
                        (stableChoices (
                            FrozenList<ChoiceRequirement>
                                .Create(
                                    requirements
                                    |> Seq.filter (fun requirement -> requirement.Chooser = actor)
                                )
                        ))
                        (MatchAction.UsePartyTrick(source.Id, effect)))

            let attacks =
                catalog.Attacks source
                |> Seq.map (fun attack ->
                    let effect = EffectId attack.MechanicalId

                    let requirements =
                        invocationRequirements interpreter state actor source.Id effect

                    let key = $"attack:{source.Id.Value}:{effect.Value}"

                    legal
                        LegalActionKind.Attack
                        state
                        actor
                        key
                        key
                        requirements
                        (stableChoices (
                            FrozenList<ChoiceRequirement>
                                .Create(
                                    requirements
                                    |> Seq.filter (fun requirement -> requirement.Chooser = actor)
                                )
                        ))
                        (MatchAction.Attack(source.Id, effect)))

            let fossils =
                if source.Kind = CardKind.Kit && catalog.IsFossil source.MechanicalId then
                    Seq.singleton (
                        simple
                            LegalActionKind.ChuckFossil
                            state
                            actor
                            $"chuck:{source.Id.Value}"
                            (MatchAction.ChuckFossil source.Id)
                    )
                else
                    Seq.empty

            Seq.concat [ tricks; attacks; fossils ])

    let private taxiActions (catalog: AuthorityCatalog) (state: MatchState) (actor: PlayerId) =
        match state.Oche actor with
        | ValueNone -> Seq.empty
        | ValueSome oche ->
            let fare = effectiveTaxiFare catalog (MatchBuilder(state, catalog)) oche

            let vim =
                oche.Attachments
                |> Seq.map state.Card
                |> Seq.filter (fun card -> card.Kind = CardKind.Vim)
                |> Seq.truncate fare
                |> Seq.map (fun card -> card.Id)
                |> FrozenList<CardInstanceId>.Create

            state.CardsIn(actor, CardZone.Booth)
            |> Seq.map (fun booth ->
                simple
                    LegalActionKind.Taxi
                    state
                    actor
                    $"taxi:{booth.Id.Value}"
                    (MatchAction.Taxi(booth.Id, vim)))

    let private localActions
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        =
        state.Cards
        |> Seq.filter (fun card -> card.Zone = CardZone.Local)
        |> Seq.collect (fun source ->
            catalog.HouseRules source
            |> Seq.filter (fun rule -> containsOpcode rule.Program BlokemonOpcode.OncePerRound)
            |> Seq.map (fun rule ->
                let effect = EffectId rule.MechanicalId

                let requirements = invocationRequirements interpreter state actor source.Id effect

                let key = $"local:{source.Id.Value}:{effect.Value}"

                legal
                    LegalActionKind.UsePartyTrick
                    state
                    actor
                    key
                    key
                    requirements
                    (stableChoices requirements)
                    (MatchAction.UsePartyTrick(source.Id, effect))))

    let playingActions
        (catalog: AuthorityCatalog)
        (interpreter: BlokemonInterpreter)
        (state: MatchState)
        (actor: PlayerId)
        =
        if state.ActivePlayer <> actor then
            Seq.empty
        else
            let inPlay =
                state.Cards
                |> Seq.filter (fun card -> card.Owner = actor && isInPlay card)
                |> Seq.toArray

            Seq.concat
                [ vimActions state actor inPlay
                  promoteActions catalog interpreter state actor inPlay
                  playBlokeActions catalog state actor
                  playKitActions catalog interpreter state actor inPlay
                  inPlayEffectActions catalog interpreter state actor inPlay
                  taxiActions catalog state actor
                  localActions catalog interpreter state actor
                  Seq.singleton (
                      simple LegalActionKind.EndRound state actor "end" MatchAction.EndRound
                  ) ]
