/// Every persisted document shape the catalogue reads, and the mechanical facts each starter
/// entry is checked against. [<CLIMutable>] is what lets these fields carry
/// [<property: JsonRequired>]: System.Text.Json refuses a required property with no setter, and
/// an immutable F# record has none. See .agent-workspace/068/probe-and-censuses.md leg (a0).
/// They live in their own module so their labels cannot capture a construction of the public
/// value records that carry the same names.
// PUBLIC BY FORCE, not by design: the C# originals were `private sealed record`s, whose
// constructors C# still emits as public IL members. F# gives an `internal` type internal
// constructors and accessors, which System.Text.Json's reflection resolver cannot reach at all
// ("Deserialization of types without a parameterless constructor ... is not supported"). These
// carry no behaviour and are named as documents so the widening reads as what it is.
module Blokemon.App.Catalogue.CatalogueDocuments

open System
open System.Text.Json.Serialization
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign


[<CLIMutable>]
type StarterDeckEntrySource =
    { [<property: JsonRequired>]
      CardId: string
      [<property: JsonRequired>]
      Quantity: int }

[<CLIMutable>]
type StarterDeckSource =
    { [<property: JsonRequired>]
      Id: string
      [<property: JsonRequired>]
      SavedDeckId: Guid
      [<property: JsonRequired>]
      Name: string
      [<property: JsonRequired>]
      Type: string
      [<property: JsonRequired>]
      Role: string
      [<property: JsonRequired>]
      Description: string
      [<property: JsonRequired>]
      LeaderCardId: string
      [<property: JsonRequired>]
      Entries: StarterDeckEntrySource array }

[<CLIMutable>]
type StarterDeckDocument =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      StarterDeckVersion: string
      [<property: JsonRequired>]
      MechanicalManifestVersion: string
      [<property: JsonRequired>]
      Decks: StarterDeckSource array }

type internal KnownCardKind =
    | Blokemon = 0
    | Trainer = 1
    | Energy = 2

type internal KnownCard =
    { Id: string
      Kind: KnownCardKind
      CopyLimit: int
      IsBasicEnergy: bool
      IsRegular: bool
      PromotesFromId: string | null
      Attacks: BlokemonAttack array }



[<CLIMutable>]
type CatalogueBootstrap =
    { [<property: JsonRequired>]
      SchemaVersion: int
      [<property: JsonRequired>]
      MechanicsJson: string
      [<property: JsonRequired>]
      StarterDecksJson: string
      [<property: JsonRequired>]
      PublicContentVersion: string
      [<property: JsonRequired>]
      CardStylesheet: string
      [<property: JsonRequired>]
      ReverseFaceHtml: string
      [<property: JsonRequired>]
      PackPresentation: PackPresentationView
      [<property: JsonRequired>]
      Cards: CardView array
      [<property: JsonRequired>]
      Effects: CatalogueEffect array }
