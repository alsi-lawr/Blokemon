# Blokemon technical mechanics

**Status:** Portable mechanical authority.

## Authority and boundaries

The published mechanical authority is `content/authorities/mechanics.json`. It defines 151 collectible names and types, opaque mechanical IDs, internal mechanical types, display mappings, opcodes, targets, the fixed kit and Basic Vim library, acquisition products and the complete base-rule structure.

All authority programs are validated against executable opcode, condition, target, selection, distribution and trigger shapes. `sv151-authority-reconciliation.json` binds each effect to the preserved SV151 candidate.6 source hashes and records the narrow cases where the declarative authority corrects a candidate-program omission or ambiguity. `Blokemon.Game` executes the authority without provider, web or storage dependencies and persists random state, identified face-down bar chits, deferred choices, trigger timing and accepted command identities in `MatchState`.

Roadie is the display label for internal Metal on BLK-035, BLK-036 and BLK-124 soft spots and BLK-137's selectable-affinity mechanic. Roadie is not a collectible type.

## Stack and opening

- Each side has exactly 60 cards, no more than four cards with one mechanical identity except unlimited Basic Vim, and at least one Regular Bloke.
- Sample the opening side before either shuffle or opening draw. Each side shuffles, draws a seven-card mitt, places one Regular Bloke at the oche, may place up to five Regular Blokes in the booth, then sets six bar chits.
- A mitt without a Regular Bloke is reshuffled and redrawn until legal. Simultaneous mulligans grant no bonus. For each excess mulligan, the other side may draw up to one extra card.
- Throughout a bout, every card instance occupies exactly one zone, and each side's booth holds at most five Blokes.
- The opening side cannot play a Mate or declare an Attack in its first round.

## Round, promotion, Vim, kits and taxi

- Start each round with the required stack draw. Failure to make that required draw loses the bout. An effect draw from a short stack takes only available cards and does not itself lose the bout.
- An Attack ends the round; a Party Trick does not. One normal Vim attachment is allowed per round. Attack Vim costs stay attached unless an instruction says to chuck them; Local cost symbols accept any Vim.
- Promotion requires the exact mechanical edge. A Bloke cannot promote on either side's first round, its first round in play, or twice in one round. Promotion retains damage and attached cards while clearing rough states and Attack effects.
- Bar Bits and Bar Kits are unlimited per round, with at most one Bar Kit on a Bloke. At most one Mate and one Local may be played per round. Only one Local is in play per side; an identical mechanical Local cannot replace itself and a different Local chucks the old one.
- Taxi is once per round, requires a booth Bloke and chucks one Vim per fare symbol. NoddedOff or Legless Blokes cannot taxi. Moving to the booth clears rough states and Attack effects but retains damage and attachments.

## Attack and damage ordering

Resolve one committed Attack in this exact order:

1. Validate the declared Attack and Vim.

Attack declaration metadata is authoritative at this step: `canBeUsedFromBench` is false by default, while a true value permits that Attack to be declared while its Blokemon is on the Bench. The match engine enforces this field directly.
2. Apply effects that alter or cancel the Attack.
3. Resolve the Muddled beer-mat check.
4. Make required choices.
5. Pay or perform use requirements.
6. Apply before-damage effects.
7. Calculate and place damage.
8. Resolve other effects.
9. Check every send-home condition.
10. Take bar chits and promote from the booth where required.
11. End the round.

Calculate damage in this exact order: printed or program-defined base damage; effects on the Attacking Bloke before soft spot/stubborn streak; soft spot; stubborn streak; effects on the defending Bloke after those modifiers; clamp at zero and place counters. Booth damage ignores soft spots and stubborn streaks. Placed counters are not damage and do not use damage modifiers.

`UpTo` means one through the stated count except an optional draw may choose zero. “Any amount/number” may choose zero. Optional effects may be declined. `Chosen` consumes an explicit eligible object; `SeededRandom` consumes the explicit deterministic RNG stream.

## Rough states and checkup

Only the oche Bloke has rough states. During checkup resolve DodgyPint, Singed, NoddedOff and Legless as one non-interleavable block; other checkup effects may occur only before or after the whole block, then check both sides for send-home conditions.

- DodgyPint places one damage counter.
- Singed places two damage counters, then a beer-mat badge side clears it.
- NoddedOff prevents Attack and taxi; a checkup beer-mat badge side clears it.
- Legless prevents Attack and taxi and clears after its owner's next round.
- Muddled makes a beer-mat check before Attack; blank side cancels the Attack and places three self-damage counters.
- NoddedOff, Muddled and Legless are the rotated group; the latest replaces the previous. Singed and DodgyPint coexist with each other and the rotated group. Promotion or moving to the booth clears all rough states.

## Send home, bar chits and terminal outcomes

A Bloke is sent home when damage is at least staying power. Chuck that Bloke and every attachment. A normal target awards one bar chit; a Big Hitter awards two. If play continues, the defeated side chooses a booth Bloke to promote to the oche.

