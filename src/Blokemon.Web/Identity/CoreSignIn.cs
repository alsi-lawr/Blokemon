namespace Blokemon.Web.Identity;

/// <summary>
/// The default tenant's issuer is the Blokemon plugin on the operator's own BlokeBot
/// (BLOKEMON-D-043); its sign-in page is the core sign-in, offered as "Sign in with Twitch"
/// when <c>Blokemon:Identity:Providers:BlokeBot:CoreSignInUrl</c> is configured. This host is
/// where those names live; the application tier and the client carry neither.
/// </summary>
internal static class CoreSignIn
{
    public const string ProviderName = "blokebot";

    public const string Label = "Sign in with Twitch";
}
