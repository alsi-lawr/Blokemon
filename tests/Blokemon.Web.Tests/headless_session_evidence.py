#!/usr/bin/env python3
"""Headless client checks for BLOKEMON-149 against a running Blokemon.Web.

Driven by HeadlessSessionTests, which hosts Blokemon.Web on Kestrel with the test-only provider
double, mints a session, and hands this script the origins and the token through the environment.
The token is never printed, never placed in a URL, and only ever written to sessionStorage the way
the client itself writes it. Chrome runs headless only.
"""
from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
import threading
import time
import urllib.error
from pathlib import Path
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parent))
from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from static_host import static_server  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SIGN_IN_MODULE = ROOT / "src" / "Blokemon.Web.Client" / "wwwroot" / "signIn.js"
SESSION_KEY = "blokemon.session"
EXCHANGE = "/api/session/blokebot"
RESUME = "/api/session/resume"

PARENT_PAGE = """<!doctype html>
<meta charset="utf-8">
<title>Parent</title>
<body>
<script>
const params = new URLSearchParams(location.search);
const app = params.get("app");
const mode = params.get("mode");
window.__log = [];
let posted = 0;
let ready = false;
let timer = null;
const frame = document.createElement("iframe");
frame.src = app + "/t/core";
function post() {
    posted += 1;
    frame.contentWindow.postMessage({ type: "blokemon.handoff", code: "parent-code-" + posted }, app);
    window.__log.push({ event: "posted", count: posted, ready, t: Math.round(performance.now()) });
}
window.addEventListener("message", (event) => {
    window.__log.push({ event: "message", origin: event.origin, type: event.data && event.data.type, t: Math.round(performance.now()) });
    if (event.data && event.data.type === "blokemon.ready") {
        ready = true;
        if (timer) { clearInterval(timer); timer = null; }
        if (mode === "after") { post(); }
    }
});
frame.addEventListener("load", () => {
    window.__log.push({ event: "load", t: Math.round(performance.now()) });
    if (mode === "early") {
        post();
        timer = setInterval(() => { if (!ready) { post(); } }, 100);
    }
    if (mode === "blind") {
        setTimeout(post, 3000);
    }
});
document.body.appendChild(frame);
</script>
"""

RECEIVER_UNIT_PAGE = """<!doctype html>
<meta charset="utf-8">
<title>Receiver</title>
<body>
<script type="module">
import { createReceiver } from "./signIn.js";

const good = "https://parent.example";
function world(parentIsTarget = false, retainedBefore = []) {
    const target = new EventTarget();
    const posts = [];
    const parent = parentIsTarget ? target : { postMessage: (message, origin) => posts.push([message.type, origin]) };
    const delivered = [];
    const receiver = createReceiver(target, parent, (code) => delivered.push(code), retainedBefore);
    const send = (origin, data) => target.dispatchEvent(new MessageEvent("message", { data, origin }));
    return { receiver, posts, delivered, send };
}
const handoff = (code) => ({ type: "blokemon.handoff", code });

const results = {};

// Before the origin is known nothing is accepted and nothing is posted.
const a = world();
a.send("https://evil.example", handoff("retained-evil"));
a.send(good, handoff("retained-good"));
a.send("https://sub.parent.example", handoff("retained-sub"));
results.nothingBeforeBind = a.delivered.length === 0 && a.posts.length === 0;
// Binding validates what was retained against the origin and signals readiness with it.
results.bindReturnsTrue = a.receiver.bind(good) === true;
results.retainedGoodDelivered = JSON.stringify(a.delivered) === JSON.stringify(["retained-good"]);
results.readyPostedWithExactOrigin = JSON.stringify(a.posts) === JSON.stringify([["blokemon.ready", good]]);
// After binding only the exact origin is accepted.
a.send(good, handoff("live-good"));
a.send("https://sub.parent.example", handoff("live-sub"));
a.send("http://parent.example", handoff("live-scheme"));
a.send("https://parent.example:8443", handoff("live-port"));
a.send("https://evil.example", handoff("live-evil"));
a.send(good, "not an object");
a.send(good, { type: "blokemon.handoff" });
a.send(good, { type: "other", code: "x" });
results.onlyExactOriginAccepted = JSON.stringify(a.delivered) === JSON.stringify(["retained-good", "live-good"]);
results.reauthPostedWithExactOrigin = a.receiver.post("blokemon.reauth") === true
    && JSON.stringify(a.posts[1]) === JSON.stringify(["blokemon.reauth", good]);
results.neverWildcard = a.posts.every(([, origin]) => origin !== "*");

// A tenant with no registered origin binds to nothing: retained messages are discarded.
const b = world();
b.send(good, handoff("retained-good"));
results.bindNullReturnsFalse = b.receiver.bind(null) === false;
b.send(good, handoff("live-good"));
results.originlessAcceptsNothing = b.delivered.length === 0 && b.posts.length === 0 && b.receiver.post("blokemon.ready") === false;

// A window that is its own parent never posts to itself.
const c = world(true);
c.receiver.bind(good);
results.noSelfPost = c.receiver.post("blokemon.ready") === false;

// What the page retained before the receiver existed is validated the same way at bind.
const d = world(false, [
    { origin: "https://evil.example", data: handoff("page-evil") },
    { origin: good, data: handoff("page-good") },
]);
results.pageRetainedNothingBeforeBind = d.delivered.length === 0;
d.receiver.bind(good);
results.pageRetainedValidatedAtBind = JSON.stringify(d.delivered) === JSON.stringify(["page-good"]);

// Detaching stops listening.
a.receiver.detach();
a.send(good, handoff("after-detach"));
results.detachStops = a.delivered.length === 2;

window.__results = results;
</script>
"""

