// Striking a body, and crumpling a sheet: the two engines the material tables feed.
//
// A struck body is an IMPULSE heard through modes that each carry their own decay. Getting a long
// decay out of a filter would need a Q so high that noise through it becomes a sine oscillator, so
// the modes are computed into a buffer instead - which also allows the two things that keep it from
// sounding like an additive synth: enough modes to beat against each other, and an amplitude that
// wanders as each one dies.
//
// Crumpling is not an impact at all. It is hundreds of separate fibre releases a second whose
// energies follow a power law, and below about forty a second the ear picks them out individually
// as clicks instead of hearing a texture.

import { AC, master, verbSend, noiseSrc, out } from "./audioContext.js";
import { MATERIAL, STRUCK, CRINKLE } from "./materials.js";

export function modal(t, name, o) {
  o = o || {};
  // The four bodies that have real modal specs now go through strike(). Foil and brass stay here:
  // foil genuinely is a near-instant click as a CONTACT (its crinkle is a texture, built
  // elsewhere), and brass is the one thing on the page that is supposed to ring like a tone.
  // Only wood and the box are struck bodies now; see the note where card and mat used to be.
  if (typeof STRUCK !== "undefined" && STRUCK[name]) return strike(t, name, o);
  const m = MATERIAL[name] || MATERIAL.card;
  const pitch = o.pitch || 1, ds = o.decay == null ? 1 : o.decay;
  const gain = o.gain == null ? 0.4 : o.gain;
  const pan = o.pan || 0, send = o.send == null ? 0.18 : o.send, bus = o.bus;

  if (m.tonal) {
    for (const [r, a, dec0] of m.p) {
      const osc = AC.createOscillator(); osc.type = "sine";
      osc.frequency.value = m.f * pitch * r * (1 + (Math.random() - 0.5) * 0.012);
      const g = AC.createGain();
      const dec = Math.max(0.01, dec0 * ds);
      g.gain.setValueAtTime(0.0001, t);
      g.gain.linearRampToValueAtTime(Math.max(0.0002, gain * a), t + 0.0018);
      g.gain.exponentialRampToValueAtTime(0.0001, t + dec);
      osc.connect(g); out(g, pan, send, bus);
      osc.start(t); osc.stop(t + dec + 0.03);
    }
    const e = m.exc, s = noiseSrc();
    const bp = AC.createBiquadFilter(); bp.type = "bandpass";
    bp.frequency.value = e.f * pitch; bp.Q.value = e.q;
    const g = AC.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(gain * e.g, t + 0.0008);
    g.gain.exponentialRampToValueAtTime(0.0001, t + e.dur);
    s.connect(bp); bp.connect(g); out(g, pan, send * 0.6, bus);
    s.start(t); s.stop(t + e.dur + 0.02);
    return;
  }

  // One short burst of noise — the contact itself — heard through the body.
  const dur = Math.max(0.003, m.exc.dur * ds);
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = m.exc.lo * pitch;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = Math.min(18000, m.exc.hi * pitch);
  const env = AC.createGain();
  env.gain.setValueAtTime(0.0001, t);
  env.gain.linearRampToValueAtTime(1, t + 0.0007);
  env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  s.connect(hp); hp.connect(lp); lp.connect(env);

  const mix = AC.createGain(); mix.gain.value = gain;
  for (const [f, q, a] of m.modes) {
    const bp = AC.createBiquadFilter(); bp.type = "bandpass";
    bp.frequency.value = f * pitch * (1 + (Math.random() - 0.5) * 0.02);
    bp.Q.value = q;
    const g = AC.createGain(); g.gain.value = a * 1.5;
    env.connect(bp); bp.connect(g); g.connect(mix);
  }
  // some of the raw strike, because the contact is the first thing you hear
  const dry = AC.createGain(); dry.gain.value = m.dry * (o.strike == null ? 1 : o.strike);
  env.connect(dry); dry.connect(mix);
  out(mix, pan, send, bus);
  s.start(t); s.stop(t + dur + 0.35);
}

