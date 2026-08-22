// The two arrangements.
//
// Menu music is eight bars in F at 88bpm - Fmaj7, Dm7, Bbmaj7, C7, Am7, Dm7, Gm7, C7 - on piano,
// bass and a reedy pad. Battle music is its relative minor at 152bpm, sixteen bars with a driving
// eighth-note bass, hammered offbeat stabs, a stomp-and-clap kit and a melody that climbs through
// the second eight. The A7 turnaround leans on its C sharp so the loop pulls back to the top
// rather than stopping and restarting.

import { SPB, B_SPB } from "./tempo.js";
import { mid2f, piano, bassNote, pad, kick, snare, hat } from "./instruments.js";



// F A C E · D A C F · Bb F A D · C G Bb E · A E G C · D A C F · G D F Bb · C G Bb E
const CHORDS = [[53,57,60,64],[50,57,60,65],[46,53,57,62],[48,55,58,64],
                [45,52,55,60],[50,57,60,65],[43,50,53,58],[48,55,58,64]];

const BASS = [[41,48],[38,45],[46,41],[48,43],[45,40],[38,45],[43,50],[48,43]];

// [beat, midi, beats held]
const MELODY = [
  [[1,69,1],[2,72,1]],
  [[0,77,2],[2,76,1],[3,74,1]],
  [[0,72,2],[2,74,1],[3,77,1]],
  [[0,76,3]],
  [[0,72,1],[1,76,1],[2,79,2]],
  [[0,77,2],[2,69,1],[3,72,1]],
  [[0,74,2],[2,70,1],[3,74,1]],
  [[0,72,4]]
];

// [chord voicing, bass root]
const B_PROG = [
  [[62,65,69],38],[[62,65,69],38],[[58,62,65],34],[[60,64,67],36],
  [[62,65,69],38],[[62,65,69],38],[[55,58,62],43],[[57,61,64,67],45],
  [[53,57,60],41],[[60,64,67],36],[[62,65,69],38],[[62,65,69],38],
  [[58,62,65],34],[[60,64,67],36],[[62,65,69],38],[[57,61,64,67],45]
];

// [beat, midi, beats held]
const B_MEL = [
  [[2,69,0.5],[2.5,74,0.5],[3,77,1]],
  [[0,76,0.5],[0.5,74,0.5],[1,72,1],[2,74,2]],
  [[0,77,0.5],[0.5,79,0.5],[1,81,1],[2,77,1],[3,74,1]],
  [[0,76,1],[1,79,1],[2,76,2]],
  [[2,69,0.5],[2.5,74,0.5],[3,77,1]],
  [[0,76,0.5],[0.5,77,0.5],[1,81,1],[2,79,2]],
  [[0,82,1],[1,81,1],[2,79,2]],
  [[0,77,0.5],[0.5,76,0.5],[1,73,1],[2,76,2]],
  [[0,81,1],[1,84,1],[2,81,1],[3,77,1]],
  [[0,79,1],[1,76,1],[2,79,2]],
  [[0,81,1],[1,77,1],[2,74,2]],
  [[0,76,0.5],[0.5,77,0.5],[1,79,1],[2,81,2]],
  [[0,86,1],[1,84,1],[2,81,2]],
  [[0,84,1],[1,82,1],[2,79,2]],
  [[0,81,1],[1,74,1],[2,77,1],[3,81,1]],
  [[0,79,1],[1,77,1],[2,76,1],[3,73,1]]
];

export function scheduleBar(index, t0, bus) {
  const chord = CHORDS[index], bass = BASS[index];
  // rolled, the way it is played rather than pressed
  chord.forEach((n, i) => piano(t0 + i * 0.019, n, 2.4, 0.85, bus));
  chord.forEach((n, i) => piano(t0 + 2 * SPB + i * 0.012, n, 1.1, 0.42, bus));
  pad(t0, chord[0] + 12, 4, 0.9, bus);
  pad(t0, chord[2] + 12, 4, 0.7, bus);
  bassNote(t0, bass[0], 2, 1, bus);
  bassNote(t0 + 2 * SPB, bass[1], 2, 0.85, bus);
  for (const [beat, midi, held] of MELODY[index]) {
    piano(t0 + beat * SPB, midi, held, 1.05, bus);
    piano(t0 + beat * SPB + 0.004, midi + 12, held, 0.2, bus);
  }
}

export function scheduleBattleBar(index, t0, bus, tension) {
  const [voicing, root] = B_PROG[index];
  const nextRoot = B_PROG[(index + 1) % 16][1];
  const E = B_SPB / 2;

  // driving eighths, walking into the next bar on the last one
  const FIG = [0, 0, 7, 0, 12, 0, 7, null];
  for (let i = 0; i < 8; i++) {
    const step = FIG[i];
    const midi = step === null ? (nextRoot > root ? nextRoot - 1 : nextRoot + 1) : root + step;
    bassNote(t0 + i * E, midi, 0.46, i === 0 ? 1 : 0.78, bus);
  }

  // hammered, not rolled: on the beat hard, then the offbeats that give it the shove
  voicing.forEach(n => piano(t0, n, 0.4, 0.9, bus));
  for (const e of [1, 3, 6]) voicing.forEach(n => piano(t0 + e * E, n, 0.3, 0.5, bus));

  pad(t0, voicing[0] + 12, 4 * (B_SPB / SPB), 0.55, bus);

  // stomp and clap
  for (const e of [0, 3, 4]) kick(t0 + e * E, e === 0 ? 1 : 0.8, bus);
  for (const e of [2, 6]) snare(t0 + e * E, 0.9, bus);
  const div = tension ? 16 : 8;
  for (let i = 0; i < div; i++) {
    hat(t0 + i * (4 * B_SPB / div), i % (div / 4) === 0 ? 0.9 : 0.5, i === div - 2, bus);
  }
  // The extra kick enters on a bar line, which is where a rhythm layer belongs. The drone is NOT
  // scheduled here — bar scheduling meant the button took up to a bar and a lookahead to answer,
  // which reads as nothing happening. It is a continuous voice instead, started on the press.
  if (tension) kick(t0 + 6 * E, 0.75, bus);

  for (const [beat, midi, held] of B_MEL[index]) {
    piano(t0 + beat * B_SPB, midi, held * (B_SPB / SPB), 1.05, bus);
    piano(t0 + beat * B_SPB + 0.004, midi + 12, held * (B_SPB / SPB), 0.18, bus);
  }
}
