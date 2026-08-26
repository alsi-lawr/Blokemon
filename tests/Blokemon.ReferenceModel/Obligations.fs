namespace Blokemon.ReferenceModel

open System
open System.IO
open System.Text.Json

[<Struct>]
type ReferenceSetupRoute =
    private
    | ReferenceSetupRoute of string

    member this.Value =
        let (ReferenceSetupRoute value) = this
        value

[<RequireQualifiedAccess>]
type ReferenceInputActionKind =
    | Attack = 0
    | EndRound = 1
    | UsePartyTrick = 2
    | Promote = 3
    | PlayKit = 4
    | ResolveKnockoutTrigger = 5
    | ResolveBarChitTrigger = 6

[<RequireQualifiedAccess>]
type ReferenceZone =
    | Stack = 0
    | Mitt = 1
    | Oche = 2
    | Booth = 3
    | Attached = 4
    | EmptiesTray = 5
    | Local = 6
    | BarChit = 7

type ReferenceDistributionInput = { Card: string; Counters: int }

type ReferenceAttachmentInput = { Vim: string; Bloke: string }

[<RequireQualifiedAccess>]
type ReferenceChoiceValue =
    | Optional of bool
    | Amount of int
    | Cards of string array
    | MechanicalType of ReferenceMechanicalType
    | Attack of string
    | Distribution of ReferenceDistributionInput array
    | Attachments of ReferenceAttachmentInput array

type ReferenceChoiceInput =
    { RequirementId: string
      Value: ReferenceChoiceValue
      WhenAvailable: bool }

type ReferenceActionInput =
    { Kind: ReferenceInputActionKind
      Actor: string
      SourceCard: string
      TargetCard: string
      EffectId: string
      Choices: ReferenceChoiceInput array }

type ReferenceCardSetupInput =
    { CardId: string
      Owner: string
      MechanicalId: string
      Zone: ReferenceZone }

type ReferenceZoneCountInput =
    { Owner: string
      Zone: ReferenceZone
      Count: int }

type ReferencePlayerSetupInput =
    { Player: string
      BarChitsRemaining: int }

type ReferenceInitialStateInput =
    { Route: ReferenceSetupRoute
      Parameters: string array
      Cards: ReferenceCardSetupInput array
      ZoneCounts: ReferenceZoneCountInput array
      Players: ReferencePlayerSetupInput array }

type ReferenceReviewedProgram =
    { OwnerId: string
      Kind: ReferenceProgramKind
      MechanicalId: string }

    member this.Key =
        let segment =
            match this.Kind with
            | ReferenceProgramKind.Attack -> "attack"
            | ReferenceProgramKind.PartyTrick -> "party-trick"
            | ReferenceProgramKind.HouseRule -> "house-rule"

        $"{this.OwnerId}/{segment}/{this.MechanicalId}"

type ReferenceObligationInput =
    { Id: string
      ProgramKey: string
      ReviewedProgram: ReferenceReviewedProgram
      InitialState: ReferenceInitialStateInput
      Actions: ReferenceActionInput array
      RandomSeed: uint64 }

type ReferenceObligationInventory =
    { Obligations: ReferenceObligationInput array
      RouteIdentities: Set<ReferenceSetupRoute>
      AcceptedProgramRoutes: Set<ReferenceSetupRoute> }

