namespace Blokemon.Core.PublicContent

open System
open System.Collections.Generic
open System.Globalization
open System.Linq
open System.Text.RegularExpressions
open Blokemon.Core.SetDesign

/// Owned validation of the public content authority against the mechanical authority.
module BlokemonPublicContentValidator =

    [<Literal>]
    let SchemaVersion = "blokemon-public-content-schema-1999-candidate.1"

    [<Literal>]
    let ContentVersion = "blokemon-public-content-1999-candidate.1"

    [<Literal>]
    let TerminologyVersion = "blokemon-public-terminology-1999-candidate.1"

    [<Literal>]
    let private ArtAuthority = "Blokemon"

    /// One effect the rule authority requires the public content to publish.
    type private ExpectedEffect =
        { MechanicalId: string
          Program: BlokemonEffectInstruction array
          CanOmitText: bool
          CanBeUsedFromBench: bool }

    let private rejectedMechanicsVocabulary =
        [| "Barney"
           "Bother"
           "Staying Power"
           "Party Trick"
           "Vim"
           "Stack"
           "Oche"
           "Booth"
           "Soft Spot"
           "Stubborn Streak"
           "Taxi"
           "Bar Chit"
           "Sent Home"
           "Promotion"
           "Mitt"
           "Empties Tray"
           "Chuck"
           "Rough"
           "beer mat"
           "badge side"
           "blank side"
           "other side"
           "bloke" |]

    let private termCounts =
        [| BlokemonPublicTermCategory.Type, 10
           BlokemonPublicTermCategory.Stage, 3
           BlokemonPublicTermCategory.Category, 1
           BlokemonPublicTermCategory.Status, 4
           BlokemonPublicTermCategory.Target, 16
           BlokemonPublicTermCategory.Choice, 8
           BlokemonPublicTermCategory.Quantity, 11
           BlokemonPublicTermCategory.Cost, 4
           BlokemonPublicTermCategory.Timing, 12
           BlokemonPublicTermCategory.Core, 21 |]

    let private effectTextOf (effect: BlokemonPublicEffect) = Option.ofObj effect.EffectText

    let private check
        (condition: bool)
        (code: string)
        (message: string)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        if not condition then
            issues.Add({ Code = code; Message = message })

    let private distinctCount (comparer: IEqualityComparer<string>) (values: string seq) =
        HashSet<string>(values, comparer).Count

    let private containsTerm (value: string) (term: string) =
        Regex.IsMatch(
            value,
            $@"(?<![A-Za-z]){Regex.Escape(term)}(?:s)?(?![A-Za-z])",
            RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant
        )

    let private isCompleteSentence (value: string) =
        let ending = value.TrimEnd([| ')' |])
        ending.Length > 0 && ".!?".Contains(ending[ending.Length - 1])

    let private expectedFrom (mechanicalId: string) program canOmitText canBeUsedFromBench =
        { MechanicalId = mechanicalId
          Program = program
          CanOmitText = canOmitText
          CanBeUsedFromBench = canBeUsedFromBench }

    let private expectedCollectibleRules
        (_mechanics: BlokemonRuntimeManifest)
        (mechanical: BlokemonCollectible)
        =
        mechanical.HouseRules
        |> Array.map (fun effect -> expectedFrom effect.MechanicalId effect.Program false false)

    let private validateEffects
        (ownerId: string)
        (kind: string)
        (content: BlokemonPublicEffect array)
        (expected: ExpectedEffect array)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        let contentIds = content |> Array.map (fun effect -> effect.MechanicalId)
        let expectedIds = expected |> Array.map (fun effect -> effect.MechanicalId)

        check
            (contentIds = expectedIds)
            "effect.mechanical-id"
            $"{ownerId} public {kind} IDs or order do not match the locked mechanical authority."
            issues

        for index in 0 .. Math.Min(content.Length, expected.Length) - 1 do
            let effect = content[index]
            let authority = expected[index]

            check
                (not (String.IsNullOrWhiteSpace(effect.Name)))
                "effect.name"
                $"{effect.MechanicalId} has no public name."
                issues

            if authority.CanOmitText then
                check
                    (isNull effect.EffectText)
                    "effect.pure-damage"
                    $"{effect.MechanicalId} is a pure-Damage Attack and must omit effectText."
                    issues
            else
                check
                    (not (String.IsNullOrWhiteSpace(effect.EffectText)))
                    "effect.text"
                    $"{effect.MechanicalId} has no exact public effect text."
                    issues

                match effect.EffectText with
                | null -> ()
                | text when String.IsNullOrWhiteSpace(text) -> ()
                | text ->
                    check
                        (Char.IsUpper(text[0]) && isCompleteSentence text)
                        "effect.grammar"
                        $"{effect.MechanicalId} effect text must be a complete, capitalised sentence."
                        issues

                    check
                        (not authority.CanBeUsedFromBench
                         || text.Contains("Bench", StringComparison.Ordinal))
                        "effect.bench-declaration"
                        $"{effect.MechanicalId} must state its typed Bench declaration permission."
                        issues

    let private validateTerminology
        (manifest: BlokemonPublicContentManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        check
            (manifest.Terminology.Length = (termCounts |> Array.sumBy snd))
            "terminology.count"
            "The terminology table has the wrong total cardinality."
            issues

        check
            (manifest.Terminology
             |> Array.map (fun term -> term.Id)
             |> distinctCount StringComparer.Ordinal = manifest.Terminology.Length)
            "terminology.id"
            "Every terminology ID must be unique."
            issues

        for category, expectedCount in termCounts do
            let terms =
                manifest.Terminology |> Array.filter (fun term -> term.Category = category)

            check
                (terms.Length = expectedCount)
                "terminology.category-count"
                $"{category} must contain exactly {expectedCount} terms."
                issues

            let prefix = $"TERM-{category.ToString().ToUpperInvariant()}-"

            let expectedIds =
                [| for index in 1..expectedCount ->
                       prefix + index.ToString("000", CultureInfo.InvariantCulture) |]

            check
                ((terms |> Array.map (fun term -> term.Id)) = expectedIds)
                "terminology.order"
                $"{category} terminology IDs must be complete and deterministic."
                issues

        let roadie =
            manifest.Terminology.SingleOrDefault(fun term -> term.Id = "TERM-TYPE-010")

        check
            (match roadie with
             | null -> false
             | term -> term.Singular = "Roadie" && term.Plural = "Roadie")
            "terminology.roadie"
            "D224 Roadie must remain the tenth effect-only type term."
            issues

        let requiredTerms =
            [| "HP"
               "Blokemon Power"
               "Attack"
               "Damage"
               "Energy"
               "Deck"
               "Active Blokemon"
               "Bench"
               "Weakness"
               "Resistance"
               "Retreat"
               "Prize Card"
               "Knocked Out"
               "Evolution"
               "Trainer" |]

        let publicLabels =
            manifest.Terminology
            |> Seq.collect (fun term -> [| term.Singular; term.Plural |])

        for requiredTerm in requiredTerms do
            check
                (publicLabels
                 |> Seq.exists (fun label ->
                     String.Equals(label, requiredTerm, StringComparison.Ordinal)))
                "terminology.required"
                $"The public terminology is missing {requiredTerm}."
                issues

    let private validateCollectibles
        (manifest: BlokemonPublicContentManifest)
        (mechanics: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        check
            (manifest.Collectibles.Length = 151)
            "collectible.count"
            "There must be exactly 151 public collectible entries."
            issues

        for index in 0 .. Math.Min(manifest.Collectibles.Length, mechanics.Collectibles.Length) - 1 do
            let content = manifest.Collectibles[index]
            let mechanical = mechanics.Collectibles[index]

            check
                (content.Id = mechanical.Id)
                "collectible.id"
                $"Collectible {index + 1} is not bound to the locked ID."
                issues

            check
                (content.ApprovedName = mechanical.ApprovedName)
                "collectible.name"
                $"{mechanical.Id} changed its D223 name."
                issues

            check
                (content.ApprovedType = mechanical.ApprovedType)
                "collectible.type"
                $"{mechanical.Id} changed its D223 type."
                issues

            check
                (content.Art.Status = BlokemonPublicArtStatus.Accepted
                 && content.Art.Authority = ArtAuthority)
                "collectible.art"
                $"{mechanical.Id} must expose accepted Blokemon artwork."
                issues

            validateEffects
                content.Id
                "Blokemon Power"
                content.PokemonPowers
                (mechanical.PartyTricks
                 |> Array.map (fun effect ->
                     expectedFrom effect.MechanicalId effect.Program false false))
                issues

            validateEffects
                content.Id
                "attack"
                content.Attacks
                (mechanical.Attacks
                 |> Array.map (fun effect ->
                     expectedFrom
                         effect.MechanicalId
                         effect.Program
                         (BlokemonAttackSemantics.isPureDamageAttack effect)
                         effect.CanBeUsedFromBench))
                issues

            validateEffects
                content.Id
                "rule"
                content.Rules
                (expectedCollectibleRules mechanics mechanical)
                issues

        check
            (manifest.Collectibles
             |> Array.map (fun card -> card.FlavourText)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 151)
            "collectible.flavour"
            "Every flavour line must be individually authored."
            issues

        check
            (manifest.Collectibles
             |> Array.map (fun card -> card.Illustration.Brief)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 151)
            "collectible.brief"
            "Every illustration brief must be individually authored."
            issues

        check
            (manifest.Collectibles
             |> Array.map (fun card -> card.Illustration.Prompt)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 151)
            "collectible.prompt"
            "Every illustration prompt must be individually authored."
            issues

        check
            (manifest.Collectibles
             |> Array.map (fun card -> card.Illustration.AltIntent)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 151)
            "collectible.alt"
            "Every alt intent must be individually authored."
            issues

    let private validateTrainers
        (manifest: BlokemonPublicContentManifest)
        (mechanics: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        check
            (manifest.Trainers.Length = 32)
            "trainer.count"
            "There must be exactly 32 public Trainer entries."
            issues

        for index in 0 .. Math.Min(manifest.Trainers.Length, mechanics.Kits.Length) - 1 do
            let content = manifest.Trainers[index]
            let mechanical = mechanics.Kits[index]

            check
                (content.Id = mechanical.Id)
                "trainer.id"
                $"Trainer {index + 1} is not bound to the locked ID."
                issues

            let expectedEffects =
                [| yield!
                       mechanical.PartyTricks
                       |> Array.map (fun effect ->
                           expectedFrom effect.MechanicalId effect.Program false false)
                   yield!
                       mechanical.Attacks
                       |> Array.map (fun effect ->
                           expectedFrom effect.MechanicalId effect.Program false false)
                   yield!
                       mechanical.HouseRules
                       |> Array.map (fun effect ->
                           expectedFrom effect.MechanicalId effect.Program false false) |]

            validateEffects content.Id "effect" content.Effects expectedEffects issues

        check
            (manifest.Trainers
             |> Array.map (fun trainer -> trainer.Name)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 32)
            "trainer.name"
            "Every Trainer must retain an individually authored name."
            issues

    let private validateEnergy
        (manifest: BlokemonPublicContentManifest)
        (mechanics: BlokemonRuntimeManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        check
            (manifest.Energy.Length = 7)
            "energy.count"
            "There must be exactly seven public Energy entries."
            issues

        for index in 0 .. Math.Min(manifest.Energy.Length, mechanics.BasicVim.Length) - 1 do
            let content = manifest.Energy[index]
            let mechanical = mechanics.BasicVim[index]

            let stableSuffix = mechanical.Id.Substring("VIM-".Length)
            let energyKind = if mechanical.IsBasic then "Basic" else "Special"

            check
                (content.Id = $"ENERGY-{stableSuffix}")
                "energy.id"
                $"Energy {index + 1} has the wrong public ID."
                issues

            check
                (content.SymbolKey = $"energy-{stableSuffix.ToLowerInvariant()}")
                "energy.symbol"
                $"{content.Id} has the wrong public symbol key."
                issues

            check
                (content.AccessibleLabel.EndsWith(
                    $", {energyKind} Energy",
                    StringComparison.Ordinal
                ))
                "energy.accessibility"
                $"{content.Id} does not use its mechanical Energy classification."
                issues

        check
            (manifest.Energy
             |> Array.map (fun energy -> energy.Name)
             |> distinctCount StringComparer.OrdinalIgnoreCase = 7)
            "energy.name"
            "Every Energy entry must retain an individually authored name."
            issues

        check
            (manifest.Energy
             |> Array.map (fun energy -> energy.SymbolKey)
             |> distinctCount StringComparer.Ordinal = 7)
            "energy.symbol"
            "Every Energy entry must have a unique symbol key."
            issues

    let private publicStrings (manifest: BlokemonPublicContentManifest) =
        seq {
            yield manifest.SchemaVersion
            yield manifest.ContentVersion
            yield manifest.MechanicalManifestVersion
            yield manifest.TerminologyVersion

            for term in manifest.Terminology do
                yield term.Id
                yield term.Singular
                yield term.Plural
                yield term.Definition

            for card in manifest.Collectibles do
                yield card.Id
                yield card.ApprovedName
                yield card.FlavourText

                for effect in Array.concat [ card.PokemonPowers; card.Attacks; card.Rules ] do
                    yield effect.MechanicalId
                    yield effect.Name

                    yield! effectTextOf effect |> Option.toList

                yield card.Illustration.Brief
                yield card.Illustration.Prompt
                yield card.Illustration.AltIntent
                yield card.Art.Authority

            for trainer in manifest.Trainers do
                yield trainer.Id
                yield trainer.Name

                for effect in trainer.Effects do
                    yield effect.MechanicalId
                    yield effect.Name

                    yield! effectTextOf effect |> Option.toList

            for energy in manifest.Energy do
                yield energy.Id
                yield energy.Name
                yield energy.Definition
                yield energy.SymbolKey
                yield energy.AccessibleLabel
        }

    let private mechanicsStrings (manifest: BlokemonPublicContentManifest) =
        let effectTexts (effects: BlokemonPublicEffect array) = effects |> Array.choose effectTextOf

        seq {
            yield!
                manifest.Terminology
                |> Seq.collect (fun term -> [| term.Singular; term.Plural; term.Definition |])

            yield!
                manifest.Collectibles
                |> Seq.collect (fun card ->
                    effectTexts (Array.concat [ card.PokemonPowers; card.Attacks; card.Rules ]))

            yield! manifest.Trainers |> Seq.collect (fun trainer -> effectTexts trainer.Effects)

            yield!
                manifest.Energy
                |> Seq.collect (fun energy -> [| energy.Definition; energy.AccessibleLabel |])
        }

    let private validatePublicStrings
        (manifest: BlokemonPublicContentManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        for value in publicStrings manifest do
            check
                (value = value.Trim() && value.Length > 0)
                "text.trim"
                "Public strings must be non-empty and trimmed."
                issues

            let created, _ = Uri.TryCreate(value, UriKind.Absolute)

            check (not created) "text.uri" "Public content cannot contain an absolute URI." issues

    let private validateMechanicsVocabulary
        (manifest: BlokemonPublicContentManifest)
        (issues: ResizeArray<BlokemonPublicContentIssue>)
        =
        for value in mechanicsStrings manifest do
            for rejected in rejectedMechanicsVocabulary do
                check
                    (not (containsTerm value rejected))
                    "text.rejected-mechanics-term"
                    $"Public mechanics text contains rejected candidate.2 vocabulary: {rejected}."
                    issues

    /// Validates the public content authority against the rules this repository owns.
    let ValidateDocument
        (manifest: BlokemonPublicContentManifest)
        (mechanics: BlokemonRuntimeManifest)
        =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        ArgumentNullException.ThrowIfNull(mechanics, nameof mechanics)
        let issues = ResizeArray<BlokemonPublicContentIssue>()

        check
            (manifest.SchemaVersion = SchemaVersion)
            "document.schema"
            "The public schema version is not the approved 1999 schema."
            issues

        check
            (manifest.ContentVersion = ContentVersion)
            "document.version"
            "The public content version is not the approved 1999 version."
            issues

        check
            (manifest.TerminologyVersion = TerminologyVersion)
            "document.terminology-version"
            "The terminology version is not the approved 1999 version."
            issues

        check
            (manifest.MechanicalManifestVersion = mechanics.ManifestVersion)
            "document.mechanical-version"
            "The public manifest must bind the exact mechanical manifest version."
            issues

        check
            (manifest.HumanApprovalStatus = BlokemonPublicContentApprovalStatus.Accepted)
            "document.approval"
            "The 1999 public authority must carry exact human acceptance."
            issues

        validateTerminology manifest issues
        validateCollectibles manifest mechanics issues
        validateTrainers manifest mechanics issues
        validateEnergy manifest mechanics issues
        validatePublicStrings manifest issues
        validateMechanicsVocabulary manifest issues

        { BlokemonPublicContentValidation.Issues = issues.ToArray() }
