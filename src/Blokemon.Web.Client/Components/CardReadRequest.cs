using Microsoft.AspNetCore.Components;

namespace Blokemon.Web.Client.Components;

// A card the player has asked to read, and the surface they asked from.
//
// The pointer holds a card up and puts it down again, so it never leaves the card it started on and
// has nothing to be handed back to. A reading control does: activating it takes focus off the table
// and into the viewer, and the only place focus can honestly return to afterwards is the card the
// reading control belongs to. That element travels with the request rather than being looked up
// later, because by the time the viewer closes the control that opened it may no longer be showing.
public readonly record struct CardReadRequest(string CardInstanceId, ElementReference Card);
