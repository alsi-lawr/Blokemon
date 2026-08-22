// Starting, stopping and holding the music, plus the two layers that sit under it.
//
// Bars are scheduled ahead on the audio clock rather than from a timer, so the loop cannot drift.
// The lookahead is deliberately short: it is exactly how long a live change - arming the tension
// layer, say - takes to become audible, and a long one reads as the control doing nothing.
//
// The tension drone runs continuously rather than being re-armed each bar, for the same reason.

import { AC, musicBus, now, noiseSrc } from "./audioContext.js";
import { mid2f } from "./instruments.js";
import { BAR, B_BAR } from "./tempo.js";
import { scheduleBar, scheduleBattleBar } from "./themes.js";

export let music = null;
let roomNode = null;
let tensionDrone = null;
let tension = false;

/* ---- room tone: the pub through a wall ----

   It rides the music bus rather than a bus of its own. Room tone is the bed the theme is heard
   over, it comes and goes with the theme, and a player who turns the music off has asked for the
   room to go quiet too - not to be left with the extractor fan. */
export function roomOn() {
  if (roomNode) return;
  const s = noiseSrc();
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = 420;
  const lp2 = AC.createBiquadFilter(); lp2.type = "lowpass"; lp2.frequency.value = 900;
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 60;
  const g = AC.createGain(); g.gain.value = 0.0001;
  g.gain.linearRampToValueAtTime(0.045, now() + 1.2);
  const lfo = AC.createOscillator(); lfo.frequency.value = 0.06;
  const lfoG = AC.createGain(); lfoG.gain.value = 0.016;
  lfo.connect(lfoG); lfoG.connect(g.gain); lfo.start();
  s.connect(hp); hp.connect(lp); lp.connect(lp2); lp2.connect(g); g.connect(musicBus);
  s.start();
  roomNode = { s, g, lfo };
}

export function roomOff() {
  if (!roomNode) return;
  const { s, g, lfo } = roomNode;
  g.gain.cancelScheduledValues(now());
  g.gain.setValueAtTime(g.gain.value, now());
  g.gain.linearRampToValueAtTime(0.0001, now() + 0.5);
  s.stop(now() + 0.6); lfo.stop(now() + 0.6);
  roomNode = null;
}

/* The tension layer's drone. It was a D at MIDI 26 — 36.7 Hz — which is below what a laptop or a
   desktop monitor reproduces at all, so it was running correctly and could not be heard. It is now
   a D2 with its octave and fifth above it, which is still unmistakably a low drone but lives where
   speakers actually work, and it moves: a slow filter sweep and a slow swell, because a static hum
   disappears into the mix within seconds whereas something breathing under the music does not. */
export function tensionOn(bus) {
  if (tensionDrone || !bus) return;
  const g = AC.createGain(); g.gain.value = 0.0001;
  g.gain.linearRampToValueAtTime(0.3, now() + 0.4);
  g.connect(bus);

  const lp = AC.createBiquadFilter(); lp.type = "lowpass";
  lp.frequency.value = 520; lp.Q.value = 4;

  // slow sweep across the drone, so it breathes rather than sits
  const lfo = AC.createOscillator(); lfo.type = "sine"; lfo.frequency.value = 0.14;
  const lfoA = AC.createGain(); lfoA.gain.value = 260;
  lfo.connect(lfoA); lfoA.connect(lp.frequency); lfo.start();

  // slow swell on top of the sweep
  const trem = AC.createOscillator(); trem.type = "sine"; trem.frequency.value = 0.5;
  const tremA = AC.createGain(); tremA.gain.value = 0.22;
  const tremG = AC.createGain(); tremG.gain.value = 0.78;
  trem.connect(tremA); tremA.connect(tremG.gain); trem.start();
  lp.connect(tremG); tremG.connect(g);

  const nodes = [lfo, trem];
  // D2, its octave, and the fifth above — a power drone rooted in the battle theme's D minor
  for (const [midi, a, type] of [[38, 1, "sawtooth"], [50, 0.5, "sawtooth"], [57, 0.28, "triangle"]]) {
    const o = AC.createOscillator(); o.type = type;
    o.frequency.value = mid2f(midi) * (1 + (Math.random() - 0.5) * 0.004);
    const vg = AC.createGain(); vg.gain.value = a * 0.5;
    o.connect(vg); vg.connect(lp);
    o.start(); nodes.push(o);
  }
  tensionDrone = { g, nodes };
}

export function tensionOff() {
  if (!tensionDrone) return;
  const { g, nodes } = tensionDrone; tensionDrone = null;
  g.gain.cancelScheduledValues(now());
  g.gain.setValueAtTime(g.gain.value, now());
  g.gain.linearRampToValueAtTime(0.0001, now() + 0.35);
  nodes.forEach(o => { try { o.stop(now() + 0.5); } catch (e) {} });
}

export function musicOn(mode) {
  if (music && music.mode === mode) return;
  if (music) musicOff();
  const battle = mode === "battle";
  const level = AC.createGain(); level.gain.value = 0.0001;
  const duckG = AC.createGain(); duckG.gain.value = 1;
  level.connect(duckG); duckG.connect(musicBus);
  level.gain.linearRampToValueAtTime(battle ? 0.78 : 1.2, now() + (battle ? 0.7 : 1.4));
  music = {
    mode, level, duckG, next: now() + 0.25, bar: 0, timer: null, tension,
    bars: battle ? 16 : 8, len: battle ? B_BAR : BAR
  };
  const pump = () => {
    if (!music) return;
    const ahead = now() + 0.55;
    while (music.next < ahead) {
      if (music.mode === "battle") scheduleBattleBar(music.bar % 16, music.next, music.level, music.tension);
      else scheduleBar(music.bar % 8, music.next, music.level);
      music.next += music.len;
      music.bar++;
    }
  };
  pump();
  music.timer = setInterval(pump, 120);
  roomOn();
  if (battle && tension) tensionOn(level);
}

export function musicOff() {
  if (!music) return;
  tensionOff();
  roomOff();
  const m = music; music = null;
  clearInterval(m.timer);
  m.level.gain.cancelScheduledValues(now());
  m.level.gain.setValueAtTime(m.level.gain.value, now());
  m.level.gain.linearRampToValueAtTime(0.0001, now() + 0.6);
}

/* The last prize. Armed the moment a player is one prize from winning and disarmed if that stops
   being true, so it tracks the game rather than being switched on once and left. The drone answers
   on the press; the extra kick and the double-time hats join on the next bar line, which is where
   a rhythm layer belongs. */
export function setTension(on) {
  tension = !!on;
  if (!music) return;
  music.tension = tension;
  if (tension && music.mode === "battle") tensionOn(music.level);
  else tensionOff();
}

// The loud cues push the music down and let it back up, as the design sheet asks.
export function duck(amount, hold) {
  if (!music) return;
  const g = music.duckG.gain, t = now();
  g.cancelScheduledValues(t); g.setValueAtTime(g.value, t);
  g.linearRampToValueAtTime(amount == null ? 0.42 : amount, t + 0.06);
  g.linearRampToValueAtTime(1, t + 0.06 + (hold == null ? 0.3 : hold) + 0.55);
}
