// The drag that plays a card: a press on a card that can be carried becomes, once it has travelled
// further than a tap or a hold would, the card following the pointer to where it goes.
//
// The carrying is done here rather than by the page. A frame of movement that went through the
// renderer would be a frame late on a phone, so the card is moved with a transform the browser
// composites, and the place under the pointer is found by the browser's own hit test. The page is
// told three things - the card was picked up, the card was dropped on a place, the card was let go
// over nothing - and decides everything about what they mean. Nothing about the game is known
// here: which cards can be carried and which places can take them are marks the page puts on the
// elements (data-drag, data-drop, data-card), and this reads them off.
//
// The press is shared with the hold that reads a card. The same travel that begins a drag here is
// the travel the page reads to give up the hold and the tap, so the first move past it is let
// through to the page; every move after it is the browser's alone, and is stopped here before the
// page is asked to do anything with it.

// The same travel the page counts as a scroll or a drag (CardHold), so the two never disagree
// about which press has become one.
const travelTolerance = 12;

// How long a card let go over nothing takes to spring back.
const springMilliseconds = 320;

let page = null;
let armed = false;

// The press that may become a drag, and then the drag.
let press = null;

// A card from the hand left where it was dropped, until the page settles it: the move it made
// starts from there rather than from the hand it was carried out of.
let held = null;

export function armDrags(dotnet) {
  page = dotnet;
  if (armed) {
    return;
  }

  armed = true;
  document.addEventListener("pointerdown", down, { capture: true });
  document.addEventListener("pointermove", move, { capture: true });
  document.addEventListener("pointerup", up, { capture: true });
  document.addEventListener("pointercancel", lost, { capture: true });
}

export function disarmDrags() {
  page = null;
  if (press?.dragging) {
    putBack(press, false);
  }

  press = null;
  settleDrag();
}

// A card held where it was dropped goes back where it came from, if it is still there to go.
export function settleDrag() {
  const card = held;
  held = null;
  if (card) {
    putBack(card, true);
  }
}

function down(event) {
  if (!page || event.button > 0) {
    return;
  }

  const root = event.target?.closest?.("[data-drag]");
  const surface = root?.querySelector(".card-press-surface");
  if (!root || !surface) {
    return;
  }

  settleDrag();
  press = {
    pointerId: event.pointerId,
    root,
    surface,
    id: root.dataset.drag,
    inHand: root.classList.contains("hand-card"),
    startX: event.clientX,
    startY: event.clientY,
    dragging: false,
    x: 0,
    y: 0,
    zoom: 1,
    frame: 0,
    over: null,
  };
}

function move(event) {
  if (!press || event.pointerId !== press.pointerId) {
    return;
  }

  const drag = press;
  drag.x = event.clientX - drag.startX;
  drag.y = event.clientY - drag.startY;
  if (!drag.dragging) {
    if (Math.abs(drag.x) <= travelTolerance && Math.abs(drag.y) <= travelTolerance) {
      return;
    }

    // This move is the one that turns the press into a drag. It goes on to the page, which
    // reads the same travel and gives the hold and the tap up.
    begin(drag);
    return;
  }

  event.stopPropagation();
  if (!drag.frame) {
    drag.frame = requestAnimationFrame(() => follow(drag));
  }
}

function up(event) {
  if (!press || event.pointerId !== press.pointerId) {
    return;
  }

  const drag = press;
  press = null;
  if (!drag.dragging) {
    return;
  }

  if (drag.frame) {
    cancelAnimationFrame(drag.frame);
    drag.frame = 0;
  }

  drag.x = event.clientX - drag.startX;
  drag.y = event.clientY - drag.startY;
  place(drag);
  setOver(drag, targetUnder(drag));
  const target = drag.over;
  setOver(drag, null);
  if (target) {
    drop(drag, target);
  } else {
    release(drag);
  }
}

