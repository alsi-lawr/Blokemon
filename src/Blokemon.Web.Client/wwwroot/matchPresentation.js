export function prefersReducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function focusElement(element) {
  element?.focus();
}

let returnFocusTo = null;

// The battle surfaces move focus into themselves, so the element that was focused when a
// surface opened is remembered here and refocused when it closes.
export function focusSurface(element) {
  if (!element) {
    return;
  }

  const active = document.activeElement;
  if (active && active !== document.body && !element.contains(active)) {
    returnFocusTo = active;
  }
  element.focus();
}

export function restoreFocus() {
  const target = returnFocusTo;
  returnFocusTo = null;
  if (target?.isConnected) {
    target.focus();
  }
}

// The printed face measures 750 x 1050, so the viewer scale is whichever of the two viewport
// margins binds first. The card keeps its aspect ratio and clears the margin on both axes.
const cardWidth = 750;
const cardHeight = 1050;
const viewerMargin = 20;

function viewerScaleFor(width, height) {
  const horizontal = (width - viewerMargin * 2) / cardWidth;
  const vertical = (height - viewerMargin * 2) / cardHeight;
  return Math.max(0.05, Math.min(horizontal, vertical));
}

export function viewerScale() {
  return viewerScaleFor(window.innerWidth, window.innerHeight);
}

// A viewer open across a rotation or resize is rescaled in the browser rather than through a
// round trip, so the margin holds without re-rendering the card.
window.addEventListener("resize", () => {
  const viewer = document.querySelector(".card-viewer");
  viewer?.style.setProperty("--viewer-scale", `${viewerScale()}`);
});

// How far apart two elements are on this screen at this size, which is the one thing about a
// card's journey that cannot be written in a stylesheet. Everything measured here is handed back
// as a custom property on an element the renderer owns; nothing is added to the page, and nothing
// is remembered between calls.
function centre(element) {
  const rect = element.getBoundingClientRect();
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2, rect };
}

export function positionDrawCards(table) {
  const deckZone = table?.classList.contains("cue-actor-opponent")
    ? ".opponent-zone"
    : ".player-zone";
  const deck = table?.querySelector(`${deckZone} .deck-card-back`);
  if (!deck) {
    return;
  }

  // The card being dealt is the player's own held card, whose motion is declared on the visual
  // inside its press surface, or - when the opponent is the one drawing - the newest back in
  // their strip. Either way it is that element the suppression, the measurements and the replay
  // below all address. How high each one arcs is a share of how far it has to go, and the two
  // journeys carry different shares of it.
  const visuals = [];
  for (const card of table.querySelectorAll(".hand-card.is-cue-target")) {
    const visual = card.querySelector(".hand-card-visual");
    if (visual) {
      visuals.push({ element: visual, arc: 0.25 });
    }
  }
  for (const back of table.querySelectorAll(`${deckZone} .opponent-hand .is-drawn`)) {
    visuals.push({ element: back, arc: 0.2 });
  }

  if (!visuals.length) {
    return;
  }

  // Suppress the stylesheet's own run before it can paint a frame of it.
  deck.style.animation = "none";
  for (const { element } of visuals) {
    element.style.animation = "none";
  }

  // Measure at rest so the offsets carry each card back to the deck it came from, and so that
  // the size it leaves the Deck at is the Deck's own size rather than a number written down
  // somewhere: a card is dealt from a stack it is the same size as, and ends up whatever size
  // the place receiving it draws cards at.
  const { x: deckX, y: deckY } = centre(deck);
  for (const { element, arc } of visuals) {
    const at = centre(element);
    const dx = deckX - at.x;
    const dy = deckY - at.y;
    element.style.setProperty("--draw-from-x", `${dx}px`);
    element.style.setProperty("--draw-from-y", `${dy}px`);
    element.style.setProperty("--draw-lift", `${arc * Math.hypot(dx, dy)}px`);
    element.style.setProperty(
      "--draw-from-scale",
      `${Math.max(0.05, deck.offsetWidth / Math.max(1, element.offsetWidth))}`,
    );
  }

  void table.offsetWidth;
  deck.style.removeProperty("animation");
  for (const { element } of visuals) {
    element.style.removeProperty("animation");
  }

  void table.offsetWidth;
  const deckAnimation = deck
    .getAnimations()
    .find((animation) => animation.animationName === "deck-press");
  deckAnimation?.pause();
  if (deckAnimation) {
    deckAnimation.currentTime = 0;
  }

  for (const { element } of visuals) {
    const animation = element
      .getAnimations()
      .find((candidate) => candidate.animationName.startsWith("draw-arc"));
    if (!animation) {
      continue;
    }

    // Held at its first keyframe the card sits where the run will begin. The gap
    // left to the deck is whatever the keyframe's rotation and scale moved, so fold
    // that into the offsets before playing the run once.
    animation.pause();
    animation.currentTime = 0;
    const start = centre(element);
    const fromX = Number.parseFloat(element.style.getPropertyValue("--draw-from-x"));
    const fromY = Number.parseFloat(element.style.getPropertyValue("--draw-from-y"));
    element.style.setProperty("--draw-from-x", `${fromX + deckX - start.x}px`);
    element.style.setProperty("--draw-from-y", `${fromY + deckY - start.y}px`);
    animation.currentTime = 0;
    animation.play();
  }

  deckAnimation?.play();
}

