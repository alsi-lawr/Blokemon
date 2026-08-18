namespace Blokemon.App

open System.Collections.Immutable
open Blokemon.Game

/// Restores the promise the saved battle has always made about an absent collection member.
///
/// System.Text.Json leaves an ImmutableArray member the JSON never mentioned in its struct
/// default state, and every read of a default one - enumerating it, counting it, even writing it
/// back out - throws InvalidOperationException. FrozenList<'T>, which these members carried
/// until BLOKEMON-069, normalised exactly that shape to empty behind its Items getter, so a
/// stored document missing a nested collection member has always loaded as an empty collection.
/// Every deserialised document passes through here before any guard, fingerprint or replay reads
/// it, which keeps the normalisation at the ingress instead of at each of those use sites.
///
/// Only an absent member is normalised. An explicit null is still refused inside the
/// deserialiser as a JsonException, and the two hand-written union converters still refuse a
/// payload whose member is absent, so the strictness those paths already carried is untouched.
module internal MatchDocumentNormalization =

    let private orEmpty (values: ImmutableArray<'T>) =
        if values.IsDefault then
            ImmutableArray<'T>.Empty
        else
            values

    /// A record member the JSON never mentioned arrives as a null, and the damaged-document
    /// guards are what reject that, so an absent record is carried through as it is.
    let private present (normalise: 'T -> 'T) (value: 'T) =
        match box value with
        | null -> value
        | _ -> normalise value

    let private elements (normalise: 'T -> 'T) (values: ImmutableArray<'T>) =
        if values.IsDefault then
            ImmutableArray<'T>.Empty
        else
            values |> Seq.map (present normalise) |> ImmutableArray.CreateRange

    let private deck (snapshot: FrozenDeckSnapshot) =
        { snapshot with
            Cards = orEmpty snapshot.Cards }

    let private start (request: MatchStartRequest) =
        { request with
            FirstDeck = present deck request.FirstDeck
            SecondDeck = present deck request.SecondDeck }

    let private command (value: MatchCommand) =
        { value with
            Choices = orEmpty value.Choices }

    /// The saved battle, and each archived battle inside the saved history.
    let matchDocument (document: MatchDocument) =
        { document with
            Start = present start document.Start
            Commands = document.Commands |> elements command
            ClientCommands = orEmpty document.ClientCommands }

    let historyDocument (document: MatchHistoryDocument) =
        { document with
            Matches = document.Matches |> elements matchDocument }
