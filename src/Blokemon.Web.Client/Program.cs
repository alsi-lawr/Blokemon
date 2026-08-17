using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.Web.Client.Application;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace Blokemon.Web.Client;

public static class ClientProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
        var bootstrap = await http.GetStringAsync("content/catalogue.json");
#if BLOKEMON_STANDALONE_BROWSER
        var playModes = new PlayModeAvailability(ServerBacked: false);
#else
        var playModes = new PlayModeAvailability(ServerBacked: true);
#endif
        builder.Services.AddBlokemonClient(
            http,
            BlokemonCatalogue.FromBootstrapJson(bootstrap),
            playModes,
            EconomyConfiguration.Resolve(builder.Configuration)
        );

#if BLOKEMON_STANDALONE_BROWSER
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
#endif

        await builder.Build().RunAsync();
    }
}