CORE_SIGN_IN_PAGE = """<!doctype html>
<meta charset="utf-8">
<title>Core sign-in</title>
<body><h1 id="core-sign-in">Core sign-in landing</h1></body>
"""


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def api_state(origin, token):
    request = Request(f"{origin}/api/state", headers={"Authorization": f"Bearer {token}"})
    with urlopen(request, timeout=10) as response:
        return json.load(response)


def drain(devtools):
    devtools.evaluate("1")


def requests_to(devtools, path):
    drain(devtools)
    found = []
    for event in devtools.events:
        if event.get("method") != "Network.requestWillBeSent":
            continue
        params = event["params"]
        url = params["request"]["url"]
        if url.split("?")[0].endswith(path):
            found.append(params)
    return found


def diagnostics(devtools, entries):
    """What happened, for a failure message: the parent's log, the frame's request paths and any errors. No code or token."""
    drain(devtools)
    paths = []
    errors = []
    for event in devtools.events:
        method = event.get("method")
        if method == "Network.requestWillBeSent":
            url = event["params"]["request"]["url"].split("?")[0].split("#")[0]
            if "/_framework/" not in url and not url.endswith((".css", ".js", ".svg", ".png", ".woff2")):
                paths.append(url)
        elif method == "Runtime.exceptionThrown":
            errors.append(str(event["params"]["exceptionDetails"].get("text"))[:300])
        elif method == "Log.entryAdded" and event["params"]["entry"].get("level") == "error":
            errors.append(str(event["params"]["entry"].get("text"))[:300])
        elif method == "Runtime.consoleAPICalled" and event["params"].get("type") == "error":
            errors.append(" ".join(str(a.get("value", a.get("description", "")))[:300] for a in event["params"].get("args", [])))
    return f" | log={json.dumps(entries)} | requests={json.dumps(paths)} | errors={json.dumps(errors)}"


def request_headers(devtools, request_id):
    headers = {}
    for event in devtools.events:
        if event.get("method") in ("Network.requestWillBeSent", "Network.requestWillBeSentExtraInfo"):
            params = event["params"]
            if params.get("requestId") == request_id:
                source = params["request"]["headers"] if "request" in params else params.get("headers", {})
                headers.update({key.lower(): value for key, value in source.items()})
    return headers


def activate(devtools, text, selector="button, a"):
    wanted = json.dumps(text)
    devtools.wait_for(
        f"[...document.querySelectorAll({json.dumps(selector)})].some(e => e.textContent.trim() === {wanted})",
        f"the {text!r} control",
        timeout=30,
    )
    devtools.click_text(text, selector)


def open_menu(devtools):
    require(devtools.evaluate("(() => { const b = document.querySelector('.app-menu-button'); if (!b) return false; b.click(); return true; })()"), "opened the menu")
    devtools.wait_for("document.querySelector('.app-menu-panel') !== null", "the menu panel")


