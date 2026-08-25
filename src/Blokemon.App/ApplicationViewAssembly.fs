namespace Blokemon.App

open System
open System.Collections.Generic
open System.Linq
open System.Threading
open System.Threading.Tasks
open Blokemon.App.ApplicationProjectionIdentity
open Blokemon.App.Catalogue
open Blokemon.App.Contracts
open Blokemon.App.ProfileProjection
open Blokemon.Product

/// The one ApplicationView every operation returns: the profile, its cards and decks, the last
/// pack, and the saved battle as it stands after the operation.
module internal ApplicationViewAssembly =

    let private noProfile =
        { Summary = "profile:none"
          Cards = "cards:none"
          Decks = "decks:none"
          StarterDecks = "starters:none"
          LastPack = "pack:none"
          MatchProfile = "match-profile:none" }

    let private emptyMatch =
        { View = null
          Error = null
          Recovery = null
          Presentation = null
          DocumentIdentity = MatchFailures.noDocumentProjection }

    let private compareCardId (left: CardId) (right: CardId) =
        String.CompareOrdinal(left.Value, right.Value)

    let private profileIdentities (catalogue: BlokemonCatalogue) (loaded: LoadedProfile) =
        let profile = loaded.Profile

        let ownership =
            content (fun target ->
                profile.CollectibleOwnership
                |> Seq.sortWith (fun left right -> compareCardId left.Key right.Key)
                |> Seq.iter (fun entry ->
                    appendString target entry.Key.Value
                    appendInt target entry.Value))

        let receiptCards =
            content (fun target ->
                profile.PackReceipts.Values
                |> Seq.collect _.SampledCollectibleIds
                |> Seq.map _.Value
                |> Seq.distinct
                |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
                |> Seq.iter (appendString target))

        let deckCardIds =
            content (fun target ->
                profile.SavedDecks.Values
                |> Seq.collect _.Cards.Keys
                |> Seq.map _.Value
                |> Seq.distinct
                |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
                |> Seq.iter (appendString target))

        let decks =
            content (fun target ->
                profile.SavedDecks.Values
                |> Seq.sortWith (fun left right ->
                    String.CompareOrdinal(left.Id.Value, right.Id.Value))
                |> Seq.iter (fun deck ->
                    appendString target deck.Id.Value
                    appendString target (loaded.Ids.Decks[deck.Id].ToString "D")
                    appendString target deck.Name.Value
                    appendInt64 target deck.Revision.Value

                    deck.Cards
                    |> Seq.sortWith (fun left right -> compareCardId left.Key right.Key)
                    |> Seq.iter (fun entry ->
                        appendString target entry.Key.Value
                        appendInt target entry.Value
                        appendInt target (profile.OwnedCollectibleQuantity entry.Key))))

        let starterClaims =
            content (fun target ->
                let leaderIds =
                    catalogue.StarterDecks.Decks |> Seq.map _.LeaderCardId |> HashSet<string>

                profile.CollectibleOwnership
                |> Seq.filter (fun entry -> leaderIds.Contains entry.Key.Value)
                |> Seq.sortWith (fun left right -> compareCardId left.Key right.Key)
                |> Seq.iter (fun entry ->
                    appendString target entry.Key.Value
                    appendInt target entry.Value)

                profile.StarterDeckClaims
                |> Seq.map _.Id.Value
                |> Seq.distinct
                |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
                |> Seq.iter (appendString target))

        let lastReceipt =
            profile.PackReceipts.Values
                .OrderByDescending(fun receipt -> receipt.Sequence)
                .FirstOrDefault()

        let lastPack =
            match lastReceipt with
            | null -> "pack:none"
            | receipt ->
                content (fun target ->
                    appendString target receipt.Id.Value
                    appendString target (loaded.Ids.PackReceipts[receipt.Id].ToString "D")
                    appendInt target receipt.Sequence

                    receipt.SampledCollectibleIds
                    |> Seq.iter (fun id ->
                        appendString target id.Value
                        appendInt target (profile.OwnedCollectibleQuantity id)))

        let summary =
            content (fun target ->
                appendInt64 target loaded.Revision
                appendString target (loaded.Ids.Profile.ToString "D")
                appendString target profile.DisplayName.Value

                match profile.LatestStarterDeckClaim with
                | null -> appendString target null
                | claim -> appendString target claim.Id.Value

                if profile.RemainingPackAllowance.HasValue then
                    appendInt target profile.RemainingPackAllowance.Value
                else
                    appendString target null

                if profile.RemainingStarterDeckClaimAllowance.HasValue then
                    appendInt target profile.RemainingStarterDeckClaimAllowance.Value
                else
                    appendString target null)

        let matchProfile =
            content (fun target ->
                appendString target profile.Id.Value
                appendString target profile.DisplayName.Value
                appendString target profile.BoundAuthorityManifestVersion)

        { Summary = summary
          Cards = combine [ ownership; deckCardIds; receiptCards ]
          Decks = if profile.SavedDecks.Count = 0 then "decks:none" else decks
          StarterDecks = starterClaims
          LastPack = lastPack
          MatchProfile = matchProfile }

    let private resolveMatch
        (context: ApplicationContext)
        (profile: LoadedProfile)
        (cancellationToken: CancellationToken)
        (knownMatch: MatchProjectionResult | null)
        =
        task {
            let operation =
                ApplicationProjectionMatrix.operation context.ProjectionRequest.Operation

            match operation.MatchSource, knownMatch with
            | MatchProjectionSource.NoMatch, _ -> return emptyMatch
            | MatchProjectionSource.UseCommittedMatch, NonNull known -> return known
            | _ ->
                return!
                    context.Matches.StateProjection(
                        profile.Profile,
                        profile.Profile.DisplayName.Value,
                        cancellationToken
                    )
        }

    let toView
        (context: ApplicationContext)
        (loaded: LoadedProfile | null)
        (cancellationToken: CancellationToken)
        (knownMatch: MatchProjectionResult | null)
        =
        let catalogue = context.Catalogue
        let currentCard = currentCard catalogue
        let deckView = deckView catalogue
        let starterViews = starterViews catalogue

        task {
            let identityResult =
                match loaded with
                | null ->
                    { Identities = noProfile
                      Publication = ClearProfileProjectionIdentities }
                | profile ->
                    context.Projections.ProfileIdentities(
                        profile.Revision,
                        profile.ContentIdentity,
                        (fun () -> profileIdentities catalogue profile),
                        cancellationToken
                    )

            let identities = identityResult.Identities

            let! resolvedMatch =
                match loaded with
                | null -> Task.FromResult emptyMatch
                | profile -> resolveMatch context profile cancellationToken knownMatch

            let keys =
                { Catalogue = context.Projections.CatalogueIdentity
                  ProfileSummary = identities.Summary
                  Cards = identities.Cards
                  Decks = identities.Decks
                  StarterDecks = identities.StarterDecks
                  LastPack = identities.LastPack
                  MatchProfile = identities.MatchProfile
                  MatchDocument = matchDocument resolvedMatch }

            let builders =
                match loaded with
                | null ->
                    let cards = lazy (catalogue.CardsWithOwnership(Dictionary<string, int>()))

                    { Profile = fun () -> null
                      Cards = fun () -> cards.Value
                      Decks = fun () -> Array.empty
                      StarterDecks =
                        fun () -> starterViews (HashSet<string>(StringComparer.Ordinal)) cards.Value
                      PackPresentation = fun () -> catalogue.PackPresentation
                      LastPack = fun () -> null
                      Match = fun () -> null
                      MatchError = fun () -> null
                      MatchRecovery = fun () -> null }
                | profile ->
                    let ownership =
                        lazy
                            (let values = Dictionary<string, int>(StringComparer.Ordinal)

                             for entry in profile.Profile.CollectibleOwnership do
                                 values.Add(entry.Key.Value, entry.Value)

                             values)

                    let currentCards =
                        lazy
                            (let values = Dictionary<string, CardView>(StringComparer.Ordinal)

                             for card in catalogue.Cards do
                                 values.Add(card.Id, card)

                             values)

                    let cards =
                        lazy
                            (currentCards.Value.Keys
                                .Concat(ownership.Value.Keys)
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
                                .Select(fun id -> currentCard id ownership.Value currentCards.Value)
                                .OrderBy(fun card -> card.Kind)
                                .ThenBy((fun card -> card.Id), StringComparer.Ordinal)
                                .ToArray())

                    { Profile =
                        fun () ->
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
                            )
                      Cards = fun () -> cards.Value
                      Decks =
                        fun () ->
                            profile.Profile.SavedDecks.Values
                                .OrderBy(fun deck -> deck.Name.Value)
                                .Select(fun deck ->
                                    deckView profile.Profile deck profile.Ids.Decks[deck.Id])
                                .ToArray()
                      StarterDecks =
                        fun () ->
                            starterViews
                                (profile.Profile.StarterDeckClaims
                                 |> Seq.map _.Id.Value
                                 |> fun ids -> HashSet<string>(ids, StringComparer.Ordinal))
                                cards.Value
                      PackPresentation = fun () -> catalogue.PackPresentation
                      LastPack =
                        fun () ->
                            profile.Profile.PackReceipts.Values
                                .OrderByDescending(fun receipt -> receipt.Sequence)
                                .Select(fun receipt ->
                                    PackReceiptView(
                                        profile.Ids.PackReceipts[receipt.Id],
                                        receipt.Sequence,
                                        receipt.SampledCollectibleIds
                                        |> Seq.map (fun id ->
                                            currentCard
                                                id.Value
                                                ownership.Value
                                                currentCards.Value)
                                        |> Seq.toArray
                                    ))
                                .FirstOrDefault()
                      Match = fun () -> resolvedMatch.View
                      MatchError = fun () -> resolvedMatch.Error
                      MatchRecovery = fun () -> resolvedMatch.Recovery }

            return!
                context.Projections.Assemble(
                    context.ProjectionRequest,
                    keys,
                    builders,
                    identityResult.Publication,
                    cancellationToken
                )
        }
