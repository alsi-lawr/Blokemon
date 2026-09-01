namespace Blokemon.App

open System
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization
open Blokemon.Core.SetDesign
open Blokemon.Game

/// Reading helpers shared by the two union converters. System.Text.Json refuses F# unions outright
/// ("F# discriminated union serialization is not supported"), so the command and choice payloads
/// are read and written by hand. Unknown members are refused here because a converter bypasses the
/// JsonUnmappedMemberHandling.Disallow the rest of the document is read under.
module internal UnionPayload =

    let read (reader: byref<Utf8JsonReader>) =
        if reader.TokenType <> JsonTokenType.StartObject then
            raise (JsonException "A union payload must be a JSON object.")

        JsonElement.ParseValue &reader

    let discriminator (name: string) (payload: JsonElement) =
        match payload.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String ->
            match value.GetString() with
            | null -> raise (JsonException $"The {name} discriminator is missing.")
            | text -> text
        | _ -> raise (JsonException $"The {name} discriminator is missing.")

    let field<'T> (name: string) (options: JsonSerializerOptions) (payload: JsonElement) : 'T =
        match payload.TryGetProperty name with
        | true, value ->
            match value.Deserialize<'T>(options) with
            | null -> raise (JsonException $"The member '{name}' is missing.")
            | parsed -> parsed
        | _ -> raise (JsonException $"The member '{name}' is missing.")

    let optionalCard
        (name: string)
        (options: JsonSerializerOptions)
        (payload: JsonElement)
        : CardInstanceId voption =
        match payload.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Null -> ValueNone
        | true, _ -> ValueSome(field<CardInstanceId> name options payload)
        | _ -> raise (JsonException $"The member '{name}' is missing.")

    /// One more than the field count, for the discriminator itself.
    let expect (fieldCount: int) (payload: JsonElement) =
        let mutable count = 0

        for _ in payload.EnumerateObject() do
            count <- count + 1

        if count <> fieldCount + 1 then
            raise (JsonException "The union payload carries an unexpected member.")

        payload

type internal EffectChoiceJsonConverter() =
    inherit JsonConverter<EffectChoice>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, options) =
        let payload = UnionPayload.read &reader
        let choiceId = UnionPayload.field<EffectChoiceId> "choiceId" options

        match UnionPayload.discriminator "$choice" payload with
        | "amount" ->
            let payload = UnionPayload.expect 2 payload
            EffectChoice.Amount(choiceId payload, UnionPayload.field "value" options payload)
        | "cards" ->
            let payload = UnionPayload.expect 2 payload
            EffectChoice.Cards(choiceId payload, UnionPayload.field "values" options payload)
        | "mechanicalType" ->
            let payload = UnionPayload.expect 2 payload

            EffectChoice.MechanicalType(
                choiceId payload,
                UnionPayload.field "value" options payload
            )
        | "attack" ->
            let payload = UnionPayload.expect 2 payload
            EffectChoice.Attack(choiceId payload, UnionPayload.field "value" options payload)
        | "attachments" ->
            let payload = UnionPayload.expect 2 payload
            EffectChoice.Attachments(choiceId payload, UnionPayload.field "values" options payload)
        | other -> raise (JsonException $"Unknown choice discriminator '{other}'.")

    override _.Write(writer: Utf8JsonWriter, value: EffectChoice, options) =
        let case (name: string) =
            writer.WriteString("$choice", name)
            writer.WritePropertyName "choiceId"
            JsonSerializer.Serialize(writer, value.Id, options)

        let write (name: string) payload =
            writer.WritePropertyName name
            JsonSerializer.Serialize(writer, payload, options)

        writer.WriteStartObject()

        match value with
        | EffectChoice.Amount(_, amount) ->
            case "amount"
            writer.WriteNumber("value", amount)
        | EffectChoice.Cards(_, cards) ->
            case "cards"
            write "values" cards
        | EffectChoice.MechanicalType(_, mechanicalType) ->
            case "mechanicalType"
            write "value" mechanicalType
        | EffectChoice.Attack(_, attack) ->
            case "attack"
            write "value" attack
        | EffectChoice.Attachments(_, placements) ->
            case "attachments"
            write "values" placements

        writer.WriteEndObject()

