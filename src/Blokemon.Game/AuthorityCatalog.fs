namespace Blokemon.Game

open System
open System.Collections.Generic
open System.Linq
open Blokemon.Core.SetDesign

/// Every lookup the engine and the interpreter make against the printed authority, indexed once
/// per engine instead of scanned per question.
type internal AuthorityCatalog(manifest: BlokemonRuntimeManifest) =
    // Add, not the indexer: a duplicated mechanical id is an authority defect and must surface as
    // one, exactly as the C# ToDictionary calls this replaces did.
    let index (keyOf: 'T -> string) (items: 'T seq) =
        let result = Dictionary<string, 'T>(StringComparer.Ordinal)

        for item in items do
            result.Add(keyOf item, item)

        result

    let collectibles = manifest.Collectibles |> index (fun card -> card.Id)
    let kits = manifest.Kits |> index (fun card -> card.Id)
    let vim = manifest.BasicVim |> index (fun card -> card.Id)

    let attacks =
        Seq.append
            (manifest.Collectibles |> Seq.collect (fun card -> card.Attacks))
            (manifest.Kits |> Seq.collect (fun card -> card.Attacks))
        |> index (fun effect -> effect.MechanicalId)

    let partyTricks =
        Seq.append
            (manifest.Collectibles |> Seq.collect (fun card -> card.PartyTricks))
            (manifest.Kits |> Seq.collect (fun card -> card.PartyTricks))
        |> index (fun effect -> effect.MechanicalId)

    let houseRules =
        Seq.append
            (manifest.Collectibles |> Seq.collect (fun card -> card.HouseRules))
            (manifest.Kits |> Seq.collect (fun card -> card.HouseRules))
        |> index (fun effect -> effect.MechanicalId)

    let tryGet (source: Dictionary<string, 'T>) (key: string) =
        match source.TryGetValue key with
        | true, value -> ValueSome value
        | _ -> ValueNone

    member _.Manifest = manifest

    member _.Contains(id: MechanicalCardId) =
        collectibles.ContainsKey id.Value
        || kits.ContainsKey id.Value
        || vim.ContainsKey id.Value

    member _.Kind(id: MechanicalCardId) =
        if collectibles.ContainsKey id.Value then CardKind.Bloke
        elif kits.ContainsKey id.Value then CardKind.Kit
        else CardKind.Vim

    member _.CopyLimit(id: MechanicalCardId) =
        match tryGet collectibles id.Value with
        | ValueSome bloke -> bloke.StackCopyLimit
        | ValueNone ->
            match tryGet kits id.Value with
            | ValueSome kit -> kit.StackCopyLimit
            | ValueNone -> vim[id.Value].StackCopyLimit

    member _.IsRegular(id: MechanicalCardId) =
        match tryGet collectibles id.Value with
        | ValueSome card -> card.Rank = BlokemonRank.Regular
        | ValueNone -> false

    member _.IsFossil(id: MechanicalCardId) =
        manifest.BaseRules.FossilKits.KitIds.Contains(id.Value, StringComparer.Ordinal)

    member _.Bloke(id: MechanicalCardId) = collectibles[id.Value]

    member _.Kit(id: MechanicalCardId) = kits[id.Value]

    member _.Vim(id: MechanicalCardId) = vim[id.Value]

    member _.Attack(id: EffectId) = tryGet attacks id.Value

    member _.PartyTrick(id: EffectId) = tryGet partyTricks id.Value

    member _.HouseRule(id: EffectId) = tryGet houseRules id.Value

    member this.StayingPower(card: CardState) =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).StayingPower
        else
            manifest.BaseRules.FossilKits.PlayAsRegularLocalStayingPower

    member this.TaxiFare(card: CardState) =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).TaxiFare
        else
            Int32.MaxValue

    member this.BarChits(card: CardState) =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).BarChitsWhenSentHome
        elif manifest.BaseRules.FossilKits.SentHomeAwardsOneBarChit then
            1
        else
            0

    member this.MechanicalTypes(card: CardState) =
        if card.Kind = CardKind.Bloke then
            FrozenList<BlokemonMechanicalType>.Create(this.Bloke(card.MechanicalId).MechanicalTypes)
        else
            FrozenList<BlokemonMechanicalType>.Create BlokemonMechanicalType.Colorless

    member this.PartyTricks(card: CardState) : BlokemonPartyTrick seq =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).PartyTricks
        elif card.Kind = CardKind.Kit then
            this.Kit(card.MechanicalId).PartyTricks
        else
            Array.empty

    member this.Attacks(card: CardState) : BlokemonAttack seq =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).Attacks
        elif card.Kind = CardKind.Kit then
            this.Kit(card.MechanicalId).Attacks
        else
            Array.empty

    member this.HouseRules(card: CardState) : BlokemonHouseRule seq =
        if card.Kind = CardKind.Bloke then
            this.Bloke(card.MechanicalId).HouseRules
        elif card.Kind = CardKind.Kit then
            this.Kit(card.MechanicalId).HouseRules
        else
            Array.empty
