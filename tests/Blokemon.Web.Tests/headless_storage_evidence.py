"""Headless evidence that a page left while its browser storage is still opening does not block
the next.

Driven by HeadlessStorageTests, which starts the host and hands this script its origin. Every page
opens the browser's IndexedDB as soon as its shell has rendered, and the first open of all creates
the database. A page that the browser puts into its back/forward cache during that creation would
keep the creation frozen with it, and the next page in the tab would wait for the database forever.
So: a first page is left the moment its open begins, and the Home page that follows must still
offer the choice of where to save the game, with the database created. Chrome runs headless only.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402

DATABASE = "blokemon-browser-local-v1"

# Leaves the page the moment it asks for its database, before any of the app's script runs: the
# navigation starts while the open is still pending, which is the race a person wins by pressing a
# link the instant the page appears. A page still loading is never cached, so a page that asks
# before its load event leaves on that event instead.
LEAVE_ON_FIRST_OPEN = """
window.__blokemonOpens = 0;
const open = IDBFactory.prototype.open;
IDBFactory.prototype.open = function (...args) {
    const request = open.apply(this, args);
    if (window.__blokemonOpens++ === 0 && location.pathname !== "/") {
        const leave = () => location.assign("/");
        if (document.readyState === "complete") {
            leave();
        } else {
            window.addEventListener("load", leave);
        }
    }
    return request;
};
"""


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def leave_during_the_first_open(devtools, origin):
    devtools.command("Page.enable")
    devtools.command("Page.addScriptToEvaluateOnNewDocument", {"source": LEAVE_ON_FIRST_OPEN})
    devtools.command("Page.navigate", {"url": f"{origin}/privacy"})
    devtools.wait_for(
        "location.pathname === '/' && document.readyState === 'complete'",
        "the Home page after leaving the first page on its storage open",
        timeout=60,
    )


def home_still_offers_the_choice(devtools):
    devtools.wait_for(
        "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Use this browser')",
        "the choice of where to save the game",
        timeout=30,
    )
    require(
        devtools.evaluate(f"indexedDB.databases().then(all => all.some(db => db.name === {json.dumps(DATABASE)}))") is True,
        "the Home page created the database the left page never finished",
    )
    print("PASS a page left during its first storage open does not block the next")


def main():
    origin = env("BLOKEMON_ORIGIN").rstrip("/")
    with tempfile.TemporaryDirectory(prefix="blokemon-headless-storage-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.set_viewport(1440, 900)
            leave_during_the_first_open(devtools, origin)
            home_still_offers_the_choice(devtools)
        finally:
            chrome.close()
    print("HEADLESS STORAGE EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
