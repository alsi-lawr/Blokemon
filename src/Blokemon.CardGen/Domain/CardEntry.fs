namespace Blokemon.CardGen.Domain

open System
open System.Collections.Immutable

/// An entry in a card's mechanics region.
[<RequireQualifiedAccess>]
type CardEntry =
    /// A Blokemon Power entry.
    | PokemonPower of id: MechanicalId * name: string * effectText: string

    /// An Attack entry, whose effect text is absent on a pure-Damage Attack.
    | Attack of
        id: MechanicalId *
        name: string *
        energyCost: ImmutableArray<BlokemonType> *
        damage: Damage *
        effectText: string option

    /// A Rule entry.
    | Rule of id: MechanicalId * name: string * effectText: string

    /// The mechanical identifier of the entry.
    member this.Id =
        match this with
        | CardEntry.PokemonPower(id = id)
        | CardEntry.Attack(id = id)
        | CardEntry.Rule(id = id) -> id

    /// The printed name of the entry.
    member this.Name =
        match this with
        | CardEntry.PokemonPower(name = name)
        | CardEntry.Attack(name = name)
        | CardEntry.Rule(name = name) -> name

/// An entry in a card's mechanics region.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CardEntry =

    /// Every entry prints a name, so a blank one is a defect in the authority rather than a card.
    let private printedName (name: string) =
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof name)
        name

    /// A Blokemon Power entry.
    let pokemonPower id name effectText =
        CardEntry.PokemonPower(id, printedName name, effectText)

    /// An Attack entry.
    let attack id name energyCost damage effectText =
        CardEntry.Attack(id, printedName name, energyCost, damage, effectText)

    /// A Rule entry.
    let rule id name effectText =
        CardEntry.Rule(id, printedName name, effectText)
