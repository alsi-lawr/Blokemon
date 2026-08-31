#!/usr/bin/env python3

import hashlib
import json
import re
import sys
from collections import Counter
from datetime import date
from pathlib import Path

EXPECTED_RULEBOOK_SHA256 = '374e154ca72536146e359e9eca6e22a7815ded5403d7db51429ac7351cf6c00a'
EXPECTED_RULEBOOK_URL = 'https://www.judgeball.com/files/archives/tcg-rulebooks/en/WOTC_v1.pdf'
EXPECTED_SELECTED_PRINTING_DIGESTS_SHA256 = '6d86e7b2dc67b1825a67ed5740dfc4310b0139d40712addce9fbcc1bfceb7cfd'
EXPECTED_TRAINER_PRESENTATION_SOURCE_BY_KIT_ID = {
    'KIT-001': 'base1-70',
    'KIT-002': 'base3-62',
    'KIT-003': 'base1-74',
    'KIT-004': 'base1-95',
    'KIT-005': 'base1-77',
    'KIT-006': 'base1-83',
    'KIT-007': 'base1-91',
    'KIT-008': 'base3-61',
    'KIT-009': 'base1-86',
    'KIT-010': 'base1-92',
    'KIT-011': 'base1-75',
    'KIT-012': 'base1-94',
    'KIT-013': 'base1-82',
    'KIT-014': 'base1-80',
    'KIT-015': 'base1-71',
    'KIT-016': 'base1-72',
    'KIT-017': 'base1-73',
    'KIT-018': 'base1-76',
    'KIT-019': 'base1-78',
    'KIT-020': 'base1-79',
    'KIT-021': 'base1-81',
    'KIT-022': 'base1-84',
    'KIT-023': 'base1-85',
    'KIT-024': 'base1-87',
    'KIT-025': 'base1-88',
    'KIT-026': 'base1-89',
    'KIT-027': 'base1-90',
    'KIT-028': 'base1-93',
    'KIT-029': 'base2-64',
    'KIT-030': 'base3-58',
    'KIT-031': 'base3-59',
    'KIT-032': 'base3-60',
}
EXPECTED_ENERGY_PRESENTATION_SOURCE_BY_VIM_ID = {
    'VIM-DODGY': 'base1-96',
    'VIM-LAIRY': 'base1-97',
    'VIM-CURRY': 'base1-98',
    'VIM-BLAZED': 'base1-99',
    'VIM-BEER': 'base1-100',
    'VIM-GEEKED': 'base1-101',
    'VIM-SOBER': 'base1-102',
}


def canonical_sha256(value):
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()
    return hashlib.sha256(encoded).hexdigest()


