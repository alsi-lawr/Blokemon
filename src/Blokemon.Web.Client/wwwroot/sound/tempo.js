// The two clocks the music is written against.
//
// A beat is the unit every voice takes its length in, so the conversion lives here rather than in
// either arrangement: the instruments need it to know how long to hold a note, and the themes need
// it to know where to put one. Putting it in the themes made the instruments import the
// arrangement they were being played by, which is the wrong way round.

// Menu music: eight bars in F.
export const BPM = 88;
export const SPB = 60 / BPM;
export const BAR = 4 * SPB;

// Battle music: its relative minor, half again as fast.
export const B_BPM = 152;
export const B_SPB = 60 / B_BPM;
export const B_BAR = 4 * B_SPB;
