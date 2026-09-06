"""Headless evidence that the computer thinks in its own runtime, by the page's rules, without
holding the page's thread.

Driven by HeadlessComputerTests, which starts the host, saves a battle with the computer to act,
decides that battle's next move in its own process, and hands this script the origin, the saved
battle, the candidate it expects and the rules version the page plays by. On a page of the site,
the site's own computerThinking.js module boots the worker from the site's framework files and is
asked for the decision. The answer must be the one the policy gave in process, the rules the worker
reports must be the page's, and the page's thread must run no long task while the worker thinks.
Chrome runs headless only.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402

# Watches the page's thread while the worker thinks: every task the browser reports as long (over
# fifty milliseconds), and the longest gap between ticks of a timer that should fire constantly.
WATCH = """
window.__blokemonLong = [];
window.__blokemonGaps = [];
window.__blokemonLast = performance.now();
window.__blokemonObserver = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
        window.__blokemonLong.push(Math.round(entry.duration));
    }
});
window.__blokemonObserver.observe({ entryTypes: ["longtask"] });
window.__blokemonTicker = setInterval(() => {
    const now = performance.now();
    window.__blokemonGaps.push(now - window.__blokemonLast);
    window.__blokemonLast = now;
}, 8);
true
"""

WATCHED = """
(() => {
    clearInterval(window.__blokemonTicker);
    window.__blokemonObserver.disconnect();
    return {
        longTasks: window.__blokemonLong,
        ticks: window.__blokemonGaps.length,
        longestGap: Math.round(Math.max(0, ...window.__blokemonGaps)),
    };
})()
"""


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def load_the_computer(devtools, origin, authority):
    devtools.navigate(origin, "/")
    supported = devtools.evaluate(
        "import('/computerThinking.js').then((module) => { window.__blokemonComputer = module; return module.supported(); })"
    )
    require(supported is True, "the page's browser can run a worker")
    versions = devtools.evaluate("window.__blokemonComputer.start('content/catalogue.json')")
    require(
        isinstance(versions, dict) and versions.get("authorityVersion") == authority,
        f"the worker plays by the page's rules ({versions!r})",
    )
    print(f"PASS the worker booted from the site's own framework and reports rules {authority}")


def the_worker_decides(devtools, battle, expected):
    devtools.evaluate(WATCH)
    decided = devtools.evaluate(f"window.__blokemonComputer.decide({json.dumps(battle)})")
    watched = devtools.evaluate(WATCHED)
    require(decided == expected, f"the worker decided {decided!r}; the policy decided {expected!r}")
    print(f"PASS the worker decided {decided}, as the policy did in process")
    require(
        watched["longTasks"] == [],
        f"the page's thread ran long tasks while the worker thought: {watched['longTasks']} ms",
    )
    print(
        f"PASS the page's thread ran no long task while the worker thought "
        f"({watched['ticks']} ticks, longest gap {watched['longestGap']} ms)"
    )
    devtools.evaluate("window.__blokemonComputer.stop(); true")


def main():
    origin = env("BLOKEMON_ORIGIN").rstrip("/")
    battle = Path(env("BLOKEMON_BATTLE")).read_text(encoding="utf-8")
    expected = env("BLOKEMON_EXPECTED")
    authority = env("BLOKEMON_AUTHORITY")
    with tempfile.TemporaryDirectory(prefix="blokemon-headless-computer-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.set_viewport(1440, 900)
            load_the_computer(devtools, origin, authority)
            the_worker_decides(devtools, battle, expected)
        finally:
            chrome.close()
    print("HEADLESS COMPUTER EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
