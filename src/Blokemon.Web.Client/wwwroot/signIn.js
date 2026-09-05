// The hand-off fragment and the hosted-mode receiver.
//
// A hand-off code may travel in a URL fragment because it is single-use and lives a minute; a
// session token never travels in a URL at all. The fragment is cleared before the code is used,
// so a reload or a copied link cannot present it twice.
//
// The receiver listens from the moment the hosted document loads - this module is loaded by
// the page itself, ahead of the application - and retains what arrives before the tenant's
// registered parent origin is known. Nothing retained is accepted on arrival: once the
// origin is known each retained message is validated against it or discarded. A bound receiver
// accepts a hand-off only when event.origin equals the registered origin exactly - a subdomain
// or a different scheme or port is another origin - and posts to the parent only with that
// exact origin as the target. It never posts to "*".

const handoffType = "blokemon.handoff";
const readyType = "blokemon.ready";
const retainedLimit = 8;

// What arrived while the application was still loading, kept only for a hosted route.
const early = [];
const earlyListener = (event) => {
    if (early.length < retainedLimit) {
        early.push({ origin: event.origin, data: event.data });
    }
};
if (window.parent !== window && /^\/t\//.test(location.pathname)) {
    window.addEventListener("message", earlyListener);
}

export function readHandoffCode() {
    const hash = location.hash;
    if (!hash || hash.length < 2) {
        return null;
    }

    const code = new URLSearchParams(hash.substring(1)).get("handoff");
    if (!code) {
        return null;
    }

    history.replaceState(history.state, "", location.pathname + location.search);
    return code;
}

function isHandoff(data) {
    return (
        data !== null &&
        typeof data === "object" &&
        data.type === handoffType &&
        typeof data.code === "string" &&
        data.code.length > 0
    );
}

export function createReceiver(target, parent, deliver, retainedBefore = []) {
    let origin = null;
    let bound = false;
    const retained = retainedBefore.slice(0, retainedLimit);

    const listener = (event) => {
        if (!bound) {
            if (retained.length < retainedLimit) {
                retained.push({ origin: event.origin, data: event.data });
            }
            return;
        }

        if (origin === null || event.origin !== origin || !isHandoff(event.data)) {
            return;
        }

        deliver(event.data.code);
    };

    target.addEventListener("message", listener);

    const receiver = {
        bind(registeredParentOrigin) {
            bound = true;
            origin =
                typeof registeredParentOrigin === "string" && registeredParentOrigin.length > 0
                    ? registeredParentOrigin
                    : null;
            const pending = retained.splice(0);
            if (origin === null) {
                return false;
            }

            const accepted = pending.find(
                (message) => message.origin === origin && isHandoff(message.data),
            );
            if (accepted) {
                deliver(accepted.data.code);
            }

            receiver.post(readyType);
            return true;
        },

        post(type) {
            if (origin === null || !parent || parent === target) {
                return false;
            }

            parent.postMessage({ type }, origin);
            return true;
        },

        detach() {
            target.removeEventListener("message", listener);
        },
    };

    return receiver;
}

export function attachReceiver(dotNet) {
    window.removeEventListener("message", earlyListener);
    return createReceiver(
        window,
        window.parent,
        (code) => {
            dotNet.invokeMethodAsync("ReceiveHandoff", code).catch(() => {});
        },
        early.splice(0),
    );
}

// Opens the continuation in a new top-level window the client itself owns: the code travels in
// the fragment, never a session token, and the window's handle back to this one is severed
// once it is known to have opened (a "noopener" feature would open it blind, with no way to
// tell a blocked window from an opened one). False when the browser blocked it.
export function openContinuation(url) {
    const opened = window.open(url, "_blank");
    if (opened === null) {
        return false;
    }
    opened.opener = null;
    return true;
}
