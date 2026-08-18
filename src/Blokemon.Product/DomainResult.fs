namespace Blokemon.Product

open System
open System.Collections.Immutable

/// The outcome of a fallible domain operation: a value, or a typed failure.
[<RequireQualifiedAccess>]
type DomainResult<'TValue, 'TFailure> =
    | Succeeded of Value: 'TValue
    | Failed of Error: 'TFailure

    /// Folds the outcome into a single value. Kept for the call sites that hold a result
    /// without narrowing it to a case first.
    member this.Match<'TResult>
        (onSuccess: Func<'TValue, 'TResult>, onFailure: Func<'TFailure, 'TResult>)
        =
        match this with
        | DomainResult.Succeeded value -> onSuccess.Invoke value
        | DomainResult.Failed error -> onFailure.Invoke error

/// Sequences fallible steps, short-circuiting on the first typed failure.
[<AutoOpen>]
module internal DomainResultComputation =

    type DomainResultBuilder() =

        member _.Bind(source: DomainResult<'a, 'e>, binder: 'a -> DomainResult<'b, 'e>) =
            match source with
            | DomainResult.Succeeded value -> binder value
            | DomainResult.Failed error -> DomainResult.Failed error

        member _.Return(value: 'a) : DomainResult<'a, 'e> = DomainResult.Succeeded value

        member _.ReturnFrom(source: DomainResult<'a, 'e>) = source

        member _.Zero() : DomainResult<unit, 'e> = DomainResult.Succeeded()

    /// `result { let! … }` reads a chain of fallible steps as one.
    let result = DomainResultBuilder()

    /// Threads a state through an indexed sequence, stopping at the first typed failure.
    let foldIndexed
        (folder: 'state -> int -> 'item -> DomainResult<'state, 'failure>)
        (state: 'state)
        (items: ImmutableArray<'item>)
        : DomainResult<'state, 'failure> =
        let rec step index carried =
            if index >= items.Length then
                DomainResult.Succeeded carried
            else
                match folder carried index items[index] with
                | DomainResult.Succeeded next -> step (index + 1) next
                | DomainResult.Failed error -> DomainResult.Failed error

        step 0 state

    /// Fails when the guard holds, and carries on otherwise.
    let failWhen (condition: bool) (failure: 'failure) : DomainResult<unit, 'failure> =
        if condition then
            DomainResult.Failed failure
        else
            DomainResult.Succeeded()
