#!/usr/bin/env python3
"""Headless smoke of the published Blokemon.Web host (BLOKEMON-155).

Driven by PublishedHostTests, which publishes and starts the host and hands this script its
origin. A signed-out visitor sees both modes at the root; choosing the server lands on the
sign-in page with nothing saved; keeping the browser returns to the chooser; the browser game
creates a player and opens a pack. Throughout, the browser's own network log shows no request
to a circuit, and the responses carry the framing, caching and baseline headers and a
compressed WebAssembly payload. Chrome runs headless only.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_session_evidence import SESSION_KEY, activate, drain  # noqa: E402


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def responses(devtools):
    drain(devtools)
    found = []
    for event in devtools.events:
        if event.get("method") != "Network.responseReceived":
            continue
        response = event["params"]["response"]
        found.append((response["url"], {key.lower(): value for key, value in response["headers"].items()}))
    return found


def requested_urls(devtools):
    drain(devtools)
    return [event["params"]["request"]["url"] for event in devtools.events if event.get("method") == "Network.requestWillBeSent"]


def chooser_choices(devtools):
    """The chooser's controls by the choice each names (its data-choice hook), not its words."""
    return devtools.evaluate("[...document.querySelectorAll('.hero .primary')].map(b => b.dataset.choice)")


def choose(devtools, choice):
    require(devtools.evaluate(f"(() => {{ const b = document.querySelector('.hero .primary[data-choice={json.dumps(choice)}]'); if (!b || b.disabled) return false; b.click(); return true; }})()"), f"chose {choice!r}")


def wait_chooser(devtools, what):
    devtools.wait_for("document.querySelectorAll('.hero .primary').length === 2", what, timeout=60)


def signed_out_server_choice(devtools, origin):
    devtools.navigate(origin, "/")
    wait_chooser(devtools, "the two-mode chooser")
    require(chooser_choices(devtools) == ["sign-in", "browser"], f"a signed-out visitor is offered sign-in for the server and the browser game ({chooser_choices(devtools)})")
    choose(devtools, "sign-in")
    devtools.wait_for("location.pathname === '/signin' && document.querySelector('.sign-in') !== null", "the sign-in page", timeout=30)
    require(devtools.evaluate("document.querySelector('.sign-in-situation') === null"), "the sign-in page carries no situation line for a deliberate choice")
    require(devtools.evaluate(f"sessionStorage.getItem({SESSION_KEY!r}) === null"), "no session is held")
    activate(devtools, "Keep playing in this browser")
    wait_chooser(devtools, "the chooser again, nothing chosen")
    require(chooser_choices(devtools) == ["sign-in", "browser"], "returning shows the chooser with no mode saved")
    # A reload agrees: nothing was saved.
    devtools.command("Page.reload", {"ignoreCache": True})
    devtools.wait_for("document.readyState === 'complete'", "reload")
    wait_chooser(devtools, "the chooser after a reload")


def browser_local_journey(devtools, origin):
    choose(devtools, "browser")
    devtools.wait_for("document.querySelector('a[href=\"profile\"]') !== null", "the create-player link", timeout=30)
    activate(devtools, "Create player")
    devtools.wait_for("document.querySelector('#display-name') !== null", "the profile form", timeout=30)
    # A signed-out visitor choosing the server from the player form is sent to sign in, as from
    # the chooser: the form is not offered against a server that would refuse it.
    require(devtools.evaluate("[...document.querySelectorAll('.setup-card button')].some(b => b.textContent.trim() === 'Sign in to use this server')"), "the player form offers sign-in for the server while signed out")
    activate(devtools, "Sign in to use this server")
    devtools.wait_for("location.pathname === '/signin' && document.querySelector('.sign-in') !== null", "the sign-in page from the player form", timeout=30)
    require(devtools.evaluate("document.querySelector('#display-name') === null"), "no player form is shown on the way to sign in")
    activate(devtools, "Keep playing in this browser")
    devtools.wait_for("document.querySelector('a[href=\"profile\"]') !== null", "the create-player link again", timeout=30)
    activate(devtools, "Create player")
    devtools.wait_for("document.querySelector('#display-name') !== null", "the profile form again", timeout=30)
    devtools.set_value("#display-name", "Published Player")
    activate(devtools, "Create player")
    devtools.wait_for("[...document.querySelectorAll('.starter-option button')].some(b => b.textContent.trim().startsWith('Open '))", "the starter catalogue", timeout=30)
    # The pack opens as the card harness opens it: with motion on, the sealed pack is pressed.
    devtools.set_reduced_motion(False)
    devtools.navigate(origin, "/packs")
    devtools.wait_for("[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Open pack')", "the pack order", timeout=30)
    activate(devtools, "Open pack")
    devtools.wait_for("document.querySelector('button.opening-pack') !== null", "the sealed pack", timeout=30)
    point = devtools.evaluate("(() => { const r = document.querySelector('button.opening-pack').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()")
    devtools.mouse_click(point)
    devtools.wait_for("document.querySelector('.pack-reveal-card') !== null", "the first reveal card", timeout=30)


def network_facts(devtools, origin):
    urls = requested_urls(devtools)
    require(urls and all("/_blazor" not in url for url in urls), f"no request went to a circuit ({len(urls)} requests)")
    seen = responses(devtools)
    shell = next((headers for url, headers in seen if url == f"{origin}/"), None)
    require(shell is not None, "the shell's response was observed")
    require(shell.get("content-security-policy") == "frame-ancestors 'none'", f"the root is not framable ({shell.get('content-security-policy')})")
    require("x-frame-options" not in shell, "no X-Frame-Options accompanies the policy")
    require("no-store" in shell.get("cache-control", "") or "no-cache" in shell.get("cache-control", ""), f"the shell is revalidated ({shell.get('cache-control')})")
    require(shell.get("x-content-type-options") == "nosniff" and shell.get("referrer-policy") == "no-referrer", "the baseline headers travel with the shell")
    wasm = next((headers for url, headers in seen if url.endswith(".wasm") and "dotnet.native" in url), None)
    require(wasm is not None, "the native runtime was fetched")
    require(wasm.get("content-encoding") in ("br", "gzip"), f"the WebAssembly payload arrived compressed ({wasm.get('content-encoding')})")
    require(wasm.get("cache-control") == "public, max-age=31536000, immutable", f"the fingerprinted payload is immutable ({wasm.get('cache-control')})")
    css = next((headers for url, headers in seen if "/css/" in url), None)
    require(css is not None and ("no-cache" in css.get("cache-control", "") or "immutable" in css.get("cache-control", "")), f"a stylesheet carries a caching decision ({css and css.get('cache-control')})")
    art = next((headers for url, headers in seen if "/art/" in url), None)
    require(art is not None and "no-cache" in art.get("cache-control", ""), f"card art is revalidated ({art and art.get('cache-control')})")
    signin = next((headers for url, headers in seen if url == f"{origin}/signin"), None)
    require(signin is None or signin.get("content-security-policy") == "frame-ancestors 'none'", "the sign-in page is not framable")


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-published-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Network.enable")
            devtools.command("Runtime.enable")
            devtools.set_viewport(1440, 900)
            devtools.set_reduced_motion(True)
            signed_out_server_choice(devtools, origin)
            browser_local_journey(devtools, origin)
            network_facts(devtools, origin)
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 300) : null")
            except Exception:  # noqa: BLE001
                where, body = "?", "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r}") from failure
        finally:
            chrome.close()
    print("HEADLESS PUBLISHED EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
