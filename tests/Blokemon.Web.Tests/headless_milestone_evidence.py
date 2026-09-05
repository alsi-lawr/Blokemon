#!/usr/bin/env python3
"""The milestone's end-to-end browser journey for BLOKEMON-154, against a running Blokemon.Web.

Driven by MilestoneBrowserJourneyTests, which hosts Blokemon.Web on Kestrel with the BlokeBot and
first-party providers, an operator, the default tenant (core) and a channel (alpha) admitted, and a
second short-session-lifetime host for the re-authentication step; it hands this script the origins
and the channels' integration tokens through the environment. The tokens are used the way the plugin
uses them, server-to-server to mint hand-off codes, and are never printed or placed in a page. Chrome
runs headless only, with a virtual authenticator from the DevTools WebAuthn domain.

Both sign-in paths are covered at desktop (1440x900) and narrow (412x915, touch) viewports:

- The first-party path: a passkey account created through a CDP virtual authenticator.
- The channel path: a hand-off minted with a test integration token, exchanged in a hosted frame,
  through the continuation-window round trip, the blokemon.reauth round trip, the channel's closure
  and the closed channel's passkey-less player being adopted by the core issuer.

Screenshots for the reauth, continuation, closure and adoption steps are captured at both viewports
into the directory named by BLOKEMON_SCREENSHOTS (the Casefile evidence directory, not the repo).
"""
from __future__ import annotations

import base64
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
from headless_channel_evidence import Frame, offer_moment, signed_in_frame  # noqa: E402
from headless_passkey_evidence import add_authenticator, recovery_codes_screen  # noqa: E402
from headless_session_evidence import activate, close_menu, identity_text, open_menu, requests_to  # noqa: E402
from static_host import static_server  # noqa: E402

