// Contacts, friction and air - the things a hand does to a card, a mat, a box and a bell.
//
// The rule the file is built on is that CARDS HAVE NO PITCH. A card struck, flicked, landed or
// fanned is a broadband papery transient with no ring, so a run of card events built as pitched
// hits is a tuned percussion instrument. Card sounds vary in level, spectral centre and duration
// and never in frequency, and where several cards move together the answer is friction rather than
// a row of taps, because that is what cards actually do to each other.

import { AC, sfxBus, sfxRoom, noiseSrc, out } from "./audioContext.js";
import { MATERIAL, STRUCK } from "./materials.js";
import { modal, strike, crinkle } from "./resonance.js";

/* Something soft and fibrous landing flat — a beer mat. Pulpboard has a loss factor around
   0.05-0.1, which is to say it absorbs rather than rings, and MODES were the mistake here: even
   heavily damped, a defined frequency makes it read as a thin piece of wood. All noise, and gone. */
export function matSlap(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.5 : o.gain, pan = o.pan || 0;
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 150;
  const pk = AC.createBiquadFilter(); pk.type = "peaking";
  pk.frequency.value = 640; pk.Q.value = 0.75; pk.gain.value = 3;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = o.lp || 2300;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(gain, t + 0.002);
  g.gain.exponentialRampToValueAtTime(0.0001, t + (o.dur || 0.055));
  s.connect(hp); hp.connect(pk); pk.connect(lp); lp.connect(g); out(g, pan, 0.16);
  s.start(t); s.stop(t + 0.12);
  // the pat of it against the table: low, brief, and with no note in it
  const s2 = noiseSrc();
  const hp2 = AC.createBiquadFilter(); hp2.type = "highpass"; hp2.frequency.value = 95;
  const lp2 = AC.createBiquadFilter(); lp2.type = "lowpass"; lp2.frequency.value = 300; lp2.Q.value = 0.7;
  const g2 = AC.createGain();
  g2.gain.setValueAtTime(0.0001, t);
  g2.gain.linearRampToValueAtTime(gain * 0.8, t + 0.004);
  g2.gain.exponentialRampToValueAtTime(0.0001, t + 0.085);
  s2.connect(hp2); hp2.connect(lp2); lp2.connect(g2); out(g2, pan, 0.12);
  s2.start(t); s2.stop(t + 0.14);
}

/* A flat mat turning edge-through-air. Each chop is a PUFF, not a note. Pitching each chop down a
   scale as the spin decelerated is exactly the effect that got called plinko: the chops vary in
   level and position and nothing else, because that is all that actually varies. */
export function matFlap(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.1 : o.gain, pan = o.pan || 0;
  const s = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass";
  bp.frequency.value = 1150; bp.Q.value = 1.0;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(gain, t + 0.003);
  g.gain.exponentialRampToValueAtTime(0.0001, t + (o.dur || 0.038));
  s.connect(bp); bp.connect(g); out(g, pan, 0.12);
  s.start(t); s.stop(t + 0.09);
}

// One card contacting something. No modes, no tone, nothing to tune.
export function cardTap(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.3 : o.gain, pan = o.pan || 0;
  const dur = o.dur || 0.013;
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = o.hp || 460;
  const pk = AC.createBiquadFilter(); pk.type = "peaking";
  pk.frequency.value = o.centre || 3200; pk.Q.value = 0.75; pk.gain.value = 5;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = o.lp || 9500;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(gain, t + 0.0006);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  s.connect(hp); hp.connect(pk); pk.connect(lp); lp.connect(g);
  out(g, pan, o.send == null ? 0.14 : o.send);
  s.start(t); s.stop(t + dur + 0.02);

  // the surface underneath, when it lands on one: a soft low pat with no tone in it at all
  if (o.surface) {
    const s2 = noiseSrc();
    const lp2 = AC.createBiquadFilter(); lp2.type = "lowpass";
    lp2.frequency.value = o.surface === "felt" ? 420 : 700; lp2.Q.value = 0.6;
    const hp2 = AC.createBiquadFilter(); hp2.type = "highpass"; hp2.frequency.value = 110;
    const g2 = AC.createGain();
    g2.gain.setValueAtTime(0.0001, t);
    g2.gain.linearRampToValueAtTime(gain * 0.55, t + 0.003);
    g2.gain.exponentialRampToValueAtTime(0.0001, t + 0.045);
    s2.connect(hp2); hp2.connect(lp2); lp2.connect(g2); out(g2, pan, 0.1);
    s2.start(t); s2.stop(t + 0.08);
  }
}

