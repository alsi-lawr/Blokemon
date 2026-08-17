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

export function positionDrawCards(table) {
  const deckZone = table?.classList.contains("cue-actor-opponent")
    ? ".opponent-zone"
    : ".player-zone";
  const deck = table?.querySelector(`${deckZone} .deck-card-back`);
  const cards = table?.querySelectorAll(".cue-draw .hand-card.is-cue-target");
  if (!deck || !cards?.length) {
    return;
  }

  // The draw-card animation is declared on the inner visual, so the suppression,
  // the measurements, and the replay below all address the visual rather than its
  // hand-card button.
  const visuals = [];
  for (const card of cards) {
    const visual = card.querySelector(".hand-card-visual");
    if (visual) {
      visuals.push(visual);
    }
  }

  if (!visuals.length) {
    return;
  }

  // Suppress the stylesheet's own run before it can paint a frame of it.
  deck.style.animation = "none";
  for (const visual of visuals) {
    visual.style.animation = "none";
  }

  // Measure at rest so the offsets carry each card back to the deck it came from.
  const deckRect = deck.getBoundingClientRect();
  const deckX = deckRect.left + deckRect.width / 2;
  const deckY = deckRect.top + deckRect.height / 2;
  for (const visual of visuals) {
    const visualRect = visual.getBoundingClientRect();
    const visualX = visualRect.left + visualRect.width / 2;
    const visualY = visualRect.top + visualRect.height / 2;
    visual.style.setProperty("--draw-from-x", `${deckX - visualX}px`);
    visual.style.setProperty("--draw-from-y", `${deckY - visualY}px`);
  }

  void table.offsetWidth;
  deck.style.removeProperty("animation");
  for (const visual of visuals) {
    visual.style.removeProperty("animation");
  }

  void table.offsetWidth;
  const deckAnimation = deck
    .getAnimations()
    .find((animation) => animation.animationName === "deck-draw");
  deckAnimation?.pause();
  if (deckAnimation) {
    deckAnimation.currentTime = 0;
  }

  for (const visual of visuals) {
    const animation = visual
      .getAnimations()
      .find((candidate) => candidate.animationName === "draw-card");
    if (!animation) {
      continue;
    }

    // Held at its first keyframe the card sits where the run will begin. The gap
    // left to the deck is whatever the keyframe's rotation and scale moved, so fold
    // that into the offsets before playing the run once.
    animation.pause();
    animation.currentTime = 0;
    const startRect = visual.getBoundingClientRect();
    const startX = startRect.left + startRect.width / 2;
    const startY = startRect.top + startRect.height / 2;
    const fromX = Number.parseFloat(visual.style.getPropertyValue("--draw-from-x"));
    const fromY = Number.parseFloat(visual.style.getPropertyValue("--draw-from-y"));
    visual.style.setProperty("--draw-from-x", `${fromX + deckX - startX}px`);
    visual.style.setProperty("--draw-from-y", `${fromY + deckY - startY}px`);
    animation.currentTime = 0;
    animation.play();
  }

  deckAnimation?.play();
}

export async function toggleFullscreen(element) {
  if (document.fullscreenElement === element) {
    await document.exitFullscreen();
    return false;
  }

  await element.requestFullscreen();
  return true;
}
