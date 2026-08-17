namespace Blokemon.Core.SetDesign

/// Attack shapes the owned validators read off the mechanical authority.
module internal BlokemonAttackSemantics =

    /// Whether an attack is nothing but its printed Damage against the other Oche.
    let isPureDamageAttack (attack: BlokemonAttack) =
        not attack.CanBeUsedFromBench
        && not attack.VariablePrintedDamage
        && (match attack.Program with
            | [| instruction |] ->
                instruction.Opcode = BlokemonOpcode.DealPrintedDamage
                && instruction.ValueSource = BlokemonValueSource.PrintedDamage
                && instruction.Targets = [| BlokemonTarget.OtherOche |]
                && instruction.Selection = BlokemonSelection.All
                && instruction.TargetCount = 1
                && instruction.Predicates.Length = 0
                && instruction.MechanicalTypes.Length = 0
                && instruction.RoughStates.Length = 0
                && instruction.RelatedIds.Length = 0
                && instruction.Then.Length = 0
                && instruction.Otherwise.Length = 0
                && instruction.Amount = attack.PrintedDamage
            | _ -> false)
