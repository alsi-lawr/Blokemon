using System.Diagnostics;
using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public sealed record InterpreterAuditIssue(string Code, EffectId? Effect);

public sealed record InterpreterAudit(
    int EffectCount,
    int InstructionCount,
    FrozenList<InterpreterAuditIssue> Issues
)
{
    public bool IsInventoryComplete => Issues.Count == 0;
}

public sealed record EffectInvocation(
    PlayerId Actor,
    CardInstanceId Source,
    EffectId Effect,
    FrozenList<EffectChoice> Choices
);

public sealed class BlokemonInterpreter
{
    private readonly AuthorityCatalog _catalog;

    public BlokemonInterpreter(BlokemonRuntimeManifest authority)
    {
        _catalog = new AuthorityCatalog(authority);
    }

    public InterpreterAudit AuditAuthority()
    {
        var issues = new List<InterpreterAuditIssue>();
        var effectCount = 0;
        var instructionCount = 0;
        var declared = _catalog.Manifest.BaseRules.OpcodeInventory.ToHashSet();
        foreach (var opcode in Enum.GetValues<BlokemonOpcode>())
        {
            if (!declared.Contains(opcode))
            {
                issues.Add(new InterpreterAuditIssue("opcode-not-declared", null));
            }
        }

        foreach (var declaredOpcode in declared)
        {
            if (!Enum.IsDefined(declaredOpcode))
            {
                issues.Add(new InterpreterAuditIssue("unknown-declared-opcode", null));
            }
        }

        foreach (var (effect, program) in AllPrograms())
        {
            effectCount++;
            instructionCount += AuditProgram(effect, program, issues);
        }

        foreach (var card in _catalog.Manifest.Collectibles)
        {
            foreach (var trick in card.PartyTricks)
            {
                AuditTrigger(trick, issues);
            }
        }

        foreach (var trick in _catalog.Manifest.Kits.SelectMany(static card => card.PartyTricks))
        {
            AuditTrigger(trick, issues);
        }

        return new InterpreterAudit(
            effectCount,
            instructionCount,
            FrozenList<InterpreterAuditIssue>.Create(issues)
        );
    }

    public FrozenList<ChoiceRequirement> GetChoiceRequirements(
        MatchState state,
        EffectInvocation invocation
    )
    {
        var source = state.Cards.SingleOrDefault(card => card.Id == invocation.Source);
        if (source is null)
        {
            return [];
        }

        var program = FindProgram(invocation.Effect);
        if (program is null)
        {
            return [];
        }

        var builder = new MatchBuilder(state, _catalog);
        return InspectChoices(builder, invocation.Actor, source, invocation.Effect, program);
    }

    internal FrozenList<ChoiceRequirement> InspectChoices(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        EffectId effect,
        BlokemonEffectInstruction[] program
    )
    {
        var requirements = new List<ChoiceRequirement>();
        InspectProgram(builder, actor, source, effect, program, "root", null, requirements);
        return FrozenList<ChoiceRequirement>.Create(
            requirements.DistinctBy(static requirement => requirement.Id)
        );
    }

    internal InterpreterExecution Execute(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        EffectId effect,
        BlokemonEffectInstruction[] program,
        FrozenList<EffectChoice> choices,
        bool isAttack,
        bool isHouseRule = false,
        HashSet<EffectId>? copyStack = null
    )
    {
        var requirements = InspectChoices(builder, actor, source, effect, program);
        var scopedChoices = FrozenList<EffectChoice>.Create(
            choices.Where(choice =>
                choice.Id.Value.StartsWith(effect.Value + ":", StringComparison.Ordinal)
            )
        );
        var choiceValidation = ValidateChoices(scopedChoices, requirements);
        if (choiceValidation is not null)
        {
            return new InterpreterExecution(false, choiceValidation.Value, requirements);
        }

        var runtime = new EffectRuntime(
            builder,
            _catalog,
            actor,
            source,
            effect,
            scopedChoices,
            isAttack,
            isHouseRule,
            copyStack ?? []
        );
        ExecuteProgram(runtime, program, "root");
        if (runtime.Rejection is { } rejection)
        {
            return new InterpreterExecution(false, rejection, requirements);
        }

        ResolveDamage(runtime);
        return new InterpreterExecution(
            true,
            null,
            requirements,
            FrozenList<CardInstanceId>.Create(runtime.ForcedSendHome.Order()),
            runtime.SourceChucked
        );
    }

    internal CommandRejectionCode? ValidateChoiceSubmission(
        FrozenList<EffectChoice> choices,
        FrozenList<ChoiceRequirement> requirements,
        PlayerId chooser
    )
    {
        var owned = FrozenList<ChoiceRequirement>.Create(
            requirements.Where(requirement => requirement.Chooser == chooser)
        );
        if (
            choices.Any(choice =>
                requirements.Any(requirement => requirement.Id == choice.Id)
                && !owned.Any(requirement => requirement.Id == choice.Id)
            )
        )
        {
            return CommandRejectionCode.WrongChooser;
        }

        if (choices.Any(choice => !requirements.Any(requirement => requirement.Id == choice.Id)))
        {
            return CommandRejectionCode.InvalidChoice;
        }

        return ValidateChoices(choices, owned);
    }

    private CommandRejectionCode? ValidateChoices(
        FrozenList<EffectChoice> choices,
        FrozenList<ChoiceRequirement> requirements
    )
    {
        foreach (var requirement in requirements)
        {
            if (requirement.DependsOnOptional is { } dependency)
            {
                var optional = choices
                    .OfType<EffectChoice.Optional>()
                    .SingleOrDefault(choice => choice.Id == dependency);
                if (optional is null)
                {
                    return CommandRejectionCode.ChoiceRequired;
                }

                if (!optional.IsAccepted)
                {
                    continue;
                }
            }

            var matching = choices.Where(choice => choice.Id == requirement.Id).ToArray();
            if (matching.Length == 0)
            {
                return CommandRejectionCode.ChoiceRequired;
            }

            if (matching.Length != 1 || !ChoiceIsValid(matching[0], requirement))
            {
                return CommandRejectionCode.InvalidChoice;
            }
        }

        if (choices.Any(choice => !requirements.Any(requirement => requirement.Id == choice.Id)))
        {
            return CommandRejectionCode.InvalidChoice;
        }

        return null;
    }

    private bool ChoiceIsValid(EffectChoice choice, ChoiceRequirement requirement) =>
        choice.Match(
            optional => requirement.Kind == ChoiceRequirementKind.Optional,
            amount =>
                requirement.Kind == ChoiceRequirementKind.Amount
                && amount.Value >= requirement.Minimum
                && amount.Value <= requirement.Maximum,
            cards =>
                requirement.Kind == ChoiceRequirementKind.Cards
                && cards.Values.Count >= requirement.Minimum
                && cards.Values.Count <= requirement.Maximum
                && cards.Values.Distinct().Count() == cards.Values.Count
                && cards.Values.All(requirement.EligibleCards.Contains)
                && (
                    !requirement.RequireDifferentMechanicalTypes
                    || HaveDifferentMechanicalTypes(cards.Values, requirement)
                ),
            mechanicalType =>
                requirement.Kind == ChoiceRequirementKind.MechanicalType
                && requirement.EligibleMechanicalTypes.Contains(mechanicalType.Value),
            attack =>
                requirement.Kind == ChoiceRequirementKind.Attack
                && requirement.EligibleEffects.Contains(attack.Value),
            distribution =>
                requirement.Kind == ChoiceRequirementKind.Distribution
                && distribution.Values.Sum(static allocation => allocation.Counters)
                    == requirement.Maximum
                && distribution
                    .Values.Select(static allocation => allocation.Card)
                    .Distinct()
                    .Count() == distribution.Values.Count
                && distribution.Values.All(allocation =>
                    allocation.Counters >= 0 && requirement.EligibleCards.Contains(allocation.Card)
                ),
            attachments =>
                requirement.Kind == ChoiceRequirementKind.Attachments
                && attachments.Values.Count >= requirement.Minimum
                && attachments.Values.Count <= requirement.Maximum
                && attachments.Values.Select(static placement => placement.Vim).Distinct().Count()
                    == attachments.Values.Count
                && attachments.Values.All(placement =>
                    requirement.EligibleCards.Contains(placement.Vim)
                    && requirement.EligibleTargets.Contains(placement.Bloke)
                )
        );

    private void InspectProgram(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        EffectId effect,
        BlokemonEffectInstruction[] program,
        string parentPath,
        EffectChoiceId? optionalDependency,
        List<ChoiceRequirement> requirements
    )
    {
        for (var index = 0; index < program.Length; index++)
        {
            var instruction = program[index];
            var path = $"{parentPath}/{index}";
            var dependency = optionalDependency;
            if (
                instruction.Opcode == BlokemonOpcode.Conditional
                && instruction.Predicates.Any(static predicate =>
                    predicate.Condition == BlokemonCondition.Optional
                )
            )
            {
                var choiceId = ChoiceId(effect, path, "optional");
                requirements.Add(
                    new ChoiceRequirement(
                        choiceId,
                        ChoiceRequirementKind.Optional,
                        actor,
                        0,
                        1,
                        [],
                        [],
                        [],
                        optionalDependency
                    )
                );
                dependency = choiceId;
            }

            InspectInstructionChoice(
                builder,
                actor,
                source,
                effect,
                instruction,
                path,
                dependency,
                requirements
            );
            InspectProgram(
                builder,
                actor,
                source,
                effect,
                instruction.Then,
                path + "/then",
                dependency,
                requirements
            );
            InspectProgram(
                builder,
                actor,
                source,
                effect,
                instruction.Otherwise,
                path + "/otherwise",
                optionalDependency,
                requirements
            );
        }
    }

