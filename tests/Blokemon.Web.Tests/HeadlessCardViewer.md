# Headless card-viewer evidence

Run the repository-contained browser check from the repository root:

```sh
python3 tests/Blokemon.Web.Tests/headless_card_viewer.py
```

The check publishes the checked-out standalone game and its focused reveal-cue component host into a disposable
directory, serves both on loopback, and drives an installed Chrome or Chromium through the browser's DevTools
protocol. It uses only Python's standard library and the repository's existing .NET dependencies.

The executable evidence covers:

- the deterministic direct `CardFace` consumer inventory;
- actual reading-control activation and non-nested sibling controls;
- unchanged card markup and uniformly scaled internal geometry at 1440x900 and 390x844, within the 20px viewer
  margin;
- focus transfer, guarded Tab, Escape, Enter, Space, mouse, touch, and hold lifecycle with exact focus return;
- pack identity and reader gating before face-up, with reading isolated from reveal advancement;
- revealed cue reading isolated from acknowledgement;
- action isolation in Home, Decks, Packs, and Match representatives; and
- reduced-motion viewer behavior in Collection, Home, Packs, Decks, and Match.

Published files, browser profiles, and logs are transient and are removed when the check exits.
