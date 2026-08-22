// The voices both themes are played on: a pub upright, a bass, a reedy pad, and a kit.
//
// The piano is inharmonic with a felt-hammer transient and a decay that shortens as it goes up the
// keyboard, and it is slightly detuned, because a pub piano always is.

import { AC, noiseSrc, out } from "./audioContext.js";
import { SPB, B_SPB } from "./tempo.js";

export const mid2f = m => 440 * Math.pow(2, (m - 69) / 12);

// An upright piano: inharmonic partials, a felt-hammer transient, and a decay that shortens as
// it goes up the keyboard. Slightly detuned, because a pub piano always is.
export function piano(t, midi, beats, vel, bus) {
  const f = mid2f(midi) * (1 + (Math.random() - 0.5) * 0.0035);
  const P = [[1,1,1],[2,0.40,0.72],[3,0.20,0.54],[4,0.11,0.42],[5,0.065,0.32],[6,0.04,0.25],[8,0.018,0.18]];
  const base = Math.max(0.5, 3.1 - Math.log2(f / 98) * 0.48);
  for (const [n, amp, dm] of P) {
    const osc = AC.createOscillator(); osc.type = "sine";
    osc.frequency.value = f * n * Math.sqrt(1 + 0.00042 * n * n);
    const g = AC.createGain();
    const dec = Math.min(base * dm, beats * SPB + 1.5);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(Math.max(0.0002, vel * amp * 0.085), t + 0.005);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dec);
    osc.connect(g); out(g, 0, 0.3, bus);
    osc.start(t); osc.stop(t + dec + 0.05);
  }
  const s = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = f * 2.6; bp.Q.value = 1.1;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vel * 0.035, t + 0.001);
  g.gain.exponentialRampToValueAtTime(0.0001, t + 0.02);
  s.connect(bp); bp.connect(g); out(g, 0, 0.15, bus);
  s.start(t); s.stop(t + 0.04);
}

export function bassNote(t, midi, beats, vel, bus) {
  const f = mid2f(midi), dur = beats * SPB;
  const osc = AC.createOscillator(); osc.type = "triangle"; osc.frequency.value = f;
  const sub = AC.createOscillator(); sub.type = "sine"; sub.frequency.value = f;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = 620; lp.Q.value = 0.9;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vel * 0.16, t + 0.014);
  g.gain.exponentialRampToValueAtTime(Math.max(0.0002, vel * 0.06), t + dur * 0.6);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur * 0.98);
  osc.connect(lp); sub.connect(lp); lp.connect(g); out(g, 0, 0.1, bus);
  osc.start(t); osc.stop(t + dur); sub.start(t); sub.stop(t + dur);
}

// A reedy drawbar pad: harmonics 1, 2, 3 and 4 with a slow swell, sat well under everything.
export function pad(t, midi, beats, vel, bus) {
  const f = mid2f(midi), dur = beats * SPB;
  for (const [n, a] of [[1,1],[2,0.5],[3,0.28],[4,0.14]]) {
    const osc = AC.createOscillator(); osc.type = "sine";
    osc.frequency.value = f * n * (1 + (Math.random() - 0.5) * 0.004);
    const g = AC.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(Math.max(0.0002, vel * a * 0.022), t + 0.14);
    g.gain.setValueAtTime(Math.max(0.0002, vel * a * 0.022), t + dur * 0.72);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(g); out(g, (n % 2 ? -0.18 : 0.18), 0.4, bus);
    osc.start(t); osc.stop(t + dur + 0.05);
  }
}

export function kick(t, vel, bus) {
  const o = AC.createOscillator(); o.type = "sine";
  o.frequency.setValueAtTime(140, t);
  o.frequency.exponentialRampToValueAtTime(45, t + 0.085);
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vel * 0.5, t + 0.004);
  g.gain.exponentialRampToValueAtTime(0.0001, t + 0.23);
  o.connect(g); out(g, 0, 0.05, bus);
  o.start(t); o.stop(t + 0.3);
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 1400;
  const g2 = AC.createGain();
  g2.gain.setValueAtTime(vel * 0.09, t);
  g2.gain.exponentialRampToValueAtTime(0.0001, t + 0.013);
  s.connect(hp); hp.connect(g2); out(g2, 0, 0.04, bus);
  s.start(t); s.stop(t + 0.03);
}

// Two tuned shell modes plus the snares across them — a clap in a pub, near enough.
export function snare(t, vel, bus) {
  for (const [f, a] of [[186, 0.5], [332, 0.3]]) {
    const o = AC.createOscillator(); o.type = "triangle"; o.frequency.value = f;
    const g = AC.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(vel * a * 0.2, t + 0.002);
    g.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
    o.connect(g); out(g, 0.06, 0.26, bus);
    o.start(t); o.stop(t + 0.16);
  }
  const s = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = 1850; bp.Q.value = 0.55;
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 900;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vel * 0.26, t + 0.002);
  g.gain.exponentialRampToValueAtTime(0.0001, t + 0.15);
  s.connect(bp); bp.connect(hp); hp.connect(g); out(g, 0.06, 0.32, bus);
  s.start(t); s.stop(t + 0.2);
}

export function hat(t, vel, open, bus) {
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 7200;
  const bp = AC.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = 9800; bp.Q.value = 0.85;
  const dur = open ? 0.15 : 0.03;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vel * 0.1, t + 0.001);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  s.connect(hp); hp.connect(bp); bp.connect(g); out(g, -0.18, 0.12, bus);
  s.start(t); s.stop(t + dur + 0.03);
}

export function drone(t, midi, beats, vel, bus) {
  const f = mid2f(midi), dur = beats * B_SPB;
  for (const [n, a] of [[1, 1], [2, 0.4], [3, 0.16]]) {
    const o = AC.createOscillator(); o.type = "sawtooth";
    o.frequency.value = f * n * (1 + (Math.random() - 0.5) * 0.004);
    const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = 320;
    const g = AC.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.linearRampToValueAtTime(vel * a * 0.05, t + 0.08);
    g.gain.setValueAtTime(vel * a * 0.05, t + dur * 0.8);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(lp); lp.connect(g); out(g, 0, 0.2, bus);
    o.start(t); o.stop(t + dur + 0.05);
  }
}
