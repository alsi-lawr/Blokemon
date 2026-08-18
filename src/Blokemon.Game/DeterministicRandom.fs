namespace Blokemon.Game

/// The match's only source of chance: a splitmix64 stream seeded from the match seed, snapshotted
/// into the state after every command so a replay draws exactly the same cards.
type internal DeterministicRandom(state: MatchRandomState) =
    let mutable current = state.State
    let mutable consumptionIndex = state.ConsumptionIndex

    member _.ConsumptionIndex = consumptionIndex

    member _.Snapshot = MatchRandomState(current, consumptionIndex)

    member private _.NextUInt64() =
        current <- current + 0x9E3779B97F4A7C15UL
        let mutable value = current
        value <- (value ^^^ (value >>> 30)) * 0xBF58476D1CE4E5B9UL
        value <- (value ^^^ (value >>> 27)) * 0x94D049BB133111EBUL
        value ^^^ (value >>> 31)

    member this.NextInt(exclusiveMaximum: int) =
        let bound = uint64 exclusiveMaximum
        let threshold = (0UL - bound) % bound
        let mutable result = ValueNone

        while result.IsNone do
            let value = this.NextUInt64()

            if value >= threshold then
                consumptionIndex <- consumptionIndex + 1
                result <- ValueSome(int (value % bound))

        result.Value
