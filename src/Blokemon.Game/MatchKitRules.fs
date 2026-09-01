namespace Blokemon.Game

open Blokemon.Core.SetDesign

/// Vintage Trainer cards share one play rule. Card-specific text can move a Trainer elsewhere;
/// otherwise the handler discards it after resolving that text.
module internal MatchKitRules =

    let validateKitCategory
        (_catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (_trainer: BlokemonKit)
        (targetId: CardInstanceId voption)
        =
        if targetId.IsSome then
            ValueSome CommandRejectionCode.InvalidChoice
        elif
            builder.Effects
            |> Seq.exists (fun effect ->
                effect.Owner <> actor && effect.Kind = TemporaryEffectKind.RestrictKit)
        then
            ValueSome CommandRejectionCode.EffectUnavailable
        else
            ValueNone
