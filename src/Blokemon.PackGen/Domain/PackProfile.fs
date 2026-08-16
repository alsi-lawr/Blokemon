namespace Blokemon.PackGen.Domain

open System

/// The configurable surface of a packaging deployment.
// BLOKEMON-016 fixes this surface at three points. The packaging design itself carries no
// override seam, so anything not named here is deliberately absent rather than ignored.
type PackProfile private (noun: string, stock: PackStock, names: Map<PackKey, string>) =

    /// The product noun printed as the wordmark.
    member _.Noun = noun

    /// The stock every pack without a fixed material follows.
    member _.Stock = stock

    /// The printed name of a pack.
    member _.Name(key: PackKey) = names[key]

    /// The same profile printed on another stock.
    member _.On(stock) = PackProfile(noun, stock, names)

    /// A profile printing every pack under a name.
    static member Create(noun, stock, names: Map<PackKey, string>) =
        if String.IsNullOrWhiteSpace noun then
            invalidArg (nameof noun) "a deployment needs a product noun"

        let absent =
            Enum.GetValues<PackKey>()
            |> Array.filter (fun key ->
                match Map.tryFind key names with
                | Some name -> String.IsNullOrWhiteSpace name
                | None -> true)

        if not (Array.isEmpty absent) then
            invalidArg
                (nameof names)
                $"""every pack needs a printed name; missing {String.Join(", ", absent)}"""

        PackProfile(noun, stock, names)

    /// The default Blokemon profile.
    static member Blokemon(stock) =
        PackProfile.Create(
            "Blokemon",
            stock,
            Map
                [ PackKey.Booster, "The Oche"
                  PackKey.StarterDeck, "Starter Deck"
                  PackKey.OneForTheRoad, "One for the Road"
                  PackKey.RoundOfThree, "Round"
                  PackKey.LockIn, "Lock-In"
                  PackKey.Session, "Session" ]
        )
