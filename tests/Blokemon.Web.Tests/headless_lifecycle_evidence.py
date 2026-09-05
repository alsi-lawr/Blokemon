#!/usr/bin/env python3
"""Headless checks of BLOKEMON-152's pages against a running Blokemon.Web.

Driven by HeadlessLifecycleTests, which hosts Blokemon.Web on Kestrel with an operator, a
channel whose broadcaster is signed in (the owner) and a viewer of it (the player), and hands
this script their session tokens through the environment. Each visitor's session is placed
where the client keeps its own copy; the pages are then driven as a person would. Chrome runs
headless only. No token is ever printed; the integration token an admission shows is read for
its shape and never echoed.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_session_evidence import SESSION_KEY, activate, close_menu, identity_text, open_menu  # noqa: E402


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def text(devtools):
    return devtools.evaluate("document.body.textContent")


def visit_as(devtools, origin, token, display_name, expires_at):
    """A fresh visitor holding the given session: the app origin's storage is cleared and the
    client's own record written, then the root is loaded as a reload would load it."""
    devtools.command("Page.navigate", {"url": f"{origin}/signin"})
    devtools.wait_for("document.readyState === 'complete'", "the app origin, to reset its storage")
    stored = json.dumps({"token": token, "expiresAt": expires_at, "displayName": display_name})
    devtools.evaluate(f"sessionStorage.clear(); sessionStorage.setItem({json.dumps(SESSION_KEY)}, {json.dumps(stored)}); true")
    devtools.navigate(origin, "/")
    settled = "document.querySelector('.app-shell') !== null && document.querySelector('.loading-panel') === null"
    devtools.wait_for(settled, "the home page settled", timeout=60)
    # The first visitor of this browser chooses the server game; the choice then persists.
    if devtools.evaluate("[...document.querySelectorAll('.hero .primary')].some(b => b.textContent.trim() === 'Use this server')"):
        activate(devtools, "Use this server")
        devtools.wait_for(f"({settled}) && ![...document.querySelectorAll('.hero .primary')].some(b => b.textContent.trim() === 'Use this server')", "the server game chosen", timeout=60)


def menu_items(devtools):
    open_menu(devtools)
    items = devtools.evaluate("[...document.querySelectorAll('.app-menu-item span')].map(e => e.textContent.trim())")
    who = identity_text(devtools)
    return items, who


def rows(devtools, section):
    return devtools.evaluate(f"""[...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].map(li => ({{
        id: li.dataset.id,
        badges: [...li.querySelectorAll('.dev-badge')].map(b => b.textContent.trim()),
        buttons: [...li.querySelectorAll('button')].map(b => ({{ text: b.textContent.trim(), disabled: b.disabled, height: b.getBoundingClientRect().height, width: b.getBoundingClientRect().width }})),
        error: li.querySelector('.field-error')?.textContent ?? null,
        fits: li.getBoundingClientRect().left >= 0 && li.getBoundingClientRect().right <= innerWidth,
        width: li.getBoundingClientRect().width
    }}))""")


def row(devtools, section, row_id):
    return next((r for r in rows(devtools, section) if r["id"] == row_id), None)


def press_in_row(devtools, section, row_id, label):
    require(devtools.evaluate(f"""(() => {{
        const li = [...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].find(li => li.dataset.id === {json.dumps(row_id)});
        if (!li) return false;
        const b = [...li.querySelectorAll('button')].find(b => b.textContent.trim() === {json.dumps(label)} && !b.disabled);
        if (!b) return false;
        b.click();
        return true;
    }})()"""), f"pressed {label!r} in the row {row_id[:8]}")


def confirm_in_row(devtools, section, row_id, verb, working):
    press_in_row(devtools, section, row_id, verb)
    devtools.wait_for(f"""(() => {{
        const li = [...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].find(li => li.dataset.id === {json.dumps(row_id)});
        return li && [...li.querySelectorAll('button')].some(b => b.textContent.trim() === 'Keep');
    }})()""", f"the second press for {verb!r}")
    press_in_row(devtools, section, row_id, verb)
    devtools.wait_for(f"""(() => {{
        const li = [...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].find(li => li.dataset.id === {json.dumps(row_id)});
        return !li || ![...li.querySelectorAll('button')].some(b => b.textContent.trim() === {json.dumps(working)} || b.textContent.trim() === 'Keep');
    }})()""", f"{verb!r} settled", timeout=30)


def wait_row_settled(devtools, section, row_id, what):
    devtools.wait_for(f"""(() => {{
        const li = [...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].find(li => li.dataset.id === {json.dumps(row_id)});
        return li && ![...li.querySelectorAll('button')].some(b => b.disabled);
    }})()""", f"{what} settled", timeout=30)
    require(row(devtools, section, row_id)["error"] is None, f"{what} raised no error in the row")


