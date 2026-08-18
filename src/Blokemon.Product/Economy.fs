namespace Blokemon.Product

open System

/// How much product a profile is allowed to take.
type EconomyMode =
    | Unlimited = 0
    | ClassicScarcity = 1

/// Why a set of economy rules could not be built.
type EconomyRulesFailure =
    | UnknownMode = 0
    | PackAllowanceOutOfRange = 1

/// The allowances a profile plays under.
type EconomyRules =
    private
        { mode: EconomyMode
          packAllowance: Nullable<int>
          starterDeckClaimAllowance: Nullable<int> }

    /// The pack allowance a Classic profile takes when none is configured.
    static member DefaultClassicPackAllowance = 10

    static member private ClassicStarterDeckClaimAllowance = 1

    /// The rules that cap nothing.
    static member val Unlimited =
        { mode = EconomyMode.Unlimited
          packAllowance = Nullable()
          starterDeckClaimAllowance = Nullable() }

    member this.Mode = this.mode

    // Both allowances are null exactly when the mode grants them without limit.
    member this.PackAllowance = this.packAllowance

    member this.StarterDeckClaimAllowance = this.starterDeckClaimAllowance

    member this.PersistedPackAllowance =
        if this.packAllowance.HasValue then
            this.packAllowance.Value
        else
            0

    static member op_Equality(left: EconomyRules, right: EconomyRules) = left.Equals(right)

    static member op_Inequality(left: EconomyRules, right: EconomyRules) = not (left.Equals(right))

    /// The Classic rules for a pack allowance.
    static member Classic(packAllowance: int) =
        EconomyRules.Create(EconomyMode.ClassicScarcity, packAllowance)

    /// The rules for a mode and its pack allowance.
    static member Create(mode: EconomyMode, packAllowance: int) =
        if not (Enum.IsDefined mode) then
            DomainResult.Failed EconomyRulesFailure.UnknownMode
        elif mode = EconomyMode.Unlimited then
            if packAllowance = 0 then
                DomainResult.Succeeded EconomyRules.Unlimited
            else
                DomainResult.Failed EconomyRulesFailure.PackAllowanceOutOfRange
        elif packAllowance < 0 then
            DomainResult.Failed EconomyRulesFailure.PackAllowanceOutOfRange
        else
            DomainResult.Succeeded
                { mode = mode
                  packAllowance = Nullable packAllowance
                  starterDeckClaimAllowance = Nullable EconomyRules.ClassicStarterDeckClaimAllowance }

    static member internal Remaining(allowance: Nullable<int>, used: int) =
        if allowance.HasValue then
            Nullable(max 0 (allowance.Value - used))
        else
            Nullable()
