#!/usr/bin/env python3
from __future__ import annotations

import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import struct
import subprocess
import tempfile
import threading
import time
from urllib.parse import urlparse
from urllib.request import urlopen

from static_host import static_server


ROOT = Path(__file__).resolve().parents[2]
TEST_ROOT = Path(__file__).resolve().parent


class EvidenceFailure(RuntimeError):
    pass


class DevToolsError(RuntimeError):
    pass


def require(condition, description):
    if not condition:
        raise EvidenceFailure(description)
    print(f"PASS {description}")


def run(command):
    print("$", " ".join(command))
    subprocess.run(command, cwd=ROOT, check=True)


def check_consumer_inventory():
    source_root = ROOT / "src/Blokemon.Web.Client"
    actual = []
    for path in sorted(source_root.rglob("*.razor"), key=lambda item: item.as_posix()):
        count = len(re.findall(r"<CardFace\b", path.read_text()))
        relative = path.relative_to(source_root).as_posix()
        actual.extend(f"{relative}#{occurrence}" for occurrence in range(1, count + 1))

    inventory = (TEST_ROOT / "Fixtures/CardFaceConsumers.md").read_text()
    expected = re.findall(r"^\d+\. `([^`]+#\d+)`", inventory, re.MULTILINE)
    require(actual == expected, "direct CardFace consumer inventory matches the ordered Razor source list")


def published_root(output):
    for candidate in (output / "wwwroot", output):
        if (candidate / "index.html").is_file():
            return candidate
    raise EvidenceFailure(f"No published static root was found under {output}")


class HostedRoot:
    def __init__(self, root):
        self.server = static_server(root)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        host, port = self.server.server_address
        self.origin = f"http://{host}:{port}"
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)


class WebSocket:
    def __init__(self, url):
        parsed = urlparse(url)
        self.socket = socket.create_connection((parsed.hostname, parsed.port), timeout=30)
        self.socket.settimeout(30)
        key = base64.b64encode(os.urandom(16)).decode("ascii")
        path = parsed.path or "/"
        if parsed.query:
            path += f"?{parsed.query}"
        request = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {parsed.hostname}:{parsed.port}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n\r\n"
        )
        self.socket.sendall(request.encode("ascii"))
        response = self._read_headers()
        require(response.startswith(b"HTTP/1.1 101"), "Chrome accepted the DevTools WebSocket connection")
        expected = base64.b64encode(
            hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()
        ).decode("ascii")
        headers = {
            name.lower(): value.strip()
            for name, value in (
                line.split(":", 1)
                for line in response.decode("ascii").split("\r\n")[1:]
                if ":" in line
            )
        }
        require(
            headers.get("sec-websocket-accept") == expected,
            "Chrome completed the DevTools WebSocket handshake",
        )

    def close(self):
        try:
            self._send_frame(b"", opcode=8)
        except OSError:
            pass
        self.socket.close()

    def send_json(self, value):
        self._send_frame(json.dumps(value, separators=(",", ":")).encode("utf-8"), opcode=1)

    def receive_json(self):
        return json.loads(self._receive_message().decode("utf-8"))

    def _read_headers(self):
        response = bytearray()
        while b"\r\n\r\n" not in response:
            response.extend(self.socket.recv(4096))
        return bytes(response)

    def _send_frame(self, payload, opcode):
        first = 0x80 | opcode
        length = len(payload)
        if length < 126:
            header = bytes((first, 0x80 | length))
        elif length < 65536:
            header = bytes((first, 0x80 | 126)) + struct.pack("!H", length)
        else:
            header = bytes((first, 0x80 | 127)) + struct.pack("!Q", length)
        mask = os.urandom(4)
        masked = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
        self.socket.sendall(header + mask + masked)

    def _receive_message(self):
        chunks = []
        message_opcode = None
        while True:
            first, second = self._receive_exact(2)
            final = bool(first & 0x80)
            opcode = first & 0x0F
            masked = bool(second & 0x80)
            length = second & 0x7F
            if length == 126:
                length = struct.unpack("!H", self._receive_exact(2))[0]
            elif length == 127:
                length = struct.unpack("!Q", self._receive_exact(8))[0]
            mask = self._receive_exact(4) if masked else None
            payload = self._receive_exact(length)
            if mask is not None:
                payload = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
            if opcode == 8:
                raise DevToolsError("Chrome closed the DevTools WebSocket")
            if opcode == 9:
                self._send_frame(payload, opcode=10)
                continue
            if opcode in (1, 2):
                message_opcode = opcode
                chunks = [payload]
            elif opcode == 0 and message_opcode is not None:
                chunks.append(payload)
            else:
                continue
            if final:
                return b"".join(chunks)

    def _receive_exact(self, length):
        result = bytearray()
        while len(result) < length:
            chunk = self.socket.recv(length - len(result))
            if not chunk:
                raise DevToolsError("Chrome closed the DevTools connection")
            result.extend(chunk)
        return bytes(result)


