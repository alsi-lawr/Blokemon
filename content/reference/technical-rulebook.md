# Blokemon technical rules

**Status:** Published companion to the selected 1999 rules and card authority.

## Authority and scope

Blokemon uses a closed 1999 rules profile. The normative general-rules authority is the pinned *Pokémon Trading Card Game Advanced Rulebook, Version 1* published by Wizards of the Coast. The normative card authority is the selected English printing for each card in the 1999 Kanto authority ledger. The ledger also closes the profile over 25 enumerated pre-Base-Set-2 Wizards rulings; no later-era rule or card text is imported implicitly.

The executable expression of that profile is `content/authorities/mechanics.json`. Public names, card copy and terminology are bound by `content/authorities/public-content.json`. `content/reference/1999-kanto-authority-ledger.json` records each source selection, ruling and presentation mapping. If this companion is less specific than a selected card, that card's printed text governs.

## Closed card pool and public terminology

The collectible card pool contains:

- 151 selected Blokemon presentations sourced from Base Set, Jungle and Fossil, with the approved Mew Black Star Promo 8 exception;
- all 32 distinct Trainer cards from Base Set, Jungle and Fossil, each represented once; and
- the six Basic Energy cards plus Side Hustle, the Special Energy presentation of Double Colorless Energy.

Blokemon public rules use **Blokemon**, **Trainer**, **Energy**, **Active Spot**, **Bench**, **Hand**, **Deck**, **discard pile**, **Prize Card**, **HP**, **Damage**, **Attack**, **Blokemon Power**, **Weakness**, **Resistance**, **Retreat**, **Evolution** and **Knocked Out**. Internal identifiers and storage labels are not extra card categories or rules.

## Deck construction and setup

- A deck contains exactly 60 cards and at least one Basic Blokemon.
- A deck may contain no more than four cards with the same identity. Basic Energy cards are exempt from that limit; Side Hustle is Special Energy and is therefore limited to four copies.
- Each player shuffles, draws a seven-card Hand, places one Basic Blokemon in the Active Spot and may place up to five more Basic Blokemon on the Bench, then sets aside six Prize Cards.
- A Hand without a Basic Blokemon is revealed, reshuffled and redrawn until legal. The other player may draw an optional card for each excess mulligan after both players have a legal Hand.
- The starting player may Attack on their first turn. Neither player may evolve a Blokemon on their first turn.

## Turn sequence

At the start of a turn, the player draws one card. A player who cannot make this required draw loses the game. A draw required by card text takes the available cards if the Deck runs short and does not itself cause that loss. During the turn, the player may take the following actions in any legal order:

- put Basic Blokemon onto the Bench, up to its five-card limit;
- evolve Blokemon;
- attach one Energy card from the Hand;
- play Trainer cards;
- use Blokemon Powers;
- Retreat the Active Blokemon once; and
- Attack with the Active Blokemon.

An Attack ends the turn. A player may end the turn without Attacking.

## Trainers and Blokemon Powers

Trainer is one card class. A player may play any number of Trainer cards during their turn. A Trainer resolves according to its printed text and is then discarded unless that text places it somewhere else. A selected card's own text governs exceptional objects such as Reenactor and Spiral-Eyed Regular.

A Blokemon Power is not an Attack and does not end the turn. It can normally be used from the Bench as well as the Active Spot. Asleep, Confused or Paralyzed Blokemon cannot use a Blokemon Power unless the selected card says otherwise.

## Energy and Retreat

The six Basic Energy cards are the unlimited-copy Energy cards. Side Hustle is Special Energy: while attached to a Blokemon it provides 2 Local Energy, and it never counts as Basic Energy. Attaching Side Hustle uses the turn's single Energy-card attachment.

An Attack's Local Energy requirements can be satisfied by any Energy type. Energy used to satisfy an Attack remains attached unless the selected card says to discard it.

A player may Retreat the Active Blokemon once during their turn if the Bench is not empty. They discard attached Energy cards that provide at least the printed Retreat cost, then exchange the Active Blokemon with a Benched Blokemon. Because Side Hustle provides 2 Local Energy, one attached Side Hustle can pay a Retreat cost of two. A Confused Blokemon flips a coin before paying to Retreat; a failed check uses that turn's Retreat without discarding Energy or moving. Retreating keeps Damage and attached cards that were not paid, while clearing Special Conditions and effects on the retreating Active Blokemon.

## Evolution

Evolution must follow the exact Basic-to-Stage-1 or Stage-1-to-Stage-2 edge printed on the selected cards. A Blokemon cannot evolve on either player's first turn, on the same turn it entered play, or more than once in one turn. Evolution keeps Damage and attached cards while clearing Special Conditions and effects on that Blokemon.

## Attacks and Damage

After an Attack is declared and its Energy requirement is validated, resolve it in this order:

1. make the Confused check, if required;
2. make the choices required by the Attack;
3. pay or perform its use requirements;
4. apply effects that change or cancel the Attack;
5. apply effects that occur before Damage;
6. calculate and place Damage;
7. resolve the Attack's other effects;
8. check all Knocked Out Blokemon, take Prize Cards and promote from the Bench where required; and
9. end the turn.

Calculate Damage in this order:

1. begin with the amount printed or calculated by the Attack;
2. apply effects on the Attacking Blokemon;
3. stop if the result is zero;
4. apply Weakness;
5. apply Resistance;
6. apply Trainer effects;
7. apply Blokemon Powers;
8. place Damage counters; and
9. resolve effects that occur after Damage.

Damage to a Benched Blokemon does not apply Weakness or Resistance. Effects that place Damage counters are not Damage and do not apply Damage modifiers.

## Special Conditions and between-turn checks

Only an Active Blokemon can be Poisoned, Asleep, Paralyzed or Confused.

- Poisoned places one Damage counter between turns.
- Asleep prevents Attacking and Retreating; a coin flip between turns clears it on heads.
- Paralyzed prevents Attacking and Retreating and clears after its owner's next turn.
- Confused requires a coin flip before Attacking; a failed check cancels the Attack and places two Damage counters on that Blokemon.

Asleep, Confused and Paralyzed replace one another. Poisoned can coexist with one of those conditions. Between turns, resolve Special Conditions as one check block, then check for Knocked Out Blokemon.

## Knock Out, Prize Cards and winning

When Damage on a Blokemon equals or exceeds its HP, it is Knocked Out and discarded with its attached cards. Knocking Out an ordinary Blokemon awards exactly one Prize Card. Selected card-text exceptions, including Reenactor and Spiral-Eyed Regular, govern whether a Prize Card is taken for those objects.

A player wins by taking their last Prize Card, leaving the opponent with no Blokemon in play, or having the opponent fail the required start-of-turn draw. If both players satisfy one win condition at the same time, play Sudden Death with one Prize Card. If one player satisfies more simultaneous win conditions, that player wins.

## Pinned sources and compatibility boundary

- General rules: *Pokémon Trading Card Game Advanced Rulebook, Version 1*, Wizards of the Coast, `https://www.judgeball.com/files/archives/tcg-rulebooks/en/WOTC_v1.pdf`, SHA-256 `374e154ca72536146e359e9eca6e22a7815ded5403d7db51429ac7351cf6c00a`.
- Card printings, rulings and presentation mappings: `content/reference/1999-kanto-authority-ledger.json` and its human-readable companion `content/reference/1999-kanto-authority-ledger.md`.

Archived match manifests and migration records describe historical compatibility only. They do not add cards, terminology or rules to the published 1999 profile.
