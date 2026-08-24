const databaseName = "blokemon-browser-local-v1";
const storeName = "documents";
const broadcastChannelName = "blokemon-browser-state-v1";
const missingDocument = Symbol("missing-document");
const documents = new Map();
const documentGenerations = new Map();
let documentCacheGeneration = 0;
let connection = null;
let broadcastChannel = null;
let nextInvalidationSubscription = 1;
const invalidationSubscriptions = new Map();

function failure(error) {
    const name = error?.name ?? "UnknownError";
    const message = error?.message ?? "Browser storage is unavailable.";
    return new Error(`${name}: ${message}`);
}

function requestResult(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(failure(request.error));
    });
}

function transactionComplete(transaction) {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onabort = () => reject(failure(transaction.error));
        transaction.onerror = () => reject(failure(transaction.error));
    });
}

function invalidateConnection(current, closeDatabase) {
    if (connection === current) {
        connection = null;
    }

    if (closeDatabase && current.database) {
        current.database.close();
    }
}

function documentGeneration(key) {
    return documentGenerations.get(key) ?? 0;
}

function advanceDocumentGeneration(key) {
    documentGenerations.set(key, documentGeneration(key) + 1);
}

function invalidateDocument(key) {
    advanceDocumentGeneration(key);
    documents.delete(key);
}

function cacheDocument(key, document) {
    advanceDocumentGeneration(key);
    documents.set(key, document ?? missingDocument);
}

function clearDocuments() {
    documentCacheGeneration++;
    documents.clear();
}

function currentBroadcastChannel() {
    if (broadcastChannel || !globalThis.BroadcastChannel) {
        return broadcastChannel;
    }

    try {
        const current = new BroadcastChannel(broadcastChannelName);
        current.onmessage = (event) => {
            if (typeof event.data === "string") {
                invalidateDocument(event.data);
                for (const receiver of invalidationSubscriptions.values()) {
                    receiver.invokeMethodAsync("Invalidated", event.data).catch(() => {});
                }
            }
        };
        broadcastChannel = current;
    } catch {
        return null;
    }

    return broadcastChannel;
}

function broadcastInvalidation(key) {
    try {
        currentBroadcastChannel()?.postMessage(key);
    } catch {
        // CAS conflicts remain authoritative when cross-tab notification is unavailable.
    }
}

function closeBroadcastChannel() {
    if (broadcastChannel) {
        broadcastChannel.close();
        broadcastChannel = null;
    }
}

async function openDatabase(current) {
    if (!globalThis.indexedDB) {
        throw new Error("NotSupportedError: This browser does not provide IndexedDB.");
    }

    const request = indexedDB.open(databaseName, 1);
    request.onupgradeneeded = () => {
        const database = request.result;
        if (!database.objectStoreNames.contains(storeName)) {
            database.createObjectStore(storeName, { keyPath: "key" });
        }
    };
    const database = await requestResult(request);
    current.database = database;
    database.onversionchange = () => {
        clearDocuments();
        invalidateConnection(current, true);
    };
    database.onclose = () => {
        clearDocuments();
        invalidateConnection(current, false);
    };

    if (connection !== current) {
        database.close();
        throw new Error("AbortError: Browser storage closed while it was opening.");
    }

    return database;
}

function currentConnection() {
    if (connection) {
        return connection;
    }

    const current = { database: null, promise: null };
    current.promise = openDatabase(current).catch((error) => {
        invalidateConnection(current, false);
        throw error;
    });
    connection = current;
    return current;
}

async function useDatabase(operation) {
    currentBroadcastChannel();
    const current = currentConnection();
    const database = await current.promise;
    try {
        return await operation(database);
    } catch (error) {
        invalidateConnection(current, true);
        throw error;
    }
}

export async function read(key) {
    if (documents.has(key)) {
        const cached = documents.get(key);
        return cached === missingDocument ? null : cached;
    }

    const cacheGeneration = documentCacheGeneration;
    const generation = documentGeneration(key);
    const document = await useDatabase(async (database) => {
        const transaction = database.transaction(storeName, "readonly");
        const row = await requestResult(transaction.objectStore(storeName).get(key));
        await transactionComplete(transaction);
        return row ? { revision: row.revision, json: row.json } : null;
    });

    if (
        documentCacheGeneration === cacheGeneration &&
        documentGeneration(key) === generation
    ) {
        documents.set(key, document ?? missingDocument);
    }

    return document;
}

export async function create(key, json) {
    try {
        const revision = await useDatabase(async (database) => {
            const transaction = database.transaction(storeName, "readwrite");
            const completion = transactionComplete(transaction);
            const request = transaction.objectStore(storeName).add({ key, revision: 1, json });
            try {
                await requestResult(request);
                await completion;
                return 1;
            } catch (error) {
                try {
                    await completion;
                } catch {
                    if (error.message.startsWith("ConstraintError:")) {
                        return null;
                    }
                }

                throw error;
            }
        });

        if (revision === null) {
            invalidateDocument(key);
            return null;
        }

        cacheDocument(key, { revision, json });
        broadcastInvalidation(key);
        return revision;
    } catch (error) {
        invalidateDocument(key);
        throw error;
    }
}

export async function remove(key) {
    try {
        await useDatabase(async (database) => {
            const transaction = database.transaction(storeName, "readwrite");
            const completion = transactionComplete(transaction);
            try {
                await requestResult(transaction.objectStore(storeName).delete(key));
                await completion;
            } catch (error) {
                try {
                    await completion;
                } catch {
                    throw error;
                }
                throw error;
            }
        });
        cacheDocument(key, null);
        broadcastInvalidation(key);
        return null;
    } catch (error) {
        invalidateDocument(key);
        throw error;
    }
}

export async function update(key, expectedRevision, json) {
    try {
        const revision = await useDatabase(async (database) => {
            const transaction = database.transaction(storeName, "readwrite");
            const completion = transactionComplete(transaction);
            const store = transaction.objectStore(storeName);
            try {
                const current = await requestResult(store.get(key));
                if (!current || current.revision !== expectedRevision) {
                    transaction.abort();
                    try {
                        await completion;
                    } catch {
                        return null;
                    }
                    return null;
                }

                const nextRevision = expectedRevision + 1;
                await requestResult(store.put({ key, revision: nextRevision, json }));
                await completion;
                return nextRevision;
            } catch (error) {
                try {
                    await completion;
                } catch {
                    throw error;
                }
                throw error;
            }
        });

        if (revision === null) {
            invalidateDocument(key);
            return null;
        }

        cacheDocument(key, { revision, json });
        broadcastInvalidation(key);
        return revision;
    } catch (error) {
        invalidateDocument(key);
        throw error;
    }
}

export function subscribeInvalidation(receiver) {
    const id = nextInvalidationSubscription++;
    invalidationSubscriptions.set(id, receiver);
    currentBroadcastChannel();
    return id;
}

export function unsubscribeInvalidation(id) {
    invalidationSubscriptions.delete(id);
}

globalThis.addEventListener?.("pagehide", () => {
    clearDocuments();
    invalidationSubscriptions.clear();
    if (connection) {
        invalidateConnection(connection, true);
    }
    closeBroadcastChannel();
});

currentBroadcastChannel();
