namespace Blokemon.App

open System
open System.Collections.Generic
open Blokemon.App.Catalogue
open Blokemon.App.Contracts

/// What a signed-out caller of the server sees: the catalogue with nothing owned, no profile,
/// no decks and no battle. The same shape every operation returns, with the account-scoped
/// slots empty.
module SignedOutView =

    let build (catalogue: BlokemonCatalogue) : ApplicationView =
        let cards = catalogue.CardsWithOwnership(Dictionary<string, int>())

        ApplicationView(
            null,
            cards,
            Array.empty,
            ProfileProjection.starterViews catalogue (HashSet<string>(StringComparer.Ordinal)) cards,
            catalogue.PackPresentation,
            null,
            null,
            null
        )