    private void InspectInstructionChoice(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        EffectId effect,
        BlokemonEffectInstruction instruction,
        string path,
        EffectChoiceId? dependency,
        List<ChoiceRequirement> requirements
    )
    {
        if (
            instruction.Opcode == BlokemonOpcode.AttachVim
            && instruction.Selection == BlokemonSelection.AnyDistribution
        )
        {
            var eligibleVim = (
                instruction.Sources is { Length: > 0 }
                    ? ResolveCandidates(builder, actor, source, instruction)
                    : builder
                        .CardsIn(actor, CardZone.Stack)
                        .Take(instruction.Amount)
                        .Where(static card => card.Kind == CardKind.Vim)
            )
                .Select(static card => card.Id)
                .ToArray();
            var eligibleTargets = (
                instruction.Targets.Length > 0
                    ? instruction.Targets.SelectMany(target =>
                        ResolveTarget(builder, actor, source, instruction, target)
                    )
                    : InPlay(builder, actor)
            )
                .Where(IsInPlay)
                .Select(static card => card.Id)
                .Distinct()
                .Order()
                .ToArray();
            var required = instruction.Sources is { Length: > 0 }
                ? Math.Min(instruction.Amount, eligibleVim.Length)
                : eligibleVim.Length;
            requirements.Add(
                new ChoiceRequirement(
                    ChoiceId(effect, path, "attachments"),
                    ChoiceRequirementKind.Attachments,
                    actor,
                    required,
                    required,
                    FrozenList<CardInstanceId>.Create(eligibleVim),
                    [],
                    [],
                    dependency,
                    FrozenList<CardInstanceId>.Create(eligibleTargets)
                )
            );
            return;
        }

        if (
            instruction.Opcode == BlokemonOpcode.ModifySoftSpot
            && instruction.Selection == BlokemonSelection.Chosen
            && instruction.MechanicalTypes.Length > 1
        )
        {
            requirements.Add(
                new ChoiceRequirement(
                    ChoiceId(effect, path, "type"),
                    ChoiceRequirementKind.MechanicalType,
                    actor,
                    1,
                    1,
                    [],
                    FrozenList<BlokemonMechanicalType>.Create(instruction.MechanicalTypes),
                    [],
                    dependency
                )
            );
        }

        if (instruction.Opcode == BlokemonOpcode.CopyAttack)
        {
            var opponent = builder.Other(actor);
            var effects = builder
                .Oche(opponent)
                .Yield()
                .SelectMany(_catalog.Attacks)
                .Select(attack => new EffectId(attack.MechanicalId))
                .ToArray();
            if (effects.Length == 0)
            {
                return;
            }

            requirements.Add(
                new ChoiceRequirement(
                    ChoiceId(effect, path, "attack"),
                    ChoiceRequirementKind.Attack,
                    actor,
                    1,
                    1,
                    [],
                    [],
                    FrozenList<EffectId>.Create(effects),
                    dependency
                )
            );
            return;
        }

        if (instruction.Selection == BlokemonSelection.AnyDistribution)
        {
            if (instruction.Opcode != BlokemonOpcode.PlaceDamageCounters)
            {
                return;
            }

            var eligible = ResolveCandidates(builder, actor, source, instruction)
                .Select(static card => card.Id)
                .ToArray();
            if (eligible.Length == 0)
            {
                return;
            }

            requirements.Add(
                new ChoiceRequirement(
                    ChoiceId(effect, path, "distribution"),
                    ChoiceRequirementKind.Distribution,
                    actor,
                    0,
                    instruction.Amount,
                    FrozenList<CardInstanceId>.Create(eligible),
                    [],
                    [],
                    dependency
                )
            );
            return;
        }

        if (!InstructionOwnsCardChoice(instruction))
        {
            return;
        }

        var candidateCards = ResolveCandidates(builder, actor, source, instruction)
            .Distinct()
            .OrderBy(static card => card.Id)
            .ToArray();
        var candidates = candidateCards.Select(static card => card.Id).ToArray();
        var maximum = instruction.Selection switch
        {
            BlokemonSelection.Chosen => Math.Min(instruction.TargetCount, candidates.Length),
            BlokemonSelection.SeededRandom => 0,
            BlokemonSelection.OtherSideChosen => Math.Min(
                instruction.TargetCount,
                candidates.Length
            ),
            BlokemonSelection.AnyDistribution => 0,
            BlokemonSelection.UpTo => Math.Min(instruction.Amount, candidates.Length),
            BlokemonSelection.All => Math.Min(instruction.Amount, candidates.Length),
            BlokemonSelection.Top => 0,
            BlokemonSelection.BeerMat => 0,
            BlokemonSelection.UntilBlankSide => 0,
            _ => throw new UnreachableException(),
        };
        if (instruction.Destination == BlokemonEffectDestination.OwnBooth)
        {
            maximum = Math.Min(
                maximum,
                _catalog.Manifest.BaseRules.Opening.BoothLimit
                    - builder.CardsIn(actor, CardZone.Booth).Count()
            );
        }
        else if (instruction.Destination == BlokemonEffectDestination.OtherBooth)
        {
            maximum = Math.Min(
                maximum,
                _catalog.Manifest.BaseRules.Opening.BoothLimit
                    - builder.CardsIn(builder.Other(actor), CardZone.Booth).Count()
            );
        }

        if (maximum == 0)
        {
            return;
        }

        var minimum = instruction.Selection == BlokemonSelection.UpTo ? 1 : maximum;
        var chooser =
            instruction.Selection == BlokemonSelection.OtherSideChosen
                ? builder.Other(actor)
                : actor;
        requirements.Add(
            new ChoiceRequirement(
                ChoiceId(effect, path, "cards"),
                ChoiceRequirementKind.Cards,
                chooser,
                minimum,
                maximum,
                FrozenList<CardInstanceId>.Create(candidates),
                [],
                [],
                dependency,
                RequireDifferentMechanicalTypes: instruction.CardFilter?.DifferentMechanicalTypes
                    == true,
                EligibleCardTypes: FrozenList<CardMechanicalTypes>.Create(
                    candidateCards.Select(card => new CardMechanicalTypes(
                        card.Id,
                        _catalog.MechanicalTypes(card)
                    ))
                )
            )
        );
    }

    private static bool HaveDifferentMechanicalTypes(
        FrozenList<CardInstanceId> cards,
        ChoiceRequirement requirement
    )
    {
        var used = new HashSet<BlokemonMechanicalType>();
        foreach (var cardId in cards)
        {
            var types = requirement.EligibleCardTypes.SingleOrDefault(value =>
                value.Card == cardId
            );
            if (types is null || types.Types.Count == 0)
            {
                return false;
            }

            if (types.Types.Any(used.Contains))
            {
                return false;
            }

            used.UnionWith(types.Types);
        }

        return true;
    }

    private static bool InstructionOwnsCardChoice(BlokemonEffectInstruction instruction)
    {
        if (
            instruction.Opcode == BlokemonOpcode.MoveCards
            && instruction.Destination != BlokemonEffectDestination.Unspecified
            && instruction.Sources is not { Length: > 0 }
        )
        {
            return false;
        }

        if (
            instruction.Selection
            is not (
                BlokemonSelection.Chosen
                or BlokemonSelection.OtherSideChosen
                or BlokemonSelection.UpTo
            )
        )
        {
            return instruction.Opcode == BlokemonOpcode.ChuckCards
                && instruction.Targets.Contains(BlokemonTarget.OtherMitt);
        }

        return instruction.Opcode switch
        {
            BlokemonOpcode.DealPrintedDamage => false,
            BlokemonOpcode.AdjustDamage => false,
            BlokemonOpcode.ScaleDamage => false,
            BlokemonOpcode.DealBoothDamage => true,
            BlokemonOpcode.PlaceDamageCounters => true,
            BlokemonOpcode.DealSelfDamage => false,
            BlokemonOpcode.HealDamage => true,
            BlokemonOpcode.ApplyRoughState => true,
            BlokemonOpcode.ClearRoughState => false,
            BlokemonOpcode.DrawFromStack => false,
            BlokemonOpcode.SearchStack => true,
            BlokemonOpcode.ShuffleStack => false,
            BlokemonOpcode.RevealCards => false,
            BlokemonOpcode.MoveCards => true,
            BlokemonOpcode.ChuckCards => true,
            BlokemonOpcode.AttachVim => true,
            BlokemonOpcode.MoveVim => true,
            BlokemonOpcode.ChuckVim => true,
            BlokemonOpcode.SwapOche => true,
            BlokemonOpcode.PreventDamage => false,
            BlokemonOpcode.PreventEffects => false,
            BlokemonOpcode.ReduceDamage => false,
            BlokemonOpcode.ModifyAttackCost => false,
            BlokemonOpcode.ModifyTaxiFare => false,
            BlokemonOpcode.ModifyStayingPower => false,
            BlokemonOpcode.ModifySoftSpot => false,
            BlokemonOpcode.IgnoreStubbornStreak => false,
            BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak => false,
            BlokemonOpcode.RestrictAttack => false,
            BlokemonOpcode.RestrictTaxi => false,
            BlokemonOpcode.RestrictKit => false,
            BlokemonOpcode.RestrictLocal => false,
            BlokemonOpcode.RestrictEmptiesRecovery => false,
            BlokemonOpcode.BeerMatToss => false,
            BlokemonOpcode.RepeatUntilBlankSide => false,
            BlokemonOpcode.Conditional => false,
            BlokemonOpcode.SendHome => true,
            BlokemonOpcode.RecoverFromSendHome => false,
            BlokemonOpcode.CopyAttack => false,
            BlokemonOpcode.Demote => false,
            BlokemonOpcode.TransformFromStack => true,
            BlokemonOpcode.TakeExtraBarChit => false,
            BlokemonOpcode.PlayAsBloke => false,
            BlokemonOpcode.ChuckSelf => false,
            BlokemonOpcode.TriggeredPartyTrick => false,
            BlokemonOpcode.ContinuousPartyTrick => false,
            BlokemonOpcode.OncePerRound => false,
            BlokemonOpcode.EndRoundEffect => false,
            BlokemonOpcode.BigHitterBarChits => false,
            _ => throw new UnreachableException(),
        };
    }

    private void ExecuteProgram(
        EffectRuntime runtime,
        BlokemonEffectInstruction[] program,
        string parentPath
    )
    {
        for (var index = 0; index < program.Length && runtime.Rejection is null; index++)
        {
            var instruction = program[index];
            if (
                runtime.BeerMatGateParent == parentPath
                && runtime.BadgeSides == 0
                && instruction.Opcode != BlokemonOpcode.BeerMatToss
            )
            {
                continue;
            }

            ExecuteInstruction(runtime, instruction, $"{parentPath}/{index}");
        }
    }

