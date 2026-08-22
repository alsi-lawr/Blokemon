// The one audio graph the page owns: a context, a master gain the player's volume rides on, a
// compressor so a loud cue cannot clip the mix, and a small room every voice sends a little into.
//
// Nothing here is built until sound is armed, because a browser will not start an audio context
// before the player has touched the page, and building one that then sits suspended costs a
// thread and buys nothing.

export let AC = null;
export let master = null;
export let verbSend = null;
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
  master.gain.value = 0.75;
  master.connect(comp); comp.connect(AC.destination);

  // A small room, made by decaying noise. Everything sends a little into it.
  const len = Math.floor(AC.sampleRate * 0.4);
  const ir = AC.createBuffer(2, len, AC.sampleRate);
  for (let ch = 0; ch < 2; ch++) {
    const d = ir.getChannelData(ch);
    for (let i = 0; i < len; i++) {
      const t = i / len;
      d[i] = (Math.random() * 2 - 1) * Math.pow(1 - t, 3.4) * (i < 60 ? i / 60 : 1);
    }
  }
  const conv = AC.createConvolver(); conv.buffer = ir;
  verbSend = AC.createGain(); verbSend.gain.value = 1;
  const ret = AC.createGain(); ret.gain.value = 0.5;
  verbSend.connect(conv); conv.connect(ret); ret.connect(master);
  return AC;
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

export function out(node, pan, send, bus) {
  const p = AC.createStereoPanner(); p.pan.value = Math.max(-1, Math.min(1, pan || 0));
  node.connect(p); p.connect(bus || master);
  if (send) { const g = AC.createGain(); g.gain.value = send; p.connect(g); g.connect(verbSend); }
  return p;
}
