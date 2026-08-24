const databaseName = "blokemon-browser-local-v1";
const storeName = "documents";
let connection = null;

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
    database.onversionchange = () => invalidateConnection(current, true);
    database.onclose = () => invalidateConnection(current, false);

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
    return await useDatabase(async (database) => {
        const transaction = database.transaction(storeName, "readonly");
        const row = await requestResult(transaction.objectStore(storeName).get(key));
        await transactionComplete(transaction);
        return row ? { revision: row.revision, json: row.json } : null;
    });
}

export async function create(key, json) {
    return await useDatabase(async (database) => {
        const transaction = database.transaction(storeName, "readwrite");
        const completion = transactionComplete(transaction);
        const request = transaction.objectStore(storeName).add({ key, revision: 1, json });
        try {
            await requestResult(request);
            await completion;
            return 1;
        } catch (error) {
            if (error.message.startsWith("ConstraintError:")) {
                try {
                    await completion;
                } catch {
                    // A duplicate add aborts this transaction without changing the saved row.
                }
                return null;
            }
            throw error;
        }
    });
}

export async function remove(key) {
    return await useDatabase(async (database) => {
        const transaction = database.transaction(storeName, "readwrite");
        const completion = transactionComplete(transaction);
        await requestResult(transaction.objectStore(storeName).delete(key));
        await completion;
        return null;
    });
}

export async function update(key, expectedRevision, json) {
    return await useDatabase(async (database) => {
        const transaction = database.transaction(storeName, "readwrite");
        const completion = transactionComplete(transaction);
        const store = transaction.objectStore(storeName);
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

        const revision = expectedRevision + 1;
        await requestResult(store.put({ key, revision, json }));
        await completion;
        return revision;
    });
}

globalThis.addEventListener?.("pagehide", () => {
    if (connection) {
        invalidateConnection(connection, true);
    }
});
