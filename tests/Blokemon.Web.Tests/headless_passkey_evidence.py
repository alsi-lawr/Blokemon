#!/usr/bin/env python3
"""Headless passkey checks for BLOKEMON-150 against a running Blokemon.Web.

Driven by HeadlessPasskeyTests, which hosts Blokemon.Web on Kestrel at a known origin with the
first-party provider enabled, and hands this script that origin through the environment. Chrome
runs headless only, with a virtual authenticator from the DevTools WebAuthn domain standing in
for the platform authenticator. Recovery codes are read from the page for the flow that uses
one and never printed.
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
PLAYER = "Headless Passkey"


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def text(devtools):
    return devtools.evaluate("document.body.textContent")


def held_session(devtools):
    raw = devtools.evaluate(f"sessionStorage.getItem({json.dumps(SESSION_KEY)})")
    return json.loads(raw) if raw else None


def add_authenticator(devtools, verifies=True):
    """A platform authenticator with discoverable credentials; one that never verifies its
    user is the way to make the browser's ceremony end without a credential."""
    return devtools.command(
        "WebAuthn.addVirtualAuthenticator",
        {
            "options": {
                "protocol": "ctap2",
                "transport": "internal",
                "hasResidentKey": True,
                "hasUserVerification": True,
                "isUserVerified": verifies,
                "automaticPresenceSimulation": True,
            }
        },
    )["authenticatorId"]


def fresh_authenticator(devtools, current):
    """Another device: the one held so far is gone and a new, empty one takes its place."""
    devtools.command("WebAuthn.removeVirtualAuthenticator", {"authenticatorId": current})
    return add_authenticator(devtools)


def wait_text(devtools, wanted, description, timeout=30):
    devtools.wait_for(f"document.body.textContent.includes({json.dumps(wanted)})", description, timeout=timeout)


def warning_shown(devtools):
    """The no-recovery statement, by the element the composition names rather than its words:
    present and saying something, on every showing of the codes."""
    return devtools.evaluate("(() => { const w = document.querySelector('.recovery-codes-card .recovery-warning'); return w !== null && w.textContent.trim().length > 0; })()")


def recovery_codes_screen(devtools, continue_label, label):
    devtools.wait_for("document.querySelectorAll('.recovery-codes li code').length === 10", f"{label}: ten recovery codes", timeout=60)
    codes = devtools.evaluate("[...document.querySelectorAll('.recovery-codes li code')].map(e => e.textContent)")
    require(len(set(codes)) == 10, f"{label}: the ten codes are distinct")
    require(all(len(code) == 35 and code.count("-") == 3 for code in codes), f"{label}: each code is four groups of eight")
    require(warning_shown(devtools), f"{label}: the no-recovery warning is shown")
    require("Save these codes." in text(devtools), f"{label}: the screen is headed 'Save these codes.'")
    require(not any(code in devtools.evaluate("location.href") for code in codes), f"{label}: no code is in the URL")
    require(devtools.evaluate(f"[...document.querySelectorAll('button')].some(b => b.textContent.trim() === {json.dumps(continue_label)})"), f"{label}: the acknowledgement reads {continue_label!r}")
    overflow = devtools.evaluate("""(() => {
        const chips = [...document.querySelectorAll('.recovery-codes li code')];
        const card = document.querySelector('.recovery-codes-card').getBoundingClientRect();
        return { overflowing: chips.filter(e => e.scrollWidth > e.clientWidth + 1).length,
                 outside: chips.filter(e => { const r = e.getBoundingClientRect(); return r.left < card.left || r.right > card.right; }).length,
                 columns: getComputedStyle(document.querySelector('.recovery-codes')).gridTemplateColumns.split(' ').length };
    })()""")
    require(overflow["overflowing"] == 0 and overflow["outside"] == 0, f"{label}: no code wraps, overflows its chip or leaves the card ({overflow})")
    require(overflow["columns"] == (1 if devtools.evaluate("innerWidth") < 720 else 2), f"{label}: the codes sit in the expected columns ({overflow})")
    return codes


