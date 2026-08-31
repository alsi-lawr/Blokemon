# 1999 Kanto authority ledger

**Status:** Complete. All card, Trainer, Energy, printing, rules-source, and presentation selections are human-approved and machine-validated. This Markdown file indexes the normative JSON ledger; it is not a second authority.

The normative general-rules authority is the pinned Wizards **Advanced Rulebook Version 1** scan. The normative card authority is each selected English printed card image. The pinned PokemonTCG data is a transcription and review aid only.

## Pinned general-rules oracle

| Field | Pinned value |
| --- | --- |
| Title | Advanced Rulebook Version 1 |
| Publisher | Wizards of the Coast, Inc. |
| Locator | [https://www.judgeball.com/files/archives/tcg-rulebooks/en/WOTC_v1.pdf](https://www.judgeball.com/files/archives/tcg-rulebooks/en/WOTC_v1.pdf) |
| Media inventory | 17 PDF scan pages; printed page sequence 1–28 (28 positions), plus covers, insert, and credits |
| Copyright marking | ©1999-2000 Nintendo of America; ©1999-2000 Wizards of the Coast, Inc. |
| SHA-256 | `374e154ca72536146e359e9eca6e22a7815ded5403d7db51429ac7351cf6c00a` |
| Review | Every PDF scan page inspected |

Page position 19 is the product promotion between numbered pages 18 and 20; the scan shows no visible `19`. It is inventoried explicitly and supplies no gameplay rule.

### Page-by-page review

| PDF page | Printed page position(s) | Reviewed content |
| ---: | --- | --- |
| 1 | Cover / unnumbered | Front cover: Pokémon Trading Card Game Advanced Rulebook, Version 1. |
| 2 | 1 | Inside-front artwork and numbered page 1 table of contents. |
| 3 | 2, 3 | Advanced rules: scope, win conditions, deck size, setup, Pokémon Powers, Stage 2 evolution, Weakness, Resistance, and attack ordering. |
| 4 | 4, 5 | Attack ordering continued; Sleep, Confusion, Paralysis, Poison, condition replacement, and after-turn processing. |
| 5 | 6, 7 | Complete Rules Reference: required materials, game model, win conditions, starting hands, Bench, Prizes, and setup. |
| 6 | 8, 9 | Cards in play and anatomy of Pokémon, Trainer, and Energy cards. |
| 7 | 10, 11 | Turn sequence, drawing, playing Basic Pokémon, and evolution restrictions. |
| 8 | 12, 13 | Attaching Energy, playing Trainers, retreating, and using Pokémon Powers. |
| 9 | 14, 15 | Attack declaration, Energy requirements, Colorless requirements, Weakness, Resistance, and damage counters. |
| 10 | 16, 17 | Knock Outs, Prizes, Active replacement, turn completion, and Sleep and Confusion reference. |
| 11 | 18, 19 | Paralysis, Poison, combined conditions, followed by the product-level promotional insert occupying page position 19. |
| 12 | 20, 21 | Deck construction and Expert Rules for what counts as an attack and attack resolution order. |
| 13 | 22, 23 | Damage calculation, retreat with Double Energy, short draws and searches, and simultaneous opening mulligans. |
| 14 | 24, 25 | Simultaneous wins, Sudden Death, and glossary through Hit Points. |
| 15 | 26, 27 | Glossary completion and index. |
| 16 | 28 | Questions/contact page and credits with 1999–2000 copyright markings. |
| 17 | Cover / unnumbered | Back-cover Pokémon Trading Card Game League promotional insert. |

### General-rule citations

Every general rule selected for the migration has an explicit citation to the printed page sequence in the pinned scan.

| Citation ID | Rule topic | Rulebook citation |
| --- | --- | --- |
| `RULE-WIN-CONDITIONS` | Prize, deck-out, and no-replacement win conditions | Advanced Rulebook Version 1, pp. 2, 6, 16 |
| `RULE-DECK-CONSTRUCTION` | Exactly 60 cards, four-card name limit, and Basic Energy exception | Advanced Rulebook Version 1, pp. 2, 20 |
| `RULE-SETUP` | Starting hand, Active, Bench, six Prizes, first player, and reveal | Advanced Rulebook Version 1, pp. 2, 7 |
| `RULE-MULLIGAN` | No-Basic redraws and optional extra cards | Advanced Rulebook Version 1, pp. 7, 23 |
| `RULE-IN-PLAY` | Active, Bench, in-play cards, deck, discard pile, and Prizes | Advanced Rulebook Version 1, pp. 6–8, 25–26 |
| `RULE-TURN-SEQUENCE` | Draw, optional actions in any order, attack last, and end turn | Advanced Rulebook Version 1, pp. 10–11, 14, 16 |
| `RULE-PLAY-BASIC` | Playing Basic Pokémon to an available Bench slot | Advanced Rulebook Version 1, p. 11 |
| `RULE-EVOLUTION` | Stage sequence, retained cards and damage, cleared effects, and first/same-turn restrictions | Advanced Rulebook Version 1, pp. 3, 11 |
| `RULE-ATTACH-ENERGY` | One Energy card attachment from hand per turn to an in-play Pokémon | Advanced Rulebook Version 1, pp. 10, 12 |
| `RULE-TRAINERS` | Play Trainer cards by resolving their text and discarding them | Advanced Rulebook Version 1, pp. 9–10, 12, 27 |
| `RULE-POKEMON-POWERS` | Pokémon Powers, Benched use, frequency, and distinction from attacks | Advanced Rulebook Version 1, pp. 3, 10, 13, 26 |
| `RULE-RETREAT` | Retreat cost, Energy-card discards, switching, retained cards and damage, and cleared effects | Advanced Rulebook Version 1, pp. 10, 13, 23, 27 |
| `RULE-ATTACK-DECLARATION` | One attack, attack last, required Energy, and Colorless requirements | Advanced Rulebook Version 1, pp. 10, 14–15 |
| `RULE-ATTACK-DEFINITION` | Attack text definition, including attacks that do not affect the Defending Pokémon | Advanced Rulebook Version 1, p. 21 |
| `RULE-ATTACK-ORDER` | Choices, costs, cancellation, damage, other effects, and Knock Out order | Advanced Rulebook Version 1, pp. 3, 21 |
| `RULE-DAMAGE` | Base damage, attacker effects, zero-damage stop, Weakness, Resistance, Trainer effects, Powers, counters, and post-damage effects | Advanced Rulebook Version 1, pp. 3, 15, 22 |
| `RULE-WEAKNESS-RESISTANCE` | Weakness doubles damage before Resistance subtracts 30 | Advanced Rulebook Version 1, pp. 3, 15, 22, 26–27 |
| `RULE-KNOCK-OUT` | Damage at least HP, discard attached cards, Prize taking, and Active replacement order | Advanced Rulebook Version 1, pp. 6, 16, 25–26 |
| `RULE-ASLEEP` | Cannot attack or retreat; between-turn recovery coin flip | Advanced Rulebook Version 1, pp. 4, 17 |
| `RULE-CONFUSED` | Retreat and attack coin flips, failed retreat, and self-attack damage | Advanced Rulebook Version 1, pp. 4, 17 |
| `RULE-PARALYZED` | Cannot attack or retreat and recovers after the owner’s next turn | Advanced Rulebook Version 1, pp. 5, 18 |
| `RULE-POISONED` | Poison marker, 10 damage after each turn, and replacement rather than stacking | Advanced Rulebook Version 1, pp. 5, 18 |
| `RULE-CONDITION-INTERACTION` | Asleep, Confused, and Paralyzed replace one another; Poison can coexist; evolution or Bench clears effects | Advanced Rulebook Version 1, pp. 4–5, 13, 18 |
| `RULE-AFTER-TURN` | Poison damage and Sleep or Paralysis recovery after each player’s turn | Advanced Rulebook Version 1, pp. 5, 16–18 |
| `RULE-DOUBLE-ENERGY-RETREAT` | Paying Retreat Cost one Energy card at a time without discarding after the cost is met | Advanced Rulebook Version 1, p. 23 |
| `RULE-PARTIAL-DRAW-SEARCH` | Do as much as possible when an effect draws or searches for unavailable cards | Advanced Rulebook Version 1, p. 23 |
| `RULE-SIMULTANEOUS-WIN` | Sudden Death when both players satisfy Prize or no-replacement wins simultaneously | Advanced Rulebook Version 1, p. 24 |

### Printed cards and bounded rulings

All **190** selected printings were compared to their linked English card images. The JSON pins the source ID, image locator, byte count, image SHA-256, printed identity, mechanics digest, and completed review status for each row.

Card-specific semantics come first from the selected printed image. The only admitted external clarifications are the 25 individually enumerated Wizards rulings in `rulesOracle.officialRulingsBoundary.rulings`. Each entry identifies selected source IDs, its WotC source date, Compendium section, and the bounded clarification.

The ruling cutoff is **strictly before Base Set 2**: the latest eligible date is 2000-02-23 and entries dated 2000-02-24 or later are excluded. Undated FAQ material, Compendium editorial additions, and rulings for unselected printings are also excluded.

**Advanced Rulebook Version 10 is excluded.** Burn, Supporters, Technical Machines, Poké-Powers, Poké-Bodies, and every other later e-card-era mechanic or ruling are outside this authority.

## Approved authority decisions

1. The eight cross-set mechanical variants use their earliest English printing. Mew uses non-holographic Wizards Black Star Promo #8.
2. The 26 mechanically equivalent Jungle/Fossil pairs use the non-holographic printing because the corresponding generated Blokemon faces do not present as holographic.
3. The closed pool contains all 32 Base Set, Jungle, and Fossil Trainers, all six Basic Energy cards, and Double Colorless Energy exactly once.
4. The fourteen existing Kit presentations use the reviewed mappings below. Eighteen approved themed Trainer presentations complete the Trainer pool.
5. Side Hustle presents Double Colorless Energy. It is Special Energy that provides two Local Energy and is not Basic Energy.

## Coverage

| Inventory | Count |
| --- | ---: |
| Selected collectibles | 151 |
| Single-printing collectibles | 116 |
| Earliest-set mechanical choices | 8 |
| Non-holographic pair choices | 26 |
| Approved Mew promo choice | 1 |
| Reused Trainer presentations | 14 |
| New Trainer presentations | 18 |
| Selected vintage Trainers | 32 |
| Selected Basic Energy cards | 6 |
| Selected Special Energy cards | 1 |

The collectible ledger covers Pokédex numbers 1 through 151 exactly once and identifies one approved source printing for every row.

## Existing Trainer presentations

| Kit | Existing presentation | Selected vintage source |
| --- | --- | --- |
| KIT-001 | The Reenactor | Clefairy Doll — Base Set #70 |
| KIT-002 | The Spiral-Eyed Regular | Mysterious Fossil — Fossil #62 |
| KIT-003 | The Relic Hunter | Item Finder — Base Set #74 |
| KIT-004 | The Chauffeur | Switch — Base Set #95 |
| KIT-005 | Talent Scout | Pokémon Trader — Base Set #77 |
| KIT-006 | The Ring Road | Maintenance — Base Set #83 |
| KIT-007 | Auntie at the Door | Bill — Base Set #91 |
| KIT-008 | The Optimist | Recycle — Fossil #61 |
| KIT-009 | The Matchmaker | Pokémon Flute — Base Set #86 |
| KIT-010 | The Guv'nor | Energy Removal — Base Set #92 |
| KIT-011 | The Door Staff | Lass — Base Set #75 |
| KIT-012 | Closing-Time Regular | Potion — Base Set #94 |
| KIT-013 | Health-and-Safety Rep | Full Heal — Base Set #82 |
| KIT-014 | Old-School Bouncer | Defender — Base Set #80 |

## New Trainer presentations

| Kit | Approved name | Selected vintage source | Approved illustration concept |
| --- | --- | --- | --- |
| KIT-015 | Ask Around | Computer Search — Base Set #71 | A regular gives up two blank betting slips at a crowded pub table; two locals point him to the single wanted item in an overfilled lost-property cabinet. |
| KIT-016 | Cold Shower | Devolution Spray — Base Set #72 | A dressed-up regular loses the outer layers of a grand makeover under a bracing cold shower, returning to an earlier self. |
| KIT-017 | New Deal | Impostor Professor Oak — Base Set #73 | A smug bogus expert sweeps an opponent’s whole hand together, shuffles it, and deals seven fresh blank cards. |
| KIT-018 | Fast Track | Pokémon Breeder — Base Set #76 | A fresh-faced newcomer with a VIP wristband is ushered past the middle velvet-rope checkpoint straight to the final tier. |
| KIT-019 | Last Train Home | Scoop Up — Base Set #78 | A last train collects one regular while their attached clutter is left behind on the platform. |
| KIT-020 | Cut Off | Super Energy Removal — Base Set #79 | A bartender surrenders one drink from their own side to remove two drinks from a rival patron. |
| KIT-021 | Hair of the Dog | Energy Retrieval — Base Set #81 | One unwanted item is traded across the bar for up to two recovered drinks from the empties tray. |
| KIT-022 | Double Shot | PlusPower — Base Set #84 | A bartender places a small extra measure beside the active regular for one immediate burst of force. |
| KIT-023 | Walk-In Centre | Pokémon Center — Base Set #85 | A waiting room treats every injured member of one group while their remaining drinks are collected for disposal. |
| KIT-024 | Racing Form | Pokédex — Base Set #87 | A focused punter studies and deliberately rearranges five race cards before the next draw. |
| KIT-025 | Quizmaster | Professor Oak — Base Set #88 | A quizmaster bins a team’s current answer sheet and hands them seven clean slips. |
| KIT-026 | Defibrillator | Revive — Base Set #89 | A first aider restores a fallen novice to the bench, clearly alive but still carrying half their injuries. |
| KIT-027 | Full English | Super Potion — Base Set #90 | A substantial breakfast restores a larger amount of strength after one attached drink is given up. |
| KIT-028 | You’re Up | Gust of Wind — Base Set #93 | A seated reserve is beckoned off the bench and made to take the active darts oche while the former player steps aside. |
| KIT-029 | Lucky Dip | Poké Ball — Jungle #64 | A punter flips a coin while reaching into a lucky-dip barrel and pulls out one chosen local’s portrait token from many. |
| KIT-030 | Home Safe | Mr. Fuji — Fossil #58 | An older regular helps one benched patron, coat and bags together into a waiting cab home. |
| KIT-031 | Top Up | Energy Search — Fossil #59 | A bartender reaches into a mixed cellar crate and retrieves the one charged Energy token needed for a refill. |
| KIT-032 | Fruit Machine | Gambler — Fossil #60 | A player feeds their whole hand into a fruit machine; one coin flip pays either eight cards or only one. |

## Energy presentations

| Presentation | Selected vintage source | Disposition |
| --- | --- | --- |
| Side Hustle (`VIM-DODGY`) | Double Colorless Energy — Base Set #96 | `ReuseSideHustlePresentationForDoubleColorless` |
| Front (`VIM-LAIRY`) | Fighting Energy — Base Set #97 | `UseSelectedSourceMechanics` |
| Heat (`VIM-CURRY`) | Fire Energy — Base Set #98 | `UseSelectedSourceMechanics` |
| Haze (`VIM-BLAZED`) | Grass Energy — Base Set #99 | `UseSelectedSourceMechanics` |
| Dutch Courage (`VIM-BEER`) | Lightning Energy — Base Set #100 | `UseSelectedSourceMechanics` |
| Rush (`VIM-GEEKED`) | Psychic Energy — Base Set #101 | `UseSelectedSourceMechanics` |
| Resolve (`VIM-SOBER`) | Water Energy — Base Set #102 | `UseSelectedSourceMechanics` |

## Cross-set mechanical choices

| Blokemon | Species | Selected source | Other candidates |
| --- | --- | --- | --- |
| BLK-025 Pintman | Pikachu | Base Set #58 (`base1-58`) | Jungle #60 (`base2-60`) |
| BLK-026 Donni | Raichu | Base Set #14 (`base1-14`) | Fossil #14 (`base3-14`), Fossil #29 (`base3-29`) |
| BLK-082 Three-Pint Hero | Magneton | Base Set #9 (`base1-9`) | Fossil #11 (`base3-11`), Fossil #26 (`base3-26`) |
| BLK-092 Kitchen Afterparty | Gastly | Base Set #50 (`base1-50`) | Fossil #33 (`base3-33`) |
| BLK-093 Someone's Mate | Haunter | Base Set #29 (`base1-29`) | Fossil #6 (`base3-6`), Fossil #21 (`base3-21`) |
| BLK-101 Kegstand | Electrode | Base Set #21 (`base1-21`) | Jungle #2 (`base2-2`), Jungle #18 (`base2-18`) |
| BLK-126 Mr Vesta | Magmar | Base Set #36 (`base1-36`) | Fossil #39 (`base3-39`) |
| BLK-145 Beer Baron | Zapdos | Base Set #16 (`base1-16`) | Fossil #15 (`base3-15`), Fossil #30 (`base3-30`) |

## Deterministic validation

Run:

```console
$ content/reference/validate-1999-kanto-authority-ledger.py
Validated ...: 151 collectibles, 32 Trainers, 7 Energy cards, 190 printing reviews, 17 rulebook PDF pages, 27 general-rule citations, and 25 bounded rulings.
```

The validator rejects missing or duplicate pool entries and mappings, changes to the approved Trainer or Energy presentation mappings, stale proposal state, unapproved dispositions, changed mechanics or selected-printing image digests, incomplete printing reviews, incomplete page inventory or rule citations, out-of-cutoff rulings, and loss of the explicit Version 10/e-card exclusions.

## Machine-readable authority

`content/reference/1999-kanto-authority-ledger.json` is the complete source-selection and provenance ledger. It does not implement gameplay, card programs, or presentation assets.