type internal MatchActionJsonConverter() =
    inherit JsonConverter<MatchAction>()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, options) =
        let payload = UnionPayload.read &reader

        let card name =
            UnionPayload.field<CardInstanceId> name options

        let effect name =
            UnionPayload.field<EffectId> name options

        let cards name =
            UnionPayload.field<ImmutableArray<CardInstanceId>> name options

        match UnionPayload.discriminator "$command" payload with
        | "chooseMulliganBonus" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ChooseMulliganBonus(UnionPayload.field "cardsToDraw" options payload)
        | "chooseOpening" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.ChooseOpening(card "oche" payload, cards "booth" payload)
        | "chooseBonusPlacement" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ChooseBonusPlacement(cards "bonusBooth" payload)
        | "attachVim" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.AttachVim(card "vim" payload, card "bloke" payload)
        | "playBloke" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.PlayBloke(card "bloke" payload)
        | "promote" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.Promote(card "promotion" payload, card "bloke" payload)
        | "playKit" ->
            let payload = UnionPayload.expect 2 payload

            MatchAction.PlayKit(
                card "kit" payload,
                UnionPayload.optionalCard "target" options payload
            )
        | "taxi" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.Taxi(card "boothBloke" payload, cards "vimToChuck" payload)
        | "usePartyTrick" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.UsePartyTrick(card "source" payload, effect "effect" payload)
        | "attack" ->
            let payload = UnionPayload.expect 2 payload
            MatchAction.Attack(card "attacker" payload, effect "attackId" payload)
        | "chuckFossil" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ChuckFossil(card "fossil" payload)
        | "endRound" ->
            UnionPayload.expect 0 payload |> ignore
            MatchAction.EndRound
        | "chooseReplacement" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ChooseReplacement(card "boothBloke" payload)
        | "resolveEffectChoice" ->
            UnionPayload.expect 0 payload |> ignore
            MatchAction.ResolveEffectChoice
        | "resolveKnockoutTrigger" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ResolveKnockoutTrigger(UnionPayload.optionalCard "vim" options payload)
        | "resolveBarChitTrigger" ->
            let payload = UnionPayload.expect 1 payload
            MatchAction.ResolveBarChitTrigger(UnionPayload.field "putOntoBooth" options payload)
        | "resign" ->
            UnionPayload.expect 0 payload |> ignore
            MatchAction.Resign
        | other -> raise (JsonException $"Unknown command discriminator '{other}'.")

    override _.Write(writer: Utf8JsonWriter, value: MatchAction, options) =
        let case (name: string) = writer.WriteString("$command", name)

        let write (name: string) payload =
            writer.WritePropertyName name
            JsonSerializer.Serialize(writer, payload, options)

        let writeOptional (name: string) (payload: CardInstanceId voption) =
            writer.WritePropertyName name

            match payload with
            | ValueNone -> writer.WriteNullValue()
            | ValueSome card -> JsonSerializer.Serialize(writer, card, options)

        writer.WriteStartObject()

        match value with
        | MatchAction.ChooseMulliganBonus cardsToDraw ->
            case "chooseMulliganBonus"
            writer.WriteNumber("cardsToDraw", cardsToDraw)
        | MatchAction.ChooseOpening(oche, booth) ->
            case "chooseOpening"
            write "oche" oche
            write "booth" booth
        | MatchAction.ChooseBonusPlacement bonusBooth ->
            case "chooseBonusPlacement"
            write "bonusBooth" bonusBooth
        | MatchAction.AttachVim(vim, bloke) ->
            case "attachVim"
            write "vim" vim
            write "bloke" bloke
        | MatchAction.PlayBloke bloke ->
            case "playBloke"
            write "bloke" bloke
        | MatchAction.Promote(promotion, bloke) ->
            case "promote"
            write "promotion" promotion
            write "bloke" bloke
        | MatchAction.PlayKit(kit, target) ->
            case "playKit"
            write "kit" kit
            writeOptional "target" target
        | MatchAction.Taxi(boothBloke, vimToChuck) ->
            case "taxi"
            write "boothBloke" boothBloke
            write "vimToChuck" vimToChuck
        | MatchAction.UsePartyTrick(source, effect) ->
            case "usePartyTrick"
            write "source" source
            write "effect" effect
        | MatchAction.Attack(attacker, attackId) ->
            case "attack"
            write "attacker" attacker
            write "attackId" attackId
        | MatchAction.ChuckFossil fossil ->
            case "chuckFossil"
            write "fossil" fossil
        | MatchAction.EndRound -> case "endRound"
        | MatchAction.ChooseReplacement boothBloke ->
            case "chooseReplacement"
            write "boothBloke" boothBloke
        | MatchAction.ResolveEffectChoice -> case "resolveEffectChoice"
        | MatchAction.ResolveKnockoutTrigger vim ->
            case "resolveKnockoutTrigger"
            writeOptional "vim" vim
        | MatchAction.ResolveBarChitTrigger putOntoBooth ->
            case "resolveBarChitTrigger"
            writer.WriteBoolean("putOntoBooth", putOntoBooth)
        | MatchAction.Resign -> case "resign"

        writer.WriteEndObject()
