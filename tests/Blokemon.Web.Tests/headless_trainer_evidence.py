#!/usr/bin/env python3
"""Headless checks of Trainers coming out of packs and being owned (BLOKEMON-156, BLOKEMON-157)
against a running Blokemon.Web.

Driven by HeadlessTrainerTests, which hosts Blokemon.Web on Kestrel and hands this script its
origin. The browser game creates a player, claims a starter and opens packs with reduced motion on,
so each opening lands on its summary; every pack shows eleven distinct cards through the card face,
at least two of them Trainers, and the last pack lists the same eleven afterwards, at desktop and on
a phone. The collection then shows each pulled Trainer with an owned count and its Owned view keeps
only Trainers with one, and the deck builder lets a pulled Trainer into a new deck exactly as many
times as it is owned. Chrome runs headless only.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_published_evidence import choose, wait_chooser  # noqa: E402
from headless_session_evidence import activate  # noqa: E402


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def faces(devtools, scope):
    """The card faces inside a scope: each face's canonical id, its printed kind, and whether it
    sits inside the viewport."""
    return devtools.evaluate(
        f"""[...document.querySelectorAll({json.dumps(scope)} + ' .card-face-host article.blokemon-gym-card')].map(a => {{
            const r = a.getBoundingClientRect();
            return {{ id: a.dataset.canonicalId, type: a.dataset.cardType, fits: r.left >= 0 && r.right <= innerWidth }};
        }})"""
    )


def create_player(devtools, origin):
    devtools.navigate(origin, "/")
    wait_chooser(devtools, "the two-mode chooser")
    choose(devtools, "browser")
    devtools.wait_for("document.querySelector('a[href=\"profile\"]') !== null", "the create-player link", timeout=30)
    activate(devtools, "Create player")
    devtools.wait_for("document.querySelector('#display-name') !== null", "the profile form", timeout=30)
    devtools.set_value("#display-name", "Trainer Player")
    activate(devtools, "Create player")
    devtools.wait_for(
        "[...document.querySelectorAll('.starter-option button')].some(b => b.textContent.trim().startsWith('Open '))",
        "the starter catalogue",
        timeout=30,
    )


def claim_starter(devtools):
    require(
        devtools.evaluate(
            """
            (() => {
              const button = [...document.querySelectorAll('.starter-option > button')]
                .find(candidate => candidate.textContent.trim().startsWith('Open '));
              if (!button) return false;
              button.click();
              return true;
            })()
            """
        ),
        "claimed one starter",
    )
    devtools.wait_for("document.querySelector('.starter-option') === null || [...document.querySelectorAll('.starter-option > button')].every(b => b.disabled)", "the starter claimed", timeout=30)


def open_pack(devtools, origin, viewport):
    """Opens one pack and returns the eleven faces its summary showed, after checking them."""
    devtools.navigate(origin, "/packs")
    activate(devtools, "Open pack")
    devtools.wait_for("document.querySelector('.opening-summary-grid') !== null", "the pack summary", timeout=30)
    dealt = faces(devtools, ".opening-summary-grid")
    require(len(dealt) == 11, f"{viewport}: the summary shows eleven card faces ({len(dealt)})")
    require(len({card["id"] for card in dealt}) == 11, f"{viewport}: the eleven faces are distinct cards")
    trainers = [card for card in dealt if card["type"] == "Trainer"]
    require(len(trainers) >= 2, f"{viewport}: at least two of the eleven are Trainers ({len(trainers)})")
    require(all(card["fits"] for card in dealt), f"{viewport}: every summary face sits inside the viewport")
    activate(devtools, "Done")
    devtools.wait_for("document.querySelector('.recent-pack-cards') !== null", "the last pack", timeout=30)
    recent = faces(devtools, ".recent-pack-cards")
    require(
        [card["id"] for card in recent] == [card["id"] for card in dealt],
        f"{viewport}: the last pack lists the same eleven cards in the same order",
    )
    # The last-pack strip scrolls sideways on a phone, so only the summary is held to the viewport.
    return dealt


def quantity_shown(devtools, card_id):
    """The owned count a collection tile shows for a card: the digits in its quantity badge."""
    return devtools.evaluate(
        f"""(() => {{
            const tile = [...document.querySelectorAll('.collection-grid .card-tile')]
              .find(t => t.querySelector('article.blokemon-gym-card')?.dataset.canonicalId === {json.dumps(card_id)});
            if (!tile) return null;
            const digits = (tile.querySelector('.qty')?.textContent ?? '').match(/\d+/);
            return digits ? Number(digits[0]) : null;
        }})()"""
    )


def collection_counts(devtools, origin, pulled, viewport):
    """Every pulled Trainer's tile shows at least the copies the packs dealt, and the Owned view
    keeps a Trainer only when its count is above zero."""
    devtools.navigate(origin, "/collection")
    devtools.wait_for("document.querySelector('.collection-grid article.blokemon-gym-card') !== null", "the collection", timeout=30)
    dealt = {}
    for card in pulled:
        dealt[card["id"]] = dealt.get(card["id"], 0) + 1
    for card_id, copies in dealt.items():
        shown = quantity_shown(devtools, card_id)
        require(shown is not None and shown >= copies, f"{viewport}: {card_id} shows an owned count of at least {copies} ({shown})")
    activate(devtools, "Owned")
    devtools.wait_for("document.querySelector('.toolbar button.selected') !== null", "the Owned view")
    owned_trainers = devtools.evaluate(
        """[...document.querySelectorAll('.collection-grid .card-tile')]
            .filter(t => t.querySelector('article.blokemon-gym-card')?.dataset.cardType === 'Trainer')
            .map(t => ({ id: t.querySelector('article.blokemon-gym-card').dataset.canonicalId, digits: ((t.querySelector('.qty')?.textContent ?? '').match(/\d+/) || [null])[0] }))"""
    )
    require(owned_trainers, f"{viewport}: the Owned view lists Trainers")
    require(all(card["digits"] is not None and int(card["digits"]) > 0 for card in owned_trainers), f"{viewport}: every Trainer in the Owned view has an owned count above zero")
    require({card["id"] for card in pulled} <= {card["id"] for card in owned_trainers}, f"{viewport}: every pulled Trainer is in the Owned view")
    activate(devtools, "Owned")


def deck_builder_cap(devtools, origin, pulled, viewport):
    """A pulled Trainer outside the starter list can be added to a new deck exactly as many times
    as the packs dealt it, then the add step disables."""
    devtools.navigate(origin, "/decks")
    devtools.wait_for("document.querySelector('.deck-line article.blokemon-gym-card') !== null", "the starter deck list", timeout=30)
    listed = set(devtools.evaluate("[...document.querySelectorAll('.deck-line article.blokemon-gym-card')].map(a => a.dataset.canonicalId)"))
    dealt = {}
    for card in pulled:
        if card["type"] == "Trainer" and card["id"] not in listed:
            dealt[card["id"]] = dealt.get(card["id"], 0) + 1
    require(dealt, f"{viewport}: a pulled Trainer sits outside the starter list ({sorted(listed)})")
    card_id, owned = sorted(dealt.items())[0]
    activate(devtools, "New deck")
    devtools.wait_for("[...document.querySelectorAll('.deck-line')].length === 0", "an empty new deck", timeout=30)
    stepper = f"""(() => {{
        const card = [...document.querySelectorAll('.catalogue-card')]
          .find(c => c.querySelector('article.blokemon-gym-card')?.dataset.canonicalId === {json.dumps(card_id)});
        if (!card) return null;
        const buttons = card.querySelectorAll('.stepper button');
        return {{ count: Number(card.querySelector('.stepper output').textContent.trim()), addDisabled: buttons[buttons.length - 1].disabled }};
    }})()"""
    for step in range(owned):
        state = devtools.evaluate(stepper)
        require(state is not None and state["count"] == step and not state["addDisabled"], f"{viewport}: {card_id} at {step} of {owned} can be added ({state})")
        devtools.evaluate(f"""(() => {{
            const card = [...document.querySelectorAll('.catalogue-card')]
              .find(c => c.querySelector('article.blokemon-gym-card')?.dataset.canonicalId === {json.dumps(card_id)});
            const buttons = card.querySelectorAll('.stepper button');
            buttons[buttons.length - 1].click();
        }})()""")
        devtools.wait_for(f"({stepper})?.count === {step + 1}", f"{card_id} added ({step + 1})")
    state = devtools.evaluate(stepper)
    require(state["count"] == owned and state["addDisabled"], f"{viewport}: {card_id} stops at its {owned} owned copies ({state})")


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-trainer-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.set_viewport(1440, 900)
            # With reduced motion the opening goes straight to its summary; the ceremony itself is
            # the card harness's concern.
            devtools.set_reduced_motion(True)
            create_player(devtools, origin)
            claim_starter(devtools)
            desktop = open_pack(devtools, origin, "1440x900")
            devtools.set_viewport(412, 915, touch=True)
            phone = open_pack(devtools, origin, "412x915")
            require(
                [card["id"] for card in desktop] != [card["id"] for card in phone],
                "the second pack is its own draw",
            )
            pulled = [card for card in desktop + phone if card["type"] == "Trainer"]
            collection_counts(devtools, origin, pulled, "412x915")
            deck_builder_cap(devtools, origin, pulled, "412x915")
            devtools.set_viewport(1440, 900)
            collection_counts(devtools, origin, pulled, "1440x900")
            deck_builder_cap(devtools, origin, pulled, "1440x900")
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 300) : null")
            except Exception:  # noqa: BLE001
                where, body = "?", "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r}") from failure
        finally:
            chrome.close()
    print("HEADLESS TRAINER EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
