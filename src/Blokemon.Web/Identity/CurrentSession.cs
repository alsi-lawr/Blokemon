using Blokemon.App;

namespace Blokemon.Web.Identity;

/// <summary>The session the request's bearer token established, set by the session middleware.</summary>
public sealed class CurrentSession
{
    public Session? Session { get; internal set; }
}
