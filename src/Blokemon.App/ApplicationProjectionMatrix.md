# Complete application projection invalidation matrix

The projection cache always returns one complete `ApplicationView`. Reuse is decided from the
source identities below, not from an operation name. This means an idempotent operation reuses
unchanged segments, while an external write seen during any operation invalidates every segment
whose actual source identity changed.

| `ApplicationView` field | Executable source dependencies | Rebuilt when |
| --- | --- | --- |
| `Profile` | `ProfileSummary` | The profile document revision, public profile id, display name, latest starter claim, or either remaining allowance changes. |
| `Cards` | `Catalogue`, `CardUniverseAndOwnership` | The catalogue, ownership, or the distinct card-id universe in ownership, saved decks, or pack receipts changes. |
| `Decks` | `Catalogue`, `SavedDecksAndOwnership` | The catalogue, a saved deck/id/revision/entry, or ownership of a card used by a saved deck changes. Empty decks do not depend on unrelated ownership. |
| `StarterDecks` | `Catalogue`, `StarterClaimsAndOwnership` | The catalogue, claimed starter ids, or ownership of a starter leader changes. |
| `PackPresentation` | `Catalogue` | The catalogue/bootstrap presentation changes. |
| `LastPack` | `Catalogue`, `PackHistoryAndOwnership` | The catalogue, latest receipt/id/sequence/order, or ownership of a card in that receipt changes. No receipt does not depend on unrelated ownership. |
| `Match` | `Catalogue`, `MatchProfile`, `MatchDocument` | The catalogue, profile match identity/display name/authority, saved match document revision/content, or match error changes. |
| `MatchError` | `Catalogue`, `MatchProfile`, `MatchDocument` | The same match sources change, including an error appearing, changing, or clearing. |

`ApplicationProjectionMatrix.fields` is the table used by
`ApplicationProjectionCache.sameSources`; it is the executable authority for field reuse.

| Application path | Owned source changes after a successful commit | Match source |
| --- | --- | --- |
| `State` | None; it can observe external catalogue, profile, or match changes | Load saved match |
| `CreateProfile` | `ProfileSummary`; the new profile also changes the profile-derived source identities from `none` | Load saved match |
| `OpenPack` | `ProfileSummary`, collection ownership/universe, deck legality ownership, starter-leader ownership, latest-pack history/ownership | Load saved match |
| `ClaimStarterDeck` | `ProfileSummary`, collection ownership/universe, saved decks/legality, starter claims/leader ownership, and an existing latest pack's sampled-card ownership | Load saved match |
| `SaveDeck` | `ProfileSummary`, saved decks/legality, and collection card universe when historical-only ids enter or leave | Load saved match |
| `DeleteDeck` | `ProfileSummary`, saved decks/legality, and collection card universe when historical-only ids leave | Load saved match |
| `StartMatch` | `MatchDocument` | Use the committed match result |
| `ApplyMatchAction` | `MatchDocument` | Use the committed match result |
| `PurgeData` | All profile and match source identities return to `none`; catalogue presentation remains reusable | No match |
| External profile revision/content | Exactly the profile-derived identities whose content changed | Load saved match |
| External match revision/content | `MatchDocument` and therefore `Match`/`MatchError` | Load saved match |
| Catalogue/authority replacement | Every field carrying a `Catalogue` dependency; compatible profile migration separately changes `ProfileSummary` | Service restart supplies the replacement catalogue |
| Failed, cancelled, CAS-conflicted, or idempotent operation | None unless a complete successful projection observes a real external source change | No partial cache publication |

`ApplicationProjectionMatrix.operations` supplies the match-source choice used by
`ApplicationViewAssembly.resolveMatch` and records the owned source domains for each operation.
Actual source identities remain the final invalidation authority so retries and cross-tab writes are
both handled without speculative invalidation.
