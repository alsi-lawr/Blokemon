// The sessionStorage copy of the held session. It survives a reload and dies with the tab, and
// it is the only place outside memory the token is ever put.
const key = "blokemon.session";

export function read() {
    try {
        const raw = sessionStorage.getItem(key);
        if (!raw) {
            return null;
        }

        const stored = JSON.parse(raw);
        if (typeof stored?.token !== "string" || stored.token.length === 0) {
            return null;
        }

        return {
            token: stored.token,
            expiresAt: typeof stored.expiresAt === "string" ? stored.expiresAt : null,
            displayName: typeof stored.displayName === "string" ? stored.displayName : null,
            recovery: stored.recovery === true,
        };
    } catch {
        return null;
    }
}

export function write(token, expiresAt, displayName, recovery) {
    try {
        sessionStorage.setItem(key, JSON.stringify({ token, expiresAt, displayName, recovery: recovery === true }));
        return true;
    } catch {
        return false;
    }
}

export function clear() {
    try {
        sessionStorage.removeItem(key);
    } catch {
        // Nothing was stored.
    }
}
