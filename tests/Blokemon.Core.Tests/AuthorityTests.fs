namespace Blokemon.Core.Tests

open System
open System.IO
open System.Linq
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private Authorities =

    let read () =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json"))

    let mechanics = lazy (BlokemonSetJson.RuntimeManifest(read ()))

    let document () =
        match JsonNode.Parse(read ()) with
        | null -> failwith "The mechanical authority did not parse as JSON."
        | parsed -> parsed.AsObject()

    let deserialize (document: JsonNode) =
        BlokemonSetJson.RuntimeManifest(document.ToJsonString())

    let withBaseRuleValue (pointer: string) (jsonValue: string) =
        let document = document ()
        let segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)

        let mutable current: JsonNode =
            match document["baseRules"] with
            | null -> failwith "The authority omits baseRules."
            | baseRules -> baseRules

        for segment in segments |> Array.take (segments.Length - 1) do
            let next =
                match current with
                | :? JsonArray as array -> array[int segment]
                | _ -> current[segment]

            current <-
                match next with
                | null -> failwith $"The BaseRules path {pointer} crosses a null value."
                | node -> node

        let value = JsonNode.Parse jsonValue
        let final = segments[segments.Length - 1]

        match current with
        | :? JsonArray as array -> array[int final] <- value
        | :? JsonObject as objectNode -> objectNode[final] <- value
        | _ -> failwith $"Cannot replace the BaseRules leaf {pointer}."

        deserialize document

    let objectProperty (name: string) (document: JsonObject) =
        match document[name] with
        | null -> failwith $"The authority omits {name}."
        | value -> value.AsObject()

    let arrayProperty (name: string) (document: JsonObject) =
        match document[name] with
        | null -> failwith $"The authority omits {name}."
        | value -> value.AsArray()

    let firstObject (values: JsonArray) =
        match values[0] with
        | null -> failwith "The authority array begins with null."
        | value -> value.AsObject()

    let validationCodes manifest =
        BlokemonSetValidator.ValidateRuntime(manifest).Issues |> Seq.map _.Code

