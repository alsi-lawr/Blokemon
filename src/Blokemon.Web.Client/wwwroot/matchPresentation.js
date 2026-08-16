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

  deck.style.animation = "none";
  for (const card of cards) {
    card.style.animation = "none";
  }

  const deckRect = deck.getBoundingClientRect();
  const deckX = deckRect.left + deckRect.width / 2;
  const deckY = deckRect.top + deckRect.height / 2;
  for (const card of cards) {
    const cardRect = card.getBoundingClientRect();
    const cardX = cardRect.left + cardRect.width / 2;
    const cardY = cardRect.top + cardRect.height / 2;
    card.style.setProperty("--draw-from-x", `${deckX - cardX}px`);
    card.style.setProperty("--draw-from-y", `${deckY - cardY}px`);
  }

  void table.offsetWidth;
  deck.style.removeProperty("animation");
  for (const card of cards) {
    card.style.removeProperty("animation");
  }

  void table.offsetWidth;
  const deckAnimation = deck
    .getAnimations()
    .find((animation) => animation.animationName === "deck-draw");
  deckAnimation?.pause();
  if (deckAnimation) {
    deckAnimation.currentTime = 0;
  }

  for (const card of cards) {
    const animation = card
      .getAnimations()
      .find((candidate) => candidate.animationName === "draw-card");
    if (!animation) {
      continue;
    }

    animation.pause();
    animation.currentTime = 0;
    const startRect = card.getBoundingClientRect();
    const startX = startRect.left + startRect.width / 2;
    const startY = startRect.top + startRect.height / 2;
    const fromX = Number.parseFloat(card.style.getPropertyValue("--draw-from-x"));
    const fromY = Number.parseFloat(card.style.getPropertyValue("--draw-from-y"));
    card.style.setProperty("--draw-from-x", `${fromX + deckX - startX}px`);
    card.style.setProperty("--draw-from-y", `${fromY + deckY - startY}px`);
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
