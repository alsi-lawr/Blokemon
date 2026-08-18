namespace Blokemon.App

open System
open System.Collections.Generic
open System.Linq
open System.Threading
open Blokemon.App.Contracts
open Blokemon.App.ProfileProjection
open Blokemon.Product

/// The one ApplicationView every operation returns: the profile, its cards and decks, the last
/// pack, and the saved battle as it stands after the operation.
module internal ApplicationViewAssembly =

    let toView
        (context: ApplicationContext)
        (loaded: LoadedProfile | null)
        (cancellationToken: CancellationToken)
        (knownMatch: MatchServiceResult | null)
        =
        let catalogue = context.Catalogue
        let matches = context.Matches
        let currentCard = currentCard catalogue
        let deckView = deckView catalogue
        let starterViews = starterViews catalogue

        task {
            match loaded with
            | null ->
                let emptyCards = catalogue.CardsWithOwnership(Dictionary<string, int>())

                return
                    ApplicationView(
                        null,
                        emptyCards,
                        Array.empty,
                        starterViews (HashSet<string>(StringComparer.Ordinal)) emptyCards,
                        catalogue.PackPresentation,
                        null,
                        null,
                        null
                    )
            | profile ->
                let ownership = Dictionary<string, int>(StringComparer.Ordinal)

                for entry in profile.Profile.CollectibleOwnership do
                    ownership.Add(entry.Key.Value, entry.Value)

                let currentCards = Dictionary<string, CardView>(StringComparer.Ordinal)

                for card in catalogue.Cards do
                    currentCards.Add(card.Id, card)

                let cards =
                    currentCards.Keys
                        .Concat(ownership.Keys)
                        .Concat(
                            profile.Profile.SavedDecks.Values
                            |> Seq.collect (fun deck -> deck.Cards.Keys |> Seq.map _.Value)
                        )
                        .Concat(
                            profile.Profile.PackReceipts.Values
                            |> Seq.collect (fun receipt ->
                                receipt.SampledCollectibleIds |> Seq.map _.Value)
                        )
                        .Distinct(StringComparer.Ordinal)
                        .Select(fun id -> currentCard id ownership currentCards)
                        .OrderBy(fun card -> card.Kind)
                        .ThenBy((fun card -> card.Id), StringComparer.Ordinal)
                        .ToArray()

                let decks =
                    profile.Profile.SavedDecks.Values
                        .OrderBy(fun deck -> deck.Name.Value)
                        .Select(fun deck ->
                            deckView profile.Profile deck profile.Ids.Decks[deck.Id])
                        .ToArray()

                let lastPack =
                    profile.Profile.PackReceipts.Values
                        .OrderByDescending(fun receipt -> receipt.Sequence)
                        .Select(fun receipt ->
                            PackReceiptView(
                                profile.Ids.PackReceipts[receipt.Id],
                                receipt.Sequence,
                                receipt.SampledCollectibleIds
                                |> Seq.map (fun id -> currentCard id.Value ownership currentCards)
                                |> Seq.toArray
                            ))
                        .FirstOrDefault()

                let! resolvedMatch =
                    task {
                        match knownMatch with
                        | null ->
                            return!
                                matches.State(
                                    profile.Profile,
                                    profile.Profile.DisplayName.Value,
                                    cancellationToken
                                )
                        | known -> return known
                    }

                return
                    ApplicationView(
                        ProfileView(
                            profile.Ids.Profile,
                            profile.Profile.DisplayName.Value,
                            profile.Revision,
                            (match profile.Profile.LatestStarterDeckClaim with
                             | null -> null
                             | claim -> claim.Id.Value),
                            profile.Profile.RemainingPackAllowance,
                            (let remaining = profile.Profile.RemainingStarterDeckClaimAllowance

                             if remaining.HasValue then
                                 Nullable(remaining.Value = 0)
                             else
                                 Nullable())
                        ),
                        cards,
                        decks,
                        starterViews
                            (profile.Profile.StarterDeckClaims
                             |> Seq.map _.Id.Value
                             |> fun ids -> HashSet<string>(ids, StringComparer.Ordinal))
                            cards,
                        catalogue.PackPresentation,
                        lastPack,
                        resolvedMatch.View,
                        resolvedMatch.Error
                    )
        }
