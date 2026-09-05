"""Headless evidence for the match table on a phone.

Driven by HeadlessTableTests, which starts the host and hands this script its origin. The browser
game creates a player, claims a starter and starts a battle; then, at a Pixel's screen and at the
shorter screens a browser toolbar or a smaller phone leaves, the table fits what it has - nothing
scrolls, nothing lies over the hand - and it keeps its drawn size where there is room for it. In
full screen, a finger held on a card in hand raises the card's viewer over the table, which is the
element that went full screen. Chrome runs headless only.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402

# The screen the phone table is drawn for, and two shorter ones: a Pixel under Chrome's toolbar,
# and a small phone.
DRAWN = (412, 915)
SHORTER = ((412, 780), (360, 640))


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def reach_table(devtools, origin):
    devtools.set_viewport(*DRAWN, touch=True)
    devtools.set_reduced_motion(True)
    devtools.navigate(origin, "/")
    devtools.wait_for(
        "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Use this browser')",
        "browser-local mode choice",
    )
    devtools.click_text("Use this browser")
    devtools.wait_for("document.querySelector('a[href=\"profile\"]') !== null", "profile route link")
    devtools.click_text("Create player")
    devtools.wait_for("document.querySelector('#display-name') !== null", "profile form")
    devtools.set_value("#display-name", "Table Player")
    devtools.click_text("Create player")
    devtools.wait_for(
        "[...document.querySelectorAll('.starter-option button')].some(button => button.textContent.trim().startsWith('Open '))",
        "starter catalogue",
    )
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
    devtools.wait_for(
        "[...document.querySelectorAll('h1')].some(heading => heading.textContent.trim() === 'Your deck is ready.')",
        "claimed starter state",
        timeout=30,
    )
    devtools.navigate(origin, "/match")
    devtools.wait_for("document.querySelector('#match-deck') !== null", "match start form", timeout=30)
    devtools.click_text("Start battle")
    devtools.wait_for("document.querySelector('.battle-screen') !== null", "battle screen", timeout=30)
    settle(devtools)


def settle(devtools):
    """The opening deals both hands; the table is measured once the player's is on it."""
    for _ in range(20):
        if devtools.evaluate("document.querySelectorAll('.hand-card').length > 0"):
            break
        for control in ("Continue", "Skip animation"):
            try:
                devtools.click_text(control)
                break
            except EvidenceFailure:
                continue
        time.sleep(0.6)
    devtools.wait_for("document.querySelectorAll('.hand-card').length > 0", "the player's hand on the table", timeout=30)
    time.sleep(0.6)


GEOMETRY = """
(() => {
  const screen = document.querySelector('.battle-screen');
  const canvas = document.querySelector('.battle-canvas');
  const box = screen.getBoundingClientRect();
  const within = (selector) => [...document.querySelectorAll(selector)].every(element => {
    const rect = element.getBoundingClientRect();
    return rect.top >= box.top - 1 && rect.bottom <= box.bottom + 1;
  });
  const hand = document.querySelector('.hand-zone').getBoundingClientRect();
  const benches = [...document.querySelectorAll('.player-zone .bench-row')].map(element => element.getBoundingClientRect());
  return {
    scrolls: screen.scrollHeight > screen.clientHeight + 1 || screen.scrollWidth > screen.clientWidth + 1,
    canvasFills: Math.abs(canvas.getBoundingClientRect().height - screen.clientHeight) <= 1,
    piecesWithin: within('.bench-row, .active-slot, .hand-zone, .deck-stack, .prize-rack, .empties-tray'),
    benchClearOfHand: benches.every(bench => bench.bottom <= hand.top + 1),
    fit: canvas.currentCSSZoom ?? Number.parseFloat(getComputedStyle(canvas).zoom),
  };
})()
"""


def table_fits(devtools, origin, width, height, drawn):
    devtools.set_viewport(width, height, touch=True)
    devtools.navigate(origin, "/match")
    devtools.wait_for("document.querySelector('.battle-screen') !== null", f"{width}x{height} battle screen", timeout=30)
    settle(devtools)
    geometry = devtools.evaluate(GEOMETRY)
    label = f"{width}x{height}"
    require(not geometry["scrolls"], f"{label} nothing on the table scrolls")
    require(geometry["canvasFills"], f"{label} the table fills its screen")
    require(geometry["piecesWithin"], f"{label} every piece is on the screen")
    require(geometry["benchClearOfHand"], f"{label} the Bench is clear of the hand")
    if drawn:
        require(geometry["fit"] == 1, f"{label} the table keeps its drawn size ({geometry['fit']})")
    else:
        require(0 < geometry["fit"] < 1, f"{label} the table is scaled to fit ({geometry['fit']})")


def fullscreen_hold(devtools, origin):
    devtools.set_viewport(*SHORTER[0], touch=True)
    devtools.navigate(origin, "/match")
    devtools.wait_for("document.querySelector('.battle-screen') !== null", "battle screen for full screen", timeout=30)
    settle(devtools)
    toggle = devtools.evaluate(
        "(() => { const box = document.querySelector('.fullscreen-toggle').getBoundingClientRect(); return {x: box.left + box.width / 2, y: box.top + box.height / 2}; })()"
    )
    devtools.mouse_click(toggle)
    devtools.wait_for(
        "document.fullscreenElement !== null && document.fullscreenElement.classList.contains('battle-screen')",
        "the table went full screen",
    )
    card = devtools.evaluate(
        "(() => { const surface = document.querySelector('.hand-card .card-press-surface'); const box = surface.getBoundingClientRect(); return {x: box.left + box.width / 2, y: box.top + box.height / 2}; })()"
    )
    devtools.touch("touchStart", card)
    try:
        devtools.wait_for("document.querySelector('.card-viewer') !== null", "a held card's viewer in full screen", timeout=5)
        shown = devtools.evaluate(
            """
            (() => {
              const viewer = document.querySelector('.card-viewer');
              const box = viewer.getBoundingClientRect();
              return {
                inside: document.fullscreenElement.contains(viewer),
                visible: viewer.checkVisibility(),
                covers: box.width >= innerWidth - 1 && box.height >= innerHeight - 1,
              };
            })()
            """
        )
    finally:
        devtools.touch("touchEnd")
    require(shown["inside"], "the viewer is inside the element that went full screen")
    require(shown["visible"] and shown["covers"], "the held card's viewer is shown over the full-screen table")
    viewer = devtools.evaluate(
        "(() => { const box = document.querySelector('.card-viewer').getBoundingClientRect(); return {x: box.left + box.width / 2, y: box.top + box.height / 2}; })()"
    )
    devtools.touch_tap(viewer)
    devtools.wait_for("document.querySelector('.card-viewer') === null", "the held card put down")
    devtools.evaluate("document.exitFullscreen()")
    devtools.wait_for("document.fullscreenElement === null", "the table left full screen")


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-table-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            reach_table(devtools, origin)
            table_fits(devtools, origin, *DRAWN, drawn=True)
            for width, height in SHORTER:
                table_fits(devtools, origin, width, height, drawn=False)
            fullscreen_hold(devtools, origin)
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 300) : null")
            except Exception:  # noqa: BLE001
                where, body = "?", "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r}") from failure
        finally:
            chrome.close()
    print("HEADLESS TABLE EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
