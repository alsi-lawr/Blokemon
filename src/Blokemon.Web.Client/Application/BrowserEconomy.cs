using Blokemon.Product;

namespace Blokemon.Web.Client.Application;

public static class BrowserEconomy
{
    // A browser-local game has no application configuration file, so the build compiles in
    // the same default the server ships: the unlimited economy.
    public static EconomyRules Default { get; } = EconomyRules.Unlimited;
}