PLAYER = "Milestone Passkey"

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
window.__reauths = 0;
window.__nextCode = null;
const frame = document.createElement("iframe");
frame.src = app + "/t/" + params.get("slug");
frame.style.width = "100%";
frame.style.height = "760px";
if (params.get("allow") === "1") {
    frame.setAttribute("allow", "publickey-credentials-get; publickey-credentials-create");
}
window.addEventListener("message", (event) => {
    const type = event.data && event.data.type;
    window.__log.push({ type, origin: event.origin });
    if (type === "blokemon.ready") { window.__ready = true; }
    if (type === "blokemon.reauth") {
        window.__reauths += 1;
        if (window.__nextCode) { frame.contentWindow.postMessage({ type: "blokemon.handoff", code: window.__nextCode }, app); window.__nextCode = null; }
    }
});
window.__post = (code) => { frame.contentWindow.postMessage({ type: "blokemon.handoff", code }, app); return true; };
window.__setNext = (code) => { window.__nextCode = code; return true; };
document.body.appendChild(frame);
</script>
"""


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def screenshot(devtools, shots, name):
    data = devtools.command("Page.captureScreenshot", {"format": "png", "captureBeyondViewport": False})["data"]
    (shots / f"{name}.png").write_bytes(base64.b64decode(data))


def mint(app_origin, token, twitch_user_id, display_name, allow_failure=False):
    """The plugin's server-side hand-off mint; None when a closed channel refuses and that is
    the expected outcome."""
    body = json.dumps({"twitchUserId": twitch_user_id, "displayName": display_name}).encode()
    request = Request(f"{app_origin}/api/tenant/handoff", data=body, headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"}, method="POST")
    with urlopen(request, timeout=10) as response:
        envelope = json.load(response)
    if envelope.get("succeeded") is not True:
        if allow_failure:
            return None
        raise EvidenceFailure(f"the channel did not mint a hand-off ({envelope.get('error')})")
    return envelope["value"]["code"]


def close_channel(app_origin, token):
    """The plugin's uninstall hook: server-to-server /api/tenant/close with the integration token."""
    request = Request(f"{app_origin}/api/tenant/close", data=b"{}", headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"}, method="POST")
    with urlopen(request, timeout=10) as response:
        require(response.status == 200, "the channel closed")


def open_frame(devtools, parent_origin, app_origin, slug, allow, code, user_gesture=True):
    devtools.events.clear()
    devtools.command("Page.navigate", {"url": f"{parent_origin}/parent.html?app={app_origin}&slug={slug}&allow={'1' if allow else '0'}"})
    devtools.wait_for("window.__ready === true", f"the {slug} frame signalled readiness", timeout=90)
    require(devtools.evaluate(f"window.__post({json.dumps(code)})"), "the parent posted the hand-off code")
    return Frame(devtools, app_origin, user_gesture)


def firstparty_signin(devtools, shots, app_origin, authenticator, viewport):
    """The first-party sign-in path: a passkey account through the CDP virtual authenticator."""
    fresh = fresh_authenticator(devtools, authenticator)
    devtools.navigate(app_origin, "/signin", ready_selector=".sign-in")
    activate(devtools, "Create an account", selector="a")
    devtools.wait_for("location.pathname === '/signin/create' && document.querySelector('#player-name') !== null", "the create-account page", timeout=30)
    name = f"{PLAYER} {viewport}"
    devtools.set_value("#player-name", name)
    activate(devtools, "Create with a passkey")
    recovery_codes_screen(devtools, "Continue to your game", f"first-party create ({viewport})")
    activate(devtools, "Continue to your game")
    devtools.wait_for("location.pathname === '/'", "home after the codes", timeout=30)
    devtools.wait_for("document.body.textContent.includes('Choose your first deck.')", "the new passkey player's game", timeout=60)
    open_menu(devtools)
    require(identity_text(devtools) == name, f"the passkey player is signed in ({viewport})")
    close_menu(devtools)
    screenshot(devtools, shots, f"passkey-signin-{viewport}")

    # And a fresh top-level sign-in with that passkey proves the round trip both ways.
    devtools.navigate(app_origin, "/")
    open_menu(devtools)
    activate(devtools, "Sign out")
    devtools.wait_for("sessionStorage.getItem('blokemon.session') === null", "the passkey player signed out")
    devtools.navigate(app_origin, "/signin", ready_selector=".sign-in")
    activate(devtools, "Sign in with a passkey")
    devtools.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "home after the passkey sign-in", timeout=90)
    open_menu(devtools)
    require(identity_text(devtools) == name, f"signed back in with the passkey ({viewport})")
    close_menu(devtools)
    return fresh


def fresh_authenticator(devtools, current):
    if current is not None:
        devtools.command("WebAuthn.removeVirtualAuthenticator", {"authenticatorId": current})
    return add_authenticator(devtools)


def continuation(devtools, shots, chrome, parent_origin, app_origin, channel_token, viewport):
    """The channel path's continuation-window round trip: a frame with no delegated permission
    opens a top-level window the ceremony runs in."""
    code = mint(app_origin, channel_token, f"71{viewport}", "Continuation Viewer")
    frame = open_frame(devtools, parent_origin, app_origin, "gamma", False, code)
    signed_in_frame(frame, f"continuation ({viewport})")
    frame.wait_for(offer_moment("offer"), "the passkey offer in the undelegated frame")
    frame.click("Add a passkey")
    frame.wait_for(offer_moment("opened"), "the frame points at the window it opened", timeout=60)
    screenshot(devtools, shots, f"continuation-{viewport}")

    port = int((chrome.profile / "DevToolsActivePort").read_text().splitlines()[0])
    deadline = time.monotonic() + 30
    target = None
    while time.monotonic() < deadline and target is None:
        with urlopen(f"http://127.0.0.1:{port}/json/list", timeout=5) as response:
            targets = json.load(response)
        target = next((t for t in targets if t.get("type") == "page" and t.get("url", "").startswith(app_origin)), None)
        time.sleep(0.2)
    require(target is not None, "a top-level continuation window opened")
    require("/t/gamma/continue" in target["url"], f"the window opened at the continuation route ({target['url'].split('#')[0]})")
    require("?" not in target["url"], "nothing travels in the continuation query string")
    window = DevTools(target["webSocketDebuggerUrl"])
    try:
        window.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "the game in the continuation window", timeout=90)
        require(window.evaluate("location.hash") == "", "the continuation window cleared its fragment")
        open_menu(window)
        require(identity_text(window) == "Continuation Viewer", "the continuation window is the same player")
        close_menu(window)
    finally:
        try:
            window.command("Target.closeTarget", {"targetId": target["id"]})
        except EvidenceFailure:
            pass
        window.close()


