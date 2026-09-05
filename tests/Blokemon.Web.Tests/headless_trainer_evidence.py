#!/usr/bin/env python3
"""Headless checks of Trainers coming out of packs (BLOKEMON-156) against a running Blokemon.Web.

Driven by HeadlessTrainerTests, which hosts Blokemon.Web on Kestrel and hands this script its
origin. The browser game creates a player and opens packs with reduced motion on, so each opening
lands on its summary; every pack shows eleven distinct cards through the card face, at least two of
them Trainers, and the last pack lists the same eleven afterwards, at desktop and on a phone. Chrome
runs headless only.
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
            desktop = open_pack(devtools, origin, "1440x900")
            devtools.set_viewport(412, 915, touch=True)
            phone = open_pack(devtools, origin, "412x915")
            require(
                [card["id"] for card in desktop] != [card["id"] for card in phone],
                "the second pack is its own draw",
            )
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
