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
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_session_evidence import activate, close_menu, identity_text, open_menu  # noqa: E402

SESSION_KEY = "blokemon.session"
PLAYER = "Googly Player"


ERROR_COLLECTOR = """
window.__blokemonErrors = [];
const __push = (kind, text) => { try { window.__blokemonErrors.push(kind + ": " + String(text).slice(0, 800)); } catch (e) {} };
window.addEventListener("error", e => __push("error", e.message + " @ " + e.filename + ":" + e.lineno));
window.addEventListener("unhandledrejection", e => __push("rejection", e.reason && (e.reason.stack || e.reason.message || e.reason)));
const __error = console.error.bind(console);
console.error = (...args) => { __push("console", args.map(a => (a && a.stack) || String(a)).join(" ")); __error(...args); };
"""


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


def sign_in_page_link(devtools, origin, label):
    devtools.navigate(origin, "/signin", ready_selector=".sign-in-providers")
    devtools.wait_for("[...document.querySelectorAll('.sign-in-providers a')].some(a => a.textContent.trim() === 'Sign in with Google')", f"{label}: the Google link", timeout=30)
    texts = controls(devtools)
    require(texts[:4] == ["Sign in", "Sign in with a passkey", "Sign in with Google", "Create an account"], f"{label}: the login, the passkey, Google, then create ({texts})")
    link = devtools.evaluate("(() => { const a = [...document.querySelectorAll('.sign-in-providers a')].find(a => a.textContent.trim() === 'Sign in with Google'); const r = a.getBoundingClientRect(); return { href: a.getAttribute('href'), target: a.target, height: r.height }; })()")
    require(link["href"] == "api/session/google/start?slug=core" and link["target"] == "_top", f"{label}: the link is the server's start route, top-level ({link})")
    require(link["height"] >= 44, f"{label}: the link is a real target ({link})")


def first_sign_in_with_google(devtools, origin, label):
    """A person Google knows and Blokemon does not: the sign-in page's link makes no account,
    because the terms were never agreed to, and lands on the create page saying so; the create
    page's link is a link only once the box is ticked, and the account it makes has no name
    until the person chooses one."""
    sign_in_page_link(devtools, origin, label)
    activate(devtools, "Sign in with Google", selector="a")
    devtools.wait_for("location.pathname === '/signin/create' && location.search.includes('reason=terms')", f"{label}: a first sign-in without the terms lands on the create page", timeout=90)
    devtools.wait_for("document.querySelector('.sign-in-situation') !== null", f"{label}: the situation is named", timeout=30)
    require("Agree to the terms" in devtools.evaluate("document.querySelector('.sign-in-situation').textContent"), f"{label}: the situation names the terms")
    require(held_session(devtools) is None, f"{label}: nothing was signed in and nothing is held")
    # Until the box is ticked the Google control is a button that names the box, not a link.
    devtools.wait_for("[...document.querySelectorAll('.create-account button.sign-in-link')].some(b => b.textContent.trim() === 'Sign in with Google')", f"{label}: the Google control waits for consent", timeout=30)
    activate(devtools, "Sign in with Google", selector="button")
    devtools.wait_for("document.body.textContent.includes('Tick the box to agree to the terms first.')", f"{label}: pressing it names the box")
    require(devtools.evaluate("[...document.querySelectorAll('.terms-consent a')].map(a => a.getAttribute('href')).sort().join('|')") == "privacy|terms", f"{label}: the consent links to the terms and the privacy notice")
    accept_terms(devtools)
    devtools.wait_for("[...document.querySelectorAll('.create-account a.sign-in-link')].some(a => a.textContent.trim() === 'Sign in with Google')", f"{label}: the control is a link once the box is ticked", timeout=30)
    href = devtools.evaluate("[...document.querySelectorAll('.create-account a.sign-in-link')].find(a => a.textContent.trim() === 'Sign in with Google').getAttribute('href')")
    require(href.startswith("api/session/google/start?slug=core&terms="), f"{label}: the link carries the accepted terms ({href})")
    activate(devtools, "Sign in with Google", selector="a")
    # Signed in with no player yet: the game sends the person to choose a name first.
    devtools.wait_for("location.pathname === '/profile' && document.querySelector('.profile-name-prompt #display-name') !== null", f"{label}: the name prompt after the round trip", timeout=90)
    require("handoff=" not in devtools.evaluate("location.href"), f"{label}: the code left the URL")
    held = held_session(devtools)
    require(held is not None and held.get("recovery") is False and held.get("displayName") is None, f"{label}: the browser holds a first-party session with no name ({held})")
    open_menu(devtools)
    require(identity_text(devtools) == "New player", f"{label}: nothing of Google's names the player ({identity_text(devtools)})")
    close_menu(devtools)
    devtools.set_value("#display-name", PLAYER)
    devtools.events.clear()
    activate(devtools, "Continue to your game")
    devtools.wait_for("location.pathname === '/'", f"{label}: home after the name", timeout=60)
    devtools.wait_for("document.body.textContent.includes('Choose your first deck.')", f"{label}: the new player's game on the server", timeout=60)
    wait_for_credentials(devtools, label)
    require(devtools.evaluate("document.querySelector('.passkey-offer') === null"), f"{label}: a Google account is offered no passkey")
    open_menu(devtools)
    require(identity_text(devtools) == PLAYER, f"{label}: signed in as {PLAYER}")
    close_menu(devtools)


