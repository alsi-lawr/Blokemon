# Complete application projection invalidation matrix

The executable authority is `ApplicationProjectionMatrix.fields` and
`ApplicationProjectionMatrix.operations` in `ApplicationProjectionModel.fs`. The tables below use
those exact enum and flag names; they are the review view of the same rows, not another production
configuration. Focused integration tests execute every application operation and each observable
external-change class against the change plan produced from those rows.

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

`ApplicationProjectionCache.changePlan` uses the selected operation row's `OwnedChanges` to choose
the source identities evaluated as operation-owned, then evaluates the complementary identities as
external. It combines only identities whose content actually differs into
`InvalidatedDependencies`, which selects cached templates or rebuilds them. Operation domains are
therefore executable inputs to source evaluation, while observed identities remain the final
conservative invalidation authority.

| Application path | Owned source changes after a successful commit | Match source |
| --- | --- | --- |
| `State` | None; it can observe external catalogue, profile, or match changes | Load saved match |
| `CreateProfile` | `ProfileSummary`, `CardUniverseAndOwnership`, `StarterClaimsAndOwnership`, `MatchProfile` | Load saved match |
| `OpenPack` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership`, `StarterClaimsAndOwnership`, `PackHistoryAndOwnership` | Load saved match |
| `ClaimStarterDeck` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership`, `StarterClaimsAndOwnership`, `PackHistoryAndOwnership` | Load saved match |
| `SaveDeck` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership` | Load saved match |
| `DeleteDeck` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership` | Load saved match |
| `StartMatch` | `MatchDocument` | Use the committed match result |
| `ApplyMatchAction` | `MatchDocument` | Use the committed match result |
| `PurgeData` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership`, `StarterClaimsAndOwnership`, `PackHistoryAndOwnership`, `MatchProfile`, `MatchDocument` | No match |
| External profile revision/content | Exactly the profile-derived identities whose content changed | Load saved match |
| External match revision/content | `MatchDocument` and therefore `Match`/`MatchError` | Load saved match |
| Catalogue/authority replacement | Every field carrying a `Catalogue` dependency; compatible profile migration separately changes `ProfileSummary` | Service restart supplies the replacement catalogue |
| Failed, cancelled, CAS-conflicted, or idempotent operation | None unless a complete successful projection observes a real external source change | No partial cache publication |

The cache retains private templates only. Every public `ApplicationView` and public
`MatchServiceResult` is a newly materialized deep graph, so public array mutation cannot become a
source identity or corrupt a later response. Cancellation is checked after identity construction,
after each changed template segment, after complete template construction, and immediately before
publication. A rebuilt profile-identity candidate remains staged until the same final gate publishes
it with the complete application template, so cancellation cannot publish either cache alone.
