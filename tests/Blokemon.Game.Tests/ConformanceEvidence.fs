namespace Blokemon.Game.Tests

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text.Json
open ConformanceCensus

module internal ConformanceEvidence =

    let CensusFilename = "080-conformance-census-final.md"
    let CoverageFilename = "080-program-coverage-post-correction.md"

    type HistoricalDisposition =
        | Removed
        | Changed
        | Surviving

    type HistoricalRow =
        { MechanicalId: string
          Disposition: HistoricalDisposition }

    let sha256 (path: string) =
        path
        |> File.ReadAllBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private requireEnvironment name =
        match Environment.GetEnvironmentVariable name with
        | null
        | "" -> failwith $"{name} must name the governed evidence input or output."
        | value -> value

    let private programJsonById path =
        use document = JsonDocument.Parse(File.ReadAllText path)
        let programs = Dictionary<string, string>(StringComparer.Ordinal)

        for group in [| "collectibles"; "kits" |] do
            for card in document.RootElement.GetProperty(group).EnumerateArray() do
                for effectKind in [| "partyTricks"; "attacks"; "houseRules" |] do
                    for effect in card.GetProperty(effectKind).EnumerateArray() do
                        match effect.GetProperty("mechanicalId").GetString() with
                        | null -> failwith $"A {effectKind} mechanicalId was null in {path}."
                        | mechanicalId ->
                            programs.Add(mechanicalId, effect.GetProperty("program").GetRawText())

        programs

    let historicalResidualIds path =
        let lines = File.ReadAllLines path

        let heading =
            lines
            |> Array.tryFindIndex ((=) "## Never-observed or event-unobservable programs")
            |> Option.defaultWith (fun () ->
                failwith $"The residual heading was absent from {path}.")

        lines[(heading + 1) ..]
        |> Array.choose (fun line ->
            if
                line.StartsWith("- BLK-", StringComparison.Ordinal)
                || line.StartsWith("- KIT-", StringComparison.Ordinal)
            then
                Some line[2..]
            else
                None)

    let reconcileHistoricalResiduals historicalReport oldMechanics currentMechanics =
        let oldPrograms = programJsonById oldMechanics
        let currentPrograms = programJsonById currentMechanics

        historicalResidualIds historicalReport
        |> Array.map (fun mechanicalId ->
            let disposition =
                match currentPrograms.TryGetValue mechanicalId with
                | false, _ -> Removed
                | true, current ->
                    match oldPrograms.TryGetValue mechanicalId with
                    | true, old when old = current -> Surviving
                    | _ -> Changed

            { MechanicalId = mechanicalId
              Disposition = disposition })

    let private escaped (value: string) = value.Replace("|", "\\|")

    let private opcodeList (row: ProgramRow) =
        instructions row.Program
        |> Seq.map _.Opcode
        |> Seq.distinct
        |> Seq.map (fun opcode -> $"`{opcode}`")
        |> String.concat ", "

    let private conditionList (row: ProgramRow) =
        instructions row.Program
        |> Seq.collect _.Predicates
        |> Seq.map _.Condition
        |> Seq.distinct
        |> Seq.map (fun condition -> $"`{condition}`")
        |> String.concat ", "

    let private writePostCorrectionCoverage
        evidenceDirectory
        sourceHead
        (results: FuzzHarness.BoutResult array)
        =
        if FuzzHarness.reconciliationProgramIds <> FuzzHarness.allPrograms then
            failwith "The reconciliation and mechanics program populations differ."

        let knownPrograms = FuzzHarness.allPrograms |> Set.ofArray

        let unknownObserved =
            results
            |> Seq.collect _.ObservedEffects
            |> Seq.map _.Value
            |> Seq.filter (knownPrograms.Contains >> not)
            |> Seq.distinct
            |> Seq.toArray

        if unknownObserved.Length <> 0 then
            failwith $"Unknown observed effect IDs: {String.Join(',', unknownObserved)}"

        let temporaryName = "BLOKEMON-080-program-coverage-working.md"

        let temporaryPath, observed, unobserved, incomplete =
            FuzzHarness.coverageReport
                temporaryName
                FuzzHarness.LargeSweepSeeds
                FuzzHarness.LargeSweepStepCeiling
                TimeSpan.Zero
                results

        let mechanics =
            Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")

        let reconciliation =
            Path.Combine(
                AppContext.BaseDirectory,
                "Authorities",
                "sv151-authority-reconciliation.json"
            )

        let lines = File.ReadAllLines temporaryPath |> ResizeArray
        lines[0] <- "# BLOKEMON-080 post-correction self-play program coverage"
        lines.Insert(2, $"- Source HEAD: `{sourceHead}`")
        lines.Insert(3, $"- mechanics.json SHA-256: `{sha256 mechanics}`")
        lines.Insert(4, $"- reconciliation SHA-256: `{sha256 reconciliation}`")

        let runtimeLine =
            lines
            |> Seq.findIndex (fun line -> line.StartsWith("- Measured harness runtime:"))

        lines[runtimeLine] <-
            "- Measured harness runtime: omitted from this canonical deterministic report; wall time belongs in the verification log."

        let output = Path.Combine(evidenceDirectory, CoverageFilename)

        File.WriteAllLines(output, lines)
        File.Delete temporaryPath
        output, observed, unobserved, incomplete

    let private writeCensus
        evidenceDirectory
        sourceHead
        historicalReport
        oldMechanics
        coveragePath
        coverageObserved
        coverageUnobserved
        historicalRows
        =
        let mechanics =
            Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")

        let reconciliation =
            Path.Combine(
                AppContext.BaseDirectory,
                "Authorities",
                "sv151-authority-reconciliation.json"
            )

        let dispositionCount disposition =
            historicalRows
            |> Array.filter (fun row -> row.Disposition = disposition)
            |> Array.length

        let lines = ResizeArray<string>()
        lines.Add "# BLOKEMON-080 corrected-authority conformance census"
        lines.Add ""
        lines.Add "## Provenance and independent totals"
        lines.Add ""
        lines.Add $"- Source HEAD: `{sourceHead}`"
        lines.Add $"- mechanics.json SHA-256: `{sha256 mechanics}`"
        lines.Add $"- reconciliation SHA-256: `{sha256 reconciliation}`"
        lines.Add $"- BLOKEMON-079 report SHA-256: `{sha256 historicalReport}`"
        lines.Add $"- Pre-correction mechanics SHA-256: `{sha256 oldMechanics}`"
        lines.Add $"- Post-correction coverage SHA-256: `{sha256 coveragePath}`"

        lines.Add
            "- Program flattening recursively counts each instruction in `program`, `then`, and `otherwise`; recursively nontrivial means more than one such instruction."

        lines.Add
            "- The post-correction coverage report remains **APPROXIMATE** effect attribution. Neither its attributed nor unobserved/event-unobservable population is execution proof."

        lines.Add $"- Program-bearing cards: {totals.ProgramBearingCards}"
        lines.Add $"- Programs: {totals.Programs}"
        lines.Add $"- Recursive instructions: {totals.RecursiveInstructions}"
        lines.Add $"- Declared-and-used opcodes: {totals.DeclaredAndUsedOpcodes}"
        lines.Add $"- Declared-and-used conditions: {totals.DeclaredAndUsedConditions}"
        lines.Add $"- Non-Activated triggers: {totals.NonActivatedTriggers}"
        lines.Add $"- Recursively nontrivial programs: {totals.RecursiveNontrivialPrograms}"

        lines.Add
            $"- Executable nontrivial composition snapshots: {executableNontrivialPrograms.Length}"

        lines.Add
            $"- Structural nontrivial program-row exclusions: {structuralNontrivialProgramExclusions.Length}"

        lines.Add $"- Big Hitters: {totals.BigHitters}"
        lines.Add ""
        lines.Add "## Opcode evidence"
        lines.Add ""
        lines.Add "| Opcode | Exact semantic state/event/validation/structural reference |"
        lines.Add "|---|---|"

        for opcode in usedOpcodes |> Set.toArray |> Array.sortBy int do
            lines.Add $"| `{opcode}` | {escaped opcodeEvidence[opcode]} |"

        lines.Add ""
        lines.Add "## Condition evidence"
        lines.Add ""
        lines.Add "| Condition | Truthy and falsy observable MatchEngine reference |"
        lines.Add "|---|---|"

        for condition in usedConditions |> Set.toArray |> Array.sortBy int do
            lines.Add $"| `{condition}` | {escaped (conditionEvidence condition)} |"

        lines.Add ""
        lines.Add "## Non-Activated trigger evidence"
        lines.Add ""

        lines.Add
            "| Program | Trigger | Fires in declared condition | Does not fire outside condition |"

        lines.Add "|---|---|---|---|"

        for row in nonActivatedTriggers do
            let trigger = row.Trigger.Value
            let reference = triggerEvidence row |> escaped

            lines.Add
                $"| `{row.MechanicalId}` | `{trigger}` | {reference} [firing] | {reference} [non-firing] |"

        lines.Add ""
        lines.Add "## Recursively nontrivial program composition"
        lines.Add ""

        lines.Add
            "| Program | Instructions | Opcodes | Conditions | Semantic SHA-256 | Exact composition scenario |"

        lines.Add "|---|---:|---|---|---|---|"

        for row, count in recursiveNontrivialPrograms do
            let conditions = conditionList row
            let displayedConditions = if conditions = "" then "none" else conditions

            match expectedCompositionHashes.TryFind row.MechanicalId with
            | Some semanticHash ->
                lines.Add
                    $"| `{row.MechanicalId}` | {count} | {opcodeList row} | {displayedConditions} | `{semanticHash}` | {escaped (executionEvidence row)} |"
            | None when declarativeKitStructuralProgramIds.Contains row.MechanicalId ->
                lines.Add
                    $"| `{row.MechanicalId}` | {count} | {opcodeList row} | {displayedConditions} | not executed / structural | {escaped (executionEvidence row)} |"
            | None -> failwith $"Nontrivial program {row.MechanicalId} had no evidence disposition."

        lines.Add ""
        lines.Add "## Historical BLOKEMON-079 residual reconciliation"
        lines.Add ""
        lines.Add $"- Historical residual IDs: {historicalRows.Length}"
        lines.Add $"- Removed: {dispositionCount Removed}"
        lines.Add $"- Changed: {dispositionCount Changed}"
        lines.Add $"- Surviving: {dispositionCount Surviving}"

        lines.Add
            "- Current-only: `BLK-040-B02`; it was added by the accepted Jungle correction and therefore is not one of the historical 222 residual IDs."

        lines.Add ""
        lines.Add "| Historical residual program | Disposition |"
        lines.Add "|---|---|"

        for row in historicalRows do
            lines.Add $"| `{row.MechanicalId}` | {row.Disposition} |"

        lines.Add ""
        lines.Add "## Post-correction approximate attribution"
        lines.Add ""
        lines.Add $"- Effect-attributed program IDs: {coverageObserved}/{totals.Programs}"

        lines.Add
            $"- Unobserved or event-unobservable program IDs: {coverageUnobserved}/{totals.Programs}"

        lines.Add $"- Exact sibling report: `{CoverageFilename}`"
        lines.Add ""
        lines.Add "## Semantic mutation checks"
        lines.Add ""

        lines.Add
            "`AuthorityAuditTests.``mutating a declared reactive trigger program should change what the runtime does``` exercises these five distinct opcode families through MatchEngine:"

        lines.Add ""
        lines.Add "- `MoveVim`: remove its accepted mechanical-type restriction."
        lines.Add "- `RecoverFromSendHome`: reduce its recovery amount."
        lines.Add "- `PlaceDamageCounters`: change the number of counters."
        lines.Add "- `SendHome`: replace the send-home operation with counter placement."
        lines.Add "- `TakeExtraBarChit`: change the extra award amount."
        lines.Add ""
        lines.Add "## Accepted correction retention"
        lines.Add ""

        lines.Add
            "- KIT-013 Regular truthy and non-Regular falsy behavior remains pinned by the two protective-goggles MatchEngine tests."

        lines.Add
            "- Jungle BLK-040 remains pinned by exact authority/public parity, Lullaby zero-damage NoddedOff, Do the Wave own-Booth scaling, Fighting x2, Psychic -30, fare 2, ordinary one-chit send-home, and the eleven-Big-Hitter award test."

        lines.Add
            "- `SelfHasSpecialVim`, `ModifyStayingPower`, and the dead Big-Hitter opcode/rules have no declared or used current authority surface."

        lines.Add ""
        lines.Add "## Precise exclusions"
        lines.Add ""

        lines.Add
            "- `TriggeredPartyTrick` is structural-only: AuthorityAudit validates the marker, while each owning `BlokemonPartyTrick.Trigger` row has firing/non-firing MatchEngine proof. No other opcode's state change is credited to it."

        lines.Add
            "- `TriggeredPartyTrick` remains the sole opcode-level structural exclusion. The fourteen exclusions below are program-row dispositions and do not exclude either `Conditional` or `ContinuousPartyTrick` from opcode semantics."

        lines.Add
            "- `BoothHasSpace` falsy proof is the authoritative full-Booth outer guard/non-invocation route; it does not assert that the nested predicate evaluator ran."

        lines.Add
            "- `OwnBlokeSentHomeByOtherAttackDamage` falsy proof is authoritative trigger non-invocation for recoil; it does not assert that the nested predicate evaluator ran."

        lines.Add
            "- `KIT-001-R02` through `KIT-014-R02` are the exact fourteen NOT EXECUTED / STRUCTURAL PROGRAM ROW exclusions. `MatchKitHandlers` filters `isDeclarativeHouseRule` from Kit-play execution, and `MatchContinuous.refreshContinuousEffects` filters house rules containing `Optional` from continuous refresh. Their `Conditional(Optional) -> ContinuousPartyTrick` shape is declarative, so no Kit-play or refresh state change is credited to these rows."

        lines.Add
            "- The other 178 recursively nontrivial rows name exact MatchEngine composition snapshots. Together the 178 executable rows and fourteen structural rows account for all 192 without overlap."

        lines.Add ""
        lines.Add "## Reproduction commands"
        lines.Add ""
        lines.Add "```sh"

        lines.Add "export BLOKEMON_080_EVIDENCE_DIR='<output-directory>'"
        lines.Add "export BLOKEMON_080_SOURCE_HEAD='<immutable-source-head>'"
        lines.Add "export BLOKEMON_079_REPORT='<historical-079-coverage-report>'"

        lines.Add "export BLOKEMON_080_PRE_CORRECTION_MECHANICS='<pre-correction-mechanics-json>'"

        lines.Add
            "git show da75b663c15a8626e991b3c70ddc37d002f3d81a:content/authorities/mechanics.json > \"$BLOKEMON_080_PRE_CORRECTION_MECHANICS\""

        lines.Add
            "dotnet test tests/Blokemon.Game.Tests/Blokemon.Game.Tests.fsproj -c Release --treenode-filter '/*/*/*/the opt-in BLOKEMON-080 evidence generator should reconcile and write the deterministic reports' -- --minimum-expected-tests 1"

        lines.Add
            $"sha256sum \"$BLOKEMON_080_EVIDENCE_DIR/{CensusFilename}\" \"$BLOKEMON_080_EVIDENCE_DIR/{CoverageFilename}\""

        lines.Add "```"

        let output = Path.Combine(evidenceDirectory, CensusFilename)
        File.WriteAllLines(output, lines)
        output

    let generate () =
        let evidenceDirectory = requireEnvironment "BLOKEMON_080_EVIDENCE_DIR"
        let sourceHead = requireEnvironment "BLOKEMON_080_SOURCE_HEAD"
        let historicalReport = requireEnvironment "BLOKEMON_079_REPORT"

        let oldMechanics = requireEnvironment "BLOKEMON_080_PRE_CORRECTION_MECHANICS"

        Directory.CreateDirectory evidenceDirectory |> ignore

        let currentMechanics =
            Path.Combine(AppContext.BaseDirectory, "Authorities", "mechanics.json")

        let historicalRows =
            reconcileHistoricalResiduals historicalReport oldMechanics currentMechanics

        let results =
            FuzzHarness.LargeSweepSeeds
            |> Array.map (fun seed -> FuzzHarness.runBout seed FuzzHarness.LargeSweepStepCeiling)

        FuzzHarness.assertNoFindings results

        let incomplete =
            results |> Array.filter (fun result -> result.Status = FuzzHarness.Incomplete)

        if incomplete.Length <> 0 then
            failwith $"The post-correction sweep left {incomplete.Length} incomplete bouts."

        let coveragePath, observed, unobserved, reportedIncomplete =
            writePostCorrectionCoverage evidenceDirectory sourceHead results

        if reportedIncomplete <> 0 then
            failwith
                $"The report counted {reportedIncomplete} incomplete bouts after the pre-write gate."

        let censusPath =
            writeCensus
                evidenceDirectory
                sourceHead
                historicalReport
                oldMechanics
                coveragePath
                observed
                unobserved
                historicalRows

        censusPath, coveragePath, historicalRows, observed, unobserved