[<RequireQualifiedAccess>]
module private ReferenceSetupRoutes =

    let values =
        Set
            [ "activated-decline"
              "activated-trigger"
              "activated-unavailable"
              "bar-chit-trigger"
              "bar-chit-trigger-blank"
              "bar-chit-trigger-decline"
              "bar-chit-trigger-full-booth"
              "booth-all-own-swap"
              "booth-search"
              "chuck-vim-booth"
              "coin-branch"
              "coin-effects"
              "coin-search"
              "coin-swap"
              "conditional-adjust"
              "conditional-demote"
              "conditional-extra-bar"
              "conditional-rough"
              "continuous-refresh"
              "damage-attach-vim"
              "damage-booth-spread"
              "damage-chuck-cards"
              "damage-chuck-vim"
              "damage-effect"
              "damage-effects"
              "damage-heal"
              "damage-move-vim"
              "damage-rough"
              "damage-rough-effects"
              "damage-self"
              "damage-swap"
              "day-two-forced-blank"
              "dynamic-adjust"
              "full-booth-search"
              "gone-smoke"
              "hand-kit-scale"
              "heal-clear"
              "ignore-modifier"
              "kit-condition"
              "knockout-trigger"
              "knockout-trigger-decline"
              "local-decline"
              "local-trigger"
              "multi-toss-damage"
              "optional-bar-kit"
              "optional-decline"
              "optional-invalid-duplicate"
              "optional-max"
              "optional-zero"
              "paul-chuckle-trigger-fire"
              "paul-chuckle-trigger-nonfire"
              "play-kit"
              "promotion-decline"
              "promotion-trigger"
              "reactive-trigger"
              "repeat-damage"
              "repeat-draw"
              "search-all"
              "shirt-off-badge"
              "shirt-off-blank"
              "still-coming-up-not-promoted"
              "still-coming-up-promoted"
              "top-qualifying"
              "trigger-nonfire"
              "trivial-booth-damage"
              "trivial-chuck"
              "trivial-copy"
              "trivial-damage"
              "trivial-distribution"
              "trivial-draw"
              "trivial-rough"
              "trivial-soft-spot"
              "trivial-swap" ]

    let parse value =
        if values.Contains value then
            ReferenceSetupRoute value
        else
            raise (JsonException($"Unknown reviewed setup route identity {value}."))

