namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.Product

/// One account or tenant as a listing shows it: its identifier, status and timestamps from the
/// store's summary projection, and nothing from the document itself.
type LifecycleSummary =
    { Id: string
      Status: string | null
      CreatedAt: Nullable<DateTimeOffset>
      ErasedAt: Nullable<DateTimeOffset> }

/// One approval record of a tenant as its owner's listing shows it.
type ApprovalSummary =
    { Account: string
      Status: string | null
      ApprovedAt: Nullable<DateTimeOffset>
      ExcludedAt: Nullable<DateTimeOffset> }

/// The operator's and the tenant owner's listings, built on the key-prefix listing and its
/// declared projections: identifiers from the keys, status and timestamps from the projection,
/// never a document body.
module Listings =

    let private lifecycle (prefix: string) (summary: DocumentSummary) : LifecycleSummary =
        let id = summary.Key.Substring prefix.Length

        match summary.Projection with
        | :? DocumentProjection.Lifecycle as projection ->
            { Id = id
              Status = projection.Status
              CreatedAt = Option.toNullable (Option.ofNullable projection.CreatedAt)
              ErasedAt = Option.toNullable (Option.ofNullable projection.ErasedAt) }
        | _ ->
            { Id = id
              Status = null
              CreatedAt = Nullable()
              ErasedAt = Nullable() }

    let accounts (listing: IDocumentListing) (cancellationToken: CancellationToken) =
        task {
            let! summaries = listing.List("account/", cancellationToken)
            return summaries |> Seq.map (lifecycle "account/") |> List.ofSeq
        }

    let tenants (listing: IDocumentListing) (cancellationToken: CancellationToken) =
        task {
            let! summaries = listing.List("tenant/", cancellationToken)
            return summaries |> Seq.map (lifecycle "tenant/") |> List.ofSeq
        }

    /// The tenant's own approval records and no other tenant's: the key names the tenant last.
    let approvalsOf
        (listing: IDocumentListing)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<ApprovalSummary list> =
        task {
            let! summaries = listing.List("approval/", cancellationToken)
            let suffix = $"/{tenant}"

            return
                summaries
                |> Seq.filter (fun summary ->
                    summary.Key.EndsWith(suffix, StringComparison.Ordinal))
                |> Seq.map (fun summary ->
                    let account =
                        summary.Key.Substring(
                            "approval/".Length,
                            summary.Key.Length - "approval/".Length - suffix.Length
                        )

                    match summary.Projection with
                    | :? DocumentProjection.Approval as projection ->
                        { Account = account
                          Status = projection.Status
                          ApprovedAt = Option.toNullable (Option.ofNullable projection.ApprovedAt)
                          ExcludedAt = Option.toNullable (Option.ofNullable projection.ExcludedAt) }
                    | _ ->
                        { Account = account
                          Status = null
                          ApprovedAt = Nullable()
                          ExcludedAt = Nullable() })
                |> List.ofSeq
        }