type AuthorityTests() =

    [<Test>]
    [<Arguments(BlokemonMechanicalType.Grass, BlokemonApprovedMechanicalLabel.Blazed)>]
    [<Arguments(BlokemonMechanicalType.Fire, BlokemonApprovedMechanicalLabel.Curry)>]
    [<Arguments(BlokemonMechanicalType.Water, BlokemonApprovedMechanicalLabel.Sober)>]
    [<Arguments(BlokemonMechanicalType.Lightning, BlokemonApprovedMechanicalLabel.Beer)>]
    [<Arguments(BlokemonMechanicalType.Psychic, BlokemonApprovedMechanicalLabel.Geeked)>]
    [<Arguments(BlokemonMechanicalType.Fighting, BlokemonApprovedMechanicalLabel.Lairy)>]
    [<Arguments(BlokemonMechanicalType.Darkness, BlokemonApprovedMechanicalLabel.Dodgy)>]
    [<Arguments(BlokemonMechanicalType.Colorless, BlokemonApprovedMechanicalLabel.Local)>]
    [<Arguments(BlokemonMechanicalType.Dragon, BlokemonApprovedMechanicalLabel.Legend)>]
    [<Arguments(BlokemonMechanicalType.Metal, BlokemonApprovedMechanicalLabel.Roadie)>]
    member _.``a mechanical type should resolve to its approved player-facing label``
        (mechanicalType: BlokemonMechanicalType, expected: BlokemonApprovedMechanicalLabel)
        =
        BlokemonMechanicalDisplay.ApprovedLabel Authorities.mechanics.Value mechanicalType
        |> should equal expected

    [<Test>]
    member _.``runtime validation should reject a duplicated mechanical display mapping``() =
        let mappings = Array.copy Authorities.mechanics.Value.ApprovedMechanicalDisplayMap
        mappings[mappings.Length - 1] <- mappings[0]

        { Authorities.mechanics.Value with
            ApprovedMechanicalDisplayMap = mappings }
        |> Authorities.validationCodes
        |> should contain "runtime.mechanical-display-map"

    [<Test>]
    member _.``runtime validation should reject an incomplete mechanical display mapping``() =
        { Authorities.mechanics.Value with
            ApprovedMechanicalDisplayMap =
                Authorities.mechanics.Value.ApprovedMechanicalDisplayMap |> Array.skip 1 }
        |> Authorities.validationCodes
        |> should contain "runtime.mechanical-display-map"

    [<Test>]
    member _.``an unmapped mechanical type should fail with the requested type``() =
        let incomplete =
            { Authorities.mechanics.Value with
                ApprovedMechanicalDisplayMap =
                    Authorities.mechanics.Value.ApprovedMechanicalDisplayMap
                    |> Array.filter (fun mapping ->
                        mapping.MechanicalType <> BlokemonMechanicalType.Grass) }

        let failure =
            try
                BlokemonMechanicalDisplay.ApprovedLabel incomplete BlokemonMechanicalType.Grass
                |> ignore

                failwith "The incomplete display map was accepted."
            with :? InvalidDataException as invalid ->
                invalid

        failure.Message |> should contain "Grass"

    // Advanced Rulebook Version 1, pp. 2, 6-7, 20-24: Deck, setup, win, and
    // Sudden Death rules are one immutable load-time contract, not tunable house rules.
    [<Test>]
    member _.``the pinned 1999 shared rule authority should pass strict validation``() =
        let authority = Authorities.mechanics.Value

        BlokemonSetValidator.ValidateRuntime(authority).IsValid |> should be True

        authority.ManifestVersion
        |> should equal "base-jungle-fossil-1999-shared-rules-candidate.1"

        authority.BaseRules.RulesVersion
        |> should equal "wotc-advanced-rulebook-v1-1999-candidate.1"

        authority.Kits.Length |> should equal 32

    // Advanced Rulebook Version 1, pp. 14-15, 20, 23: Basic Energy alone is exempt
    // from the four-card limit; Double Colorless supplies two Colorless Energy units.
    [<Test>]
    member _.``the Energy pool should contain six Basic Energy and exact Double Colorless``() =
        let energy = Authorities.mechanics.Value.BasicVim
        let basics = energy |> Array.filter _.IsBasic

        let doubleColorless =
            energy |> Array.filter (fun card -> not card.IsBasic) |> Array.exactlyOne

        basics.Length |> should equal 6

        basics
        |> Array.forall (fun card -> card.Provides = [| card.MechanicalType |])
        |> should be True

        doubleColorless.Id |> should equal "VIM-DODGY"

        doubleColorless.Provides
        |> should equal [| BlokemonMechanicalType.Colorless; BlokemonMechanicalType.Colorless |]

        doubleColorless.StackCopyLimit |> should equal 4

    // Advanced Rulebook Version 1, pp. 2-24: each shared rule family is fixed by the
    // pinned oracle, so changing one leaf must make the runtime authority invalid.
    [<Test>]
    [<Arguments("/rulesVersion", "\"unsupported\"", "runtime.rules-version")>]
    [<Arguments("/stack/cardCount", "59", "runtime.stack-rules")>]
    [<Arguments("/opening/mittSize", "6", "runtime.opening-rules")>]
    [<Arguments("/round/requiredOpeningDraw", "false", "runtime.turn-rules")>]
    [<Arguments("/promotion/notFirstRoundInPlay", "false", "runtime.evolution-rules")>]
    [<Arguments("/vim/normalAttachmentPerRound", "0", "runtime.energy-rules")>]
    [<Arguments("/trainer/unlimitedPerTurn", "false", "runtime.trainer-rules")>]
    [<Arguments("/pokemonPower/disabledBy", "[\"DodgyPint\"]", "runtime.pokemon-power-rules")>]
    [<Arguments("/taxi/perRound", "0", "runtime.retreat-rules")>]
    [<Arguments("/damage/placedCountersUseDamageModifiers", "true", "runtime.damage-rules")>]
    [<Arguments("/selectionRules/optional", "\"Required\"", "runtime.selection-rules")>]
    [<Arguments("/effectDrawFromShortStack", "99", "runtime.draw-rules")>]
    [<Arguments("/checkup/roughStateOrder/0", "\"NoddedOff\"", "runtime.condition-checkup")>]
    [<Arguments("/roughStates/0/checkupDamageCounters", "2", "runtime.condition-poisoned")>]
    [<Arguments("/roughStates/1/preventsAttack", "false", "runtime.condition-asleep")>]
    [<Arguments("/roughStates/2/recoversAfterOwnersNextRound",
                "false",
                "runtime.condition-paralyzed")>]
    [<Arguments("/roughStates/3/blankSideCancelsAndSelfDamageCounters",
                "1",
                "runtime.condition-confused")>]
    [<Arguments("/roughStateCoexistence/markerGroup/0",
                "\"Legless\"",
                "runtime.condition-coexistence")>]
    [<Arguments("/sendHome/prizeCardsPerKnockout", "2", "runtime.knockout-rules")>]
    [<Arguments("/win/suddenDeathStartsFreshGame", "false", "runtime.win-rules")>]
    [<Arguments("/opcodeInventory/0", "\"AdjustDamage\"", "runtime.opcode-inventory")>]
    member _.``a mutated shared rule should be rejected with its rule-family diagnostic``
        (pointer: string, jsonValue: string, expectedCode: string)
        =
        Authorities.withBaseRuleValue pointer jsonValue
        |> Authorities.validationCodes
        |> should contain expectedCode

    [<Test>]
    member _.``an omitted or reordered resolution step should reject the authority``() =
        let authority = Authorities.mechanics.Value
        let damageOrder = Array.copy authority.BaseRules.DamageOrder
        let held = damageOrder[3]
        damageOrder[3] <- damageOrder[4]
        damageOrder[4] <- held

        let changed =
            { authority with
                BaseRules =
                    { authority.BaseRules with
                        AttackOrder = authority.BaseRules.AttackOrder |> Array.skip 1
                        DamageOrder = damageOrder } }

        let codes = Authorities.validationCodes changed
        codes |> should contain "runtime.attack-order-omission"
        codes |> should contain "runtime.damage-order-unsupported-order"

    // The strict codec is the hybrid-manifest boundary. These were normative SV151-era
    // surfaces; accepting any of them would silently combine incompatible rule eras.
    [<Test>]
    member _.``a modern Trainer category should be rejected as an unmapped field``() =
        let document = Authorities.document ()
        let trainer = Authorities.arrayProperty "kits" document |> Authorities.firstObject
        trainer["kind"] <- JsonValue.Create("Mate")

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    [<Arguments("barChitsWhenSentHome")>]
    member _.``a modern per-Pokemon Prize field should be rejected as unmapped``(field: string) =
        let document = Authorities.document ()

        let collectible =
            Authorities.arrayProperty "collectibles" document |> Authorities.firstObject

        collectible[field] <- JsonValue.Create(2)

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    [<Arguments("kit")>]
    [<Arguments("fossilKits")>]
    [<Arguments("bigHitters")>]
    member _.``a modern base-rule block should be rejected instead of forming a hybrid manifest``
        (field: string)
        =
        let document = Authorities.document ()
        (Authorities.objectProperty "baseRules" document)[field] <- JsonObject()

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    [<Arguments("opening", "barChitCount")>]
    [<Arguments("opening", "openingParticipantMayPlayMate")>]
    [<Arguments("sendHome", "normalBarChits")>]
    [<Arguments("sendHome", "bigHitterBarChits")>]
    [<Arguments("win", "suddenDeathBarChits")>]
    [<Arguments("win", "oneMethodEach")>]
    [<Arguments("win", "moreMethodsWins")>]
    member _.``a replaced modern rule leaf should be rejected as unmapped``
        (block: string, field: string)
        =
        let document = Authorities.document ()
        let rules = Authorities.objectProperty "baseRules" document
        (Authorities.objectProperty block rules)[field] <- JsonValue.Create(1)

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    member _.``runtime authority should reject unknown root fields``() =
        let document = Authorities.document ()
        document["unsupported"] <- JsonValue.Create(true)

        (fun () -> Authorities.deserialize document |> ignore)
        |> should throw typeof<JsonException>

    [<Test>]
    member _.``eleven card sampling should remain deterministic and preserve pack composition``() =
        let manifest = Authorities.mechanics.Value
        let first = BlokemonSeededRandom(0xB10CE188UL)
        let replay = BlokemonSeededRandom(0xB10CE188UL)
        let cards = manifest.Collectibles |> Array.map (fun card -> card.Id, card) |> dict

        for _ in 1..32 do
            let pack = BlokemonPackSampler.SampleEleven manifest first
            let repeated = BlokemonPackSampler.SampleEleven manifest replay
            pack.SequenceEqual(repeated) |> should be True
            pack.Distinct(StringComparer.Ordinal).Count() |> should equal 11

            let bucketCount bucket =
                pack |> Seq.filter (fun id -> cards[id].ProductBucket = bucket) |> Seq.length

            bucketCount BlokemonProductBucket.Rare |> should equal 1
            bucketCount BlokemonProductBucket.Uncommon |> should equal 3
            bucketCount BlokemonProductBucket.Common |> should equal 7

        first.ConsumptionIndex |> should equal replay.ConsumptionIndex