class Chrome:
    def __init__(self, temporary_root):
        self.browser = self._find_browser()
        self.profile = temporary_root / "chrome-profile"
        self.profile.mkdir()
        self.stderr_path = temporary_root / "chrome.log"
        self.stderr = self.stderr_path.open("wb")
        self.process = subprocess.Popen(
            [
                self.browser,
                "--headless=new",
                "--disable-background-networking",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-dev-shm-usage",
                "--disable-extensions",
                "--disable-gpu",
                "--disable-sync",
                "--hide-scrollbars",
                "--metrics-recording-only",
                "--mute-audio",
                "--no-default-browser-check",
                "--no-first-run",
                "--remote-debugging-port=0",
                f"--user-data-dir={self.profile}",
                "about:blank",
            ],
            stdout=subprocess.DEVNULL,
            stderr=self.stderr,
        )
        port_file = self.profile / "DevToolsActivePort"
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline and not port_file.is_file():
            if self.process.poll() is not None:
                self._raise_start_failure()
            time.sleep(0.05)
        if not port_file.is_file():
            self._raise_start_failure()
        port = int(port_file.read_text().splitlines()[0])
        with urlopen(f"http://127.0.0.1:{port}/json/list", timeout=5) as response:
            targets = json.load(response)
        target = next(item for item in targets if item.get("type") == "page")
        self.devtools = DevTools(target["webSocketDebuggerUrl"])

    def close(self):
        self.devtools.close()
        self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)
        self.stderr.close()

    @staticmethod
    def _find_browser():
        for candidate in (
            os.environ.get("CHROME"),
            "google-chrome-stable",
            "google-chrome",
            "chromium",
            "chromium-browser",
        ):
            if candidate and (resolved := shutil.which(candidate)):
                return resolved
        raise EvidenceFailure("Chrome or Chromium is required for the headless card-viewer check")

    def _raise_start_failure(self):
        self.stderr.flush()
        details = self.stderr_path.read_text(errors="replace")[-4000:]
        raise EvidenceFailure(f"Chrome did not expose DevTools.\n{details}")


