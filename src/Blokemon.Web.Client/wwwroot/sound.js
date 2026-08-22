// The one thing the page imports to make a noise, and the only file the renderer knows about.
//
// Everything below the surface is synthesised live - there is no audio file anywhere in the game -
// but none of that is the renderer's business. What it gets is: tell me what happened and how fast
// the table is playing it, and I will decide what that sounds like.
//
// A browser will not start an audio context until the player has touched the page, so the context
// is built on the first gesture rather than at load. Until then every cue is dropped on the floor,
// which is the correct behaviour and not a failure: the first thing a player does is press
// something, and by the second thing there is sound.

import { ensure, AC, master, ready } from "./sound/audioContext.js";
import { musicOn, musicOff, setTension } from "./sound/transport.js";
import * as ceremony from "./sound/ceremonyCues.js";
import * as match from "./sound/matchCues.js";

const STORAGE_KEY = "blokemon.sound";

// Every cue the game can ask for. The renderer names one of these and nothing else; a name with no
// entry is dropped rather than throwing, because a missing sound must never cost a beat.
const CUES = {
  tear: ceremony.tear,
  deckOpening: ceremony.deckOpening,
  riffle: ceremony.riffle,
  setup: match.setup,
  draw: match.draw,
  play: match.play,
  attach: match.attach,
  evolve: match.evolve,
  attack: match.attack,
  damage: match.damage,
  heal: match.heal,
  condition: match.condition,
  knockout: match.knockout,
  prize: match.prize,
  turn: match.turn,
  coin: match.coin,
  victory: match.victory,
  reveal: match.reveal,
};

let enabled = true;
let volume = 0.7;
let armed = false;
let wanted = null;

function stored() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    // A browser with storage blocked still gets sound; it just cannot remember the choice.
    return null;
  }
}

function remember() {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ enabled, volume }));
  } catch {
    // As above: not remembering is survivable, failing to make a sound is not.
  }
}

// The first gesture is what a browser needs before it will start an audio context, so it is taken
// from whatever the player happens to touch first rather than from a control they have to find.
function armOnFirstGesture() {
  const arm = () => {
    if (armed) {
      return;
    }
    armed = true;
    if (enabled) {
      start();
    }
  };
  for (const event of ["pointerdown", "keydown", "touchstart"]) {
    window.addEventListener(event, arm, { once: true, passive: true });
  }
}

function start() {
  ensure();
  if (AC.state === "suspended") {
    AC.resume();
  }
  master.gain.value = volume;
  if (wanted) {
    musicOn(wanted);
  }
}

// Reads back what the player chose last time, so the control renders in the right state on the
// first frame rather than flicking to it once storage has been asked.
export function initialise() {
  const saved = stored();
  if (saved) {
    enabled = saved.enabled !== false;
    volume = typeof saved.volume === "number" ? saved.volume : volume;
  }
  armOnFirstGesture();
  return { enabled, volume };
}

export function setEnabled(on) {
  enabled = !!on;
  remember();
  if (!enabled) {
    musicOff();
    if (ready()) {
      master.gain.value = 0;
    }
    return;
  }
  if (armed) {
    start();
  }
}

export function setVolume(level) {
  volume = Math.min(1, Math.max(0, level));
  remember();
  if (enabled && ready()) {
    master.gain.value = volume;
  }
}

// Which theme the page wants under it. Held even while sound is off or before the first gesture,
// so that turning sound on starts the music the page asked for rather than silence.
export function setMusic(mode) {
  wanted = mode || null;
  if (!enabled || !armed) {
    return;
  }
  ensure();
  if (wanted) {
    musicOn(wanted);
  } else {
    musicOff();
  }
}

// Armed while a player is one prize from winning, and disarmed if that stops being true.
export function setLastPrize(on) {
  if (ready()) {
    setTension(on);
  }
}

// What just happened on the table. `options` carries only what changes the sound - how fast the
// table is playing the cue, which finish a pack has, whether a prize is the last one.
export function cue(name, options) {
  if (!enabled || !armed || !CUES[name]) {
    return;
  }
  ensure();
  if (AC.state === "suspended") {
    AC.resume();
  }
  CUES[name](AC.currentTime + 0.02, options || {});
}
