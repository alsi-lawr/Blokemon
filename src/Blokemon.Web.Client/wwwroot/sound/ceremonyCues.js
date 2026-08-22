// The ceremonies: a booster pack coming open, a starter deck coming out of its carton, and the
// Deck being shuffled.
//
// Every offset below is a keyframe of the stylesheet that draws the same moment, not an estimate.
// A cue is a list of parts and the millisecond each one lands on, so the two stay legible side by
// side: if the animation is retimed, the number to change here is the one with the same name.
//
// The riffle reads --cue-pace the same way cue-states.css does, so a mulligan's quarter-speed
// shuffle shortens rather than being cut off part way through.

import { AC, master, verbSend } from "./audioContext.js";
import { modal, crinkle } from "./resonance.js";
import { cardTap, cardSlide, deckTap, scuff, whoosh, thud } from "./foley.js";
import { rip, cardCascade } from "./textures.js";
import { duck } from "./transport.js";

// A cue is played by putting every part on the clock at its own offset. Nothing here waits on a
// timer: the offsets are audio-clock times, so a busy frame cannot drag one part away from another.
export function runParts(t, parts) {
  for (const part of parts) {
    part.play(t + part.at / 1000);
  }
}

// Sealed to Tearing, 2200 ms. The rip itself runs 150-750 and crosses the wrapper in three eased
// surges, because cubic-bezier(.3,0,.7,1) applies between every pair of keyframes.
export function tear(t, { kraft = false } = {}) {
  const wrap = kraft ? "card" : "foil";
  duck(0.55, 1.4);
  runParts(t, [
    // the crimp catching
    {
      at: 0,
      play: (t) => {
        if (wrap === "foil") {
          modal(t, "foil", { pitch: 0.9, gain: 0.24, decay: 1.4, pan: -0.5, send: 0.14 });
        } else {
          cardTap(t, { gain: 0.26, dur: 0.02, centre: 2400, lp: 7000, pan: -0.5 });
        }
        scuff(t + 0.02, { mat: wrap, dur: 0.09, gain: 0.12, rate: 120, pan: -0.45 });
      },
    },
    // THE RIP
    { at: 150, play: (t) => rip(t, kraft ? "kraft" : "gloss") },
    // the crimp strip peeling free and tumbling away; crimp-peel runs 150 to 1200, so the strip is
    // already moving by the time this lands
    {
      at: 620,
      play: (t) => {
        scuff(t, { mat: wrap, dur: 0.16, gain: 0.15, rate: 130, pan: 0.35 });
        whoosh(t + 0.12, { dur: 0.42, gain: 0.09, f0: 1500, f1: 1900, q: 0.8, p0: 0.3, p1: 0.9 });
      },
    },
    // the cards rising inside the wrapper; stack-rise 800 to 1550, overshoot at 62%
    {
      at: 800,
      play: (t) => {
        whoosh(t, { dur: 0.7, gain: 0.09, f0: 700, f1: 1080, q: 0.9, peak: 0.55 });
        cardSlide(t + 0.04, {
          dur: 0.52,
          gain: 0.15,
          gloss: true,
          rate: 40,
          flicks: 5,
          pan: -0.12,
          pan1: 0.12,
        });
        crinkle(t + 0.02, "foil", { dur: 0.5, gain: 0.16, send: 0.14 });
      },
    },
    // the top of the rise
    {
      at: 1265,
      play: (t) => cardSlide(t, { dur: 0.16, gain: 0.11, gloss: true, rate: 70, flicks: 2 }),
    },
    // the empty wrapper falling out of frame; pack-tear-away 72% to 100%
    {
      at: 1584,
      play: (t) => {
        for (let i = 0; i < 7; i++) {
          const u = i / 6;
          scuff(t + u * 0.5, {
            mat: wrap,
            dur: 0.07,
            gain: 0.055 * (1 - u * 0.7),
            centre: 2200 - u * 1300,
            rate: 150,
            pan: -0.2 + u * 0.5,
          });
        }
        whoosh(t, { dur: 0.6, gain: 0.05, f0: 950, f1: 520, q: 0.9, p0: 0, p1: -0.3 });
      },
    },
  ]);
}

