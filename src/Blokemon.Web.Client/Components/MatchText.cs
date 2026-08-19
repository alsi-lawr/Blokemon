using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// The engine's own vocabulary is not the game's. Everything the battle surfaces print passes
// through here, so the match page and each of its presenters say the same words for the same
// thing whichever of them happens to be printing it.
public static class MatchText
{
    // A decision the match poses has no name of its own that means anything at the table: the
    // engine's word for it describes its own machinery rather than the game. What the player is
    // about to be asked is the question it carries, which is the same wording the choice step
    // prints, so that is what names it wherever the decision itself has to be shown.
    public static string PosedHeading(MatchActionView action) =>
        action.ChoiceRequirements.Length > 0
            ? PublicText(action.ChoiceRequirements[0].Label)
            : "Required";

    public static string PublicZone(string zone) =>
        zone switch
        {
            "Stack" => "Deck",
            "Mitt" => "Hand",
            "Oche" => "Active Blokemon",
            "Booth" => "Bench",
            "BarChits" => "Prize cards",
            "Dustbin" => "Discard pile",
            "LostProperty" => "Lost zone",
            _ => PublicText(zone),
        };

    public static string PublicText(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\b(Bar Chits|Bar Chit|Stack|Mitt|Oche|Booth|Vim|round)\b",
            static match =>
                match.Value.ToLowerInvariant() switch
                {
                    "bar chits" => "Prize cards",
                    "bar chit" => "Prize card",
                    "stack" => "Deck",
                    "mitt" => "Hand",
                    "oche" => "Active Blokemon",
                    "booth" => "Bench",
                    "vim" => "Energy",
                    "round" => "turn",
                    _ => match.Value,
                },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
}