def reauth(devtools, shots, parent_origin, reauth_origin, reauth_token, viewport):
    """The blokemon.reauth round trip: a channel session that expires mid-play makes the client
    ask its parent for a fresh hand-off, and the parent's fresh hand-off returns the player. The
    session lifetime on this host is a few seconds, so the expiry is genuine."""
    twitch = f"72{viewport}"
    code = mint(reauth_origin, reauth_token, twitch, "Reauth Viewer")
    frame = open_frame(devtools, parent_origin, reauth_origin, "alpha", False, code)
    signed_in_frame(frame, f"reauth ({viewport})")
    # Queue the parent's answer to a reauth request, then let the session expire and act.
    fresh = mint(reauth_origin, reauth_token, twitch, "Reauth Viewer")
    require(devtools.evaluate(f"window.__setNext({json.dumps(fresh)})"), "the parent holds a fresh hand-off")
    time.sleep(7)
    require(frame.evaluate("(() => { const b = [...document.querySelectorAll('.starter-option button')].find(b => b.textContent.trim().startsWith('Open ') && !b.disabled); if (!b) return false; b.click(); return true; })()"), "a starter was opened, calling the server with the expired session")
    devtools.wait_for("window.__reauths >= 1", "the client asked its parent to re-authenticate", timeout=60)
    screenshot(devtools, shots, f"reauth-{viewport}")
    require(all(entry["origin"] == reauth_origin for entry in json.loads(devtools.evaluate("JSON.stringify(window.__log)")) if entry["type"] == "blokemon.reauth"), "blokemon.reauth carried the app's exact origin")
    frame.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "the game returned after re-authentication", timeout=90)
    frame.wait_for("!!document.querySelector('.app-menu-button')", "the returned game is interactive")


def closure_and_adoption(devtools, shots, parent_origin, app_origin, channel_token, core_token, viewport):
    """A viewer created under alpha, then alpha closed: the closed channel no longer signs anyone
    in, and the passkey-less player it left is adopted by the core issuer with no approval step."""
    slug = "alpha" if viewport == "1440" else "bravo"
    twitch = f"73{viewport}"
    code = mint(app_origin, channel_token, twitch, "Adopted Viewer")
    frame = open_frame(devtools, parent_origin, app_origin, slug, False, code)
    signed_in_frame(frame, f"pre-closure ({viewport})")

    # The channel removes its integration: the tenant closes.
    close_channel(app_origin, channel_token)
    require(mint(app_origin, channel_token, twitch, "Adopted Viewer", allow_failure=True) is None, "a closed channel mints no hand-off")
    # The closed channel's hosted page no longer signs anyone in: opened top-level, its status
    # card shows the channel but never a signed-in player.
    devtools.events.clear()
    devtools.navigate(app_origin, f"/t/{slug}", ready_selector=".app-shell")
    devtools.wait_for("document.querySelector('.sign-in-status') !== null", "the closed channel's status card", timeout=90)
    require(not requests_to(devtools, "/api/session/blokebot"), "the closed channel's page made no exchange: it signs nobody in")
    screenshot(devtools, shots, f"closure-{viewport}")

    # Core adopts the orphan: top-level, no approval prompt.
    adopt = mint(app_origin, core_token, twitch, "Adopted Viewer")
    devtools.command("Page.navigate", {"url": f"{app_origin}/#handoff={adopt}"})
    devtools.wait_for("location.pathname === '/' && document.querySelector('.app-shell') !== null", "home after the core hand-off", timeout=90)
    # Settled: the sign-in status card has left and the signed-in game (its starter catalogue,
    # for a player with no deck yet) is on screen; no approval prompt ever appeared.
    devtools.wait_for("document.querySelector('.sign-in-status') === null && document.querySelector('.starter-shell') !== null", "the adopted player's game settled", timeout=90)
    require(devtools.evaluate("document.querySelector('.approval-prompt') === null"), "the core issuer adopts with no approval prompt")
    open_menu(devtools)
    require(identity_text(devtools) == "Adopted Viewer", f"the orphan is adopted by core ({viewport})")
    close_menu(devtools)
    devtools.wait_for("document.querySelector('.app-menu-panel') === null", "the menu closed")
    require(devtools.evaluate("location.hash") == "", "the adoption hand-off cleared its fragment")
    screenshot(devtools, shots, f"adoption-{viewport}")


