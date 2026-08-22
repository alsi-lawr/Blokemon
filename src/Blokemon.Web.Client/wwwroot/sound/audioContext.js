// The one audio graph the page owns: a context, a compressor so a loud cue cannot clip the mix,
// and beneath it two buses that never meet again until they leave.
//
// The split is the whole point. A player sets the music and the sounds the table makes to their
// own levels, so each is a bus of its own with its own room hung off it. One shared room would
// mean a table turned all the way down still whispering back off the walls, which is not what
// turning something off means.
//
// Nothing here is built until sound is armed, because a browser will not start an audio context
// before the player has touched the page, and building one that then sits suspended costs a
// thread and buys nothing.

export let AC = null;

// The two things a player sets the level of, and the room each of them is heard in.
export let musicBus = null;
export let sfxBus = null;
export let musicRoom = null;
export let sfxRoom = null;

// Not exported: nothing outside this file has any business at the very end of the chain. Every
// voice belongs to one bus or the other, and the buses are what the player's controls move.
let master = null;
let NOISE = null;

export const now = () => AC.currentTime;
export const ready = () => AC !== null;

export function ensure() {
  if (AC) return AC;
  const Ctx = window.AudioContext || window.webkitAudioContext;
  AC = new Ctx();
  const comp = AC.createDynamicsCompressor();
  comp.threshold.value = -13; comp.knee.value = 20; comp.ratio.value = 3.2;
  comp.attack.value = 0.004; comp.release.value = 0.2;
  master = AC.createGain();
  master.gain.value = 1;
  master.connect(comp); comp.connect(AC.destination);

  // The levels the player's own two controls ride on. They open at the same default the settings
  // do, so the first bar heard before storage has been read is not the wrong loudness.
  musicBus = AC.createGain(); musicBus.gain.value = 0.7;
  sfxBus = AC.createGain(); sfxBus.gain.value = 0.7;
  musicBus.connect(master); sfxBus.connect(master);

  // A small room, made by decaying noise. One impulse serves both: a convolver holds the buffer
  // rather than consuming it, so the same room can be hung off each bus without building it twice.
  const len = Math.floor(AC.sampleRate * 0.4);
  const ir = AC.createBuffer(2, len, AC.sampleRate);
  for (let ch = 0; ch < 2; ch++) {
    const d = ir.getChannelData(ch);
    for (let i = 0; i < len; i++) {
      const t = i / len;
      d[i] = (Math.random() * 2 - 1) * Math.pow(1 - t, 3.4) * (i < 60 ? i / 60 : 1);
    }
  }
  musicRoom = room(ir, musicBus);
  sfxRoom = room(ir, sfxBus);
  return AC;
}

// A send that everything on one bus can put a little of itself into, coming back on that same bus.
function room(ir, bus) {
  const conv = AC.createConvolver(); conv.buffer = ir;
  const send = AC.createGain(); send.gain.value = 1;
  const ret = AC.createGain(); ret.gain.value = 0.5;
  send.connect(conv); conv.connect(ret); ret.connect(bus);
  return send;
}

export function noiseBuf() {
  if (!NOISE) {
    const n = AC.createBuffer(1, Math.floor(AC.sampleRate * 2), AC.sampleRate);
    const d = n.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    NOISE = n;
  }
  return NOISE;
}

export function noiseSrc() {
  const s = AC.createBufferSource();
  s.buffer = noiseBuf(); s.loop = true;
  s.playbackRate.value = 0.85 + Math.random() * 0.3;
  return s;
}

// Where a voice comes out. A voice handed a bus is one of the theme's - the themes pass the bus
// their bar is being played on, so a whole bar can be ducked and faded as one thing - and a voice
// without one is a sound the table made. The room follows the family for the same reason the bus
// does: what a player turns down has to take its own reverberation down with it.
export function out(node, pan, send, bus) {
  const p = AC.createStereoPanner(); p.pan.value = Math.max(-1, Math.min(1, pan || 0));
  node.connect(p); p.connect(bus || sfxBus);
  if (send) {
    const g = AC.createGain(); g.gain.value = send;
    p.connect(g); g.connect(bus ? musicRoom : sfxRoom);
  }
  return p;
}