def wait_row_badge(devtools, section, row_id, badge):
    devtools.wait_for(f"""(() => {{
        const li = [...document.querySelectorAll({json.dumps(section)} + ' .admin-row')].find(li => li.dataset.id === {json.dumps(row_id)});
        return li && [...li.querySelectorAll('.dev-badge')].some(b => b.textContent.trim() === {json.dumps(badge)});
    }})()""", f"the row {row_id[:8]} showing {badge!r}", timeout=30)


def operator_page(devtools, origin, tokens, other_account):
    visit_as(devtools, origin, tokens["operator"], "Operator", tokens["expires"])
    items, who = menu_items(devtools)
    require(who == "Operator", "the operator is signed in as themselves")
    require("Operator" in items and "Your channel" not in items, f"the operator's menu offers the operator page and no channel ({items})")
    activate(devtools, "Operator", selector=".app-menu-item")
    devtools.wait_for("location.pathname === '/operator' && document.querySelector('.admin-channels') !== null", "the operator page", timeout=60)

    tenants = rows(devtools, ".admin-channels")
    require(len(tenants) == 2 and sum(1 for t in tenants if "Core" in t["badges"]) == 1, f"the channels list has the core tenant and alpha ({[t['badges'] for t in tenants]})")
    accounts = rows(devtools, ".admin-accounts")
    require(len(accounts) == 4 and all("Active" in a["badges"] for a in accounts), f"the accounts list has the four accounts, active ({[a['badges'] for a in accounts]})")
    require(all(b["height"] >= 44 for r in tenants + accounts for b in r["buttons"]), "every row action is a real target")
    require(devtools.evaluate("[...document.querySelectorAll('.admin-outcomes dt code')].some(c => c.textContent === 'session.issued')"), "the diagnostics list the sessions issued so far")

    # Admission: the form, the token shown once, then rotation, closure, re-admission, revocation.
    devtools.set_value(".admin-form label:nth-of-type(1) input", "gamma")
    devtools.set_value(".admin-form label:nth-of-type(2) input", "Gamma")
    devtools.set_value(".admin-form label:nth-of-type(3) input", "4004")
    devtools.set_value(".admin-form label:nth-of-type(4) input", "https://gamma.example")
    activate(devtools, "Admit")
    devtools.wait_for("document.querySelector('.admin-token code') !== null", "the integration token notice", timeout=30)
    first_token = devtools.evaluate("document.querySelector('.admin-token code').textContent")
    require(first_token.startswith("blkm_") and "Gamma" in devtools.evaluate("document.querySelector('.admin-token').textContent"), "the notice names the channel and carries its token")
    devtools.wait_for("document.querySelectorAll('.admin-channels .admin-row').length === 3", "gamma joined the list", timeout=30)
    gamma = next(t for t in rows(devtools, ".admin-channels") if t["id"] not in {x["id"] for x in tenants})
    require("Active" in gamma["badges"] and [b["text"] for b in gamma["buttons"]] == ["Rotate", "Close", "Revoke"], f"gamma is active with its three actions ({gamma})")
    press_in_row(devtools, ".admin-channels", gamma["id"], "Rotate")
    devtools.wait_for(f"document.querySelector('.admin-token code') !== null && document.querySelector('.admin-token code').textContent !== {json.dumps(first_token)}", "rotation showed a new token", timeout=30)
    confirm_in_row(devtools, ".admin-channels", gamma["id"], "Close", "Closing…")
    wait_row_badge(devtools, ".admin-channels", gamma["id"], "Closed")
    closed = row(devtools, ".admin-channels", gamma["id"])
    require([b["text"] for b in closed["buttons"]] == ["Re-admit", "Revoke"], f"a closed channel offers re-admission and revocation ({closed})")
    press_in_row(devtools, ".admin-channels", gamma["id"], "Re-admit")
    wait_row_badge(devtools, ".admin-channels", gamma["id"], "Active")
    confirm_in_row(devtools, ".admin-channels", gamma["id"], "Revoke", "Revoking…")
    wait_row_badge(devtools, ".admin-channels", gamma["id"], "Revoked")
    require(row(devtools, ".admin-channels", gamma["id"])["buttons"] == [], "a revoked channel has no actions")

    # Accounts: disable and enable the other viewer (disabling revokes every session of the
    # account, so the player's are left for the erase at the end), grant operator to the owner,
    # assign the default owner.
    confirm_in_row(devtools, ".admin-accounts", other_account, "Disable", "Disabling…")
    wait_row_badge(devtools, ".admin-accounts", other_account, "Disabled")
    disabled = row(devtools, ".admin-accounts", other_account)
    require([b["text"] for b in disabled["buttons"]] == ["Enable", "Erase"], f"a disabled account offers enable and erase ({disabled})")
    press_in_row(devtools, ".admin-accounts", other_account, "Enable")
    wait_row_badge(devtools, ".admin-accounts", other_account, "Active")
    # The listing carries identifiers, status and timestamps only, so a grant and an owner
    # assignment show as nothing more than the row settling without an error.
    owner_row = next(a for a in rows(devtools, ".admin-accounts") if a["id"] not in {other_account, tokens["player_account"], tokens["operator_account"]})
    press_in_row(devtools, ".admin-accounts", owner_row["id"], "Grant operator")
    wait_row_settled(devtools, ".admin-accounts", owner_row["id"], "the operator grant")
    press_in_row(devtools, ".admin-accounts", owner_row["id"], "Make default owner")
    wait_row_settled(devtools, ".admin-accounts", owner_row["id"], "the default owner assignment")


