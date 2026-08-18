namespace Blokemon.Game

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable

/// An immutable, structurally equal list that is safe to leave at its struct default: the private
/// Items sentinel turns `default(FrozenList<T>)` into an empty list, which is what the defaulted
/// parameters on ChoiceRequirement and PendingMatchEvent rely on.
[<Struct; CustomEquality; NoComparison>]
type FrozenList<'T> =
    val private stored: ImmutableArray<'T>

    private new(items: ImmutableArray<'T>) = { stored = items }

    member private this.Items =
        if this.stored.IsDefault then
            ImmutableArray<'T>.Empty
        else
            this.stored

    member this.Count = this.Items.Length

    member this.Item
        with get (index: int) = this.Items[index]

    static member Empty = FrozenList<'T>(ImmutableArray<'T>.Empty)

    static member Create(items: IEnumerable<'T>) =
        FrozenList<'T>(ImmutableArray.CreateRange items)

    static member Create([<ParamArray>] items: 'T array) =
        FrozenList<'T>(ImmutableArray.CreateRange items)

    member this.ToImmutableArray() = this.Items

    member this.Equals(other: FrozenList<'T>) =
        Linq.Enumerable.SequenceEqual(this.Items, other.Items)

    override this.Equals(other: obj | null) =
        match other with
        | :? FrozenList<'T> as list -> this.Equals list
        | _ -> false

    override this.GetHashCode() =
        let mutable hash = HashCode()

        for item in this.Items do
            hash.Add item

        hash.ToHashCode()

    interface IEquatable<FrozenList<'T>> with
        member this.Equals(other) = this.Equals other

    interface IReadOnlyList<'T> with
        member this.Count = this.Count

        member this.Item
            with get (index) = this.Items[index]

    interface IEnumerable<'T> with
        member this.GetEnumerator() : IEnumerator<'T> =
            (this.Items :> IEnumerable<'T>).GetEnumerator()

    interface IEnumerable with
        member this.GetEnumerator() : IEnumerator =
            (this.Items :> IEnumerable).GetEnumerator()

    static member op_Equality(left: FrozenList<'T>, right: FrozenList<'T>) = left.Equals right

    static member op_Inequality(left: FrozenList<'T>, right: FrozenList<'T>) =
        not (left.Equals right)

[<RequireQualifiedAccess>]
module FrozenList =

    [<GeneralizableValue>]
    let empty<'T> : FrozenList<'T> = FrozenList<'T>.Empty

    let ofSeq (items: 'T seq) = FrozenList<'T>.Create items

    let toSeq (items: FrozenList<'T>) = items :> seq<'T>