def sign_in_page(devtools, origin, label):
    devtools.navigate(origin, "/signin", ready_selector=".sign-in")
    devtools.wait_for("[...document.querySelectorAll('.sign-in button, .sign-in a')].some(e => e.textContent.trim() === 'Sign in with a passkey')", f"{label}: the passkey button", timeout=30)
    controls = devtools.evaluate("""[...document.querySelectorAll('.sign-in-providers button, .sign-in-providers a')].map(e => {
        const r = e.getBoundingClientRect();
        return { text: e.textContent.trim(), height: r.height, left: r.left, right: r.right };
    })""")
    texts = [c["text"] for c in controls]
    require(texts[:2] == ["Sign in with a passkey", "Create an account"], f"{label}: the first-party controls come first ({texts})")
    viewport = devtools.evaluate("innerWidth")
    require(all(c["height"] >= 44 for c in controls), f"{label}: every sign-in control is at least 44px ({[round(c['height']) for c in controls]})")
    require(all(c["left"] >= 0 and c["right"] <= viewport for c in controls), f"{label}: the controls fit the viewport")
    require("Lost your passkey?" in text(devtools), f"{label}: the recovery line is shown")
    require(devtools.evaluate("[...document.querySelectorAll('.sign-in-recover a')].some(a => a.getAttribute('href') === 'recover')"), f"{label}: the recovery line links to /recover")


def create_account(devtools, origin):
    sign_in_page(devtools, origin, "desktop")
    activate(devtools, "Create an account", selector="a")
    devtools.wait_for("location.pathname === '/signin/create' && document.querySelector('#display-name') !== null", "the create-account page", timeout=30)
    require("Create your account." in text(devtools), "the create-account heading")
    # Validation first: an empty name is refused without a ceremony.
    activate(devtools, "Create with a passkey")
    wait_text(devtools, "Enter a display name from 1 to 32 characters.", "the display-name validation")
    devtools.set_value("#display-name", PLAYER)
    activate(devtools, "Create with a passkey")
    codes = recovery_codes_screen(devtools, "Continue to your game", "after creation")
    held = held_session(devtools)
    require(held is not None and held.get("recovery") is False, "the browser holds a first-party session")
    require(all(code not in json.dumps(held) for code in codes), "no code is in the held session")
    activate(devtools, "Continue to your game")
    devtools.wait_for("location.pathname === '/'", "home after the codes were acknowledged", timeout=30)
    wait_text(devtools, "Choose your first deck.", "the new player's game on the server", timeout=60)
    open_menu(devtools)
    require(identity_text(devtools) == PLAYER, f"the menu shows 'Signed in as {PLAYER}'")
    close_menu(devtools)
    return codes


def profile_panel(devtools, origin, authenticator):
    devtools.navigate(origin, "/profile", ready_selector=".passkeys")
    devtools.wait_for("document.querySelectorAll('.passkey-list li').length === 1", "one passkey listed", timeout=30)
    wait_text(devtools, "10 of 10 left.", "the recovery-code count")
    rows = devtools.evaluate("[...document.querySelectorAll('.passkey-list li')].map(e => e.getBoundingClientRect().height)")
    require(all(h >= 44 for h in rows), f"passkey rows are at least 44px ({rows})")
    # The second passkey lives on another device: the first authenticator already holds the
    # excluded credential and would refuse to make a second.
    authenticator = fresh_authenticator(devtools, authenticator)
    activate(devtools, "Add a passkey")
    devtools.wait_for("document.querySelectorAll('.passkey-list li').length === 2", "a second passkey listed", timeout=60)
    require(devtools.evaluate("location.pathname") == "/profile", "adding a second passkey shows no codes screen")
    activate(devtools, "Make new codes")
    devtools.wait_for("location.pathname === '/passkeys/codes'", "the codes screen after regeneration", timeout=30)
    codes = recovery_codes_screen(devtools, "Back to your profile", "after regeneration")
    activate(devtools, "Back to your profile")
    devtools.wait_for("location.pathname === '/profile' && document.querySelector('.passkeys') !== null", "profile after the codes were acknowledged", timeout=30)
    wait_text(devtools, "10 of 10 left.", "the count after regeneration")
    # A reload of the codes route shows nothing: they were shown once.
    devtools.navigate(origin, "/passkeys/codes", ready_selector=".app-shell")
    devtools.wait_for("location.pathname === '/profile'", "the codes route returns to the profile on a reload", timeout=30)
    require(devtools.evaluate("document.querySelector('.recovery-codes') === null"), "no codes are shown again")
    return codes, authenticator