def close_menu(devtools):
    devtools.evaluate("document.querySelector('.app-menu-away')?.click()")


def identity_text(devtools):
    return devtools.evaluate("document.querySelector('.app-menu-identity strong')?.textContent ?? null")


def standalone_session(devtools, origin, token, expires_at):
    devtools.events.clear()
    devtools.navigate(origin, "/")
    open_menu(devtools)
    require(identity_text(devtools) is None, "a signed-out browser shows no identity in the menu")
    close_menu(devtools)

    # The copy the client itself keeps, put where the client keeps it; the reload is the client
    # reading it back.
    stored = json.dumps({"token": token, "expiresAt": expires_at, "displayName": "Headless Player"})
    devtools.evaluate(f"sessionStorage.setItem({json.dumps(SESSION_KEY)}, {json.dumps(stored)}); true")
    devtools.command("Page.reload", {"ignoreCache": True})
    devtools.wait_for("document.readyState === 'complete'", "reload after the session was stored")
    devtools.wait_for("document.querySelector('.app-shell') !== null", "the shell after reload", timeout=30)
    open_menu(devtools)
    require(identity_text(devtools) == "Headless Player", "after a reload the menu shows 'Signed in as Headless Player'")
    close_menu(devtools)

    devtools.events.clear()
    activate(devtools, "Use this server")
    devtools.wait_for("document.body.textContent.includes('Choose your first deck.')", "the signed-in player's game on the server", timeout=30)
    state_requests = requests_to(devtools, "/api/state")
    require(len(state_requests) >= 1, "the client asked the server for its state")
    headers = request_headers(devtools, state_requests[-1]["requestId"])
    require(headers.get("authorization", "").startswith("Bearer "), "the state request carried the Authorization: Bearer header")
    require(token not in state_requests[-1]["request"]["url"], "the token is not in the request URL")
    require(token not in devtools.evaluate("location.href"), "the token is not in the page URL")
    require(api_state(origin, token)["value"]["profile"]["displayName"] == "Headless Player", "the server honours the token the browser holds")

    open_menu(devtools)
    activate(devtools, "Sign out")
    devtools.wait_for(f"sessionStorage.getItem({json.dumps(SESSION_KEY)}) === null", "the browser dropped its copy on sign-out")
    devtools.wait_for("document.querySelector('.app-shell') !== null", "the shell after sign-out", timeout=30)
    devtools.wait_for("document.querySelector('.app-menu-panel') === null", "the menu closed after sign-out")
    open_menu(devtools)
    require(identity_text(devtools) is None, "after sign-out the menu shows no identity")
    close_menu(devtools)
    require(api_state(origin, token)["value"]["profile"] is None, "the server refuses the signed-out token thereafter")
    devtools.command("Page.reload", {"ignoreCache": True})
    devtools.wait_for("document.querySelector('.app-shell') !== null", "the shell after a second reload", timeout=30)
    open_menu(devtools)
    require(identity_text(devtools) is None, "a reload after sign-out stays signed out")
    close_menu(devtools)


def fragment_reader(devtools, origin, path, exchange_path):
    devtools.events.clear()
    devtools.navigate(origin, "/signin")
    devtools.command("Page.navigate", {"url": f"{origin}{path}#handoff=fragment-code-{exchange_path.rsplit('/', 1)[-1]}"})
    devtools.wait_for("document.querySelector('.sign-in-status') !== null", f"{path} sign-in status card", timeout=30)
    devtools.wait_for("document.querySelector('.sign-in-status .failure') !== null", f"{path} typed failure once the exchange is unavailable", timeout=30)
    require(devtools.evaluate("location.hash") == "", f"{path} cleared the #handoff fragment")
    exchanges = requests_to(devtools, exchange_path)
    require(len(exchanges) == 1, f"{path} called {exchange_path} once")
    require("#handoff" not in exchanges[0].get("documentURL", ""), f"{path} cleared the fragment before calling the exchange")
    require("fragment-code" not in exchanges[0]["request"]["url"], f"{path} kept the code out of the request URL")
    require(exchanges[0]["request"]["method"] == "POST", f"{path} posted the code")
    message = devtools.evaluate("document.querySelector('.sign-in-status .failure span')?.textContent")
    require("not available" in message, f"{path} shows the typed unavailable outcome")