/* ============================================================================================
   STRUCK BODIES, and why the engine above was not enough on its own.

   modal() envelopes the noise going INTO a bank of bandpass filters over 6-36 ms. A bandpass at
   Q 11 and 1.76 kHz rings for roughly Q/(pi*f) = 2 ms, so nothing in it ever decayed: every
   material was a short noise burst and therefore a CLICK, and the only thing separating a card
   from a table was how bright the burst was. A real wooden table rings for 150-300 ms.

   Getting a decay that long out of a biquad would need Q near 130, and a high-Q bandpass fed with
   SUSTAINED noise is a sine oscillator — the failure this page already made once in the other
   direction. The resolution is the physical one: the contact is an IMPULSE, and the body is a set
   of modes each carrying its own decay time. Three things stop that being an additive synth:
   there are enough modes to beat against each other, every mode's amplitude wanders as it decays,
   and the contact noise and the grain riding the decay are real parts of the mix, not a garnish.
   ============================================================================================ */
export function modalBuffer(spec, o) {
  const sr = AC.sampleRate;
  const ds = o.decay == null ? 1 : o.decay;
  const pitch = o.pitch || 1;
  let longest = 0;
  for (const m of spec.modes) longest = Math.max(longest, m[2]);
  if (spec.grain) longest = Math.max(longest, spec.grain.tau);
  const dur = longest * ds * 6 + 0.02;
  const n = Math.ceil(sr * dur);
  const buf = AC.createBuffer(1, n, sr);
  const d = buf.getChannelData(0);

  for (const [f0, amp, tau0, rough] of spec.modes) {
    const f = f0 * pitch * (1 + (Math.random() - 0.5) * (spec.detune || 0.02));
    if (f <= 0 || f >= sr / 2) continue;
    const tau = Math.max(0.0015, tau0 * ds);
    const w = 2 * Math.PI * f / sr;
    const ph = Math.random() * Math.PI * 2;
    const dec = Math.exp(-1 / (tau * sr));
    const len = Math.min(n, Math.ceil(tau * 6 * sr));
    const rr = rough == null ? (spec.rough == null ? 0.22 : spec.rough) : rough;
    let rw = 0, env = amp;
    for (let i = 0; i < len; i++) {
      rw = rw * 0.994 + (Math.random() - 0.5) * 0.16;      // slow amplitude wander
      d[i] += env * (1 + rr * rw) * Math.sin(w * i + ph);
      env *= dec;
    }
  }

  // The grain: noise riding the decay. This is what stops a struck body reading as a tone — a
  // real object's ring carries the roughness of the material it is made of.
  if (spec.grain) {
    const gtau = Math.max(0.002, spec.grain.tau * ds);
    const len = Math.min(n, Math.ceil(gtau * 6 * sr));
    const dec = Math.exp(-1 / (gtau * sr));
    const a = spec.grain.colour == null ? 0.5 : spec.grain.colour;   // 0 dark .. 1 bright
    let env = spec.grain.gain, y = 0;
    for (let i = 0; i < len; i++) {
      const x = Math.random() * 2 - 1;
      y += a * (x - y);
      d[i] += (a < 0.5 ? y : x - y) * env;
      env *= dec;
    }
  }

  let peak = 0;
  for (let i = 0; i < n; i++) peak = Math.max(peak, Math.abs(d[i]));
  if (peak > 0) for (let i = 0; i < n; i++) d[i] /= peak;
  return buf;
}

export function strike(t, name, o) {
  o = o || {};
  const spec = STRUCK[name];
  const gain = o.gain == null ? 0.5 : o.gain, pan = o.pan || 0;
  const send = o.send == null ? 0.2 : o.send;
  const src = AC.createBufferSource();
  src.buffer = modalBuffer(spec, o);
  const g = AC.createGain(); g.gain.value = gain;
  let head = src;
  if (spec.eq) {
    for (const [type, f, q, db] of spec.eq) {
      const b = AC.createBiquadFilter(); b.type = type;
      b.frequency.value = f; b.Q.value = q;
      if (db != null) b.gain.value = db;
      head.connect(b); head = b;
    }
  }
  head.connect(g); out(g, pan, send, o.bus);
  src.start(t); src.stop(t + src.buffer.duration + 0.02);

  // the contact itself, on top of the body
  if (spec.contact) {
    const c = spec.contact;
    const s = noiseSrc();
    const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = c.lo;
    const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = c.hi;
    const cg = AC.createGain();
    cg.gain.setValueAtTime(0.0001, t);
    cg.gain.linearRampToValueAtTime(gain * c.gain * (o.strike == null ? 1 : o.strike), t + 0.0012);
    cg.gain.exponentialRampToValueAtTime(0.0001, t + c.dur);
    s.connect(hp); hp.connect(lp); lp.connect(cg); out(cg, pan, send * 0.6, o.bus);
    s.start(t); s.stop(t + c.dur + 0.02);
  }
}

