using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// What the band across the middle of the table says. Two facts, and neither is enough on its own:
// a phase with no owner does not say who the match is waiting for, and a turn with no phase is
// exactly the ambiguity the opening had, where picking a starting Blokemon looked like an ordinary
// turn. So the band carries both, and the phase slot is empty during ordinary play so the common
// case stays quiet.
//
// It names the SITUATION and never the action. The glowing, tappable cards are the instruction;
// what they cannot show is which situation they are being offered in, which is the one thing words
// are needed for.
//
// Both slots may be empty and the structure never changes, so the band is the same height in every
// phase and no transition moves the table. Nothing here may wrap for the same reason: the
// opponent's name is the one variable-length thing in it, and it ellipsises rather than taking a
// second line.
public sealed record MatchBand(string? Turn, string? Phase)
{
    // The opening and a replacement ask the same structural question - which Blokemon stands at
    // the Oche - at different moments, so they are asked in the same words and the concept is
    // learned once.
    private const string ChooseYourActive = "Choose your Active";

    // An effect choice and a trigger choice differ to the engine and are identical to the player:
    // in both, someone is resolving something before play goes on. Naming them apart would teach a
    // distinction that changes nothing either player can do.
    private const string Choosing = "Choosing";

    // There is no last arm on purpose. A phase added to MatchPhaseView without words decided for
    // it does not compile - CS8509 is an error across the repository - so the one thing that
    // cannot happen is a new phase quietly leaving the middle of the table blank. A last arm would
    // swallow exactly that and turn it into something only a running game could tell you about.
    // The suppression below is for the other half of the same warning: a value cast in from
    // outside the names the enum declares, which nothing here can produce.
#pragma warning disable CS8524
    public static MatchBand For(MatchFrameView frame)
    {
        var yours = frame.Player.HasTurn;
        var them = frame.Opponent.Name;

        return frame.Phase switch
        {
            MatchPhaseView.OpeningPlacement or MatchPhaseView.AwaitingReplacement => yours
                ? new(null, ChooseYourActive)
                : new(them, Choosing),
            MatchPhaseView.MulliganBonus => new(yours ? null : them, "Extra draw"),
            // Nothing to add: with no phase to name, the affordances are the whole of it.
            MatchPhaseView.Playing => new(yours ? "Your turn" : $"{them}'s turn", null),
            MatchPhaseView.AwaitingEffectChoice or MatchPhaseView.AwaitingTriggerChoice => new(
                yours ? "Your turn" : them,
                Choosing
            ),
            // The band states the outcome rather than falling silent or holding a stale turn.
            MatchPhaseView.Complete => new(Outcome(frame), null),
        };
    }
#pragma warning restore CS8524

    private static string Outcome(MatchFrameView frame) =>
        frame.Winner is not { } winner ? "Battle over"
        : string.Equals(winner, frame.Player.Name, StringComparison.Ordinal) ? "You win"
        : $"{winner} wins";
}
