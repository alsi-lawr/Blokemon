// What the table sounds like while a cue is on screen.
//
// The offsets are the stylesheet's own waypoints, read from the keyframes rather than estimated:
// draw-arc runs 0.86s and settles over its last tenth, play-travel runs 1s and the slot reacts at
// 70%, attack-lunge runs 1.4s and makes contact at 47% of it.
//
// That last one is the reason Attack and Damage are separate here. Contact is 658 ms into the
// lunge, and the Attack beat is held for exactly that long - so the blow belongs on the FIRST
// FRAME of Damage, not on the cue that threw it. Playing it on Attack lands the hit 658 ms before
// the card arrives, and the whole thing falls apart.

import { bezier, unease } from "./easing.js";
import {
  cardTap, cardSlide, deckTap, matSlap, matFlap, punch, scuff, whoosh, bell,
} from "./foley.js";
import { duck } from "./transport.js";
import { runParts } from "./ceremonyCues.js";

// draw-arc 0.86s: the card is at the hand by 90% and has stopped moving by 100%.
export function draw(t) {
  runParts(t, [
    { at: 0, play: (t) => whoosh(t, { dur: 0.42, gain: 0.15, f0: 380, f1: 1500, q: 0.7, p0: -0.15, p1: 0 }) },
    { at: 774, play: (t) => cardTap(t, { gain: 0.2, dur: 0.016, centre: 2800, surface: "felt" }) },
    { at: 860, play: (t) => cardTap(t, { gain: 0.075, dur: 0.011, centre: 2200, lp: 6500 }) },
  ]);
}

// hand-play 0.52s lifts the card out of the hand; play-travel 1s carries it; slot-land says the
// slot is reacting at 70%, which is where the card actually arrives.
export function play(t) {
  runParts(t, [
    { at: 0, play: (t) => cardSlide(t, { dur: 0.18, gain: 0.13, gloss: true, rate: 60, flicks: 2, pan: -0.2 }) },
    { at: 120, play: (t) => whoosh(t, { dur: 0.55, gain: 0.11, f0: 420, f1: 1250, q: 0.8, p0: -0.3, p1: 0.1 }) },
    { at: 700, play: (t) => cardTap(t, { gain: 0.32, dur: 0.02, centre: 2900, surface: "felt" }) },
  ]);
}

// Something slid underneath a card that is already standing there, so it is friction first and a
// much lighter contact than a card being played.
export function attach(t) {
  runParts(t, [
    { at: 0, play: (t) => cardSlide(t, { dur: 0.26, gain: 0.15, gloss: true, rate: 52, flicks: 3, pan: -0.15, pan1: 0.1 }) },
    { at: 700, play: (t) => cardTap(t, { gain: 0.2, dur: 0.014, centre: 3400, lp: 8000, surface: "felt" }) },
  ]);
}

// A card laid squarely on top of another, which is heavier than an attachment and lands flush
// rather than sliding under: the stack is thicker afterwards and it sounds like it.
export function evolve(t) {
  runParts(t, [
    { at: 0, play: (t) => cardSlide(t, { dur: 0.22, gain: 0.14, gloss: true, rate: 58, flicks: 3, pan: 0.15 }) },
    { at: 120, play: (t) => whoosh(t, { dur: 0.5, gain: 0.09, f0: 460, f1: 1150, q: 0.8, p0: 0.2, p1: -0.1 }) },
    {
      at: 700,
      play: (t) => {
        cardTap(t, { gain: 0.34, dur: 0.022, centre: 2600, surface: "felt" });
        deckTap(t + 0.012, { gain: 0.22 });
      },
    },
  ]);
}

