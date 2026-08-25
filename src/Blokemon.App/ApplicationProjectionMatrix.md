# Complete application projection invalidation matrix

The executable invalidation authority is `ApplicationProjectionMatrix.fields` in
`ApplicationProjectionModel.fs`. `ApplicationProjectionCache` compares every source identity in its
complete key and applies those field rows to each observed difference. The first table uses the exact
enum and flag names and is enumerated by the focused integration test.

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
| `MatchRecovery` | `Catalogue`, `MatchProfile`, `MatchDocument` | The same match sources change, including an eligible active-match or history recovery gate appearing, changing identity, or clearing. |

Operation names do not participate in cache invalidation. The second table records the expected
source differences exercised by the counter/equality tests, including content-dependent differences
that may stay unchanged. It is review evidence, not a second runtime authority. The only executable
operation mapping is `MatchSource`, which decides whether view assembly loads the saved match, uses
the committed match result, or projects no match; the focused test enumerates all nine rows.

| Application path | Expected observed source differences | Match source |
| --- | --- | --- |
| `State` | None without an external write; the external rows below cover changed sources | Load saved match |
| `CreateProfile` | `ProfileSummary`, `CardUniverseAndOwnership`, `StarterClaimsAndOwnership`, `MatchProfile` | Load saved match |
| `OpenPack` | `ProfileSummary`, `CardUniverseAndOwnership`, `PackHistoryAndOwnership`; also `SavedDecksAndOwnership` or `StarterClaimsAndOwnership` when the sampled ownership affects those views | Load saved match |
| `ClaimStarterDeck` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership`, `StarterClaimsAndOwnership`; also `PackHistoryAndOwnership` when an existing latest receipt contains a newly owned card | Load saved match |
| `SaveDeck` | `ProfileSummary`, `SavedDecksAndOwnership`; also `CardUniverseAndOwnership` when a historical-only card id enters or leaves the saved-deck universe | Load saved match |
| `DeleteDeck` | `ProfileSummary`, `SavedDecksAndOwnership`; also `CardUniverseAndOwnership` when a historical-only card id leaves the saved-deck universe | Load saved match |
| `StartMatch` | `MatchDocument` | Use the committed match result |
| `ApplyMatchAction` | `MatchDocument` | Use the committed match result |
| `AbandonSavedMatch` | `MatchDocument` | Load the saved match again after the exact active-match primary is deleted. |
| `DiscardMatchHistory` | `MatchDocument` | Load the saved completed match again after its separate history gate is deleted. |
| `PurgeData` | `ProfileSummary`, `CardUniverseAndOwnership`, `SavedDecksAndOwnership`, `StarterClaimsAndOwnership`, `PackHistoryAndOwnership`, `MatchProfile`, `MatchDocument` | No match |
| External profile revision/content | Exactly the profile-derived identities whose content changed | Load saved match |
| External match revision/content | `MatchDocument` and therefore `Match`/`MatchError` | Load saved match |
| Catalogue/authority replacement | A fresh service performs one cold build of every field; compatible profile migration is compared against that cold reference | Service restart supplies the replacement catalogue |
| Failed, cancelled, CAS-conflicted, or idempotent operation | None unless a complete successful projection observes a real external source change | No partial cache publication |

The cache retains private templates only. Every public `ApplicationView` and public
`MatchServiceResult` is a newly materialized deep graph, so public array mutation cannot become a
source identity or corrupt a later response. Cancellation is checked after identity construction,
after each changed template segment, after complete template construction, and immediately before
publication. A rebuilt profile-identity candidate remains staged until the same final gate publishes
it with the complete application template, so cancellation cannot publish either cache alone.
