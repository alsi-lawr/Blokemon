// Solving the stylesheet's own timing functions, so a sound can be placed at the same eased
// waypoint the animation reaches rather than at a time somebody estimated.
//
// bezier() evaluates a CSS cubic-bezier at a progress; unease() runs it backwards, answering "at
// what fraction of this run is the card N% of the way there". The second is the one that matters:
// journeys here are timed linearly and eased by where their keyframes sit, so the sound has to
// solve the curve to land with the picture.

/* ---- CSS cubic-bezier, and its inverse ---- */
export function bezier(x1, y1, x2, y2) {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
  const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
  const sx = t => ((ax * t + bx) * t + cx) * t;
  const sy = t => ((ay * t + by) * t + cy) * t;
  const dx = t => (3 * ax * t + 2 * bx) * t + cx;
  return function (x) {
    let t = x;
    for (let i = 0; i < 8; i++) {
      const e = sx(t) - x; if (Math.abs(e) < 1e-6) break;
      const d = dx(t); if (Math.abs(d) < 1e-6) break;
      t -= e / d;
    }
    return sy(Math.min(1, Math.max(0, t)));
  };
}

// Given an eased output value, the time fraction that produces it.
export function unease(f, y) {
  let lo = 0, hi = 1;
  for (let i = 0; i < 34; i++) { const m = (lo + hi) / 2; if (f(m) < y) lo = m; else hi = m; }
  return (lo + hi) / 2;
}