// attack-lunge 1.4s: the card draws back at 29% and is thrown at 47%. Nothing lands here - the
// blow that lands belongs to Damage.
export function attack(t) {
  runParts(t, [
    { at: 0, play: (t) => scuff(t + 0.02, { mat: "card", dur: 0.18, gain: 0.11, centre: 1500, rate: 55, pan: -0.1 }) },
    { at: 406, play: (t) => whoosh(t, { dur: 0.25, gain: 0.17, f0: 340, f1: 1150, q: 1.4, peak: 0.85, p0: -0.25, p1: 0.15 }) },
  ]);
}

// The first frame of Damage is the frame the blow connects on. counter-pop runs 0.55s from here,
// which is the counter turning over on the card that took it.
export function damage(t) {
  duck(0.5, 0.22);
  runParts(t, [
    { at: 0, play: (t) => punch(t, { gain: 0.92 }) },
    { at: 60, play: (t) => scuff(t, { mat: "card", dur: 0.22, gain: 0.1, centre: 1250, rate: 65, pan: 0.2 }) },
    {
      at: 50,
      play: (t) => {
        cardSlide(t, { dur: 0.16, gain: 0.055, gloss: false, rate: 80, flicks: 1, pan: 0.25 });
        cardTap(t + 0.02, { gain: 0.075, dur: 0.008, centre: 1900, lp: 5000, pan: 0.3 });
      },
    },
  ]);
}

// Counters coming off rather than going on: the same small dry movement as damage, without the
// blow in front of it and with the air going the other way.
export function heal(t) {
  runParts(t, [
    { at: 0, play: (t) => whoosh(t, { dur: 0.4, gain: 0.07, f0: 900, f1: 1400, q: 0.9, peak: 0.4 }) },
    {
      at: 80,
      play: (t) => {
        cardSlide(t, { dur: 0.2, gain: 0.07, gloss: false, rate: 70, flicks: 2, pan: -0.2 });
        cardTap(t + 0.03, { gain: 0.08, dur: 0.009, centre: 2100, lp: 5200 });
      },
    },
  ]);
}

// A status settling onto a card. Darker and duller than anything else on the table, because it is
// the one thing here that is bad news without being violent.
export function condition(t) {
  runParts(t, [
    { at: 0, play: (t) => whoosh(t, { dur: 0.5, gain: 0.09, f0: 700, f1: 380, q: 0.9, peak: 0.35 }) },
    { at: 90, play: (t) => matSlap(t, { gain: 0.26, dur: 0.05, lp: 1500 }) },
  ]);
}

// knockout 0.85s. The blow that does it is heavier than any other on the table, and then the card
// goes over and off it.
export function knockout(t) {
  duck(0.34, 0.5);
  runParts(t, [
    {
      at: 0,
      play: (t) => {
        punch(t, { heavy: true, gain: 1.05 });
        whoosh(t + 0.09, { dur: 0.4, gain: 0.09, f0: 1050, f1: 480, q: 1.6, peak: 0.32 });
      },
    },
    {
      at: 400,
      play: (t) => {
        cardTap(t, { gain: 0.26, dur: 0.02, centre: 2300, lp: 7000, pan: -0.16 });
        deckTap(t + 0.03, { gain: 0.34 });
      },
    },
  ]);
}

// prize-take 0.6s. A prize is a good thing happening, so it is the brightest card sound here -
// and when it is the last one, the bell goes with it.
export function prize(t, { last = false } = {}) {
  runParts(t, [
    { at: 0, play: (t) => cardSlide(t, { dur: 0.3, gain: 0.16, gloss: true, rate: 48, flicks: 4, pan: 0.2, pan1: -0.1 }) },
    { at: 420, play: (t) => cardTap(t, { gain: 0.3, dur: 0.018, centre: 3600, surface: "felt" }) },
    ...(last ? [{ at: 470, play: (t) => bell(t, { f: 1240, gain: 0.2 }) }] : []),
  ]);
}

// The table changing hands. A beer mat put down is what a pub table does to mark a turn, and it
// stays quiet: this fires every turn of every game and must never become the thing you hear.
export function turn(t) {
  runParts(t, [{ at: 0, play: (t) => matSlap(t, { gain: 0.22, dur: 0.04, lp: 1900 }) }]);
}

