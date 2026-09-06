// The page's side of the computer's own runtime: one Web Worker, started when a battle opens so
// its boot is paid before the first decision is wanted, asked one decision at a time, and stopped
// when the battle is left. Every call answers with a promise; the page's thread is never held.
let worker = null;
let nextId = 1;
const pending = new Map();

function settle(message) {
  const waiting = pending.get(message.id);
  if (!waiting) {
    return;
  }
  pending.delete(message.id);
  if (message.kind === "failed") {
    waiting.reject(new Error(message.error));
  } else {
    waiting.resolve(message);
  }
}

function failAll(reason) {
  for (const waiting of pending.values()) {
    waiting.reject(new Error(reason));
  }
  pending.clear();
}

// How long the page waits for the worker: a boot that takes longer than this, or a decision
// that does, is a runtime that has wedged, and the page's own computer takes over from it.
const BOOT_LIMIT_MS = 60_000;
const DECIDE_LIMIT_MS = 120_000;

function ask(message, limit) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    const timer = setTimeout(() => {
      if (pending.delete(id)) {
        reject(new Error("The computer's runtime did not answer in time."));
      }
    }, limit);
    pending.set(id, {
      resolve: (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      reject: (error) => {
        clearTimeout(timer);
        reject(error);
      },
    });
    worker.postMessage({ id, ...message });
  });
}

// The file behind a module the page uses. The page's import map names the fingerprinted file
// each module is served as, and the runtime module it names is the one built for this page, with
// the page's own boot configuration inside it. A worker does not read the page's import map, so
// what it is to load is resolved here and handed over. A page with no import map serves the
// files under their own names.
function servedAs(specifier) {
  let mapped = specifier;
  const importMap = document.querySelector('script[type="importmap"]');
  if (importMap !== null) {
    try {
      mapped = JSON.parse(importMap.textContent).imports?.[specifier] ?? specifier;
    } catch {
      mapped = specifier;
    }
  }
  return new URL(mapped, document.baseURI).href;
}

export function supported() {
  return typeof Worker === "function";
}

// Boots the runtime and answers with the rules it plays by, so the page can refuse a computer
// that would decide by different rules than its own.
export async function start(catalogueUrl) {
  if (!supported()) {
    throw new Error("This browser cannot run a worker.");
  }
  if (worker === null) {
    worker = new Worker(servedAs("./computerWorker.js"), { type: "module" });
    worker.onmessage = (event) => settle(event.data);
    worker.onerror = (event) => failAll(event.message || "The computer's runtime failed.");
  }
  const prepared = await ask(
    {
      kind: "prepare",
      runtimeUrl: servedAs("./_framework/dotnet.js"),
      catalogueUrl: new URL(catalogueUrl, document.baseURI).href,
    },
    BOOT_LIMIT_MS,
  );
  return { authorityVersion: prepared.authorityVersion, policyVersion: prepared.policyVersion };
}

export async function decide(document) {
  if (worker === null) {
    throw new Error("The computer is not running.");
  }
  const decided = await ask({ kind: "decide", document }, DECIDE_LIMIT_MS);
  return decided.candidate;
}

export function stop() {
  if (worker === null) {
    return;
  }
  worker.terminate();
  worker = null;
  failAll("The computer was stopped.");
}
