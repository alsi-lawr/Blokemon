#!/usr/bin/env python3
"""Headless hosted-channel checks for BLOKEMON-151 against a running Blokemon.Web.

Driven by HeadlessChannelTests, which hosts Blokemon.Web on Kestrel with the BlokeBot provider,
the passkey relying party and two admitted channels, and hands this script the origins and the
channels' integration tokens through the environment. The tokens are used the way the plugin uses
them, server-to-server to mint hand-off codes, and are never printed or placed in a page. Chrome
runs headless only, with a virtual authenticator from the DevTools WebAuthn domain.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
import threading
import time
from pathlib import Path
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, DevTools, EvidenceFailure, require  # noqa: E402
from headless_passkey_evidence import add_authenticator, recovery_codes_screen, warning_shown  # noqa: E402
from headless_session_evidence import activate, close_menu, identity_text, open_menu  # noqa: E402
from static_host import static_server  # noqa: E402

OFFER = "Add a passkey so you can play anywhere and confirm new channels."
PROMPT_BODY = "Confirm from a channel you already play in, or sign in with your passkey."

PARENT_PAGE = """<!doctype html>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Parent</title>
<body>
<script>
const params = new URLSearchParams(location.search);
const app = params.get("app");
window.__log = [];
window.__ready = false;
const frame = document.createElement("iframe");
frame.src = app + "/t/" + params.get("slug");
frame.style.width = "100%";
frame.style.height = "800px";
if (params.get("allow") === "1") {
    frame.setAttribute("allow", "publickey-credentials-get; publickey-credentials-create");
}
window.addEventListener("message", (event) => {
    window.__log.push({ type: event.data && event.data.type, origin: event.origin });
    if (event.data && event.data.type === "blokemon.ready") { window.__ready = true; }
});
window.__post = (code) => { frame.contentWindow.postMessage({ type: "blokemon.handoff", code }, app); return true; };
document.body.appendChild(frame);
</script>
"""


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def mint_handoff(origin, token, twitch_user_id, display_name):
    """What the plugin does server-side: a hand-off for the viewer it authenticated."""
    body = json.dumps({"twitchUserId": twitch_user_id, "displayName": display_name}).encode()
    request = Request(
        f"{origin}/api/tenant/handoff",
        data=body,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=10) as response:
        envelope = json.load(response)
    require(envelope.get("succeeded") is True, f"the channel minted a hand-off ({envelope.get('error')})")
    return envelope["value"]["code"]


class Frame:
    """The hosted page inside the parent's iframe, evaluated in its own execution context."""

    def __init__(self, devtools, app_origin, user_gesture=True):
        self.devtools = devtools
        self.app_origin = app_origin
        # Every evaluation in the frame counts as a user gesture unless the check needs the
        # frame never to have had one: a gesture grants transient activation for seconds.
        self.user_gesture = user_gesture

    def context(self):
        self.devtools.evaluate("1")
        contexts = [
            event["params"]["context"]
            for event in self.devtools.events
            if event.get("method") == "Runtime.executionContextCreated"
        ]
        destroyed = {
            event["params"]["executionContextId"]
            for event in self.devtools.events
            if event.get("method") == "Runtime.executionContextDestroyed"
        }
        live = [c for c in contexts if c.get("origin") == self.app_origin and c["id"] not in destroyed and c.get("auxData", {}).get("type") == "default"]
        if not live:
            raise EvidenceFailure("the hosted frame has no execution context yet")
        return live[-1]["id"]

    def evaluate(self, expression):
        result = self.devtools.command(
            "Runtime.evaluate",
            {"expression": expression, "awaitPromise": True, "returnByValue": True, "userGesture": self.user_gesture, "contextId": self.context()},
        )
        if "exceptionDetails" in result:
            raise EvidenceFailure(result["exceptionDetails"].get("text", "frame evaluation failed"))
        return result.get("result", {}).get("value")

    def wait_for(self, expression, description, timeout=60):
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            try:
                value = self.evaluate(expression)
                if value:
                    return value
            except EvidenceFailure as failure:
                last = failure
            time.sleep(0.1)
        try:
            body = self.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').replace(/[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}-[0-9a-f]{8}/g, '<code>').slice(0, 400) : null")
            where = self.evaluate("location.pathname + ' canRun=' + (document.permissionsPolicy ? document.permissionsPolicy.allowsFeature('publickey-credentials-create') : 'n/a')")
        except EvidenceFailure:
            body, where = None, None
        raise EvidenceFailure(f"Timed out waiting for {description}" + (f" ({last})" if last else "") + f" | frame at {where} | frame body={body!r}")

    def text(self):
        return self.evaluate("document.body ? document.body.textContent : ''")

    def click(self, text, selector="button, a"):
        wanted = json.dumps(text)
        self.wait_for(f"[...document.querySelectorAll({json.dumps(selector)})].some(e => e.textContent.trim() === {wanted} && !e.disabled)", f"the {text!r} control in the frame")
        require(self.evaluate(f"(() => {{ const e = [...document.querySelectorAll({json.dumps(selector)})].find(e => e.textContent.trim() === {wanted}); if (!e) return false; e.click(); return true; }})()"), f"activated {text!r} in the frame")


