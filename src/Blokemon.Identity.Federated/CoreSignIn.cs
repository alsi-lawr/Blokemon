namespace Blokemon.Identity.Federated;

/// <summary>
/// The default tenant's issuer is the Blokemon plugin on the operator's own BlokeBot
/// (BLOKEMON-D-043); its sign-in page is the core sign-in, offered as "Sign in with Twitch"
/// when <c>Blokemon:Identity:Providers:BlokeBot:CoreSignInUrl</c> is configured. The federation
/// is where those names live; the application tier and the client carry neither.
/// </summary>
public static class CoreSignIn
{
    public const string ProviderName = BlokeBotProvider.ProviderName;

    public const string Label = "Sign in with Twitch";
}
