namespace Blokemon.CardGen.Domain

/// The printed physical description of a collectible.
type Stature =
    {
        /// The whole feet of the length.
        Feet: int

        /// The remaining inches of the length.
        Inches: int

        /// The weight in pounds.
        Pounds: int
    }

    /// The length as printed on the identity strip.
    member this.PrintedLength() =
        if this.Inches = 0 then
            $"{this.Feet}'"
        else
            $"{this.Feet}' {this.Inches}\""

    /// The weight as printed on the identity strip.
    member this.PrintedWeight() = $"{this.Pounds} lbs."

/// The species epithet and stature printed on the identity strip.
type CardProfile =
    {
        /// The epithet naming the collectible's species.
        Subtype: string

        /// The printed length and weight.
        Stature: Stature
    }

    /// The identity strip line.
    member this.PrintedIdentity() =
        let length = this.Stature.PrintedLength()
        let weight = this.Stature.PrintedWeight()
        $"{this.Subtype} Blokemon. Length: {length}, Weight: {weight}"
