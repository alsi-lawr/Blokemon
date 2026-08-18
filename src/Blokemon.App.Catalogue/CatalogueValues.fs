namespace Blokemon.App.Catalogue

open System
open System.Collections.Generic

/// One card a starter deck contains, and how many copies of it.
type StarterDeckEntry =
    { CardId: string
      Quantity: int }

    // The C# sealed record this replaces carried compiler-generated structural operators, and an
    // F# record emits none: a C# `==` against it would silently fall back to reference equality.
    static member op_Equality(left: StarterDeckEntry, right: StarterDeckEntry) = left.Equals right

    static member op_Inequality(left: StarterDeckEntry, right: StarterDeckEntry) =
        not (left.Equals right)

/// One of the catalogue's starter decks.
type StarterDeck =
    { Id: string
      SavedDeckId: Guid
      Name: string
      Type: string
      Role: string
      Description: string
      LeaderCardId: string
      Entries: IReadOnlyList<StarterDeckEntry> }

    member this.CardCount = this.Entries |> Seq.sumBy (fun entry -> entry.Quantity)

    member this.ExpandedCardIds =
        this.Entries
        |> Seq.sortWith (fun left right -> String.CompareOrdinal(left.CardId, right.CardId))
        |> Seq.collect (fun entry -> Seq.replicate entry.Quantity entry.CardId)
        |> Seq.toArray

    static member op_Equality(left: StarterDeck, right: StarterDeck) = left.Equals right

    static member op_Inequality(left: StarterDeck, right: StarterDeck) = not (left.Equals right)

/// One rules effect the catalogue can name: an attack, a party trick or a house rule.
type CatalogueEffect =
    { Id: string
      Name: string
      Text: string | null }

    // The C# sealed record this replaces carried compiler-generated structural operators, and an
    // F# record emits none: a C# `==` against it would silently fall back to reference equality.
    static member op_Equality(left: CatalogueEffect, right: CatalogueEffect) = left.Equals right

    static member op_Inequality(left: CatalogueEffect, right: CatalogueEffect) =
        not (left.Equals right)
