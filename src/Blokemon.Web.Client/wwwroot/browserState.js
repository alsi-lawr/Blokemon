const databaseName = "blokemon-browser-local-v1";
const storeName = "documents";

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

async function openDatabase() {
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
    return await requestResult(request);
}

export async function read(key) {
    const database = await openDatabase();
    try {
        const transaction = database.transaction(storeName, "readonly");
        const row = await requestResult(transaction.objectStore(storeName).get(key));
        await transactionComplete(transaction);
        return row ? { revision: row.revision, json: row.json } : null;
    } finally {
        database.close();
    }
}

export async function create(key, json) {
    const database = await openDatabase();
    try {
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
    } finally {
        database.close();
    }
}

export async function remove(key) {
    const database = await openDatabase();
    try {
        const transaction = database.transaction(storeName, "readwrite");
        const completion = transactionComplete(transaction);
        await requestResult(transaction.objectStore(storeName).delete(key));
        await completion;
        return null;
    } finally {
        database.close();
    }
}

export async function update(key, expectedRevision, json) {
    const database = await openDatabase();
    try {
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
    } finally {
        database.close();
    }
}