def unknown_tenant(devtools, origin):
    devtools.navigate(origin, "/t/nobody")
    devtools.wait_for("document.querySelector('.sign-in-status .failure') !== null", "unknown channel failure", timeout=30)
    require("not on this server" in devtools.evaluate("document.querySelector('.sign-in-status .failure span').textContent"), "an unknown channel is named as such")


def hosted(devtools, parent_origin, app_origin, mode, expect_ready, expect_exchange):
    devtools.events.clear()
    devtools.command("Page.navigate", {"url": f"{parent_origin}/parent.html?app={app_origin}&mode={mode}"})
    devtools.wait_for("document.readyState === 'complete' && document.querySelector('iframe') !== null", f"parent page ({parent_origin}, {mode})")
    # The application boots inside the frame; under full-suite load that alone can take tens of
    # seconds, and the exchange follows readiness by a further round trip.
    deadline = time.monotonic() + 60
    grace = None
    while time.monotonic() < deadline:
        log = devtools.evaluate("JSON.stringify(window.__log)")
        entries = json.loads(log)
        ready = [entry for entry in entries if entry["event"] == "message" and entry["type"] == "blokemon.ready"]
        exchanges = requests_to(devtools, EXCHANGE)
        if expect_ready and ready and (not expect_exchange or exchanges):
            break
        if expect_ready and ready and grace is None:
            grace = time.monotonic() + 15
        if grace is not None and time.monotonic() > grace:
            break
        if not expect_ready and any(entry["event"] == "posted" for entry in entries) and time.monotonic() > deadline - 54:
            break
        time.sleep(0.2)
    entries = json.loads(devtools.evaluate("JSON.stringify(window.__log)"))
    ready = [entry for entry in entries if entry["event"] == "message" and entry["type"] == "blokemon.ready"]
    exchanges = requests_to(devtools, EXCHANGE)
    label = f"{mode} from {parent_origin}"
    if expect_ready:
        require(len(ready) >= 1, f"{label}: the parent received blokemon.ready")
        require(all(entry["origin"] == app_origin for entry in ready), f"{label}: blokemon.ready carried the app's exact origin")
        descriptor = requests_to(devtools, "/api/tenant/core")
        require(len(descriptor) >= 1, f"{label}: the frame resolved its descriptor")
        if mode == "after":
            first_post = next(index for index, entry in enumerate(entries) if entry["event"] == "posted")
            first_ready = next(index for index, entry in enumerate(entries) if entry["event"] == "message" and entry["type"] == "blokemon.ready")
            require(first_ready < first_post, f"{label}: the parent posted only after readiness")
    else:
        require(len(ready) == 0, f"{label}: a parent on another origin never receives blokemon.ready")
    if expect_exchange:
        require(len(exchanges) >= 1, f"{label}: the frame exchanged the parent's code at {EXCHANGE}" + ("" if exchanges else diagnostics(devtools, entries)))
        require(all("parent-code" not in e["request"]["url"] for e in exchanges), f"{label}: the code stayed out of the URL")
    else:
        require(len(exchanges) == 0, f"{label}: the frame ignored the code from another origin")


def receiver_rules(devtools, parent_origin):
    devtools.command("Page.navigate", {"url": f"{parent_origin}/receiver_unit.html"})
    devtools.wait_for("window.__results !== undefined", "receiver unit results")
    results = json.loads(devtools.evaluate("JSON.stringify(window.__results)"))
    for name, outcome in results.items():
        require(outcome is True, f"receiver rule: {name}")


