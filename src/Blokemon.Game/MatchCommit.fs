namespace Blokemon.Game

/// Turning the staging area back into an immutable state plus the event tail that describes it.
module internal MatchCommit =

    let private commit (builder: MatchBuilder) (revision: MatchRevision) =
        builder.Revision <- revision
        let firstSequence = builder.LastEventSequence + 1L
        builder.LastEventSequence <- builder.LastEventSequence + int64 builder.Events.Count + 1L
        let state = builder.Snapshot()

        let events =
            builder.Events
            |> Seq.mapi (fun index pending ->
                { Sequence = firstSequence + int64 index
                  Revision = revision
                  Kind = pending.Kind
                  Actor = pending.Actor
                  SourceCard = pending.SourceCard
                  TargetCards = pending.TargetCards
                  Effect = pending.Effect
                  RoughState = pending.RoughState
                  DamageKind = pending.DamageKind
                  DrawReason = pending.DrawReason
                  Amount = pending.Amount
                  BadgeSide = pending.BadgeSide
                  StartRequest = pending.StartRequest
                  Command = pending.Command
                  CommittedState = ValueNone })

        let committed =
            { Sequence = builder.LastEventSequence
              Revision = revision
              Kind = MatchEventKind.StateCommitted
              Actor = ValueNone
              SourceCard = ValueNone
              TargetCards = FrozenList.empty
              Effect = ValueNone
              RoughState = ValueNone
              DamageKind = ValueNone
              DrawReason = ValueNone
              Amount = 0
              BadgeSide = ValueNone
              StartRequest = ValueNone
              Command = ValueNone
              CommittedState = ValueSome state }

        FrozenList<MatchEvent>.Create(Seq.append events [ committed ])

    let commitStart (builder: MatchBuilder) =
        let events = commit builder builder.Revision
        MatchStartOutcome.Started(events[events.Count - 1].CommittedState.Value, events)

    let commitCommand (builder: MatchBuilder) =
        let events = commit builder (builder.Revision.Next())
        CommandOutcome.Applied(events[events.Count - 1].CommittedState.Value, events)

    let reject
        (state: MatchState)
        (rejection: CommandRejectionCode)
        (requirements: FrozenList<ChoiceRequirement>)
        =
        CommandOutcome.Rejected(
            state,
            { Code = rejection
              ChoiceRequirements = requirements }
        )
