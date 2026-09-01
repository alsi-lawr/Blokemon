namespace Blokemon.Core.PublicContent

open System.Text.Json.Serialization
open Blokemon.Core.SetDesign

type BlokemonPublicContentApprovalStatus =
    | Accepted = 0

type BlokemonPublicTermCategory =
    | Type = 0
    | Stage = 1
    | Category = 2
    | Status = 3
    | Target = 4
    | Choice = 5
    | Quantity = 6
    | Cost = 7
    | Timing = 8
    | Core = 9

type BlokemonPublicTerm =
    { Id: string
      Category: BlokemonPublicTermCategory
      Singular: string
      Plural: string
      Definition: string }

/// One of the three ragged-JSON contract types; see the note on BlokemonEffectInstruction.
[<CLIMutable>]
type BlokemonPublicEffect =
    { MechanicalId: string
      Name: string

      [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      EffectText: string | null }

type BlokemonPublicIllustration =
    { Brief: string
      Prompt: string
      AltIntent: string }

type BlokemonPublicArtStatus =
    | Accepted = 0

type BlokemonPublicArtReference =
    { Status: BlokemonPublicArtStatus
      Authority: string }

type BlokemonPublicCollectible =
    { Id: string
      ApprovedName: string
      ApprovedType: BlokemonApprovedType
      FlavourText: string
      PokemonPowers: BlokemonPublicEffect array
      Attacks: BlokemonPublicEffect array
      Rules: BlokemonPublicEffect array
      Illustration: BlokemonPublicIllustration
      Art: BlokemonPublicArtReference }

type BlokemonPublicTrainer =
    { Id: string
      Name: string
      Effects: BlokemonPublicEffect array }

type BlokemonPublicEnergy =
    { Id: string
      Name: string
      Definition: string
      SymbolKey: string
      AccessibleLabel: string }

type BlokemonPublicContentManifest =
    { SchemaVersion: string
      ContentVersion: string
      MechanicalManifestVersion: string
      TerminologyVersion: string
      HumanApprovalStatus: BlokemonPublicContentApprovalStatus
      Terminology: BlokemonPublicTerm array
      Collectibles: BlokemonPublicCollectible array
      Trainers: BlokemonPublicTrainer array
      Energy: BlokemonPublicEnergy array }

type BlokemonPublicContentIssue = { Code: string; Message: string }

type BlokemonPublicContentValidation =
    { Issues: BlokemonPublicContentIssue array }

    member this.IsValid = this.Issues.Length = 0