    private void ExecuteInstruction(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        switch (instruction.Opcode)
        {
            case BlokemonOpcode.DealPrintedDamage:
                AddPendingDamage(runtime, instruction, path, instruction.Amount, DamageKind.Attack);
                break;
            case BlokemonOpcode.AdjustDamage:
                AdjustPendingDamage(
                    runtime,
                    instruction.Amount * ResolveValue(runtime, instruction)
                );
                break;
            case BlokemonOpcode.ScaleDamage:
                ExecuteScaleDamage(runtime, instruction, path);
                break;
            case BlokemonOpcode.DealBoothDamage:
                AddPendingDamage(
                    runtime,
                    instruction,
                    path,
                    instruction.Amount,
                    DamageKind.BoothAttack
                );
                break;
            case BlokemonOpcode.PlaceDamageCounters:
                if (!runtime.DeferringEndRound)
                {
                    ExecutePlacedCounters(runtime, instruction, path);
                }
                break;
            case BlokemonOpcode.DealSelfDamage:
                runtime.PendingOtherDamage.Add(
                    new PendingDamage(runtime.Source.Id, instruction.Amount, DamageKind.SelfDamage)
                );
                break;
            case BlokemonOpcode.HealDamage:
                if (runtime.DeferringEndRound)
                {
                    break;
                }

                foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
                {
                    runtime.Builder.Heal(
                        runtime.Actor,
                        target.Id,
                        instruction.Amount,
                        runtime.Source.Id
                    );
                }
                break;
            case BlokemonOpcode.ApplyRoughState:
                foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
                {
                    if (EffectIsPrevented(runtime, target))
                    {
                        continue;
                    }

                    foreach (var state in instruction.RoughStates)
                    {
                        runtime.Builder.ApplyRoughState(
                            runtime.Actor,
                            target.Id,
                            state,
                            runtime.Source.Id
                        );
                    }
                }
                break;
            case BlokemonOpcode.ClearRoughState:
                foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
                {
                    runtime.Builder.ClearRoughStates(runtime.Actor, target.Id);
                }
                break;
            case BlokemonOpcode.DrawFromStack:
                ExecuteDraw(runtime, instruction);
                break;
            case BlokemonOpcode.SearchStack:
                var selected = ResolveSelectedTargets(runtime, instruction, path);
                if (
                    runtime.BeerMatGateParent == ParentPath(path)
                    && runtime.TossCount == instruction.Amount
                )
                {
                    selected = selected.Take(runtime.BadgeSides);
                }

                runtime.LastSelectedCards = FrozenList<CardInstanceId>.Create(
                    selected.Select(static card => card.Id)
                );
                MoveCardsToDestination(
                    runtime,
                    runtime.LastSelectedCards.Select(runtime.Builder.Card),
                    instruction.Destination
                );
                break;
            case BlokemonOpcode.ShuffleStack:
                runtime.Builder.Shuffle(runtime.Actor);
                break;
            case BlokemonOpcode.RevealCards:
                if (runtime.LastSelectedCards.Count == 0)
                {
                    runtime.LastSelectedCards = FrozenList<CardInstanceId>.Create(
                        ResolveSelectedTargets(runtime, instruction, path)
                            .Select(static card => card.Id)
                    );
                }

                runtime.Builder.Events.Add(
                    new PendingMatchEvent(
                        MatchEventKind.CardMoved,
                        runtime.Actor,
                        runtime.Source.Id,
                        runtime.LastSelectedCards,
                        runtime.Effect
                    )
                );
                break;
            case BlokemonOpcode.MoveCards:
                ExecuteMoveCards(runtime, instruction, path);
                break;
            case BlokemonOpcode.ChuckCards:
                ExecuteChuckCards(runtime, instruction, path);
                break;
            case BlokemonOpcode.AttachVim:
                ExecuteAttachVim(runtime, instruction, path);
                break;
            case BlokemonOpcode.MoveVim:
                ExecuteMoveVim(runtime, instruction, path);
                break;
            case BlokemonOpcode.ChuckVim:
                ExecuteChuckVim(runtime, instruction, path);
                break;
            case BlokemonOpcode.SwapOche:
                ExecuteSwap(runtime, instruction, path);
                break;
            case BlokemonOpcode.PreventDamage:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.PreventDamage);
                break;
            case BlokemonOpcode.PreventEffects:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.PreventEffects);
                break;
            case BlokemonOpcode.ReduceDamage:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ReduceDamage);
                break;
            case BlokemonOpcode.ModifyAttackCost:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ModifyAttackCost);
                break;
            case BlokemonOpcode.ModifyTaxiFare:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ModifyTaxiFare);
                break;
            case BlokemonOpcode.ModifyStayingPower:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ModifyStayingPower);
                break;
            case BlokemonOpcode.ModifySoftSpot:
                if (!runtime.IsAttack || instruction.MechanicalTypes.Length > 1)
                {
                    RegisterEffect(runtime, instruction, TemporaryEffectKind.ModifySoftSpot, path);
                }
                break;
            case BlokemonOpcode.IgnoreStubbornStreak:
                runtime.IgnoreStubbornStreak = true;
                break;
            case BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak:
                runtime.IgnoreSoftSpot = true;
                runtime.IgnoreStubbornStreak = true;
                break;
            case BlokemonOpcode.RestrictAttack:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RestrictAttack);
                break;
            case BlokemonOpcode.RestrictTaxi:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RestrictTaxi);
                break;
            case BlokemonOpcode.RestrictKit:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RestrictKit);
                break;
            case BlokemonOpcode.RestrictLocal:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RestrictLocal);
                break;
            case BlokemonOpcode.RestrictEmptiesRecovery:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RestrictEmptiesRecovery);
                break;
            case BlokemonOpcode.BeerMatToss:
                ExecuteBeerMat(runtime, instruction, path);
                return;
            case BlokemonOpcode.RepeatUntilBlankSide:
                ExecuteUntilBlank(runtime, instruction, path);
                return;
            case BlokemonOpcode.Conditional:
                ExecuteConditional(runtime, instruction, path);
                return;
            case BlokemonOpcode.SendHome:
                foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
                {
                    if (!EffectIsPrevented(runtime, target))
                    {
                        runtime.ForcedSendHome.Add(target.Id);
                    }
                }
                break;
            case BlokemonOpcode.RecoverFromSendHome:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.RecoverFromSendHome);
                break;
            case BlokemonOpcode.CopyAttack:
                ExecuteCopyAttack(runtime, path);
                break;
            case BlokemonOpcode.Demote:
                foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
                {
                    if (!EffectIsPrevented(runtime, target))
                    {
                        Demote(runtime, target);
                    }
                }
                break;
            case BlokemonOpcode.TransformFromStack:
                ExecuteTransform(runtime, instruction, path);
                break;
            case BlokemonOpcode.TakeExtraBarChit:
                TakeBarChits(runtime.Builder, runtime.Actor, instruction.Amount, runtime.Source.Id);
                break;
            case BlokemonOpcode.PlayAsBloke:
                PlayAsBloke(runtime);
                break;
            case BlokemonOpcode.ChuckSelf:
                if (!runtime.IsHouseRule)
                {
                    runtime.Builder.ChuckBloke(runtime.Source.Id);
                    runtime.SourceChucked = true;
                }
                break;
            case BlokemonOpcode.TriggeredPartyTrick:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.TriggeredPartyTrick);
                break;
            case BlokemonOpcode.ContinuousPartyTrick:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ContinuousPartyTrick);
                break;
            case BlokemonOpcode.OncePerRound:
                runtime.Builder.RoundUsage = runtime.Builder.RoundUsage with
                {
                    EffectsUsed = FrozenList<EffectId>.Create(
                        runtime.Builder.RoundUsage.EffectsUsed.Append(runtime.Effect).Distinct()
                    ),
                };
                break;
            case BlokemonOpcode.EndRoundEffect:
                RegisterEffect(runtime, instruction, TemporaryEffectKind.EndRoundEffect);
                runtime.DeferringEndRound = true;
                break;
            case BlokemonOpcode.BigHitterBarChits:
                break;
        }

        ExecuteProgram(runtime, instruction.Then, path + "/then");
    }

    private void ExecuteBeerMat(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        runtime.BadgeSides = 0;
        runtime.TossCount = instruction.Amount;
        runtime.FirstBeerMatIsBlank = false;
        runtime.BeerMatGateParent =
            instruction.Then.Length == 0 && instruction.Otherwise.Length == 0
                ? ParentPath(path)
                : null;
        for (var toss = 0; toss < instruction.Amount; toss++)
        {
            var badge = runtime.Builder.Random.NextInt(2) == 1;
            if (badge)
            {
                runtime.BadgeSides++;
            }

            if (toss == 0)
            {
                runtime.FirstBeerMatIsBlank = !badge;
            }

            runtime.Builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.BeerMatTossed,
                    runtime.Actor,
                    runtime.Source.Id,
                    Effect: runtime.Effect,
                    BadgeSide: badge
                )
            );
        }

        if (runtime.BadgeSides > 0)
        {
            ExecuteProgram(runtime, instruction.Then, path + "/then");
        }
        else
        {
            ExecuteProgram(runtime, instruction.Otherwise, path + "/otherwise");
        }
    }

    private void ExecuteUntilBlank(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        runtime.BadgeSides = 0;
        runtime.TossCount = 0;
        while (true)
        {
            var badge = runtime.Builder.Random.NextInt(2) == 1;
            runtime.TossCount++;
            runtime.Builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.BeerMatTossed,
                    runtime.Actor,
                    runtime.Source.Id,
                    Effect: runtime.Effect,
                    BadgeSide: badge
                )
            );
            if (!badge)
            {
                runtime.FirstBeerMatIsBlank = runtime.TossCount == 1;
                break;
            }

            runtime.BadgeSides++;
        }

        ExecuteProgram(runtime, instruction.Then, path + "/then");
    }

    private void ExecuteConditional(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var passed = instruction.Predicates.All(predicate =>
            EvaluatePredicate(runtime, predicate, path)
        );
        ExecuteProgram(
            runtime,
            passed ? instruction.Then : instruction.Otherwise,
            path + (passed ? "/then" : "/otherwise")
        );
    }

    private bool EvaluatePredicate(
        EffectRuntime runtime,
        BlokemonEffectPredicate predicate,
        string path
    ) =>
        predicate.Condition switch
        {
            BlokemonCondition.Optional => runtime.Choice<EffectChoice.Optional>(
                ChoiceId(runtime.Effect, path, "optional")
            )
                is { IsAccepted: true },
            BlokemonCondition.FirstBeerMatIsBlankSide => runtime.FirstBeerMatIsBlank,
            BlokemonCondition.SelfIsAtOche => runtime.Builder.Card(runtime.Source.Id).Zone
                == CardZone.Oche,
            BlokemonCondition.SelfIsInBooth => runtime.Builder.Card(runtime.Source.Id).Zone
                == CardZone.Booth,
            BlokemonCondition.SelfHasDamage => runtime.Builder.Card(runtime.Source.Id).Damage > 0,
            BlokemonCondition.SelfHasVim => AttachedVim(runtime.Builder, runtime.Source.Id).Any(),
            BlokemonCondition.SelfHasSpecialVim => AttachedVim(runtime.Builder, runtime.Source.Id)
                .Any(vim => !vim.MechanicalId.Value.StartsWith("VIM-", StringComparison.Ordinal)),
            BlokemonCondition.SelfHasRoughState => predicate.RoughState is { } state
                && runtime
                    .Builder.Card(runtime.Source.Id)
                    .RoughStates.Any(entry => entry.State == state),
            BlokemonCondition.OwnMittIsEmpty => !runtime
                .Builder.CardsIn(runtime.Actor, CardZone.Mitt)
                .Any(),
            BlokemonCondition.MittCountsAreEqual => runtime
                .Builder.CardsIn(runtime.Actor, CardZone.Mitt)
                .Count()
                == runtime
                    .Builder.CardsIn(runtime.Builder.Other(runtime.Actor), CardZone.Mitt)
                    .Count(),
            BlokemonCondition.MatePlayedThisRound => runtime.Builder.RoundUsage.MatesPlayed > 0,
            BlokemonCondition.NamedBlokeInPlay => predicate.RelatedId is { } id
                && InPlay(runtime.Builder, runtime.Actor)
                    .Any(card => card.MechanicalId.Value == id),
            BlokemonCondition.NamedBlokeInBooth => predicate.RelatedId is { } id
                && runtime
                    .Builder.CardsIn(runtime.Actor, CardZone.Booth)
                    .Any(card => card.MechanicalId.Value == id),
            BlokemonCondition.OtherOcheHasMechanicalType => OtherOcheHasType(runtime, predicate),
            BlokemonCondition.OtherOcheHasDamage => runtime.Builder.Oche(
                runtime.Builder.Other(runtime.Actor)
            )
                is { Damage: > 0 },
            BlokemonCondition.OtherOcheHasRoughState => predicate.RoughState is { } state
                && runtime.Builder.Oche(runtime.Builder.Other(runtime.Actor)) is { } other
                && other.RoughStates.Any(entry => entry.State == state),
            BlokemonCondition.OtherOcheIsPromoted => runtime.Builder.Oche(
                runtime.Builder.Other(runtime.Actor)
            )
                is { UnderlyingCards.Count: > 0 },
            BlokemonCondition.OtherOcheIsBigHitter => runtime.Builder.Oche(
                runtime.Builder.Other(runtime.Actor)
            )
                is { } other
                && _catalog.Manifest.BaseRules.BigHitters.BlokeIds.Contains(
                    other.MechanicalId.Value,
                    StringComparer.Ordinal
                ),
            BlokemonCondition.AttachedVimCountsAreEqual => runtime.Builder.Oche(runtime.Actor)
                is { } own
                && runtime.Builder.Oche(runtime.Builder.Other(runtime.Actor)) is { } other
                && AttachedVim(runtime.Builder, own.Id).Count()
                    == AttachedVim(runtime.Builder, other.Id).Count(),
            BlokemonCondition.OwnBarChitCountIsGreater => runtime
                .Builder.Player(runtime.Actor)
                .BarChitsRemaining
                > runtime.Builder.Player(runtime.Builder.Other(runtime.Actor)).BarChitsRemaining,
            BlokemonCondition.TargetHasDamage => runtime.LastSelectedCards.Any(id =>
                runtime.Builder.Card(id).Damage > 0
            ),
            BlokemonCondition.OtherBoothExists => runtime
                .Builder.CardsIn(runtime.Builder.Other(runtime.Actor), CardZone.Booth)
                .Any(),
            BlokemonCondition.OwnBlokeSentHomeByOtherAttackDamage => false,
            BlokemonCondition.OtherSentHomeByThisAttackDamage => PendingSendsHome(runtime),
            BlokemonCondition.OwnersFirstRound => runtime
                .Builder.Player(runtime.Actor)
                .RoundsStarted == 1,
            BlokemonCondition.OpenedSecond => runtime.Builder.OpeningPlayer != runtime.Actor,
            BlokemonCondition.PromotedFromMittThisRound => runtime
                .Builder.Card(runtime.Source.Id)
                .LastPromotedRound == runtime.Builder.RoundNumber,
            BlokemonCondition.SourceIsRegular => runtime.Source.Kind == CardKind.Bloke
                && _catalog.Bloke(runtime.Source.MechanicalId).Rank == BlokemonRank.Regular,
            BlokemonCondition.TargetIsRegular => false,
            BlokemonCondition.TargetIsSeasoned => false,
            BlokemonCondition.TargetIsLandlord => false,
            _ => throw new UnreachableException(),
        };

    private static bool OtherOcheHasType(EffectRuntime runtime, BlokemonEffectPredicate predicate)
    {
        var other = runtime.Builder.Oche(runtime.Builder.Other(runtime.Actor));
        if (other is null)
        {
            return false;
        }

        if (predicate.MechanicalType is { } type)
        {
            return runtime.Catalog.MechanicalTypes(other).Contains(type);
        }

        return true;
    }

    private static bool PendingSendsHome(EffectRuntime runtime) =>
        runtime.PendingAttackDamage.Any(pending =>
            pending.Amount + runtime.Builder.Card(pending.Target).Damage
            >= runtime.Catalog.StayingPower(runtime.Builder.Card(pending.Target))
        );

    private void ExecuteScaleDamage(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        if (
            instruction.ValueSource == BlokemonValueSource.Fixed
            && instruction.Selection
                is BlokemonSelection.BeerMat
                    or BlokemonSelection.UntilBlankSide
        )
        {
            return;
        }

        if (
            instruction.ValueSource == BlokemonValueSource.Fixed
            && instruction.Selection == BlokemonSelection.All
        )
        {
            if (runtime.IsAttack)
            {
                runtime.Builder.AddEffect(
                    new TemporaryEffect(
                        runtime.Effect,
                        runtime.Source.Id,
                        runtime.Actor,
                        runtime.Source.Id,
                        TemporaryEffectKind.ScaleNextAttackDamage,
                        instruction.Amount,
                        [],
                        [],
                        [],
                        [],
                        EffectDuration.UntilEndOfOpponentsNextRound,
                        runtime.Builder.RoundNumber + 2,
                        runtime.Builder.RoundNumber + 2
                    )
                );
            }
            else
            {
                RegisterEffect(runtime, instruction, TemporaryEffectKind.ScaleNextAttackDamage);
            }

            return;
        }

        var damage = instruction.Amount * ResolveValue(runtime, instruction);
        AddPendingDamage(runtime, instruction, path, damage, DamageKind.Attack);
    }

    private void ExecutePlacedCounters(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        if (instruction.Selection == BlokemonSelection.AnyDistribution)
        {
            if (
                !ResolveCandidates(runtime.Builder, runtime.Actor, runtime.Source, instruction)
                    .Any()
            )
            {
                return;
            }

            var choice = runtime.Choice<EffectChoice.Distribution>(
                ChoiceId(runtime.Effect, path, "distribution")
            );
            if (choice is null)
            {
                runtime.Rejection = CommandRejectionCode.ChoiceRequired;
                return;
            }

            foreach (var allocation in choice.Values)
            {
                if (EffectIsPrevented(runtime, runtime.Builder.Card(allocation.Card)))
                {
                    continue;
                }

                runtime.PendingOtherDamage.Add(
                    new PendingDamage(
                        allocation.Card,
                        allocation.Counters * 10,
                        DamageKind.PlacedCounter
                    )
                );
            }

            return;
        }

        foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
        {
            if (EffectIsPrevented(runtime, target))
            {
                continue;
            }

            runtime.PendingOtherDamage.Add(
                new PendingDamage(target.Id, instruction.Amount * 10, DamageKind.PlacedCounter)
            );
        }
    }

    private void ExecuteDraw(EffectRuntime runtime, BlokemonEffectInstruction instruction)
    {
        var count =
            instruction.Selection == BlokemonSelection.UntilBlankSide
                ? runtime.BadgeSides * instruction.Amount
            : instruction.ValueSource == BlokemonValueSource.MittCardsNeeded
                ? ResolveValue(runtime, instruction)
            : instruction.Amount;
        runtime.Builder.Draw(runtime.Actor, count, DrawReason.Effect);
    }

    private void ExecuteMoveCards(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var selected = instruction.Sources is { Length: > 0 }
            ? ResolveSelectedTargets(runtime, instruction, path).ToArray()
            : runtime.LastSelectedCards.Select(runtime.Builder.Card).ToArray();
        if (selected.Length == 0)
        {
            selected = ResolveSelectedTargets(runtime, instruction, path).ToArray();
        }

        MoveCardsToDestination(runtime, selected.Take(instruction.Amount), instruction.Destination);
        runtime.LastSelectedCards = FrozenList<CardInstanceId>.Create(
            selected.Select(static card => card.Id)
        );
    }

    private static void MoveCardsToDestination(
        EffectRuntime runtime,
        IEnumerable<CardState> selected,
        BlokemonEffectDestination destination
    )
    {
        var cards = selected.ToArray();
        foreach (var card in cards)
        {
            if (IsInPlay(card) && EffectIsPrevented(runtime, card))
            {
                continue;
            }

            var zone = destination switch
            {
                BlokemonEffectDestination.OwnMitt or BlokemonEffectDestination.OtherMitt =>
                    CardZone.Mitt,
                BlokemonEffectDestination.OwnBooth or BlokemonEffectDestination.OtherBooth =>
                    CardZone.Booth,
                BlokemonEffectDestination.OwnStack
                or BlokemonEffectDestination.OtherStack
                or BlokemonEffectDestination.BottomOfOwnStack
                or BlokemonEffectDestination.BottomOfOtherStack => CardZone.Stack,
                BlokemonEffectDestination.OwnEmptiesTray
                or BlokemonEffectDestination.OtherEmptiesTray => CardZone.EmptiesTray,
                BlokemonEffectDestination.Unspecified => card.Zone,
                _ => throw new UnreachableException(),
            };
            if (zone == card.Zone && destination == BlokemonEffectDestination.Unspecified)
            {
                continue;
            }

            runtime.Builder.MoveCard(card.Id, zone);
            if (
                destination
                is BlokemonEffectDestination.BottomOfOwnStack
                    or BlokemonEffectDestination.BottomOfOtherStack
            )
            {
                var bottom = runtime.Builder.CardsIn(card.Owner, CardZone.Stack).Count() - 1;
                runtime.Builder.SetCard(
                    runtime.Builder.Card(card.Id) with
                    {
                        StackPosition = bottom,
                    }
                );
            }
        }
    }

    private void ExecuteChuckCards(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        if (instruction.Targets.Contains(BlokemonTarget.OwnEmptiesTray))
        {
            return;
        }

        var selected = ResolveSelectedTargets(runtime, instruction, path).ToArray();
        foreach (var card in selected.Take(instruction.Amount))
        {
            if (card.Zone == CardZone.Attached)
            {
                runtime.Builder.DetachTo(card.Id, CardZone.EmptiesTray);
            }
            else
            {
                runtime.Builder.MoveCard(card.Id, CardZone.EmptiesTray);
            }

            runtime.CardsChucked++;
            if (card.Kind == CardKind.Bloke && _catalog.Bloke(card.MechanicalId).TaxiFare == 4)
            {
                runtime.QualifyingChuckedCards++;
            }
        }
    }

    private void ExecuteAttachVim(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        if (instruction.Selection == BlokemonSelection.AnyDistribution)
        {
            var choice = runtime.Choice<EffectChoice.Attachments>(
                ChoiceId(runtime.Effect, path, "attachments")
            );
            if (choice is null)
            {
                runtime.Rejection = CommandRejectionCode.ChoiceRequired;
                return;
            }

            foreach (var placement in choice.Values)
            {
                runtime.Builder.Attach(placement.Vim, placement.Bloke);
            }

            return;
        }

        var selected = instruction.Sources is { Length: > 0 }
            ? ResolveSelectedTargets(
                    runtime,
                    instruction with
                    {
                        Targets = instruction.Sources,
                        Sources = null,
                    },
                    path
                )
                .Where(card => card.Kind == CardKind.Vim)
                .ToArray()
            : runtime
                .LastSelectedCards.Select(runtime.Builder.Card)
                .Where(card => card.Kind == CardKind.Vim)
                .ToArray();
        if (selected.Length == 0)
        {
            selected = ResolveSelectedTargets(runtime, instruction, path)
                .Where(card => card.Kind == CardKind.Vim)
                .ToArray();
        }

        var targets =
            instruction.Targets.Length > 0
                ? instruction
                    .Targets.SelectMany(target =>
                        ResolveTarget(
                            runtime.Builder,
                            runtime.Actor,
                            runtime.Source,
                            instruction,
                            target
                        )
                    )
                    .Where(IsInPlay)
                    .ToArray()
                : [runtime.Builder.Card(runtime.Source.Id)];
        if (targets.Length == 0)
        {
            return;
        }

        for (var index = 0; index < Math.Min(instruction.Amount, selected.Length); index++)
        {
            runtime.Builder.Attach(selected[index].Id, targets[index % targets.Length].Id);
        }
    }

    private void ExecuteMoveVim(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var selected = ResolveSelectedTargets(runtime, instruction, path)
            .Where(card => card.Kind == CardKind.Vim)
            .Take(instruction.Amount)
            .ToArray();
        foreach (var vim in selected)
        {
            if (
                vim.AttachedTo is { } attachedTo
                && EffectIsPrevented(runtime, runtime.Builder.Card(attachedTo))
            )
            {
                continue;
            }

            runtime.Builder.DetachTo(vim.Id, CardZone.Mitt);
            runtime.Builder.Attach(vim.Id, runtime.Source.Id);
        }
    }

    private void ExecuteChuckVim(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var selected = ResolveSelectedTargets(runtime, instruction, path)
            .Where(card => card.Kind == CardKind.Vim)
            .Take(instruction.Amount)
            .ToArray();
        foreach (var vim in selected)
        {
            runtime.Builder.DetachTo(vim.Id, CardZone.EmptiesTray);
        }
    }

    private void ExecuteSwap(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var incoming = runtime
            .LastSelectedCards.Select(runtime.Builder.Card)
            .FirstOrDefault(static card => card.Zone == CardZone.Booth);
        incoming ??= ResolveSelectedTargets(runtime, instruction, path).FirstOrDefault();
        if (incoming is null || incoming.Zone != CardZone.Booth)
        {
            return;
        }

        var outgoing = runtime.Builder.Oche(incoming.Owner);
        if (outgoing is null || EffectIsPrevented(runtime, outgoing))
        {
            return;
        }

        runtime.Builder.MoveCard(outgoing.Id, CardZone.Booth);
        runtime.Builder.ClearRoughStates(runtime.Actor, outgoing.Id);
        runtime.Builder.RemoveEffectsFor(outgoing.Id);
        runtime.Builder.MoveCard(incoming.Id, CardZone.Oche);
    }

    private void RegisterEffect(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        TemporaryEffectKind kind,
        string? path = null
    )
    {
        if (instruction.Selection == BlokemonSelection.BeerMat && runtime.BadgeSides == 0)
        {
            return;
        }

        var targets = (
            path is null
                ? ResolveCandidates(runtime.Builder, runtime.Actor, runtime.Source, instruction)
                : ResolveSelectedTargets(runtime, instruction, path)
        )
            .Where(IsInPlay)
            .ToArray();
        if (
            targets.Length == 0
            && runtime.Builder.Card(runtime.Source.Id).AttachedTo is { } attachedTo
        )
        {
            targets = [runtime.Builder.Card(attachedTo)];
        }

        if (targets.Length == 0 && IsInPlay(runtime.Builder.Card(runtime.Source.Id)))
        {
            targets = [runtime.Builder.Card(runtime.Source.Id)];
        }

        var duration = kind
            is TemporaryEffectKind.ContinuousPartyTrick
                or TemporaryEffectKind.ModifyStayingPower
            ? EffectDuration.WhileSourceInPlay
            : EffectDuration.UntilEndOfOpponentsNextRound;
        foreach (var target in targets.DefaultIfEmpty())
        {
            var mechanicalTypes = instruction.MechanicalTypes.AsEnumerable();
            if (
                path is not null
                && runtime.Choice<EffectChoice.MechanicalType>(
                    ChoiceId(runtime.Effect, path, "type")
                )
                    is { } selectedType
            )
            {
                mechanicalTypes = [selectedType.Value];
            }

            runtime.Builder.AddEffect(
                new TemporaryEffect(
                    runtime.Effect,
                    runtime.Source.Id,
                    runtime.Actor,
                    target?.Id,
                    kind,
                    instruction.Amount,
                    FrozenList<BlokemonMechanicalType>.Create(mechanicalTypes),
                    FrozenList<BlokemonRoughState>.Create(instruction.RoughStates),
                    FrozenList<MechanicalCardId>.Create(
                        instruction.RelatedIds.Select(static id => new MechanicalCardId(id))
                    ),
                    FrozenList<BlokemonCondition>.Create(
                        instruction.Predicates.Select(static predicate => predicate.Condition)
                    ),
                    duration,
                    runtime.Builder.RoundNumber,
                    kind == TemporaryEffectKind.EndRoundEffect
                        ? runtime.Builder.RoundNumber + 1
                        : runtime.Builder.RoundNumber + 2
                )
            );
        }
    }

    private void ExecuteCopyAttack(EffectRuntime runtime, string path)
    {
        var choice = runtime.Choice<EffectChoice.Attack>(ChoiceId(runtime.Effect, path, "attack"));
        if (choice is null || runtime.CopyStack.Contains(choice.Value))
        {
            runtime.Rejection = CommandRejectionCode.EffectUnavailable;
            return;
        }

        var attack = _catalog.Attack(choice.Value);
        if (attack is null)
        {
            runtime.Rejection = CommandRejectionCode.EffectNotFound;
            return;
        }

        runtime.CopyStack.Add(choice.Value);
        ExecuteProgram(runtime, attack.Program, path + "/copy");
        runtime.CopyStack.Remove(choice.Value);
    }

    private static void Demote(EffectRuntime runtime, CardState target)
    {
        if (target.UnderlyingCards.Count == 0)
        {
            return;
        }

        var underlyingId = target.UnderlyingCards[^1];
        var underlying = runtime.Builder.Card(underlyingId);
        runtime.Builder.MoveCard(target.Id, CardZone.Mitt);
        runtime.Builder.SetCard(
            underlying with
            {
                Zone = target.Zone,
                Damage = target.Damage,
                Attachments = target.Attachments,
                UnderlyingCards = FrozenList<CardInstanceId>.Create(
                    target.UnderlyingCards.SkipLast(1)
                ),
                AttachedTo = null,
            }
        );
        foreach (var attachmentId in target.Attachments)
        {
            var attachment = runtime.Builder.Card(attachmentId);
            runtime.Builder.SetCard(attachment with { AttachedTo = underlyingId });
        }
    }

    private void ExecuteTransform(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var replacement = runtime
            .LastSelectedCards.Select(runtime.Builder.Card)
            .FirstOrDefault(static card => card.Zone == CardZone.Stack);
        replacement ??= ResolveSelectedTargets(runtime, instruction, path).FirstOrDefault();
        if (replacement is null || replacement.Zone != CardZone.Stack)
        {
            return;
        }

        var source = runtime.Builder.Card(runtime.Source.Id);
        runtime.Builder.MoveCard(source.Id, CardZone.EmptiesTray);
        runtime.Builder.SetCard(
            replacement with
            {
                Zone = source.Zone,
                Damage = source.Damage,
                EnteredAtOwnerRound = runtime.Builder.Player(runtime.Actor).RoundsStarted,
            }
        );
    }

    private static void PlayAsBloke(EffectRuntime runtime)
    {
        var source = runtime.Builder.Card(runtime.Source.Id);
        if (source.Zone != CardZone.Mitt)
        {
            return;
        }

        var zone = runtime.Builder.Oche(runtime.Actor) is null ? CardZone.Oche : CardZone.Booth;
        if (
            zone == CardZone.Booth
            && runtime.Builder.CardsIn(runtime.Actor, CardZone.Booth).Count()
                >= runtime.Catalog.Manifest.BaseRules.Opening.BoothLimit
        )
        {
            runtime.Rejection = CommandRejectionCode.RuleLimitReached;
            return;
        }

        runtime.Builder.MoveCard(source.Id, zone);
    }

    private void TakeBarChits(
        MatchBuilder builder,
        PlayerId actor,
        int count,
        CardInstanceId source
    )
    {
        var taken = builder.TakeBarChits(actor, count, source);
        foreach (var cardId in taken)
        {
            var card = builder.Card(cardId);
            var trick = _catalog
                .PartyTricks(card)
                .FirstOrDefault(static value => value.Trigger == BlokemonTrigger.OnBarChitTaken);
            if (
                trick is null
                || builder.CardsIn(actor, CardZone.Booth).Count()
                    >= _catalog.Manifest.BaseRules.Opening.BoothLimit
            )
            {
                continue;
            }

            var pending = new PendingBarChitResolution(
                actor,
                cardId,
                new EffectId(trick.MechanicalId),
                true
            );
            builder.QueueBarChit(pending);
            builder.Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.TriggerQueued,
                    actor,
                    cardId,
                    Effect: pending.Effect
                )
            );
        }
    }

    private void AddPendingDamage(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path,
        int amount,
        DamageKind kind
    )
    {
        foreach (var target in ResolveSelectedTargets(runtime, instruction, path))
        {
            var resolvedKind =
                kind == DamageKind.BoothAttack && target.Zone == CardZone.Oche
                    ? DamageKind.Attack
                    : kind;
            var existing = runtime.PendingAttackDamage.FindIndex(damage =>
                damage.Target == target.Id
            );
            if (existing >= 0)
            {
                var pending = runtime.PendingAttackDamage[existing];
                runtime.PendingAttackDamage[existing] = pending with
                {
                    Amount = pending.Amount + amount,
                };
            }
            else
            {
                runtime.PendingAttackDamage.Add(new PendingDamage(target.Id, amount, resolvedKind));
            }
        }
    }

    private static void AdjustPendingDamage(EffectRuntime runtime, int amount)
    {
        if (runtime.PendingAttackDamage.Count == 0)
        {
            var other = runtime.Builder.Oche(runtime.Builder.Other(runtime.Actor));
            if (other is not null)
            {
                runtime.PendingAttackDamage.Add(
                    new PendingDamage(other.Id, amount, DamageKind.Attack)
                );
            }

            return;
        }

        for (var index = 0; index < runtime.PendingAttackDamage.Count; index++)
        {
            var pending = runtime.PendingAttackDamage[index];
            runtime.PendingAttackDamage[index] = pending with { Amount = pending.Amount + amount };
        }
    }

    private void ResolveDamage(EffectRuntime runtime)
    {
        foreach (var pending in runtime.PendingAttackDamage)
        {
            var target = runtime.Builder.Card(pending.Target);
            var damage = pending.Amount;
            if (pending.Kind == DamageKind.Attack)
            {
                damage = ApplyAttackDamageOrder(runtime, target, damage);
            }

            runtime.Builder.PlaceDamage(
                runtime.Actor,
                target.Id,
                Math.Max(0, damage),
                pending.Kind,
                runtime.Source.Id
            );
        }

        foreach (var pending in runtime.PendingOtherDamage)
        {
            runtime.Builder.PlaceDamage(
                runtime.Actor,
                pending.Target,
                pending.Amount,
                pending.Kind,
                runtime.Source.Id
            );
        }
    }

    private int ApplyAttackDamageOrder(EffectRuntime runtime, CardState target, int damage)
    {
        damage += runtime
            .Builder.Effects.Where(effect =>
                effect.Owner == runtime.Actor
                && effect.TargetCard == runtime.Source.Id
                && effect.Kind == TemporaryEffectKind.ScaleNextAttackDamage
                && effect.AppliesFromRound <= runtime.Builder.RoundNumber
            )
            .Sum(static effect => effect.Amount);

        if (!runtime.IgnoreSoftSpot && target.Kind == CardKind.Bloke)
        {
            var attackerTypes = _catalog.MechanicalTypes(runtime.Builder.Card(runtime.Source.Id));
            var modifiedSoftSpot = runtime.Builder.Effects.LastOrDefault(effect =>
                effect.TargetCard == target.Id
                && effect.Kind == TemporaryEffectKind.ModifySoftSpot
                && EffectMatchesAttack(effect, runtime.Source, target)
            );
            if (
                modifiedSoftSpot is not null
                && modifiedSoftSpot.MechanicalTypes.Any(attackerTypes.Contains)
            )
            {
                damage *= modifiedSoftSpot.Amount == 4 ? 4 : 2;
            }
            else if (modifiedSoftSpot is null)
            {
                var softSpots = _catalog.Bloke(target.MechanicalId).SoftSpots;
                if (softSpots.Any(softSpot => attackerTypes.Contains(softSpot.MechanicalType)))
                {
                    damage *= 2;
                }
            }
        }

        if (!runtime.IgnoreStubbornStreak && target.Kind == CardKind.Bloke)
        {
            var attackerTypes = _catalog.MechanicalTypes(runtime.Builder.Card(runtime.Source.Id));
            var stubborn = _catalog.Bloke(target.MechanicalId).StubbornStreaks;
            if (stubborn.Any(streak => attackerTypes.Contains(streak.MechanicalType)))
            {
                damage -= 30;
            }
        }

        var targetEffects = runtime
            .Builder.Effects.Where(effect =>
                effect.TargetCard == target.Id
                && EffectMatchesAttack(effect, runtime.Source, target)
            )
            .ToArray();
        if (targetEffects.Any(effect => effect.Kind == TemporaryEffectKind.PreventDamage))
        {
            return 0;
        }

        damage -= targetEffects
            .Where(effect => effect.Kind == TemporaryEffectKind.ReduceDamage)
            .Sum(static effect => effect.Amount);
        return Math.Max(0, damage);
    }

    private bool EffectMatchesAttack(TemporaryEffect effect, CardState attacker, CardState target)
    {
        if (
            effect.Conditions.Contains(BlokemonCondition.SourceIsRegular)
            && (
                attacker.Kind != CardKind.Bloke
                || _catalog.Bloke(attacker.MechanicalId).Rank != BlokemonRank.Regular
            )
        )
        {
            return false;
        }

        if (target.Kind != CardKind.Bloke)
        {
            return !effect.Conditions.Any(condition =>
                condition
                    is BlokemonCondition.TargetIsRegular
                        or BlokemonCondition.TargetIsSeasoned
                        or BlokemonCondition.TargetIsLandlord
            );
        }

        var rank = _catalog.Bloke(target.MechanicalId).Rank;
        return (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsRegular)
                || rank == BlokemonRank.Regular
            )
            && (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsSeasoned)
                || rank == BlokemonRank.Seasoned
            )
            && (
                !effect.Conditions.Contains(BlokemonCondition.TargetIsLandlord)
                || rank == BlokemonRank.Landlord
            );
    }

    private int ResolveValue(EffectRuntime runtime, BlokemonEffectInstruction instruction) =>
        instruction.ValueSource switch
        {
            BlokemonValueSource.Fixed => 1,
            BlokemonValueSource.PrintedDamage => instruction.Amount,
            BlokemonValueSource.SelfDamageCounters => runtime.Builder.Card(runtime.Source.Id).Damage
                / 10,
            BlokemonValueSource.OtherOcheDamageCounters => runtime
                .Builder.Oche(runtime.Builder.Other(runtime.Actor))
                ?.Damage / 10
                ?? 0,
            BlokemonValueSource.OtherBoothCount => runtime
                .Builder.CardsIn(runtime.Builder.Other(runtime.Actor), CardZone.Booth)
                .Count(),
            BlokemonValueSource.OwnAttachedVim => AttachedVim(runtime.Builder, runtime.Source.Id)
                .Count(),
            BlokemonValueSource.OtherAttachedVim => runtime.Builder.Oche(
                runtime.Builder.Other(runtime.Actor)
            )
                is { } other
                ? AttachedVim(runtime.Builder, other.Id).Count()
                : 0,
            BlokemonValueSource.BadgeSides => runtime.BadgeSides,
            BlokemonValueSource.CardsChuckedByEffect => runtime.CardsChucked,
            BlokemonValueSource.KitCardsInOtherMitt => runtime
                .Builder.CardsIn(runtime.Builder.Other(runtime.Actor), CardZone.Mitt)
                .Count(static card => card.Kind == CardKind.Kit),
            BlokemonValueSource.QualifyingChuckedCards => runtime.QualifyingChuckedCards,
            BlokemonValueSource.MittCardsNeeded => Math.Max(
                0,
                instruction.Amount - runtime.Builder.CardsIn(runtime.Actor, CardZone.Mitt).Count()
            ),
            _ => throw new UnreachableException(),
        };

    private IEnumerable<CardState> ResolveSelectedTargets(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path
    )
    {
        var candidates = ResolveCandidates(
                runtime.Builder,
                runtime.Actor,
                runtime.Source,
                instruction
            )
            .ToArray();
        if (instruction.Selection == BlokemonSelection.BeerMat && runtime.BadgeSides == 0)
        {
            return [];
        }

        return instruction.Selection switch
        {
            BlokemonSelection.Chosen => ChoiceCards(runtime, instruction, path, candidates),
            BlokemonSelection.SeededRandom => candidates
                .OrderBy(_ => runtime.Builder.Random.NextInt(int.MaxValue))
                .Take(instruction.TargetCount),
            BlokemonSelection.OtherSideChosen => ChoiceCards(
                runtime,
                instruction,
                path,
                candidates
            ),
            BlokemonSelection.AnyDistribution => runtime.Choice<EffectChoice.Cards>(
                ChoiceId(runtime.Effect, path, "cards")
            )
                is { } distributedCards
                ? distributedCards.Values.Select(runtime.Builder.Card)
                : candidates,
            BlokemonSelection.UpTo => ChoiceCards(runtime, instruction, path, candidates),
            BlokemonSelection.All => candidates.Take(
                instruction.TargetCount > 1 ? instruction.TargetCount : candidates.Length
            ),
            BlokemonSelection.Top => candidates.Take(instruction.Amount),
            BlokemonSelection.BeerMat => candidates,
            BlokemonSelection.UntilBlankSide => candidates,
            _ => throw new UnreachableException(),
        };
    }

    private static IEnumerable<CardState> ChoiceCards(
        EffectRuntime runtime,
        BlokemonEffectInstruction instruction,
        string path,
        CardState[] candidates
    )
    {
        var choice = runtime.Choice<EffectChoice.Cards>(ChoiceId(runtime.Effect, path, "cards"));
        if (choice is not null)
        {
            runtime.LastSelectedCards = choice.Values;
            return choice.Values.Select(runtime.Builder.Card);
        }

        if (runtime.LastSelectedCards.Count > 0)
        {
            return runtime
                .LastSelectedCards.Select(runtime.Builder.Card)
                .Where(candidates.Contains);
        }

        return instruction.Selection == BlokemonSelection.UpTo
            ? []
            : candidates.Take(instruction.TargetCount);
    }

    private IEnumerable<CardState> ResolveCandidates(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        BlokemonEffectInstruction instruction
    )
    {
        var declaredSources = instruction.Sources is { Length: > 0 }
            ? instruction.Sources
            : instruction.Targets;
        var candidates =
            declaredSources.Length == 0
                ? ResolveImplicitCandidates(builder, actor, source, instruction)
                : declaredSources.SelectMany(target =>
                    ResolveTarget(builder, actor, source, instruction, target)
                );
        if (
            instruction.Opcode == BlokemonOpcode.ChuckVim
            && instruction.Sources is not { Length: > 0 }
        )
        {
            candidates = candidates
                .Where(static card => card.Kind == CardKind.Bloke)
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(static card => card.Kind == CardKind.Vim);
        }

        if (instruction.SourceTopCount > 0)
        {
            candidates = candidates.Take(instruction.SourceTopCount);
        }

        return FilterCards(candidates, instruction);
    }

    private IEnumerable<CardState> ResolveTarget(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        BlokemonEffectInstruction instruction,
        BlokemonTarget target
    ) =>
        target switch
        {
            BlokemonTarget.Self => source.Yield(),
            BlokemonTarget.OwnOche => builder.Oche(actor).Yield(),
            BlokemonTarget.OwnBoothChosen => builder.CardsIn(actor, CardZone.Booth),
            BlokemonTarget.OwnBlokeChosen => InPlay(builder, actor),
            BlokemonTarget.OwnBlokesAll => InPlay(builder, actor),
            BlokemonTarget.OtherOche => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonTarget.OtherBoothChosen => builder.CardsIn(
                builder.Other(actor),
                CardZone.Booth
            ),
            BlokemonTarget.OtherBoothAll => builder.CardsIn(builder.Other(actor), CardZone.Booth),
            BlokemonTarget.OtherBlokeChosen => InPlay(builder, builder.Other(actor)),
            BlokemonTarget.OtherBlokesAll => InPlay(builder, builder.Other(actor)),
            BlokemonTarget.OwnMitt => builder.CardsIn(actor, CardZone.Mitt),
            BlokemonTarget.OtherMitt => builder.CardsIn(builder.Other(actor), CardZone.Mitt),
            BlokemonTarget.OwnStack => builder.CardsIn(actor, CardZone.Stack),
            BlokemonTarget.OtherStack => builder.CardsIn(builder.Other(actor), CardZone.Stack),
            BlokemonTarget.OwnEmptiesTray => builder.CardsIn(actor, CardZone.EmptiesTray),
            BlokemonTarget.OtherEmptiesTray => builder.CardsIn(
                builder.Other(actor),
                CardZone.EmptiesTray
            ),
            BlokemonTarget.OwnAttachedBarKits => InPlay(builder, actor)
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(card =>
                    card.Kind == CardKind.Kit
                    && _catalog.Kit(card.MechanicalId).Kind == BlokemonKitKind.BarKit
                ),
            BlokemonTarget.OwnOcheAttachedVim => builder
                .Oche(actor)
                .Yield()
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(static card => card.Kind == CardKind.Vim),
            BlokemonTarget.OtherOcheAttachedVim => builder
                .Oche(builder.Other(actor))
                .Yield()
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(static card => card.Kind == CardKind.Vim),
            BlokemonTarget.BarChits => builder.CardsIn(actor, CardZone.BarChit),
            BlokemonTarget.LocalInPlay => builder.Cards.Where(card => card.Zone == CardZone.Local),
            _ => throw new UnreachableException(),
        };

    private IEnumerable<CardState> ResolveImplicitCandidates(
        MatchBuilder builder,
        PlayerId actor,
        CardState source,
        BlokemonEffectInstruction instruction
    ) =>
        instruction.Opcode switch
        {
            BlokemonOpcode.DealPrintedDamage => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.AdjustDamage => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.ScaleDamage => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.DealBoothDamage => builder.CardsIn(builder.Other(actor), CardZone.Booth),
            BlokemonOpcode.PlaceDamageCounters => InPlay(builder, builder.Other(actor)),
            BlokemonOpcode.DealSelfDamage => source.Yield(),
            BlokemonOpcode.HealDamage => source.Yield(),
            BlokemonOpcode.ApplyRoughState => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.ClearRoughState => source.Yield(),
            BlokemonOpcode.DrawFromStack => builder.CardsIn(actor, CardZone.Stack),
            BlokemonOpcode.SearchStack => FilterCards(
                builder.CardsIn(actor, CardZone.Stack),
                instruction
            ),
            BlokemonOpcode.ShuffleStack => builder.CardsIn(actor, CardZone.Stack),
            BlokemonOpcode.RevealCards => [],
            BlokemonOpcode.MoveCards => [],
            BlokemonOpcode.ChuckCards => InPlay(builder, actor)
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(static card => card.Kind == CardKind.Kit),
            BlokemonOpcode.AttachVim => builder
                .CardsIn(actor, CardZone.Mitt)
                .Where(static card => card.Kind == CardKind.Vim),
            BlokemonOpcode.MoveVim => InPlay(builder, actor)
                .SelectMany(card => card.Attachments.Select(builder.Card))
                .Where(static card => card.Kind == CardKind.Vim),
            BlokemonOpcode.ChuckVim => source
                .Attachments.Select(builder.Card)
                .Where(static card => card.Kind == CardKind.Vim),
            BlokemonOpcode.SwapOche => builder.CardsIn(builder.Other(actor), CardZone.Booth),
            BlokemonOpcode.PreventDamage => source.Yield(),
            BlokemonOpcode.PreventEffects => source.Yield(),
            BlokemonOpcode.ReduceDamage => source.Yield(),
            BlokemonOpcode.ModifyAttackCost => source.Yield(),
            BlokemonOpcode.ModifyTaxiFare => source.Yield(),
            BlokemonOpcode.ModifyStayingPower => source.Yield(),
            BlokemonOpcode.ModifySoftSpot => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.IgnoreStubbornStreak => [],
            BlokemonOpcode.IgnoreSoftSpotAndStubbornStreak => [],
            BlokemonOpcode.RestrictAttack => source.Yield(),
            BlokemonOpcode.RestrictTaxi => source.Yield(),
            BlokemonOpcode.RestrictKit => source.Yield(),
            BlokemonOpcode.RestrictLocal => source.Yield(),
            BlokemonOpcode.RestrictEmptiesRecovery => source.Yield(),
            BlokemonOpcode.BeerMatToss => [],
            BlokemonOpcode.RepeatUntilBlankSide => [],
            BlokemonOpcode.Conditional => [],
            BlokemonOpcode.SendHome => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.RecoverFromSendHome => source.Yield(),
            BlokemonOpcode.CopyAttack => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.Demote => builder.Oche(builder.Other(actor)).Yield(),
            BlokemonOpcode.TransformFromStack => FilterCards(
                builder.CardsIn(actor, CardZone.Stack),
                instruction
            ),
            BlokemonOpcode.TakeExtraBarChit => [],
            BlokemonOpcode.PlayAsBloke => source.Yield(),
            BlokemonOpcode.ChuckSelf => source.Yield(),
            BlokemonOpcode.TriggeredPartyTrick => source.Yield(),
            BlokemonOpcode.ContinuousPartyTrick => source.Yield(),
            BlokemonOpcode.OncePerRound => source.Yield(),
            BlokemonOpcode.EndRoundEffect => source.Yield(),
            BlokemonOpcode.BigHitterBarChits => [],
            _ => throw new UnreachableException(),
        };

    private IEnumerable<CardState> FilterCards(
        IEnumerable<CardState> cards,
        BlokemonEffectInstruction instruction
    ) =>
        cards.Where(card =>
            (
                instruction.RelatedIds.Length == 0
                || instruction.RelatedIds.Contains(card.MechanicalId.Value, StringComparer.Ordinal)
            )
            && (
                instruction.MechanicalTypes.Length == 0
                || (
                    card.Kind == CardKind.Vim
                        ? instruction.MechanicalTypes.Contains(
                            _catalog.Vim(card.MechanicalId).MechanicalType
                        )
                        : card.Kind == CardKind.Bloke
                            && _catalog
                                .Bloke(card.MechanicalId)
                                .MechanicalTypes.Any(instruction.MechanicalTypes.Contains)
                )
            )
            && (
                !instruction.Predicates.Any(predicate =>
                    predicate.Condition == BlokemonCondition.TargetHasDamage
                )
                || card.Damage > 0
            )
            && MatchesCardFilter(card, instruction.CardFilter)
        );

    private bool MatchesCardFilter(CardState card, BlokemonEffectCardFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var category = card.Kind switch
        {
            CardKind.Bloke => BlokemonCardCategory.Bloke,
            CardKind.Vim => BlokemonCardCategory.Vim,
            CardKind.Kit => BlokemonCardCategory.Kit,
            _ => throw new UnreachableException(),
        };
        return (filter.Categories.Length == 0 || filter.Categories.Contains(category))
            && (
                filter.Ranks.Length == 0
                || card.Kind == CardKind.Bloke
                    && filter.Ranks.Contains(_catalog.Bloke(card.MechanicalId).Rank)
            )
            && (
                filter.KitKinds.Length == 0
                || card.Kind == CardKind.Kit
                    && filter.KitKinds.Contains(_catalog.Kit(card.MechanicalId).Kind)
            )
            && (!filter.BasicVimOnly || card.Kind == CardKind.Vim)
            && !filter.ExcludedRelatedIds.Contains(card.MechanicalId.Value, StringComparer.Ordinal);
    }

    private static IEnumerable<CardState> InPlay(MatchBuilder builder, PlayerId player) =>
        builder.Cards.Where(card =>
            card.Owner == player && card.Zone is CardZone.Oche or CardZone.Booth
        );

    private static bool IsInPlay(CardState card) => card.Zone is CardZone.Oche or CardZone.Booth;

    private static bool EffectIsPrevented(EffectRuntime runtime, CardState target) =>
        runtime.IsAttack
        && runtime.Builder.Effects.Any(effect =>
            effect.TargetCard == target.Id
            && effect.Owner != runtime.Actor
            && effect.Kind == TemporaryEffectKind.PreventEffects
        );

    private static IEnumerable<CardState> AttachedVim(MatchBuilder builder, CardInstanceId card) =>
        builder
            .Card(card)
            .Attachments.Select(builder.Card)
            .Where(static attached => attached.Kind == CardKind.Vim);

    private static EffectChoiceId ChoiceId(EffectId effect, string path, string kind) =>
        new($"{effect.Value}:{path}:{kind}");

    private static string ParentPath(string path) => path[..path.LastIndexOf('/')];

    private BlokemonEffectInstruction[]? FindProgram(EffectId effect) =>
        _catalog.Attack(effect)?.Program
        ?? _catalog.PartyTrick(effect)?.Program
        ?? _catalog.HouseRule(effect)?.Program;

    private IEnumerable<(EffectId Effect, BlokemonEffectInstruction[] Program)> AllPrograms() =>
        _catalog
            .Manifest.Collectibles.SelectMany(static card =>
                card.PartyTricks.Select(static effect =>
                        (new EffectId(effect.MechanicalId), effect.Program)
                    )
                    .Concat(
                        card.Attacks.Select(static effect =>
                            (new EffectId(effect.MechanicalId), effect.Program)
                        )
                    )
                    .Concat(
                        card.HouseRules.Select(static effect =>
                            (new EffectId(effect.MechanicalId), effect.Program)
                        )
                    )
            )
            .Concat(
                _catalog.Manifest.Kits.SelectMany(static card =>
                    card.PartyTricks.Select(static effect =>
                            (new EffectId(effect.MechanicalId), effect.Program)
                        )
                        .Concat(
                            card.Attacks.Select(static effect =>
                                (new EffectId(effect.MechanicalId), effect.Program)
                            )
                        )
                        .Concat(
                            card.HouseRules.Select(static effect =>
                                (new EffectId(effect.MechanicalId), effect.Program)
                            )
                        )
                )
            );

    private static int AuditProgram(
        EffectId effect,
        BlokemonEffectInstruction[] program,
        List<InterpreterAuditIssue> issues
    )
    {
        var count = 0;
        foreach (var instruction in program)
        {
            count++;
            if (!Enum.IsDefined(instruction.Opcode))
            {
                issues.Add(new InterpreterAuditIssue("unknown-opcode", effect));
            }

            if (!Enum.IsDefined(instruction.Selection))
            {
                issues.Add(new InterpreterAuditIssue("unknown-selection", effect));
            }

            if (!Enum.IsDefined(instruction.ValueSource))
            {
                issues.Add(new InterpreterAuditIssue("unknown-value-source", effect));
            }

            if (instruction.Targets.Any(static target => !Enum.IsDefined(target)))
            {
                issues.Add(new InterpreterAuditIssue("unknown-target", effect));
            }

            if (
                instruction.Predicates.Any(static predicate => !Enum.IsDefined(predicate.Condition))
            )
            {
                issues.Add(new InterpreterAuditIssue("unknown-condition", effect));
            }

            if (!HasSupportedSemanticShape(instruction))
            {
                issues.Add(new InterpreterAuditIssue("unsupported-semantic-shape", effect));
            }

            count += AuditProgram(effect, instruction.Then, issues);
            count += AuditProgram(effect, instruction.Otherwise, issues);
        }

        return count;
    }

    private static bool HasSupportedSemanticShape(BlokemonEffectInstruction instruction)
    {
        if (instruction.TargetCount < 1 || instruction.Amount < -99)
        {
            return false;
        }

        if (
            instruction.Selection == BlokemonSelection.OtherSideChosen
            && instruction.Opcode is not (BlokemonOpcode.SwapOche or BlokemonOpcode.ChuckCards)
        )
        {
            return false;
        }

        if (
            instruction.Opcode == BlokemonOpcode.MoveCards
            && instruction.Destination == BlokemonEffectDestination.Unspecified
        )
        {
            return false;
        }

        if (
            instruction.CardFilter is { } filter
            && (
                filter.Categories.Any(static value => !Enum.IsDefined(value))
                || filter.Ranks.Any(static value => !Enum.IsDefined(value))
                || filter.KitKinds.Any(static value => !Enum.IsDefined(value))
            )
        )
        {
            return false;
        }

        if (
            instruction.Selection == BlokemonSelection.AnyDistribution
            && instruction.Opcode
                is not (BlokemonOpcode.AttachVim or BlokemonOpcode.PlaceDamageCounters)
            && instruction.Opcode != BlokemonOpcode.TriggeredPartyTrick
        )
        {
            return false;
        }

        if (
            instruction.Selection == BlokemonSelection.Top
            && instruction.Opcode
                is not (
                    BlokemonOpcode.SearchStack
                    or BlokemonOpcode.ShuffleStack
                    or BlokemonOpcode.RevealCards
                    or BlokemonOpcode.MoveCards
                    or BlokemonOpcode.ChuckCards
                )
        )
        {
            return false;
        }

        return instruction.Opcode != BlokemonOpcode.Conditional
            || instruction.Predicates.Length > 0;
    }

    private static void AuditTrigger(BlokemonPartyTrick trick, List<InterpreterAuditIssue> issues)
    {
        var effect = new EffectId(trick.MechanicalId);
        var continuous = ProgramContainsOpcode(trick.Program, BlokemonOpcode.ContinuousPartyTrick);
        var triggered = ProgramContainsOpcode(trick.Program, BlokemonOpcode.TriggeredPartyTrick);
        var valid = trick.Trigger switch
        {
            BlokemonTrigger.Activated => !continuous && !triggered,
            BlokemonTrigger.Continuous => continuous,
            BlokemonTrigger.OnPromotionFromMitt => triggered
                && ProgramContainsCondition(
                    trick.Program,
                    BlokemonCondition.PromotedFromMittThisRound
                ),
            BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage => triggered,
            BlokemonTrigger.BeforeSelfSentHomeByAttackDamage => triggered,
            BlokemonTrigger.AfterSelfDamagedByAttack => triggered,
            BlokemonTrigger.AfterSelfSentHomeByAttackDamage => triggered,
            BlokemonTrigger.OnBarChitTaken => triggered,
            _ => false,
        };
        if (!valid)
        {
            issues.Add(new InterpreterAuditIssue("unsupported-trigger-shape", effect));
        }
    }

    private static bool ProgramContainsOpcode(
        BlokemonEffectInstruction[] program,
        BlokemonOpcode opcode
    ) =>
        program.Any(instruction =>
            instruction.Opcode == opcode
            || ProgramContainsOpcode(instruction.Then, opcode)
            || ProgramContainsOpcode(instruction.Otherwise, opcode)
        );

    private static bool ProgramContainsCondition(
        BlokemonEffectInstruction[] program,
        BlokemonCondition condition
    ) =>
        program.Any(instruction =>
            instruction.Predicates.Any(predicate => predicate.Condition == condition)
            || ProgramContainsCondition(instruction.Then, condition)
            || ProgramContainsCondition(instruction.Otherwise, condition)
        );

    internal sealed record InterpreterExecution(
        bool IsApplied,
        CommandRejectionCode? Rejection,
        FrozenList<ChoiceRequirement> Requirements,
        FrozenList<CardInstanceId> ForcedSendHome = default,
        bool SourceChucked = false
    );

    private sealed class EffectRuntime(
        MatchBuilder builder,
        AuthorityCatalog catalog,
        PlayerId actor,
        CardState source,
        EffectId effect,
        FrozenList<EffectChoice> choices,
        bool isAttack,
        bool isHouseRule,
        HashSet<EffectId> copyStack
    )
    {
        public MatchBuilder Builder { get; } = builder;

        public AuthorityCatalog Catalog { get; } = catalog;

        public PlayerId Actor { get; } = actor;

        public CardState Source { get; } = source;

        public EffectId Effect { get; } = effect;

        public FrozenList<EffectChoice> Choices { get; } = choices;

        public bool IsAttack { get; } = isAttack;

        public bool IsHouseRule { get; } = isHouseRule;

        public HashSet<EffectId> CopyStack { get; } = copyStack;

        public List<PendingDamage> PendingAttackDamage { get; } = [];

        public List<PendingDamage> PendingOtherDamage { get; } = [];

        public HashSet<CardInstanceId> ForcedSendHome { get; } = [];

        public bool SourceChucked { get; set; }

        public FrozenList<CardInstanceId> LastSelectedCards { get; set; } = [];

        public int BadgeSides { get; set; }

        public int TossCount { get; set; }

        public string? BeerMatGateParent { get; set; }

        public bool FirstBeerMatIsBlank { get; set; }

        public int CardsChucked { get; set; }

        public int QualifyingChuckedCards { get; set; }

        public bool IgnoreSoftSpot { get; set; }

        public bool IgnoreStubbornStreak { get; set; }

        public bool DeferringEndRound { get; set; }

        public CommandRejectionCode? Rejection { get; set; }

        public TChoice? Choice<TChoice>(EffectChoiceId id)
            where TChoice : EffectChoice =>
            Choices.OfType<TChoice>().SingleOrDefault(choice => choice.Id == id);
    }

    private sealed record PendingDamage(CardInstanceId Target, int Amount, DamageKind Kind);
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> Yield<T>(this T? value)
        where T : class => value is null ? [] : [value];
}
