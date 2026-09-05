namespace Blokemon.App

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// Tenant records as the server reads them: by id, by slug, and the default tenant that the
/// public Blokemon at `/` is.
module Tenants =

    /// The default tenant's slug. It is a well-formed, unreserved slug, so a channel cannot be
    /// admitted under it once the default tenant holds it.
    let DefaultSlug =
        match TenantSlug.Create "core" with
        | DomainResult.Succeeded slug -> slug
        | DomainResult.Failed _ -> failwith "The default slug is well formed."

    [<Literal>]
    let DefaultLabel = "Blokemon"

    let private parse (document: StoredDocument) : TenantDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<TenantDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when value.SchemaVersion = tenantSchemaVersion -> Some value
        | _ -> None

    let read
        (documents: IStateDocumentStore)
        (tenant: TenantId)
        (cancellationToken: CancellationToken)
        : Task<TenantDocument option> =
        task {
            let! stored = documents.Read(tenantKey tenant, cancellationToken)

            match stored with
            | null -> return None
            | document -> return parse document
        }

    /// Every readable tenant record, in key order.
    let all
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (cancellationToken: CancellationToken)
        : Task<TenantDocument list> =
        task {
            let! summaries = listing.List("tenant/", cancellationToken)
            let mutable tenants = []

            for summary in summaries do
                let! stored = documents.Read(summary.Key, cancellationToken)

                match stored with
                | null -> ()
                | document ->
                    match parse document with
                    | Some tenant -> tenants <- tenant :: tenants
                    | None -> ()

            return List.rev tenants
        }

    let findBySlug
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (slug: TenantSlug)
        (cancellationToken: CancellationToken)
        : Task<TenantDocument option> =
        task {
            let! tenants = all documents listing cancellationToken

            return
                tenants
                |> List.tryFind (fun tenant ->
                    String.Equals(tenant.Slug, slug.Value, StringComparison.Ordinal))
        }

    /// The default tenant, created on first start-up with its issuer slots empty; admission
    /// populates them later without redefining the record.
    let ensureDefault
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<TenantDocument> =
        task {
            let! existing = findBySlug documents listing DefaultSlug cancellationToken

            match existing with
            | Some tenant -> return tenant
            | None ->
                let id = TenantId.Mint()
                let tenant = newTenant id DefaultSlug DefaultLabel now

                let! write =
                    documents.Create(
                        tenantKey id,
                        JsonSerializer.Serialize(tenant, json),
                        cancellationToken
                    )

                match write with
                | :? DocumentWriteResult.Written -> return tenant
                | _ ->
                    let! raced = findBySlug documents listing DefaultSlug cancellationToken

                    match raced with
                    | Some tenant -> return tenant
                    | None ->
                        return
                            raise (
                                InvalidOperationException "The default tenant could not be created."
                            )
        }
