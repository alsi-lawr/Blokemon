namespace Blokemon.ReferenceModel

open System

type ReferenceBranchingObligation =
    { Input: ReferenceObligationInput
      InitialState: CanonicalState }

[<RequireQualifiedAccess>]
module ReferenceBranchingPrograms =

    let acceptedRoutes =
        Set
            [ "booth-search"
              "full-booth-search"
              "search-all"
              "coin-branch"
              "coin-effects"
              "coin-search"
              "coin-swap"
              "multi-toss-damage"
              "repeat-damage"
              "repeat-draw"
              "optional-bar-kit"
              "optional-decline"
              "optional-invalid-duplicate"
              "optional-max"
              "optional-zero"
              "conditional-adjust"
              "conditional-demote"
              "conditional-extra-bar"
              "conditional-rough"
              "dynamic-adjust"
              "day-two-forced-blank"
              "top-qualifying"
              "gone-smoke"
              "still-coming-up-not-promoted"
              "still-coming-up-promoted"
              "shirt-off-badge"
              "shirt-off-blank" ]

    let acceptedObligationIds =
        Set
            [ "shirt-off-badge"
              "shirt-off-blank"
              "still-coming-up-promoted"
              "still-coming-up-not-promoted"
              "trivial-force-blank-blk-054-b01"
              "dynamic-adjust-blk-019-b01-positive"
              "dynamic-adjust-blk-019-b01-zero"
              "dynamic-adjust-blk-020-b01-positive"
              "dynamic-adjust-blk-020-b01-zero"
              "dynamic-adjust-blk-040-b02-positive"
              "dynamic-adjust-blk-040-b02-zero"
              "dynamic-adjust-blk-065-b01-positive"
              "dynamic-adjust-blk-065-b01-zero"
              "dynamic-adjust-blk-085-b01-positive"
              "dynamic-adjust-blk-085-b01-zero"
              "dynamic-adjust-blk-103-b01-positive"
              "dynamic-adjust-blk-103-b01-zero"
              "dynamic-adjust-blk-119-b02-positive"
              "dynamic-adjust-blk-119-b02-zero"
              "dynamic-adjust-blk-128-b02-positive"
              "dynamic-adjust-blk-128-b02-zero"
              "coin-branch-blk-023-b01-badge"
              "coin-branch-blk-023-b01-blank"
              "coin-branch-blk-037-b01-badge"
              "coin-branch-blk-037-b01-blank"
              "coin-branch-blk-060-b01-badge"
              "coin-branch-blk-060-b01-blank"
              "coin-branch-blk-061-b02-badge"
              "coin-branch-blk-061-b02-blank"
              "coin-branch-blk-062-b01-badge"
              "coin-branch-blk-062-b01-blank"
              "coin-branch-blk-062-b02-badge"
              "coin-branch-blk-062-b02-blank"
              "coin-branch-blk-100-b01-badge"
              "coin-branch-blk-100-b01-blank"
              "coin-branch-blk-136-b01-badge"
              "coin-branch-blk-136-b01-blank"
              "conditional-adjust-blk-006-b01-true"
              "conditional-adjust-blk-006-b01-false"
              "conditional-adjust-blk-010-b01-true"
              "conditional-adjust-blk-010-b01-false"
              "conditional-adjust-blk-028-b02-true"
              "conditional-adjust-blk-028-b02-false"
              "conditional-adjust-blk-038-b02-true"
              "conditional-adjust-blk-038-b02-false"
              "conditional-adjust-blk-112-b02-true"
              "conditional-adjust-blk-112-b02-false"
              "conditional-adjust-blk-114-b01-true"
              "conditional-adjust-blk-114-b01-false"
              "conditional-adjust-blk-125-b01-true"
              "conditional-adjust-blk-125-b01-false"
              "conditional-adjust-blk-126-b02-true"
              "conditional-adjust-blk-126-b02-false"
              "conditional-adjust-blk-127-b02-true"
              "conditional-adjust-blk-127-b02-false"
              "multi-toss-blk-039-b02-all-badge"
              "multi-toss-blk-039-b02-all-blank"
              "multi-toss-blk-104-b01-all-badge"
              "multi-toss-blk-104-b01-all-blank"
              "multi-toss-blk-115-b02-all-badge"
              "multi-toss-blk-115-b02-all-blank"
              "multi-toss-blk-118-b01-all-badge"
              "multi-toss-blk-118-b01-all-blank"
              "multi-toss-blk-140-b01-all-badge"
              "multi-toss-blk-140-b01-all-blank"
              "conditional-rough-blk-057-b02-true"
              "conditional-rough-blk-057-b02-false"
              "conditional-rough-blk-124-b01-true"
              "conditional-rough-blk-124-b01-false"
              "coin-effects-blk-007-b01-badge"
              "coin-effects-blk-007-b01-blank"
              "coin-effects-blk-011-b02-badge"
              "coin-effects-blk-011-b02-blank"
              "coin-effects-blk-018-b02-badge"
              "coin-effects-blk-018-b02-blank"
              "coin-effects-blk-119-b01-badge"
              "coin-effects-blk-119-b01-blank"
              "full-booth-blk-016-b01"
              "full-booth-blk-035-b01"
              "full-booth-blk-047-b01-badges"
              "repeat-blk-073-b02-first-blank"
              "repeat-blk-073-b02-many"
              "repeat-blk-075-b01-first-blank"
              "repeat-blk-075-b01-many"
              "repeat-blk-102-b01-first-blank"
              "repeat-blk-102-b01-many"
              "repeat-blk-129-b01-first-blank"
              "repeat-blk-129-b01-many"
              "odds-on-summons-badge"
              "odds-on-summons-blank"
              "optional-zero-blk-008-b01"
              "optional-zero-blk-009-b01"
              "optional-zero-blk-022-b01"
              "optional-zero-blk-030-b01"
              "optional-zero-blk-055-b01"
              "optional-zero-blk-059-b01"
              "optional-zero-blk-082-b01"
              "optional-zero-blk-101-b01"
              "optional-zero-blk-131-b01"
              "optional-zero-blk-133-b01"
              "search-all-blk-025-b01"
              "search-all-blk-039-b01"
              "search-all-blk-128-b01"
              "big-hitter-blk-134-b02-true"
              "big-hitter-blk-134-b02-false"
              "big-hitter-blk-135-b02-true"
              "big-hitter-blk-135-b02-false"
              "big-hitter-blk-136-b02-true"
              "big-hitter-blk-136-b02-false"
              "conditional-nonfire-blk-015-b01"
              "conditional-nonfire-blk-036-b02"
              "conditional-nonfire-blk-142-b02"
              "vernalanche-one-qualifying-top-card"
              "gone-with-the-smoke-no-opponent-booth"
              "salty-start-blank"
              "optional-maximum-blk-008-b01"
              "optional-maximum-blk-009-b01"
              "optional-maximum-blk-022-b01"
              "optional-maximum-blk-030-b01"
              "optional-maximum-blk-055-b01"
              "optional-maximum-blk-059-b01"
              "optional-maximum-blk-082-b01"
              "optional-maximum-blk-131-b01"
              "optional-maximum-blk-133-b01"
              "bring-the-trades-rejects-duplicate-types"
              "booth-search-zero-blk-016-b01"
              "booth-search-maximum-blk-016-b01"
              "booth-search-zero-blk-035-b01"
              "booth-search-maximum-blk-035-b01"
              "booth-search-zero-blk-047-b01"
              "booth-search-maximum-blk-047-b01"
              "salty-start-badge-selects-zero"
              "salty-start-badge-attaches-two"
              "needle-drop-empty-mitt-adds-damage-and-rough-states"
              "chain-reaction-chucks-two-bar-kits"
              "back-to-basics-demotes-promoted-defender"
              "keep-it-going-takes-extra-bar-chit"
              "gone-with-the-smoke-shuffles-both-active-lines"
              "optional-decline-blk-008-blk-008-b01"
              "optional-decline-blk-009-blk-009-b01"
              "optional-decline-blk-016-blk-016-b01"
              "optional-decline-blk-022-blk-022-b01"
              "optional-decline-blk-030-blk-030-b01"
              "optional-decline-blk-035-blk-035-b01"
              "optional-decline-blk-055-blk-055-b01"
              "optional-decline-blk-059-blk-059-b01"
              "optional-decline-blk-082-blk-082-b01"
              "optional-decline-blk-101-blk-101-b01"
              "optional-decline-blk-131-blk-131-b01"
              "optional-decline-blk-133-blk-133-b01"
              "full-booth-blk-047-b01-blanks"
              "vernalanche-zero-qualifying-top-cards" ]

    [<Literal>]
    let AcceptedRouteCount = 27

    [<Literal>]
    let AcceptedObligationCount = 152

    let reconcile (inventory: ReferenceObligationInventory) =
        let selected =
            inventory.Obligations
            |> Array.filter (fun value -> acceptedRoutes.Contains value.InitialState.Route.Value)

        let selectedRoutes = selected |> Seq.map _.InitialState.Route.Value |> Set.ofSeq
        let selectedIds = selected |> Seq.map _.Id |> Set.ofSeq

        let duplicateIds =
            selected
            |> Seq.countBy _.Id
            |> Seq.choose (fun (id, count) -> if count = 1 then None else Some id)
            |> Seq.toArray

        if selected.Length <> AcceptedObligationCount then
            invalidOp
                $"The BLOKEMON-136 ledger contains {selected.Length} obligations instead of {AcceptedObligationCount}."

        if selectedRoutes <> acceptedRoutes || selectedRoutes.Count <> AcceptedRouteCount then
            invalidOp "The BLOKEMON-136 route ledger is missing, stale, duplicated, or borrowed."

        if
            selectedIds <> acceptedObligationIds
            || acceptedObligationIds.Count <> AcceptedObligationCount
        then
            invalidOp
                "The BLOKEMON-136 obligation ledger is missing, stale, duplicated, or borrowed."

        if duplicateIds.Length <> 0 then
            invalidOp
                $"The BLOKEMON-136 obligation ledger contains duplicate identities: {String.Join(',', duplicateIds)}."

        selected

    let materialize authority inventory =
        reconcile inventory
        |> Array.map (fun input ->
            { Input = input
              InitialState = ReferenceDeterministicPrograms.materializeInput authority input })

    let legalActions = ReferenceDeterministicPrograms.legalActions
    let selectAction = ReferenceDeterministicPrograms.selectAction
    let apply = ReferenceDeterministicPrograms.apply
    let legalResolutionAction = ReferenceDeterministicPrograms.legalResolutionAction
    let selectResolution = ReferenceDeterministicPrograms.selectResolution
    let resolveEffectChoice = ReferenceDeterministicPrograms.resolveEffectChoice
