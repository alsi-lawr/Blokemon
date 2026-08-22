// The one thing the page imports to make a noise, and the only file the renderer knows about.
//
// Everything below the surface is synthesised live - there is no audio file anywhere in the game -
// but none of that is the renderer's business. What it gets is: tell me what happened and how fast
// the table is playing it, and I will decide what that sounds like.
//
// There are two levels rather than one, because the music and the table are two different things
// to want quiet. Either at nothing is that half switched off outright: the theme stops being
// scheduled and cues stop being built at all, so a player who has turned something down to zero
// is not paying for it.
//
// A browser will not start an audio context until the player has touched the page, so the context
// is built on the first gesture rather than at load. Until then every cue is dropped on the floor,
// which is the correct behaviour and not a failure: the first thing a player does is press
// something, and by the second thing there is sound.

import { ensure, AC, musicBus, sfxBus, ready } from "./sound/audioContext.js";
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

let musicLevel = 0.7;
let effectsLevel = 0.7;
let armed = false;
let wanted = null;

function stored() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    // A browser with storage blocked still gets sound; it just cannot remember the levels.
    return null;
  }
}

function remember() {
  try {
    window.localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ music: musicLevel, effects: effectsLevel })
    );
  } catch {
    // As above: not remembering is survivable, failing to make a sound is not.
  }
}

const level = (value, fallback) =>
  typeof value === "number" && value >= 0 && value <= 1 ? value : fallback;

// The first gesture is what a browser needs before it will start an audio context, so it is taken
// from whatever the player happens to touch first rather than from a control they have to find.
function armOnFirstGesture() {
  const arm = () => {
    if (armed) {
      return;
    }
    armed = true;
    start();
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
  musicBus.gain.value = musicLevel;
  sfxBus.gain.value = effectsLevel;
  if (wanted && musicLevel > 0) {
    musicOn(wanted);
  }
}

// Reads back what the player chose last time, so the controls render in the right state on the
// first frame rather than flicking to it once storage has been asked.
export function initialise() {
  const saved = stored();
  if (saved) {
    musicLevel = level(saved.music, musicLevel);
    effectsLevel = level(saved.effects, effectsLevel);
  }
  armOnFirstGesture();
  return { music: musicLevel, effects: effectsLevel };
}

// The theme under the page. At nothing it is not merely inaudible: the bar pump stops, because a
// loop that keeps scheduling oscillators into a silent bus is work nobody asked for.
export function setMusicVolume(value) {
  musicLevel = Math.min(1, Math.max(0, value));
  remember();
  if (!ready()) {
    return;
  }
  musicBus.gain.value = musicLevel;
  if (musicLevel > 0) {
    if (wanted) {
      musicOn(wanted);
    }
  } else {
    musicOff();
  }
}

export function setEffectsVolume(value) {
  effectsLevel = Math.min(1, Math.max(0, value));
  remember();
  if (ready()) {
    sfxBus.gain.value = effectsLevel;
  }
}

// Which theme the page wants under it. Held even before the first gesture or while the music is at
// nothing, so that turning it back up starts the theme the page asked for rather than silence.
export function setMusic(mode) {
  wanted = mode || null;
  if (!armed || musicLevel === 0) {
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
  if (!armed || effectsLevel === 0 || !CUES[name]) {
    return;
  }
  ensure();
  if (AC.state === "suspended") {
    AC.resume();
  }
  CUES[name](AC.currentTime + 0.02, options || {});
}