def sign_out(devtools, origin):
    devtools.navigate(origin, "/")
    open_menu(devtools)
    activate(devtools, "Sign out")
    devtools.wait_for(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) === null", "the browser dropped its session on sign-out")


def sign_in_with_passkey(devtools, origin, label):
    devtools.navigate(origin, "/signin", ready_selector=".sign-in")
    activate(devtools, "Sign in with a passkey")
    devtools.wait_for("location.pathname === '/'", f"{label}: home after the passkey sign-in", timeout=60)
    devtools.wait_for("document.querySelector('.app-shell') !== null", f"{label}: the shell", timeout=30)
    open_menu(devtools)
    require(identity_text(devtools) == PLAYER, f"{label}: signed in as {PLAYER} with the passkey")
    close_menu(devtools)


def declined_sign_in(devtools, origin, authenticator):
    # A ceremony the person does not complete (here, an authenticator that never verifies its
    # user) ends without a credential: the page says so under the button and nothing else
    # changes. The passkey is lost from here on; the fresh device that follows is what recovery
    # enrols the replacement on.
    devtools.command("WebAuthn.removeVirtualAuthenticator", {"authenticatorId": authenticator})
    refusing = add_authenticator(devtools, verifies=False)
    devtools.navigate(origin, "/signin", ready_selector=".sign-in")
    activate(devtools, "Sign in with a passkey")
    wait_text(devtools, "No passkey was used.", "the declined ceremony is named under the button", timeout=90)
    require(devtools.evaluate("location.pathname") == "/signin", "a declined ceremony stays on the sign-in page")
    require(held_session(devtools) is None, "a declined ceremony holds no session")
    return fresh_authenticator(devtools, refusing)


def recovery(devtools, origin, codes):
    devtools.navigate(origin, "/recover", ready_selector=".recover")
    require("Use a recovery code." in text(devtools), "the recovery heading")
    devtools.set_value("#recovery-code", "00000000-00000000-00000000-00000000")
    activate(devtools, "Recover")
    wait_text(devtools, "That code is not one of yours, or was used already.", "a wrong code is refused")
    devtools.set_value("#recovery-code", codes[0].upper().replace("-", " "))
    activate(devtools, "Recover")
    devtools.wait_for("location.pathname === '/recover/passkey'", "the replacement page after a good code", timeout=30)
    held = held_session(devtools)
    require(held is not None and held.get("recovery") is True, "the browser holds a recovery session")
    require("Add a new passkey." in text(devtools), "the replacement heading")
    # A recovery session can do one thing; home sends it back here.
    devtools.command("Page.navigate", {"url": f"{origin}/"})
    devtools.wait_for("location.pathname === '/recover/passkey'", "home redirects a recovery session to the replacement page", timeout=60)
    devtools.wait_for("[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Add a passkey')", "the replacement button", timeout=30)
    activate(devtools, "Add a passkey")
    devtools.wait_for("location.pathname === '/passkeys/codes'", "the codes screen after the replacement", timeout=60)
    new_codes = recovery_codes_screen(devtools, "Sign in with your new passkey", "after recovery")
    require(not set(new_codes) & set(codes), "the replacement's codes are a new set")
    require(held_session(devtools) is None, "the recovery session was discarded once the replacement was enrolled")
    activate(devtools, "Sign in with your new passkey")
    devtools.wait_for("location.pathname === '/signin'", "the sign-in page after recovery", timeout=30)
    sign_in_with_passkey(devtools, origin, "after recovery")
    # The consumed code, and every code of the replaced set, are dead.
    sign_out(devtools, origin)
    devtools.navigate(origin, "/recover", ready_selector=".recover")
    devtools.set_value("#recovery-code", codes[1])
    activate(devtools, "Recover")
    wait_text(devtools, "That code is not one of yours, or was used already.", "a code from the replaced set is refused")


