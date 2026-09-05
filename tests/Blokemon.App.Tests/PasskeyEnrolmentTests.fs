namespace Blokemon.App.Tests

open Blokemon.App
open Blokemon.Product
open FsUnit
open TUnit.Core

type PasskeyEnrolmentTests() =

    [<Test>]
    member _.``each provenance should be allowed to enrol exactly as the contract states``() =
        let allowed generates =
            DomainResult.Succeeded { GeneratesCodes = generates }
            : DomainResult<EnrolmentGrant, EnrolmentFailure>

        let refused: DomainResult<EnrolmentGrant, EnrolmentFailure> =
            DomainResult.Failed EnrolmentFailure.ProvenanceRefused

        let cases =
            [ SessionProvenance.FirstParty, false, false, allowed true
              SessionProvenance.FirstParty, true, true, allowed false
              SessionProvenance.FirstParty, true, false, allowed false
              SessionProvenance.Recovery, false, false, allowed true
              SessionProvenance.Recovery, true, true, allowed true
              SessionProvenance.Issuer, false, false, allowed true
              SessionProvenance.Issuer, true, false, refused
              SessionProvenance.Issuer, false, true, refused
              SessionProvenance.Issuer, true, true, refused ]

        for provenance, hasCredential, hasLiveCodes, expected in cases do
            PasskeyEnrolment.authorize provenance hasCredential hasLiveCodes
            |> should equal expected

    [<Test>]
    member _.``only a first party session should make new recovery codes``() =
        PasskeyEnrolment.mayRegenerate SessionProvenance.FirstParty |> should be True
        PasskeyEnrolment.mayRegenerate SessionProvenance.Recovery |> should be False
        PasskeyEnrolment.mayRegenerate SessionProvenance.Issuer |> should be False
