namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.Core.SetDesign
open Blokemon.Game
open ConformanceCensus
open FsUnit
open TUnit.Core

module private ConformanceObligationTestCases =

    type Observation =
        { InitialStateFailures: string array
          InputFailures: string array
          LegalActionResult: string
          Choices: string array
          CanonicalState: string array
          OrderedEvents: string array }

    let compilation () =
        ConformanceFixture.load () |> ConformanceObligationCompiler.compile

    let private requiredNode description (node: JsonNode | null) =
        match node with
        | null -> failwith $"The conformance projection has no {description}."
        | value -> value

    let private decodePointerSegment (segment: string) =
        segment
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal)

    let private segments (pointer: string) =
        pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map decodePointerSegment

    let private child (segment: string) (node: JsonNode) =
        match node with
        | :? JsonObject as value -> value[segment] |> requiredNode segment
        | :? JsonArray as value -> value[int segment] |> requiredNode $"item {segment}"
        | _ -> failwith $"The pointer segment {segment} did not name a container."

    let private replace (pointer: string) (replacement: JsonNode) (root: JsonNode) =
        let segments = segments pointer

        let parent =
            segments[.. segments.Length - 2]
            |> Array.fold (fun current segment -> child segment current) root

        let leaf = segments[segments.Length - 1]

        match parent with
        | :? JsonObject as value -> value[leaf] <- replacement
        | :? JsonArray as value -> value[int leaf] <- replacement
        | _ -> failwith $"The pointer {pointer} did not name a replaceable value."

    let incrementInteger pointer (root: JsonNode) =
        let current =
            segments pointer
            |> Array.fold (fun node segment -> child segment node) root
            |> _.GetValue<int>()

        replace pointer (JsonValue.Create(current + 1) |> requiredNode "integer replacement") root

    let decrementInteger pointer (root: JsonNode) =
        let current =
            segments pointer
            |> Array.fold (fun node segment -> child segment node) root
            |> _.GetValue<int>()

        replace pointer (JsonValue.Create(current - 1) |> requiredNode "integer replacement") root

    let replaceString pointer (value: string) (root: JsonNode) =
        replace pointer (JsonValue.Create(value) |> requiredNode "string replacement") root

    let toggleBoolean pointer (root: JsonNode) =
        let current =
            segments pointer
            |> Array.fold (fun node segment -> child segment node) root
            |> _.GetValue<bool>()

        replace pointer (JsonValue.Create(not current) |> requiredNode "boolean replacement") root

    let removeBranch pointer (root: JsonNode) = replace pointer (JsonArray()) root

    let private idOption value =
        match value with
        | ValueSome value -> value
        | ValueNone -> ""

    let private booleanOption value =
        match value with
        | ValueSome true -> "true"
        | ValueSome false -> "false"
        | ValueNone -> ""

    let private choiceFact =
        function
        | EffectChoice.Optional(id, accepted) ->
            $"optional|{id.Value}|{accepted.ToString().ToLowerInvariant()}"
        | EffectChoice.Amount(id, amount) -> $"amount|{id.Value}|{amount}"
        | EffectChoice.Cards(id, cards) ->
            let values = cards |> Seq.map _.Value |> String.concat ","
            $"cards|{id.Value}|{values}"
        | EffectChoice.MechanicalType(id, mechanicalType) ->
            $"mechanical-type|{id.Value}|{mechanicalType}"
        | EffectChoice.Attack(id, attack) -> $"attack|{id.Value}|{attack.Value}"
        | EffectChoice.Distribution(id, allocations) ->
            let values =
                allocations
                |> Seq.map (fun allocation -> $"{allocation.Card.Value}:{allocation.Counters}")
                |> String.concat ","

            $"distribution|{id.Value}|{values}"
        | EffectChoice.Attachments(id, placements) ->
            let values =
                placements
                |> Seq.map (fun placement -> $"{placement.Vim.Value}->{placement.Bloke.Value}")
                |> String.concat ","

            $"attachments|{id.Value}|{values}"

    let private inputChoice (choice: ConformanceChoiceInput) =
        let id = EffectChoiceId choice.RequirementId

        match choice.Kind with
        | "optional" -> EffectChoice.Optional(id, Boolean.Parse choice.Values[0])
        | "amount" -> EffectChoice.Amount(id, int choice.Values[0])
        | "cards" ->
            EffectChoice.Cards(
                id,
                choice.Values |> Seq.map CardInstanceId |> ImmutableArray.CreateRange
            )
        | "mechanical-type" ->
            EffectChoice.MechanicalType(id, Enum.Parse<BlokemonMechanicalType> choice.Values[0])
        | "attack" -> EffectChoice.Attack(id, EffectId choice.Values[0])
        | "distribution" ->
            EffectChoice.Distribution(
                id,
                choice.Values
                |> Seq.map (fun value ->
                    let parts = value.Split(':')

                    { Card = CardInstanceId parts[0]
                      Counters = int parts[1] })
                |> ImmutableArray.CreateRange
            )
        | "attachments" ->
            EffectChoice.Attachments(
                id,
                choice.Values
                |> Seq.map (fun value ->
                    let parts = value.Split("->", StringSplitOptions.None)

                    { Vim = CardInstanceId parts[0]
                      Bloke = CardInstanceId parts[1] })
                |> ImmutableArray.CreateRange
            )
        | kind -> failwith $"Unknown structured choice input {kind}."

    let private requirementFact (requirement: ChoiceRequirement) =
        let cards = requirement.EligibleCards |> Seq.map _.Value |> String.concat ","

        let types =
            requirement.EligibleMechanicalTypes |> Seq.map string |> String.concat ","

        let effects = requirement.EligibleEffects |> Seq.map _.Value |> String.concat ","
        let targets = requirement.EligibleTargets |> Seq.map _.Value |> String.concat ","

        let optional = requirement.DependsOnOptional |> ValueOption.map _.Value |> idOption

        $"requirement|{requirement.Id.Value}|{requirement.Kind}|chooser={requirement.Chooser.Value}|minimum={requirement.Minimum}|maximum={requirement.Maximum}|cards={cards}|types={types}|effects={effects}|targets={targets}|optional={optional}"

    let private eventFact (event: MatchEvent) =
        let targets = event.TargetCards |> Seq.map _.Value |> String.concat ","

        $"{event.Kind}|actor={event.Actor |> ValueOption.map _.Value |> idOption}|source={event.SourceCard |> ValueOption.map _.Value |> idOption}|targets={targets}|effect={event.Effect |> ValueOption.map _.Value |> idOption}|rough={event.RoughState |> ValueOption.map string |> idOption}|damage={event.DamageKind |> ValueOption.map string |> idOption}|amount={event.Amount}|badge={booleanOption event.BadgeSide}"

    let private cardFact id includeZone includeRough (state: MatchState) =
        let card = state.Cards |> Seq.tryFind (fun card -> card.Id = CardInstanceId id)

        match card with
        | None -> $"card={id}|missing"
        | Some card ->
            let fields = ResizeArray<string> [ $"card={id}" ]

            if includeZone then
                fields.Add $"zone={card.Zone}"

            fields.Add $"damage={card.Damage}"

            if includeRough then
                let rough =
                    card.RoughStates |> Seq.map _.State |> Seq.map string |> String.concat ","

                fields.Add $"rough={rough}"

            String.concat "|" fields

    let private attachedCardFact id (state: MatchState) =
        match state.Cards |> Seq.tryFind (fun card -> card.Id = CardInstanceId id) with
        | None -> $"card={id}|missing"
        | Some card ->
            let attachedTo = card.AttachedTo |> ValueOption.map _.Value |> idOption
            $"{cardFact id true false state}|attached-to={attachedTo}"

    let private formatEffect (effect: TemporaryEffect) =
        let target = effect.TargetCard |> ValueOption.map _.Value |> idOption
        let types = effect.MechanicalTypes |> Seq.map string |> String.concat ","
        let rough = effect.RoughStates |> Seq.map string |> String.concat ","
        let related = effect.RelatedCards |> Seq.map _.Value |> String.concat ","
        let conditions = effect.Conditions |> Seq.map string |> String.concat ","

        $"effect={effect.SourceEffect.Value}|source={effect.SourceCard.Value}|owner={effect.Owner.Value}|target={target}|kind={effect.Kind}|amount={effect.Amount}|types={types}|rough={rough}|related={related}|conditions={conditions}|duration={effect.Duration}"

    let private effectFact effectId (state: MatchState) =
        state.Effects
        |> Seq.tryFind (fun effect -> effect.SourceEffect = EffectId effectId)
        |> Option.map formatEffect
        |> Option.defaultValue $"effect={effectId}|missing"

    let private effectFacts effectId (state: MatchState) =
        state.Effects
        |> Seq.filter (fun effect -> effect.SourceEffect = EffectId effectId)
        |> Seq.map formatEffect
        |> Seq.toArray

    let private winnerFact (state: MatchState) =
        state.Winner |> ValueOption.map _.Value |> idOption

    let private canonicalState (scenario: string) (state: MatchState) =
        match scenario.Split('|') with
        | [| "trivial-damage"; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "damage-attach-vim"; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "own-booth" true false state
               attachedCardFact "recovered-vim" state |]
        | [| "damage-heal"; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "damage-self"; owner; _; _; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"

               if owner = "BLK-081" then
                   yield $"winner={winnerFact state}"

               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state |]
        | [| "damage-rough"; _; _; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true true state
               cardFact "defender" true true state |]
        | [| "damage-rough-effects"; _; _; effect; _; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true true state
               yield cardFact "defender" true true state
               yield! effectFacts effect state |]
        | [| "dynamic-adjust"; _; _; _; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "coin-branch"; _; _; branch; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true true state
               yield cardFact "defender" true true state

               if branch = "ChuckVim" then
                   yield cardFact "other-vim-0" true false state |]
        | [| "damage-chuck-vim"; _; _; _; count; side; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               for index in 0 .. int count - 1 do
                   yield
                       cardFact
                           (if side = "self" then
                                $"vim-{index}"
                            else
                                $"other-vim-{index}")
                           true
                           false
                           state |]
        | [| "damage-move-vim"; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "other-vim-0" true false state |]
        | [| "damage-chuck-cards"; _; _; _; count; zone |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               for index in 0 .. int count - 1 do
                   let id =
                       match zone with
                       | "OtherMitt" -> $"other-mitt-{index}"
                       | "OtherStack" -> $"other-stack-{index}"
                       | "OwnStack" when index = 0 -> "first-draw"
                       | "OwnStack" -> $"own-stack-{index}"
                       | other -> failwith $"Unknown chuck-card zone {other}."

                   yield cardFact id true false state |]
        | [| "damage-booth-spread"; _; _; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "a-booth-0" true false state
               cardFact "a-booth-1" true false state |]
        | [| "damage-swap"; _; _; _; side; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact (if side = "own" then "a-own-swap" else "a-other-swap") true false state |]
        | [| "booth-all-own-swap"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "a-own-swap" true false state
               cardFact "defender" true false state
               cardFact "a-other-booth" true false state |]
        | [| "chuck-vim-booth"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "vim-0" true false state
               cardFact "vim-1" true false state
               cardFact "vim-2" true false state
               cardFact "a-other-booth" true false state |]
        | [| "damage-effect"; _; _; _; rule; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               effectFact rule state |]
        | [| "conditional-adjust"; owner; _; _; _; _; _; mode; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true (owner = "BLK-015" && mode = "true") state |]
        | [| "conditional-demote"; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "lower-stage" true false state
               cardFact "second-draw" true false state |]
        | [| "conditional-extra-bar"; _; _ |] ->
            [| cardFact "defender" true false state
               $"bar-chits=first|remaining={(state.Player MatchScenario.FirstPlayer).BarChitsRemaining}" |]
        | [| "gone-smoke"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "vim-0" true false state
               cardFact "vim-1" true false state
               cardFact "other-booth" true false state
               $"random-consumption-index={state.Random.ConsumptionIndex}" |]
        | [| "multi-toss-damage"; owner; _; _; _; mode |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"

               if owner = "BLK-115" && mode = "all-badge" then
                   yield $"winner={winnerFact state}"

               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state
               yield
                   $"scale-next-effects={state.Effects
                                         |> Seq.filter (fun effect -> effect.Kind = TemporaryEffectKind.ScaleNextAttackDamage)
                                         |> Seq.length}" |]
        | [| "repeat-damage"; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true true state
               cardFact "defender" true true state
               $"scale-next-effects={state.Effects
                                     |> Seq.filter (fun effect -> effect.Kind = TemporaryEffectKind.ScaleNextAttackDamage)
                                     |> Seq.length}" |]
        | [| "repeat-draw"; _; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "first-draw" true false state
               yield cardFact "effect-draw-1" true false state
               yield cardFact "second-draw" true false state |]
        | [| "conditional-rough"; owner; _; _; mode |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"

               if owner = "BLK-124" && mode = "true" then
                   yield $"winner={winnerFact state}"

               yield cardFact "attacker" true true state
               yield cardFact "defender" true true state |]
        | [| "heal-clear"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true true state
               cardFact "defender" true true state |]
        | [| "ignore-modifier"; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "damage-effects"; _; effect; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state
               yield! effectFacts effect state |]
        | [| "coin-effects"; _; effect; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state
               yield! effectFacts effect state |]
        | [| "coin-swap"; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "defender" true false state
               cardFact "a-other-booth" true false state |]
        | [| "full-booth-search"; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "search-card" true false state |]
        | [| "booth-search"; _; _; _; _; count; mode |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"

               for index in 1 .. int count do
                   yield cardFact $"candidate-{index}" true false state |]
        | [| "coin-search"; _; _; _; count; mode |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               for index in 1 .. int count do
                   if mode = "max" then
                       yield attachedCardFact $"candidate-{index}" state
                   else
                       yield cardFact $"candidate-{index}" true false state |]
        | [| ("optional-zero" | "optional-decline"); _; _; _; candidate |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if candidate <> "none" then
                   yield cardFact candidate true false state |]
        | [| "optional-max"; owner; _; _; count |] ->
            let count = int count

            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if owner = "BLK-022" then
                   yield cardFact "first-draw" true false state

                   for index in 1 .. count - 1 do
                       yield cardFact $"candidate-{index}" true false state
               else
                   for index in 1..count do
                       if owner = "BLK-059" then
                           yield attachedCardFact $"candidate-{index}" state
                       else
                           yield cardFact $"candidate-{index}" true false state |]
        | [| "optional-invalid-duplicate"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "candidate-1" true false state
               cardFact "candidate-2" true false state |]
        | [| "optional-bar-kit"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "bar-kit" true false state
               cardFact "bar-kit-2" true false state
               cardFact "own-booth" true false state |]
        | [| "search-all"; owner; _; _; candidate |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               if owner = "BLK-025" then
                   attachedCardFact candidate state
               else
                   cardFact candidate true false state |]
        | [| "hand-kit-scale"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "other-mitt-0" true false state
               cardFact "other-mitt-1" true false state |]
        | [| "hand-kit-scale"; _; _; "zero" |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "top-qualifying"; _; _ |]
        | [| "top-qualifying"; _; _; "zero" |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state
               yield cardFact "first-draw" true false state

               for index in 1..4 do
                   yield cardFact $"top-{index}" true false state |]
        | [| "continuous-refresh"; owner; effect; setup |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if setup = "out-of-play" then
                   yield cardFact "own-oche" true false state

               if owner = "BLK-122" && setup = "no-vim-self" then
                   yield cardFact "vim-sentinel" true false state

               yield! effectFacts effect state |]
        | [| "trigger-nonfire"; _; _; setup |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if setup = "bar-chit" then
                   yield cardFact "own-oche" true false state |]
        | [| "activated-decline"; _; _; _; candidate |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if candidate <> "none" then
                   yield cardFact candidate true false state |]
        | [| "activated-trigger"; owner; _; _; candidate |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if candidate <> "none" then
                   yield cardFact candidate true false state

               if owner = "BLK-132" then
                   yield cardFact "first-draw" true false state

               if owner = "BLK-085" then
                   yield cardFact "first-draw" true false state
                   yield cardFact "mitt-sentinel" true false state

               if owner = "BLK-151" then
                   yield cardFact "first-draw" true false state
                   yield cardFact "effect-draw-1" true false state
                   yield cardFact "effect-draw-2" true false state
                   yield cardFact "mitt-sentinel" true false state

               if owner = "BLK-121" then
                   yield cardFact "own-booth" true false state

               if owner = "BLK-143" then
                   yield cardFact "candidate-2" true false state |]
        | [| "activated-unavailable"; _; _; setup |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "defender" true false state

               if setup = "booth-first-round" || setup = "booth" then
                   yield cardFact "own-oche" true false state |]
        | [| "promotion-decline"; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "promotion" true false state
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "promotion-trigger"; owner; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "promotion" true false state
               yield cardFact "attacker" true false state
               yield cardFact "defender" true (owner = "BLK-097") state

               let promotedVimCount =
                   if owner = "BLK-044" then 3
                   elif owner = "BLK-045" then 8
                   else 0

               if promotedVimCount > 0 then
                   yield attachedCardFact "first-draw" state
               else
                   yield cardFact "first-draw" true false state

               for index in 1..4 do
                   if index < promotedVimCount then
                       yield attachedCardFact $"top-{index}" state
                   else
                       yield cardFact $"top-{index}" true false state

               if promotedVimCount > 5 then
                   for index in 5 .. promotedVimCount - 1 do
                       yield attachedCardFact $"top-{index}" state

               if owner = "BLK-045" then
                   yield cardFact "top-8" true false state

               if owner = "BLK-093" then
                   yield cardFact "opponent-supporter" true false state

               if promotedVimCount > 0 then
                   yield $"random-consumption-index={state.Random.ConsumptionIndex}" |]
        | [| "reactive-trigger"; _; _; _ |]
        | [| "reactive-trigger"; _; _; _; _ |] ->
            [| cardFact "attacker" true false state; cardFact "defender" true false state |]
        | [| ("knockout-trigger" | "knockout-trigger-decline"); _; _ |] ->
            [| cardFact "defender" true false state

               if scenario.StartsWith("knockout-trigger-decline|", StringComparison.Ordinal) then
                   cardFact "movable-vim" true false state
               else
                   attachedCardFact "movable-vim" state

               cardFact "prize" true false state |]
        | [| "bar-chit-trigger"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"winner={winnerFact state}"
               cardFact "defender" true false state
               cardFact "triggered-prize" true false state
               cardFact "extra-prize" true false state
               $"bar-chits=first|remaining={(state.Player MatchScenario.FirstPlayer).BarChitsRemaining}" |]
        | [| "bar-chit-trigger-blank"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "defender" true false state
               cardFact "triggered-prize" true false state
               cardFact "extra-prize" true false state
               $"bar-chits=first|remaining={(state.Player MatchScenario.FirstPlayer).BarChitsRemaining}" |]
        | [| ("bar-chit-trigger-decline" | "bar-chit-trigger-full-booth"); _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "defender" true false state
               yield cardFact "triggered-prize" true false state
               yield cardFact "extra-prize" true false state

               if scenario.StartsWith("bar-chit-trigger-full-booth|", StringComparison.Ordinal) then
                   for index in 0..4 do
                       yield cardFact $"full-booth-{index}" true false state

               yield
                   $"bar-chits=first|remaining={(state.Player MatchScenario.FirstPlayer).BarChitsRemaining}" |]
        | [| "local-decline"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "local-under-test" true false state
               cardFact "mitt-vim" true false state
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "local-trigger"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "local-under-test" true false state
               cardFact "mitt-vim" true false state
               cardFact "mitt-sentinel" true false state
               cardFact "first-draw" true false state
               cardFact "effect-draw-2" true false state
               cardFact "effect-draw-3" true false state
               cardFact "attacker" true false state
               cardFact "defender" true false state |]
        | [| "play-kit"; kit; rule; mode |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "kit-under-test" true false state
               yield cardFact "attacker" true false state

               if kit = "KIT-007" then
                   yield cardFact "first-draw" true false state
                   yield cardFact "effect-draw-1" true false state
                   yield cardFact "prize" true false state

               if kit = "KIT-009" then
                   yield cardFact "defender" true false state
                   yield cardFact "other-mitt-bloke" true false state

               if kit = "KIT-010" then
                   yield cardFact "defender" true false state
                   yield attachedCardFact "other-vim" state
                   yield attachedCardFact "own-vim" state

               if kit = "KIT-011" then
                   yield cardFact "other-mitt-bloke" true false state

               if kit = "KIT-008" && mode = "badge" then
                   yield cardFact "own-booth" true false state
                   yield attachedCardFact "candidate" state

               if kit = "KIT-005" && mode.StartsWith("search-", StringComparison.Ordinal) then
                   yield cardFact "first-draw" true false state

                   for index in 1..7 do
                       yield cardFact $"top-{index}" true false state

                   yield $"random-consumption-index={state.Random.ConsumptionIndex}"

               yield! effectFacts rule state |]
        | [| "trivial-rough"; _; _; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true true state
               cardFact "defender" true true state |]
        | [| "trivial-draw"; _; _; count |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "mitt-sentinel" true false state

               for index in 0 .. int count - 1 do
                   yield
                       cardFact
                           (if index = 0 then "first-draw" else $"effect-draw-{index}")
                           true
                           false
                           state

               yield cardFact "second-draw" true false state |]
        | [| "trivial-chuck"; _; _; "local" |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "local-under-test" true false state |]
        | [| "trivial-chuck"; _; _; "other-stack" |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               $"winner={winnerFact state}"
               cardFact "second-draw" true false state |]
        | [| "trivial-booth-damage"; owner; _; _ |]
        | [| "trivial-booth-damage"; owner; _; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "attacker" true false state
               yield cardFact "a-other-booth" true false state

               if owner = "BLK-087" then
                   yield cardFact "defender" true false state |]
        | [| "trivial-swap"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "defender" true false state
               cardFact "a-other-booth" true false state |]
        | [| "trivial-distribution"; owner; _; _ |] ->
            [| yield $"phase={state.Phase}"
               yield $"active-player={state.ActivePlayer.Value}"
               yield cardFact "defender" true false state
               yield cardFact "a-other-booth" true false state

               if owner = "BLK-094" then
                   yield cardFact "b-other-booth" true false state |]
        | [| "trivial-soft-spot"; _; effect |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               effectFact effect state |]
        | [| "trivial-copy"; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true true state
               cardFact "defender" true true state |]
        | [| "kit-condition"; _; rule; _; _; _ |] ->
            [| $"phase={state.Phase}"
               $"active-player={state.ActivePlayer.Value}"
               cardFact "attacker" true false state
               cardFact "defender" true false state
               cardFact "kit-under-test" true false state
               effectFact rule state |]
        | _ ->
            match scenario with
            | "shirt-off-badge"
            | "shirt-off-blank"
            | "still-coming-up-promoted"
            | "still-coming-up-not-promoted" ->
                [| $"phase={state.Phase}"
                   $"active-player={state.ActivePlayer.Value}"
                   cardFact "attacker" true false state
                   cardFact "defender" true false state |]
            | "paul-chuckle-trigger-fire" ->
                [| cardFact "attacker" false false state
                   cardFact "defender" false false state |]
            | "paul-chuckle-trigger-nonfire" ->
                [| cardFact "attacker" false false state
                   cardFact "defender" false true state |]
            | "day-two-forced-blank" ->
                [| $"phase={state.Phase}"
                   $"active-player={state.ActivePlayer.Value}"
                   cardFact "attacker" true true state
                   cardFact "defender" true true state
                   "force-blank-effects="
                   + (state.Effects
                      |> Seq.filter (fun effect ->
                          effect.Kind = TemporaryEffectKind.ForceBeerMatBlank)
                      |> Seq.map (fun effect -> effect.SourceEffect.Value)
                      |> String.concat ",") |]
            | other -> failwith $"Unknown conformance scenario {other}."

    let private relevantEvents (scenario: string) =
        match scenario.Split('|') with
        | [| "trivial-damage"; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "damage-attach-vim"; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "damage-heal"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamageHealed
                  MatchEventKind.DamagePlaced ]
        | [| "damage-self"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.BlokeSentHome
                  MatchEventKind.BarChitsTaken
                  MatchEventKind.MatchWon ]
        | [| "damage-rough"; _; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.RoughStateApplied
                  MatchEventKind.DamagePlaced
                  MatchEventKind.BeerMatTossed ]
        | [| "damage-rough-effects"; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.RoughStateApplied
                  MatchEventKind.EffectRegistered
                  MatchEventKind.DamagePlaced ]
        | [| "dynamic-adjust"; _; _; _; _; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "coin-branch"; _; _; branch; _ |] ->
            set
                [ yield MatchEventKind.AttackDeclared
                  yield MatchEventKind.BeerMatTossed
                  yield MatchEventKind.DamagePlaced
                  yield MatchEventKind.RoughStateApplied

                  if branch = "ChuckVim" then
                      yield MatchEventKind.CardMoved ]
        | [| "damage-chuck-vim"; _; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.DamagePlaced ]
        | [| "damage-move-vim"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.DamagePlaced ]
        | [| "damage-chuck-cards"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.DamagePlaced ]
        | [| "damage-booth-spread"; _; _; _; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "damage-swap"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.CardMoved
                  MatchEventKind.OcheSwapped ]
        | [| "booth-all-own-swap"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.CardMoved
                  MatchEventKind.OcheSwapped ]
        | [| "chuck-vim-booth"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.DamagePlaced ]
        | [| "damage-effect"; _; _; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.EffectRegistered
                  MatchEventKind.DamagePlaced ]
        | [| "conditional-adjust"; _; _; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.RoughStateApplied ]
        | [| "conditional-demote"; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "conditional-extra-bar"; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "gone-smoke"; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.CardsShuffled ]
        | [| "multi-toss-damage"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.DamagePlaced
                  MatchEventKind.BlokeSentHome
                  MatchEventKind.BarChitsTaken
                  MatchEventKind.MatchWon ]
        | [| "repeat-damage"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.RoughStateApplied
                  MatchEventKind.DamagePlaced ]
        | [| "repeat-draw"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.CardsDrawn ]
        | [| "conditional-rough"; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.DamagePlaced
                  MatchEventKind.BlokeSentHome
                  MatchEventKind.BarChitsTaken
                  MatchEventKind.MatchWon ]
        | [| "heal-clear"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamageHealed
                  MatchEventKind.RoughStateCleared ]
        | [| "ignore-modifier"; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "damage-effects"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.EffectRegistered
                  MatchEventKind.DamagePlaced ]
        | [| "coin-effects"; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.EffectRegistered
                  MatchEventKind.DamagePlaced ]
        | [| "coin-swap"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.CardMoved
                  MatchEventKind.OcheSwapped ]
        | [| "full-booth-search"; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.BeerMatTossed ]
        | [| "booth-search"; _; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsShuffled ]
        | [| "coin-search"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsShuffled ]
        | [| ("optional-zero" | "optional-decline"); _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.CardMoved ]
        | [| "optional-max"; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsShuffled
                  MatchEventKind.CardsRevealed ]
        | [| "optional-invalid-duplicate"; _; _ |] -> set [ MatchEventKind.AttackDeclared ]
        | [| "optional-bar-kit"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.CardMoved ]
        | [| "search-all"; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsShuffled
                  MatchEventKind.CardsRevealed ]
        | [| "hand-kit-scale"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardsRevealed
                  MatchEventKind.DamagePlaced ]
        | [| "hand-kit-scale"; _; _; "zero" |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardsRevealed
                  MatchEventKind.DamagePlaced ]
        | [| "top-qualifying"; _; _ |]
        | [| "top-qualifying"; _; _; "zero" |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.DamagePlaced ]
        | [| "continuous-refresh"; _; _; _ |] ->
            set [ MatchEventKind.EffectRegistered; MatchEventKind.RoundEnded ]
        | [| "trigger-nonfire"; _; _; _ |] ->
            set
                [ MatchEventKind.TriggerQueued
                  MatchEventKind.TriggerResolved
                  MatchEventKind.RoundEnded ]
        | [| "activated-decline"; _; _; _; _ |] -> set [ MatchEventKind.CommandApplied ]
        | [| "activated-trigger"; _; _; _; _ |] ->
            set
                [ MatchEventKind.CommandApplied
                  MatchEventKind.CardsShuffled
                  MatchEventKind.CardsRevealed
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsDrawn
                  MatchEventKind.DamageHealed
                  MatchEventKind.DamagePlaced ]
        | [| "promotion-decline"; _; _; _ |] -> set [ MatchEventKind.CommandApplied ]
        | [| "promotion-trigger"; _; _; _ |] ->
            set
                [ MatchEventKind.CommandApplied
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsShuffled
                  MatchEventKind.RoughStateApplied ]
        | [| "reactive-trigger"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.DamageHealed
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.TriggerResolved
                  MatchEventKind.BlokeSentHome ]
        | [| "reactive-trigger"; _; _; _; "blank" |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.BlokeSentHome ]
        | [| ("knockout-trigger" | "knockout-trigger-decline"); _; _ |] ->
            set [ MatchEventKind.TriggerQueued; MatchEventKind.TriggerResolved ]
        | [| "bar-chit-trigger-blank"; _; _ |] ->
            set [ MatchEventKind.TriggerQueued; MatchEventKind.BeerMatTossed ]
        | [| ("bar-chit-trigger" | "bar-chit-trigger-decline" | "bar-chit-trigger-full-booth"); _; _ |] ->
            set
                [ MatchEventKind.TriggerQueued
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.TriggerResolved ]
        | [| "local-decline"; _; _ |] -> set [ MatchEventKind.CommandApplied ]
        | [| "local-trigger"; _; _ |] ->
            set
                [ MatchEventKind.CommandApplied
                  MatchEventKind.CardMoved
                  MatchEventKind.CardsDrawn ]
        | [| "play-kit"; "KIT-005"; _; "search-zero" |] -> set [ MatchEventKind.CardMoved ]
        | [| "play-kit"; "KIT-005"; _; "search-max" |] ->
            set [ MatchEventKind.CardMoved; MatchEventKind.CardsRevealed ]
        | [| "play-kit"; _; _; _ |] ->
            set
                [ MatchEventKind.CardMoved
                  MatchEventKind.EffectRegistered
                  MatchEventKind.CardsDrawn
                  MatchEventKind.CardsRevealed
                  MatchEventKind.DamageHealed
                  MatchEventKind.BeerMatTossed
                  MatchEventKind.OcheSwapped ]
        | [| "trivial-rough"; _; _; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.RoughStateApplied ]
        | [| "trivial-draw"; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.CardsDrawn ]
        | [| "trivial-chuck"; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.MatchWon ]
        | [| "trivial-booth-damage"; _; _; _ |]
        | [| "trivial-booth-damage"; _; _; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.TriggerQueued
                  MatchEventKind.TriggerResolved ]
        | [| "trivial-swap"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.CardMoved
                  MatchEventKind.OcheSwapped ]
        | [| "trivial-distribution"; _; _; _ |] ->
            set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
        | [| "trivial-soft-spot"; _; _ |] -> set [ MatchEventKind.AttackDeclared ]
        | [| "trivial-copy"; _; _ |] ->
            set
                [ MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced
                  MatchEventKind.RoughStateApplied ]
        | [| "kit-condition"; _; _; _; _; _ |] ->
            set
                [ MatchEventKind.CardMoved
                  MatchEventKind.EffectRegistered
                  MatchEventKind.AttackDeclared
                  MatchEventKind.DamagePlaced ]
        | _ ->
            match scenario with
            | "shirt-off-badge"
            | "shirt-off-blank" -> set [ MatchEventKind.BeerMatTossed; MatchEventKind.DamagePlaced ]
            | "still-coming-up-promoted"
            | "still-coming-up-not-promoted" ->
                set [ MatchEventKind.AttackDeclared; MatchEventKind.DamagePlaced ]
            | "paul-chuckle-trigger-fire" ->
                set [ MatchEventKind.DamagePlaced; MatchEventKind.TriggerResolved ]
            | "paul-chuckle-trigger-nonfire" ->
                set
                    [ MatchEventKind.DamagePlaced
                      MatchEventKind.RoughStateApplied
                      MatchEventKind.TriggerResolved ]
            | "day-two-forced-blank" ->
                set
                    [ MatchEventKind.AttackDeclared
                      MatchEventKind.EffectRegistered
                      MatchEventKind.BeerMatTossed
                      MatchEventKind.DamagePlaced
                      MatchEventKind.AttackCancelled ]
            | other -> failwith $"Unknown conformance scenario {other}."

    let private promotedState promotedThisRound =
        let state =
            MatchScenario.BattleState
                "BLK-080"
                "BLK-003"
                [ "VIM-GEEKED"; "VIM-SOBER"; "VIM-SOBER" ]
                1103UL

        let lowerStage =
            MatchScenario.AttachedCard
                "own-lower-stage"
                "BLK-079"
                MatchScenario.FirstPlayer
                CardZone.Attached
                -1
                (CardInstanceId "attacker")

        let attacker =
            { state.Card(CardInstanceId "attacker") with
                UnderlyingCards = ImmutableArray.Create lowerStage.Id
                LastPromotedRound =
                    if promotedThisRound then
                        state.RoundNumber
                    else
                        state.RoundNumber - 1 }

        MatchScenario.WithCards state [ attacker; lowerStage ]

    let private initialState (scenario: string) =
        match scenario.Split('|') with
        | [| "trivial-damage"; owner; defender; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner defender vim 0UL
        | [| "damage-heal"; owner; defender; _; _; heal |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner defender vim 0UL

            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "attacker") with
                      Damage = int heal } ]
        | [| "damage-attach-vim"; owner; defender; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner defender vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "own-booth"
                      "BLK-001"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1
                  MatchScenario.PlainCard
                      "recovered-vim"
                      "VIM-BLAZED"
                      MatchScenario.FirstPlayer
                      CardZone.EmptiesTray
                      -1 ]
        | [| "damage-self"; owner; defender; _; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner defender vim 0UL
        | [| "damage-rough"; owner; defender; _; _; rough; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let seed =
                if
                    rough.Contains("Singed", StringComparison.Ordinal)
                    || rough.Contains("NoddedOff", StringComparison.Ordinal)
                then
                    2UL
                else
                    0UL

            MatchScenario.BattleState owner defender vim seed
        | [| "damage-rough-effects"; owner; defender; _; _; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner defender vim 0UL
        | [| "dynamic-adjust"; owner; defender; _; _; _; source; units; _ |] ->
            let units = int units

            let vim =
                if source = "OwnAttachedVim" then
                    Seq.replicate 3 (if units = 0 then "VIM-BLAZED" else "VIM-SOBER")
                else
                    Seq.replicate 3 "VIM-SOBER"

            let state = MatchScenario.BattleState owner defender vim 0UL
            let added = ResizeArray<CardState>()

            if source = "OwnBoothCount" then
                for index in 0 .. units - 1 do
                    added.Add(
                        MatchScenario.PlainCard
                            $"own-count-{index}"
                            "BLK-001"
                            MatchScenario.FirstPlayer
                            CardZone.Booth
                            -1
                    )
            elif source = "OtherBoothCount" then
                for index in 0 .. units - 1 do
                    added.Add(
                        MatchScenario.PlainCard
                            $"other-count-{index}"
                            "BLK-001"
                            MatchScenario.SecondPlayer
                            CardZone.Booth
                            -1
                    )
            elif source = "OtherAttachedVim" then
                let attachments =
                    [ for index in 0 .. units - 1 ->
                          MatchScenario.AttachedCard
                              $"other-count-vim-{index}"
                              "VIM-SOBER"
                              MatchScenario.SecondPlayer
                              CardZone.Attached
                              -1
                              (CardInstanceId "defender") ]

                let defenderCard = state.Card(CardInstanceId "defender")

                added.Add
                    { defenderCard with
                        Attachments =
                            ImmutableArray.CreateRange(
                                Seq.append defenderCard.Attachments (attachments |> Seq.map _.Id)
                            ) }

                added.AddRange attachments

            if source = "SelfDamageCounters" then
                added.Add
                    { state.Card(CardInstanceId "attacker") with
                        Damage = units * 10 }
            elif source = "OtherOcheDamageCounters" then
                added.Add
                    { state.Card(CardInstanceId "defender") with
                        Damage = units * 10 }

            MatchScenario.WithCards state added
        | [| "coin-branch"; owner; _; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state =
                MatchScenario.BattleState owner "BLK-003" vim (if mode = "badge" then 0UL else 2UL)

            let otherVim =
                MatchScenario.AttachedCard
                    "other-vim-0"
                    "VIM-SOBER"
                    MatchScenario.SecondPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "defender")

            let defender = state.Card(CardInstanceId "defender")

            MatchScenario.WithCards
                state
                [ { defender with
                      Attachments = ImmutableArray.Create otherVim.Id }
                  otherVim ]
        | [| "damage-chuck-vim"; owner; _; _; count; side; vimId; attached |] ->
            let attached = int attached
            let count = int count

            let sourceVim =
                if side = "self" then Seq.replicate attached vimId
                elif owner = "BLK-058" then Seq.replicate 1 "VIM-CURRY"
                else Seq.replicate 4 "VIM-SOBER"

            let state = MatchScenario.BattleState owner "BLK-003" sourceVim 0UL
            let added = ResizeArray<CardState>()

            if side = "other" then
                let attachments =
                    [ for index in 0 .. count - 1 ->
                          MatchScenario.AttachedCard
                              $"other-vim-{index}"
                              vimId
                              MatchScenario.SecondPlayer
                              CardZone.Attached
                              -1
                              (CardInstanceId "defender") ]

                let defender = state.Card(CardInstanceId "defender")

                added.Add
                    { defender with
                        Attachments = ImmutableArray.CreateRange(attachments |> Seq.map _.Id) }

                added.AddRange attachments

            let state =
                if added.Count > 0 then
                    MatchScenario.WithCards state added
                else
                    state

            if
                MatchScenario.Authority.Collectibles
                |> Seq.find (fun card -> card.Id = owner)
                |> _.MechanicalTypes
                |> Seq.contains BlokemonMechanicalType.Fire
            then
                { state with
                    Effects =
                        ImmutableArray.Create(
                            { SourceEffect = EffectId "BLK-137-B01"
                              SourceCard = CardInstanceId "attacker"
                              Owner = MatchScenario.FirstPlayer
                              TargetCard = ValueSome(CardInstanceId "defender")
                              Kind = TemporaryEffectKind.ModifySoftSpot
                              Amount = 1
                              MechanicalTypes = ImmutableArray.Create BlokemonMechanicalType.Grass
                              RoughStates = ImmutableArray<_>.Empty
                              RelatedCards = ImmutableArray<_>.Empty
                              Conditions = ImmutableArray<_>.Empty
                              Duration = EffectDuration.WhileTargetInPlay
                              AppliesFromRound = state.RoundNumber
                              ExpiresAfterRound = state.RoundNumber }
                        ) }
            else
                state
        | [| "damage-move-vim"; owner; _; vimId |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            let moved =
                MatchScenario.AttachedCard
                    "other-vim-0"
                    vimId
                    MatchScenario.SecondPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "defender")

            let defender = state.Card(CardInstanceId "defender")

            MatchScenario.WithCards
                state
                [ { defender with
                      Attachments = ImmutableArray.Create moved.Id }
                  moved ]
        | [| "damage-chuck-cards"; owner; _; _; count; zone |] ->
            let count = int count

            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            let added =
                match zone with
                | "OtherMitt" ->
                    [ for index in 0 .. count - 1 ->
                          MatchScenario.PlainCard
                              $"other-mitt-{index}"
                              "BLK-001"
                              MatchScenario.SecondPlayer
                              CardZone.Mitt
                              -1 ]
                | "OtherStack" ->
                    [ yield
                          { state.Card(CardInstanceId "second-draw") with
                              StackPosition = count }

                      for index in 0 .. count - 1 do
                          yield
                              MatchScenario.PlainCard
                                  $"other-stack-{index}"
                                  "VIM-SOBER"
                                  MatchScenario.SecondPlayer
                                  CardZone.Stack
                                  index ]
                | "OwnStack" ->
                    [ for index in 1 .. count - 1 ->
                          MatchScenario.PlainCard
                              $"own-stack-{index}"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Stack
                              index ]
                | other -> failwith $"Unknown chuck-card zone {other}."

            MatchScenario.WithCards state added
        | [| "damage-booth-spread"; owner; _; _; _; _; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            let booth index =
                let card =
                    MatchScenario.PlainCard
                        $"a-booth-{index}"
                        "BLK-003"
                        MatchScenario.SecondPlayer
                        CardZone.Booth
                        -1

                if mode = "predicate-true" && index = 0 then
                    { card with Damage = 10 }
                else
                    card

            MatchScenario.WithCards state [ booth 0; booth 1 ]
        | [| "damage-swap"; owner; _; _; side; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      (if side = "own" then "a-own-swap" else "a-other-swap")
                      "BLK-001"
                      (if side = "own" then
                           MatchScenario.FirstPlayer
                       else
                           MatchScenario.SecondPlayer)
                      CardZone.Booth
                      -1 ]
        | [| "booth-all-own-swap"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-own-swap"
                      "BLK-001"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1
                  MatchScenario.PlainCard
                      "a-other-booth"
                      "BLK-001"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "chuck-vim-booth"; owner; _ |] ->
            let state =
                MatchScenario.BattleState
                    owner
                    "BLK-003"
                    [ "VIM-CURRY"; "VIM-CURRY"; "VIM-CURRY" ]
                    0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-other-booth"
                      "BLK-003"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "damage-effect"; owner; _; _; _; _; _; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "conditional-adjust"; owner; _; defender; _; _; condition; mode; related |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner defender vim 0UL

            let state =
                match condition, mode with
                | "SelfHasDamage", "true" ->
                    MatchScenario.WithCards
                        state
                        [ { state.Card(CardInstanceId "attacker") with
                              Damage = 10 } ]
                | "OtherOcheHasDamage", "true" ->
                    MatchScenario.WithCards
                        state
                        [ { state.Card(CardInstanceId "defender") with
                              Damage = 10 } ]
                | "MittCountsAreEqual", "false" ->
                    MatchScenario.WithCards
                        state
                        [ MatchScenario.PlainCard
                              "own-mitt-condition"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Mitt
                              -1 ]
                | "OwnMittIsEmpty", "false" ->
                    MatchScenario.WithCards
                        state
                        [ MatchScenario.PlainCard
                              "own-mitt-condition"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Mitt
                              -1 ]
                | "NamedBlokeInBooth", "true" ->
                    MatchScenario.WithCards
                        state
                        [ MatchScenario.PlainCard
                              "named-condition"
                              related
                              MatchScenario.FirstPlayer
                              CardZone.Booth
                              -1 ]
                | _ -> state

            match condition, mode with
            | "MatePlayedThisRound", "true" ->
                { state with
                    RoundUsage =
                        { state.RoundUsage with
                            MatesPlayed = 1
                            KitsPlayed = ImmutableArray.Create(MechanicalCardId related) } }
            | "OwnBarChitCountIsGreater", "true" ->
                MatchScenario.WithBarChits state MatchScenario.SecondPlayer 5
            | _ -> state
        | [| "conditional-demote"; owner; _; defender; lowerStage |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner defender vim 0UL

            let underlying =
                MatchScenario.AttachedCard
                    "lower-stage"
                    lowerStage
                    MatchScenario.SecondPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "defender")

            let defenderCard = state.Card(CardInstanceId "defender")

            MatchScenario.WithCards
                state
                [ underlying
                  { defenderCard with
                      UnderlyingCards = ImmutableArray.Create underlying.Id
                      LastPromotedRound = state.RoundNumber - 1 } ]
        | [| "conditional-extra-bar"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-001" vim 0UL

            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "defender") with
                      Damage = 20 }
                  MatchScenario.PlainCard
                      "bar-chit-0"
                      "VIM-BLAZED"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      0
                  MatchScenario.PlainCard
                      "bar-chit-1"
                      "VIM-SOBER"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      1 ]
            |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2
        | [| "gone-smoke"; owner; _ |] ->
            let state =
                MatchScenario.BattleState owner "BLK-003" [ "VIM-BLAZED"; "VIM-SOBER" ] 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "other-booth"
                      "BLK-001"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1
                  MatchScenario.PlainCard
                      "own-booth"
                      "BLK-001"
                      MatchScenario.FirstPlayer
                      CardZone.Booth
                      -1 ]
        | [| "multi-toss-damage"; owner; _; _; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim (if mode = "all-badge" then 3UL else 9UL)
        | [| "repeat-damage"; owner; _; _; mode; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState
                owner
                "BLK-003"
                vim
                (if mode = "first-blank" then 2UL else 1UL)
        | [| "repeat-draw"; owner; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state =
                MatchScenario.BattleState
                    owner
                    "BLK-003"
                    vim
                    (if mode = "first-blank" then 2UL else 1UL)

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "effect-draw-1"
                      "VIM-BLAZED"
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      1 ]
        | [| "conditional-rough"; owner; _; target; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            if mode = "true" then
                let id = if target = "self" then "attacker" else "defender"

                let rough =
                    if target = "self" then
                        BlokemonRoughState.Muddled
                    else
                        BlokemonRoughState.NoddedOff

                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId id) with
                          RoughStates = ImmutableArray.Create(MatchScenario.RoughState rough 1) } ]
            else
                state
        | [| "heal-clear"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ { state.Card(CardInstanceId "attacker") with
                      Damage = 30
                      RoughStates =
                          ImmutableArray.Create(
                              MatchScenario.RoughState BlokemonRoughState.DodgyPint 1,
                              MatchScenario.RoughState BlokemonRoughState.Singed 1
                          ) } ]
        | [| "ignore-modifier"; owner; _; defender; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner defender vim 0UL
        | [| "damage-effects"; owner; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "coin-effects"; owner; _; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim (if mode = "badge" then 1UL else 2UL)
        | [| "coin-swap"; owner; _; mode |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state =
                MatchScenario.BattleState owner "BLK-003" vim (if mode = "badge" then 0UL else 2UL)

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-other-booth"
                      "BLK-001"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "full-booth-search"; owner; _; searched; seed |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim (uint64 seed)

            let booth =
                [ for index in 0..4 ->
                      MatchScenario.PlainCard
                          $"full-booth-{index}"
                          "BLK-004"
                          MatchScenario.FirstPlayer
                          CardZone.Booth
                          index ]

            MatchScenario.WithCards
                state
                (MatchScenario.PlainCard
                    "search-card"
                    searched
                    MatchScenario.FirstPlayer
                    CardZone.Stack
                    0
                 :: booth)
        | [| "booth-search"; owner; _; searched; seed; count; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim (uint64 seed)

            MatchScenario.WithCards
                state
                [ for index in 1 .. int count ->
                      MatchScenario.PlainCard
                          $"candidate-{index}"
                          searched
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          index ]
        | [| "coin-search"; owner; _; seed; count; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim (uint64 seed)

            MatchScenario.WithCards
                state
                [ for index in 1 .. int count ->
                      MatchScenario.PlainCard
                          $"candidate-{index}"
                          "VIM-SOBER"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          index ]
        | [| ("optional-zero" | "optional-decline"); owner; _; setup; candidate |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            if candidate = "none" || candidate = "first-draw" then
                state
            else
                let mechanicalId, zone =
                    match setup with
                    | "recover-water" -> "VIM-SOBER", CardZone.EmptiesTray
                    | "recover-bloke" -> "BLK-001", CardZone.EmptiesTray
                    | "recover-barbit" -> "KIT-001", CardZone.EmptiesTray
                    | "recover-fire" -> "VIM-CURRY", CardZone.EmptiesTray
                    | "mitt-water" -> "VIM-SOBER", CardZone.Mitt
                    | "stack-bloke" -> "BLK-001", CardZone.Stack
                    | other -> failwith $"Unknown optional-zero setup {other}."

                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          mechanicalId
                          MatchScenario.FirstPlayer
                          zone
                          (if zone = CardZone.Stack then 1 else -1) ]
        | [| "optional-max"; owner; _; setup; count |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL
            let count = int count

            let mechanicalId index =
                match setup with
                | "recover-water" -> "VIM-SOBER"
                | "recover-bloke"
                | "stack-bloke" -> "BLK-001"
                | "stack-distinct-bloke" -> [| "BLK-001"; "BLK-004"; "BLK-007" |][index - 1]
                | "recover-barbit" -> "KIT-001"
                | "recover-fire" -> "VIM-CURRY"
                | "mitt-water" -> "VIM-SOBER"
                | other -> failwith $"Unknown optional-max setup {other}."

            let zone =
                match setup with
                | "recover-water"
                | "recover-bloke"
                | "recover-barbit"
                | "recover-fire" -> CardZone.EmptiesTray
                | "mitt-water" -> CardZone.Mitt
                | "stack-bloke"
                | "stack-distinct-bloke" -> CardZone.Stack
                | other -> failwith $"Unknown optional-max setup {other}."

            let firstIndex = if owner = "BLK-022" then 1 else 0

            MatchScenario.WithCards
                state
                [ for index in 1 .. count - firstIndex ->
                      MatchScenario.PlainCard
                          $"candidate-{index}"
                          (mechanicalId index)
                          MatchScenario.FirstPlayer
                          zone
                          (if zone = CardZone.Stack then index else -1) ]
        | [| "optional-invalid-duplicate"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
            |> fun state ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "candidate-1"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1
                      MatchScenario.PlainCard
                          "candidate-2"
                          "BLK-010"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          2 ]
        | [| "optional-bar-kit"; owner; _ |] ->
            let state = MatchScenario.BattleState owner "BLK-003" [ "VIM-BEER" ] 0UL

            let barKit =
                MatchScenario.AttachedCard
                    "bar-kit"
                    "KIT-004"
                    MatchScenario.FirstPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "attacker")

            let ownBooth =
                MatchScenario.PlainCard
                    "own-booth"
                    "BLK-003"
                    MatchScenario.FirstPlayer
                    CardZone.Booth
                    -1

            let secondBarKit =
                MatchScenario.AttachedCard
                    "bar-kit-2"
                    "KIT-004"
                    MatchScenario.FirstPlayer
                    CardZone.Attached
                    -1
                    ownBooth.Id

            let attacker = state.Card(CardInstanceId "attacker")

            MatchScenario.WithCards
                state
                [ barKit
                  secondBarKit
                  { attacker with
                      Attachments = attacker.Attachments.Add barKit.Id }
                  { ownBooth with
                      Attachments = ImmutableArray.Create secondBarKit.Id } ]
        | [| "search-all"; owner; _; mechanicalId; candidate |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      candidate
                      mechanicalId
                      MatchScenario.FirstPlayer
                      CardZone.Stack
                      1 ]
        | [| "hand-kit-scale"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.WithCards
                (MatchScenario.BattleState owner "BLK-003" vim 0UL)
                [ MatchScenario.PlainCard
                      "other-mitt-0"
                      "KIT-001"
                      MatchScenario.SecondPlayer
                      CardZone.Mitt
                      -1
                  MatchScenario.PlainCard
                      "other-mitt-1"
                      "KIT-002"
                      MatchScenario.SecondPlayer
                      CardZone.Mitt
                      -1 ]
        | [| "hand-kit-scale"; owner; _; "zero" |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "top-qualifying"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ yield
                      { state.Card(CardInstanceId "first-draw") with
                          MechanicalId = MechanicalCardId "BLK-003"
                          Kind = CardKind.Bloke }

                  for index in 1..4 ->
                      MatchScenario.PlainCard
                          $"top-{index}"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          index ]
        | [| "top-qualifying"; owner; _; "zero" |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ yield
                      { state.Card(CardInstanceId "first-draw") with
                          MechanicalId = MechanicalCardId "BLK-001"
                          Kind = CardKind.Bloke }

                  for index in 1..4 ->
                      MatchScenario.PlainCard
                          $"top-{index}"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          index ]
        | [| "continuous-refresh"; owner; _; setup |] ->
            let attachedVim =
                match setup with
                | "Water" -> [ "VIM-SOBER" ]
                | "Lightning" -> [ "VIM-BEER" ]
                | "Fire" -> [ "VIM-CURRY" ]
                | "unequal" -> [ "VIM-SOBER" ]
                | _ -> []

            let state = MatchScenario.BattleState owner "BLK-001" attachedVim 0UL

            let moveSourceToBooth named =
                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId "attacker") with
                          Zone = CardZone.Booth }
                      MatchScenario.PlainCard
                          "own-oche"
                          named
                          MatchScenario.FirstPlayer
                          CardZone.Oche
                          -1 ]

            match setup with
            | "booth" -> moveSourceToBooth "BLK-001"
            | "out-of-play" ->
                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId "attacker") with
                          Zone = CardZone.Mitt }
                      MatchScenario.PlainCard
                          "own-oche"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Oche
                          -1 ]
            | "no-vim-self" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "vim-sentinel"
                          "VIM-SOBER"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]
            | value when value.StartsWith("booth-named-", StringComparison.Ordinal) ->
                moveSourceToBooth (value["booth-named-".Length ..])
            | value when value.StartsWith("named-", StringComparison.Ordinal) ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "named-condition"
                          (value["named-".Length ..])
                          MatchScenario.FirstPlayer
                          CardZone.Booth
                          -1 ]
            | "first-round" ->
                { state with
                    Players =
                        ImmutableArray.CreateRange(
                            state.Players
                            |> Seq.map (fun player ->
                                if player.Id = MatchScenario.FirstPlayer then
                                    { player with RoundsStarted = 1 }
                                else
                                    player)
                        ) }
            | _ -> state
        | [| "trigger-nonfire"; owner; _; setup |] ->
            match setup with
            | "in-play" -> MatchScenario.BattleState owner "BLK-001" [] 0UL
            | "bar-chit" ->
                let state = MatchScenario.BattleState "BLK-001" "BLK-001" [] 0UL
                let source = state.Card(CardInstanceId "attacker")

                MatchScenario.WithCards
                    state
                    [ { source with
                          MechanicalId = MechanicalCardId owner
                          Zone = CardZone.BarChit
                          StackPosition = 0 }
                      MatchScenario.PlainCard
                          "own-oche"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Oche
                          -1 ]
                |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1
            | other -> failwith $"Unknown trigger-nonfire setup {other}."
        | [| "activated-decline"; owner; _; setup; candidate |] ->
            let state = MatchScenario.BattleState owner "BLK-001" [] 0UL

            match setup with
            | "damaged-self" ->
                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId "attacker") with
                          Damage = 60 } ]
            | "opponent-mitt" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "BLK-004"
                          MatchScenario.SecondPlayer
                          CardZone.Mitt
                          -1 ]
            | "stack-kit" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "KIT-010"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1 ]
            | "stack-bloke-first-round" ->
                let state =
                    MatchScenario.WithCards
                        state
                        [ MatchScenario.PlainCard
                              candidate
                              "BLK-001"
                              MatchScenario.FirstPlayer
                              CardZone.Stack
                              1 ]

                { state with
                    Players =
                        ImmutableArray.CreateRange(
                            state.Players
                            |> Seq.map (fun player ->
                                if player.Id = MatchScenario.FirstPlayer then
                                    { player with RoundsStarted = 1 }
                                else
                                    player)
                        ) }
            | "empties-kit" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "KIT-012"
                          MatchScenario.FirstPlayer
                          CardZone.EmptiesTray
                          -1 ]
            | _ -> state
        | [| "activated-trigger"; owner; _; setup; candidate |] ->
            let state = MatchScenario.BattleState owner "BLK-001" [] 0UL

            match setup with
            | "damaged-self" ->
                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId "attacker") with
                          Damage = 60 } ]
            | "opponent-mitt" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "BLK-004"
                          MatchScenario.SecondPlayer
                          CardZone.Mitt
                          -1 ]
            | "stack-kit" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "KIT-010"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1 ]
            | "stack-bloke-first-round" ->
                let state =
                    MatchScenario.WithCards
                        state
                        [ MatchScenario.PlainCard
                              candidate
                              "BLK-001"
                              MatchScenario.FirstPlayer
                              CardZone.Stack
                              1 ]

                { state with
                    Players =
                        ImmutableArray.CreateRange(
                            state.Players
                            |> Seq.map (fun player ->
                                if player.Id = MatchScenario.FirstPlayer then
                                    { player with RoundsStarted = 1 }
                                else
                                    player)
                        ) }
            | "empties-kit" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          candidate
                          "KIT-012"
                          MatchScenario.FirstPlayer
                          CardZone.EmptiesTray
                          -1
                      MatchScenario.PlainCard
                          "candidate-2"
                          "KIT-012"
                          MatchScenario.FirstPlayer
                          CardZone.EmptiesTray
                          -1 ]
            | "three-card-draw" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "mitt-sentinel"
                          "KIT-001"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1
                      MatchScenario.PlainCard
                          "effect-draw-1"
                          "VIM-BLAZED"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1
                      MatchScenario.PlainCard
                          "effect-draw-2"
                          "VIM-SOBER"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          2 ]
            | "with-own-booth" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "own-booth"
                          "BLK-004"
                          MatchScenario.FirstPlayer
                          CardZone.Booth
                          -1 ]
            | "fixed-draw-sentinel" ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "mitt-sentinel"
                          "KIT-001"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]
            | "default" -> state
            | other -> failwith $"Unknown activated trigger setup {other}."
        | [| "activated-unavailable"; owner; _; setup |] ->
            let state = MatchScenario.BattleState owner "BLK-001" [] 0UL

            let firstRound (state: MatchState) =
                { state with
                    Players =
                        ImmutableArray.CreateRange(
                            state.Players
                            |> Seq.map (fun player ->
                                if player.Id = MatchScenario.FirstPlayer then
                                    { player with RoundsStarted = 1 }
                                else
                                    player)
                        ) }

            match setup with
            | "booth"
            | "booth-first-round" ->
                MatchScenario.WithCards
                    state
                    [ { state.Card(CardInstanceId "attacker") with
                          Zone = CardZone.Booth }
                      MatchScenario.PlainCard
                          "own-oche"
                          "BLK-001"
                          MatchScenario.FirstPlayer
                          CardZone.Oche
                          -1 ]
                |> fun state ->
                    if setup = "booth-first-round" then
                        firstRound state
                    else
                        state
            | "later-round" -> state
            | other -> failwith $"Unknown activated-unavailable setup {other}."
        | [| "promotion-decline"; owner; _; promotesFrom |] ->
            let state = MatchScenario.BattleState promotesFrom "BLK-001" [] 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "promotion"
                      owner
                      MatchScenario.FirstPlayer
                      CardZone.Mitt
                      -1 ]
        | [| "promotion-trigger"; owner; _; promotesFrom |] ->
            let state = MatchScenario.BattleState promotesFrom "BLK-001" [] 0UL

            MatchScenario.WithCards
                state
                [ yield
                      MatchScenario.PlainCard
                          "promotion"
                          owner
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1

                  for index in 1 .. (if owner = "BLK-045" then 8 else 4) ->
                      MatchScenario.PlainCard
                          $"top-{index}"
                          "VIM-BLAZED"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          index

                  if owner = "BLK-093" then
                      yield
                          MatchScenario.PlainCard
                              "opponent-supporter"
                              "KIT-005"
                              MatchScenario.SecondPlayer
                              CardZone.EmptiesTray
                              -1 ]
        | [| "reactive-trigger"; defender; _; attackOwner |] ->
            MatchScenario.BattleState
                attackOwner
                defender
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                0UL
        | [| "reactive-trigger"; defender; _; attackOwner; "blank" |] ->
            MatchScenario.BattleState
                attackOwner
                defender
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                2UL
        | [| ("knockout-trigger" | "knockout-trigger-decline"); _; _ |] ->
            let state =
                MatchScenario.BattleState
                    "BLK-003"
                    "BLK-001"
                    [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                    0UL

            let source =
                MatchScenario.PlainCard
                    "trigger-source"
                    "BLK-026"
                    MatchScenario.SecondPlayer
                    CardZone.Booth
                    -1

            let movableVim =
                MatchScenario.AttachedCard
                    "movable-vim"
                    "VIM-BEER"
                    MatchScenario.SecondPlayer
                    CardZone.Attached
                    -1
                    (CardInstanceId "defender")

            let defender =
                { state.Card(CardInstanceId "defender") with
                    Attachments = ImmutableArray.Create movableVim.Id }

            let prize =
                MatchScenario.PlainCard
                    "prize"
                    "VIM-LAIRY"
                    MatchScenario.FirstPlayer
                    CardZone.BarChit
                    0

            MatchScenario.WithCards state [ source; movableVim; defender; prize ]
            |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1
        | [| ("bar-chit-trigger" | "bar-chit-trigger-decline"); _; _ |] ->
            let state =
                MatchScenario.BattleState
                    "BLK-003"
                    "BLK-001"
                    [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                    0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "triggered-prize"
                      "BLK-113"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      0
                  MatchScenario.PlainCard
                      "extra-prize"
                      "VIM-LAIRY"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      1
                  MatchScenario.PlainCard
                      "defender-bench"
                      "BLK-004"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
            |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2
        | [| "bar-chit-trigger-blank"; _; _ |] ->
            let state =
                MatchScenario.BattleState
                    "BLK-003"
                    "BLK-001"
                    [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                    2UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "triggered-prize"
                      "BLK-113"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      0
                  MatchScenario.PlainCard
                      "extra-prize"
                      "VIM-LAIRY"
                      MatchScenario.FirstPlayer
                      CardZone.BarChit
                      1
                  MatchScenario.PlainCard
                      "defender-bench"
                      "BLK-004"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
            |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2
        | [| "bar-chit-trigger-full-booth"; _; _ |] ->
            let state =
                MatchScenario.BattleState
                    "BLK-003"
                    "BLK-001"
                    [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                    0UL

            MatchScenario.WithCards
                state
                [ yield
                      MatchScenario.PlainCard
                          "triggered-prize"
                          "BLK-113"
                          MatchScenario.FirstPlayer
                          CardZone.BarChit
                          0
                  yield
                      MatchScenario.PlainCard
                          "extra-prize"
                          "VIM-LAIRY"
                          MatchScenario.FirstPlayer
                          CardZone.BarChit
                          1
                  yield
                      MatchScenario.PlainCard
                          "defender-bench"
                          "BLK-004"
                          MatchScenario.SecondPlayer
                          CardZone.Booth
                          -1

                  for index in 0..4 do
                      yield
                          MatchScenario.PlainCard
                              $"full-booth-{index}"
                              "BLK-004"
                              MatchScenario.FirstPlayer
                              CardZone.Booth
                              index ]
            |> fun state -> MatchScenario.WithBarChits state MatchScenario.FirstPlayer 2
        | [| "local-decline"; _; _ |] ->
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 0UL
            |> fun state ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "local-under-test"
                          "KIT-006"
                          MatchScenario.FirstPlayer
                          CardZone.Local
                          -1
                      MatchScenario.PlainCard
                          "mitt-vim"
                          "VIM-SOBER"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1 ]
        | [| "local-trigger"; _; _ |] ->
            MatchScenario.BattleState "BLK-001" "BLK-150" [] 0UL
            |> fun state ->
                MatchScenario.WithCards
                    state
                    [ MatchScenario.PlainCard
                          "local-under-test"
                          "KIT-006"
                          MatchScenario.FirstPlayer
                          CardZone.Local
                          -1
                      MatchScenario.PlainCard
                          "mitt-vim"
                          "VIM-SOBER"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1
                      MatchScenario.PlainCard
                          "mitt-sentinel"
                          "KIT-001"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1
                      MatchScenario.PlainCard
                          "effect-draw-2"
                          "VIM-BLAZED"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          1
                      MatchScenario.PlainCard
                          "effect-draw-3"
                          "VIM-CURRY"
                          MatchScenario.FirstPlayer
                          CardZone.Stack
                          2 ]
        | [| "play-kit"; kit; _; mode |] ->
            let state =
                MatchScenario.BattleState
                    "BLK-003"
                    "BLK-001"
                    []
                    (if kit = "KIT-008" then
                         if mode = "badge" then 1UL else 2UL
                     else
                         0UL)

            let added =
                [ yield
                      MatchScenario.PlainCard
                          "kit-under-test"
                          kit
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1

                  if kit = "KIT-007" then
                      yield
                          MatchScenario.PlainCard
                              "effect-draw-1"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Stack
                              1

                      yield
                          MatchScenario.PlainCard
                              "prize"
                              "VIM-LAIRY"
                              MatchScenario.FirstPlayer
                              CardZone.BarChit
                              0

                  if kit = "KIT-009" then
                      yield
                          MatchScenario.PlainCard
                              "other-mitt-bloke"
                              "BLK-004"
                              MatchScenario.SecondPlayer
                              CardZone.Mitt
                              -1

                  if kit = "KIT-010" then
                      let otherVim =
                          MatchScenario.AttachedCard
                              "other-vim"
                              "VIM-SOBER"
                              MatchScenario.SecondPlayer
                              CardZone.Attached
                              -1
                              (CardInstanceId "defender")

                      yield
                          MatchScenario.PlainCard
                              "own-vim"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Mitt
                              -1

                      yield otherVim

                      yield
                          { state.Card(CardInstanceId "defender") with
                              Attachments = ImmutableArray.Create otherVim.Id }

                  if kit = "KIT-011" then
                      yield
                          MatchScenario.PlainCard
                              "other-mitt-bloke"
                              "BLK-004"
                              MatchScenario.SecondPlayer
                              CardZone.Mitt
                              -1 ]

            let added =
                if kit = "KIT-008" && mode = "badge" then
                    seq {
                        yield! added

                        yield
                            MatchScenario.PlainCard
                                "own-booth"
                                "BLK-004"
                                MatchScenario.FirstPlayer
                                CardZone.Booth
                                -1

                        yield
                            MatchScenario.PlainCard
                                "candidate"
                                "VIM-SOBER"
                                MatchScenario.FirstPlayer
                                CardZone.EmptiesTray
                                -1
                    }
                else
                    added

            let added =
                if kit = "KIT-005" && mode.StartsWith("search-", StringComparison.Ordinal) then
                    seq {
                        yield! added

                        yield
                            { state.Card(CardInstanceId "first-draw") with
                                MechanicalId = MechanicalCardId "BLK-001"
                                Kind = CardKind.Bloke }

                        for index in 1..7 do
                            yield
                                MatchScenario.PlainCard
                                    $"top-{index}"
                                    "BLK-001"
                                    MatchScenario.FirstPlayer
                                    CardZone.Stack
                                    index
                    }
                else
                    added

            MatchScenario.WithCards state added
            |> fun state ->
                if kit = "KIT-007" then
                    MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1
                else
                    state
        | [| "trivial-rough"; owner; defender; _; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner defender vim 2UL
        | [| "trivial-draw"; owner; _; count |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            let added =
                [ yield
                      MatchScenario.PlainCard
                          "mitt-sentinel"
                          "KIT-001"
                          MatchScenario.FirstPlayer
                          CardZone.Mitt
                          -1

                  for index in 1 .. int count - 1 do
                      yield
                          MatchScenario.PlainCard
                              $"effect-draw-{index}"
                              "VIM-BLAZED"
                              MatchScenario.FirstPlayer
                              CardZone.Stack
                              index ]

            MatchScenario.WithCards state added
        | [| "trivial-chuck"; owner; _; "local" |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "local-under-test"
                      "KIT-006"
                      MatchScenario.FirstPlayer
                      CardZone.Local
                      -1 ]
        | [| "trivial-chuck"; owner; _; "other-stack" |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "trivial-booth-damage"; owner; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-other-booth"
                      "BLK-003"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "trivial-booth-damage"; owner; _; _; benchOwner |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-other-booth"
                      benchOwner
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "trivial-swap"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ MatchScenario.PlainCard
                      "a-other-booth"
                      "BLK-001"
                      MatchScenario.SecondPlayer
                      CardZone.Booth
                      -1 ]
        | [| "trivial-distribution"; owner; _; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            let state = MatchScenario.BattleState owner "BLK-003" vim 0UL

            MatchScenario.WithCards
                state
                [ yield
                      MatchScenario.PlainCard
                          "a-other-booth"
                          "BLK-003"
                          MatchScenario.SecondPlayer
                          CardZone.Booth
                          -1

                  if owner = "BLK-094" then
                      yield
                          MatchScenario.PlainCard
                              "b-other-booth"
                              "BLK-003"
                              MatchScenario.SecondPlayer
                              CardZone.Booth
                              -1 ]
        | [| "trivial-soft-spot"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "trivial-copy"; owner; _ |] ->
            let vim =
                MatchScenario.Authority.BasicVim
                |> Seq.collect (fun card -> Seq.replicate 4 card.Id)

            MatchScenario.BattleState owner "BLK-003" vim 0UL
        | [| "kit-condition"; kit; _; owner; _; _ |] ->
            let state = MatchScenario.BattleState owner "BLK-001" [] 0UL

            let defendingVim =
                [ MatchScenario.AttachedCard
                      "other-vim-0"
                      "VIM-BLAZED"
                      MatchScenario.SecondPlayer
                      CardZone.Attached
                      -1
                      (CardInstanceId "defender")
                  MatchScenario.AttachedCard
                      "other-vim-1"
                      "VIM-SOBER"
                      MatchScenario.SecondPlayer
                      CardZone.Attached
                      -1
                      (CardInstanceId "defender") ]

            let defender = state.Card(CardInstanceId "defender")

            MatchScenario.WithCards
                state
                ({ defender with
                    Attachments =
                        ImmutableArray.CreateRange(defendingVim |> Seq.map (fun vim -> vim.Id)) }
                 :: MatchScenario.PlainCard
                     "kit-under-test"
                     kit
                     MatchScenario.FirstPlayer
                     CardZone.Mitt
                     -1
                 :: defendingVim)
        | _ ->
            match scenario with
            | "shirt-off-badge" -> MatchScenario.BattleState "BLK-056" "BLK-001" [ "VIM-LAIRY" ] 1UL
            | "shirt-off-blank" -> MatchScenario.BattleState "BLK-056" "BLK-001" [ "VIM-LAIRY" ] 2UL
            | "still-coming-up-promoted" -> promotedState true
            | "still-coming-up-not-promoted" -> promotedState false
            | "paul-chuckle-trigger-fire" ->
                let state = MatchScenario.BattleState "BLK-076" "BLK-107" [ "VIM-LAIRY" ] 107UL

                let defender =
                    { state.Card(CardInstanceId "defender") with
                        Damage = 70 }

                MatchScenario.WithCards state [ defender ]
            | "paul-chuckle-trigger-nonfire" ->
                MatchScenario.BattleState "BLK-040" "BLK-107" [ "VIM-SOBER" ] 941UL
            | "day-two-forced-blank" ->
                let state =
                    MatchScenario.BattleState
                        "BLK-054"
                        "BLK-001"
                        [ "VIM-SOBER" ]
                        (FullSetBehaviorFixtures.seedForBadge ())

                let defendingVim =
                    [ MatchScenario.AttachedCard
                          "other-vim-0"
                          "VIM-BLAZED"
                          MatchScenario.SecondPlayer
                          CardZone.Attached
                          -1
                          (CardInstanceId "defender")
                      MatchScenario.AttachedCard
                          "other-vim-1"
                          "VIM-SOBER"
                          MatchScenario.SecondPlayer
                          CardZone.Attached
                          -1
                          (CardInstanceId "defender") ]

                let defender = state.Card(CardInstanceId "defender")

                MatchScenario.WithCards
                    state
                    ({ defender with
                        Attachments =
                            ImmutableArray.CreateRange(defendingVim |> Seq.map (fun vim -> vim.Id))
                        RoughStates =
                            ImmutableArray.Create(
                                MatchScenario.RoughState BlokemonRoughState.Muddled 1
                            ) }
                     :: defendingVim)
            | other -> failwith $"Unknown conformance scenario {other}."

    let private attackId (scenario: string) =
        match scenario.Split('|') with
        | [| "trivial-damage"; _; _; effect; _ |] -> effect
        | [| "damage-attach-vim"; _; _; effect; _ |] -> effect
        | [| "damage-heal"; _; _; effect; _; _ |] -> effect
        | [| "damage-self"; _; _; effect; _; _ |] -> effect
        | [| "damage-rough"; _; _; effect; _; _; _; _ |] -> effect
        | [| "damage-rough-effects"; _; _; effect; _; _; _ |] -> effect
        | [| "dynamic-adjust"; _; _; effect; _; _; _; _; _ |] -> effect
        | [| "coin-branch"; _; effect; _; _ |] -> effect
        | [| "damage-chuck-vim"; _; effect; _; _; _; _; _ |] -> effect
        | [| "damage-move-vim"; _; effect; _ |] -> effect
        | [| "damage-chuck-cards"; _; effect; _; _; _ |] -> effect
        | [| "damage-booth-spread"; _; effect; _; _; _; _; _ |] -> effect
        | [| "damage-swap"; _; effect; _; _; _ |] -> effect
        | [| "booth-all-own-swap"; _; effect |] -> effect
        | [| "chuck-vim-booth"; _; effect |] -> effect
        | [| "damage-effect"; _; effect; _; _; _; _; _; _ |] -> effect
        | [| "conditional-adjust"; _; effect; _; _; _; _; _; _ |] -> effect
        | [| "conditional-demote"; _; effect; _; _ |] -> effect
        | [| "conditional-extra-bar"; _; effect |] -> effect
        | [| "gone-smoke"; _; effect |] -> effect
        | [| "multi-toss-damage"; _; effect; _; _; _ |] -> effect
        | [| "repeat-damage"; _; effect; _; _; _ |] -> effect
        | [| "repeat-draw"; _; effect; _ |] -> effect
        | [| "conditional-rough"; _; effect; _; _ |] -> effect
        | [| "heal-clear"; _; effect |] -> effect
        | [| "ignore-modifier"; _; effect; _; _ |] -> effect
        | [| "damage-effects"; _; effect; _ |] -> effect
        | [| "coin-effects"; _; effect; _; _ |] -> effect
        | [| "coin-swap"; _; effect; _ |] -> effect
        | [| "full-booth-search"; _; effect; _; _ |] -> effect
        | [| "booth-search"; _; effect; _; _; _; _ |] -> effect
        | [| "coin-search"; _; effect; _; _; _ |] -> effect
        | [| ("optional-zero" | "optional-decline"); _; effect; _; _ |] -> effect
        | [| "optional-max"; _; effect; _; _ |] -> effect
        | [| "optional-invalid-duplicate"; _; effect |] -> effect
        | [| "optional-bar-kit"; _; effect |] -> effect
        | [| "search-all"; _; effect; _; _ |] -> effect
        | [| "hand-kit-scale"; _; effect |] -> effect
        | [| "hand-kit-scale"; _; effect; _ |] -> effect
        | [| "top-qualifying"; _; effect |]
        | [| "top-qualifying"; _; effect; _ |] -> effect
        | [| "continuous-refresh"; _; effect; _ |] -> effect
        | [| "trigger-nonfire"; _; effect; _ |] -> effect
        | [| "activated-decline"; _; effect; _; _ |] -> effect
        | [| "activated-trigger"; _; effect; _; _ |] -> effect
        | [| "activated-unavailable"; _; effect; _ |] -> effect
        | [| "promotion-decline"; _; effect; _ |] -> effect
        | [| "promotion-trigger"; _; effect; _ |] -> effect
        | [| "reactive-trigger"; _; _; _ |]
        | [| "reactive-trigger"; _; _; _; _ |] -> "BLK-076-B02"
        | [| ("knockout-trigger" | "knockout-trigger-decline"); _; _ |]
        | [| ("bar-chit-trigger" | "bar-chit-trigger-blank" | "bar-chit-trigger-decline" | "bar-chit-trigger-full-booth")
             _
             _ |] -> "BLK-003-B01"
        | [| "local-decline"; _; effect |] -> effect
        | [| "local-trigger"; _; effect |] -> effect
        | [| "play-kit"; _; rule; _ |] -> rule
        | [| "trivial-rough"; _; _; effect; _; _ |] -> effect
        | [| "trivial-draw"; _; effect; _ |] -> effect
        | [| "trivial-chuck"; _; effect; _ |] -> effect
        | [| "trivial-booth-damage"; _; effect; _ |]
        | [| "trivial-booth-damage"; _; effect; _; _ |] -> effect
        | [| "trivial-swap"; _; effect |] -> effect
        | [| "trivial-distribution"; _; effect; _ |] -> effect
        | [| "trivial-soft-spot"; _; effect |] -> effect
        | [| "trivial-copy"; _; effect |] -> effect
        | [| "kit-condition"; _; _; _; effect; _ |] -> effect
        | _ ->
            match scenario with
            | "shirt-off-badge"
            | "shirt-off-blank" -> "BLK-056-B01"
            | "still-coming-up-promoted"
            | "still-coming-up-not-promoted" -> "BLK-080-B02"
            | "paul-chuckle-trigger-fire" -> "BLK-076-B01"
            | "paul-chuckle-trigger-nonfire" -> "BLK-040-B01"
            | "day-two-forced-blank" -> "BLK-054-B01"
            | other -> failwith $"Unknown conformance scenario {other}."

    let private scenarioKey (input: ConformanceInitialStateInput) =
        String.concat
            "|"
            (seq {
                yield input.Route
                yield! input.Parameters
            })

    let private playerId value = PlayerId value

    let private withRandomInput seed (state: MatchState) =
        { state with
            Seed = MatchSeed seed
            Random = { State = seed; ConsumptionIndex = 0 } }

    let private initialStateFailures (input: ConformanceInitialStateInput) (state: MatchState) =
        seq {
            for expected in input.Cards do
                let card =
                    state.Cards
                    |> Seq.tryFind (fun card -> card.Id = CardInstanceId expected.CardId)

                let zone = Enum.Parse<CardZone> expected.Zone

                if
                    card
                    |> Option.forall (fun card ->
                        card.Owner <> playerId expected.Owner
                        || card.MechanicalId <> MechanicalCardId expected.MechanicalId
                        || card.Zone <> zone)
                then
                    yield $"initial-state/card/{expected.CardId}"

            for expected in input.ZoneCounts do
                let zone = Enum.Parse<CardZone> expected.Zone

                let count =
                    state.Cards
                    |> Seq.filter (fun card ->
                        card.Owner = playerId expected.Owner && card.Zone = zone)
                    |> Seq.length

                if count <> expected.Count then
                    yield $"initial-state/zone-count/{expected.Owner}/{expected.Zone}"

            for expected in input.Players do
                let remaining = (state.Player(playerId expected.Player)).BarChitsRemaining

                if remaining <> expected.BarChitsRemaining then
                    yield $"initial-state/player/{expected.Player}/bar-chits-remaining"
        }
        |> Seq.toArray

    let observe authority (obligation: ConformanceSemanticObligation) =
        let scenario = scenarioKey obligation.InitialState

        let state = initialState scenario |> withRandomInput obligation.RandomInput.Seed

        let initialFailures = initialStateFailures obligation.InitialState state
        let primaryAction = obligation.Actions[0]
        let attack = EffectId primaryAction.EffectId
        let engine = MatchEngine authority
        let inputProblems = ResizeArray<string>()
        let mutable publicRouteRejected = false

        let applyPublic stage outcome =
            match outcome with
            | CommandOutcome.Applied(applied, events) -> applied, events
            | CommandOutcome.Rejected(rejected, rejection) ->
                publicRouteRejected <- true
                inputProblems.Add $"action-result/{stage}/rejected/{rejection.Code}"
                rejected, ImmutableArray<_>.Empty

        let remainingChoices = obligation.Actions |> Seq.collect _.Choices |> ResizeArray

        let commandWithInputChoices (action: LegalAction) =
            let requirementIds =
                action.ChoiceRequirements
                |> Seq.filter (fun requirement -> requirement.Chooser = action.Command.Actor)
                |> Seq.map _.Id.Value
                |> Set.ofSeq

            let selected =
                remainingChoices
                |> Seq.filter (fun choice -> Set.contains choice.RequirementId requirementIds)
                |> Seq.toArray

            for choice in selected do
                remainingChoices.Remove choice |> ignore

            if selected.Length = 0 then
                action.Command
            else
                { action.Command with
                    Choices = selected |> Seq.map inputChoice |> ImmutableArray.CreateRange }

        let inputFailures () =
            seq {
                yield! inputProblems

                for choice in remainingChoices do
                    if not choice.WhenAvailable then
                        yield $"action-input/choice/{choice.RequirementId}"
            }
            |> Seq.distinct
            |> Seq.toArray

        let publicAttack (state: MatchState) actor source target effect =
            let targetMatches =
                state.Cards
                |> Seq.exists (fun card ->
                    card.Id = CardInstanceId target
                    && card.Owner <> actor
                    && card.Zone = CardZone.Oche)

            if not targetMatches then
                [||]
            else
                engine.GetLegalActions(state, actor)
                |> Seq.filter (fun action ->
                    action.Kind = LegalActionKind.Attack
                    && action.Affordability = ActionAffordability.Payable
                    && (match action.Command.Action with
                        | MatchAction.Attack(attacker, attack) ->
                            attacker = CardInstanceId source && attack = EffectId effect
                        | _ -> false))
                |> Seq.toArray

        let publicActionsFor state index =
            if index >= obligation.Actions.Length then
                inputProblems.Add $"action-input/missing/{index}"
                [||]
            else
                let input = obligation.Actions[index]
                let actor = playerId input.Actor

                match input.Kind with
                | "Attack" ->
                    publicAttack state actor input.SourceCard input.TargetCard input.EffectId
                | "EndRound" ->
                    engine.GetLegalActions(state, actor)
                    |> Seq.filter (fun action -> action.Kind = LegalActionKind.EndRound)
                    |> Seq.toArray
                | "UsePartyTrick" ->
                    engine.GetLegalActions(state, actor)
                    |> Seq.filter (fun action ->
                        action.Kind = LegalActionKind.UsePartyTrick
                        && (match action.Command.Action with
                            | MatchAction.UsePartyTrick(source, effect) ->
                                source = CardInstanceId input.SourceCard
                                && effect = EffectId input.EffectId
                            | _ -> false))
                    |> Seq.toArray
                | "Promote" ->
                    engine.GetLegalActions(state, actor)
                    |> Seq.filter (fun action ->
                        action.Kind = LegalActionKind.Promote
                        && (match action.Command.Action with
                            | MatchAction.Promote(promotion, target) ->
                                promotion = CardInstanceId input.SourceCard
                                && target = CardInstanceId input.TargetCard
                            | _ -> false))
                    |> Seq.toArray
                | "PlayKit" ->
                    let target =
                        if String.IsNullOrWhiteSpace input.TargetCard then
                            ValueNone
                        else
                            ValueSome(CardInstanceId input.TargetCard)

                    engine.GetLegalActions(state, actor)
                    |> Seq.filter (fun action ->
                        action.Kind = LegalActionKind.PlayKit
                        && (match action.Command.Action with
                            | MatchAction.PlayKit(kit, actualTarget) ->
                                kit = CardInstanceId input.SourceCard && actualTarget = target
                            | _ -> false))
                    |> Seq.toArray
                | kind ->
                    inputProblems.Add $"action-input/kind/{index}/{kind}"
                    [||]

        if scenario.StartsWith("optional-invalid-duplicate|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            let firstCommand =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-invalid-duplicate-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let awaiting, firstEvents =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, firstCommand))
                | _ -> state, ImmutableArray<_>.Empty

            let resolutions =
                engine.GetLegalActions(awaiting, MatchScenario.FirstPlayer)
                |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
                |> Seq.toArray

            let resolutionCommand =
                match resolutions with
                | [| resolution |] -> commandWithInputChoices resolution
                | _ ->
                    MatchScenario.Command
                        awaiting
                        "missing-invalid-duplicate-resolution"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let applied, resolutionEvents, legalActionResult =
                match resolutions with
                | [| _ |] ->
                    match engine.Apply(awaiting, resolutionCommand) with
                    | CommandOutcome.Rejected(rejected, _) ->
                        rejected,
                        ImmutableArray<_>.Empty,
                        "public ResolveEffectChoice rejects two chosen Blokemon of the same type"
                    | CommandOutcome.Applied(settled, events) ->
                        settled, events, "duplicate-type selection unexpectedly applied"
                | _ ->
                    awaiting,
                    ImmutableArray<_>.Empty,
                    $"unexpected public action counts {actions.Length},{resolutions.Length}"

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult = legalActionResult
              Choices =
                [| yield! actions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! firstCommand.Choices |> Seq.map choiceFact
                   yield! resolutions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! resolutionCommand.Choices |> Seq.map choiceFact |]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                Seq.append firstEvents resolutionEvents
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif
            scenario.StartsWith("continuous-refresh|", StringComparison.Ordinal)
            || scenario.StartsWith("trigger-nonfire|", StringComparison.Ordinal)
        then
            let actions = publicActionsFor state 0

            let applied, events =
                match actions with
                | [| action |] -> applyPublic "0" (engine.Apply(state, action.Command))
                | _ -> state, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public EndRound action"
                else
                    $"unexpected public EndRound action count {actions.Length}"
              Choices = [||]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("activated-unavailable|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 0 then
                    "zero public activated party-trick actions"
                else
                    $"unexpected activated party-trick action count {actions.Length}"
              Choices = [||]
              CanonicalState = canonicalState scenario state
              OrderedEvents = [||] }
        elif scenario.StartsWith("activated-decline|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-activated-conformance-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let applied, events =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public activated party-trick action"
                else
                    $"unexpected public activated action count {actions.Length}"
              Choices =
                [| yield! actions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! command.Choices |> Seq.map choiceFact |]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("promotion-decline|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-promotion-conformance-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let applied, events =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public promotion action"
                else
                    $"unexpected public promotion action count {actions.Length}"
              Choices =
                [| yield! actions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! command.Choices |> Seq.map choiceFact |]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("activated-trigger|", StringComparison.Ordinal) then
            let scenarioParts = scenario.Split('|')
            let owner = scenarioParts[1]
            let candidate = scenarioParts[4]

            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-activated-trigger-conformance-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let mutable applied, firstEvents =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let events = ResizeArray<MatchEvent>(firstEvents)
            let choices = ResizeArray<string>()

            match actions with
            | [| action |] ->
                choices.AddRange(action.ChoiceRequirements |> Seq.map requirementFact)
                choices.AddRange(command.Choices |> Seq.map choiceFact)
            | _ -> ()

            while applied.Phase = MatchPhase.AwaitingEffectChoice && not publicRouteRejected do
                let chooser = applied.PendingEffect.Value.Chooser

                let resolutionActions =
                    engine.GetLegalActions(applied, chooser)
                    |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
                    |> Seq.toArray

                match resolutionActions with
                | [| resolution |] ->
                    let resolutionCommand = commandWithInputChoices resolution

                    choices.AddRange(resolution.ChoiceRequirements |> Seq.map requirementFact)

                    choices.AddRange(resolutionCommand.Choices |> Seq.map choiceFact)

                    let settled, resolutionEvents =
                        applyPublic "resolution" (engine.Apply(applied, resolutionCommand))

                    applied <- settled
                    events.AddRange resolutionEvents
                | actions ->
                    publicRouteRejected <- true
                    inputProblems.Add $"action-result/resolution/available/{actions.Length}"

            let stillAvailable =
                engine.GetLegalActions(applied, MatchScenario.FirstPlayer)
                |> Seq.exists (fun action ->
                    match action.Command.Action with
                    | MatchAction.UsePartyTrick(source, effect) ->
                        source = CardInstanceId "attacker" && effect = attack
                    | _ -> false)

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public activated party-trick action"
                else
                    $"unexpected public activated action count {actions.Length}"
              Choices = choices.ToArray()
              CanonicalState =
                Array.append
                    (canonicalState scenario applied)
                    [| $"activation-available={stillAvailable.ToString().ToLowerInvariant()}" |]
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("promotion-trigger|", StringComparison.Ordinal) then
            let owner = scenario.Split('|')[1]

            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-structured-public-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let applied, events =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public promotion action"
                else
                    $"unexpected public promotion action count {actions.Length}"
              Choices =
                [| yield! actions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! command.Choices |> Seq.map choiceFact |]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("knockout-trigger", StringComparison.Ordinal) then
            let resolutionInput = obligation.Actions[1]

            let selectedVim =
                if String.IsNullOrWhiteSpace resolutionInput.TargetCard then
                    ValueNone
                else
                    ValueSome(CardInstanceId resolutionInput.TargetCard)

            let attackActions = publicActionsFor state 0

            let afterAttack, attackEvents =
                match attackActions with
                | [| action |] -> applyPublic "0" (engine.Apply(state, action.Command))
                | _ -> state, ImmutableArray<_>.Empty

            let resolutionActions =
                match afterAttack.PendingKnockout with
                | ValueSome pending when
                    pending.TriggerSource = CardInstanceId resolutionInput.SourceCard
                    && pending.TriggerEffect = EffectId resolutionInput.EffectId
                    && pending.Chooser = playerId resolutionInput.Actor
                    ->
                    engine.GetLegalActions(afterAttack, playerId resolutionInput.Actor)
                    |> Seq.filter (fun action ->
                        action.Kind = LegalActionKind.ResolveKnockoutTrigger
                        && action.Command.Action = MatchAction.ResolveKnockoutTrigger selectedVim)
                    |> Seq.toArray
                | _ -> [||]

            let applied, resolutionEvents =
                match resolutionActions with
                | [| action |] -> applyPublic "1" (engine.Apply(afterAttack, action.Command))
                | _ -> afterAttack, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if attackActions.Length = 1 && resolutionActions.Length = 1 then
                    if selectedVim.IsNone then
                        "one payable public Attack action followed by one public knockout-trigger decline resolution"
                    else
                        "one payable public Attack action followed by one public knockout-trigger resolution selecting the eligible Lightning Vim"
                else
                    $"unexpected public action counts {attackActions.Length},{resolutionActions.Length}"
              Choices =
                Seq.append
                    (attackActions |> Seq.collect _.ChoiceRequirements)
                    (resolutionActions |> Seq.collect _.ChoiceRequirements)
                |> Seq.map requirementFact
                |> Seq.toArray
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                Seq.append attackEvents resolutionEvents
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("bar-chit-trigger", StringComparison.Ordinal) then
            let fullBooth = obligation.Actions.Length = 1

            let chooseBooth = not fullBooth && obligation.Actions[1].TargetCard = "Booth"

            let attackActions = publicActionsFor state 0

            let afterAttack, attackEvents =
                match attackActions with
                | [| action |] -> applyPublic "0" (engine.Apply(state, action.Command))
                | _ -> state, ImmutableArray<_>.Empty

            let resolutionActions =
                if fullBooth then
                    [||]
                else
                    let input = obligation.Actions[1]

                    let pendingMatches =
                        afterAttack.PendingBarChits
                        |> Seq.exists (fun pending ->
                            pending.Player = playerId input.Actor
                            && pending.Card = CardInstanceId input.SourceCard
                            && pending.Effect = EffectId input.EffectId)

                    if pendingMatches then
                        engine.GetLegalActions(afterAttack, playerId input.Actor)
                        |> Seq.filter (fun action ->
                            action.Kind = LegalActionKind.ResolveBarChitTrigger
                            && action.Command.Action = MatchAction.ResolveBarChitTrigger
                                chooseBooth)
                        |> Seq.toArray
                    else
                        [||]

            let applied, resolutionEvents =
                match resolutionActions with
                | [| action |] -> applyPublic "1" (engine.Apply(afterAttack, action.Command))
                | _ -> afterAttack, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if fullBooth && attackActions.Length = 1 then
                    "one payable public Attack action and no Bar Chit trigger action because BoothHasSpace is false"
                elif attackActions.Length = 1 && resolutionActions.Length = 1 then
                    if chooseBooth then
                        "one payable public Attack action followed by one public Bar Chit trigger resolution choosing Booth"
                    else
                        "one payable public Attack action followed by one public Bar Chit trigger resolution declining Booth"
                else
                    $"unexpected public action counts {attackActions.Length},{resolutionActions.Length}"
              Choices =
                Seq.append
                    (attackActions |> Seq.collect _.ChoiceRequirements)
                    (resolutionActions |> Seq.collect _.ChoiceRequirements)
                |> Seq.map requirementFact
                |> Seq.toArray
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                Seq.append attackEvents resolutionEvents
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("local-trigger|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-local-trigger-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let mutable applied, firstEvents =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let events = ResizeArray<MatchEvent>(firstEvents)
            let choices = ResizeArray<string>()

            match actions with
            | [| action |] ->
                choices.AddRange(action.ChoiceRequirements |> Seq.map requirementFact)
                choices.AddRange(command.Choices |> Seq.map choiceFact)
            | _ -> ()

            while applied.Phase = MatchPhase.AwaitingEffectChoice && not publicRouteRejected do
                let chooser = applied.PendingEffect.Value.Chooser

                let resolutionActions =
                    engine.GetLegalActions(applied, chooser)
                    |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
                    |> Seq.toArray

                match resolutionActions with
                | [| resolution |] ->
                    let resolutionCommand = commandWithInputChoices resolution

                    choices.AddRange(resolution.ChoiceRequirements |> Seq.map requirementFact)

                    choices.AddRange(resolutionCommand.Choices |> Seq.map choiceFact)

                    let settled, resolutionEvents =
                        applyPublic "resolution" (engine.Apply(applied, resolutionCommand))

                    applied <- settled
                    events.AddRange resolutionEvents
                | pendingActions ->
                    publicRouteRejected <- true
                    inputProblems.Add $"action-result/resolution/available/{pendingActions.Length}"

            let stillAvailable =
                engine.GetLegalActions(applied, MatchScenario.FirstPlayer)
                |> Seq.exists (fun action ->
                    match action.Command.Action with
                    | MatchAction.UsePartyTrick(source, effect) ->
                        source = CardInstanceId "local-under-test" && effect = attack
                    | _ -> false)

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public house-rule party-trick action"
                else
                    $"unexpected public house-rule action count {actions.Length}"
              Choices = choices.ToArray()
              CanonicalState =
                Array.append
                    (canonicalState scenario applied)
                    [| $"activation-available={stillAvailable.ToString().ToLowerInvariant()}" |]
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("local-decline|", StringComparison.Ordinal) then
            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-local-conformance-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let applied, events =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public house-rule party-trick action"
                else
                    $"unexpected public house-rule action count {actions.Length}"
              Choices =
                [| yield! actions |> Seq.collect _.ChoiceRequirements |> Seq.map requirementFact
                   yield! command.Choices |> Seq.map choiceFact |]
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("play-kit|", StringComparison.Ordinal) then
            let targeted, decline =
                match scenario.Split('|') with
                | [| _; _; _; "targeted" |] -> true, false
                | [| _; _; _; "untargeted" |] -> false, false
                | [| _; _; _; "decline" |] -> false, true
                | [| _; "KIT-005"; _; "search-max" |]
                | [| _; "KIT-005"; _; "search-zero" |] -> false, false
                | [| _; "KIT-008"; _; "badge" |] -> false, false
                | _ -> failwith "Malformed play-kit scenario."

            let actions = publicActionsFor state 0

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-structured-public-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let mutable applied, firstEvents =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let events = ResizeArray<MatchEvent>(firstEvents)
            let choices = ResizeArray<string>()

            match actions with
            | [| action |] ->
                choices.AddRange(action.ChoiceRequirements |> Seq.map requirementFact)
                choices.AddRange(command.Choices |> Seq.map choiceFact)
            | _ -> ()

            while applied.Phase = MatchPhase.AwaitingEffectChoice && not publicRouteRejected do
                let chooser = applied.PendingEffect.Value.Chooser

                let resolutionActions =
                    engine.GetLegalActions(applied, chooser)
                    |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
                    |> Seq.toArray

                match resolutionActions with
                | [| resolution |] ->
                    let resolutionCommand = commandWithInputChoices resolution

                    choices.AddRange(resolution.ChoiceRequirements |> Seq.map requirementFact)

                    choices.AddRange(resolutionCommand.Choices |> Seq.map choiceFact)

                    let settled, resolutionEvents =
                        applyPublic "resolution" (engine.Apply(applied, resolutionCommand))

                    applied <- settled
                    events.AddRange resolutionEvents
                | pendingActions ->
                    publicRouteRejected <- true
                    inputProblems.Add $"action-result/resolution/available/{pendingActions.Length}"

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if actions.Length = 1 then
                    "one public kit-play action"
                else
                    $"unexpected public kit-play action count {actions.Length}"
              Choices = choices.ToArray()
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario.StartsWith("kit-condition|", StringComparison.Ordinal) then
            let _, _, _, _, replyEffect, _ =
                match scenario.Split('|') with
                | [| scenario; kit; rule; owner; effect; damage |] ->
                    scenario, kit, rule, owner, effect, damage
                | _ -> failwith "Malformed kit-condition scenario."

            let kitActions = publicActionsFor state 0

            let afterKit, kitEvents =
                match kitActions with
                | [| action |] -> applyPublic "0" (engine.Apply(state, action.Command))
                | _ -> state, ImmutableArray<_>.Empty

            let endRoundActions = publicActionsFor afterKit 1

            let opponentRound, endRoundEvents =
                match endRoundActions with
                | [| action |] -> applyPublic "1" (engine.Apply(afterKit, action.Command))
                | _ -> afterKit, ImmutableArray<_>.Empty

            let replyActions = publicActionsFor opponentRound 2

            let applied, replyEvents =
                match replyActions with
                | [| action |] -> applyPublic "2" (engine.Apply(opponentRound, action.Command))
                | _ -> opponentRound, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if
                    kitActions.Length = 1 && endRoundActions.Length = 1 && replyActions.Length = 1
                then
                    "one public target-specific kit play followed by one payable public reply Attack action"
                else
                    $"unexpected public action counts {kitActions.Length},{endRoundActions.Length},{replyActions.Length}"
              Choices =
                Seq.append
                    (kitActions |> Seq.collect _.ChoiceRequirements)
                    (replyActions |> Seq.collect _.ChoiceRequirements)
                |> Seq.map requirementFact
                |> Seq.toArray
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                Seq.concat [ kitEvents; endRoundEvents; replyEvents ]
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        elif scenario = "day-two-forced-blank" then
            let dayTwoActions = publicActionsFor state 0

            let afterDayTwo, dayTwoEvents =
                match dayTwoActions with
                | [| action |] -> applyPublic "0" (engine.Apply(state, action.Command))
                | _ -> state, ImmutableArray<_>.Empty

            let replyActions = publicActionsFor afterDayTwo 1

            let applied, replyEvents =
                match replyActions with
                | [| action |] -> applyPublic "1" (engine.Apply(afterDayTwo, action.Command))
                | _ -> afterDayTwo, ImmutableArray<_>.Empty

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult =
                if dayTwoActions.Length = 1 && replyActions.Length = 1 then
                    "one payable public Attack action followed by one payable public reply Attack action"
                else
                    $"unexpected public Attack action counts {dayTwoActions.Length},{replyActions.Length}"
              Choices =
                Seq.append
                    (dayTwoActions |> Seq.collect _.ChoiceRequirements)
                    (replyActions |> Seq.collect _.ChoiceRequirements)
                |> Seq.map requirementFact
                |> Seq.toArray
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                Seq.append dayTwoEvents replyEvents
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }
        else

            let actions = publicActionsFor state 0

            let legalActionResult =
                match actions with
                | [| _ |] -> "one payable public Attack action"
                | _ -> $"unexpected public Attack action count {actions.Length}"

            let command =
                match actions with
                | [| action |] -> commandWithInputChoices action
                | _ ->
                    MatchScenario.Command
                        state
                        "missing-conformance-action"
                        MatchScenario.FirstPlayer
                        ImmutableArray<_>.Empty
                        MatchAction.EndRound

            let mutable applied, firstEvents =
                match actions with
                | [| _ |] -> applyPublic "0" (engine.Apply(state, command))
                | _ -> state, ImmutableArray<_>.Empty

            let events = ResizeArray<MatchEvent>(firstEvents)
            let choices = ResizeArray<string>()

            match actions with
            | [| action |] ->
                choices.AddRange(action.ChoiceRequirements |> Seq.map requirementFact)
                choices.AddRange(command.Choices |> Seq.map choiceFact)
            | _ -> ()

            let mutable resolutions = 0

            while applied.Phase = MatchPhase.AwaitingEffectChoice && not publicRouteRejected do
                if resolutions >= 20 then
                    failwith $"The exact scenario {obligation.Id} did not settle."

                resolutions <- resolutions + 1
                let chooser = applied.PendingEffect.Value.Chooser

                let resolutionActions =
                    engine.GetLegalActions(applied, chooser)
                    |> Seq.filter (fun action -> action.Kind = LegalActionKind.ResolveEffectChoice)
                    |> Seq.toArray

                match resolutionActions with
                | [| resolution |] ->
                    let resolutionCommand = commandWithInputChoices resolution

                    choices.AddRange(resolution.ChoiceRequirements |> Seq.map requirementFact)

                    choices.AddRange(resolutionCommand.Choices |> Seq.map choiceFact)

                    let settled, resolutionEvents =
                        applyPublic "resolution" (engine.Apply(applied, resolutionCommand))

                    applied <- settled
                    events.AddRange resolutionEvents
                | pendingActions ->
                    publicRouteRejected <- true
                    inputProblems.Add $"action-result/resolution/available/{pendingActions.Length}"

            let relevant = relevantEvents scenario

            { InitialStateFailures = initialFailures
              InputFailures = inputFailures ()
              LegalActionResult = legalActionResult
              Choices = choices.ToArray()
              CanonicalState = canonicalState scenario applied
              OrderedEvents =
                events
                |> Seq.filter (fun event -> Set.contains event.Kind relevant)
                |> Seq.map eventFact
                |> Seq.toArray }

    let private stablePaths category pathOf (values: string array) =
        let occurrences = Collections.Generic.Dictionary<string, int>()

        values
        |> Array.map (fun value ->
            let basePath = $"{category}/{pathOf value}"
            let mutable occurrence = 0

            if occurrences.TryGetValue(basePath, &occurrence) then
                occurrences[basePath] <- occurrence + 1
                $"{basePath}/{occurrence}", value
            else
                occurrences[basePath] <- 1
                basePath, value)

    let private choicePath (value: string) =
        let parts = value.Split('|')
        $"{parts[0]}/{parts[1]}"

    let private canonicalPath (value: string) =
        let parts = value.Split('|')
        let head = parts[0].Split('=')

        if head[0] = "card" || head[0] = "effect" then
            $"{head[0]}/{head[1]}"
        else
            head[0]

    let private eventPath (value: string) = value.Split('|')[0]

    let private sequenceFailures category pathOf expected actual =
        let expectedFacts = stablePaths category pathOf expected
        let actualFacts = stablePaths category pathOf actual
        let expectedByPath = expectedFacts |> Map.ofArray
        let actualByPath = actualFacts |> Map.ofArray

        seq {
            for KeyValue(path, value) in expectedByPath do
                if Map.tryFind path actualByPath <> Some value then
                    yield path

            for KeyValue(path, _) in actualByPath do
                if not (Map.containsKey path expectedByPath) then
                    yield path

            if
                expectedByPath = actualByPath
                && (expectedFacts |> Array.map fst) <> (actualFacts |> Array.map fst)
            then
                yield $"{category}/order"
        }

    let assertionFailures (obligation: ConformanceSemanticObligation) (observation: Observation) =
        seq {
            yield! observation.InitialStateFailures
            yield! observation.InputFailures

            if observation.LegalActionResult <> obligation.LegalActionResult then
                yield "legal-action/result"

            yield!
                sequenceFailures "choices" choicePath obligation.ExpectedChoices observation.Choices

            yield!
                sequenceFailures
                    "canonical-state"
                    canonicalPath
                    obligation.CanonicalState
                    observation.CanonicalState

            yield!
                sequenceFailures
                    "ordered-events"
                    eventPath
                    obligation.OrderedEvents
                    observation.OrderedEvents
        }
        |> Seq.distinct
        |> Seq.toArray

    let semanticMutationKilled
        (expectedFailurePaths: string array)
        (observation: Result<string array, exn>)
        =
        match observation with
        | Error _ -> false
        | Ok failures ->
            failures.Length > 0
            && Set.isSubset (failures |> Set.ofArray) (expectedFailurePaths |> Set.ofArray)

    let mutatedFixture canonical (mutation: ConformanceSemanticMutation) =
        let mutatedNode = canonical |> ConformanceFixture.toNode |> _.DeepClone()

        match mutation.Operation with
        | "increment-integer" -> incrementInteger mutation.Pointer mutatedNode
        | "decrement-integer" -> decrementInteger mutation.Pointer mutatedNode
        | "toggle-boolean" -> toggleBoolean mutation.Pointer mutatedNode
        | "remove-branch" -> removeBranch mutation.Pointer mutatedNode
        | value when value.StartsWith("replace-string:", StringComparison.Ordinal) ->
            replaceString mutation.Pointer value["replace-string:".Length ..] mutatedNode
        | operation -> failwith $"Unknown scoped mutation operation {operation}."

        mutatedNode.ToJsonString() |> ConformanceFixture.parse

type ConformanceObligationTests() =

    [<Test>]
    member _.``the compiler should derive stable recursive keys from the canonical typed fixture``
        ()
        =
        let compilation = ConformanceObligationTestCases.compilation ()

        compilation.Required
        |> Seq.map _.Key
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        compilation.Required
        |> Seq.map _.ProgramKey
        |> Set.ofSeq
        |> should equal (compilation.Programs |> Seq.map _.ProgramKey |> Set.ofSeq)

        compilation.Required
        |> Seq.map _.Key
        |> should contain "BLK-056/attack/BLK-056-B01/root/1/otherwise/0::opcode/DealSelfDamage"

        compilation.Required
        |> Seq.map _.Key
        |> should contain "BLK-080/attack/BLK-080-B02/root/0/predicate/0::truth/true"

    [<Test>]
    member _.``every executable obligation key should have exactly one current semantic fact``() =
        let compilation = ConformanceObligationTestCases.compilation ()
        let facts = ConformanceObligationFacts.load ()

        let executable =
            compilation.Programs
            |> Array.filter (fun program -> program.Disposition = Executable)

        let required = executable |> Array.collect _.Required |> Seq.map _.Key |> Set.ofSeq

        let executablePrograms = executable |> Seq.map _.ProgramKey |> Set.ofSeq
        let covers = facts.Obligations |> Array.collect _.Covers

        ConformanceObligationFacts.malformed facts |> should be Empty

        facts.Obligations
        |> Seq.map _.Id
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        covers |> ConformanceObligationFacts.duplicates |> should be Empty

        covers |> Set.ofArray |> should equal required

        facts.Obligations
        |> Seq.map _.ProgramKey
        |> Set.ofSeq
        |> should equal executablePrograms

        let kindSegment =
            function
            | ProgramKind.Attack -> "attack"
            | ProgramKind.PartyTrick -> "party-trick"
            | ProgramKind.HouseRule -> "house-rule"

        for obligation in facts.Obligations do
            let compiled =
                executable
                |> Array.find (fun program -> program.ProgramKey = obligation.ProgramKey)

            let reviewed = obligation.ReviewedProgram
            let expectedKind = kindSegment compiled.Row.Kind

            (reviewed.OwnerId, reviewed.Kind, reviewed.MechanicalId)
            |> should equal (compiled.Row.OwnerId, expectedKind, compiled.Row.MechanicalId)

            $"{reviewed.OwnerId}/{reviewed.Kind}/{reviewed.MechanicalId}"
            |> should equal obligation.ProgramKey

        compilation.Required
        |> Array.forall (fun required -> required.Multiplicity = ExactlyOnce)
        |> should be True

    [<Test>]
    member _.``all independently reviewed obligations should match exact public state and ordered events``
        ()
        =
        let facts = ConformanceObligationFacts.load ()

        for obligation in facts.Obligations do
            let observation =
                ConformanceObligationTestCases.observe MatchScenario.Authority obligation

            ConformanceObligationTestCases.assertionFailures obligation observation
            |> should be Empty

    [<Test>]
    member _.``a wrong declared failure path should not count as a semantic mutation kill``() =
        ConformanceObligationTestCases.semanticMutationKilled
            [| "canonical-state/phase" |]
            (Ok [| "ordered-events/DamagePlaced" |])
        |> should be False

    [<Test>]
    member _.``a thrown observation should not count as a semantic mutation kill``() =
        let thrown: Result<string array, exn> =
            Error(InvalidOperationException("synthetic observation failure"))

        ConformanceObligationTestCases.semanticMutationKilled [| "canonical-state/phase" |] thrown
        |> should be False

    [<Test>]
    member _.``an undeclared collateral failure should not count as a semantic mutation kill``() =
        ConformanceObligationTestCases.semanticMutationKilled
            [| "canonical-state/phase" |]
            (Ok [| "canonical-state/phase"; "ordered-events/DamagePlaced" |])
        |> should be False

    [<Test>]
    member _.``a structured BLK-036 setup identity drift should fail the normal semantic comparison``
        ()
        =
        let obligation =
            (ConformanceObligationFacts.load ()).Obligations
            |> Array.find (fun value -> value.Id = "keep-it-going-takes-extra-bar-chit")

        let driftedCards =
            obligation.InitialState.Cards
            |> Array.map (fun card ->
                if card.CardId = "defender" then
                    { card with MechanicalId = "BLK-003" }
                else
                    card)

        let drifted =
            { obligation with
                InitialState =
                    { obligation.InitialState with
                        Cards = driftedCards } }

        ConformanceObligationTestCases.observe MatchScenario.Authority drifted
        |> ConformanceObligationTestCases.assertionFailures drifted
        |> should contain "initial-state/card/defender"

    [<Test>]
    member _.``a structured action drift should fail the normal semantic comparison``() =
        let obligation =
            (ConformanceObligationFacts.load ()).Obligations
            |> Array.find (fun value -> value.Id = "keep-it-going-takes-extra-bar-chit")

        let action = obligation.Actions[0]

        let drifted =
            { obligation with
                Actions = [| { action with EffectId = "BLK-003-B01" } |] }

        ConformanceObligationTestCases.observe MatchScenario.Authority drifted
        |> ConformanceObligationTestCases.assertionFailures drifted
        |> should contain "legal-action/result"

    [<Test>]
    member _.``a structured random drift should fail the normal semantic comparison``() =
        let obligation =
            (ConformanceObligationFacts.load ()).Obligations
            |> Array.find (fun value -> value.Id = "shirt-off-badge")

        let drifted =
            { obligation with
                RandomInput = { Seed = 2UL } }

        ConformanceObligationTestCases.observe MatchScenario.Authority drifted
        |> ConformanceObligationTestCases.assertionFailures drifted
        |> should contain "ordered-events/BeerMatTossed"

    [<Test>]
    member _.``every named scoped mutation should fail its semantic obligation``() =
        let facts = ConformanceObligationFacts.load ()
        let canonical = ConformanceFixture.load ()
        let compilation = ConformanceObligationCompiler.compile canonical

        let operationMatches expected actual =
            match expected, actual with
            | "increment-integer", IncrementInteger
            | "decrement-integer", DecrementInteger
            | "toggle-boolean", ToggleBoolean
            | "remove-branch", RemoveBranch -> true
            | expected, ReplaceString value -> expected = $"replace-string:{value}"
            | _ -> false

        let operationName =
            function
            | IncrementInteger -> "increment-integer"
            | DecrementInteger -> "decrement-integer"
            | ToggleBoolean -> "toggle-boolean"
            | RemoveBranch -> "remove-branch"
            | ReplaceString value -> $"replace-string:{value}"

        facts.Mutations
        |> Seq.map _.Id
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        facts.Mutations
        |> Seq.map (fun mutation -> mutation.Pointer, mutation.NamedObligation)
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        facts.Mutations
        |> Seq.map (fun mutation -> mutation.Pointer, mutation.NamedObligation, mutation.Operation)
        |> Set.ofSeq
        |> should
            equal
            (compilation.Mutations
             |> Seq.map (fun mutation ->
                 mutation.Pointer, mutation.NamedObligation, operationName mutation.Operation)
             |> Set.ofSeq)

        for mutation in facts.Mutations do
            let obligation =
                facts.Obligations
                |> Array.find (fun obligation -> obligation.Id = mutation.ScenarioObligation)

            obligation.Covers |> should contain mutation.NamedObligation

            compilation.Mutations
            |> Array.exists (fun target ->
                target.Pointer = mutation.Pointer
                && target.NamedObligation = mutation.NamedObligation
                && operationMatches mutation.Operation target.Operation)
            |> should be True

            let mutated = ConformanceObligationTestCases.mutatedFixture canonical mutation

            let observation =
                try
                    ConformanceObligationTestCases.observe mutated.Authority obligation
                    |> ConformanceObligationTestCases.assertionFailures obligation
                    |> Ok
                with error ->
                    Error error

            match observation with
            | Error error ->
                raise (
                    InvalidOperationException(
                        $"Scoped mutation {mutation.Id} did not produce a normal public observation.",
                        error
                    )
                )
            | Ok failures when failures.Length = 0 ->
                failwith
                    $"Scoped mutation {mutation.Id} produced no semantic disagreement for its independently declared obligation."
            | Ok _ when
                not (
                    ConformanceObligationTestCases.semanticMutationKilled
                        mutation.ExpectedFailurePaths
                        observation
                )
                ->
                failwith
                    $"Scoped mutation {mutation.Id} did not fail only its independently declared semantic assertion paths."
            | Ok _ -> ()

    [<Test>]
    member _.``every scoped mutation should remove its superseded compiler key``() =
        let facts = ConformanceObligationFacts.load ()
        let canonical = ConformanceFixture.load ()

        for mutation in facts.Mutations do
            let mutated = ConformanceObligationTestCases.mutatedFixture canonical mutation

            let mutatedRequired =
                mutated
                |> ConformanceObligationCompiler.compile
                |> _.Required
                |> Seq.map _.Key
                |> Set.ofSeq

            Set.contains mutation.NamedObligation mutatedRequired |> should be False

    [<Test>]
    member _.``structural programs should map every occurrence predicate and non-empty edge to rationale``
        ()
        =
        let compilation = ConformanceObligationTestCases.compilation ()
        let facts = ConformanceObligationFacts.load ()

        let structural =
            compilation.Programs
            |> Array.filter (fun program -> program.Disposition = StructuralNonExecutable)

        let structuralRequired = structural |> Array.collect _.Required
        let rationaleKeys = facts.StructuralRationales |> Seq.map _.Key |> Set.ofSeq

        facts.StructuralRationales
        |> Seq.map _.Key
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        structuralRequired |> Seq.map _.Key |> Set.ofSeq |> should equal rationaleKeys

        facts.StructuralRationales
        |> Array.forall (fun rationale ->
            rationale.Rationale = ConformanceObligationCompiler.StructuralRationale
            && (structuralRequired
                |> Array.exists (fun required ->
                    required.Key = rationale.Key && required.ProgramKey = rationale.ProgramKey)))
        |> should be True

        structuralRequired
        |> Array.filter (fun required -> required.Kind = SemanticDimension)
        |> should be Empty

        structuralRequired
        |> Array.forall (fun required -> required.Multiplicity = ExactlyOnce)
        |> should be True

        structural
        |> Array.collect _.NonMutableOperands
        |> Array.forall (fun operand ->
            operand.Rationale = ConformanceObligationCompiler.StructuralRationale)
        |> should be True

        let compiledNonMutable = compilation.NonMutableOperands

        facts.NonMutableOperands
        |> Seq.map (fun rationale -> rationale.Pointer, rationale.NamedItem)
        |> ConformanceObligationFacts.duplicates
        |> should be Empty

        compiledNonMutable
        |> Seq.map (fun operand -> operand.Pointer, operand.NamedItem)
        |> Set.ofSeq
        |> should
            equal
            (facts.NonMutableOperands
             |> Seq.map (fun rationale -> rationale.Pointer, rationale.NamedItem)
             |> Set.ofSeq)

        facts.NonMutableOperands
        |> Array.forall (fun rationale ->
            compiledNonMutable
            |> Array.exists (fun compiled ->
                compiled.Pointer = rationale.Pointer
                && compiled.ProgramKey = rationale.ProgramKey
                && compiled.NamedItem = rationale.NamedItem
                && compiled.Rationale = rationale.Rationale))
        |> should be True

    [<Test>]
    member _.``schema additions should fail expectation loading``() =
        let path = ConformanceObligationFacts.path ()

        let root =
            match JsonNode.Parse(IO.File.ReadAllText path) with
            | null -> failwith "The conformance obligation facts were empty."
            | node -> node.AsObject()

        root["schemaGrowth"] <- JsonValue.Create true
        let added = IO.Path.GetTempFileName()

        try
            IO.File.WriteAllText(added, root.ToJsonString())

            (fun () -> ConformanceObligationFacts.loadFrom added |> ignore)
            |> should throw typeof<JsonException>
        finally
            IO.File.Delete added
