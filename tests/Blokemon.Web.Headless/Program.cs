using Blokemon.Web.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace Blokemon.Web.Headless;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<HeadlessApp>("#app");
        await builder.Build().RunAsync();
    }
}
