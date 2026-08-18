namespace Blokemon.Game

open System
open System.Collections.Generic
open Blokemon.Core.SetDesign
open Blokemon.Game.ChoiceShapes
open Blokemon.Game.ChoiceInspection
open Blokemon.Game.ChoiceValidation
open Blokemon.Game.EffectDamage
open Blokemon.Game.EffectExecution

/// Runs one printed effect against the staging area: works out what it must ask, refuses answers
/// that do not fit, then applies the instructions in order.
type BlokemonInterpreter(authority: BlokemonRuntimeManifest) =
    let catalog = AuthorityCatalog authority

    member internal _.Catalog = catalog

    member _.AuditAuthority() = AuthorityAudit.auditAuthority catalog

    member internal _.InspectChoices
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            triggerContext: TriggerContext voption
        ) =
        inspectChoices catalog builder actor source effect program triggerContext

    member internal this.InspectChoices
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array
        ) =
        this.InspectChoices(builder, actor, source, effect, program, ValueNone)

    member internal _.FindProgram(effect: EffectId) =
        match catalog.Attack effect with
        | ValueSome attack -> ValueSome attack.Program
        | ValueNone ->
            match catalog.PartyTrick effect with
            | ValueSome trick -> ValueSome trick.Program
            | ValueNone ->
                match catalog.HouseRule effect with
                | ValueSome rule -> ValueSome rule.Program
                | ValueNone -> ValueNone

    member this.GetChoiceRequirements(state: MatchState, invocation: EffectInvocation) =
        match state.Cards |> Seq.tryFind (fun card -> card.Id = invocation.Source) with
        | None -> FrozenList.empty
        | Some source ->
            match this.FindProgram invocation.Effect with
            | ValueNone -> FrozenList.empty
            | ValueSome program ->
                let builder = MatchBuilder(state, catalog)

                inspectChoices
                    catalog
                    builder
                    invocation.Actor
                    source
                    invocation.Effect
                    program
                    ValueNone

    member internal _.Execute
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            choices: FrozenList<EffectChoice>,
            isAttack: bool,
            isHouseRule: bool,
            copyStack: HashSet<EffectId> voption,
            beerMatResults: FrozenList<bool>,
            triggerContext: TriggerContext voption
        ) =
        let requirements =
            inspectChoices catalog builder actor source effect program triggerContext

        let scopedChoices =
            FrozenList<EffectChoice>
                .Create(
                    choices
                    |> Seq.filter (fun choice ->
                        choice.Id.Value.StartsWith(effect.Value + ":", StringComparison.Ordinal))
                )

        let initialChoices =
            FrozenList<EffectChoice>
                .Create(
                    scopedChoices
                    |> Seq.filter (fun choice ->
                        requirements |> Seq.exists (fun requirement -> requirement.Id = choice.Id))
                )

        match validateChoices initialChoices requirements with
        | ValueSome rejection -> InterpreterExecution.rejected rejection requirements
        | ValueNone ->

            let runtime =
                EffectRuntime(
                    builder,
                    catalog,
                    actor,
                    source,
                    effect,
                    scopedChoices,
                    isAttack,
                    isHouseRule,
                    (match copyStack with
                     | ValueSome stack -> stack
                     | ValueNone -> HashSet<EffectId>()),
                    beerMatResults,
                    triggerContext
                )

            runtime.Use requirements
            executeProgram catalog runtime program "root"

            if runtime.DeferredRequirements.Count > 0 then
                { InterpreterExecution.rejected
                      CommandRejectionCode.ChoiceRequired
                      runtime.DeferredRequirements with
                    BeerMatResults = runtime.BeerMatResults }
            elif runtime.Rejection.IsSome then
                { InterpreterExecution.rejected runtime.Rejection.Value requirements with
                    BeerMatResults = runtime.BeerMatResults }
            elif
                scopedChoices
                |> Seq.exists (fun choice -> not (runtime.UsedChoiceIds.Contains choice.Id))
            then
                { InterpreterExecution.rejected CommandRejectionCode.InvalidChoice requirements with
                    BeerMatResults = runtime.BeerMatResults }
            else
                resolveDamage catalog runtime

                { IsApplied = true
                  Rejection = ValueNone
                  Requirements = requirements
                  ForcedSendHome =
                    FrozenList<CardInstanceId>.Create(runtime.ForcedSendHome |> Seq.sort)
                  SourceChucked = runtime.SourceChucked
                  BeerMatResults = runtime.BeerMatResults
                  AttackDamageTargets =
                    FrozenList<CardInstanceId>.Create(runtime.AttackDamageTargets |> Seq.sort)
                  DeferredAttackKnockoutBarChits = runtime.DeferredAttackKnockoutBarChits }

    member internal this.Execute
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            choices: FrozenList<EffectChoice>,
            isAttack: bool
        ) =
        this.Execute(
            builder,
            actor,
            source,
            effect,
            program,
            choices,
            isAttack,
            false,
            ValueNone,
            FrozenList.empty,
            ValueNone
        )

    member internal this.ExecuteTriggered
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            choices: FrozenList<EffectChoice>,
            triggerContext: TriggerContext voption
        ) =
        this.Execute(
            builder,
            actor,
            source,
            effect,
            program,
            choices,
            false,
            false,
            ValueNone,
            FrozenList.empty,
            triggerContext
        )

    member internal this.Plan
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            choices: FrozenList<EffectChoice>,
            isAttack: bool,
            isHouseRule: bool,
            beerMatResults: FrozenList<bool>,
            triggerContext: TriggerContext voption
        ) =
        let simulation = MatchBuilder(builder.Snapshot(), catalog)

        this.Execute(
            simulation,
            actor,
            simulation.Card source.Id,
            effect,
            program,
            choices,
            isAttack,
            isHouseRule,
            ValueNone,
            beerMatResults,
            triggerContext
        )

    member internal this.Plan
        (
            builder: MatchBuilder,
            actor: PlayerId,
            source: CardState,
            effect: EffectId,
            program: BlokemonEffectInstruction array,
            choices: FrozenList<EffectChoice>,
            isAttack: bool,
            isHouseRule: bool,
            beerMatResults: FrozenList<bool>
        ) =
        this.Plan(
            builder,
            actor,
            source,
            effect,
            program,
            choices,
            isAttack,
            isHouseRule,
            beerMatResults,
            ValueNone
        )

    member internal _.ValidateChoiceSubmission
        (
            choices: FrozenList<EffectChoice>,
            requirements: FrozenList<ChoiceRequirement>,
            chooser: PlayerId
        ) =
        validateChoiceSubmission choices requirements chooser
