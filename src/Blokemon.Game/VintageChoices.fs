namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.PokemonPowers

module internal VintageChoices =

    let private cardsRequirement
        (requirements: ResizeArray<ChoiceRequirement>)
        (effect: EffectId)
        (path: string)
        (suffix: string)
        (chooser: PlayerId)
        (minimum: int)
        (maximum: int)
        (cards: CardState seq)
        (dependency: EffectChoiceId voption)
        =
        requirements.Add(
            ChoiceRequirement.create
                (choiceId effect path suffix)
                ChoiceRequirementKind.Cards
                chooser
                minimum
                maximum
                (ImmutableArray.CreateRange(cards |> Seq.map (fun card -> card.Id)))
                ImmutableArray<_>.Empty
                ImmutableArray<_>.Empty
                dependency
        )

    let private typeRequirement
        (requirements: ResizeArray<ChoiceRequirement>)
        (effect: EffectId)
        (path: string)
        (actor: PlayerId)
        (types: BlokemonMechanicalType seq)
        (dependency: EffectChoiceId voption)
        =
        requirements.Add(
            ChoiceRequirement.create
                (choiceId effect path "type")
                ChoiceRequirementKind.MechanicalType
                actor
                1
                1
                ImmutableArray<_>.Empty
                (ImmutableArray.CreateRange(types |> Seq.distinct |> Seq.sort))
                ImmutableArray<_>.Empty
                dependency
        )

    let private damaged (cards: CardState seq) =
        cards |> Seq.filter (fun card -> card.Damage > 0)

    let inspect
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        (dependency: EffectChoiceId voption)
        (requirements: ResizeArray<ChoiceRequirement>)
        =
        let ownPokemon = inPlay builder actor |> Seq.filter catalog.CountsAsPokemon

        let otherPokemon =
            inPlay builder (builder.Other actor) |> Seq.filter catalog.CountsAsPokemon

        let addTransfer (fromCards: CardState seq) (toCards: CardState seq) =
            let fromCards = fromCards |> Seq.toArray

            let toCards =
                match fromCards |> Array.tryHead with
                | Some first ->
                    Seq.append
                        (toCards |> Seq.filter (fun card -> card.Id <> first.Id))
                        (toCards |> Seq.filter (fun card -> card.Id = first.Id))
                | None -> toCards

            cardsRequirement requirements effect path "from" actor 1 1 fromCards dependency
            cardsRequirement requirements effect path "to" actor 1 1 toCards dependency

        match instruction.Opcode with
        | BlokemonOpcode.EnergyTrans ->
            let energy =
                ownPokemon
                |> Seq.collect (fun card -> card.Attachments |> Seq.map builder.Card)
                |> Seq.filter (fun card ->
                    effectiveEnergy catalog builder (builder.Card card.AttachedTo.Value) card
                    |> Seq.contains BlokemonMechanicalType.Grass)
                |> Seq.toArray

            let targets =
                match
                    energy
                    |> Array.tryHead
                    |> Option.bind (fun card -> card.AttachedTo |> ValueOption.toOption)
                with
                | Some firstHost ->
                    Seq.append
                        (ownPokemon |> Seq.filter (fun card -> card.Id <> firstHost))
                        (ownPokemon |> Seq.filter (fun card -> card.Id = firstHost))
                    |> Seq.map (fun card -> card.Id)
                    |> Seq.toArray
                | None -> ownPokemon |> Seq.map (fun card -> card.Id) |> Seq.toArray

            if energy.Length > 0 && targets.Length > 0 then
                requirements.Add
                    { ChoiceRequirement.create
                          (choiceId effect path "attachments")
                          ChoiceRequirementKind.Attachments
                          actor
                          1
                          1
                          (ImmutableArray.CreateRange(energy |> Seq.map (fun card -> card.Id)))
                          ImmutableArray<_>.Empty
                          ImmutableArray<_>.Empty
                          dependency with
                        EligibleTargets = ImmutableArray.CreateRange targets }

            true
        | BlokemonOpcode.RainDance ->
            let energy =
                builder.CardsIn(actor, CardZone.Mitt)
                |> Seq.filter (fun card ->
                    card.Kind = CardKind.Vim
                    && (catalog.Vim card.MechanicalId).IsBasic
                    && (catalog.Vim card.MechanicalId).MechanicalType = BlokemonMechanicalType.Water)
                |> Seq.toArray

            let targets =
                ownPokemon
                |> Seq.filter (fun card ->
                    effectiveMechanicalTypes catalog builder card
                    |> Seq.contains BlokemonMechanicalType.Water)
                |> Seq.map (fun card -> card.Id)
                |> Seq.toArray

            if energy.Length > 0 && targets.Length > 0 then
                requirements.Add
                    { ChoiceRequirement.create
                          (choiceId effect path "attachments")
                          ChoiceRequirementKind.Attachments
                          actor
                          1
                          1
                          (ImmutableArray.CreateRange(energy |> Seq.map (fun card -> card.Id)))
                          ImmutableArray<_>.Empty
                          ImmutableArray<_>.Empty
                          dependency with
                        EligibleTargets = ImmutableArray.CreateRange targets }

            true
        | BlokemonOpcode.Shift ->
            let types =
                Seq.append ownPokemon otherPokemon
                |> Seq.filter (fun card -> card.Id <> source.Id)
                |> Seq.collect (effectiveMechanicalTypes catalog builder)
                |> Seq.filter (fun mechanicalType ->
                    mechanicalType <> BlokemonMechanicalType.Colorless)

            typeRequirement requirements effect path actor types dependency
            true
        | BlokemonOpcode.ChangeResistance ->
            typeRequirement requirements effect path actor instruction.MechanicalTypes dependency
            true
        | BlokemonOpcode.Peek ->
            let visibleChoices =
                seq {
                    yield! builder.CardsIn(actor, CardZone.Stack) |> Seq.truncate 1
                    yield! builder.CardsIn(builder.Other actor, CardZone.Stack) |> Seq.truncate 1
                    yield! builder.CardsIn(actor, CardZone.BarChit)
                    yield! builder.CardsIn(builder.Other actor, CardZone.BarChit)

                    if
                        builder.CardsIn(builder.Other actor, CardZone.Mitt) |> Seq.isEmpty |> not
                    then
                        yield source
                }

            cardsRequirement requirements effect path "cards" actor 1 1 visibleChoices dependency
            true
        | BlokemonOpcode.DamageSwap ->
            addTransfer (damaged ownPokemon) ownPokemon
            true
        | BlokemonOpcode.StrangeBehavior ->
            addTransfer
                (damaged ownPokemon |> Seq.filter (fun card -> card.Id <> source.Id))
                (Seq.singleton source)

            true
        | BlokemonOpcode.Curse ->
            addTransfer (damaged otherPokemon) otherPokemon
            true
        | BlokemonOpcode.Buzzap ->
            cardsRequirement
                requirements
                effect
                path
                "cards"
                actor
                1
                1
                (ownPokemon |> Seq.filter (fun card -> card.Id <> source.Id))
                dependency

            typeRequirement
                requirements
                effect
                path
                actor
                [ BlokemonMechanicalType.Grass
                  BlokemonMechanicalType.Fire
                  BlokemonMechanicalType.Water
                  BlokemonMechanicalType.Lightning
                  BlokemonMechanicalType.Psychic
                  BlokemonMechanicalType.Fighting ]
                dependency

            true
        | BlokemonOpcode.Devolve ->
            let candidates =
                Seq.append ownPokemon otherPokemon
                |> Seq.filter (fun card -> card.UnderlyingCards.Length > 0)

            cardsRequirement requirements effect path "cards" actor 1 1 candidates dependency
            true
        | BlokemonOpcode.DevolutionSpray ->
            let stages =
                ownPokemon
                |> Seq.collect (fun card ->
                    seq {
                        if card.UnderlyingCards.Length > 0 then
                            yield card

                        yield! card.UnderlyingCards |> Seq.skip 1 |> Seq.map builder.Card
                    })

            cardsRequirement requirements effect path "cards" actor 1 1 stages dependency
            true
        | BlokemonOpcode.RearrangeTopDeck ->
            let cards =
                instruction.Targets
                |> Seq.collect (fun target ->
                    resolveTarget catalog builder actor source target
                    |> Seq.truncate instruction.Amount)

            let count = cards |> Seq.length
            let maximum = min instruction.Amount count

            cardsRequirement
                requirements
                effect
                path
                "cards"
                actor
                (if instruction.Selection = BlokemonSelection.UpTo then
                     0
                 else
                     maximum)
                maximum
                cards
                dependency

            true
        | BlokemonOpcode.Wildfire ->
            let energy =
                source.Attachments
                |> Seq.map builder.Card
                |> Seq.filter (fun card ->
                    effectiveEnergy catalog builder source card
                    |> Seq.contains BlokemonMechanicalType.Fire)

            let count = energy |> Seq.length
            cardsRequirement requirements effect path "cards" actor 0 count energy dependency
            true
        | BlokemonOpcode.PokemonBreeder ->
            let stageTwo =
                builder.CardsIn(actor, CardZone.Mitt)
                |> Seq.filter (fun card ->
                    card.Kind = CardKind.Bloke
                    && (catalog.Bloke card.MechanicalId).Rank = BlokemonRank.Landlord)

            let basics =
                ownPokemon
                |> Seq.filter (fun card ->
                    card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)

            cardsRequirement requirements effect path "evolution" actor 1 1 stageTwo dependency
            cardsRequirement requirements effect path "basic" actor 1 1 basics dependency
            true
        | BlokemonOpcode.ScoopUp
        | BlokemonOpcode.AttachDefender ->
            cardsRequirement requirements effect path "cards" actor 1 1 ownPokemon dependency
            true
        | BlokemonOpcode.Revive ->
            let basics =
                builder.CardsIn(actor, CardZone.EmptiesTray)
                |> Seq.filter (fun card ->
                    card.Kind = CardKind.Bloke && catalog.IsRegular card.MechanicalId)

            cardsRequirement requirements effect path "cards" actor 1 1 basics dependency
            true
        | BlokemonOpcode.SuperPotion ->
            let candidates =
                damaged ownPokemon
                |> Seq.filter (fun card ->
                    card.Attachments
                    |> Seq.map builder.Card
                    |> Seq.exists (fun energy ->
                        effectiveEnergy catalog builder card energy |> Seq.isEmpty |> not))

            cardsRequirement requirements effect path "cards" actor 1 1 candidates dependency

            let energy =
                candidates
                |> Seq.collect (fun pokemon ->
                    pokemon.Attachments
                    |> Seq.map builder.Card
                    |> Seq.filter (fun energy ->
                        effectiveEnergy catalog builder pokemon energy |> Seq.isEmpty |> not))

            cardsRequirement requirements effect path "energy" actor 1 1 energy dependency

            let maximum =
                candidates |> Seq.map (fun card -> min 4 (card.Damage / 10)) |> Seq.fold max 0

            requirements.Add(
                ChoiceRequirement.create
                    (choiceId effect path "amount")
                    ChoiceRequirementKind.Amount
                    actor
                    0
                    maximum
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    dependency
            )

            true
        | BlokemonOpcode.Potion ->
            let candidates = damaged ownPokemon
            cardsRequirement requirements effect path "cards" actor 1 1 candidates dependency

            let maximum =
                candidates |> Seq.map (fun card -> min 2 (card.Damage / 10)) |> Seq.fold max 0

            requirements.Add(
                ChoiceRequirement.create
                    (choiceId effect path "amount")
                    ChoiceRequirementKind.Amount
                    actor
                    0
                    maximum
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    ImmutableArray<_>.Empty
                    dependency
            )

            true
        | _ -> false