def returning_sign_in_with_google(devtools, origin, label):
    sign_in_page_link(devtools, origin, label)
    activate(devtools, "Sign in with Google", selector="a")
    devtools.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", f"{label}: home after the round trip", timeout=90)
    require("handoff=" not in devtools.evaluate("location.href"), f"{label}: the code left the URL")
    held = held_session(devtools)
    require(held is not None and held.get("recovery") is False, f"{label}: the browser holds a first-party session")
    open_menu(devtools)
    require(identity_text(devtools) == PLAYER, f"{label}: signed in as {PLAYER}")
    close_menu(devtools)


def wait_for_credentials(devtools, label):
    """The offer decides itself from the credential state the server answers with; a check that
    nothing is offered waits for that answer, after the events were cleared for the page."""
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        if any(e.get("method") == "Network.responseReceived" and "firstparty/credentials" in e["params"]["response"]["url"] for e in devtools.events):
            devtools.evaluate("new Promise(r => setTimeout(r, 400))")
            return
        # Events arrive only while a command is in flight: this one is the pump.
        devtools.evaluate("new Promise(r => setTimeout(r, 200))")
    raise EvidenceFailure(f"{label}: the credential state was never asked for")


def accept_terms(devtools):
    require(devtools.evaluate("(() => { const b = document.querySelector('#accept-terms'); if (!b) return false; if (!b.checked) b.click(); return true; })()"), "the consent box")
    devtools.wait_for("document.querySelector('#accept-terms').checked === true", "the box ticked")


def sign_out(devtools, origin):
    devtools.navigate(origin, "/")
    open_menu(devtools)
    activate(devtools, "Sign out")
    devtools.wait_for(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) === null", "the browser dropped its session on sign-out")


def diagnostics(devtools):
    try:
        location = devtools.evaluate("location.href")
        body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 400) : null")
        collected = devtools.evaluate("JSON.stringify((window.__blokemonErrors || []).slice(-4))")
        storage = devtools.evaluate("JSON.stringify({ session: sessionStorage.getItem('blokemon.session') !== null, local: Object.fromEntries(Object.keys(localStorage).map(k => [k, String(localStorage.getItem(k)).slice(0, 60)])) })")
    except Exception as error:  # noqa: BLE001
        return f" | diagnostics unavailable ({error})"
    calls = []
    pending = {}
    for event in devtools.events:
        if event.get("method") == "Network.requestWillBeSent":
            request = event["params"]["request"]
            if "/api/" in request["url"]:
                pending[event["params"]["requestId"]] = request["method"] + " " + request["url"].split("://", 1)[1].split("/", 1)[1].split("?")[0]
        elif event.get("method") == "Network.responseReceived":
            pending.pop(event["params"]["requestId"], None)
            response = event["params"]["response"]
            if "/api/" in response["url"] or "stub-google" in response["url"]:
                calls.append((response["url"].split("://", 1)[1].split("/", 1)[1].split("?")[0], response["status"]))
        elif event.get("method") == "Network.loadingFailed":
            failed = pending.pop(event["params"]["requestId"], None)
            if failed:
                calls.append((failed, "failed: " + str(event["params"].get("errorText"))))
    errors = []
    for event in devtools.events:
        if event.get("method") == "Runtime.exceptionThrown":
            details = event["params"]["exceptionDetails"]
            errors.append((details.get("exception") or {}).get("description") or details.get("text"))
        elif event.get("method") == "Runtime.consoleAPICalled" and event["params"].get("type") in ("error", "warning"):
            errors.append(" ".join(str(a.get("value") or a.get("description") or "") for a in event["params"].get("args", [])))
        elif event.get("method") == "Log.entryAdded" and event["params"]["entry"].get("level") == "error":
            errors.append(event["params"]["entry"].get("text"))
    errors = [e for e in errors if "Refused to apply style" not in str(e)]
    return f" | at {location} | body={body!r} | calls={json.dumps(calls[-10:])} | errors={json.dumps([str(e)[:600] for e in errors[-4:]])} | collected={collected} | storage={storage} | unanswered={json.dumps(list(pending.values())[-5:])}"


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-google-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.command("Log.enable")
            devtools.command("Network.enable")
            devtools.command("Page.enable")
            # Every document keeps its own errors: unhandled ones, rejected promises and whatever
            # is written to console.error, so a failure can say what the client hit.
            devtools.command("Page.addScriptToEvaluateOnNewDocument", {"source": ERROR_COLLECTOR})
            devtools.set_viewport(1440, 900)
            first_sign_in_with_google(devtools, origin, "desktop")
            # The account came in by Google: no login name yet, and a passkey can be added.
            devtools.navigate(origin, "/profile", ready_selector=".login-panel")
            devtools.wait_for("document.querySelector('.login-panel .login-name') !== null", "the login panel", timeout=30)
            require(devtools.evaluate("document.querySelector('.login-panel .login-name').textContent.trim()") == "None yet", "the Google account has no login name yet")
            require(devtools.evaluate("document.querySelector('.topline h1').textContent.trim()") == PLAYER, "the profile is headed by the chosen name")
            devtools.wait_for("[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Add a passkey')", "a passkey can be added from the profile", timeout=30)
            sign_out(devtools, origin)
            devtools.set_viewport(412, 915, touch=True)
            returning_sign_in_with_google(devtools, origin, "touch")
            # The create page offers the same control, waiting for consent.
            devtools.navigate(origin, "/signin/create", ready_selector="#player-name")
            devtools.wait_for("[...document.querySelectorAll('.create-account button.sign-in-link')].some(b => b.textContent.trim() === 'Sign in with Google')", "the Google control on the create page", timeout=30)
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
