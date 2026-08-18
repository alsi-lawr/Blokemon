namespace Blokemon.Web.Client.Components;

// The engine's own vocabulary is not the game's. Everything the battle surfaces print passes
// through here, so the match page and each of its presenters say the same words for the same
// thing whichever of them happens to be printing it.
public static class MatchText
{
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