/* Cards sliding over one another — a deck coming out of a tray, a hand fanning open, a pack being
   squared. Friction, not impacts: a band of noise whose level wobbles with the irregularity of the
   contact. Documented card friction sits above about 3 kHz with near-nothing below 500, but paper
   rustling also has low-mid bands around 170 and 390 Hz, so there is a little body underneath.
   Any edges that let go on the way are cardTaps, so they have no pitch either. */
export function cardSlide(t, o) {
  o = o || {};
  const dur = o.dur || 0.4;
  /* Mixed at 0.4 of the level the call sites ask for. They were set against the peak amplitude of
     the transients around them, which is the wrong comparison: friction is SUSTAINED, and the ear
     integrates loudness over time, so a slide held for half a second at the same peak as a 13 ms
     tap is far louder than it. The factor is here rather than at the call sites so the balance
     between the slides stays as it was — only the whole family moves. */
  const gain = (o.gain == null ? 0.16 : o.gain) * 0.4;
  const pan = o.pan || 0, gloss = o.gloss !== false;
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = gloss ? 1500 : 900;
  const pk = AC.createBiquadFilter(); pk.type = "peaking";
  pk.frequency.value = gloss ? 4200 : 3200; pk.Q.value = 0.6; pk.gain.value = gloss ? 5 : 3;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = gloss ? 9500 : 6800;
  const g = AC.createGain();
  // the wobble: contact between two sheets is never even
  const steps = Math.max(6, Math.round(dur * (o.rate || 46)));
  g.gain.setValueAtTime(0.0001, t);
  for (let i = 1; i <= steps; i++) {
    const u = i / steps;
    g.gain.linearRampToValueAtTime(
      Math.max(0.0001, gain * Math.sin(Math.PI * Math.pow(u, o.skew || 0.85)) * (0.4 + Math.random() * 0.75)),
      t + dur * u);
  }
  g.gain.linearRampToValueAtTime(0.0001, t + dur + 0.015);
  const pn = AC.createStereoPanner();
  pn.pan.setValueAtTime(pan, t);
  pn.pan.linearRampToValueAtTime(o.pan1 == null ? pan : o.pan1, t + dur);
  s.connect(hp); hp.connect(pk); pk.connect(lp); lp.connect(g); g.connect(pn); pn.connect(sfxBus);
  const sg = AC.createGain(); sg.gain.value = o.send == null ? 0.16 : o.send;
  pn.connect(sg); sg.connect(sfxRoom);
  s.start(t); s.stop(t + dur + 0.05);

  // the mass of the stack under the friction
  const s2 = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass";
  bp.frequency.value = 360; bp.Q.value = 1.1;
  const g2 = AC.createGain();
  g2.gain.setValueAtTime(0.0001, t);
  g2.gain.linearRampToValueAtTime(gain * 0.42, t + dur * 0.35);
  g2.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  s2.connect(bp); bp.connect(g2); out(g2, pan, 0.12);
  s2.start(t); s2.stop(t + dur + 0.05);

  // edges letting go as the cards pass each other
  const flicks = o.flicks == null ? Math.round(dur * 14) : o.flicks;
  for (let i = 0; i < flicks; i++) {
    cardTap(t + Math.random() * dur, {
      gain: gain * (0.25 + Math.random() * 0.5), dur: 0.006 + Math.random() * 0.008,
      centre: 2400 + Math.random() * 3000, pan: pan + (Math.random() - 0.5) * 0.5, send: 0.1
    });
  }
}

/* A card edge landing. This used to be a pitched modal hit with a wood thump under it, and since
   almost everything on this page that involves several cards calls it in a row, it is where most of
   the xylophone came from. It is a papery transient and the surface beneath it, and it has no
   pitch — `freq` now names the centre of a filter on noise, not a note. */
export function tick(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.42 : o.gain;
  cardTap(t, {
    gain: gain,
    dur: o.dur || 0.014,
    pan: o.pan || 0,
    centre: Math.min(5200, o.centre || 3000),
    send: o.send == null ? 0.14 : o.send,
    surface: o.body === false ? null : "felt"
  });
}

/* Something with mass arriving on the table. The falling sine that used to be here — 1.7f down to
   0.6f — is how a timpani is synthesised, and a tensioned membrane whose pitch is the point is the
   one thing a cardboard box is not. There is no pitch glide left in it. */
