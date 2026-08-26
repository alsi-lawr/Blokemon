namespace Blokemon.ReferenceModel

type internal ReferenceRandom(initial: CanonicalRandomState) =
    let mutable current = initial.State
    let mutable consumptionIndex = initial.ConsumptionIndex

    member _.Snapshot =
        { State = current
          ConsumptionIndex = consumptionIndex }

    member private _.NextUInt64() =
        current <- current + 0x9E3779B97F4A7C15UL
        let mutable value = current
        value <- (value ^^^ (value >>> 30)) * 0xBF58476D1CE4E5B9UL
        value <- (value ^^^ (value >>> 27)) * 0x94D049BB133111EBUL
        value ^^^ (value >>> 31)

    member this.NextInt(exclusiveMaximum: int) =
        if exclusiveMaximum <= 0 then
            invalidArg (nameof exclusiveMaximum) "The random bound must be positive."

        let bound = uint64 exclusiveMaximum
        let threshold = (0UL - bound) % bound
        let mutable accepted = false
        let mutable result = 0

        while not accepted do
            let value = this.NextUInt64()

            if value >= threshold then
                consumptionIndex <- consumptionIndex + 1
                result <- int (value % bound)
                accepted <- true

        result
