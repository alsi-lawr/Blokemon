namespace Blokemon.App

open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.Product

type internal ApplicationContext =
    { Catalogue: BlokemonCatalogue
      Documents: IStateDocumentStore
      Matches: LocalMatchService
      Economy: EconomyRules
      ProfileAuthorityPolicy: ProfileAuthorityPolicy
      Projections: ApplicationProjectionCache
      ProjectionRequest: ApplicationProjectionRequest }
