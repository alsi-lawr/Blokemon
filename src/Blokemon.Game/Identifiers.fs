namespace Blokemon.Game

/// The match's value objects. Each is a [<Struct>] record with a single `Value` member, which is
/// what System.Text.Json needs (it refuses F# unions outright) and what keeps the persisted
/// `{"value":"…"}` shape byte-identical to the C# `readonly record struct` it replaces. The
/// same-named constructor functions in ValueObjects below keep every `CardInstanceId "C1"` call
/// site reading as it always did.
[<Struct>]
type MatchId = { Value: string }

[<Struct>]
type PlayerId = { Value: string }

[<Struct>]
type CommandId = { Value: string }

[<Struct>]
type CardInstanceId = { Value: string }

[<Struct>]
type MechanicalCardId = { Value: string }

[<Struct>]
type EffectId = { Value: string }

[<Struct>]
type EffectChoiceId = { Value: string }

[<Struct>]
type MatchRevision =
    { Value: int64 }

    member this.Next() = { Value = this.Value + 1L }

[<Struct>]
type MatchSeed = { Value: uint64 }

[<Struct>]
type MatchRandomState =
    { State: uint64; ConsumptionIndex: int }

[<AutoOpen>]
module ValueObjects =

    let MatchId value : MatchId = { Value = value }

    let PlayerId value : PlayerId = { Value = value }

    let CommandId value : CommandId = { Value = value }

    let CardInstanceId value : CardInstanceId = { Value = value }

    let MechanicalCardId value : MechanicalCardId = { Value = value }

    let EffectId value : EffectId = { Value = value }

    let EffectChoiceId value : EffectChoiceId = { Value = value }

    let MatchRevision value : MatchRevision = { Value = value }

    let MatchSeed value : MatchSeed = { Value = value }

    let MatchRandomState (state, consumptionIndex) : MatchRandomState =
        { State = state
          ConsumptionIndex = consumptionIndex }