A side wins by taking its last bar chit, leaving the other side with no Bloke in play, or having the other side fail its required opening draw. If both sides achieve one win method simultaneously, use sudden death with one bar chit and repeat until a winner. If one side achieves more simultaneous win methods, it wins immediately.

KIT-001 through KIT-003 may act as Regular Local Blokes with 60 staying power. They cannot have rough states or taxi, may be chucked by their owner during their round, and award one bar chit when sent home. The eleven IDs listed in `bigHitters.blokeIds` award two bar chits.

## Products

The one-card product is uniform across all 151 collectible identities (1/151 each). The eleven-card product selects one of 49 Rare, three distinct of 49 Uncommon and seven distinct of 53 Common identities. Exact named-identity inclusion odds are 1/49 Rare, 3/49 Uncommon and 7/53 Common. There is no pity; one pack cannot repeat an identity; separate packs may.

## D-216 timing and finalisation rows

The persisted revision/idempotency transition evidence is the 37-case `timing-corpus.json`. The authoritative 20 rows are:

| Row | Event | Clock | Seconds | Terminal | Outcome | Idempotency |
|---|---|---:|---:|---|---|---|
| unaccepted-challenge-expiry | ChallengeDeadline | ChallengeEnabledTime | 300 | ChallengeExpired | ChallengeExpiresNoReservation | ChallengeRevisionAndDeadline |
| accepted-action-clock | RequiredActionAssigned | ActionConnectedEnabledTime | 90 | NonTerminal | ActionClockStartsWithNinetySeconds | BattleRevisionAndActionOwner |
| participant-disconnect | ParticipantDisconnected | ReconnectEnabledTime | 300 | NonTerminal | ActionClockPausesReconnectGraceStarts | BattleRevisionAndParticipant |
| timeout-revision-wins | TimeoutRevisionCommittedFirst | ActionConnectedEnabledTime | 0 | ActionTimeoutForfeit | ActionTimeoutForfeitIntent | BattleRevisionAndDeadline |
| disconnect-revision-wins | DisconnectRevisionCommittedFirst | ReconnectEnabledTime | 300 | NonTerminal | ReconnectGraceControls | BattleRevisionAndParticipant |
| single-absence-grace-expiry | SingleReconnectGraceDeadline | ReconnectEnabledTime | 300 | ReconnectForfeit | AbsentParticipantForfeitIntent | BattleRevisionAndDeadline |
| both-absent-earliest-grace | EarliestReconnectGraceDeadlineBothAbsent | ReconnectEnabledTime | 300 | CancelledBothAbsent | BothAbsentCancellationIntent | BattleRevisionAndDeadline |
| process-restart-pause | ProcessRestarted | None | 0 | NonTerminal | ResumeExactRemainingTime | PauseRevision |
| feature-disable-pause | FeatureDisabled | None | 0 | NonTerminal | ResumeExactRemainingTime | PauseRevision |
| recoverable-system-pause | RecoverableSystemPause | None | 0 | NonTerminal | ResumeExactRemainingTime | PauseRevision |
| rule-win-finalisation | RuleWinDeclared | None | 0 | RuleWin | RuleWinIntent | BattleFinalizationKey |
| voluntary-forfeit-finalisation | VoluntaryForfeitDeclared | None | 0 | VoluntaryForfeit | VoluntaryForfeitIntent | BattleFinalizationKey |
| action-timeout-finalisation | ActionTimeoutDeclared | None | 0 | ActionTimeoutForfeit | ActionTimeoutForfeitIntent | BattleFinalizationKey |
| reconnect-forfeit-finalisation | ReconnectForfeitDeclared | None | 0 | ReconnectForfeit | AbsentParticipantForfeitIntent | BattleFinalizationKey |
| rule-draw-finalisation | RuleDrawDeclared | None | 0 | RuleDraw | RuleDrawIntent | BattleFinalizationKey |
| mutual-cancellation-finalisation | MutualCancellationDeclared | None | 0 | CancelledMutual | MutualCancellationIntent | BattleFinalizationKey |
| both-absent-cancellation-finalisation | BothAbsentCancellationDeclared | None | 0 | CancelledBothAbsent | BothAbsentCancellationIntent | BattleFinalizationKey |
| viewer-erasure-finalisation | ViewerErasureDeclared | None | 0 | CancelledErasure | ViewerErasureCancellationIntent | BattleFinalizationKey |
| authorised-recovery-cancellation-finalisation | AuthorisedRecoveryCancellationDeclared | None | 0 | CancelledAuthorizedRecovery | AuthorisedRecoveryCancellationIntent | BattleFinalizationKey |
| unrecoverable-failure-finalisation | UnrecoverableFailureDeclared | None | 0 | CancelledUnrecoverableFailure | UnrecoverableFailureCancellationIntent | BattleFinalizationKey |

The timing replay is a finite evidence evaluator, not battle persistence or settlement. Product finalisation remains the named owner where the row says so.