[<RequireQualifiedAccess>]
module ReferenceObligations =

    [<Literal>]
    let ObligationCount = 429

    [<Literal>]
    let RouteIdentityCount = 73

    let private fail (message: string) = raise (JsonException message)

    let private expectObject (context: string) (expected: Set<string>) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            fail $"The structured obligation {context} is not an object."

        let actual = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let missing = Set.difference expected actual
        let unknown = Set.difference actual expected

        if not missing.IsEmpty || not unknown.IsEmpty then
            let missingText = missing |> String.concat ","
            let unknownText = unknown |> String.concat ","

            fail
                $"The structured obligation {context} schema drifted (missing={missingText}; unknown={unknownText})."

    let private property (context: string) (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value -> value
        | false, _ -> fail $"The structured obligation {context} has no {name}."

    let private text (context: string) (name: string) (element: JsonElement) =
        let value = property context name element

        if value.ValueKind <> JsonValueKind.String then
            fail $"The structured obligation {context}.{name} is not text."

        match value.GetString() |> Option.ofObj with
        | None -> fail $"The structured obligation {context}.{name} is null."
        | Some value -> value

    let private nonblank (context: string) (name: string) (element: JsonElement) =
        let value = text context name element

        if String.IsNullOrWhiteSpace value then
            fail $"The structured obligation {context}.{name} is blank."

        value

    let private integer (context: string) (name: string) (element: JsonElement) =
        let value = property context name element

        if value.ValueKind <> JsonValueKind.Number then
            fail $"The structured obligation {context}.{name} is not an integer."

        value.GetInt32()

    let private uint64 (context: string) (name: string) (element: JsonElement) =
        let value = property context name element

        if value.ValueKind <> JsonValueKind.Number then
            fail $"The structured obligation {context}.{name} is not an unsigned integer."

        value.GetUInt64()

    let private boolean (context: string) (name: string) (element: JsonElement) =
        let value = property context name element

        match value.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> fail $"The structured obligation {context}.{name} is not a boolean."

    let private array (context: string) (name: string) (element: JsonElement) =
        let value = property context name element

        if value.ValueKind <> JsonValueKind.Array then
            fail $"The structured obligation {context}.{name} is not an array."

        value.EnumerateArray() |> Seq.toArray

    let private strings (context: string) (name: string) (element: JsonElement) =
        array context name element
        |> Array.mapi (fun index value ->
            if value.ValueKind <> JsonValueKind.String then
                fail $"The structured obligation {context}.{name}[{index}] is not text."

            match value.GetString() |> Option.ofObj with
            | None
            | Some "" -> fail $"The structured obligation {context}.{name}[{index}] is blank."
            | Some text -> text)

    let private enumValue<'value
        when 'value: struct and 'value :> ValueType and 'value: (new: unit -> 'value)>
        (context: string)
        (value: string)
        =
        let mutable parsed = Unchecked.defaultof<'value>

        if
            Enum.TryParse<'value>(value, false, &parsed)
            && String.Equals(string parsed, value, StringComparison.Ordinal)
        then
            parsed
        else
            fail $"The structured obligation {context} has unknown value {value}."

    let private programKind (context: string) (value: string) =
        match value with
        | "attack" -> ReferenceProgramKind.Attack
        | "party-trick" -> ReferenceProgramKind.PartyTrick
        | "house-rule" -> ReferenceProgramKind.HouseRule
        | _ -> fail $"The structured obligation {context} has unknown program kind {value}."

    let private choice (context: string) (element: JsonElement) =
        let fields = Set [ "kind"; "requirementId"; "values"; "whenAvailable" ]
        expectObject context fields element
        let kind = nonblank context "kind" element
        let values = strings context "values" element

        let one () =
            if values.Length <> 1 then
                fail $"The structured obligation {context} requires exactly one choice value."

            values[0]

        let value =
            match kind with
            | "optional" -> ReferenceChoiceValue.Optional(Boolean.Parse(one ()))
            | "amount" -> ReferenceChoiceValue.Amount(Int32.Parse(one ()))
            | "cards" -> ReferenceChoiceValue.Cards values
            | "mechanical-type" ->
                ReferenceChoiceValue.MechanicalType(
                    enumValue<ReferenceMechanicalType> context (one ())
                )
            | "attack" -> ReferenceChoiceValue.Attack(one ())
            | "distribution" ->
                values
                |> Array.map (fun allocation ->
                    let parts = allocation.Split(':')

                    if parts.Length <> 2 || String.IsNullOrWhiteSpace parts[0] then
                        fail $"The structured obligation {context} has an invalid distribution."

                    { Card = parts[0]
                      Counters = Int32.Parse(parts[1]) })
                |> ReferenceChoiceValue.Distribution
            | "attachments" ->
                values
                |> Array.map (fun placement ->
                    let parts = placement.Split("->", StringSplitOptions.None)

                    if
                        parts.Length <> 2
                        || String.IsNullOrWhiteSpace parts[0]
                        || String.IsNullOrWhiteSpace parts[1]
                    then
                        fail $"The structured obligation {context} has an invalid attachment."

                    { Vim = parts[0]; Bloke = parts[1] })
                |> ReferenceChoiceValue.Attachments
            | _ -> fail $"The structured obligation {context} has unknown choice kind {kind}."

        { RequirementId = nonblank context "requirementId" element
          Value = value
          WhenAvailable = boolean context "whenAvailable" element }

    let private action (context: string) (element: JsonElement) =
        let fields =
            Set [ "kind"; "actor"; "sourceCard"; "targetCard"; "effectId"; "choices" ]

        expectObject context fields element

        { Kind =
            nonblank context "kind" element
            |> enumValue<ReferenceInputActionKind> $"{context}.kind"
          Actor = nonblank context "actor" element
          SourceCard = text context "sourceCard" element
          TargetCard = text context "targetCard" element
          EffectId = text context "effectId" element
          Choices =
            array context "choices" element
            |> Array.mapi (fun index value -> choice $"{context}.choices[{index}]" value) }

    let private initialState (context: string) (element: JsonElement) =
        let fields = Set [ "route"; "parameters"; "cards"; "zoneCounts"; "players" ]
        expectObject context fields element

        { Route = nonblank context "route" element |> ReferenceSetupRoutes.parse
          Parameters = strings context "parameters" element
          Cards =
            array context "cards" element
            |> Array.mapi (fun index value ->
                let current = $"{context}.cards[{index}]"
                let cardFields = Set [ "cardId"; "owner"; "mechanicalId"; "zone" ]
                expectObject current cardFields value

                { CardId = nonblank current "cardId" value
                  Owner = nonblank current "owner" value
                  MechanicalId = nonblank current "mechanicalId" value
                  Zone =
                    nonblank current "zone" value |> enumValue<ReferenceZone> $"{current}.zone" })
          ZoneCounts =
            array context "zoneCounts" element
            |> Array.mapi (fun index value ->
                let current = $"{context}.zoneCounts[{index}]"
                let countFields = Set [ "owner"; "zone"; "count" ]
                expectObject current countFields value
                let count = integer current "count" value

                if count < 0 then
                    fail $"The structured obligation {current}.count is negative."

                { Owner = nonblank current "owner" value
                  Zone =
                    nonblank current "zone" value |> enumValue<ReferenceZone> $"{current}.zone"
                  Count = count })
          Players =
            array context "players" element
            |> Array.mapi (fun index value ->
                let current = $"{context}.players[{index}]"
                let playerFields = Set [ "player"; "barChitsRemaining" ]
                expectObject current playerFields value
                let count = integer current "barChitsRemaining" value

                if count < 0 then
                    fail $"The structured obligation {current}.barChitsRemaining is negative."

                { Player = nonblank current "player" value
                  BarChitsRemaining = count }) }

    let private obligation index (element: JsonElement) =
        let context = $"obligations[{index}]"

        let inputFields =
            Set
                [ "id"
                  "programKey"
                  "reviewedProgram"
                  "initialState"
                  "actions"
                  "randomInput" ]

        let ignoredEvidenceFields =
            Set
                [ "covers"
                  "expectedChoices"
                  "legalActionResult"
                  "canonicalState"
                  "orderedEvents" ]

        expectObject context (Set.union inputFields ignoredEvidenceFields) element
        let reviewedElement = property context "reviewedProgram" element
        let reviewedFields = Set [ "ownerId"; "kind"; "mechanicalId" ]
        expectObject $"{context}.reviewedProgram" reviewedFields reviewedElement

        let reviewed =
            { OwnerId = nonblank $"{context}.reviewedProgram" "ownerId" reviewedElement
              Kind =
                nonblank $"{context}.reviewedProgram" "kind" reviewedElement
                |> programKind $"{context}.reviewedProgram.kind"
              MechanicalId = nonblank $"{context}.reviewedProgram" "mechanicalId" reviewedElement }

        let random = property context "randomInput" element
        let randomFields = Set [ "seed" ]
        expectObject $"{context}.randomInput" randomFields random

        { Id = nonblank context "id" element
          ProgramKey = nonblank context "programKey" element
          ReviewedProgram = reviewed
          InitialState =
            property context "initialState" element
            |> initialState $"{context}.initialState"
          Actions =
            array context "actions" element
            |> Array.mapi (fun actionIndex value ->
                action $"{context}.actions[{actionIndex}]" value)
          RandomSeed = uint64 $"{context}.randomInput" "seed" random }

    let private duplicates values =
        values
        |> Seq.countBy id
        |> Seq.choose (fun (value, count) -> if count = 1 then None else Some value)
        |> Seq.toArray

    let load (authority: ReferenceAuthority) path =
        use document = JsonDocument.Parse(File.ReadAllText path)
        let root = document.RootElement

        let rootFields =
            Set
                [ "schemaVersion"
                  "obligations"
                  "structuralRationales"
                  "mutations"
                  "nonMutableOperands" ]

        expectObject "root" rootFields root

        if integer "root" "schemaVersion" root <> 2 then
            fail "The structured obligation schema is unsupported."

        let obligations = array "root" "obligations" root |> Array.mapi obligation

        if obligations.Length <> ObligationCount then
            fail
                $"Expected {ObligationCount} structured obligations but found {obligations.Length}."

        if duplicates (obligations |> Seq.map _.Id) |> Array.isEmpty |> not then
            fail "The structured obligations have duplicate identities."

        let programs =
            authority.Programs |> Seq.map (fun program -> program.Key, program) |> Map.ofSeq

        for item in obligations do
            if item.ProgramKey <> item.ReviewedProgram.Key then
                fail
                    $"Structured obligation {item.Id} disagrees with its reviewed program identity."

            match programs.TryFind item.ProgramKey with
            | None ->
                fail $"Structured obligation {item.Id} names unknown program {item.ProgramKey}."
            | Some program when
                program.OwnerId <> item.ReviewedProgram.OwnerId
                || program.Kind <> item.ReviewedProgram.Kind
                || program.MechanicalId <> item.ReviewedProgram.MechanicalId
                ->
                fail
                    $"Structured obligation {item.Id} reviewed identity drifted from the raw authority."
            | Some _ -> ()

            if item.Actions.Length = 0 then
                fail $"Structured obligation {item.Id} has no action input."

        let routes = obligations |> Seq.map _.InitialState.Route |> Set.ofSeq

        if
            routes.Count <> RouteIdentityCount
            || routes <> (ReferenceSetupRoutes.values |> Set.map ReferenceSetupRoute)
        then
            fail $"Expected exactly {RouteIdentityCount} reviewed setup route identities."

        { Obligations = obligations
          RouteIdentities = routes
          AcceptedProgramRoutes = Set.empty }