def open_hosted(devtools, parent_origin, app_origin, slug, allow, code, user_gesture=True):
    devtools.events.clear()
    devtools.command("Page.navigate", {"url": f"{parent_origin}/parent.html?app={app_origin}&slug={slug}&allow={'1' if allow else '0'}"})
    devtools.wait_for("window.__ready === true", f"the {slug} frame signalled readiness", timeout=90)
    require(devtools.evaluate(f"window.__post({json.dumps(code)})"), "the parent posted the hand-off code")
    return Frame(devtools, app_origin, user_gesture)


def signed_in_frame(frame, label):
    frame.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", f"{label}: the game opened in the frame", timeout=90)
    frame.wait_for("document.body.textContent.includes('Choose your first deck.')", f"{label}: the new player's game", timeout=60)


def in_frame_passkey(devtools, parent_origin, app_origin, alpha_token):
    """A viewer handed off by a channel whose page delegated the WebAuthn permissions: the
    offer runs the ceremony in the frame and the codes are shown there."""
    code = mint_handoff(app_origin, alpha_token, "111", "Viewer One")
    frame = open_hosted(devtools, parent_origin, app_origin, "alpha", True, code)
    signed_in_frame(frame, "in-frame")
    frame.wait_for(f"document.body.textContent.includes({json.dumps(OFFER)})", "the passkey offer in the frame")
    frame.click("Add a passkey")
    frame.wait_for("location.pathname === '/passkeys/codes' && document.querySelectorAll('.recovery-codes li code').length === 10", "the codes screen inside the frame", timeout=90)
    require(warning_shown(frame), "the warning is shown in the frame")
    frame.click("Continue to your game")
    frame.wait_for("location.pathname === '/'", "the game again after the codes")
    frame.wait_for(f"!document.body.textContent.includes({json.dumps(OFFER)})", "the offer left once the account had a passkey")


def app_windows(chrome, app_origin):
    """The top-level pages at the app's origin: the frame is not one, so any is a window the
    client opened."""
    port = int((chrome.profile / "DevToolsActivePort").read_text().splitlines()[0])
    with urlopen(f"http://127.0.0.1:{port}/json/list", timeout=5) as response:
        targets = json.load(response)
    return [t for t in targets if t.get("type") == "page" and t.get("url", "").startswith(app_origin)]


def offer_band(frame):
    return frame.evaluate("(() => { const n = document.querySelector('.passkey-offer'); if (!n) return null; const b = n.querySelector('button'); return { moment: n.dataset.moment, sentence: n.querySelector('p').textContent.trim(), button: b !== null && !b.disabled, error: n.querySelector('.field-error') !== null }; })()")


def blocked_continuation(chrome, devtools, parent_origin, app_origin, beta_token):
    """The same offer with the browser's pop-up blocking on and the click coming from a script
    rather than a person (the frame never receives a gesture): the window is blocked, the band
    names that moment in its sentence with the button back and no field error, and nothing
    opened. A real press then opens it."""
    code = mint_handoff(app_origin, beta_token, "333", "Viewer Three")
    frame = open_hosted(devtools, parent_origin, app_origin, "beta", False, code, user_gesture=False)
    frame.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "blocked: the hosted game", timeout=90)
    frame.wait_for("document.querySelector('.passkey-offer button') !== null", "blocked: the passkey offer")
    offered = offer_band(frame)
    require(offered["moment"] == "offer" and offered["button"], f"blocked: the band starts as the offer ({offered})")
    frame.click("Add a passkey")
    frame.wait_for("(document.querySelector('.passkey-offer') || {dataset: {}}).dataset.moment === 'blocked'", "blocked: the band names the blocked window", timeout=60)
    blocked = offer_band(frame)
    require(blocked["sentence"] and blocked["sentence"] != offered["sentence"], f"blocked: the band's sentence changed for the moment ({blocked})")
    require(blocked["button"] and not blocked["error"], f"blocked: the button returned and there is no field error ({blocked})")
    require(not app_windows(chrome, app_origin), "blocked: no window opened")
    frame.user_gesture = True
    frame.click("Add a passkey")
    frame.wait_for("(document.querySelector('.passkey-offer') || {dataset: {}}).dataset.moment === 'opened'", "blocked: a real press opens the window", timeout=60)
    deadline = time.monotonic() + 30
    opened = []
    while time.monotonic() < deadline and not opened:
        opened = app_windows(chrome, app_origin)
        time.sleep(0.2)
    require(len(opened) == 1 and "/t/beta/continue" in opened[0]["url"], f"blocked: the pressed window opened at the continuation route ({[o['url'].split('#')[0] for o in opened]})")
    require(not offer_band(frame)["button"], "blocked: the button is gone while the window is open")
    window = DevTools(opened[0]["webSocketDebuggerUrl"])
    try:
        window.command("Target.closeTarget", {"targetId": opened[0]["id"]})
    except EvidenceFailure:
        pass
    window.close()