export function thud(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.6 : o.gain, pan = o.pan || 0;
  const mat = o.mat || "cardboard";
  const base = STRUCK[mat].modes[0][0];
  strike(t, mat, {
    pitch: (o.freq || base) / base, gain: gain * 0.95, decay: o.decayScale || 1.15,
    pan: pan, send: o.send == null ? 0.24 : o.send
  });
  // and the table taking it
  strike(t + 0.004, "wood", { pitch: 0.82, gain: gain * 0.42, decay: 0.6, pan: pan, send: 0.2 });
}

/* A blow landing. Told twice that this still was not a punch, and the reason is the layer I kept
   protecting: a SINE for the body. A sine at any frequency is a drum tone, and no amount of moving
   it around changes that — 150 Hz is simply a higher-pitched drum than 60 Hz. Real impact foley on
   a body is not tonal at all. Everything here is noise: a fleshy slap on top, a mid crunch, and a
   low thump made by slamming a lowpass shut on broadband noise, which gives weight without ever
   giving a pitch. There is nothing left in it that can be hummed. */
export function punch(t, o) {
  o = o || {};
  const gain = o.gain == null ? 1 : o.gain, pan = o.pan || 0, heavy = !!o.heavy;

  // the slap — the loudest layer, and the one that says flesh rather than drumhead
  const s1 = noiseSrc();
  const hp1 = AC.createBiquadFilter(); hp1.type = "highpass"; hp1.frequency.value = heavy ? 800 : 1100;
  const lp1 = AC.createBiquadFilter(); lp1.type = "lowpass"; lp1.frequency.value = heavy ? 5000 : 5800;
  const pk1 = AC.createBiquadFilter(); pk1.type = "peaking";
  pk1.frequency.value = heavy ? 2700 : 3300; pk1.Q.value = 0.7; pk1.gain.value = 8;
  const g1 = AC.createGain();
  g1.gain.setValueAtTime(0.0001, t);
  g1.gain.linearRampToValueAtTime(gain * 0.72, t + 0.004);
  g1.gain.exponentialRampToValueAtTime(0.0001, t + (heavy ? 0.10 : 0.082));
  s1.connect(hp1); hp1.connect(lp1); lp1.connect(pk1); pk1.connect(g1); out(g1, pan, 0.16);
  s1.start(t); s1.stop(t + 0.2);

  // the crunch — the mid texture of something soft giving way
  const s2 = noiseSrc();
  const bp2 = AC.createBiquadFilter(); bp2.type = "bandpass";
  bp2.frequency.value = heavy ? 560 : 720; bp2.Q.value = 0.8;
  const g2 = AC.createGain();
  g2.gain.setValueAtTime(0.0001, t);
  g2.gain.linearRampToValueAtTime(gain * 0.56, t + 0.007);
  g2.gain.exponentialRampToValueAtTime(0.0001, t + (heavy ? 0.17 : 0.13));
  s2.connect(bp2); bp2.connect(g2); out(g2, pan, 0.14);
  s2.start(t); s2.stop(t + 0.26);

  // the thump — noise with a lowpass slammed shut on it. All the weight of a sine, none of the note.
  const s3 = noiseSrc();
  const lp3 = AC.createBiquadFilter(); lp3.type = "lowpass"; lp3.Q.value = 0.9;
  lp3.frequency.setValueAtTime(heavy ? 420 : 500, t);
  lp3.frequency.exponentialRampToValueAtTime(heavy ? 62 : 78, t + (heavy ? 0.13 : 0.10));
  const hp3 = AC.createBiquadFilter(); hp3.type = "highpass"; hp3.frequency.value = 38;
  const g3 = AC.createGain();
  g3.gain.setValueAtTime(0.0001, t);
  g3.gain.linearRampToValueAtTime(gain * 0.85, t + 0.007);
  g3.gain.exponentialRampToValueAtTime(0.0001, t + (heavy ? 0.24 : 0.18));
  s3.connect(hp3); hp3.connect(lp3); lp3.connect(g3); out(g3, pan, 0.08);
  s3.start(t); s3.stop(t + 0.32);
}