/* ============================================================================================
   CRINKLE. A card being flexed and a wrapper being scrunched are not impacts at all — they are
   hundreds of separate buckling events spread over a fifth of a second or more, which is exactly
   why building them as one short burst produced a click no matter how the burst was filtered.
   Crumpling is a well-studied crackling process: the events are BURSTY rather than evenly spaced,
   and their energies follow a heavy-tailed distribution, so most are tiny and a few are large.
   Both of those matter — evenly spaced events of similar size read as a rattle, not a crinkle.
   ============================================================================================ */
export function crinkleBuffer(dur, spec) {
  const sr = AC.sampleRate, n = Math.ceil(sr * dur);
  const buf = AC.createBuffer(2, n, sr);
  const L = buf.getChannelData(0), R = buf.getChannelData(1);
  const place = (at, amp, pan, bright) => {
    const start = Math.floor(at * sr);
    if (start < 0 || start >= n) return;
    const len = Math.floor(sr * (spec.evMin + Math.random() * (spec.evMax - spec.evMin)));
    const a = 0.15 + bright * 0.8;
    const gl = Math.cos((pan + 1) * Math.PI / 4), gr = Math.sin((pan + 1) * Math.PI / 4);
    let y = 0;
    for (let i = 0; i < len && start + i < n; i++) {
      const x = Math.random() * 2 - 1;
      y += a * (x - y);
      const s = (x - y) * amp * Math.exp(-4.2 * i / len);
      L[start + i] += s * gl; R[start + i] += s * gr;
    }
  };
  /* Event energies follow a power law with an exponent measured at 1.3-1.6 for crumpling sheets,
     spanning a factor of about a million from the smallest release to the largest. That spread is
     most of what makes a crinkle sound alive: a great many events too small to pick out, and the
     occasional one that pops. Uniform-ish energies read as a rattle instead. */
  const alpha = spec.alpha || 1.45;
  const pw = -1 / (alpha - 1);
  const energy = () => Math.min(50, Math.pow(Math.max(1e-4, 1 - Math.random()), pw));

  /* Timing is clustered, not even: crumpling releases arrive in cascades with quiet between,
     following a log-Poisson waiting time, so gaps are drawn log-uniformly rather than linearly. */
  const total = Math.round(spec.density * dur);
  const gapLo = Math.log(0.004), gapHi = Math.log(spec.gapMax || 0.09);
  let placed = 0, at = Math.random() * 0.01;
  while (placed < total && at < dur) {
    const size = 1 + Math.floor(Math.random() * spec.burst);
    const spread = 0.002 + Math.random() * 0.03;
    const pan = (Math.random() * 2 - 1) * 0.6;
    for (let i = 0; i < size && placed < total; i++, placed++) {
      place(at + Math.random() * spread, Math.sqrt(energy()),
            pan + (Math.random() - 0.5) * 0.25, Math.random());
    }
    at += spread + Math.exp(gapLo + Math.random() * (gapHi - gapLo));
  }
  let peak = 0;
  for (let i = 0; i < n; i++) peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
  if (peak > 0) for (let i = 0; i < n; i++) { L[i] /= peak; R[i] /= peak; }
  return buf;
}

export function crinkle(t, name, o) {
  o = o || {};
  const spec = CRINKLE[name];
  const dur = o.dur || spec.dur;
  const gain = o.gain == null ? 0.4 : o.gain;
  const src = AC.createBufferSource();
  src.buffer = crinkleBuffer(dur, spec);
  let head = src;
  for (const [type, f, q, db] of spec.eq) {
    const b = AC.createBiquadFilter(); b.type = type;
    b.frequency.value = f; b.Q.value = q;
    if (db != null) b.gain.value = db;
    head.connect(b); head = b;
  }
  const g = AC.createGain();
  // an uneven swell, because a hand does not scrunch at a constant rate
  g.gain.setValueAtTime(0.0001, t);
  const steps = 5;
  for (let i = 1; i <= steps; i++) {
    g.gain.linearRampToValueAtTime(gain * (0.45 + Math.random() * 0.7), t + dur * (i / steps) * 0.9);
  }
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur + 0.02);
  head.connect(g); g.connect(master);
  const sg = AC.createGain(); sg.gain.value = o.send == null ? 0.18 : o.send;
  g.connect(sg); sg.connect(verbSend);
  src.start(t); src.stop(t + dur + 0.05);
}
