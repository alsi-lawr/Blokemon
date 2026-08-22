// Pulls card illustrations into the browser cache during idle time, so a card that is revealed
// later paints with its art already downloaded. Warming is optimistic: it yields to first paint
// and to every interaction, it reuses whatever the cache already holds, and it stays quiet when
// an illustration cannot be fetched.
const batchSize = 3;
const requested = new Set();
const queue = [];
let running = false;

// The page a warmed illustration is most likely to be wanted on is the collection, where a card
// is drawn at the catalogue scale and its illustration is 590 grid pixels across: 124 css pixels
// on a desktop and 106 on a phone, at the same breakpoint the card stylesheet uses. Handing the
// browser that width alongside the candidates lets it apply its own rule - the density of the
// screen included - so that what is warmed is the file the collection then asks for. It cannot
// be left to `sizes="auto"` here: that reads a laid-out box, and nothing being warmed is on the
// page yet.
const collectionTile = "(max-width: 720px) 106px, 124px";

function schedule(work) {
  if (typeof requestIdleCallback === "function") {
    requestIdleCallback(work, { timeout: 2000 });
  } else {
    setTimeout(work, 50);
  }
}

function download(art) {
  // An Image rather than a fetch, because choosing between the delivered widths is something the
  // browser does and a fetch cannot: a fetch names one file, and only the browser knows which
  // one this screen wants.
  return new Promise((resolve) => {
    const image = new Image();
    // One illustration that will not load is not worth reporting: its card still renders, and
    // the browser fetches the art again when that card appears.
    image.onload = resolve;
    image.onerror = resolve;
    image.decoding = "async";
    image.fetchPriority = "low";
    if (art.candidates) {
      image.sizes = collectionTile;
      image.srcset = art.candidates;
    }
    image.src = art.source;
  });
}

async function pump() {
  await Promise.all(queue.splice(0, batchSize).map(download));
  if (queue.length === 0) {
    running = false;
    return;
  }
  schedule(pump);
}

export function warm(illustrations) {
  if (!Array.isArray(illustrations)) {
    return;
  }

  for (const art of illustrations) {
    if (art && typeof art.source === "string" && art.source.length > 0 && !requested.has(art.source)) {
      requested.add(art.source);
      queue.push(art);
    }
  }

  if (running || queue.length === 0) {
    return;
  }

  running = true;
  schedule(pump);
}