// Friction genuinely is noise — but it is noise heard THROUGH the thing being rubbed, so it
// gets that material's resonance and a narrow filter rather than a broad one.
export function scuff(t, o) {
  o = o || {};
  const mat = o.mat || "cardboard", m = MATERIAL[mat];
  const dur = o.dur || 0.13, gain = o.gain == null ? 0.34 : o.gain;
  const pan = o.pan || 0, rate = o.rate || 90;
  const s = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass";
  bp.frequency.value = o.centre || m.f * 2.1; bp.Q.value = o.q || 2.4;
  const res = AC.createBiquadFilter(); res.type = "peaking";
  res.frequency.value = m.f; res.Q.value = 3.5; res.gain.value = 9;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = o.lp || 3000;
  const g = AC.createGain();
  const steps = Math.max(5, Math.round(dur * rate));
  g.gain.setValueAtTime(0.0001, t);
  for (let i = 1; i <= steps; i++) {
    const u = i / steps;
    g.gain.linearRampToValueAtTime(Math.max(0.0001, gain * Math.sin(Math.PI * u) * (0.35 + Math.random() * 0.65)), t + dur * u);
  }
  g.gain.linearRampToValueAtTime(0.0001, t + dur + 0.01);
  s.connect(bp); bp.connect(res); res.connect(lp); lp.connect(g); out(g, pan, 0.2);
  s.start(t); s.stop(t + dur + 0.06);
}

/* A deck squared on the table. The version here was an edge slap over a modal body over a 225 Hz
   sine, and a 225 Hz sine is a note — which is why it was heard as an instrument. Squaring a deck
   is mostly the cards sliding against each other and then the whole stack meeting the felt, and
   neither of those has a pitch. */
export function deckTap(t, o) {
  o = o || {};
  const gain = o.gain == null ? 0.5 : o.gain, pan = o.pan || 0;
  // the cards shuffling flush against one another
  cardSlide(t - 0.05, { dur: 0.13, gain: gain * 0.3, gloss: false, flicks: 3, rate: 70, pan: pan });
  // the edges of the stack meeting
  cardTap(t, { gain: gain * 0.5, dur: 0.016, centre: 2600, lp: 7000, pan: pan });
  // and the weight of it onto the felt: low, dry, toneless
  const s = noiseSrc();
  const hp = AC.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 95;
  const lp = AC.createBiquadFilter(); lp.type = "lowpass"; lp.frequency.value = 380; lp.Q.value = 0.7;
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(gain * 0.7, t + 0.005);
  g.gain.exponentialRampToValueAtTime(0.0001, t + 0.075);
  s.connect(hp); hp.connect(lp); lp.connect(g); out(g, pan, 0.12);
  s.start(t); s.stop(t + 0.13);
}

// Air. Movement past an edge: filtered noise and nothing else.
export function whoosh(t, o) {
  o = o || {};
  const dur = o.dur || 0.4, gain = o.gain == null ? 0.22 : o.gain;
  const f0 = o.f0 || 500, f1 = o.f1 || 2200;
  const p0 = o.p0 == null ? 0 : o.p0, p1 = o.p1 == null ? 0 : o.p1;
  const peak = o.peak == null ? 0.45 : o.peak;
  const s = noiseSrc();
  const bp = AC.createBiquadFilter(); bp.type = "bandpass"; bp.Q.value = o.q || 3.4;
  bp.frequency.setValueAtTime(f0, t);
  bp.frequency.exponentialRampToValueAtTime(Math.max(40, f1), t + dur);
  const g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(gain, t + dur * peak);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  const p = AC.createStereoPanner();
  p.pan.setValueAtTime(p0, t); p.pan.linearRampToValueAtTime(p1, t + dur);
  s.connect(bp); bp.connect(g); g.connect(p); p.connect(sfxBus);
  const sg = AC.createGain(); sg.gain.value = 0.2; p.connect(sg); sg.connect(sfxRoom);
  s.start(t); s.stop(t + dur + 0.06);
  // The pitched component is OFF by default. A sine tracking a frequency sweep is a slide
  // whistle, which is the single thing that makes a synthesised material sound cartoon.
  if (o.tone === true) {
    const osc = AC.createOscillator(); osc.type = "sine";
    osc.frequency.setValueAtTime(f0 * 0.5, t);
    osc.frequency.exponentialRampToValueAtTime(Math.max(30, f1 * 0.5), t + dur);
    const tg = AC.createGain();
    tg.gain.setValueAtTime(0.0001, t);
    tg.gain.linearRampToValueAtTime(gain * 0.3, t + dur * peak);
    tg.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(tg); tg.connect(p);
    osc.start(t); osc.stop(t + dur + 0.06);
  }
  return g;
}

export function bell(t, o) {
  o = o || {};
  modal(t, "brass", { pitch: (o.f || 1240) / 1240, gain: o.gain == null ? 0.34 : o.gain, pan: o.pan || 0, send: 0.45 });
}
