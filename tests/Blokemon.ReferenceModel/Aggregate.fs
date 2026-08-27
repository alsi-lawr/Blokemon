namespace Blokemon.ReferenceModel

open System

type ReferenceAggregateObligation =
    | Deterministic of ReferenceDeterministicObligation
    | Branching of ReferenceBranchingObligation
    | Lifecycle of ReferenceLifecycleObligation
    | SpecializedTrigger of ReferenceSpecializedTriggerObligation

type ReferenceAggregate =
    { Authority: ReferenceAuthority
      Obligations: ReferenceAggregateObligation array
      RouteIdentities: Set<string> }

[<RequireQualifiedAccess>]
module ReferenceAggregate =

    let input obligation =
        match obligation with
        | Deterministic value -> value.Input
        | Branching value -> value.Input
        | Lifecycle value -> value.Input
        | SpecializedTrigger value -> value.Input

    let initialState obligation =
        match obligation with
        | Deterministic value -> value.InitialState
        | Branching value -> value.InitialState
        | Lifecycle value -> value.InitialState
        | SpecializedTrigger value -> value.InitialState

    let runner obligation =
        match obligation with
        | Deterministic _ -> "deterministic"
        | Branching _ -> "branching"
        | Lifecycle _ -> "lifecycle"
        | SpecializedTrigger _ -> "specialized-trigger"

    let private duplicates values =
        values
        |> Seq.countBy id
        |> Seq.choose (fun (value, count) -> if count = 1 then None else Some value)
        |> Seq.toArray

    let load authorityPath obligationPath =
        let authority = ReferenceAuthority.load authorityPath
        let inventory = ReferenceObligations.load authority obligationPath

        let obligations =
            [| ReferenceDeterministicPrograms.materialize authority inventory
               |> Array.map Deterministic
               ReferenceBranchingPrograms.materialize authority inventory
               |> Array.map Branching
               ReferenceLifecyclePrograms.materialize authority inventory
               |> Array.map Lifecycle
               ReferenceSpecializedTriggerPrograms.materialize authority inventory
               |> Array.map SpecializedTrigger |]
            |> Array.concat
            |> Array.sortBy (input >> _.Id)

        let obligationIds = obligations |> Array.map (input >> _.Id)
        let inventoryIds = inventory.Obligations |> Seq.map _.Id |> Set.ofSeq
        let aggregateIds = obligationIds |> Set.ofArray
        let duplicateIds = duplicates obligationIds

        if
            obligations.Length <> ReferenceObligations.ObligationCount
            || aggregateIds.Count <> ReferenceObligations.ObligationCount
            || aggregateIds <> inventoryIds
            || duplicateIds.Length <> 0
        then
            invalidOp
                $"The aggregate obligation dispatcher did not partition all {ReferenceObligations.ObligationCount} reviewed inputs exactly once (duplicates={String.Join(',', duplicateIds)})."

        let routeIdentities =
            obligations |> Seq.map (input >> _.InitialState.Route.Value) |> Set.ofSeq

        let reviewedRoutes = inventory.RouteIdentities |> Set.map _.Value

        if
            routeIdentities.Count <> ReferenceObligations.RouteIdentityCount
            || routeIdentities <> reviewedRoutes
        then
            invalidOp
                $"The aggregate obligation dispatcher did not partition all {ReferenceObligations.RouteIdentityCount} reviewed routes."

        { Authority = authority
          Obligations = obligations
          RouteIdentities = routeIdentities }