class DevTools:
    def __init__(self, url):
        self.websocket = WebSocket(url)
        self.next_id = 1
        self.command("Page.enable")
        self.command("Runtime.enable")

    def close(self):
        self.websocket.close()

    def command(self, method, params=None):
        command_id = self.next_id
        self.next_id += 1
        message = {"id": command_id, "method": method}
        if params is not None:
            message["params"] = params
        self.websocket.send_json(message)
        while True:
            response = self.websocket.receive_json()
            if response.get("id") != command_id:
                continue
            if "error" in response:
                raise DevToolsError(f"{method}: {response['error']}")
            return response.get("result", {})

    def evaluate(self, expression):
        result = self.command(
            "Runtime.evaluate",
            {
                "expression": expression,
                "awaitPromise": True,
                "returnByValue": True,
                "userGesture": True,
            },
        )
        if "exceptionDetails" in result:
            description = result["exceptionDetails"].get("exception", {}).get("description")
            raise DevToolsError(description or result["exceptionDetails"].get("text", "Evaluation failed"))
        return result.get("result", {}).get("value")

    def wait_for(self, expression, description, timeout=20):
        deadline = time.monotonic() + timeout
        last_error = None
        while time.monotonic() < deadline:
            try:
                value = self.evaluate(expression)
                if value:
                    return value
            except DevToolsError as error:
                last_error = error
            time.sleep(0.05)
        suffix = f" ({last_error})" if last_error else ""
        raise EvidenceFailure(f"Timed out waiting for {description}{suffix}")

    def set_viewport(self, width, height, touch=False):
        self.command(
            "Emulation.setDeviceMetricsOverride",
            {
                "width": width,
                "height": height,
                "screenWidth": width,
                "screenHeight": height,
                "deviceScaleFactor": 1,
                "mobile": touch,
            },
        )
        self.command(
            "Emulation.setTouchEmulationEnabled",
            {"enabled": touch, "maxTouchPoints": 1},
        )

    def set_reduced_motion(self, reduced):
        self.command(
            "Emulation.setEmulatedMedia",
            {
                "media": "",
                "features": [
                    {
                        "name": "prefers-reduced-motion",
                        "value": "reduce" if reduced else "no-preference",
                    }
                ],
            },
        )

    def navigate(self, origin, path, ready_selector=".app-shell"):
        self.command("Page.navigate", {"url": f"{origin}{path}"})
        self.wait_for("document.readyState === 'complete'", f"{path} document load")
        selector = json.dumps(ready_selector)
        self.wait_for(
            f"document.querySelector({selector}) !== null",
            f"{path} application render",
            timeout=30,
        )

    def click_text(self, text, selector="button, a"):
        expression = f"""
        (() => {{
          const wanted = {json.dumps(text)};
          const element = [...document.querySelectorAll({json.dumps(selector)})]
            .find(candidate => candidate.textContent.trim() === wanted);
          if (!element) return false;
          element.click();
          return true;
        }})()
        """
        require(self.evaluate(expression), f"activated {text!r}")

    def set_value(self, selector, value):
        expression = f"""
        (() => {{
          const element = document.querySelector({json.dumps(selector)});
          if (!element) return false;
          element.value = {json.dumps(value)};
          element.dispatchEvent(new Event('input', {{ bubbles: true }}));
          element.dispatchEvent(new Event('change', {{ bubbles: true }}));
          return true;
        }})()
        """
        require(self.evaluate(expression), f"set {selector} to the headless fixture value")

    def send_key(self, key, activate=False):
        details = {
            "Enter": ("Enter", 13),
            "Escape": ("Escape", 27),
            "Tab": ("Tab", 9),
            " ": ("Space", 32),
        }
        code, virtual_key = details[key]
        base = {
            "key": key,
            "code": code,
            "windowsVirtualKeyCode": virtual_key,
            "nativeVirtualKeyCode": virtual_key,
        }
        text = "\r" if activate and key == "Enter" else key if activate and key == " " else ""
        down = {"type": "keyDown" if text else "rawKeyDown", **base}
        if text:
            down["text"] = text
            down["unmodifiedText"] = text
        self.command("Input.dispatchKeyEvent", down)
        self.command("Input.dispatchKeyEvent", {"type": "keyUp", **base})

    def mouse(self, event_type, point, buttons=0):
        self.command(
            "Input.dispatchMouseEvent",
            {
                "type": event_type,
                "x": point["x"],
                "y": point["y"],
                "button": "left",
                "buttons": buttons,
                "clickCount": 1,
            },
        )

    def mouse_click(self, point):
        self.mouse("mousePressed", point, buttons=1)
        self.mouse("mouseReleased", point)

    def touch(self, event_type, point=None):
        touch_points = []
        if point is not None:
            touch_points = [
                {
                    "x": point["x"],
                    "y": point["y"],
                    "radiusX": 1,
                    "radiusY": 1,
                    "force": 1,
                    "id": 1,
                }
            ]
        self.command(
            "Input.dispatchTouchEvent",
            {"type": event_type, "touchPoints": touch_points},
        )

    def touch_tap(self, point):
        self.touch("touchStart", point)
        self.touch("touchEnd")


