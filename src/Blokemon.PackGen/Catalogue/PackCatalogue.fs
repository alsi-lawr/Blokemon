namespace Blokemon.PackGen.Catalogue

open System.Collections.Immutable
open Blokemon.PackGen.Domain

/// The approved packaging objects.
module PackCatalogue =

    /// The approved packaging objects.
    let All: ImmutableArray<Pack> =
        // Delays are spread across the 5.5s glint cycle so no two wrappers on the sheet flash
        // together, which reads as a synchronised page effect rather than as light on film.
        ImmutableArray.CreateRange
            [ { Key = PackKey.Booster
                Format = PackFormat.Wrapper(size = WrapperSize.Booster, resealable = false)
                Contents =
                  { Count = "11 cards"
                    Declaration = "11 additional game cards"
                    NotForResale = true }
                Glint = GlintDelay.create 0.0
                FixedMaterial = None }
              { Key = PackKey.StarterDeck
                Format = PackFormat.Carton
                Contents =
                  { Count = "60 cards"
                    Declaration = "60 cards \u00b7 rulebook"
                    NotForResale = false }
                Glint = GlintDelay.create 0.0
                FixedMaterial = None }
              { Key = PackKey.OneForTheRoad
                Format = PackFormat.Wrapper(size = WrapperSize.Small, resealable = false)
                Contents =
                  { Count = "1 card"
                    Declaration = "1 additional game card"
                    NotForResale = true }
                Glint = GlintDelay.create 0.8
                FixedMaterial = None }
              { Key = PackKey.RoundOfThree
                Format = PackFormat.Wrapper(size = WrapperSize.Small, resealable = false)
                Contents =
                  { Count = "3 cards"
                    Declaration = "3 additional game cards"
                    NotForResale = true }
                Glint = GlintDelay.create 2.7
                FixedMaterial = None }
              // The premium pull prints gold whatever stock the deployment chose. The colourway
              // is part of the fixed design, not a third stock a deployment can select.
              { Key = PackKey.LockIn
                Format = PackFormat.Wrapper(size = WrapperSize.Small, resealable = false)
                Contents =
                  { Count = "1 holo"
                    Declaration = "1 additional game card"
                    NotForResale = true }
                Glint = GlintDelay.create 1.2
                FixedMaterial = Some PackMaterial.Gold }
              { Key = PackKey.Session
                Format = PackFormat.Wrapper(size = WrapperSize.Small, resealable = true)
                Contents =
                  { Count = "3 boosters"
                    Declaration = "33 additional game cards"
                    NotForResale = true }
                Glint = GlintDelay.create 3.4
                FixedMaterial = None } ]

    /// One approved packaging object.
    let Get (key: PackKey) =
        All |> Seq.find (fun pack -> pack.Key = key)
