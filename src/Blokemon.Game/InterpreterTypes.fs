namespace Blokemon.Game

open System.Collections.Generic
open System.Collections.Immutable

type InterpreterAuditIssue =
    { Code: string
      Effect: EffectId voption }

type InterpreterAudit =
    { EffectCount: int
      InstructionCount: int
      Issues: ImmutableArray<InterpreterAuditIssue> }

    member this.IsInventoryComplete = this.Issues.Length = 0

type EffectInvocation =
    { Actor: PlayerId
      Source: CardInstanceId
      Effect: EffectId
      Choices: ImmutableArray<EffectChoice> }

type internal PendingDamage =
    { Target: CardInstanceId
      Amount: int
      Kind: DamageKind }

type internal InterpreterExecution =
    { IsApplied: bool
      Rejection: CommandRejectionCode voption
      Requirements: ImmutableArray<ChoiceRequirement>
      ForcedSendHome: ImmutableArray<CardInstanceId>
      SourceChucked: bool
      BeerMatResults: ImmutableArray<bool>
      AttackDamageTargets: ImmutableArray<CardInstanceId>
      DeferredAttackKnockoutBarChits: int }

[<RequireQualifiedAccess>]
module internal InterpreterExecution =

    let rejected
        (rejection: CommandRejectionCode)
        (requirements: ImmutableArray<ChoiceRequirement>)
        =
        { IsApplied = false
          Rejection = ValueSome rejection
          Requirements = requirements
          ForcedSendHome = ImmutableArray<_>.Empty
          SourceChucked = false
          BeerMatResults = ImmutableArray<_>.Empty
          AttackDamageTargets = ImmutableArray<_>.Empty
          DeferredAttackKnockoutBarChits = 0 }

/// Everything one effect's execution accumulates: the staged damage, the choices it consumed, the
/// beer-mat results it must be able to replay, and the outcome flags the engine reads back.
type internal EffectRuntime
    (
        builder: MatchBuilder,
        catalog: AuthorityCatalog,
        actor: PlayerId,
        source: CardState,
        effect: EffectId,
        choices: ImmutableArray<EffectChoice>,
        isAttack: bool,
        isHouseRule: bool,
        copyStack: HashSet<EffectId>,
        beerMatResults: ImmutableArray<bool>,
        triggerContext: TriggerContext voption
    ) =
    let recordedBeerMats = ResizeArray<bool> beerMatResults
    let usedChoiceIds = HashSet<EffectChoiceId>()
    let pendingAttackDamage = ResizeArray<PendingDamage>()
    let pendingOtherDamage = ResizeArray<PendingDamage>()
    let forcedSendHome = HashSet<CardInstanceId>()
    let attackDamageTargets = HashSet<CardInstanceId>()
    let mutable replayedBeerMats = 0
    let mutable lastBeerMatWasReplayed = false

    member _.Builder = builder
    member _.Catalog = catalog
    member _.Actor = actor
    member _.Source = source
    member _.Effect = effect
    member _.Choices = choices
    member _.IsAttack = isAttack
    member _.IsHouseRule = isHouseRule
    member _.CopyStack = copyStack
    member _.TriggerContext = triggerContext

    member val DeferredRequirements: ImmutableArray<ChoiceRequirement> =
        ImmutableArray<_>.Empty with get, set

    member _.UsedChoiceIds = usedChoiceIds

    member _.BeerMatResults = ImmutableArray.CreateRange recordedBeerMats

    member _.PendingAttackDamage = pendingAttackDamage

    member _.PendingOtherDamage = pendingOtherDamage

    member _.ForcedSendHome = forcedSendHome

    member _.AttackDamageTargets = attackDamageTargets

    member val SourceChucked = false with get, set

    member val LastSelectedCards: ImmutableArray<CardInstanceId> =
        ImmutableArray<_>.Empty with get, set

    member val HasCardSelection = false with get, set
    member val BadgeSides = 0 with get, set
    member val TossCount = 0 with get, set
    member val DeferredAttackKnockoutBarChits = 0 with get, set
    member val BeerMatGateParent: string voption = ValueNone with get, set
    member val FirstBeerMatIsBlank = false with get, set
    member val CardsChucked = 0 with get, set
    member val QualifyingChuckedCards = 0 with get, set
    member val IgnoreSoftSpot = false with get, set
    member val IgnoreStubbornStreak = false with get, set
    member val DeferringEndRound = false with get, set
    member val Rejection: CommandRejectionCode voption = ValueNone with get, set

    member _.NextBeerMat() =
        if replayedBeerMats < recordedBeerMats.Count then
            lastBeerMatWasReplayed <- true
            let result = recordedBeerMats[replayedBeerMats]
            replayedBeerMats <- replayedBeerMats + 1
            result
        else
            lastBeerMatWasReplayed <- false
            let result = builder.TossBeerMat actor
            recordedBeerMats.Add result
            replayedBeerMats <- replayedBeerMats + 1
            result

    member this.RecordBeerMatEvent(badge: bool) =
        if not lastBeerMatWasReplayed then
            builder.Events.Add
                { PendingMatchEvent.forCard MatchEventKind.BeerMatTossed actor source.Id with
                    Effect = ValueSome effect
                    BadgeSide = ValueSome badge }

    member this.Defer(requirements: ImmutableArray<ChoiceRequirement>) =
        this.DeferredRequirements <- requirements

    member this.Use(requirements: ImmutableArray<ChoiceRequirement>) =
        for requirement in requirements do
            this.UsedChoiceIds.Add requirement.Id |> ignore

    member private _.Pick(select: EffectChoice -> 'T voption) =
        let mutable found = ValueNone

        for choice in choices do
            if found.IsNone then
                found <- select choice

        found

    member this.OptionalChoice(id: EffectChoiceId) = this.Pick(EffectChoice.optional id)
    member this.CardsChoice(id: EffectChoiceId) = this.Pick(EffectChoice.cards id)

    member this.TypeChoice(id: EffectChoiceId) =
        this.Pick(EffectChoice.mechanicalType id)

    member this.AttackChoice(id: EffectChoiceId) = this.Pick(EffectChoice.attack id)
    member this.DistributionChoice(id: EffectChoiceId) = this.Pick(EffectChoice.distribution id)
    member this.AttachmentsChoice(id: EffectChoiceId) = this.Pick(EffectChoice.attachments id)
