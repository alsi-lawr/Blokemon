namespace Blokemon.Web.Identity;

/// <summary>
/// The route that exchanges a hand-off code minted for a BlokeBot-hosted tenant (BLOKEMON-151
/// implements it). This host names the route in the session policy and in the tenant
/// descriptor, so the client learns it at run time and carries no provider name (D-029).
/// </summary>
internal static class HandoffExchange
{
    /// <summary>The path relative to the client's base address, as the descriptor states it.</summary>
    public const string ClientPath = "api/session/blokebot";

    /// <summary>The route as the session policy matches it.</summary>
    public const string Route = "/" + ClientPath;
}
