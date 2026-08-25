namespace Blokemon.Product

open System
open System.Collections.Generic
open Blokemon.Core.SetDesign

/// Checks deck selections against the mechanical authority and what a profile owns.
module DeckValidator =

    /// Validates deck selections for a profile against the current authority.
    let Validate
        (profile: LocalProfile)
        (authority: BlokemonRuntimeManifest)
        (selections: IEnumerable<DeckCardSelection>)
        =
        ArgumentNullException.ThrowIfNull(profile, nameof profile)
        DeckRules.validate profile.OwnedCollectibleQuantity authority selections
