namespace Blokemon.Game

open System.Collections.Immutable
open Blokemon.Core.SetDesign

module internal PokemonPowers =

    let rec containsOpcode (program: BlokemonEffectInstruction array) (opcode: BlokemonOpcode) =
        program
        |> Array.exists (fun instruction ->
            instruction.Opcode = opcode
            || containsOpcode instruction.Then opcode
            || containsOpcode instruction.Otherwise opcode)

    let private isInPlay (card: CardState) =
        card.Zone = CardZone.Oche || card.Zone = CardZone.Booth

    let private rawPowerIsEnabled (catalog: AuthorityCatalog) (card: CardState) =
        card.Kind = CardKind.Bloke
        && isInPlay card
        && not (
            card.RoughStates
            |> Seq.exists (fun entry ->
                Array.contains entry.State catalog.Manifest.BaseRules.PokemonPower.DisabledBy)
        )

    let pokemonPowerIsEnabled
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (card: CardState)
        =
        let hasToxicGas (candidate: CardState) =
            rawPowerIsEnabled catalog candidate
            && catalog.PartyTricks candidate
               |> Seq.exists (fun trick -> containsOpcode trick.Program BlokemonOpcode.ToxicGas)

        rawPowerIsEnabled catalog card
        && (catalog.PartyTricks card
            |> Seq.exists (fun trick -> containsOpcode trick.Program BlokemonOpcode.ToxicGas)
            || not (builder.Cards |> Seq.exists hasToxicGas))

    let private isTransforming
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (card: CardState)
        =
        card.MechanicalId = MechanicalCardId "BLK-132"
        && card.Zone = CardZone.Oche
        && pokemonPowerIsEnabled catalog builder card

    let effectiveBloke (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        if isTransforming catalog builder card then
            match builder.Oche(builder.Other card.Owner) with
            | ValueSome opponent when opponent.Kind = CardKind.Bloke ->
                catalog.Bloke opponent.MechanicalId
            | _ -> catalog.Bloke card.MechanicalId
        else
            catalog.Bloke card.MechanicalId

    let effectiveAttacks (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        if card.Kind <> CardKind.Bloke then
            catalog.Attacks card
        elif isTransforming catalog builder card then
            match builder.Oche(builder.Other card.Owner) with
            | ValueSome opponent -> catalog.Attacks opponent
            | ValueNone -> Seq.empty
        else
            catalog.Attacks card

    let effectivePartyTricks (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        if card.Kind <> CardKind.Bloke then
            catalog.PartyTricks card
        elif isTransforming catalog builder card then
            let transform = catalog.PartyTricks card

            match builder.Oche(builder.Other card.Owner) with
            | ValueSome opponent ->
                Seq.append transform (catalog.PartyTricks opponent)
                |> Seq.distinctBy (fun trick -> trick.MechanicalId)
            | ValueNone -> transform
        else
            catalog.PartyTricks card

    let hasActivePower
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (card: CardState)
        (opcode: BlokemonOpcode)
        =
        pokemonPowerIsEnabled catalog builder card
        && effectivePartyTricks catalog builder card
           |> Seq.exists (fun trick -> containsOpcode trick.Program opcode)

    let effectiveStayingPower
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (card: CardState)
        =
        if card.Kind = CardKind.Bloke then
            (effectiveBloke catalog builder card).StayingPower
        else
            catalog.StayingPower card

    let effectiveTaxiFare (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        if card.Kind = CardKind.Bloke then
            (effectiveBloke catalog builder card).TaxiFare
        else
            catalog.TaxiFare card

    let effectiveMechanicalTypes
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (card: CardState)
        =
        let changed =
            builder.Effects
            |> Seq.tryFindBack (fun effect ->
                effect.TargetCard = ValueSome card.Id
                && effect.Kind = TemporaryEffectKind.ChangeType
                && effect.MechanicalTypes.Length > 0)

        match changed with
        | Some effect -> effect.MechanicalTypes
        | None when card.Kind = CardKind.Bloke ->
            ImmutableArray.CreateRange((effectiveBloke catalog builder card).MechanicalTypes)
        | None -> ImmutableArray.Create BlokemonMechanicalType.Colorless

    let hasEffectiveSoftSpot (catalog: AuthorityCatalog) (builder: MatchBuilder) (card: CardState) =
        if card.Kind <> CardKind.Bloke then
            false
        else
            let effects =
                builder.Effects
                |> Seq.filter (fun effect ->
                    effect.TargetCard = ValueSome card.Id
                    && effect.Kind = TemporaryEffectKind.ModifySoftSpot)
                |> Seq.toArray

            if
                effects
                |> Array.exists (fun effect ->
                    effect.Amount = 1 && effect.MechanicalTypes.Length = 0)
            then
                false
            else
                effects |> Array.exists (fun effect -> effect.MechanicalTypes.Length > 0)
                || (effectiveBloke catalog builder card).SoftSpots.Length > 0

    let effectiveEnergy
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (host: CardState)
        (energy: CardState)
        =
        if
            (builder.Effects
             |> Seq.exists (fun effect ->
                 effect.TargetCard = ValueSome host.Id
                 && effect.Kind = TemporaryEffectKind.EnergyBurn))
            || isTransforming catalog builder host
        then
            let units =
                if energy.Kind = CardKind.Vim then
                    (catalog.Vim energy.MechanicalId).Provides.Length
                else
                    builder.Effects
                    |> Seq.tryFind (fun effect ->
                        effect.TargetCard = ValueSome energy.Id
                        && effect.Kind = TemporaryEffectKind.BuzzapEnergy)
                    |> Option.map (fun _ -> 2)
                    |> Option.defaultValue 0

            if isTransforming catalog builder host then
                ImmutableArray.CreateRange(Seq.replicate units BlokemonMechanicalType.Colorless)
            else
                ImmutableArray.CreateRange(Seq.replicate units BlokemonMechanicalType.Fire)
        elif energy.Kind = CardKind.Vim then
            ImmutableArray.CreateRange((catalog.Vim energy.MechanicalId).Provides)
        else
            builder.Effects
            |> Seq.tryPick (fun effect ->
                if
                    effect.TargetCard = ValueSome energy.Id
                    && effect.Kind = TemporaryEffectKind.BuzzapEnergy
                    && effect.MechanicalTypes.Length = 1
                then
                    Some(
                        ImmutableArray.Create(effect.MechanicalTypes[0], effect.MechanicalTypes[0])
                    )
                else
                    None)
            |> Option.defaultValue ImmutableArray<_>.Empty
