namespace Blokemon.App.Tests

open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type TenantTests() =

    [<Test>]
    member _.``ensuring the default tenant should create it once and find it by slug``() =
        task {
            let documents = MemoryDocumentStore()

            let! first = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>
            let! second = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>

            let! found =
                Tenants.findBySlug documents documents Tenants.DefaultSlug Unchecked.defaultof<_>

            second |> should equal first
            found |> should equal (Some first)
            first.Slug |> should equal Tenants.DefaultSlug.Value
            first.Status |> should equal TenantStatus.Active
            first.RegisteredParentOrigin |> should be Null
            keysUnder documents "tenant/" |> should equal [ $"tenant/{first.Id}" ]
        }

    [<Test>]
    member _.``an unknown slug should not be found``() =
        task {
            let documents = MemoryDocumentStore()
            let! _ = Tenants.ensureDefault documents documents now Unchecked.defaultof<_>

            let slug =
                match TenantSlug.Create "nobody" with
                | DomainResult.Succeeded slug -> slug
                | DomainResult.Failed failure -> failwith $"{failure}"

            let! found = Tenants.findBySlug documents documents slug Unchecked.defaultof<_>

            found |> should equal None
        }