def run_viewport(devtools, shots, chrome, ctx, viewport, width, height, touch):
    devtools.set_viewport(width, height, touch=touch)
    ctx["authenticator"] = firstparty_signin(devtools, shots, ctx["app_origin"], ctx["authenticator"], viewport)
    continuation(devtools, shots, chrome, ctx["parent_origin"], ctx["app_origin"], ctx["gamma_token"], viewport)
    reauth(devtools, shots, ctx["parent_origin"], ctx["reauth_origin"], ctx["reauth_token"], viewport)


def main():
    app_origin = env("BLOKEMON_ORIGIN")
    reauth_origin = env("BLOKEMON_REAUTH_ORIGIN")
    parent_port = int(env("BLOKEMON_PARENT_PORT"))
    alpha_token = env("BLOKEMON_ALPHA_TOKEN")
    bravo_token = env("BLOKEMON_BRAVO_TOKEN")
    gamma_token = env("BLOKEMON_GAMMA_TOKEN")
    core_token = env("BLOKEMON_CORE_TOKEN")
    reauth_token = env("BLOKEMON_REAUTH_TOKEN")
    shots = Path(env("BLOKEMON_SCREENSHOTS"))
    shots.mkdir(parents=True, exist_ok=True)
    parent_origin = f"http://localhost:{parent_port}"
    isolation_off = ("--disable-site-isolation-trials", "--disable-features=IsolateOrigins,site-per-process", "--disable-popup-blocking")

    with tempfile.TemporaryDirectory(prefix="blokemon-milestone-evidence-") as temporary:
        temporary_root = Path(temporary)
        site = temporary_root / "site"
        site.mkdir()
        (site / "parent.html").write_text(PARENT_PAGE)
        server = static_server(site, parent_port)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        chrome = Chrome(temporary_root, extra_arguments=isolation_off)
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            devtools.command("Log.enable")
            devtools.command("Network.enable")
            devtools.command("WebAuthn.enable", {"enableUI": False})
            ctx = {
                "app_origin": app_origin,
                "reauth_origin": reauth_origin,
                "parent_origin": parent_origin,
                "alpha_token": alpha_token,
                "gamma_token": gamma_token,
                "core_token": core_token,
                "reauth_token": reauth_token,
                "authenticator": None,
            }
            # Desktop: both sign-in paths, then closure and adoption once (a channel closes once).
            run_viewport(devtools, shots, chrome, ctx, "1440", 1440, 900, False)
            closure_and_adoption(devtools, shots, parent_origin, app_origin, alpha_token, core_token, "1440")
            # Narrow: both sign-in paths again; closure and adoption's screenshots at the phone.
            run_viewport(devtools, shots, chrome, ctx, "412", 412, 915, True)
            closure_and_adoption(devtools, shots, parent_origin, app_origin, bravo_token, core_token, "412")
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 300) : null")
            except Exception:  # noqa: BLE001
                where, body = "?", "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r}") from failure
        finally:
            chrome.close()
            server.shutdown()
            server.server_close()
    print("HEADLESS MILESTONE EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
