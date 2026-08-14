using System.Diagnostics;
using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public abstract record EffectChoice
{
    private EffectChoice() { }

    public abstract EffectChoiceId Id { get; }

    public sealed record Optional(EffectChoiceId ChoiceId, bool IsAccepted) : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record Amount(EffectChoiceId ChoiceId, int Value) : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record Cards(EffectChoiceId ChoiceId, FrozenList<CardInstanceId> Values)
        : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record MechanicalType(EffectChoiceId ChoiceId, BlokemonMechanicalType Value)
        : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record Attack(EffectChoiceId ChoiceId, EffectId Value) : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record Distribution(EffectChoiceId ChoiceId, FrozenList<DamageAllocation> Values)
        : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public sealed record Attachments(EffectChoiceId ChoiceId, FrozenList<VimAttachment> Values)
        : EffectChoice
    {
        public override EffectChoiceId Id => ChoiceId;
    }

    public TResult Match<TResult>(
        Func<Optional, TResult> optional,
        Func<Amount, TResult> amount,
        Func<Cards, TResult> cards,
        Func<MechanicalType, TResult> mechanicalType,
        Func<Attack, TResult> attack,
        Func<Distribution, TResult> distribution,
        Func<Attachments, TResult> attachments
    ) =>
        this switch
        {
            Optional value => optional(value),
            Amount value => amount(value),
            Cards value => cards(value),
            MechanicalType value => mechanicalType(value),
            Attack value => attack(value),
            Distribution value => distribution(value),
            Attachments value => attachments(value),
            _ => throw new UnreachableException(),
        };
}

public abstract record MatchCommand
{
    private MatchCommand() { }

    public abstract CommandId Id { get; init; }

    public abstract MatchId MatchId { get; init; }

    public abstract PlayerId Actor { get; init; }

    public abstract MatchRevision ExpectedRevision { get; init; }

    public abstract FrozenList<EffectChoice> Choices { get; init; }

    public sealed record ChooseMulliganBonus(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        int CardsToDraw
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record ChooseOpening(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Oche,
        FrozenList<CardInstanceId> Booth
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record AttachVim(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Vim,
        CardInstanceId Bloke
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record PlayBloke(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Bloke
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record Promote(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Promotion,
        CardInstanceId Bloke,
        FrozenList<EffectChoice> Choices
    ) : MatchCommand;

    public sealed record PlayKit(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Kit,
        CardInstanceId? Target,
        FrozenList<EffectChoice> Choices
    ) : MatchCommand;

    public sealed record Taxi(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId BoothBloke,
        FrozenList<CardInstanceId> VimToChuck
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record UsePartyTrick(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Source,
        EffectId Effect,
        FrozenList<EffectChoice> Choices
    ) : MatchCommand;

    public sealed record Attack(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Attacker,
        EffectId AttackId,
        FrozenList<EffectChoice> Choices
    ) : MatchCommand;

    public sealed record ChuckFossil(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId Fossil
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record EndRound(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record ChooseReplacement(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId BoothBloke
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record ResolveEffectChoice(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        FrozenList<EffectChoice> Choices
    ) : MatchCommand;

    public sealed record ResolveKnockoutTrigger(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        CardInstanceId? Vim
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public sealed record ResolveBarChitTrigger(
        CommandId Id,
        MatchId MatchId,
        PlayerId Actor,
        MatchRevision ExpectedRevision,
        bool PutOntoBooth
    ) : MatchCommand
    {
        public override FrozenList<EffectChoice> Choices { get; init; } = [];
    }

    public TResult Match<TResult>(
        Func<ChooseMulliganBonus, TResult> chooseMulliganBonus,
        Func<ChooseOpening, TResult> chooseOpening,
        Func<AttachVim, TResult> attachVim,
        Func<PlayBloke, TResult> playBloke,
        Func<Promote, TResult> promote,
        Func<PlayKit, TResult> playKit,
        Func<Taxi, TResult> taxi,
        Func<UsePartyTrick, TResult> usePartyTrick,
        Func<Attack, TResult> attack,
        Func<ChuckFossil, TResult> chuckFossil,
        Func<EndRound, TResult> endRound,
        Func<ChooseReplacement, TResult> chooseReplacement,
        Func<ResolveEffectChoice, TResult> resolveEffectChoice,
        Func<ResolveKnockoutTrigger, TResult> resolveKnockoutTrigger,
        Func<ResolveBarChitTrigger, TResult> resolveBarChitTrigger
    ) =>
        this switch
        {
            ChooseMulliganBonus value => chooseMulliganBonus(value),
            ChooseOpening value => chooseOpening(value),
            AttachVim value => attachVim(value),
            PlayBloke value => playBloke(value),
            Promote value => promote(value),
            PlayKit value => playKit(value),
            Taxi value => taxi(value),
            UsePartyTrick value => usePartyTrick(value),
            Attack value => attack(value),
            ChuckFossil value => chuckFossil(value),
            EndRound value => endRound(value),
            ChooseReplacement value => chooseReplacement(value),
            ResolveEffectChoice value => resolveEffectChoice(value),
            ResolveKnockoutTrigger value => resolveKnockoutTrigger(value),
            ResolveBarChitTrigger value => resolveBarChitTrigger(value),
            _ => throw new UnreachableException(),
        };
}

public enum ChoiceRequirementKind
{
    Optional,
    Amount,
    Cards,
    MechanicalType,
    Attack,
    Distribution,
    Attachments,
}

public sealed record ChoiceRequirement(
    EffectChoiceId Id,
    ChoiceRequirementKind Kind,
    PlayerId Chooser,
    int Minimum,
    int Maximum,
    FrozenList<CardInstanceId> EligibleCards,
    FrozenList<BlokemonMechanicalType> EligibleMechanicalTypes,
    FrozenList<EffectId> EligibleEffects,
    EffectChoiceId? DependsOnOptional,
    FrozenList<CardInstanceId> EligibleTargets = default,
    bool RequireDifferentMechanicalTypes = false,
    FrozenList<CardMechanicalTypes> EligibleCardTypes = default
);

public enum LegalActionKind
{
    ChooseMulliganBonus,
    ChooseOpening,
    ChooseReplacement,
    AttachVim,
    PlayBloke,
    Promote,
    PlayKit,
    UsePartyTrick,
    Attack,
    Taxi,
    ChuckFossil,
    EndRound,
    ResolveEffectChoice,
    ResolveKnockoutTrigger,
    ResolveBarChitTrigger,
}

public sealed record LegalAction(
    LegalActionKind Kind,
    MatchCommand Command,
    FrozenList<ChoiceRequirement> ChoiceRequirements,
    string StableKey
);
