// The two keys the card viewer takes off the browser while it is holding focus, and only those
// two. Space would scroll the page the viewer is covering, and Tab would carry focus out to a
// control behind it that nobody can see. Every other key keeps its ordinary browser or assistive
// technology meaning.
export function guardViewer(element) {
  element?.addEventListener("keydown", (event) => {
    if (event.key === " " || event.key === "Tab") {
      event.preventDefault();
    }
  });
}

// Pointer origin is browser-owned because a held card puts this surface under a pointer that was
// already down. A finger's first lift must therefore be ignored, while a mouse release closes at
// once. Pinned viewers open after their activation has finished, so every release on one began on
// the viewer and dismisses it.
export function armViewer(element, pinned) {
  if (!element) {
    return;
  }

  let pressed = false;
  element.addEventListener(
    "pointerdown",
    () => {
      pressed = true;
    },
    { capture: true },
  );
  const release = (event) => {
    const dismiss = event.pointerType !== "touch" || pinned || pressed;
    pressed = false;
    if (dismiss) {
      // Blazor delegates keyboard events at the document. Raise the viewer's established Escape
      // path after this pointer dispatch has unwound, rather than re-entering it inside pointerup.
      setTimeout(() => {
        if (element.isConnected) {
          element.dispatchEvent(
            new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
          );
        }
      }, 1);
    }
  };
  element.addEventListener("pointerup", release, { capture: true });
  element.addEventListener("pointercancel", release, { capture: true });
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

// Responsive card artwork is laid out at the face's rendered scale so sizes="auto" selects the
// source that face needs. A viewer grows the print without asking the browser to replace that
// already loaded source, so it carries the originating face's artwork scale into the enlarged copy.
export function artworkScale(source, cardId) {
  const sourceScope =
    source?.closest(".attached-card") ?? source?.closest(".card-press") ?? source;
  const card = [...(sourceScope?.querySelectorAll("[data-canonical-id]") ?? [])].find(
    (candidate) => candidate.dataset.canonicalId === cardId,
  );
  const face = card?.closest(".card-face-host");
  const scale = Number.parseFloat(
    getComputedStyle(face).getPropertyValue("--blokemon-card-scale"),
  );
  return Number.isFinite(scale) && scale > 0 ? scale : viewerScale();
}

// A viewer open across a rotation or resize is rescaled in the browser rather than through a
// round trip, so the margin holds without re-rendering the card.
window.addEventListener("resize", () => {
  const viewer = document.querySelector(".card-viewer");
  viewer?.style.setProperty("--viewer-scale", `${viewerScale()}`);
});