def operator_page_narrow(devtools, origin):
    devtools.set_viewport(412, 915, touch=True)
    devtools.navigate(origin, "/operator", ready_selector=".admin-channels")
    for section in (".admin-channels", ".admin-accounts"):
        listed = rows(devtools, section)
        require(listed and all(r["fits"] for r in listed), f"touch: {section} rows fit the phone")
        for r in listed:
            for b in r["buttons"]:
                require(b["height"] >= 44 and b["width"] >= 0.8 * r["width"], f"touch: {section} actions are full width and 44px ({b})")
    form = devtools.evaluate("(() => { const f = document.querySelector('.admin-form'); const r = f.getBoundingClientRect(); return { fits: r.left >= 0 && r.right <= innerWidth, columns: getComputedStyle(f).gridTemplateColumns.split(' ').length }; })()")
    require(form["fits"] and form["columns"] == 1, f"touch: the admission form is one column and fits ({form})")
    devtools.set_viewport(1440, 900)


def owner_page(devtools, origin, tokens, other_account):
    visit_as(devtools, origin, tokens["owner"], "Alpha Owner", tokens["expires"])
    items, who = menu_items(devtools)
    require(who == "Alpha Owner" and "Your channel" in items, f"the owner's menu offers their channel ({items})")
    activate(devtools, "Your channel", selector=".app-menu-item")
    devtools.wait_for("location.pathname === '/owner' && document.querySelector('.admin-players') !== null", "the owner page", timeout=60)
    require("Alpha." in text(devtools), "the page is headed by the channel's label")
    players = rows(devtools, ".admin-players")
    require({p["id"] for p in players} >= {other_account, tokens["player_account"]} and all("Approved" in p["badges"] for p in players), f"the players list has the channel's approved players ({[p['badges'] for p in players]})")
    confirm_in_row(devtools, ".admin-players", other_account, "Exclude", "Excluding…")
    wait_row_badge(devtools, ".admin-players", other_account, "Excluded")
    excluded = row(devtools, ".admin-players", other_account)
    require([b["text"] for b in excluded["buttons"]] == ["Readmit"], f"an excluded player offers readmission ({excluded})")

    devtools.set_viewport(412, 915, touch=True)
    devtools.navigate(origin, "/owner", ready_selector=".admin-players")
    narrow = row(devtools, ".admin-players", other_account)
    require(narrow["fits"] and all(b["height"] >= 44 and b["width"] >= 0.8 * narrow["width"] for b in narrow["buttons"]), f"touch: the player's row fits with a full-width action ({narrow})")
    press_in_row(devtools, ".admin-players", other_account, "Readmit")
    wait_row_badge(devtools, ".admin-players", other_account, "Approved")
    devtools.set_viewport(1440, 900)


