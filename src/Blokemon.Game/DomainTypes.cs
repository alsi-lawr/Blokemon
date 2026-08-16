using System.Collections;
using System.Collections.Immutable;
using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public readonly record struct MatchId(string Value);

public readonly record struct PlayerId(string Value) : IComparable<PlayerId>
{
    public int CompareTo(PlayerId other) => StringComparer.Ordinal.Compare(Value, other.Value);
}

public readonly record struct CommandId(string Value);

public readonly record struct CardInstanceId(string Value) : IComparable<CardInstanceId>
{
    public int CompareTo(CardInstanceId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);
}

public readonly record struct MechanicalCardId(string Value);

public readonly record struct EffectId(string Value);

public readonly record struct EffectChoiceId(string Value);

public readonly record struct MatchRevision(long Value)
{
    public MatchRevision Next() => new(Value + 1);
}

public readonly record struct MatchSeed(ulong Value);

public readonly record struct MatchRandomState(ulong State, int ConsumptionIndex);

public readonly struct FrozenList<T> : IReadOnlyList<T>, IEquatable<FrozenList<T>>
{
    private readonly ImmutableArray<T> _items;

    private FrozenList(ImmutableArray<T> items)
    {
        _items = items;
    }

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    private ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    public static FrozenList<T> Empty => new([]);

    public static FrozenList<T> Create(IEnumerable<T> items) => new([.. items]);

    public static FrozenList<T> Create(params T[] items) => new([.. items]);

    public ImmutableArray<T> ToImmutableArray() => Items;

    public bool Equals(FrozenList<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is FrozenList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(FrozenList<T> left, FrozenList<T> right) => left.Equals(right);

    public static bool operator !=(FrozenList<T> left, FrozenList<T> right) => !left.Equals(right);
}

public enum CardKind
{
    Bloke,
    Vim,
    Kit,
}

public enum CardZone
{
    Stack,
    Mitt,
    Oche,
    Booth,
    Attached,
    EmptiesTray,
    Local,
    BarChit,
}

public enum MatchPhase
{
    MulliganBonus,
    OpeningPlacement,
    Playing,
    AwaitingEffectChoice,
    AwaitingTriggerChoice,
    AwaitingReplacement,
    Complete,
}

public enum DamageKind
{
    Attack,
    BoothAttack,
    PlacedCounter,
    RoughState,
    SelfDamage,
}

public enum DrawReason
{
    OpeningMitt,
    MulliganBonus,
    RequiredRoundDraw,
    Effect,
}

public enum TemporaryEffectKind
{
    PreventDamage,
    PreventEffects,
    ReduceDamage,
    ModifyAttackCost,
    ModifyTaxiFare,
    ModifyStayingPower,
    ModifySoftSpot,
    IgnoreStubbornStreak,
    IgnoreSoftSpotAndStubbornStreak,
    RestrictAttack,
    RestrictAttackOnBeerMat,
    RestrictTaxi,
    RestrictKit,
    RestrictLocal,
    RestrictEmptiesRecovery,
    ForceBeerMatBlank,
    ReflectAttackDamage,
    ScaleNextAttackDamage,
    ContinuousPartyTrick,
    EndRoundEffect,
}

public enum EffectDuration
{
    CurrentResolution,
    UntilEndOfRound,
    UntilEndOfOpponentsNextRound,
    WhileSourceInPlay,
    WhileTargetInPlay,
}

public sealed record RoughStateEntry(BlokemonRoughState State, int AppliedAtOwnerRound);

public sealed record TemporaryEffect(
    EffectId SourceEffect,
    CardInstanceId SourceCard,
    PlayerId Owner,
    CardInstanceId? TargetCard,
    TemporaryEffectKind Kind,
    int Amount,
    FrozenList<BlokemonMechanicalType> MechanicalTypes,
    FrozenList<BlokemonRoughState> RoughStates,
    FrozenList<MechanicalCardId> RelatedCards,
    FrozenList<BlokemonCondition> Conditions,
    EffectDuration Duration,
    int AppliesFromRound,
    int ExpiresAfterRound
);

public sealed record DamageAllocation(CardInstanceId Card, int Counters);

public sealed record VimAttachment(CardInstanceId Vim, CardInstanceId Bloke);

public sealed record CardMechanicalTypes(
    CardInstanceId Card,
    FrozenList<BlokemonMechanicalType> Types
);

public sealed record PendingEffectResolution(
    MatchCommand Command,
    CardInstanceId Source,
    EffectId Effect,
    PlayerId Chooser,
    FrozenList<ChoiceRequirement> Requirements,
    FrozenList<bool> BeerMatResults,
    bool AttackStarted
);

internal sealed record TriggerContext(
    CardInstanceId? KnockedOutBloke = null,
    CardInstanceId? AttackingBloke = null
);

public sealed record PendingKnockoutResolution(
    CardInstanceId KnockedOutCard,
    FrozenList<CardInstanceId> RemainingKnockouts,
    FrozenList<CardInstanceId> TriggerSources,
    CardInstanceId TriggerSource,
    EffectId TriggerEffect,
    PlayerId Chooser,
    FrozenList<CardInstanceId> EligibleVim,
    CardInstanceId AttackingCard,
    bool FinishRoundAfterResolution,
    FrozenList<CardInstanceId> AttackDamageTargets,
    int ExtraBarChits
);

public sealed record PendingBarChitResolution(
    PlayerId Player,
    CardInstanceId Card,
    EffectId Effect,
    bool FinishRoundAfterResolution
);