// The table being set: hands dealt out, then both Decks squared.
export function setup(t) {
  runParts(t, [
    { at: 0, play: (t) => cardSlide(t, { dur: 0.6, gain: 0.18, gloss: false, rate: 40, flicks: 10, pan: -0.35, pan1: 0.35, skew: 0.8 }) },
    { at: 640, play: (t) => deckTap(t, { gain: 0.4, pan: -0.25 }) },
    { at: 790, play: (t) => deckTap(t, { gain: 0.4, pan: 0.25 }) },
  ]);
}

// Last orders.
export function victory(t) {
  duck(0.22, 1.2);
  runParts(t, [
    { at: 0, play: (t) => bell(t, { f: 1240, gain: 0.34 }) },
    { at: 330, play: (t) => bell(t, { f: 1240, gain: 0.3 }) },
  ]);
}

// card-flip 0.75s: the card is edge-on at exactly 50%, presenting no face at all.
//
// The design sheet proposed grading the sheen by rarity - a common nearly silent, a Big Hitter
// ringing - and the demo does exactly that. It is deliberately NOT here: the game has no rarity,
// the stylesheet applies face-sheen to every revealed card alike, and a graded sound driven by
// nothing would be an option no caller could ever set. When rarity exists, this is where it goes.
export function reveal(t) {
  const sheenAt = Math.round((0.4 + 0.75) * 1000);
  runParts(t, [
    {
      at: 0,
      play: (t) => {
        const air = whoosh(t, {
          dur: 0.36, gain: 0.14, f0: 900, f1: 2400, q: 1.1, peak: 0.25, p0: -0.2, p1: 0,
        });
        air.gain.linearRampToValueAtTime(0.0001, t + 0.375);
      },
    },
    {
      at: 375,
      play: (t) =>
        whoosh(t, { dur: 0.38, gain: 0.14, f0: 2400, f1: 700, q: 1.1, peak: 0.7, p0: 0, p1: 0.2 }),
    },
    { at: 750, play: (t) => cardTap(t, { gain: 0.3, dur: 0.018, centre: 2900, surface: "felt" }) },
    // face-sheen 1.1s delayed 0.4s: air across the printed surface, and nearly nothing at all
    {
      at: sheenAt,
      play: (t) =>
        whoosh(t, {
          dur: 0.75, gain: 0.028, f0: 2600, f1: 5200, q: 1.2, peak: 0.5, p0: -0.3, p1: 0.3,
        }),
    },
  ]);
}

// chip-rise 1.4s: apex at 42%, lands at 82%. A flat mat chops twice per turn, so each chop is a
// half-turn of chip-flip solved back through its easing - which is what makes them decelerate the
// way the mat does. Badge is 1800 degrees and blank is 1980, so the blank side gets one more chop
// and you can hear which way it went before it lands.
const CHIP_EASE = bezier(0.18, 0.72, 0.24, 1);

export function coin(t, { badge = true } = {}) {
  const spinEnd = 1.4 * 0.82;
  const chops = badge ? 1800 / 180 : 1980 / 180;
  runParts(t, [
    {
      at: 0,
      play: (t) => {
        matSlap(t, { gain: 0.3, dur: 0.03, lp: 3000 });
        matFlap(t + 0.012, { gain: 0.07 });
      },
    },
    {
      at: 0,
      play: (t) => {
        for (let k = 1; k <= chops; k++) {
          const y = k / chops;
          matFlap(t + spinEnd * unease(CHIP_EASE, y), {
            gain: 0.085 * (1 - 0.4 * y) * (0.85 + Math.random() * 0.3),
            dur: 0.03 + Math.random() * 0.016,
            pan: (k % 2 ? 0.24 : -0.24) * (1 - y * 0.6),
          });
        }
      },
    },
    { at: 1148, play: (t) => matSlap(t, { gain: 0.6 }) },
  ]);
}