def player_pages(devtools, origin, tokens):
    # A channel session cannot erase: the refusal shows in the panel and the panel returns.
    visit_as(devtools, origin, tokens["player_channel"], "Viewer Three", tokens["expires"])
    items, who = menu_items(devtools)
    require(who == "Viewer Three" and "Operator" not in items and "Your channel" not in items, f"the player's menu has no role items ({items})")
    close_menu(devtools)
    devtools.navigate(origin, "/operator", ready_selector=".admin-page")
    devtools.wait_for("document.querySelector('.admin-page .failure') !== null", "the operator page refuses a player", timeout=30)
    require(devtools.evaluate("document.querySelector('.admin-list') === null"), "nothing is listed to a player")
    devtools.navigate(origin, "/profile", ready_selector=".erase-account")
    require(devtools.evaluate("(() => { const panels = [...document.querySelectorAll('.setup-card')]; const purge = panels.findIndex(p => p.textContent.includes('Purge')); const erase = panels.findIndex(p => p.classList.contains('erase-account')); return purge >= 0 && erase === purge + 1; })()"), "the erase panel sits directly after the purge panel")
    activate(devtools, "Erase account")
    devtools.wait_for("[...document.querySelectorAll('.erase-account button')].map(b => b.textContent.trim()).join('|') === 'Keep my account|Erase everything'", "the second press")
    activate(devtools, "Keep my account")
    devtools.wait_for("[...document.querySelectorAll('.erase-account button')].map(b => b.textContent.trim()).join('|') === 'Erase account'", "keeping returns the panel")
    activate(devtools, "Erase account")
    activate(devtools, "Erase everything")
    devtools.wait_for("document.querySelector('.erase-account .field-error') !== null", "a channel session's erase is refused in the panel", timeout=30)
    require(devtools.evaluate("[...document.querySelectorAll('.erase-account button')].map(b => b.textContent.trim()).join('|') === 'Erase account'"), "the panel returned to its first state")
    require(devtools.evaluate(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) !== null"), "the refused session is still held")

    # The player's own first-party session erases: the browser lands signed out on sign-in.
    visit_as(devtools, origin, tokens["player"], "Viewer Three", tokens["expires"])
    devtools.set_viewport(412, 915, touch=True)
    devtools.navigate(origin, "/profile", ready_selector=".erase-account")
    activate(devtools, "Erase account")
    buttons = devtools.evaluate("[...document.querySelectorAll('.erase-account button')].map(b => { const r = b.getBoundingClientRect(); return { text: b.textContent.trim(), height: r.height, fits: r.left >= 0 && r.right <= innerWidth }; })")
    require([b["text"] for b in buttons] == ["Keep my account", "Erase everything"] and all(b["height"] >= 44 and b["fits"] for b in buttons), f"touch: the confirmation fits the phone ({buttons})")
    activate(devtools, "Erase everything")
    devtools.wait_for("location.pathname === '/signin' && document.querySelector('.sign-in') !== null", "the sign-in page after erasure", timeout=60)
    require(devtools.evaluate(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) === null"), "the browser dropped its session on erasure")
    devtools.set_viewport(1440, 900)
    devtools.navigate(origin, "/")
    open_menu(devtools)
    require(identity_text(devtools) is None, "the menu shows no identity after erasure")
    close_menu(devtools)


def erase_on_behalf(devtools, origin, tokens, other_account):
    visit_as(devtools, origin, tokens["operator"], "Operator", tokens["expires"])
    devtools.navigate(origin, "/operator", ready_selector=".admin-accounts")
    require(row(devtools, ".admin-accounts", tokens["player_account"])["badges"] == ["Erased"], "the player's own erasure shows as the tombstone")
    require(row(devtools, ".admin-accounts", tokens["player_account"])["buttons"] == [], "an erased account has no actions")
    confirm_in_row(devtools, ".admin-accounts", other_account, "Erase", "Erasing…")
    wait_row_badge(devtools, ".admin-accounts", other_account, "Erased")
    require(row(devtools, ".admin-accounts", other_account)["buttons"] == [], "the account erased on behalf has no actions either")


def main():
    origin = env("BLOKEMON_ORIGIN")
    tokens = {
        "operator": env("BLOKEMON_OPERATOR_TOKEN"),
        "owner": env("BLOKEMON_OWNER_TOKEN"),
        "player": env("BLOKEMON_PLAYER_TOKEN"),
        "player_channel": env("BLOKEMON_PLAYER_CHANNEL_TOKEN"),
        "expires": env("BLOKEMON_EXPIRES_AT"),
        "operator_account": env("BLOKEMON_OPERATOR_ACCOUNT"),
        "player_account": env("BLOKEMON_PLAYER_ACCOUNT"),
    }
    other_account = env("BLOKEMON_OTHER_ACCOUNT")

    with tempfile.TemporaryDirectory(prefix="blokemon-lifecycle-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.command("Log.enable")
            devtools.set_viewport(1440, 900)
            operator_page(devtools, origin, tokens, other_account)
            operator_page_narrow(devtools, origin)
            owner_page(devtools, origin, tokens, other_account)
            player_pages(devtools, origin, tokens)
            erase_on_behalf(devtools, origin, tokens, other_account)
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').replace(/blkm_[A-Za-z0-9_-]+/g, '<token>').slice(0, 400) : null")
                errors = [str(e["params"]["exceptionDetails"].get("text"))[:200] for e in devtools.events if e.get("method") == "Runtime.exceptionThrown"][-5:]
                errors += [str(e["params"]["entry"].get("text"))[:200] for e in devtools.events if e.get("method") == "Log.entryAdded"][-5:]
                held = devtools.evaluate("sessionStorage.getItem('blokemon.session') !== null")
            except Exception:  # noqa: BLE001
                where, body, errors, held = "?", "?", [], "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r} | held={held} | errors={errors}") from failure
        finally:
            chrome.close()
    print("HEADLESS LIFECYCLE EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
