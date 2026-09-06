// The computer's own runtime. This file runs in a Web Worker and boots a second copy of the
// same .NET runtime and assemblies the page runs on, from the same framework files the page has
// already fetched, so the policy that decides the computer's moves is the code the page ships
// and never a second build of it. The page hands over a saved battle and gets back the id of the
// candidate the policy chose; the page's own engine turns that into the command it applies.
let computer = null;

// The framework's loader takes a worker that already listens for messages to be one of its own
// thread workers, and waits for a parent runtime that never comes. This worker hosts a whole
// runtime of its own, and says so before the loader is imported, in the loader's own terms.
globalThis.dotnetSidecar = true;

// Boots the runtime from the module the page itself booted from: the page resolves its import
// map to the runtime file built for it, which carries the boot configuration, and hands the
// address over, since a worker reads no import map of its own.
async function boot(runtimeUrl) {
  const { dotnet } = await import(runtimeUrl);
  const runtime = await dotnet.create();
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  return exports.Blokemon.Web.Client.Application.ComputerWorker;
}

async function prepare(runtimeUrl, catalogueUrl) {
  const [worker, catalogue] = await Promise.all([
    boot(runtimeUrl),
    fetch(catalogueUrl).then((response) => {
      if (!response.ok) {
        throw new Error(`The catalogue could not be fetched (${response.status}).`);
      }
      return response.text();
    }),
  ]);
  const versions = JSON.parse(worker.Prepare(catalogue));
  computer = worker;
  return versions;
}

// What went wrong, in words the page can show: the framework's loader fails with plain objects
// as well as errors.
function describe(error) {
  if (error instanceof Error) {
    return error.stack || error.message;
  }
  try {
    return JSON.stringify(error);
  } catch {
    return String(error);
  }
}

self.addEventListener("message", async (event) => {
  const { id, kind } = event.data;
  try {
    if (kind === "prepare") {
      const versions = await prepare(event.data.runtimeUrl, event.data.catalogueUrl);
      self.postMessage({ id, kind: "prepared", ...versions });
    } else if (kind === "decide") {
      if (!computer) {
        throw new Error("The computer is not prepared.");
      }
      const candidate = computer.Decide(event.data.document);
      self.postMessage({ id, kind: "decided", candidate: candidate ?? null });
    }
  } catch (error) {
    self.postMessage({ id, kind: "failed", error: describe(error) });
  }
});