function lost(event) {
  if (!press || event.pointerId !== press.pointerId) {
    return;
  }

  const drag = press;
  press = null;
  if (drag.dragging) {
    setOver(drag, null);
    release(drag);
  }
}

function begin(drag) {
  drag.dragging = true;
  drag.zoom = zoomOf(drag.surface);
  drag.surface.dataset.dragging = "";
  drag.surface.style.transition = "none";
  place(drag);
  page
    .invokeMethodAsync("CardPickedUp", drag.id)
    .then((picked) => {
      // A card the page would not let go of stays where it is; the press goes on as a press
      // that has already been given up, and ends with nothing.
      if (!picked && press === drag) {
        press = null;
        putBack(drag, true);
      }
    })
    .catch(() => {
      if (press === drag) {
        press = null;
        putBack(drag, true);
      }
    });
}

function follow(drag) {
  drag.frame = 0;
  if (!drag.dragging || press !== drag) {
    return;
  }

  place(drag);
  setOver(drag, targetUnder(drag));
}

// The card is moved in its own coordinates, which on a phone are the table's: the table is
// scaled to fit the screen, and a distance measured across the screen has to be handed to it in
// its own pixels or the card would fall short of the pointer by exactly the fit.
function place(drag) {
  drag.surface.style.translate = `${drag.x / drag.zoom}px ${drag.y / drag.zoom}px`;
}

// The place under the pointer, if the topmost thing there is one. Whatever is drawn over a place
// covers it here as it does to a tap, so a card cannot be dropped through a sheet onto something
// behind it; the card being carried is looked past, because it is always under the pointer.
function targetUnder(drag) {
  const x = drag.startX + drag.x;
  const y = drag.startY + drag.y;
  for (const element of document.elementsFromPoint(x, y)) {
    if (drag.root.contains(element)) {
      continue;
    }

    const target = element.closest("[data-drop]");
    return target && !drag.root.contains(target) ? target : null;
  }

  return null;
}

function setOver(drag, target) {
  if (drag.over === target) {
    return;
  }

  if (drag.over) {
    delete drag.over.dataset.dropOver;
  }

  drag.over = target;
  if (target) {
    target.dataset.dropOver = "";
  }
}

function drop(drag, target) {
  const kind = target.dataset.drop;
  const card = target.dataset.card ?? null;
  // A card carried out of the hand stays where it was dropped until the page has taken it or
  // sent it back; a card carried across the table goes straight back to where it stands, because
  // the move it made is played from there.
  if (drag.inHand) {
    held = drag;
  } else {
    putBack(drag, true);
  }

  page.invokeMethodAsync("CardDropped", drag.id, kind, card).catch(() => {
    if (held === drag) {
      settleDrag();
    }
  });
}

function release(drag) {
  putBack(drag, true);
  page.invokeMethodAsync("CardReleased", drag.id).catch(() => {});
}

// The card goes back to where it stands: sprung, so it is seen to go, or at once when the page
// itself is going.
function putBack(drag, spring) {
  const { surface } = drag;
  delete surface.dataset.dragging;
  if (!spring || !surface.isConnected) {
    surface.style.transition = "";
    surface.style.translate = "";
    return;
  }

  surface.dataset.springing = "";
  surface.style.transition = `translate ${springMilliseconds}ms cubic-bezier(0.2, 0.9, 0.3, 1.2)`;
  surface.style.translate = "";
  setTimeout(() => {
    surface.style.transition = "";
    delete surface.dataset.springing;
  }, springMilliseconds + 40);
}

function zoomOf(element) {
  const zoom = element.currentCSSZoom;
  if (typeof zoom === "number" && zoom > 0) {
    return zoom;
  }

  const canvas = element.closest(".battle-canvas");
  const declared = canvas ? Number.parseFloat(getComputedStyle(canvas).zoom) : 1;
  return Number.isFinite(declared) && declared > 0 ? declared : 1;
}
