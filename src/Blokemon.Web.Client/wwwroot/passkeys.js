// The browser's passkey ceremonies. The server issues options with base64url byte fields, as
// the WebAuthn JSON forms have them; this module turns them into what navigator.credentials
// takes and turns the credential it returns back into the same JSON form. A ceremony the
// person declines or the browser cannot run resolves to null; nothing here throws for that.

function decode(text) {
    const padded = text.replace(/-/g, "+").replace(/_/g, "/") + "=".repeat((4 - (text.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) {
        bytes[index] = binary.charCodeAt(index);
    }
    return bytes.buffer;
}

function encode(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = "";
    for (const byte of bytes) {
        binary += String.fromCharCode(byte);
    }
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function available() {
    return typeof PublicKeyCredential !== "undefined" && !!navigator.credentials;
}

// Whether a ceremony can run in this document: always at top level, and inside a frame only
// when the parent delegated the WebAuthn permissions. A frame that cannot say is treated as
// one that cannot run it, and the continuation window is used instead.
export function canRunHere() {
    if (!available()) {
        return false;
    }
    if (window.parent === window) {
        return true;
    }
    const policy = document.permissionsPolicy || document.featurePolicy;
    if (!policy || typeof policy.allowsFeature !== "function") {
        return false;
    }
    return policy.allowsFeature("publickey-credentials-create") && policy.allowsFeature("publickey-credentials-get");
}

function creationOptions(options) {
    if (typeof PublicKeyCredential.parseCreationOptionsFromJSON === "function") {
        return PublicKeyCredential.parseCreationOptionsFromJSON(options);
    }
    return {
        ...options,
        challenge: decode(options.challenge),
        user: { ...options.user, id: decode(options.user.id) },
        excludeCredentials: (options.excludeCredentials || []).map((credential) => ({ ...credential, id: decode(credential.id) })),
    };
}

function requestOptions(options) {
    if (typeof PublicKeyCredential.parseRequestOptionsFromJSON === "function") {
        return PublicKeyCredential.parseRequestOptionsFromJSON(options);
    }
    return {
        ...options,
        challenge: decode(options.challenge),
        allowCredentials: (options.allowCredentials || []).map((credential) => ({ ...credential, id: decode(credential.id) })),
    };
}

function serialize(credential) {
    if (typeof credential.toJSON === "function") {
        return credential.toJSON();
    }
    const response = credential.response;
    const common = {
        id: credential.id,
        rawId: encode(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment ?? null,
        clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
    };
    if (response.attestationObject) {
        return {
            ...common,
            response: {
                clientDataJSON: encode(response.clientDataJSON),
                attestationObject: encode(response.attestationObject),
                transports: typeof response.getTransports === "function" ? response.getTransports() : [],
            },
        };
    }
    return {
        ...common,
        response: {
            clientDataJSON: encode(response.clientDataJSON),
            authenticatorData: encode(response.authenticatorData),
            signature: encode(response.signature),
            userHandle: response.userHandle ? encode(response.userHandle) : null,
        },
    };
}

function declined(error) {
    return error && (error.name === "NotAllowedError" || error.name === "AbortError" || error.name === "InvalidStateError");
}

export async function create(options) {
    if (!available()) {
        return null;
    }
    try {
        const credential = await navigator.credentials.create({ publicKey: creationOptions(options) });
        return credential ? serialize(credential) : null;
    } catch (error) {
        if (declined(error)) {
            return null;
        }
        throw error;
    }
}

export async function get(options) {
    if (!available()) {
        return null;
    }
    try {
        const credential = await navigator.credentials.get({ publicKey: requestOptions(options) });
        return credential ? serialize(credential) : null;
    } catch (error) {
        if (declined(error)) {
            return null;
        }
        throw error;
    }
}
