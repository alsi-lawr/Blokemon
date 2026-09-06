namespace Blokemon.Web.Client.Components;

// The names the table gives its places for the browser's hit test. A drop target carries one as
// its data-drop attribute; the browser reads it off the element under the pointer and hands it
// back, and the page turns it into the place the drop means. Nothing about the table is known in
// the browser: the names are the whole of the agreement.
public static class MatchDropPlaces
{
    // A card the table shows, named by its data-card attribute beside this.
    public const string Card = "card";

    public const string Bench = "bench";

    public const string Active = "active";

    public const string InPlay = "in-play";

    public const string Empties = "empties";
}
