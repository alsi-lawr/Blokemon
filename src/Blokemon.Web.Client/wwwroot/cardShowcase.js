const dialogStates = new WeakMap();

function isBackdropPointerEvent(dialog, event) {
  if (event.target !== dialog) {
    return false;
  }

  const bounds = dialog.getBoundingClientRect();
  return (
    event.clientX < bounds.left ||
    event.clientX > bounds.right ||
    event.clientY < bounds.top ||
    event.clientY > bounds.bottom
  );
}

function ensureDialogState(dialog) {
  const existing = dialogStates.get(dialog);
  if (existing) {
    return existing;
  }

  const state = {
    backdropPointerDown: false,
    returnFocusTo: null,
    onPointerDown: null,
    onClick: null,
    onClose: null,
  };

  state.onPointerDown = (event) => {
    state.backdropPointerDown = isBackdropPointerEvent(dialog, event);
  };
  state.onClick = (event) => {
    const dismiss =
      state.backdropPointerDown && isBackdropPointerEvent(dialog, event);
    state.backdropPointerDown = false;
    if (dismiss && dialog.open) {
      dialog.close("backdrop");
    }
  };
  state.onClose = () => {
    const returnFocusTo = state.returnFocusTo;
    state.returnFocusTo = null;
    requestAnimationFrame(() => {
      if (
        !dialog.open &&
        returnFocusTo instanceof HTMLElement &&
        returnFocusTo.isConnected
      ) {
        returnFocusTo.focus({ preventScroll: true });
      }
    });
  };

  dialog.addEventListener("pointerdown", state.onPointerDown);
  dialog.addEventListener("click", state.onClick);
  dialog.addEventListener("close", state.onClose);
  dialogStates.set(dialog, state);
  return state;
}

export function show(dialog, returnFocusTo) {
  // A reference to an element that has left the document resolves to null rather than throwing,
  // which is what happens when the page carrying this surface is navigated away from before the
  // request to open it arrives. There is nothing to open and nothing has gone wrong.
  if (!dialog) {
    return;
  }

  const state = ensureDialogState(dialog);
  state.returnFocusTo =
    returnFocusTo instanceof HTMLElement ? returnFocusTo : document.activeElement;

  if (!dialog.open) {
    dialog.showModal();
  }
}

export function dispose(dialog) {
  const state = dialogStates.get(dialog);
  if (!state) {
    return;
  }

  dialog.removeEventListener("pointerdown", state.onPointerDown);
  dialog.removeEventListener("click", state.onClick);
  dialog.removeEventListener("close", state.onClose);
  dialogStates.delete(dialog);
}
