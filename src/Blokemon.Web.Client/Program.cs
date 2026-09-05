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
        // Every API call carries the held session as a bearer header; the store is filled by
        // the session holder once the host has started.
        var tokens = new SessionTokenStore();
        var http = new HttpClient(
            new SessionAuthorizationHandler(tokens) { InnerHandler = new HttpClientHandler() }
        )
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        };
        var bootstrap = await http.GetStringAsync("content/catalogue.json");
#if BLOKEMON_STANDALONE_BROWSER
        var playModes = new PlayModeAvailability(serverBacked: false);
#else
        var playModes = new PlayModeAvailability(serverBacked: true);
#endif
#if BLOKEMON_PROJECTION_EVIDENCE
        await builder.Services.AddProjectionEvidence(http, tokens, bootstrap);
#else
        builder.Services.AddBlokemonClient(
            http,
            BlokemonCatalogue.FromBootstrapJson(bootstrap),
            playModes,
            EconomyConfiguration.Resolve(builder.Configuration),
            tokens
        );
#endif

#if BLOKEMON_STANDALONE_BROWSER
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
#endif

        var host = builder.Build();
        // The sessionStorage copy is read before the first page asks the server for anything,
        // so a reload arrives signed in.
        await host.Services.GetRequiredService<SessionHolder>().Load();
        await host.RunAsync();
    }
}
