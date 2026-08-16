using System.Text.RegularExpressions;
using Blokemon.Web.Content;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed partial class ProductionStaticAssetTests
{
    [Test]
    public async Task ProductionHost_ServesEveryRenderedFrameworkAndStyleAsset()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"production-assets-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Blokemon:DataDirectory"] = dataDirectory,
                            }
                        )
                );
            });
            using var client = factory.CreateClient();
            var html = await client.GetStringAsync("/");
            var assets = RenderedAsset()
                .Matches(html)
                .Select(static match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            assets.Length.ShouldBeGreaterThanOrEqualTo(4);

            foreach (var asset in assets)
            {
                using var response = await client.GetAsync(asset);
                response.IsSuccessStatusCode.ShouldBeTrue();
                (response.Content.Headers.ContentType?.MediaType).ShouldNotBe("text/html");
            }
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task ProductionHost_ServesTheArtWarmupModuleAndTheIllustrationsItRequests()
    {
        var dataDirectory = Path.Combine(
            AppContext.BaseDirectory,
            $"production-warmup-{Guid.NewGuid():N}"
        );
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Blokemon:DataDirectory"] = dataDirectory,
                            }
                        )
                );
            });
            using var client = factory.CreateClient();
            using var module = await client.GetAsync("/artWarmup.js");
            module.IsSuccessStatusCode.ShouldBeTrue();

            var order = CardArtAssets.WarmingOrder(
                BlokemonCatalogueBuilder.Load(Path.Combine(AppContext.BaseDirectory, "content")),
                null
            );
            foreach (var illustration in new[] { order[0], order[^1] })
            {
                using var art = await client.GetAsync(illustration);
                art.IsSuccessStatusCode.ShouldBeTrue();
                (art.Content.Headers.ContentType?.MediaType).ShouldBe("image/svg+xml");
            }
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [GeneratedRegex("(?:href|src)=\"([^\"]+(?:\\.css|\\.js))\"")]
    private static partial Regex RenderedAsset();
}
