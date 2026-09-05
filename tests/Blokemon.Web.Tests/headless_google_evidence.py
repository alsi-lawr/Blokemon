#!/usr/bin/env python3
"""Headless Google sign-in check (BLOKEMON-164) against a running Blokemon.Web.

Driven by HeadlessGoogleTests, which hosts Blokemon.Web on Kestrel at a known origin with the
Google provider enabled against a stub of Google's endpoints on the same host, and hands this
script that origin through the environment. Chrome runs headless only. The stub sends the
browser straight back with a code, so the round trip exercises the start route, the callback,
the continuation page and the exchange exactly as the real one does, minus Google's own pages.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_session_evidence import activate, close_menu, identity_text, open_menu  # noqa: E402

SESSION_KEY = "blokemon.session"
PLAYER = "Googly Player"


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def held_session(devtools):
    raw = devtools.evaluate(f"sessionStorage.getItem({json.dumps(SESSION_KEY)})")
    return json.loads(raw) if raw else None


def controls(devtools):
    return devtools.evaluate("[...document.querySelectorAll('.sign-in-providers button, .sign-in-providers a')].map(e => e.textContent.trim())")


def sign_in_with_google(devtools, origin, label):
    devtools.navigate(origin, "/signin", ready_selector=".sign-in-providers")
    devtools.wait_for("[...document.querySelectorAll('.sign-in-providers a')].some(a => a.textContent.trim() === 'Sign in with Google')", f"{label}: the Google link", timeout=30)
    texts = controls(devtools)
    require(texts[:4] == ["Sign in", "Sign in with a passkey", "Sign in with Google", "Create an account"], f"{label}: the login, the passkey, Google, then create ({texts})")
    link = devtools.evaluate("(() => { const a = [...document.querySelectorAll('.sign-in-providers a')].find(a => a.textContent.trim() === 'Sign in with Google'); const r = a.getBoundingClientRect(); return { href: a.getAttribute('href'), target: a.target, height: r.height }; })()")
    require(link["href"] == "api/session/google/start?slug=core" and link["target"] == "_top", f"{label}: the link is the server's start route, top-level ({link})")
    require(link["height"] >= 44, f"{label}: the link is a real target ({link})")
    activate(devtools, "Sign in with Google", selector="a")
    devtools.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", f"{label}: home after the round trip", timeout=90)
    require("handoff=" not in devtools.evaluate("location.href"), f"{label}: the code left the URL")
    held = held_session(devtools)
    require(held is not None and held.get("recovery") is False, f"{label}: the browser holds a first-party session")
    open_menu(devtools)
    require(identity_text(devtools) == PLAYER, f"{label}: signed in as {PLAYER}")
    close_menu(devtools)


def sign_out(devtools, origin):
    devtools.navigate(origin, "/")
    open_menu(devtools)
    activate(devtools, "Sign out")
    devtools.wait_for(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) === null", "the browser dropped its session on sign-out")


def diagnostics(devtools):
    try:
        location = devtools.evaluate("location.href")
        body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 400) : null")
    except Exception as error:  # noqa: BLE001
        return f" | diagnostics unavailable ({error})"
    calls = []
    for event in devtools.events:
        if event.get("method") == "Network.responseReceived":
            response = event["params"]["response"]
            if "/api/" in response["url"] or "stub-google" in response["url"]:
                calls.append((response["url"].split("://", 1)[1].split("/", 1)[1].split("?")[0], response["status"]))
    return f" | at {location} | body={body!r} | calls={json.dumps(calls[-10:])}"


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-google-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.command("Network.enable")
            devtools.set_viewport(1440, 900)
            sign_in_with_google(devtools, origin, "desktop")
            devtools.wait_for("document.body.textContent.includes('Choose your first deck.')", "the new player's game on the server", timeout=60)
            # The account came in by Google: no player name yet, and a passkey can be added.
            devtools.navigate(origin, "/profile", ready_selector=".login-panel")
            devtools.wait_for("document.querySelector('.login-panel .login-name') !== null", "the login panel", timeout=30)
            require(devtools.evaluate("document.querySelector('.login-panel .login-name').textContent.trim()") == "None yet", "the Google account has no player name yet")
            devtools.wait_for("[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Add a passkey')", "the passkey offer on the profile", timeout=30)
            sign_out(devtools, origin)
            devtools.set_viewport(412, 915, touch=True)
            sign_in_with_google(devtools, origin, "touch")
            # The create page offers the same link.
            devtools.navigate(origin, "/signin/create", ready_selector="#player-name")
            devtools.wait_for("[...document.querySelectorAll('.create-account a')].some(a => a.textContent.trim() === 'Sign in with Google')", "the Google link on the create page", timeout=30)
        except EvidenceFailure as failure:
            raise EvidenceFailure(f"{failure}{diagnostics(chrome.devtools)}") from failure
        finally:
            chrome.close()
    print("HEADLESS GOOGLE EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