def continuation_passkey(chrome, devtools, parent_origin, app_origin, alpha_token):
    """The same offer in a frame with no delegated permission: the client opens the
    continuation window itself and the ceremony runs there."""
    code = mint_handoff(app_origin, alpha_token, "222", "Viewer Two")
    frame = open_hosted(devtools, parent_origin, app_origin, "alpha", False, code)
    signed_in_frame(frame, "continuation")
    frame.wait_for(f"document.body.textContent.includes({json.dumps(OFFER)})", "the passkey offer in the undelegated frame")
    frame.click("Add a passkey")
    frame.wait_for("document.body.textContent.includes('Continue in the window that opened.')", "the frame points at the window it opened", timeout=60)

    port = int((chrome.profile / "DevToolsActivePort").read_text().splitlines()[0])
    deadline = time.monotonic() + 30
    target = None
    while time.monotonic() < deadline and target is None:
        with urlopen(f"http://127.0.0.1:{port}/json/list", timeout=5) as response:
            targets = json.load(response)
        target = next((t for t in targets if t.get("type") == "page" and t.get("url", "").startswith(app_origin)), None)
        time.sleep(0.2)
    require(target is not None, "a top-level continuation window opened")
    require("/t/alpha/continue" in target["url"] or target["url"] == f"{app_origin}/", f"the window opened at the app's continuation route ({target['url'].split('#')[0]})")
    require("?" not in target["url"], "nothing travels in the continuation query string")
    window = DevTools(target["webSocketDebuggerUrl"])
    try:
        window.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "the game in the continuation window", timeout=90)
        require(window.evaluate("location.hash") == "", "the continuation window cleared its fragment")
        window.command("WebAuthn.enable", {"enableUI": False})
        add_authenticator(window)
        window.wait_for(f"document.body.textContent.includes({json.dumps(OFFER)})", "the offer in the continuation window", timeout=60)
        activate(window, "Add a passkey")
        window.wait_for("location.pathname === '/passkeys/codes'", "the codes screen in the continuation window", timeout=90)
        recovery_codes_screen(window, "Continue to your game", "continuation window")
        open_menu(window)
        require(identity_text(window) == "Viewer Two", "the continuation window is signed in as the same player")
        close_menu(window)
    finally:
        # The window the client opened is closed, not just left behind with its socket dropped:
        # a same-origin document left open keeps its storage connections.
        try:
            window.command("Target.closeTarget", {"targetId": target["id"]})
        except EvidenceFailure:
            pass
        window.close()