class ViewerEvidence:
    def __init__(self, devtools):
        self.devtools = devtools

    def prepare_control(self, scope, control):
        expression = f"""
        (() => {{
          const root = document.querySelector({json.dumps(scope)});
          const opener = root?.querySelector({json.dumps(control)});
          if (!opener) return null;
          const press = opener.closest('.card-press');
          press?.querySelector(':scope > .card-press-surface')?.focus();
          opener.focus();
          window.__viewerOpener = opener;
          const rect = opener.getBoundingClientRect();
          return {{
            x: rect.left + rect.width / 2,
            y: rect.top + rect.height / 2,
            width: rect.width,
            height: rect.height,
            label: opener.getAttribute('aria-label')
          }};
        }})()
        """
        point = self.devtools.evaluate(expression)
        require(point is not None and point["width"] > 0 and point["height"] > 0, f"found visible reading control in {scope}")
        require(
            self.devtools.evaluate("document.activeElement === window.__viewerOpener"),
            "reading control has keyboard focus before activation",
        )
        return point

    def open_control(self, scope, control, touch=False):
        point = self.prepare_control(scope, control)
        if touch:
            self.devtools.touch_tap(point)
        else:
            self.devtools.send_key("Enter", activate=True)
        self.devtools.wait_for(
            "document.querySelector('.card-viewer') !== null",
            "card viewer to open",
        )
        self.devtools.wait_for(
            "document.activeElement === document.querySelector('.card-viewer')",
            "card viewer to take focus",
        )
        require(True, f"actual {'touch' if touch else 'keyboard'} reading-control activation opened the viewer")
        return point

    def close_with_key(self, key):
        self.devtools.send_key(key)
        self._wait_closed()
        self.require_exact_focus_return("Space" if key == " " else key)

    def close_with_pointer(self, touch=False):
        point = self.devtools.evaluate(
            """
            (() => {
              const rect = document.querySelector('.card-viewer').getBoundingClientRect();
              return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
            })()
            """
        )
        if touch:
            self.devtools.touch_tap(point)
        else:
            self.devtools.mouse_click(point)
        self._wait_closed()
        self.require_exact_focus_return("touch pointer" if touch else "mouse pointer")

    def require_exact_focus_return(self, route):
        require(
            self.devtools.evaluate("document.activeElement === window.__viewerOpener"),
            f"{route} dismissal returned focus to the exact opener",
        )

    def require_reduced_motion(self, page):
        state = self.devtools.evaluate(
            """
            (() => {
              const viewer = document.querySelector('.card-viewer');
              const face = viewer.querySelector('.card-face-host');
              return {
                media: matchMedia('(prefers-reduced-motion: reduce)').matches,
                viewerAnimation: getComputedStyle(viewer).animationName,
                faceAnimation: getComputedStyle(face).animationName,
                running: viewer.getAnimations({ subtree: true }).length
              };
            })()
            """
        )
        require(
            state["media"]
            and state["viewerAnimation"] == "none"
            and state["faceAnimation"] == "none"
            and state["running"] == 0,
            f"{page} representative honors reduced motion",
        )

    def capture_source_face(self, scope):
        expression = f"""
        (() => {{
          const face = document.querySelector({json.dumps(scope)})?.querySelector('.card-face-host');
          if (!face) return false;
          window.__sourceFace = face;
          return true;
        }})()
        """
        require(self.devtools.evaluate(expression), f"captured the unchanged source face in {scope}")

    def require_geometry(self, width, height):
        time.sleep(0.25)
        geometry = self.devtools.evaluate(
            """
            (() => {
              const source = window.__sourceFace;
              const viewer = document.querySelector('.card-viewer');
              const face = viewer?.querySelector('.card-face-host');
              if (!source?.isConnected || !face) return null;
              const sourceRect = source.getBoundingClientRect();
              const faceRect = face.getBoundingClientRect();
              const geometrySelector = [
                '.blokemon-gym-card',
                '[data-region]',
                '.classification',
                '.card-name',
                '.hp-cluster',
                '.main-type-icon',
                '.rule-entry',
                '.entry-energy',
                '.entry-copy',
                '.entry-damage',
                '.footer-rule',
                '.footer-stat',
                '.set-strip'
              ].join(',');
              const sourceNodes = [...source.querySelectorAll(geometrySelector)];
              const faceNodes = [...face.querySelectorAll(geometrySelector)];
              let maximumDelta = 0;
              let compared = 0;
              for (let index = 0; index < sourceNodes.length; index++) {
                const before = sourceNodes[index].getBoundingClientRect();
                const after = faceNodes[index]?.getBoundingClientRect();
                if (!after || before.width <= 0 || before.height <= 0 || after.width <= 0 || after.height <= 0) continue;
                const values = [
                  (before.left - sourceRect.left) / sourceRect.width,
                  (before.top - sourceRect.top) / sourceRect.height,
                  before.width / sourceRect.width,
                  before.height / sourceRect.height
                ];
                const expanded = [
                  (after.left - faceRect.left) / faceRect.width,
                  (after.top - faceRect.top) / faceRect.height,
                  after.width / faceRect.width,
                  after.height / faceRect.height
                ];
                for (let value = 0; value < values.length; value++) {
                  maximumDelta = Math.max(maximumDelta, Math.abs(values[value] - expanded[value]));
                }
                compared++;
              }
              const margins = [
                faceRect.left,
                faceRect.top,
                innerWidth - faceRect.right,
                innerHeight - faceRect.bottom
              ];
              return {
                htmlEqual: source.innerHTML === face.innerHTML,
                sameNodes: sourceNodes.length === faceNodes.length,
                compared,
                maximumDelta,
                scaleDelta: Math.abs(faceRect.width / sourceRect.width - faceRect.height / sourceRect.height),
                contained: margins.every(margin => margin >= 19.5),
                bound: Math.min(...margins.map(margin => Math.abs(margin - 20))) <= 0.75,
                oneFace: viewer.querySelectorAll('.card-face-host').length === 1,
                cardOnly: viewer.children.length === 1 && viewer.firstElementChild === face
              };
            })()
            """
        )
        require(geometry is not None, f"measured viewer geometry at {width}x{height}")
        print(f"INFO {width}x{height} geometry {json.dumps(geometry, sort_keys=True)}")
        require(
            geometry["htmlEqual"] and geometry["sameNodes"] and geometry["compared"] > 8,
            f"{width}x{height} viewer reuses unchanged card markup and content",
        )
        require(
            geometry["maximumDelta"] < 0.003 and geometry["scaleDelta"] < 0.002,
            f"{width}x{height} card title, art, rules, spacing, and alignment change only by uniform scale",
        )
        require(
            geometry["contained"] and geometry["bound"],
            f"{width}x{height} viewer keeps the enlarged face within the 20px viewport margin",
        )
        require(
            geometry["oneFace"] and geometry["cardOnly"],
            f"{width}x{height} viewer contains only the one canonical card face",
        )

    def require_tab_guard(self):
        before = self.devtools.evaluate("scrollY")
        self.devtools.send_key("Tab")
        guarded = self.devtools.evaluate(
            "document.activeElement === document.querySelector('.card-viewer') && scrollY === 0"
        )
        require(guarded and before == 0, "Tab is guarded and focus remains in the viewer")

    def require_sibling_dom(self, scope):
        expression = f"""
        (() => {{
          const root = document.querySelector({json.dumps(scope)});
          const surface = root?.querySelector(':scope > .card-press-surface');
          const reader = root?.querySelector('.card-read');
          if (!surface || !reader) return false;
          const interactiveAncestor = reader.parentElement.closest(
            'button, a[href], input, select, textarea, [role="button"]'
          );
          return !surface.contains(reader)
            && reader.closest('.card-press') === root
            && interactiveAncestor === null;
        }})()
        """
        require(self.devtools.evaluate(expression), "reader and action surface are non-nested sibling controls")

    def mouse_hold(self, scope, control):
        point = self.prepare_control(scope, control)
        self.devtools.mouse("mousePressed", point, buttons=1)
        self.devtools.wait_for(
            "document.querySelector('.card-viewer') !== null",
            "mouse hold viewer",
            timeout=2,
        )
        self.devtools.mouse("mouseReleased", point)
        self._wait_closed()
        require(True, "mouse hold opens only for the hold and closes on release")

    def touch_hold(self, scope, control):
        point = self.prepare_control(scope, control)
        self.devtools.touch("touchStart", point)
        self.devtools.wait_for(
            "document.querySelector('.card-viewer') !== null",
            "touch hold viewer",
            timeout=2,
        )
        self.devtools.touch("touchEnd")
        time.sleep(0.1)
        require(
            self.devtools.evaluate("document.querySelector('.card-viewer') !== null"),
            "first touch lift leaves the held card readable",
        )
        viewer_point = self.devtools.evaluate(
            """
            (() => {
              const rect = document.querySelector('.card-viewer').getBoundingClientRect();
              return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
            })()
            """
        )
        self.devtools.touch_tap(viewer_point)
        self._wait_closed()
        require(True, "a separate touch puts down a held card")

    def _wait_closed(self):
        self.devtools.wait_for(
            "document.querySelector('.card-viewer') === null",
            "card viewer to close",
        )