def validate(ledger):
    errors = []

    def check(condition, message):
        if not condition:
            errors.append(message)

    def unique(values, label):
        values = list(values)
        check(len(values) == len(set(values)), f'{label} must be unique')
        return set(values)

    def visit(value, path='$'):
        if isinstance(value, dict):
            for key, child in value.items():
                lowered = key.lower()
                check(not any(word in lowered for word in ('pending', 'proposal', 'proposed')), f'{path}.{key} is stale proposal state')
                visit(child, f'{path}.{key}')
        elif isinstance(value, list):
            for index, child in enumerate(value):
                visit(child, f'{path}[{index}]')
        elif path.endswith('.status') and isinstance(value, str):
            lowered = value.lower()
            check('pending' not in lowered and 'proposed' not in lowered and 'unapproved' not in lowered, f'{path} is not approved: {value}')

    visit(ledger)
    check(ledger.get('schemaVersion') == 2, 'schemaVersion must be 2')
    check(ledger.get('ledgerVersion') == 'blokemon-1999-kanto-authority-ledger-1.1.0', 'ledgerVersion is not final')
    check(ledger.get('status') == 'CompletePinnedAuthority', 'ledger status is not complete')

    collectibles = ledger.get('collectibles', [])
    check(len(collectibles) == 151, 'collectibles must contain 151 rows')
    check(unique((row.get('blokemonId') for row in collectibles), 'collectible IDs') == {f'BLK-{number:03}' for number in range(1, 152)}, 'collectible IDs must cover BLK-001 through BLK-151')
    check(unique((row.get('pokedexNumber') for row in collectibles), 'Pokédex numbers') == set(range(1, 152)), 'Pokédex numbers must cover 1 through 151')
    check(len({row.get('approvedName') for row in collectibles}) == 151, 'approved collectible names must be unique')

    selected_cards = []
    differences = Counter()
    for row in collectibles:
        source_id = row.get('selectedSourceId')
        candidates = row.get('candidates', [])
        matching = [candidate for candidate in candidates if candidate.get('sourceId') == source_id]
        check(row.get('selectionStatus') == 'ApprovedAndFixed', f'{row.get("blokemonId")} is not approved and fixed')
        check(len(matching) == 1, f'{row.get("blokemonId")} must select exactly one listed candidate')
        check(len({candidate.get('sourceId') for candidate in candidates}) == len(candidates), f'{row.get("blokemonId")} has duplicate candidate source IDs')
        for candidate in candidates:
            check(candidate.get('mechanicalSha256') == canonical_sha256(candidate.get('mechanics')), f'{candidate.get("sourceId")} has a stale mechanics digest')
        if matching:
            selected_cards.append(matching[0])
        difference = row.get('candidateDifference')
        differences[difference] += 1
        if difference == 'None':
            check(len(candidates) == 1 and row.get('selectionReason') == 'OnlyEligiblePrinting', f'{row.get("blokemonId")} one-printing selection is inconsistent')
        elif difference == 'MechanicalVariant':
            check(source_id.startswith('base1-'), f'{row.get("blokemonId")} mechanical variant must select Base Set')
            check(row.get('selectionReason') == 'EarliestEnglishSetMechanics', f'{row.get("blokemonId")} mechanical selection reason is wrong')
        elif difference == 'PrintingOnly':
            check(len(candidates) == 2, f'{row.get("blokemonId")} holo/non-holo pair must contain two candidates')
            check(len({candidate.get('mechanicalSha256') for candidate in candidates}) == 1, f'{row.get("blokemonId")} printing-only pair differs mechanically')
            check(matching and matching[0].get('rarity') == 'Rare', f'{row.get("blokemonId")} must select its non-holographic Rare printing')
            check(row.get('selectionReason') == 'ApprovedNonHolographicPresentationMatch', f'{row.get("blokemonId")} printing selection reason is wrong')
        elif difference == 'PromoPrintingChoice':
            check(row.get('blokemonId') == 'BLK-151' and source_id == 'basep-8', 'the promo exception must be BLK-151 selecting Mew 8')
            check(row.get('selectionReason') == 'ApprovedWizardsBlackStarPromoMew8', 'Mew selection reason is wrong')
        else:
            check(False, f'{row.get("blokemonId")} has unknown candidateDifference {difference!r}')

    expected_differences = Counter({'None': 116, 'MechanicalVariant': 8, 'PrintingOnly': 26, 'PromoPrintingChoice': 1})
    check(differences == expected_differences, f'collectible selection categories differ: {dict(differences)}')
    check(len(selected_cards) == 151, 'all collectible selections must resolve')
    check(len({card.get('sourceId') for card in selected_cards}) == 151, 'selected collectible source IDs must be unique')
    check(
        all(card.get('setId') in {'base1', 'base2', 'base3'} or card.get('sourceId') == 'basep-8' for card in selected_cards),
        'selected collectible sets exceed Base/Jungle/Fossil plus Mew 8'
    )

    supports = ledger.get('supports', {})
    trainers = supports.get('selectedTrainers', [])
    check(supports.get('status') == 'ApprovedComplete', 'Trainer pool is not approved complete')
    check(len(trainers) == 32, 'selectedTrainers must contain 32 cards')
    trainer_ids = unique((card.get('sourceId') for card in trainers), 'selected Trainer source IDs')
    check(Counter(card.get('setId') for card in trainers) == Counter({'base1': 26, 'base2': 1, 'base3': 5}), 'Trainer pool must contain 26 Base Set, one Jungle, and five Fossil Trainers')
    for card in trainers:
        check(card.get('mechanicalSha256') == canonical_sha256({'rules': card.get('rules')}), f'{card.get("sourceId")} has a stale Trainer rules digest')
    check(set(supports.get('selectedPoolSourceIds', [])) == trainer_ids and len(supports.get('selectedPoolSourceIds', [])) == 32, 'selected Trainer pool index is incomplete or duplicated')

    reused = supports.get('currentEntries', [])
    new_presentations = supports.get('newPresentations', [])
    check(len(reused) == 14, 'there must be 14 reused Trainer presentations')
    check(len(new_presentations) == 18, 'there must be 18 new Trainer presentations')
    kit_ids = unique([row.get('kitId') for row in reused + new_presentations], 'Trainer presentation Kit IDs')
    check(kit_ids == {f'KIT-{number:03}' for number in range(1, 33)}, 'Trainer presentations must cover KIT-001 through KIT-032')
    presentation_sources = [row.get('selectedSourceId') for row in reused] + [row.get('sourceId') for row in new_presentations]
    check(unique(presentation_sources, 'Trainer presentation source IDs') == trainer_ids, 'Trainer presentations must map every selected Trainer exactly once')
    presentation_source_by_kit_id = {
        row.get('kitId'): row.get('selectedSourceId') for row in reused
    } | {
        row.get('kitId'): row.get('sourceId') for row in new_presentations
    }
    check(
        presentation_source_by_kit_id == EXPECTED_TRAINER_PRESENTATION_SOURCE_BY_KIT_ID,
        'Trainer presentation mappings differ from the approved fixed mappings'
    )
    trainer_by_id = {card.get('sourceId'): card for card in trainers}
    for row in reused:
        check(row.get('dispositionStatus') == 'ApprovedExistingPresentationMapping', f'{row.get("kitId")} reused presentation is not approved')
        source = trainer_by_id.get(row.get('selectedSourceId'), {})
        check(row.get('approvedSourcePrintedName') == source.get('printedName'), f'{row.get("kitId")} printed Trainer name does not match its source')
        check(bool(row.get('approvalRationale')), f'{row.get("kitId")} has no approval rationale')
    for row in new_presentations:
        check(row.get('status') == 'ApprovedPresentation', f'{row.get("kitId")} new presentation is not approved')
        check(bool(row.get('approvedName')) and bool(row.get('illustrationConcept')), f'{row.get("kitId")} new presentation is incomplete')
    check(supports.get('presentationDecision', {}).get('status') == 'ApprovedAndFixed', 'Trainer presentation decision is not fixed')

    energy = ledger.get('energy', {})
    energy_cards = energy.get('selectedEnergyCards', [])
    check(energy.get('status') == 'ApprovedComplete', 'Energy pool is not approved complete')
    check(len(energy_cards) == 7, 'selectedEnergyCards must contain seven cards')
    energy_ids = unique((card.get('sourceId') for card in energy_cards), 'selected Energy source IDs')
    check(energy_ids == {f'base1-{number}' for number in range(96, 103)}, 'Energy pool must be Base Set 96 through 102')
    check(Counter(tuple(card.get('subtypes', [])) for card in energy_cards) == Counter({('Basic',): 6, ('Special',): 1}), 'Energy pool must contain six Basic and one Special Energy')
    for card in energy_cards:
        value = {'subtypes': card.get('subtypes'), 'rules': card.get('rules')}
        check(card.get('mechanicalSha256') == canonical_sha256(value), f'{card.get("sourceId")} has a stale Energy rules digest')
    check(set(energy.get('selectedPoolSourceIds', [])) == energy_ids and len(energy.get('selectedPoolSourceIds', [])) == 7, 'selected Energy pool index is incomplete or duplicated')
    energy_presentations = energy.get('currentEntries', [])
    check(len(energy_presentations) == 7, 'there must be seven Energy presentations')
    check(unique((row.get('selectedSourceId') for row in energy_presentations), 'Energy presentation source IDs') == energy_ids, 'Energy presentations must map every selected Energy exactly once')
    check(
        {row.get('vimId'): row.get('selectedSourceId') for row in energy_presentations}
        == EXPECTED_ENERGY_PRESENTATION_SOURCE_BY_VIM_ID,
        'Energy presentation mappings differ from the approved fixed mappings'
    )
    dce_presentations = [row for row in energy_presentations if row.get('selectedSourceId') == 'base1-96']
    check(len(dce_presentations) == 1 and dce_presentations[0].get('approvedName') == 'Side Hustle', 'Side Hustle must be the one Double Colorless presentation')
    dce = energy.get('doubleColorlessPresentation', {})
    check(dce.get('status') == 'ApprovedPresentation' and dce.get('sourceId') == 'base1-96' and dce.get('approvedName') == 'Side Hustle', 'Double Colorless presentation decision is incomplete')

    inventory = ledger.get('inventory', {})
    expected_inventory = {
        'collectibles': 151,
        'collectiblesWithOnePrinting': 116,
        'collectiblesWithMechanicalVariants': 8,
        'collectiblesWithHoloNonHoloPairs': 26,
        'collectiblesWithPromoPrintingChoice': 1,
        'selectedCollectibles': 151,
        'reusedTrainerPresentations': 14,
        'newTrainerPresentations': 18,
        'selectedTrainers': 32,
        'energyPresentations': 7,
        'selectedBasicEnergy': 6,
        'selectedSpecialEnergy': 1,
        'selectedEnergyCards': 7
    }
    check(inventory == expected_inventory, 'inventory summary does not match validated selections')

    snapshots = ledger.get('sourceSnapshots', [])
    check(len(snapshots) == 4 and {snapshot.get('setId') for snapshot in snapshots} == {'base1', 'base2', 'base3', 'basep'}, 'source snapshots must pin Base Set, Jungle, Fossil, and Wizards promos')
    for snapshot in snapshots:
        check(re.fullmatch(r'[0-9a-f]{64}', snapshot.get('transcriptionSha256', '')) is not None, f'{snapshot.get("setId")} transcription digest is invalid')
        check(snapshot.get('reviewStatus') == 'DigestVerifiedAndSelectedPrintingsReviewed', f'{snapshot.get("setId")} source snapshot review is incomplete')

    rules = ledger.get('rulesOracle', {})
    rulebook = rules.get('generalRulesAuthority', {})
    check(rules.get('status') == 'PinnedAndPageReviewed', 'rules oracle is not pinned and page-reviewed')
    check(rulebook.get('title') == 'Pokémon Trading Card Game Advanced Rulebook' and rulebook.get('version') == 'Version 1', 'wrong rulebook identity')
    check(rulebook.get('locator') == EXPECTED_RULEBOOK_URL, 'wrong rulebook locator')
    check(rulebook.get('sha256') == EXPECTED_RULEBOOK_SHA256, 'wrong rulebook digest')
    check(rulebook.get('pdfPageCount') == 17, 'rulebook PDF page count must be 17')
    sequence = rulebook.get('numberedPageSequence', {})
    check((sequence.get('first'), sequence.get('last'), sequence.get('count')) == (1, 28, 28), 'rulebook numbered page inventory must be 1 through 28')
    page_reviews = rulebook.get('pageReviews', [])
    check(len(page_reviews) == 17 and unique((page.get('pdfPage') for page in page_reviews), 'rulebook PDF review pages') == set(range(1, 18)), 'every PDF page must have one review row')
    page_positions = [position for page in page_reviews for position in page.get('pagePositions', [])]
    check(unique(page_positions, 'rulebook numbered page positions') == set(range(1, 29)), 'rulebook page reviews must inventory positions 1 through 28')
    check(rulebook.get('reviewStatus') == 'All17PdfPagesInspected', 'rulebook page review status is incomplete')

    citations = rules.get('generalRuleCitations', [])
    check(len(citations) >= 1, 'general rule citations are missing')
    unique((citation.get('id') for citation in citations), 'general rule citation IDs')
    cited_pages = set()
    for citation in citations:
        pages = citation.get('numberedPages', [])
        check(bool(pages) and all(isinstance(page, int) and 1 <= page <= 28 for page in pages), f'{citation.get("id")} has an invalid page citation')
        check(bool(citation.get('citation')), f'{citation.get("id")} has no human-readable citation')
        cited_pages.update(pages)
    check((set(range(2, 28)) - {19}) <= cited_pages, 'rule-bearing numbered pages are not all represented by general rule citations')

    boundary = rules.get('officialRulingsBoundary', {})
    check(boundary.get('status') == 'ClosedEnumeratedSet', 'official ruling boundary is not closed')
    cutoff = boundary.get('cutoff', {})
    latest = date.fromisoformat(cutoff.get('latestIncludedDate', '0001-01-01'))
    first_excluded = date.fromisoformat(cutoff.get('firstExcludedDate', '0001-01-02'))
    check(latest == date(2000, 2, 23) and first_excluded == date(2000, 2, 24), 'rulings cutoff must be strictly before Base Set 2')
    rulings = boundary.get('rulings', [])
    unique((ruling.get('id') for ruling in rulings), 'bounded ruling IDs')
    selected_source_ids = {card.get('sourceId') for card in selected_cards} | trainer_ids | energy_ids
    for ruling in rulings:
        ruling_date = date.fromisoformat(ruling.get('wotcSourceDate'))
        check(ruling_date <= latest, f'{ruling.get("id")} is outside the approved cutoff')
        subjects = ruling.get('subjectSourceIds', [])
        check(bool(subjects) and set(subjects) <= selected_source_ids, f'{ruling.get("id")} cites an unselected card')
        check('WotC Chat' in ruling.get('sourceCitation', '') and bool(ruling.get('clarification')), f'{ruling.get("id")} lacks bounded Wizards provenance')
    exclusions = rules.get('explicitExclusions', [])
    exclusion_text = ' '.join(item.get('artifact', '') + ' ' + item.get('reason', '') for item in exclusions).lower()
    check('version 10' in exclusion_text and 'e-card' in exclusion_text, 'Advanced Rulebook v10 and e-card-era mechanics must be explicitly excluded')

    selected_source_by_id = {card.get('sourceId'): card for card in selected_cards + trainers + energy_cards}
    reviews = ledger.get('selectedPrintingReviews', [])
    check(len(reviews) == 190, 'selectedPrintingReviews must contain 190 rows')
    review_ids = unique((review.get('sourceId') for review in reviews), 'selected printing review source IDs')
    check(review_ids == set(selected_source_by_id), 'selected printing reviews must cover every and only selected card')
    review_digest_by_source_id = {review.get('sourceId'): review.get('sha256') for review in reviews}
    check(
        canonical_sha256(review_digest_by_source_id) == EXPECTED_SELECTED_PRINTING_DIGESTS_SHA256,
        'selected printing image digests differ from the approved fixed digest manifest'
    )
    for review in reviews:
        source = selected_source_by_id.get(review.get('sourceId'), {})
        check(review.get('status') == 'ReviewedAgainstLinkedEnglishCardImage', f'{review.get("sourceId")} image review is incomplete')
        check(review.get('printedName') == source.get('printedName'), f'{review.get("sourceId")} reviewed printed name differs')
        check(review.get('sourceImage') == source.get('sourceImage'), f'{review.get("sourceId")} reviewed image locator differs')
        check(isinstance(review.get('byteCount'), int) and review.get('byteCount') > 0, f'{review.get("sourceId")} reviewed image byte count is invalid')
        check(re.fullmatch(r'[0-9a-f]{64}', review.get('sha256', '')) is not None, f'{review.get("sourceId")} reviewed image digest is invalid')

    decisions = ledger.get('humanDecisions', [])
    expected_decisions = {
        'COLLECTIBLE-PRINTING-POLICY',
        'MEW-PROMO',
        'TRAINER-POOL',
        'ENERGY-POOL',
        'RULES-ORACLE',
        'TRAINER-AND-DCE-PRESENTATION'
    }
    check(unique((decision.get('id') for decision in decisions), 'human decision IDs') == expected_decisions, 'human decision inventory is incomplete')
    for decision in decisions:
        check(decision.get('status') == 'ApprovedAndApplied' and bool(decision.get('decision')), f'{decision.get("id")} is not approved and applied')
        check('question' not in decision and 'recommendation' not in decision, f'{decision.get("id")} retains unresolved decision fields')

    return errors


def main():
    default_path = Path(__file__).with_name('1999-kanto-authority-ledger.json')
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else default_path
    if len(sys.argv) > 2:
        raise SystemExit(f'usage: {Path(sys.argv[0]).name} [ledger.json]')
    try:
        ledger = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f'{path}: {error}') from error
    errors = validate(ledger)
    if errors:
        for error in errors:
            print(f'ERROR: {error}', file=sys.stderr)
        raise SystemExit(1)
    rules = ledger['rulesOracle']
    print(
        f'Validated {path}: '
        f'{len(ledger["collectibles"])} collectibles, '
        f'{len(ledger["supports"]["selectedTrainers"])} Trainers, '
        f'{len(ledger["energy"]["selectedEnergyCards"])} Energy cards, '
        f'{len(ledger["selectedPrintingReviews"])} printing reviews, '
        f'{len(rules["generalRulesAuthority"]["pageReviews"])} rulebook PDF pages, '
        f'{len(rules["generalRuleCitations"])} general-rule citations, and '
        f'{len(rules["officialRulingsBoundary"]["rulings"])} bounded rulings.'
    )


if __name__ == '__main__':
    main()
