using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Web.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Blokemon.Web.Tests;

/// <summary>
/// BLOKEMON-154's solution-wide audit, in the parts that can be a test: every project in
/// <c>Blokemon.slnx</c> is enumerated, and only <c>Blokemon.Web</c> and
/// <c>Blokemon.Identity.Federated</c> reference the federation; no provider identifier is the
/// canonical key of an account or profile; and the host starts and serves anonymously with no
/// provider enabled and no channel admitted. The type-name, member-name and literal part of the
/// audit is a source scan recorded in the evidence, not a committed banned-phrase test.
/// </summary>
public sealed partial class MilestoneAuditTests
{
    [GeneratedRegex(@"Path=""([^""]+)""")]
    private static partial Regex SolutionProject();

    [GeneratedRegex(@"Include=""([^""]+)""")]
    private static partial Regex ReferenceInclude();

    [Test]
    public async Task Startup_NeedsNoProviderAndNoChannel_AndServesAnonymously()
    {
        await using var host = SessionHost.Create(withProvider: false);
        using var client = host.Client();

        var registry = host.Factory.Services.GetRequiredService<IdentityProviderRegistry>();
        registry.Enabled.ShouldBeEmpty();
        (await host.WithStore(store => store.List("tenant/"))).Count.ShouldBe(
            1,
            "only the default tenant exists; no channel is admitted"
        );

        using var health = await client.GetAsync("/healthz");
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await client.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state");
        state!.Succeeded.ShouldBeTrue();
        state.Value!.Profile.ShouldBeNull("a signed-out visitor gets the signed-out view");
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );
        descriptor!.Value!.EnabledProviders.ShouldBeEmpty();
        descriptor.Value.CoreSignIn.ShouldBeNull();
    }

    [Test]
    public async Task NoProviderIdentifier_IsTheCanonicalKeyOfAnAccountOrProfile()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, _) = await host.AdmitChannel(operatorToken, "alpha", "Alpha");
        var signedIn = await host.Exchange(await channel.HandoffCode("4242"), "alpha");
        var account = (await host.SessionOf(signedIn.Value!.Token)).Account;

        var keys = (await host.WithStore(store => store.List("")))
            .Select(static summary => summary.Key)
            .ToList();

        // The account and its profile are keyed by the account id alone; the only key that names a
        // provider is the link, which is neither the account nor the profile.
        keys.ShouldContain($"account/{account}");
        keys.ShouldContain($"a/{account}/profile");
        foreach (var provider in new[] { "twitch", "blokebot", "firstparty" })
        {
            keys.ShouldNotContain(
                key =>
                    (
                        key.StartsWith("account/", StringComparison.Ordinal)
                        || key.StartsWith("a/", StringComparison.Ordinal)
                    ) && key.Contains(provider, StringComparison.OrdinalIgnoreCase),
                $"no account or profile key names {provider}"
            );
        }
        keys.ShouldContain("link/twitch/4242");
        channel.Dispose();
    }

    [Test]
    public void OnlyTheWebHostAndTheFederation_ReferenceTheFederation()
    {
        var root = RepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Blokemon.slnx"));
        var projects = SolutionProject()
            .Matches(solution)
            .Select(match => match.Groups[1].Value)
            .ToList();
        projects.Count.ShouldBeGreaterThan(20);

        var referencing = new List<string>();
        foreach (var relative in projects)
        {
            var projectPath = Path.Combine(
                root,
                relative.Replace('\\', Path.DirectorySeparatorChar)
            );
            var name = Path.GetFileNameWithoutExtension(projectPath);
            if (name == "Blokemon.Identity.Federated")
            {
                continue;
            }

            var directory = Path.GetDirectoryName(projectPath)!;
            var references = ReferenceInclude()
                .Matches(File.ReadAllText(projectPath))
                .Select(match => match.Groups[1].Value)
                .Where(include => include.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(include =>
                    Path.GetFileNameWithoutExtension(
                        Path.GetFullPath(
                            Path.Combine(
                                directory,
                                include.Replace('\\', Path.DirectorySeparatorChar)
                            )
                        )
                    )
                );
            if (references.Contains("Blokemon.Identity.Federated"))
            {
                referencing.Add(name);
            }
        }

        referencing.ShouldBe(["Blokemon.Web"]);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