def setup_player(devtools, origin, viewer):
    devtools.set_viewport(1440, 900)
    devtools.set_reduced_motion(True)
    devtools.navigate(origin, "/")
    devtools.wait_for(
        "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Use this browser')",
        "browser-local mode choice",
    )
    devtools.click_text("Use this browser")
    devtools.wait_for("document.querySelector('a[href=" + json.dumps("profile") + "]') !== null", "profile route link")
    devtools.click_text("Create player")
    devtools.wait_for("document.querySelector('#display-name') !== null", "profile form")
    devtools.set_value("#display-name", "Headless Player")
    devtools.click_text("Create player")
    devtools.wait_for(
        "[...document.querySelectorAll('.starter-option button')].some(button => button.textContent.trim().startsWith('Open '))",
        "starter catalogue",
    )

    before = devtools.evaluate(
        "[...document.querySelectorAll('.starter-option > button')].filter(button => !button.disabled).length"
    )
    viewer.open_control(".starter-option .leader-stage .card-viewer-trigger", ".card-press-surface")
    viewer.require_reduced_motion("Home")
    viewer.close_with_key("Escape")
    after = devtools.evaluate(
        "[...document.querySelectorAll('.starter-option > button')].filter(button => !button.disabled).length"
    )
    require(before == after and before > 0, "reading the Home starter leader does not claim a deck")

    opened = devtools.evaluate(
        """
        (() => {
          const button = [...document.querySelectorAll('.starter-option > button')]
            .find(candidate => candidate.textContent.trim().startsWith('Open '));
          if (!button) return false;
          button.click();
          return true;
        })()
        """
    )
    require(opened, "claimed one starter through the checked-out game")
    devtools.wait_for(
        "[...document.querySelectorAll('h1')].some(heading => heading.textContent.trim() === 'Your deck is ready.')",
        "claimed starter state",
        timeout=30,
    )


