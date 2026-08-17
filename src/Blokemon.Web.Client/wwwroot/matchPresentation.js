export function prefersReducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function focusElement(element) {
  element?.focus();
}

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
