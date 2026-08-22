using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;

namespace Blokemon.Web.Client.Components;

// Which sound belongs to a cue on the table.
//
// The decision is made here, in C#, rather than in the browser, for one reason: the cue kinds are
// an enum, and a switch over them is checked by the compiler. A kind added to the contract without
// a sound is a build failure rather than a silent gap that nobody notices until they happen to be
// listening for it. The browser is handed a name and never asked to guess.
//
// Two of these are worth reading twice.
//
// Attack and Damage are separate sounds because the blow lands on the wrong beat otherwise.
// Contact is 658 ms into attack-lunge, and the Attack beat is held for exactly 658 ms, so the
// impact belongs to the FIRST FRAME OF DAMAGE. Put it on Attack and the hit arrives before the
// card does.
//
// Other is silent on purpose. A sound on every event is how a table turns into noise, and a cue
// the presentation has nothing particular to say about is exactly the one to say nothing about.
public static class MatchCueSound
{
    /// <summary>What the table sounds like while this cue is on screen, if anything.</summary>
    /// <param name="cue">The cue being played.</param>
    /// <param name="pace">The share of full speed the table is playing it at.</param>
    /// <param name="lastPrize">Whether a prize being taken is the one that wins the game.</param>
    /// <returns>The sound to play, or <c>null</c> where the cue is deliberately silent.</returns>
    // The suppression is for a value cast in from outside the names the enum declares, which the
    // application does not hand back. Every name it does declare is answered below, and leaving
    // the catch-all off is the point: it is what makes a new kind a build failure.
#pragma warning disable CS8524
    public static SoundCue? For(MatchEventCueView cue, double pace, bool lastPrize) =>
        cue.Kind switch
        {
            MatchAnimationKindView.Setup => new("setup"),
            MatchAnimationKindView.Shuffle => new("riffle", Pace: pace),
            MatchAnimationKindView.Draw => new("draw", Pace: pace),
            MatchAnimationKindView.Play => new("play"),
            MatchAnimationKindView.Attach => new("attach"),
            MatchAnimationKindView.Evolve => new("evolve"),
            MatchAnimationKindView.Attack => new("attack"),
            MatchAnimationKindView.Damage => new("damage"),
            MatchAnimationKindView.Heal => new("heal"),
            MatchAnimationKindView.Condition => new("condition"),
            MatchAnimationKindView.Knockout => new("knockout"),
            MatchAnimationKindView.Prize => new("prize", Last: lastPrize),
            MatchAnimationKindView.Turn => new("turn"),
            MatchAnimationKindView.Coin => new("coin", Badge: cue.BadgeSide ?? true),
            MatchAnimationKindView.Victory => new("victory"),
            MatchAnimationKindView.Reveal => new("reveal"),
            MatchAnimationKindView.Other => null,
        };
#pragma warning restore CS8524
}