def pack_gating(devtools, origin, viewer):
    devtools.set_reduced_motion(False)
    devtools.navigate(origin, "/packs")
    devtools.wait_for(
        "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Open pack')",
        "pack order",
    )
    devtools.click_text("Open pack")
    devtools.wait_for("document.querySelector('button.opening-pack') !== null", "sealed pack")
    point = devtools.evaluate(
        """
        (() => {
          const rect = document.querySelector('button.opening-pack').getBoundingClientRect();
          return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        })()
        """
    )
    devtools.mouse_click(point)
    devtools.wait_for("document.querySelector('.pack-reveal-card') !== null", "first hidden reveal card", timeout=10)
    hidden = devtools.evaluate(
        """
        (() => {
          const reveal = document.querySelector('.pack-reveal-card');
          const face = reveal.querySelector('.reveal-flip-front .card-face-host');
          const readControls = [...document.querySelectorAll('button[aria-label]')]
            .filter(button => button.getAttribute('aria-label').startsWith('Read '));
          return {
            readControls: readControls.length,
            revealReaders: reveal.querySelectorAll('.card-read').length,
            surfaceLabel: reveal.querySelector(':scope > .card-press-surface').getAttribute('aria-label'),
            faceHidden: face.getAttribute('aria-hidden') === 'true',
            step: document.querySelector('.opening-ceremony-copy .eyebrow').textContent.trim()
          };
        })()
        """
    )
    require(
        hidden["readControls"] == 0
        and hidden["revealReaders"] == 0
        and hidden["surfaceLabel"] == "Turn the card over"
        and hidden["faceHidden"],
        "Pack exposes no card identity or reading trigger before face-up",
    )

    point = devtools.evaluate(
        """
        (() => {
          const rect = document.querySelector('.pack-reveal-card > .card-press-surface').getBoundingClientRect();
          return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        })()
        """
    )
    devtools.mouse_click(point)
    devtools.wait_for("document.querySelector('.pack-reveal-card .card-read') !== null", "face-up card reader")
    viewer.require_sibling_dom(".pack-reveal-card")
    step = devtools.evaluate("document.querySelector('.opening-ceremony-copy .eyebrow').textContent.trim()")
    viewer.open_control(".pack-reveal-card", ".card-read")
    require(
        devtools.evaluate(
            "document.querySelector('.opening-ceremony-copy .eyebrow').textContent.trim() === "
            + json.dumps(step)
        ),
        "reading a face-up Pack reveal does not advance it",
    )
    viewer.close_with_key("Escape")
    require(
        devtools.evaluate(
            "document.querySelector('.opening-ceremony-copy .eyebrow').textContent.trim() === "
            + json.dumps(hidden["step"])
        ),
        "closing the Pack reader leaves the same reveal step active",
    )
    devtools.click_text("Skip animation")
    devtools.wait_for(
        "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Done')",
        "pack summary",
    )
    devtools.click_text("Done")
    devtools.wait_for("document.querySelector('.recent-pack-cards') !== null", "closed pack summary")


