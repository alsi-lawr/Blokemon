// What each material is, as numbers.
//
// Only things that genuinely resonate get a mode table. A table has twelve modes and rings for
// about 400 ms; a box has ten and is gone inside 175 ms, because corrugated walls flex and dump
// their energy; brass rings and is the only thing here meant to sound like a note. Card and
// pulpboard have no entry at all - a defined frequency turns a beer mat into thin wood and turns a
// run of cards into a xylophone, so those are built from noise in foley.js.
//
// Modes are [frequency Hz, amplitude, decay seconds]. The decay is the part that matters: an
// engine that could not express it made every material a click that differed only in brightness.

/* ---- materials ----
   A real impact is a BROADBAND NOISY TRANSIENT coloured by the body it happens to. So the sound
   is noise, and the material is a bank of resonant filters the noise is heard through — plus
   some of the raw strike passed straight to the output, because contact is what you hear first.

   What separates the materials is the excitation (how long the contact lasts and how bright it
   is) and the resonances (where the body rings and how tightly). Q is kept moderate: crank it up
   and the filters ring like sine oscillators, which stops sounding like an object being hit.
   Only brass genuinely rings, and only brass is built from tuned partials.                     */
export const MATERIAL = {
  // Thin stiff sheet: a very short bright contact with high papery colour.
  card:      { exc: { dur: 0.013, lo: 700,  hi: 9000 },  dry: 0.42,
               modes: [[1760, 11, 1], [2760, 14, 0.6], [4180, 16, 0.3]] },
  // Laminate foil: shorter and brighter still.
  foil:      { exc: { dur: 0.006, lo: 1600, hi: 13000 }, dry: 0.5,
               modes: [[3000, 12, 1], [4450, 14, 0.55], [6300, 15, 0.25]] },
  // Pulpboard beer mat: soft, dull, quick. Nothing above the low mids survives.
  mat:       { exc: { dur: 0.020, lo: 200,  hi: 3000 },  dry: 0.34,
               modes: [[610, 9, 1], [1015, 11, 0.5], [1580, 12, 0.2]] },
  // Corrugated box: longer contact, hollow, boxy.
  cardboard: { exc: { dur: 0.028, lo: 120,  hi: 2400 },  dry: 0.32,
               modes: [[395, 8, 1], [690, 10, 0.55], [1030, 12, 0.28]] },
  // The table underneath everything.
  wood:      { exc: { dur: 0.036, lo: 70,   hi: 1800 },  dry: 0.30,
               modes: [[205, 9, 1], [430, 11, 0.5], [700, 12, 0.22]] },
  // The one thing here that actually rings, so the one thing built from partials.
  brass:     { tonal: true, f: 1240,
               p: [[1,1,2.1],[2.02,0.52,1.5],[2.97,0.36,1.1],[4.14,0.22,0.8],[5.41,0.15,0.6],[6.83,0.10,0.45]],
               exc: { f: 2700, q: 0.8, dur: 0.003, g: 0.40 } }
};

/* Struck bodies. [frequency Hz, amplitude, decay seconds] per mode; decay is what the old engine
   had no way to express at all. Modes are inharmonic and there are enough of them to beat.       */
