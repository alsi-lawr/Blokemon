namespace Blokemon.Core.SetDesign

open System
open System.Collections.Generic

/// Owned validation of the mechanical authority.
module BlokemonSetValidator =

    let private check = BlokemonValidation.check

    /// Validates the mechanical authority against the rules this repository owns.
    let ValidateRuntime (manifest: BlokemonRuntimeManifest) =
        ArgumentNullException.ThrowIfNull(manifest, nameof manifest)
        let issues = ResizeArray<BlokemonValidationIssue>()

        check
            (manifest.PresentationStatus = BlokemonPresentationStatus.Accepted)
            "runtime.presentation"
            "Presentation must carry exact human acceptance."
            issues

        check
            (manifest.Collectibles.Length = 151)
            "runtime.collectible-count"
            "The runtime manifest must contain exactly 151 collectible identities."
            issues

        check
            (manifest.Kits.Length = 14)
            "runtime.kit-count"
            "The runtime manifest must contain exactly 14 fixed kit definitions."
            issues

        check
            (manifest.BasicVim.Length = 7)
            "runtime.vim-count"
            "The runtime manifest must contain exactly seven Basic Vim definitions."
            issues

        BlokemonSetContentValidator.validate manifest issues
        BlokemonBaseRulesValidator.validate manifest issues
        { Issues = issues.ToArray() }