// Where the card the page is holding on its travelling layer has come from, which is wherever it
// was standing a moment ago: in a hand, or on the table, or - for a card played from the hand
// nobody can see - the strip that hand is drawn as.
function playOrigin(table) {
  return (
    table.querySelector(".hand-card.is-cue-source .hand-card-visual") ??
    table.querySelector(".battle-card-shell.is-cue-source") ??
    table.querySelector(
      table.classList.contains("cue-actor-opponent")
        ? ".opponent-zone .opponent-hand"
        : ".hand-zone",
    )
  );
}

// The place a card ends up standing in, at the size it will stand there. The Active card leans
// against its own end of its slot rather than sitting in the middle of it, so the landing is
// taken from the edge the page marked; the size comes from a card already on the table, which is
// the only honest measure of how big this one is about to be.
function playLanding(table, landing) {
  const rect = landing.getBoundingClientRect();
  const compact = landing.classList.contains("is-landing-centre");
  const sample =
    table.querySelector(
      compact ? ".battle-card-shell.is-compact" : ".battle-card-shell:not(.is-compact)",
    ) ?? table.querySelector(".battle-card-shell");
  const size = sample?.getBoundingClientRect();
  const width = size?.width || rect.width;
  const height = size?.height || rect.height;
  const y = landing.classList.contains("is-landing-top")
    ? rect.top + height / 2
    : landing.classList.contains("is-landing-bottom")
      ? rect.bottom - height / 2
      : rect.top + rect.height / 2;
  return { x: rect.left + rect.width / 2, y, width };
}

export function positionPlayCard(table) {
  const traveller = table?.querySelector(".card-play-overlay.is-travelling .card-travel");
  const landing = table?.querySelector(".is-cue-landing");
  const origin = table ? playOrigin(table) : null;
  if (!traveller || !landing || !origin) {
    return;
  }

  // Suppress the stylesheet's own run before it can paint a frame of it, so the card is measured
  // where it rests rather than part way through its journey.
  traveller.style.animation = "none";
  void table.offsetWidth;

  const rest = centre(traveller);
  const from = centre(origin);
  const to = playLanding(table, landing);
  traveller.style.setProperty("--play-from-x", `${from.x - rest.x}px`);
  traveller.style.setProperty("--play-from-y", `${from.y - rest.y}px`);
  traveller.style.setProperty(
    "--play-from-scale",
    `${Math.max(0.05, from.rect.width / rest.rect.width)}`,
  );
  traveller.style.setProperty("--play-to-x", `${to.x - rest.x}px`);
  traveller.style.setProperty("--play-to-y", `${to.y - rest.y}px`);
  traveller.style.setProperty("--play-to-scale", `${Math.max(0.05, to.width / rest.rect.width)}`);

  // How high the card is carried over the table is a share of how far it is carried, and how far
  // it dips as it is picked up is a share of how big it is where it is picked up from: both are
  // the journey's own proportions rather than a distance in pixels that only suits one screen.
  traveller.style.setProperty(
    "--play-lift",
    `${0.27 * Math.hypot(to.x - from.x, to.y - from.y)}px`,
  );
  traveller.style.setProperty("--play-dip", `${0.06 * from.rect.height}px`);

  traveller.style.removeProperty("animation");
  void table.offsetWidth;
}

export async function toggleFullscreen(element) {
  if (document.fullscreenElement === element) {
    await document.exitFullscreen();
    return false;
  }

  await element.requestFullscreen();
  return true;
}
