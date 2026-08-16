using System.Text.RegularExpressions;
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

    [GeneratedRegex("(?:href|src)=\"([^\"]+(?:\\.css|\\.js))\"")]
    private static partial Regex RenderedAsset();
}