def sign_in_page(devtools, origin, plain_origin, core_url, touch):
    label = "touch" if touch else "desktop"
    devtools.navigate(origin, "/signin?reason=expired")
    devtools.wait_for("document.querySelector('.sign-in') !== null", f"sign-in page ({label})", timeout=30)
    require("Your last sign-in has ended." in devtools.evaluate("document.body.textContent"), f"{label}: the expired situation is named")
    anchor = devtools.evaluate("""(() => {
      const a = document.querySelector('.sign-in-providers a.primary');
      if (!a) return null;
      const rect = a.getBoundingClientRect();
      return { text: a.textContent.trim(), href: a.href, target: a.target, height: rect.height, width: rect.width };
    })()""")
    require(anchor is not None and anchor["text"] == "Sign in with Twitch", f"{label}: 'Sign in with Twitch' is shown when the core sign-in URL is configured")
    require(anchor["href"] == core_url, f"{label}: the button targets the configured core sign-in URL")
    require(anchor["target"] == "_top", f"{label}: the button navigates top-level")
    require(anchor["height"] >= 44, f"{label}: the button is a real touch target ({anchor['height']:.0f}px)")
    card = devtools.evaluate("(() => { const r = document.querySelector('.sign-in').getBoundingClientRect(); return { left: r.left, right: r.right, width: r.width }; })()")
    viewport = devtools.evaluate("innerWidth")
    require(card["right"] <= viewport and card["left"] >= 0, f"{label}: the sign-in card fits the viewport")
    activate(devtools, "Sign in with Twitch", selector="a")
    devtools.wait_for(f"location.href === {json.dumps(core_url)}", f"{label}: the Twitch button navigated top-level to the core sign-in")

    devtools.navigate(plain_origin, "/signin")
    devtools.wait_for("document.querySelector('.sign-in') !== null", f"plain sign-in page ({label})", timeout=30)
    require(devtools.evaluate("document.querySelector('.sign-in-providers a.primary') === null"), f"{label}: no Twitch button without a core sign-in URL")
    require("No sign-in is set up on this server yet." in devtools.evaluate("document.body.textContent"), f"{label}: the empty state is named")


def main():
    origin = env("BLOKEMON_ORIGIN")
    plain_origin = env("BLOKEMON_PLAIN_ORIGIN")
    parent_port = int(env("BLOKEMON_PARENT_PORT"))
    token = env("BLOKEMON_SESSION_TOKEN")
    expires_at = env("BLOKEMON_SESSION_EXPIRES_AT")
    parent_origin = f"http://localhost:{parent_port}"
    core_url = f"{parent_origin}/core-signin.html"

    with tempfile.TemporaryDirectory(prefix="blokemon-session-evidence-") as temporary:
        temporary_root = Path(temporary)
        site = temporary_root / "site"
        site.mkdir()
        shutil.copy(SIGN_IN_MODULE, site / "signIn.js")
        (site / "parent.html").write_text(PARENT_PAGE)
        (site / "receiver_unit.html").write_text(RECEIVER_UNIT_PAGE)
        (site / "core-signin.html").write_text(CORE_SIGN_IN_PAGE)
        server = static_server(site, parent_port)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        chrome = Chrome(
            temporary_root,
            extra_arguments=(
                # The hosted checks read the frame's network activity from the page target.
                "--disable-site-isolation-trials",
                "--disable-features=IsolateOrigins,site-per-process",
            ),
        )
        try:
            devtools = chrome.devtools
            devtools.command("Network.enable")
            devtools.command("Runtime.enable")
            devtools.command("Log.enable")
            devtools.set_viewport(1440, 900)
            standalone_session(devtools, origin, token, expires_at)
            fragment_reader(devtools, origin, "/", EXCHANGE)
            fragment_reader(devtools, origin, "/t/core", EXCHANGE)
            fragment_reader(devtools, origin, "/t/core/continue", RESUME)
            unknown_tenant(devtools, origin)
            receiver_rules(devtools, parent_origin)
            hosted(devtools, parent_origin, origin, "after", expect_ready=True, expect_exchange=True)
            hosted(devtools, parent_origin, origin, "early", expect_ready=True, expect_exchange=True)
            hosted(devtools, f"http://127.0.0.1:{parent_port}", origin, "blind", expect_ready=False, expect_exchange=False)
            hosted(devtools, f"http://sub.localhost:{parent_port}", origin, "blind", expect_ready=False, expect_exchange=False)
            sign_in_page(devtools, origin, plain_origin, core_url, touch=False)
            devtools.set_viewport(412, 915, touch=True)
            sign_in_page(devtools, origin, plain_origin, core_url, touch=True)
            devtools.navigate(origin, "/")
            open_menu(devtools)
            require(devtools.evaluate("(() => { const p = document.querySelector('.app-menu-panel').getBoundingClientRect(); return p.right <= innerWidth && p.left >= 0; })()"), "touch: the menu panel fits the narrow viewport")
        finally:
            chrome.close()
            server.shutdown()
            server.server_close()
    print("HEADLESS SESSION EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
