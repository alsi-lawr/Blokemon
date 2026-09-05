using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Web.Api;
using Blokemon.Web.Components;
using Blokemon.Web.Content;
using Blokemon.Web.Identity;
using Blokemon.Web.Identity.Passkeys;
using Blokemon.Web.Persistence;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

var contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
var catalogue = BlokemonCatalogueBuilder.Load(contentRoot);
var databasePath = LocalDataPath.Resolve(builder.Configuration);

builder.Services.AddSingleton(catalogue);
builder.Services.AddSingleton(EconomyConfiguration.Resolve(builder.Configuration));
builder.Services.AddPooledDbContextFactory<BlokemonDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}")
);
builder.Services.AddScoped<StateDocumentStore>();
builder.Services.AddScoped<IStateDocumentStore>(static provider =>
    provider.GetRequiredService<StateDocumentStore>()
);
builder.Services.AddScoped<IDocumentListing>(static provider =>
    provider.GetRequiredService<StateDocumentStore>()
);

// Server documents are keyed by account, so the application service is built per request for
// the account the session names (ServerApplications) rather than registered here.
builder.Services.AddServerIdentity(builder.Configuration);
builder.Services.AddScoped(serviceProvider => new HttpClient
{
    BaseAddress = new Uri(serviceProvider.GetRequiredService<NavigationManager>().BaseUri),
});
builder
    .Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.UseStaticFiles(
    new StaticFileOptions
    {
        // The delivered illustrations rather than the approved ones: cards ask for the WebP that
        // was derived from the approved artwork, and the artwork itself is read only when the
        // catalogue is assembled.
        FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "art-web")),
        RequestPath = "/art",
    }
);
app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "fonts")),
        RequestPath = "/fonts",
    }
);
app.UseApiSessions();
app.MapGet(
    "/healthz",
    () =>
        Results.Ok(
            new
            {
                status = "ready",
                mechanics = catalogue.Mechanics.ManifestVersion,
                content = catalogue.PublicContentVersion,
                starters = catalogue.StarterDecks.Version,
            }
        )
);
app.MapApplicationEndpoints();
app.MapIdentityEndpoints();
app.MapFirstPartyEndpoints();
app.MapApprovalEndpoints();
app.MapContinuationEndpoints();
app.MapBlokeBotChannels();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Blokemon.Web.Client._Imports).Assembly);

await using (var scope = app.Services.CreateAsyncScope())
{
    var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BlokemonDbContext>>();
    await using var database = await contexts.CreateDbContextAsync();
    await database.Database.MigrateAsync();
}

await app.StartIdentity();

app.Run();

public partial class Program;