def touch_checks(devtools, origin):
    devtools.set_viewport(412, 915, touch=True)
    sign_in_page(devtools, origin, "touch")
    devtools.navigate(origin, "/signin/create", ready_selector="#display-name")
    card = devtools.evaluate("(() => { const r = document.querySelector('.create-account').getBoundingClientRect(); return { left: r.left, right: r.right }; })()")
    require(card["left"] >= 0 and card["right"] <= 412, "touch: the create-account card fits the viewport")
    devtools.navigate(origin, "/recover", ready_selector=".recover")
    button = devtools.evaluate("(() => { const b = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Recover'); const r = b.getBoundingClientRect(); return { height: r.height, right: r.right }; })()")
    require(button["height"] >= 44 and button["right"] <= 412, "touch: the recover button is a real target that fits")
    # The codes screen on a phone: one column, nothing wrapping.
    sign_in_with_passkey(devtools, origin, "touch")
    devtools.navigate(origin, "/profile", ready_selector=".passkeys")
    devtools.wait_for("[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Make new codes')", "touch: the regenerate button", timeout=30)
    activate(devtools, "Make new codes")
    devtools.wait_for("location.pathname === '/passkeys/codes'", "touch: the codes screen", timeout=30)
    recovery_codes_screen(devtools, "Back to your profile", "touch")
    geometry = devtools.evaluate("""(() => {
        const card = document.querySelector('.recovery-codes-card').getBoundingClientRect();
        const chips = [...document.querySelectorAll('.recovery-codes li code')].map(e => e.getBoundingClientRect().width);
        return { left: card.left, right: card.right, width: card.width, widestChip: Math.max(...chips), viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth };
    })()""")
    require(geometry["left"] >= 0 and geometry["right"] <= 412 and geometry["scrollWidth"] <= 412, f"touch: the codes card fits the viewport ({geometry})")
    activate(devtools, "Back to your profile")
    devtools.wait_for("location.pathname === '/profile'", "touch: back on the profile", timeout=30)


def diagnostics(devtools):
    """Where the page was and what it said when a check failed. No code or token is included."""
    try:
        devtools.evaluate("1")
        location = devtools.evaluate("location.pathname")
        body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').replace(/[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}/g, '<code>').slice(0, 400) : null")
    except Exception as error:  # noqa: BLE001
        return f" | diagnostics unavailable ({error})"
    errors = []
    for event in devtools.events:
        method = event.get("method")
        if method == "Runtime.exceptionThrown":
            errors.append(str(event["params"]["exceptionDetails"].get("text"))[:300])
        elif method == "Log.entryAdded" and event["params"]["entry"].get("level") == "error":
            errors.append(str(event["params"]["entry"].get("text"))[:300])
        elif method == "Runtime.consoleAPICalled" and event["params"].get("type") == "error":
            errors.append(" ".join(str(a.get("value", a.get("description", "")))[:300] for a in event["params"].get("args", [])))
    calls = []
    for event in devtools.events:
        if event.get("method") == "Network.responseReceived":
            response = event["params"]["response"]
            if "/api/" in response["url"]:
                calls.append((response["url"].split("/api/", 1)[1].split("?")[0], response["status"]))
    return f" | at {location} | body={body!r} | errors={json.dumps(errors[-5:])} | api={json.dumps(calls[-8:])}"


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-passkey-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.command("Log.enable")
            devtools.command("Network.enable")
            devtools.command("WebAuthn.enable", {"enableUI": False})
            authenticator = add_authenticator(devtools)
            devtools.set_viewport(1440, 900)
            create_account(devtools, origin)
            codes, authenticator = profile_panel(devtools, origin, authenticator)
            sign_out(devtools, origin)
            sign_in_with_passkey(devtools, origin, "desktop")
            sign_out(devtools, origin)
            authenticator = declined_sign_in(devtools, origin, authenticator)
            recovery(devtools, origin, codes)
            touch_checks(devtools, origin)
        except EvidenceFailure as failure:
            raise EvidenceFailure(f"{failure}{diagnostics(chrome.devtools)}") from failure
        finally:
            chrome.close()
    print("HEADLESS PASSKEY EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
