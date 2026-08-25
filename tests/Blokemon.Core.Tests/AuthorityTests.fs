namespace Blokemon.Core.Tests

open System
open System.IO
open System.Linq
open System.Text.Json
open System.Text.Json.Nodes
open Blokemon.Core.PublicContent
open Blokemon.Core.SetDesign
open FsUnit
open TUnit.Core

module private Authorities =

    let read (name: string) =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Authorities", name))

    let mechanics = lazy (BlokemonSetJson.RuntimeManifest(read "mechanics.json"))

    let publicContent =
        lazy (BlokemonPublicContentJson.Manifest(read "public-content.json"))

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

        let result =
            BlokemonSetValidator.ValidateRuntime(
                { Authorities.mechanics.Value with
                    ApprovedMechanicalDisplayMap = mappings }
            )

        result.IsValid |> should be False

        result.Issues
        |> Array.exists (fun issue -> issue.Code = "runtime.mechanical-display-map")
        |> should be True

    [<Test>]
    member _.``runtime validation should reject an incomplete mechanical display mapping``() =
        let result =
            BlokemonSetValidator.ValidateRuntime(
                { Authorities.mechanics.Value with
                    ApprovedMechanicalDisplayMap =
                        Authorities.mechanics.Value.ApprovedMechanicalDisplayMap |> Array.take 9 }
            )

        result.IsValid |> should be False

        result.Issues
        |> Array.exists (fun issue -> issue.Code = "runtime.mechanical-display-map")
        |> should be True

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

    [<Test>]
    member _.``current authorities should pass owned validation``() =
        BlokemonSetValidator.ValidateRuntime(Authorities.mechanics.Value).IsValid
        |> should be True

        let publicValidation =
            BlokemonPublicContentValidator.ValidateDocument
                Authorities.publicContent.Value
                Authorities.mechanics.Value

        publicValidation.IsValid |> should be True

    [<Test>]
    member _.``runtime validation should diagnose omitted resolution steps``() =
        let authority = Authorities.mechanics.Value

        let changed =
            { authority with
                BaseRules =
                    { authority.BaseRules with
                        AttackOrder = authority.BaseRules.AttackOrder |> Array.skip 1
                        DamageOrder = authority.BaseRules.DamageOrder |> Array.skip 1 } }

        let codes =
            BlokemonSetValidator.ValidateRuntime(changed).Issues
            |> Array.map (fun issue -> issue.Code)

        codes |> should contain "runtime.attack-order-omission"
        codes |> should contain "runtime.damage-order-omission"

    [<Test>]
    member _.``runtime validation should diagnose duplicated resolution steps``() =
        let authority = Authorities.mechanics.Value
        let attackOrder = Array.copy authority.BaseRules.AttackOrder
        let damageOrder = Array.copy authority.BaseRules.DamageOrder
        attackOrder[1] <- attackOrder[0]
        damageOrder[1] <- damageOrder[0]

        let changed =
            { authority with
                BaseRules =
                    { authority.BaseRules with
                        AttackOrder = attackOrder
                        DamageOrder = damageOrder } }

        let codes =
            BlokemonSetValidator.ValidateRuntime(changed).Issues
            |> Array.map (fun issue -> issue.Code)

        codes |> should contain "runtime.attack-order-duplicate"
        codes |> should contain "runtime.damage-order-duplicate"

    [<Test>]
    member _.``runtime validation should diagnose unknown resolution steps``() =
        let authority = Authorities.mechanics.Value
        let attackOrder = Array.copy authority.BaseRules.AttackOrder
        let damageOrder = Array.copy authority.BaseRules.DamageOrder
        attackOrder[0] <- enum<BlokemonAttackResolutionStep> 99
        damageOrder[0] <- enum<BlokemonDamageResolutionStep> 99

        let changed =
            { authority with
                BaseRules =
                    { authority.BaseRules with
                        AttackOrder = attackOrder
                        DamageOrder = damageOrder } }

        let codes =
            BlokemonSetValidator.ValidateRuntime(changed).Issues
            |> Array.map (fun issue -> issue.Code)

        codes |> should contain "runtime.attack-order-unknown"
        codes |> should contain "runtime.damage-order-unknown"

    [<Test>]
    member _.``runtime validation should reject behaviorally significant adjacent order changes``
        ()
        =
        let authority = Authorities.mechanics.Value
        let attackOrder = Array.copy authority.BaseRules.AttackOrder
        let damageOrder = Array.copy authority.BaseRules.DamageOrder
        let attackSecond = attackOrder[1]
        attackOrder[1] <- attackOrder[2]
        attackOrder[2] <- attackSecond
        let damageThird = damageOrder[2]
        damageOrder[2] <- damageOrder[3]
        damageOrder[3] <- damageThird

        let changed =
            { authority with
                BaseRules =
                    { authority.BaseRules with
                        AttackOrder = attackOrder
                        DamageOrder = damageOrder } }

        let codes =
            BlokemonSetValidator.ValidateRuntime(changed).Issues
            |> Array.map (fun issue -> issue.Code)

        codes |> should contain "runtime.attack-order-unsupported-order"
        codes |> should contain "runtime.damage-order-unsupported-order"

    [<Test>]
    member _.``karaoke queen should publish the exact Jungle Wigglytuff authority``() =
        let instruction opcode amount valueSource targets roughStates =
            { Opcode = opcode
              Amount = amount
              ValueSource = valueSource
              Targets = targets
              Selection = BlokemonSelection.All
              TargetCount = 1
              Predicates = Array.empty
              MechanicalTypes = Array.empty
              RoughStates = roughStates
              RelatedIds = Array.empty
              Then = Array.empty
              Otherwise = Array.empty
              Sources = null
              Destination = BlokemonEffectDestination.Unspecified
              CardFilter = null
              SourceTopCount = 0 }

        let authority = Authorities.mechanics.Value
        let karaokeKev = authority.Collectibles.Single(fun card -> card.Id = "BLK-039")
        let karaokeQueen = authority.Collectibles.Single(fun card -> card.Id = "BLK-040")

        karaokeQueen.Rank |> should equal BlokemonRank.Seasoned
        karaokeQueen.StayingPower |> should equal 80
        karaokeQueen.PromotesFromId |> should equal "BLK-039"
        karaokeKev.PromotesToIds |> should contain "BLK-040"
        karaokeQueen.PartyTricks |> should be Empty
        karaokeQueen.HouseRules |> should be Empty

        karaokeQueen.Attacks
        |> should
            equal
            [| { MechanicalId = "BLK-040-B01"
                 PresentationStatus = BlokemonPresentationStatus.Accepted
                 VimCost = [| BlokemonMechanicalType.Colorless |]
                 PrintedDamage = 0
                 VariablePrintedDamage = false
                 CanBeUsedFromBench = false
                 Program =
                   [| instruction
                          BlokemonOpcode.ApplyRoughState
                          1
                          BlokemonValueSource.Fixed
                          [| BlokemonTarget.OtherOche |]
                          [| BlokemonRoughState.NoddedOff |] |] }
               { MechanicalId = "BLK-040-B02"
                 PresentationStatus = BlokemonPresentationStatus.Accepted
                 VimCost =
                   [| BlokemonMechanicalType.Colorless
                      BlokemonMechanicalType.Colorless
                      BlokemonMechanicalType.Colorless |]
                 PrintedDamage = 10
                 VariablePrintedDamage = true
                 CanBeUsedFromBench = false
                 Program =
                   [| instruction
                          BlokemonOpcode.DealPrintedDamage
                          10
                          BlokemonValueSource.PrintedDamage
                          [| BlokemonTarget.OtherOche |]
                          Array.empty
                      instruction
                          BlokemonOpcode.AdjustDamage
                          10
                          BlokemonValueSource.OwnBoothCount
                          [| BlokemonTarget.OtherOche |]
                          Array.empty |] } |]

        karaokeQueen.SoftSpots
        |> should
            equal
            [| { MechanicalType = BlokemonMechanicalType.Fighting
                 Modifier = "×2" } |]

        karaokeQueen.StubbornStreaks
        |> should
            equal
            [| { MechanicalType = BlokemonMechanicalType.Psychic
                 Modifier = "-30" } |]

        (karaokeQueen.TaxiFare, karaokeQueen.BarChitsWhenSentHome)
        |> should equal (2, 1)

        authority.BaseRules.BigHitters.BlokeIds |> should not' (contain "BLK-040")

        let publicCard =
            Authorities.publicContent.Value.Collectibles.Single(fun card -> card.Id = "BLK-040")

        publicCard.Abilities |> should be Empty
        publicCard.Rules |> should be Empty

        publicCard.Attacks
        |> should
            equal
            [| { MechanicalId = "BLK-040-B01"
                 Name = "Lullaby"
                 EffectText = "Your opponent's Active Blokemon is now Asleep." }
               { MechanicalId = "BLK-040-B02"
                 Name = "Do the Wave"
                 EffectText = "This Attack deals 10 more damage for each of your Benched Blokemon." } |]

    [<Test>]
    member _.``eleven card sampling should be deterministic and preserve pack composition``() =
        let manifest = Authorities.mechanics.Value
        let first = BlokemonSeededRandom(0xB10CE188UL)
        let replay = BlokemonSeededRandom(0xB10CE188UL)

        let cards = manifest.Collectibles |> Array.map (fun card -> card.Id, card) |> dict

        for _ in 1..256 do
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

    [<Test>]
    member _.``runtime validation should reject a changed roadie affinity``() =
        let manifest = Authorities.mechanics.Value
        let roadie = manifest.Collectibles.Single(fun card -> card.Id = "BLK-035")

        let changed =
            { manifest with
                Collectibles =
                    [| yield!
                           manifest.Collectibles |> Array.filter (fun card -> card.Id <> "BLK-035")
                       yield { roadie with SoftSpots = Array.empty } |] }

        let result = BlokemonSetValidator.ValidateRuntime(changed)

        result.IsValid |> should be False

        result.Issues
        |> Array.exists (fun issue -> issue.Code = "runtime.roadie-soft-spots")
        |> should be True

    [<Test>]
    member _.``runtime authority should reject unknown fields``() =
        let document =
            match JsonNode.Parse(Authorities.read "mechanics.json") with
            | null -> failwith "The mechanical authority did not parse as JSON."
            | node -> node.AsObject()

        document["unsupported"] <- JsonValue.Create(true)

        (fun () -> BlokemonSetJson.RuntimeManifest(document.ToJsonString()) |> ignore)
        |> should throw typeof<JsonException>
