using System.Collections.Immutable;

namespace Blokemon.CardGen.Domain;

/// <summary>An entry in a card's mechanics region.</summary>
public abstract record CardEntry
{
    private CardEntry(MechanicalId id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    /// <summary>The mechanical identifier of the entry.</summary>
    public MechanicalId Id { get; }

    /// <summary>The printed name of the entry.</summary>
    public string Name { get; }

    /// <summary>Applies the function matching this entry.</summary>
    /// <typeparam name="TResult">The result of the applied function.</typeparam>
    /// <param name="ability">Applied to an Ability.</param>
    /// <param name="attack">Applied to an Attack.</param>
    /// <param name="rule">Applied to a Rule.</param>
    /// <returns>The result of the applied function.</returns>
    public abstract TResult Match<TResult>(
        Func<Ability, TResult> ability,
        Func<Attack, TResult> attack,
        Func<Rule, TResult> rule
    );

    /// <summary>An Ability entry.</summary>
    /// <param name="Id">The mechanical identifier of the entry.</param>
    /// <param name="Name">The printed name of the entry.</param>
    /// <param name="EffectText">The printed effect text.</param>
    public sealed record Ability(MechanicalId Id, string Name, string EffectText)
        : CardEntry(Id, Name)
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<Ability, TResult> ability,
            Func<Attack, TResult> attack,
            Func<Rule, TResult> rule
        ) => ability(this);
    }

    /// <summary>An Attack entry.</summary>
    /// <param name="Id">The mechanical identifier of the entry.</param>
    /// <param name="Name">The printed name of the entry.</param>
    /// <param name="EnergyCost">The Energy printed in the gutter.</param>
    /// <param name="Damage">The printed Damage.</param>
    /// <param name="EffectText">The printed effect text, absent on a pure-Damage Attack.</param>
    public sealed record Attack(
        MechanicalId Id,
        string Name,
        ImmutableArray<BlokemonType> EnergyCost,
        Damage Damage,
        string? EffectText
    ) : CardEntry(Id, Name)
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<Ability, TResult> ability,
            Func<Attack, TResult> attack,
            Func<Rule, TResult> rule
        ) => attack(this);
    }

    /// <summary>A Rule entry.</summary>
    /// <param name="Id">The mechanical identifier of the entry.</param>
    /// <param name="Name">The printed name of the entry.</param>
    /// <param name="EffectText">The printed effect text.</param>
    public sealed record Rule(MechanicalId Id, string Name, string EffectText) : CardEntry(Id, Name)
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<Ability, TResult> ability,
            Func<Attack, TResult> attack,
            Func<Rule, TResult> rule
        ) => rule(this);
    }
}
