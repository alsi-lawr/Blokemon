namespace Blokemon.Product;

public enum EconomyMode
{
    Unlimited,
    ClassicScarcity,
}

public enum EconomyRulesFailure
{
    UnknownMode,
    PackAllowanceOutOfRange,
}

public sealed record EconomyRules
{
    public const int DefaultClassicPackAllowance = 10;

    private const int _classicStarterDeckClaimAllowance = 1;

    private EconomyRules(EconomyMode mode, int? packAllowance, int? starterDeckClaimAllowance)
    {
        Mode = mode;
        PackAllowance = packAllowance;
        StarterDeckClaimAllowance = starterDeckClaimAllowance;
    }

    public static EconomyRules Unlimited { get; } = new(EconomyMode.Unlimited, null, null);

    public EconomyMode Mode { get; }

    // Both allowances are null exactly when the mode grants them without limit.
    public int? PackAllowance { get; }

    public int? StarterDeckClaimAllowance { get; }

    public int PersistedPackAllowance => PackAllowance ?? 0;

    public static DomainResult<EconomyRules, EconomyRulesFailure> Classic(int packAllowance) =>
        Create(EconomyMode.ClassicScarcity, packAllowance);

    public static DomainResult<EconomyRules, EconomyRulesFailure> Create(
        EconomyMode mode,
        int packAllowance
    )
    {
        if (!Enum.IsDefined(mode))
        {
            return DomainResult<EconomyRules, EconomyRulesFailure>.Failure(
                EconomyRulesFailure.UnknownMode
            );
        }

        if (mode == EconomyMode.Unlimited)
        {
            return packAllowance == 0
                ? DomainResult<EconomyRules, EconomyRulesFailure>.Success(Unlimited)
                : DomainResult<EconomyRules, EconomyRulesFailure>.Failure(
                    EconomyRulesFailure.PackAllowanceOutOfRange
                );
        }

        return packAllowance < 0
            ? DomainResult<EconomyRules, EconomyRulesFailure>.Failure(
                EconomyRulesFailure.PackAllowanceOutOfRange
            )
            : DomainResult<EconomyRules, EconomyRulesFailure>.Success(
                new EconomyRules(mode, packAllowance, _classicStarterDeckClaimAllowance)
            );
    }

    internal static int? Remaining(int? allowance, int used) =>
        allowance is { } limit ? Math.Max(0, limit - used) : null;
}