def collection_lifecycle(devtools, origin, viewer):
    scope = ".card-grid .card-tile"
    control = ".card-press-surface"
    devtools.set_viewport(1440, 900)
    devtools.set_reduced_motion(False)
    devtools.navigate(origin, "/collection")
    devtools.wait_for(f"document.querySelector({json.dumps(scope + ' ' + control)}) !== null", "Collection card")
    viewer.capture_source_face(scope)
    viewer.open_control(scope, control)
    viewer.require_geometry(1440, 900)
    viewer.require_tab_guard()
    viewer.close_with_key("Enter")

    viewer.open_control(scope, control)
    viewer.close_with_key(" ")
    viewer.open_control(scope, control)
    viewer.close_with_key("Escape")
    viewer.open_control(scope, control)
    viewer.close_with_pointer()
    viewer.mouse_hold(scope, control)

    devtools.set_viewport(390, 844, touch=True)
    devtools.navigate(origin, "/collection")
    devtools.wait_for(f"document.querySelector({json.dumps(scope + ' ' + control)}) !== null", "mobile Collection card")
    viewer.capture_source_face(scope)
    viewer.open_control(scope, control, touch=True)
    viewer.require_geometry(390, 844)
    viewer.close_with_pointer(touch=True)
    viewer.touch_hold(scope, control)


def reduced_motion_representatives(devtools, origin, viewer):
    devtools.set_viewport(1440, 900)
    devtools.set_reduced_motion(True)
    representatives = [
        ("Collection", "/collection", ".card-grid .card-tile"),
        ("Home", "/", ".activity-row"),
        ("Packs", "/packs", ".recent-pack-cards > div"),
        ("Decks", "/decks", ".catalogue-card"),
    ]
    for name, path, scope in representatives:
        devtools.navigate(origin, path)
        selector = scope + " .card-press-surface"
        devtools.wait_for(
            f"document.querySelector({json.dumps(selector)}) !== null",
            f"{name} reading representative",
        )
        before = None
        if name == "Decks":
            before = devtools.evaluate(
                "document.querySelector('.catalogue-card output')?.textContent.trim()"
            )
        viewer.open_control(scope, ".card-press-surface")
        viewer.require_reduced_motion(name)
        viewer.close_with_key("Escape")
        if name == "Decks":
            after = devtools.evaluate(
                "document.querySelector('.catalogue-card output')?.textContent.trim()"
            )
            require(before == after, "reading a Decks card does not operate its quantity stepper")

    devtools.navigate(origin, "/match")
    devtools.wait_for(
        "document.querySelector('.battle-screen') !== null || "
        "[...document.querySelectorAll('button')].some(candidate => candidate.textContent.trim() === 'Start battle')",
        "Match start state",
        timeout=30,
    )
    if devtools.evaluate("document.querySelector('.battle-screen') !== null"):
        start = True
    else:
        start = devtools.evaluate(
            """
            (() => {
              const button = [...document.querySelectorAll('button')]
                .find(candidate => candidate.textContent.trim() === 'Start battle');
              if (!button) return false;
              button.click();
              return true;
            })()
            """
        )
    require(start, "started a Match representative through the checked-out game")
    devtools.wait_for("document.querySelector('.battle-screen') !== null", "Match table", timeout=30)
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline and not devtools.evaluate(
        "document.querySelector('.battle-screen .card-press:has(.card-read)') !== null"
    ):
        devtools.evaluate(
            """
            (() => {
              const reveal = document.querySelector('.card-reveal-overlay .reveal-continue');
              if (reveal) {
                reveal.click();
                return 'reveal';
              }
              const skip = document.querySelector('button.skip-animation');
              if (skip) {
                skip.click();
                return 'skip';
              }
              return null;
            })()
            """
        )
        time.sleep(0.1)
    require(
        devtools.evaluate(
            "document.querySelector('.battle-screen .card-press:has(.card-read)') !== null"
        ),
        "Match exposes a stable sibling reader after its opening presentation",
    )
    scope = ".battle-screen .card-press:has(.card-read)"
    viewer.require_sibling_dom(scope)
    state = devtools.evaluate(
        """
        (() => {
          const surface = document.querySelector('.battle-screen .card-press:has(.card-read) > .card-press-surface');
          window.__matchActionSurface = surface;
          return { pressed: surface.getAttribute('aria-pressed'), label: surface.getAttribute('aria-label') };
        })()
        """
    )
    viewer.open_control(scope, ".card-read")
    viewer.require_reduced_motion("Match")
    viewer.close_with_key("Escape")
    require(
        devtools.evaluate(
            """
            (() => {
              const surface = window.__matchActionSurface;
              return surface?.isConnected
                && surface.getAttribute('aria-pressed') === %s
                && surface.getAttribute('aria-label') === %s;
            })()
            """
            % (json.dumps(state["pressed"]), json.dumps(state["label"]))
        ),
        "reading a Match card does not select or operate its action surface",
    )