export const STRUCK = {
  /* A heavy solid table knocked with a knuckle. Modal analysis of wood panels puts a monopole
     fundamental near 198 Hz with dipoles at 250-450 and further modes at 620 and 930; quality
     wood runs Q 30-100, i.e. decays measured in hundreds of milliseconds, not the ~2 ms a Q-11
     biquad gave. A pub table is heavier and more damped than a soundboard, so these sit at the
     short end of that range — but they are still fifty times longer than what was here before,
     and that difference is the whole of the difference between a click and a solid table. */
  wood: {
    rough: 0.30, detune: 0.03,
    // tau = Q/(pi*f) with Q falling from about 50 in the low modes to 20 in the top ones, which
    // is where a heavy damped table sits. The decays first written here worked out at Q 174 —
    // finer than concert tonewood, and it rang like a plank rather than a pub table.
    modes: [[92, 0.45, 0.110], [148, 0.80, 0.097], [198, 1.00, 0.080], [264, 0.62, 0.054],
            [331, 0.48, 0.038], [372, 0.40, 0.033], [455, 0.30, 0.024], [620, 0.26, 0.0154],
            [793, 0.18, 0.0112], [930, 0.15, 0.0086], [1240, 0.10, 0.0056], [1610, 0.07, 0.0040]],
    grain: { gain: 0.42, tau: 0.075, colour: 0.34 },
    contact: { dur: 0.030, lo: 220, hi: 5200, gain: 0.55 },
    eq: [["highpass", 62, 0.7], ["peaking", 210, 1.1, 3], ["lowpass", 6500, 0.7]]
  },

  /* An empty corrugated box. The documented failure here was mine: a sine falling from 1.7f to
     0.6f is a TIMPANI — a tensioned membrane whose pitch is the point. A box has no pitch to
     glide. It has cavity modes, panel modes that flex and dump their energy fast, and a broad
     contact. Corrugated card is measured to absorb hardest around 125 Hz, so the box is scooped
     there rather than boomy, which is the other half of why the old one read as a drum. */
  cardboard: {
    rough: 0.42, detune: 0.05,
    // Halved from the first attempt, which was too resonant: cardboard is floppy and dumps its
    // energy through the flexing walls, so the modes are barely allowed to establish before they
    // are gone, and the contact and the grain carry most of the sound.
    modes: [[95, 0.50, 0.040], [190, 0.30, 0.030], [310, 0.60, 0.034], [395, 0.85, 0.032],
            [480, 0.50, 0.026], [560, 0.34, 0.022], [690, 0.26, 0.018], [936, 0.20, 0.014],
            [1050, 0.15, 0.012], [1380, 0.10, 0.009]],
    grain: { gain: 1.05, tau: 0.045, colour: 0.40 },
    contact: { dur: 0.06, lo: 90, hi: 7000, gain: 0.95 },
    eq: [["highpass", 70, 0.7], ["peaking", 125, 1.0, -7], ["peaking", 430, 0.9, 4], ["lowpass", 7500, 0.7]]
  },

  /* Pulpboard and card USED to have entries here, and they are gone deliberately. Neither one
     rings, and giving them modes gave them a pitch — which is what turned a beer mat into thin
     wood and turned every run of cards into a xylophone. They are built from noise instead, in
     matSlap() and cardTap(). Only things that genuinely resonate get a mode table. */
};

/* Crinkling textures: [seconds] gesture length, events per second, event length range, burst size
   and the exponent of the energy distribution. Higher tail = more small events, fewer big ones. */
// The nominal pitch of a material is its lowest mode: friction is noise heard THROUGH the thing
// being rubbed, so scuff() needs one frequency to hang a resonance on, and this is the honest
// answer for a material described by a mode table rather than a second number to keep in step.
for (const name in MATERIAL) {
  if (!MATERIAL[name].f) {
    MATERIAL[name].f = MATERIAL[name].modes[0][0];
  }
}

export const CRINKLE = {
  /* Crumpling paper peaks around 2 kHz. Rustling card also has documented low-mid bands at 170
     and 390 Hz and a sliding component above 3.15 kHz, so a card is not purely a top-end sound. */
  card:  { dur: 0.26, density: 380,  evMin: 0.0012, evMax: 0.0070, burst: 5, alpha: 1.5,
           gapMax: 0.055,
           eq: [["highpass", 240, 0.7], ["peaking", 385, 1.1, 4], ["peaking", 2000, 0.9, 5],
                ["lowpass", 9500, 0.7]] },
  /* Foil is rigid and non-porous: it reflects above 1-2 kHz where paper absorbs, and leaks its
     bass away below 500 Hz. Denser and shorter events than paper, and much brighter. */
  foil:  { dur: 0.46, density: 950,  evMin: 0.0005, evMax: 0.0032, burst: 9, alpha: 1.45,
           gapMax: 0.045,
           eq: [["highpass", 900, 0.7], ["peaking", 520, 1.0, -8], ["peaking", 4800, 0.8, 6],
                ["lowpass", 14000, 0.7]] },
  kraft: { dur: 0.36, density: 520,  evMin: 0.0012, evMax: 0.0075, burst: 6, alpha: 1.55,
           gapMax: 0.07,
           eq: [["highpass", 170, 0.7], ["peaking", 700, 0.9, 5], ["lowpass", 5200, 0.7]] }
};
