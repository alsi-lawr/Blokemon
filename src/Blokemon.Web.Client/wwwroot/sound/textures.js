// The two sounds that are computed sample by sample, because no arrangement of scheduled voices
// gets near either of them.
//
// A riffle is a "controlled waterfall" of overlapping micro-transients, and its documented failure
// mode is exactly a row of crisp ticks in sequence instead of a parallel texture. A tear is on the
// order of a thousand separate fibre ruptures a second, and - the part that matters most - it has
// NO pitch glide whatsoever. A rising sweep is a slide-whistle convention borrowed from cartoons,
// and it is the single thing that makes a synthesised tear sound fake.

import { AC, master, verbSend, noiseSrc, out } from "./audioContext.js";
import { bezier, unease } from "./easing.js";

/* A riffle is NOT a row of clicks. Reference material for card foley describes the cascade as a
   "controlled waterfall" — fast, layered, full of overlapping micro-transients — and names the
   failure mode of synthesised riffles exactly: crisp individual ticks stacked in sequence rather
   than a parallel texture. That is precisely what the first version here did, and it is why it
   sounded like sticks. Real cascades also run at something like a hundred cards a second, so the
   twelve the animation draws are representative, not the whole event.

   At that density scheduling nodes is hopeless, so the cascade is computed into a stereo buffer:
   a continuous bed of card releases that thickens as the packets zip together, with the twelve
   animated arrivals raised above it as small CLUSTERS rather than single events. Each grain gets
   its own colour from a one-pole filter with a random coefficient, because a real riffle varies
   in timbre card to card and an even texture is the other thing that reads as synthetic.        */
export function cardCascade(dur, o) {
  const sr = AC.sampleRate, n = Math.ceil(sr * dur);
  const buf = AC.createBuffer(2, n, sr);
  const L = buf.getChannelData(0), R = buf.getChannelData(1);
  const grain = (at, amp, pan, bright) => {
    const start = Math.floor(at * sr);
    if (start < 0 || start >= n) return;
    const len = Math.floor(sr * (0.006 + Math.random() * 0.030));  // one card: 6-36 ms
    const att = Math.max(2, Math.floor(sr * 0.0018));
    // kept dark on purpose: bright grains read as plastic rather than as card
    const a = 0.10 + bright * 0.42;
    const gl = Math.cos((pan + 1) * Math.PI / 4), gr = Math.sin((pan + 1) * Math.PI / 4);
    let y = 0;
    for (let i = 0; i < len && start + i < n; i++) {
      const x = Math.random() * 2 - 1;
      y += a * (x - y);
      const s = (x - y) * amp * Math.min(1, i / att) * Math.exp(-5 * i / len);
      L[start + i] += s * gl; R[start + i] += s * gr;
    }
  };
  const bed = Math.round((o.density || 260) * dur);
  for (let i = 0; i < bed; i++) {
    const u = (i + Math.random()) / bed;
    grain(dur * u, Math.pow(Math.random(), 1.6) * 0.55 * (0.45 + u * 1.0),
          (Math.random() * 2 - 1) * 0.78, Math.random());
  }
  for (let k = 0; k < (o.arrivals || 0); k++) {
    const pan = (k % 2 === 0 ? -1 : 1) * (0.4 + Math.random() * 0.14);
    const cluster = 2 + Math.floor(Math.random() * 3);
    for (let j = 0; j < cluster; j++) {
      grain(k * o.stagger + Math.random() * 0.013,
            (j === 0 ? 0.9 : 0.42) * (0.7 + Math.random() * 0.5), pan, 0.45 + Math.random() * 0.5);
    }
  }
  let peak = 0;
  for (let i = 0; i < n; i++) peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
  if (peak > 0) for (let i = 0; i < n; i++) { L[i] /= peak; R[i] /= peak; }
  return buf;
}

const EASE_TEAR = bezier(0.3, 0, 0.7, 1);

const TEAR_SEGMENTS = [[0.000, 0.228, 0.00, 0.40], [0.228, 0.444, 0.40, 0.80], [0.444, 0.600, 0.80, 1.00]];

export function crackleTear(dur, density, gloss) {
  const sr = AC.sampleRate, n = Math.ceil(sr * dur);
  const buf = AC.createBuffer(1, n, sr);
  const d = buf.getChannelData(0);
  const place = (at, amp) => {
    const start = Math.floor(at * sr);
    if (start < 0 || start >= n) return;
    // one fibre letting go: sharp onset, short ragged decay
    const len = Math.floor(sr * (0.0006 + Math.random() * (gloss ? 0.0026 : 0.0042)));
    for (let i = 0; i < len && start + i < n; i++) {
      d[start + i] += (Math.random() * 2 - 1) * amp * Math.exp(-4.5 * i / len);
    }
  };
  for (const [t0, t1, x0, x1] of TEAR_SEGMENTS) {
    const count = Math.max(4, Math.round(density * (t1 - t0)));
    for (let k = 0; k < count; k++) {
      const frac = (k + Math.random()) / count;
      const u = unease(EASE_TEAR, frac);
      // amplitudes are heavily skewed small, so the texture is grainy rather than even
      place(t0 + (t1 - t0) * u, Math.pow(Math.random(), 1.7) * (0.45 + Math.random() * 0.75));
    }
  }
  // normalise, then let the caller set the level
  let peak = 0;
  for (let i = 0; i < n; i++) peak = Math.max(peak, Math.abs(d[i]));
  if (peak > 0) for (let i = 0; i < n; i++) d[i] /= peak;
  return buf;
}

export function rip(t, stock) {
  const gloss = stock !== "kraft";
  const dur = 0.6;
  const src = AC.createBufferSource();
  src.buffer = crackleTear(dur, gloss ? 2000 : 1250, gloss);

  // FIXED voicing per material. Foil is thin and sharp with a scooped middle; kraft is grainy
  // and sits in the 100 Hz - 1 kHz band where paper crumpling actually lives.
  const hp = AC.createBiquadFilter(); hp.type = "highpass";
  hp.frequency.value = gloss ? 900 : 180;
  const scoop = AC.createBiquadFilter(); scoop.type = "peaking";
  scoop.frequency.value = gloss ? 560 : 700; scoop.Q.value = 0.9;
  scoop.gain.value = gloss ? -8 : 4;
  const top = AC.createBiquadFilter(); top.type = "highshelf";
  top.frequency.value = 3800; top.gain.value = gloss ? 3 : -10;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass";
  lp.frequency.value = gloss ? 12000 : 5200;

  // The overall swell of the tear, jagged rather than smooth: three surges, one per segment.
  const g = AC.createGain();
  const level = gloss ? 0.5 : 0.58;
  g.gain.setValueAtTime(0.0001, t);
  for (const [t0, t1] of TEAR_SEGMENTS) {
    g.gain.linearRampToValueAtTime(level * (0.75 + Math.random() * 0.25), t + t0 + (t1 - t0) * 0.35);
    g.gain.linearRampToValueAtTime(level * (0.55 + Math.random() * 0.3), t + t1);
  }
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur + 0.03);

  // The one thing that legitimately moves: where it is, because the tear crosses the wrapper.
  const p = AC.createStereoPanner();
  p.pan.setValueAtTime(-0.55, t);
  p.pan.linearRampToValueAtTime(0.55, t + dur);

  src.connect(hp); hp.connect(scoop); scoop.connect(top); top.connect(lp); lp.connect(g);
  g.connect(p); p.connect(master);
  const sg = AC.createGain(); sg.gain.value = 0.2; p.connect(sg); sg.connect(verbSend);
  src.start(t); src.stop(t + dur + 0.1);
}