def approval_prompt_and_pending_list(devtools, parent_origin, app_origin, beta_token, authenticator):
    """Another channel hands off an existing account: the prompt, then approval from a
    passkey sign-in on the profile page, then the channel signs the account in."""
    # The tab's copy of the app origin's sessionStorage still holds the last hosted viewer's
    # session; a fresh visitor's tab holds none, which is what the prompt is checked against.
    devtools.command("Page.navigate", {"url": f"{app_origin}/signin"})
    devtools.wait_for("document.readyState === 'complete'", "the app origin, to clear its storage")
    devtools.evaluate("sessionStorage.clear(); true")
    code = mint_handoff(app_origin, beta_token, "111", "Viewer One")
    frame = open_hosted(devtools, parent_origin, app_origin, "beta", False, code)
    frame.wait_for("document.querySelector('.approval-prompt') !== null", "the approval prompt", timeout=90)
    text = frame.text()
    require("Beta wants to use your Blokemon." in text, "the prompt names the channel")
    require(PROMPT_BODY in text, "the prompt says where approval is given")
    anchor = frame.evaluate("(() => { const a = document.querySelector('.approval-prompt a.primary'); if (!a) return null; const r = a.getBoundingClientRect(); return { text: a.textContent.trim(), target: a.target, href: a.href, height: r.height }; })()")
    require(anchor is not None and anchor["text"] == "Sign in" and anchor["target"] == "_top" and anchor["href"].endswith("/signin"), f"the prompt's sign-in link is top-level ({anchor})")
    require(anchor["height"] >= 44, "the sign-in link is a real touch target")
    require(frame.evaluate("[...document.querySelectorAll('button')].every(b => !b.textContent.includes('passkey'))"), "the prompt offers no enrolment")
    require(frame.evaluate(f"sessionStorage.getItem('blokemon.session') === null"), "the prompt holds no session")

    # Top level: sign in with the passkey the frame enrolled, approve from the profile.
    devtools.command("Page.navigate", {"url": f"{app_origin}/signin"})
    devtools.wait_for("document.querySelector('.sign-in') !== null", "the sign-in page", timeout=60)
    held = devtools.command("WebAuthn.getCredentials", {"authenticatorId": authenticator}).get("credentials", [])
    require(len(held) == 1, f"the tab's authenticator holds the one passkey the frame enrolled ({len(held)})")
    activate(devtools, "Sign in with a passkey")
    devtools.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "home after the passkey sign-in", timeout=90)
    open_menu(devtools)
    require(identity_text(devtools) == "Viewer One", "signed in as the viewer with the passkey")
    close_menu(devtools)
    devtools.navigate(app_origin, "/profile", ready_selector=".pending-approvals")
    rows = devtools.evaluate("[...document.querySelectorAll('.approval-list li')].map(e => ({ text: e.querySelector('span').textContent, height: e.getBoundingClientRect().height }))")
    require([r["text"] for r in rows] == ["Beta"], f"the pending list names the channel ({rows})")
    require(all(r["height"] >= 44 for r in rows), "pending rows are real touch targets")
    activate(devtools, "Approve")
    devtools.wait_for("document.querySelector('.pending-approvals') === null", "the panel left with its last row", timeout=30)

    # The channel now signs the account in.
    code = mint_handoff(app_origin, beta_token, "111", "Viewer One")
    frame = open_hosted(devtools, parent_origin, app_origin, "beta", False, code)
    frame.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "the approved channel's game", timeout=90)
    require("wants to use your Blokemon" not in frame.text(), "no prompt once approved")


def narrow(devtools, parent_origin, app_origin, alpha_token, beta_token):
    """The phone: a new viewer's hosted game with the offer band, then another channel's
    prompt for that viewer, each fitting the frame with full-width, full-height controls."""
    devtools.set_viewport(412, 915, touch=True)
    code = mint_handoff(app_origin, beta_token, "333", "Viewer Three")
    frame = open_hosted(devtools, parent_origin, app_origin, "beta", False, code)
    frame.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "touch: the hosted game", timeout=90)
    frame.wait_for("document.querySelector('.passkey-offer button') !== null", "touch: the passkey offer")
    offer = frame.evaluate("(() => { const n = document.querySelector('.passkey-offer'); const b = n.querySelector('button'); const r = n.getBoundingClientRect(); const s = b.getBoundingClientRect(); return { fits: r.left >= 0 && r.right <= innerWidth, height: s.height, band: r.width, button: s.width }; })()")
    require(offer["fits"] and offer["height"] >= 44, f"touch: the passkey offer fits the frame with a full-height button ({offer})")
    require(offer["button"] >= 0.85 * offer["band"], f"touch: the offer's button is full width beneath the sentence ({offer})")

    code = mint_handoff(app_origin, alpha_token, "333", "Viewer Three")
    frame = open_hosted(devtools, parent_origin, app_origin, "alpha", False, code)
    frame.wait_for("document.querySelector('.approval-prompt a.primary') !== null", "touch: the approval prompt", timeout=90)
    prompt = frame.evaluate("(() => { const n = document.querySelector('.approval-prompt'); const a = n.querySelector('a.primary'); const r = n.getBoundingClientRect(); const s = a.getBoundingClientRect(); return { fits: r.left >= 0 && r.right <= innerWidth, height: s.height, card: r.width, link: s.width }; })()")
    require(prompt["fits"] and prompt["height"] >= 44, f"touch: the approval prompt fits the frame with a full-height link ({prompt})")
    require("Alpha wants to use your Blokemon." in frame.text(), "touch: the prompt names the channel")


