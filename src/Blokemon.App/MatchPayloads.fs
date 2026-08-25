namespace Blokemon.App

open System
open System.Linq
open System.Text.Json
open Blokemon.App.Contracts
open Blokemon.App.DamagedDocument

/// The action payload a receipt fingerprints over, and the document-level JSON reads the
/// damaged-document gates need before a document is trusted.
module internal MatchPayloads =

    let actionPayload (matchId: Guid) (request: ApplyMatchActionRequest) =
        let choices =
            (orEmpty request.Choices)
                .OrderBy((fun choice -> choice.Id), StringComparer.Ordinal)
                .ToArray()

        JsonSerializer.Serialize(
            { MatchId = matchId
              ExpectedRevision = request.ExpectedRevision
              ActionId = request.ActionId
              Choices = choices },
            MatchJson.Options
        )

    let readActionPayload (json: string) : MatchActionPayload | null =
        try
            JsonSerializer.Deserialize<MatchActionPayload>(json, MatchJson.Options)
        with
        | :? JsonException -> null
        | :? NotSupportedException -> null

    let documentsMatch (left: MatchDocument) (right: MatchDocument) =
        String.Equals(
            JsonSerializer.Serialize(left, MatchJson.Options),
            JsonSerializer.Serialize(right, MatchJson.Options),
            StringComparison.Ordinal
        )