def cue_isolation(devtools, origin, viewer):
    devtools.set_viewport(1440, 900)
    devtools.set_reduced_motion(True)
    devtools.navigate(origin, "/", ready_selector="#cue-acknowledgements")
    devtools.wait_for("document.querySelector('.card-reveal-overlay') !== null", "revealed cue fixture")
    require(
        devtools.evaluate("document.querySelector('#cue-acknowledgements').value === '0'"),
        "reveal cue starts unacknowledged",
    )
    viewer.open_control(".card-reveal-overlay .card-viewer-trigger", ".card-press-surface")
    viewer.require_reduced_motion("revealed cue")
    require(
        devtools.evaluate("document.querySelector('#cue-acknowledgements').value === '0'"),
        "opening a revealed cue card does not acknowledge the cue",
    )
    viewer.close_with_key("Escape")
    require(
        devtools.evaluate("document.querySelector('#cue-acknowledgements').value === '0'"),
        "closing a revealed cue card does not acknowledge the cue",
    )
    point = devtools.evaluate(
        """
        (() => {
          const rect = document.querySelector('.reveal-note').getBoundingClientRect();
          return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        })()
        """
    )
    devtools.mouse_click(point)
    devtools.wait_for(
        "document.querySelector('#cue-acknowledgements').value === '1'",
        "cue acknowledgement surface",
    )
    require(True, "the cue still acknowledges through its own surface")


def main():
    check_consumer_inventory()
    with tempfile.TemporaryDirectory(prefix="blokemon-card-viewer-") as temporary:
        temporary_root = Path(temporary)
        game_output = temporary_root / "game"
        cue_output = temporary_root / "cue"
        run(
            [
                "dotnet",
                "publish",
                "src/Blokemon.Web.Client/Blokemon.Web.Client.csproj",
                "--configuration",
                "Release",
                "--output",
                str(game_output),
                "-p:StandaloneBrowser=true",
                "-p:PublishTrimmed=false",
                "-p:TreatWarningsAsErrors=true",
            ]
        )
        run(
            [
                "dotnet",
                "publish",
                "tests/Blokemon.Web.Headless/Blokemon.Web.Headless.csproj",
                "--configuration",
                "Release",
                "--output",
                str(cue_output),
                "-p:PublishTrimmed=false",
                "-p:TreatWarningsAsErrors=true",
            ]
        )
        with HostedRoot(published_root(game_output)) as game, HostedRoot(
            published_root(cue_output)
        ) as cue:
            chrome = Chrome(temporary_root)
            try:
                viewer = ViewerEvidence(chrome.devtools)
                setup_player(chrome.devtools, game.origin, viewer)
                pack_gating(chrome.devtools, game.origin, viewer)
                collection_lifecycle(chrome.devtools, game.origin, viewer)
                reduced_motion_representatives(chrome.devtools, game.origin, viewer)
                cue_isolation(chrome.devtools, cue.origin, viewer)
            finally:
                chrome.close()
    print("Headless card-viewer evidence passed.")


if __name__ == "__main__":
    main()
