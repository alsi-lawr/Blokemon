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
            (StringComparer.Ordinal.Equals(
                manifest.ManifestVersion,
                "base-jungle-fossil-1999-card-semantics-candidate.2"
            ))
            "runtime.manifest-version"
            "The runtime manifest must use the complete 1999 card-semantics load identity."
            issues

        check
            (manifest.Collectibles.Length = 151)
            "runtime.collectible-count"
            "The runtime manifest must contain exactly 151 collectible identities."
            issues

        check
            (manifest.Kits.Length = 32)
            "runtime.kit-count"
            "The runtime manifest must account for all 32 selected vintage Trainer identities."
            issues

        check
            (manifest.BasicVim.Length = 7)
            "runtime.vim-count"
            "The runtime manifest must contain the six Basic Energy cards and Double Colorless Energy."
            issues

        BlokemonSetContentValidator.validate manifest issues
        BlokemonProductValidator.validate manifest issues
        BlokemonBaseRulesValidator.validate manifest issues
        { Issues = issues.ToArray() }
