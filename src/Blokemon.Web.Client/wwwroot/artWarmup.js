// Pulls card illustrations into the browser cache during idle time, so a card that is revealed
// later paints with its art already downloaded. Warming is optimistic: it yields to first paint
// and to every interaction, it reuses whatever the cache already holds, and it stays quiet when
// an illustration cannot be fetched.
const batchSize = 3;
const requested = new Set();
const queue = [];
let running = false;

function schedule(work) {
  if (typeof requestIdleCallback === "function") {
    requestIdleCallback(work, { timeout: 2000 });
  } else {
    setTimeout(work, 50);
  }
}

async function download(url) {
  try {
    const response = await fetch(url, {
      cache: "force-cache",
      credentials: "same-origin",
      mode: "same-origin",
      priority: "low",
    });
    // Reading the body to the end is what commits the illustration to the cache.
    await response.arrayBuffer();
  } catch {
    // One illustration that will not load is not worth reporting: its card still renders,
    // and the browser fetches the art again when that card appears.
  }
}

async function pump() {
  await Promise.all(queue.splice(0, batchSize).map(download));
  if (queue.length === 0) {
    running = false;
    return;
  }
  schedule(pump);
}

export function warm(urls) {
  if (!Array.isArray(urls)) {
    return;
  }

  for (const url of urls) {
    if (typeof url === "string" && url.length > 0 && !requested.has(url)) {
      requested.add(url);
      queue.push(url);
    }
  }

  if (running || queue.length === 0) {
    return;
  }

  running = true;
  schedule(pump);
}
