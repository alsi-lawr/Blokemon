using System.Diagnostics;

namespace Blokemon.Game;

public abstract record CpuDecision
{
    private CpuDecision() { }

    public sealed record Selected(LegalAction Action) : CpuDecision;

    public sealed record NoLegalAction : CpuDecision;

    public TResult Match<TResult>(
        Func<Selected, TResult> selected,
        Func<NoLegalAction, TResult> noLegalAction
    ) =>
        this switch
        {
            Selected value => selected(value),
            NoLegalAction value => noLegalAction(value),
            _ => throw new UnreachableException(),
        };
}

public sealed class DeterministicCpu
{
    private static readonly IReadOnlyDictionary<LegalActionKind, int> _priority = new Dictionary<
        LegalActionKind,
        int
    >
    {
        [LegalActionKind.ChooseMulliganBonus] = 0,
        [LegalActionKind.ChooseOpening] = 1,
        [LegalActionKind.ChooseReplacement] = 2,
        [LegalActionKind.ResolveEffectChoice] = 3,
        [LegalActionKind.ResolveKnockoutTrigger] = 4,
        [LegalActionKind.ResolveBarChitTrigger] = 5,
        [LegalActionKind.PlayBloke] = 6,
        [LegalActionKind.Promote] = 7,
        [LegalActionKind.AttachVim] = 8,
        [LegalActionKind.PlayKit] = 9,
        [LegalActionKind.UsePartyTrick] = 10,
        [LegalActionKind.Attack] = 11,
        [LegalActionKind.Taxi] = 12,
        [LegalActionKind.ChuckFossil] = 13,
        [LegalActionKind.EndRound] = 14,
    };

    public CpuDecision Choose(MatchEngine engine, MatchState state, PlayerId actor)
    {
        // Resignation is voluntary and never automated, so the policy sees exactly the action
        // set it saw before resignation existed.
        var selected = engine
            .GetLegalActions(state, actor)
            .Where(static action => action.Kind != LegalActionKind.Resign)
            .OrderBy(action => _priority[action.Kind])
            .ThenBy(static action => action.StableKey, StringComparer.Ordinal)
            .FirstOrDefault();
        return selected is null
            ? new CpuDecision.NoLegalAction()
            : new CpuDecision.Selected(selected);
    }
}