// Boxed 950, Unboxing 900, Drawn 3250. deck-lid-off rises at 18%, drops back at 32%, rises again
// at 44%, and then flies: the lid binds twice before it comes free, and it is heard doing it.
export function deckOpening(t) {
  duck(0.5, 3.2);
  runParts(t, [
    // the carton lifting
    { at: 200, play: (t) => whoosh(t, { dur: 0.5, gain: 0.08, f0: 280, f1: 760, q: 3.4, peak: 0.6 }) },
    // the box setting down
    { at: 790, play: (t) => thud(t, { freq: 92, gain: 0.55, dur: 0.3 }) },
    // the lid binding
    { at: 1103, play: (t) => scuff(t, { dur: 0.11, gain: 0.3, centre: 700, rate: 75, pan: -0.15 }) },
    // and binding again
    { at: 1324, play: (t) => scuff(t, { dur: 0.13, gain: 0.34, centre: 620, rate: 65, pan: 0.1 }) },
    // free, and away
    {
      at: 1390,
      play: (t) => {
        scuff(t, { dur: 0.2, gain: 0.34, centre: 900, rate: 90 });
        whoosh(t + 0.11, { dur: 0.42, gain: 0.1, f0: 700, f1: 1150, q: 0.85, p0: 0, p1: 0.25 });
      },
    },
    // sixty cards out of the tray; deck-pull 1.3s from 1850, clearing the tray by 44%
    {
      at: 1850,
      play: (t) => {
        whoosh(t, { dur: 0.57, gain: 0.1, f0: 520, f1: 900, q: 0.9, peak: 0.62 });
        cardSlide(t, {
          dur: 0.56,
          gain: 0.24,
          gloss: false,
          rate: 44,
          flicks: 9,
          pan: -0.2,
          pan1: 0.15,
          skew: 0.7,
        });
      },
    },
    // the fan opening; deck-fan 1.65s from 2450, rotating from 35% to 4100 ms
    {
      at: 3028,
      play: (t) =>
        cardSlide(t, {
          dur: 1.072,
          gain: 0.17,
          gloss: true,
          rate: 30,
          flicks: 7,
          pan: -0.4,
          pan1: 0.4,
          skew: 0.65,
        }),
    },
  ]);
}

// Twelve cards, alternating piles, staggered 31 ms, then the Deck knocked square. What the twelve
// are is not twelve clicks: a riffle is a cascade of overlapping micro-transients with the packets
// rasping past each other underneath, and the twelve the table draws are raised inside it.
export function riffle(t, { pace = 1 } = {}) {
  const stagger = 0.031 * pace;
  const run = 0.5 * pace;
  const last = 11 * stagger;
  const cascadeFor = last + 0.19 * pace;
  runParts(t, [
    // the pack splitting, which is the same sliding sound a draw uses, once per packet
    {
      at: 0,
      play: (t) => {
        whoosh(t + 0.01, {
          dur: 0.42 * pace, gain: 0.1, f0: 420, f1: 1600, q: 0.7, peak: 0.5, p0: -0.5, p1: -0.28,
        });
        whoosh(t + 0.06, {
          dur: 0.4 * pace, gain: 0.1, f0: 380, f1: 1450, q: 0.7, peak: 0.55, p0: 0.5, p1: 0.28,
        });
      },
    },
    // the cascade
    {
      at: Math.round(run * 1000),
      play: (t) => {
        cardSlide(t, {
          dur: cascadeFor, gain: 0.13, gloss: false, flicks: 0, rate: 62,
          pan: -0.18, pan1: 0.18, skew: 1,
        });
        const source = AC.createBufferSource();
        source.buffer = cardCascade(cascadeFor, { density: 260, arrivals: 12, stagger });
        const hp = AC.createBiquadFilter();
        hp.type = "highpass";
        hp.frequency.value = 480;
        const friction = AC.createBiquadFilter();
        friction.type = "peaking";
        friction.frequency.value = 2500;
        friction.Q.value = 0.7;
        friction.gain.value = 3;
        const lp = AC.createBiquadFilter();
        lp.type = "lowpass";
        lp.frequency.value = 6800;
        const level = AC.createGain();
        level.gain.setValueAtTime(0.0001, t);
        level.gain.linearRampToValueAtTime(0.5, t + 0.05);
        level.gain.linearRampToValueAtTime(0.62, t + last);
        level.gain.exponentialRampToValueAtTime(0.0001, t + cascadeFor);
        source.connect(hp);
        hp.connect(friction);
        friction.connect(lp);
        lp.connect(level);
        level.connect(master);
        const send = AC.createGain();
        send.gain.value = 0.18;
        level.connect(send);
        send.connect(verbSend);
        source.start(t);
        source.stop(t + cascadeFor + 0.05);
      },
    },
    // squared on the table; deck-square-up lifts at 34% and taps down at 68%
    {
      at: Math.round(0.962 * pace * 1000),
      play: (t) => {
        scuff(t, { mat: "card", dur: 0.09 * pace, gain: 0.09, centre: 2600, rate: 70 });
        deckTap(t + 0.102 * pace, { gain: 0.5 });
      },
    },
  ]);
}