def run(root, extra_arguments, scenarios):
    """One headless Chrome with its own profile, the DevTools domains the checks read, and the
    diagnostics a failure prints (facts about the document, never its contents)."""
    root.mkdir()
    chrome = Chrome(root, extra_arguments=extra_arguments)
    try:
        devtools = chrome.devtools
        devtools.command("Runtime.enable")
        devtools.command("Log.enable")
        devtools.command("Network.enable")
        devtools.command("WebAuthn.enable", {"enableUI": False})
        authenticator = add_authenticator(devtools)
        devtools.set_viewport(1440, 900)
        scenarios(chrome, devtools, authenticator)
    except EvidenceFailure as failure:
        try:
            where = devtools.evaluate("location.href")
            body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 300) : null")
            calls = [(e["params"]["response"]["url"].split("/api/", 1)[1].split("?")[0], e["params"]["response"]["status"]) for e in devtools.events if e.get("method") == "Network.responseReceived" and "/api/" in e["params"]["response"]["url"]][-8:]
            errors = [str(e["params"]["exceptionDetails"].get("text"))[:200] for e in devtools.events if e.get("method") == "Runtime.exceptionThrown"][-5:]
            errors += [" ".join(str(a.get("value", a.get("description", "")))[:200] for a in e["params"].get("args", [])) for e in devtools.events if e.get("method") == "Runtime.consoleAPICalled" and e["params"].get("type") == "error"][-5:]
            errors += [str(e["params"]["entry"].get("text"))[:200] for e in devtools.events if e.get("method") == "Log.entryAdded"][-5:]
            answered = {e["params"]["requestId"] for e in devtools.events if e.get("method") in ("Network.responseReceived", "Network.loadingFailed")}
            pending = [e["params"]["request"]["url"].split("/api/", 1)[1].split("?")[0] for e in devtools.events if e.get("method") == "Network.requestWillBeSent" and "/api/" in e["params"]["request"]["url"] and e["params"]["requestId"] not in answered]
            # Facts about the document, never its contents: whether a session is held, not what it is.
            held = devtools.evaluate("sessionStorage.getItem('blokemon.session') !== null")
            error_ui = devtools.evaluate("(() => { const e = document.querySelector('#blazor-error-ui'); return e ? getComputedStyle(e).display : null; })()")
        except Exception:  # noqa: BLE001
            where, body, calls, errors, pending, held, error_ui = "?", "?", [], [], [], "?", "?"
        raise EvidenceFailure(f"{failure} | at {where} | body={body!r} | api={calls} | pending={pending} | held={held} | error_ui={error_ui} | errors={errors}") from failure
    finally:
        chrome.close()


def main():
    app_origin = env("BLOKEMON_ORIGIN")
    parent_port = int(env("BLOKEMON_PARENT_PORT"))
    alpha_token = env("BLOKEMON_ALPHA_TOKEN")
    beta_token = env("BLOKEMON_BETA_TOKEN")
    parent_origin = f"http://localhost:{parent_port}"
    # Site isolation is off so the frame's context is reachable over one DevTools connection.
    isolation_off = ("--disable-site-isolation-trials", "--disable-features=IsolateOrigins,site-per-process")

    def unblocked(chrome, devtools, authenticator):
        in_frame_passkey(devtools, parent_origin, app_origin, alpha_token)
        continuation_passkey(chrome, devtools, parent_origin, app_origin, alpha_token)
        approval_prompt_and_pending_list(devtools, parent_origin, app_origin, beta_token, authenticator)
        narrow(devtools, parent_origin, app_origin, alpha_token, beta_token)

    def blocked(chrome, devtools, _authenticator):
        blocked_continuation(chrome, devtools, parent_origin, app_origin, beta_token)

    with tempfile.TemporaryDirectory(prefix="blokemon-channel-evidence-") as temporary:
        site = Path(temporary) / "site"
        site.mkdir()
        (site / "parent.html").write_text(PARENT_PAGE)
        server = static_server(site, parent_port)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            # The client opens its own continuation window, so pop-up blocking is off for the
            # main run; the second run keeps the browser's blocking on for the blocked moment.
            run(Path(temporary) / "unblocked", (*isolation_off, "--disable-popup-blocking"), unblocked)
            run(Path(temporary) / "blocked", isolation_off, blocked)
        finally:
            server.shutdown()
            server.server_close()
    print("HEADLESS CHANNEL EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
