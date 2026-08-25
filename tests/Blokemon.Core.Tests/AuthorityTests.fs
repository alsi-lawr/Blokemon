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

    let withBaseRuleValue (pointer: string) (jsonValue: string) =
        let document =
            match JsonNode.Parse(read "mechanics.json") with
            | null -> failwith "The mechanical authority did not parse as JSON."
            | parsed -> parsed

        let segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)

        let mutable current =
            match document["baseRules"] with
            | null -> failwith "The mechanical authority omits baseRules."
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

        BlokemonSetJson.RuntimeManifest(document.ToJsonString())

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
    [<Arguments("/rulesVersion", "\"unsupported\"", "runtime.rules-version")>]
    [<Arguments("/stack/cardCount", "0", "runtime.stack-range")>]
    [<Arguments("/stack/mechanicalCopyLimit", "0", "runtime.stack-range")>]
    [<Arguments("/stack/requiresRegularBloke", "false", "runtime.stack-regular-required")>]
    [<Arguments("/opening/openingParticipantSampledBeforeShuffle",
                "false",
                "runtime.opening-participant-sampling")>]
    [<Arguments("/opening/ocheRegularCount", "2", "runtime.opening-oche-count")>]
    [<Arguments("/opening/mulligans", "\"Once\"", "runtime.opening-mulligan-mode")>]
    [<Arguments("/opening/bothMulliganNoBonus", "false", "runtime.opening-both-mulligan-bonus")>]
    [<Arguments("/opening/otherSideBonusPerExtraMulligan",
                "false",
                "runtime.opening-extra-mulligan-bonus")>]
    [<Arguments("/opening/otherSideBonusOptional",
                "false",
                "runtime.opening-mulligan-bonus-optional")>]
    [<Arguments("/round/partyTricksAreNotAttacks", "false", "runtime.round-party-tricks")>]
    [<Arguments("/vim/costNotChuckedUnlessSpecified", "false", "runtime.vim-cost-retention")>]
    [<Arguments("/vim/localSatisfiedByAnyVim", "false", "runtime.vim-local-cost")>]
    [<Arguments("/kit/barBitsPerRound", "\"One\"", "runtime.kit-bar-bits-per-round")>]
    [<Arguments("/kit/barKitsPerRound", "\"One\"", "runtime.kit-bar-kits-per-round")>]
    [<Arguments("/taxi/requiresBooth", "false", "runtime.taxi-source")>]
    [<Arguments("/damage/boothDamageUsesSoftSpotOrStubbornStreak",
                "true",
                "runtime.damage-booth-modifiers")>]
    [<Arguments("/damage/placedCountersUseDamageModifiers",
                "true",
                "runtime.damage-placed-counter-modifiers")>]
    [<Arguments("/selectionRules/upToCount", "\"Anything\"", "runtime.selection-up-to-count")>]
    [<Arguments("/selectionRules/anyAmountOrNumber",
                "\"AtLeastOne\"",
                "runtime.selection-any-amount")>]
    [<Arguments("/selectionRules/optional", "\"Required\"", "runtime.selection-optional")>]
    [<Arguments("/effectDrawFromShortStack", "99", "runtime.effect-draw-short-stack")>]
    [<Arguments("/requiredRoundDrawFromEmptyStack", "99", "runtime.required-round-draw")>]
    [<Arguments("/checkup/roughStateOrder/0", "\"Singed\"", "runtime.checkup-order")>]
    [<Arguments("/checkup/roughStateOrder/1", "\"DodgyPint\"", "runtime.checkup-order")>]
    [<Arguments("/checkup/roughStateOrder/2", "\"Legless\"", "runtime.checkup-order")>]
    [<Arguments("/checkup/roughStateOrder/3", "\"NoddedOff\"", "runtime.checkup-order")>]
    [<Arguments("/checkup/otherEffectsOutsideWholeBlock",
                "false",
                "runtime.checkup-other-effects-boundary")>]
    [<Arguments("/checkup/cannotInterleave", "false", "runtime.checkup-no-interleave")>]
    [<Arguments("/checkup/sendHomeAfterBothChecks", "false", "runtime.checkup-send-home-order")>]
    [<Arguments("/roughStates/0/state", "\"Singed\"", "runtime.rough-state-order")>]
    [<Arguments("/roughStates/1/state", "\"DodgyPint\"", "runtime.rough-state-order")>]
    [<Arguments("/roughStates/2/state", "\"Legless\"", "runtime.rough-state-order")>]
    [<Arguments("/roughStates/3/state", "\"Muddled\"", "runtime.rough-state-order")>]
    [<Arguments("/roughStates/4/state", "\"NoddedOff\"", "runtime.rough-state-order")>]
    [<Arguments("/roughStates/0/ocheOnly", "false", "runtime.rough-state-location")>]
    [<Arguments("/roughStates/1/ocheOnly", "false", "runtime.rough-state-location")>]
    [<Arguments("/roughStates/2/ocheOnly", "false", "runtime.rough-state-location")>]
    [<Arguments("/roughStates/3/ocheOnly", "false", "runtime.rough-state-location")>]
    [<Arguments("/roughStates/4/ocheOnly", "false", "runtime.rough-state-location")>]
    [<Arguments("/roughStates/0/checkupDamageCounters",
                "-1",
                "runtime.rough-state-checkup-damage-range")>]
    [<Arguments("/roughStates/4/checkupDamageCounters",
                "1",
                "runtime.rough-state-muddled-checkup-damage")>]
    [<Arguments("/roughStates/4/checkupBeerMat",
                "true",
                "runtime.rough-state-muddled-checkup-beer-mat")>]
    [<Arguments("/roughStates/4/badgeSideRecovers",
                "true",
                "runtime.rough-state-muddled-badge-recovery")>]
    [<Arguments("/roughStates/4/recoversAfterOwnersNextRound",
                "true",
                "runtime.rough-state-muddled-round-recovery")>]
    [<Arguments("/roughStates/0/badgeSideRecovers",
                "true",
                "runtime.rough-state-badge-requires-beer-mat")>]
    [<Arguments("/roughStates/0/recoversAfterOwnersNextRound",
                "false",
                "runtime.rough-state-round-recovery-value")>]
    [<Arguments("/roughStates/0/beforeAttackBeerMat",
                "false",
                "runtime.rough-state-before-attack-value")>]
    [<Arguments("/roughStates/0/beforeAttackBeerMat",
                "true",
                "runtime.rough-state-before-attack-pair")>]
    [<Arguments("/roughStates/4/blankSideCancelsAndSelfDamageCounters",
                "0",
                "runtime.rough-state-self-damage-range")>]
    [<Arguments("/roughStateCoexistence/rotatedGroup/0",
                "\"Muddled\"",
                "runtime.rough-state-rotated-group")>]
    [<Arguments("/roughStateCoexistence/rotatedGroup/1",
                "\"NoddedOff\"",
                "runtime.rough-state-rotated-group")>]
    [<Arguments("/roughStateCoexistence/rotatedGroup/2",
                "\"Muddled\"",
                "runtime.rough-state-rotated-group")>]
    [<Arguments("/roughStateCoexistence/markerGroup/0",
                "\"Legless\"",
                "runtime.rough-state-marker-group")>]
    [<Arguments("/roughStateCoexistence/markerGroup/1",
                "\"Singed\"",
                "runtime.rough-state-marker-group")>]
    [<Arguments("/roughStateCoexistence/markersCoexistWithEachOtherAndRotatedGroup",
                "false",
                "runtime.rough-state-marker-coexistence")>]
    [<Arguments("/sendHome/damageAtLeastStayingPower", "false", "runtime.send-home-threshold")>]
    [<Arguments("/sendHome/chuckBlokeAndAttachedCards", "false", "runtime.send-home-chuck-pile")>]
    [<Arguments("/sendHome/ownerPromotesFromBooth", "false", "runtime.send-home-replacement")>]
    [<Arguments("/win/conditions/0", "\"Unknown\"", "runtime.win-conditions")>]
    [<Arguments("/win/conditions/1", "\"Unknown\"", "runtime.win-conditions")>]
    [<Arguments("/win/conditions/2", "\"Unknown\"", "runtime.win-conditions")>]
    [<Arguments("/win/oneMethodEach", "\"Immediate\"", "runtime.win-one-method-each")>]
    [<Arguments("/win/moreMethodsWins", "\"SuddenDeath\"", "runtime.win-more-methods")>]
    [<Arguments("/win/repeatUntilWinner", "false", "runtime.win-repeat-sudden-death")>]
    [<Arguments("/fossilKits/kitIds/0", "\"KIT-004\"", "runtime.fossil-kit-ids")>]
    [<Arguments("/fossilKits/kitIds/1", "\"KIT-004\"", "runtime.fossil-kit-ids")>]
    [<Arguments("/fossilKits/kitIds/2", "\"KIT-004\"", "runtime.fossil-kit-ids")>]
    [<Arguments("/fossilKits/cannotTaxi", "false", "runtime.fossil-taxi")>]
    [<Arguments("/opcodeInventory/0", "\"AdjustDamage\"", "runtime.opcode-inventory")>]
    [<Arguments("/bigHitters/blokeIds/0", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/1", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/2", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/3", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/4", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/5", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/6", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/7", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/8", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/9", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    [<Arguments("/bigHitters/blokeIds/10", "\"BLK-004\"", "runtime.big-hitter-ids-unknown")>]
    member _.``unsupported base rule mutations should have specific load diagnostics``
        (pointer: string, jsonValue: string, expectedCode: string)
        =
        let result =
            Authorities.withBaseRuleValue pointer jsonValue
            |> BlokemonSetValidator.ValidateRuntime

        result.Issues |> Seq.map _.Code |> should contain expectedCode

    [<Test>]
    member _.``Big Hitter inventory mutations should diagnose every structural failure``() =
        let authority = Authorities.mechanics.Value

        let expected =
            [| "BLK-003"
               "BLK-006"
               "BLK-009"
               "BLK-024"
               "BLK-038"
               "BLK-065"
               "BLK-076"
               "BLK-115"
               "BLK-124"
               "BLK-145"
               "BLK-151" |]

        let codes ids =
            BlokemonSetValidator
                .ValidateRuntime(
                    { authority with
                        BaseRules =
                            { authority.BaseRules with
                                BigHitters =
                                    { authority.BaseRules.BigHitters with
                                        BlokeIds = ids } } }
                )
                .Issues
            |> Seq.map _.Code

        codes (expected |> Array.skip 1)
        |> should contain "runtime.big-hitter-ids-omission"

        let duplicated = Array.copy expected
        duplicated[1] <- duplicated[0]

        codes duplicated |> should contain "runtime.big-hitter-ids-duplicate"

        codes (Array.append expected [| "BLK-004" |])
        |> should contain "runtime.big-hitter-ids-unknown"

        let reordered = Array.copy expected
        let first = reordered[0]
        reordered[0] <- reordered[1]
        reordered[1] <- first

        codes reordered |> should contain "runtime.big-hitter-ids-unsupported-order"

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
