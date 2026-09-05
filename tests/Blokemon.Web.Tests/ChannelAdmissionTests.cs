using System.Net.Http.Json;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Identity.Federated;
using Blokemon.Product;
using Blokemon.Web.Tests.Identity;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// The operator's admission of channels and of the default tenant's core issuer, the token's
/// storage and rotation, closure, revocation and re-admission, and the channel's own closure,
/// over HTTP with the fake channel.
/// </summary>
public sealed class ChannelAdmissionTests
{
    [Test]
    public async Task AnOperator_MintsAChannelAndTheCoreIssuer_StoringOnlyTheSecretsHash()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();

        var channel = await host.Admit(
            operatorToken,
            "the-regular",
            " The Regular ",
            "1001",
            "https://parent.example/"
        );
        var core = await host.Admit(
            operatorToken,
            "core",
            "ignored",
            "2002",
            "https://bot.example"
        );

        channel.Succeeded.ShouldBeTrue(channel.Error?.Message);
        channel.Value!.Slug.ShouldBe("the-regular");
        channel.Value.Label.ShouldBe("The Regular");
        channel.Value.Status.ShouldBe("Active");
        channel.Value.Token.ShouldStartWith($"blkm_{channel.Value.Id}_");
        // 256 bits of secret is 43 base64url characters, which may themselves include '_'.
        channel
            .Value.Token[$"blkm_{channel.Value.Id}_".Length..]
            .Length.ShouldBeGreaterThanOrEqualTo(43);
        core.Succeeded.ShouldBeTrue(core.Error?.Message);
        core.Value!.Id.ShouldBe((await host.DefaultTenantId()).Value);
        core.Value.Label.ShouldBe(Tenants.DefaultLabel);

        foreach (
            var (admitted, broadcaster, origin) in new[]
            {
                (channel.Value, "1001", "https://parent.example"),
                (core.Value, "2002", "https://bot.example"),
            }
        )
        {
            var stored = (await host.WithStore(store => store.Read($"tenant/{admitted.Id}")))!;
            var document = JsonSerializer.Deserialize<TenantDocument>(stored.Json, json)!;
            document.BroadcasterSubject.ShouldBe(broadcaster);
            document.RegisteredParentOrigin.ShouldBe(origin);
            document.IntegrationTokenVerifier.ShouldNotBeNull();
            stored.Json.ShouldNotContain(admitted.Token[$"blkm_{admitted.Id}_".Length..]);
            stored.Json.ShouldNotContain("blkm_");
        }

        using var asChannel = new FakeChannel(host, channel.Value.Token);
        var self = await asChannel.Self();
        self.Succeeded.ShouldBeTrue(self.Error?.Message);
        self.Value!.Slug.ShouldBe("the-regular");
        self.Value.RegisteredParentOrigin.ShouldBe("https://parent.example");
        self.Value.EnabledProviders.ShouldContain("blokebot");
    }

    [Test]
    public async Task Admission_RefusesReservedTakenAndMalformedInputsAndNonOperators_MutatingNothing()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (_, taken) = await host.AdmitChannel(operatorToken, "taken", "Taken");
        var player = await host.SignIn("player", "Player");
        var before = await host.WithStore(store => store.List(""));

        (
            string Name,
            string Token,
            string? Slug,
            string? Label,
            string? Broadcaster,
            string? Origin,
            string Code
        )[] cases =
        [
            ("reserved slug self", operatorToken, "self", "Self", null, null, "tenant.slug"),
            (
                "reserved slug handoff",
                operatorToken,
                "handoff",
                "Handoff",
                null,
                null,
                "tenant.slug"
            ),
            ("malformed slug", operatorToken, "The Regular", "x", null, null, "tenant.slug"),
            ("taken slug", operatorToken, "taken", "Again", null, null, "tenant.slug_taken"),
            ("empty label", operatorToken, "fresh", "  ", null, null, "tenant.label"),
            (
                "bad broadcaster",
                operatorToken,
                "fresh",
                "Fresh",
                "not-digits",
                null,
                "tenant.subject"
            ),
            (
                "bad origin",
                operatorToken,
                "fresh",
                "Fresh",
                null,
                "parent.example",
                "tenant.origin"
            ),
            (
                "origin with path",
                operatorToken,
                "fresh",
                "Fresh",
                null,
                "https://parent.example/page",
                "tenant.origin"
            ),
            ("non-operator", player.Token, "fresh", "Fresh", null, null, "operator.required"),
        ];
        foreach (var (name, token, slug, label, broadcaster, origin, code) in cases)
        {
            var refused = await host.Admit(token, slug, label, broadcaster, origin);
            refused.Succeeded.ShouldBeFalse(name);
            refused.Error!.Code.ShouldBe(code, name);
        }

        foreach (var action in new[] { "rotate", "close", "revoke" })
        {
            var refused = await host.Operator<JsonElement?>(
                player.Token,
                $"/api/operator/tenants/{taken.Id}/{action}"
            );
            refused.Error!.Code.ShouldBe("operator.required", action);
        }

        (await host.WithStore(store => store.List(""))).ShouldBe(before);
    }

    [Test]
    public async Task Rotation_InvalidatesThePreviousToken()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (old, tenant) = await host.AdmitChannel(operatorToken, "rotating", "Rotating");

        var rotated = await host.Operator<AdmittedTenantView>(
            operatorToken,
            $"/api/operator/tenants/{tenant.Id}/rotate"
        );
        rotated.Succeeded.ShouldBeTrue(rotated.Error?.Message);
        rotated.Value!.Token.ShouldNotBe(old.Token!);
        using var fresh = new FakeChannel(host, rotated.Value.Token);

        (await old.Self()).Error!.Code.ShouldBe("channel.token_unknown");
        (await old.Handoff("3003")).Error!.Code.ShouldBe("channel.token_unknown");
        (await fresh.Self()).Succeeded.ShouldBeTrue();
        (await fresh.Handoff("3003")).Succeeded.ShouldBeTrue();
    }

    [Test]
    public async Task Closure_RefusesLaterCallsAndHandoffsRevokesTheTenantsSessionsAndLeavesEverythingElse()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "closing", "Closing");
        var (other, _) = await host.AdmitChannel(operatorToken, "staying", "Staying");
        var viewer = await host.Exchange(await channel.HandoffCode("4004", "Viewer"), "closing");
        viewer.Succeeded.ShouldBeTrue(viewer.Error?.Message);
        var viewerAccount = (await host.SessionOf(viewer.Value!.Token)).Account;
        var elsewhere = await host.Exchange(await other.HandoffCode("5005"), "staying");
        // A passkey holder playing in the closing channel: the session is the person's, not
        // the channel's.
        var firstParty = await host.SignIn("keeper", "Keeper", tenant: Id(tenant.Id));
        var minted = await channel.HandoffCode("4004");
        var keep = (await host.WithStore(store => store.List("")))
            .Where(static s =>
                !s.Key.StartsWith("session/", StringComparison.Ordinal)
                && !s.Key.StartsWith("tenant/", StringComparison.Ordinal)
                && !s.Key.StartsWith("handoff/", StringComparison.Ordinal)
            )
            .ToList();

        var closed = await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{tenant.Id}/close"
        );
        closed.Succeeded.ShouldBeTrue(closed.Error?.Message);
        closed.Value!.Status.ShouldBe("Closed");

        // Its token is dead, its later hand-off is refused, and a code it minted before closing
        // no longer signs anyone in.
        (await channel.Self()).Error!.Code.ShouldBe("tenant.closed");
        (await channel.Handoff("4004")).Error!.Code.ShouldBe("tenant.closed");
        (await channel.Close()).Error!.Code.ShouldBe("tenant.closed");
        (await host.Exchange(minted, "closing")).Error!.Code.ShouldBe("tenant.closed");
        // Its sessions are revoked; another tenant's are not.
        using (var revoked = host.Client(viewer.Value.Token))
        {
            (
                await revoked.GetFromJsonAsync<ApiResponse<JsonElement?>>("/api/session/approvals")
            )!.Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        }
        foreach (var token in new[] { elsewhere.Value!.Token, firstParty.Token })
        {
            using var alive = host.Client(token);
            (
                await alive.GetFromJsonAsync<ApiResponse<PendingApprovalView[]>>(
                    "/api/session/approvals"
                )
            )!.Succeeded.ShouldBeTrue();
        }
        // Accounts, links, approvals, profiles: untouched.
        (await host.WithStore(store => store.List("")))
            .Where(static s =>
                !s.Key.StartsWith("session/", StringComparison.Ordinal)
                && !s.Key.StartsWith("tenant/", StringComparison.Ordinal)
                && !s.Key.StartsWith("handoff/", StringComparison.Ordinal)
            )
            .ShouldBe(keep);
        var stored = (await host.WithStore(store => store.Read(tenantKey(Id(tenant.Id)))))!;
        JsonSerializer
            .Deserialize<TenantDocument>(stored.Json, json)!
            .IntegrationTokenVerifier.ShouldBeNull();

        // The operator's closure of a closed tenant is idempotent.
        (
            await host.Operator<TenantStatusView>(
                operatorToken,
                $"/api/operator/tenants/{tenant.Id}/close"
            )
        ).Value!.Status.ShouldBe("Closed");

        // Re-admission by a new token restores it; the old token stays dead.
        var readmitted = await host.Operator<AdmittedTenantView>(
            operatorToken,
            $"/api/operator/tenants/{tenant.Id}/rotate"
        );
        readmitted.Succeeded.ShouldBeTrue(readmitted.Error?.Message);
        readmitted.Value!.Status.ShouldBe("Active");
        using var again = new FakeChannel(host, readmitted.Value.Token);
        (await again.Self()).Succeeded.ShouldBeTrue();
        (await channel.Self()).Error!.Code.ShouldBe("channel.token_unknown");
        var back = await host.Exchange(await again.HandoffCode("4004"), "closing");
        back.Succeeded.ShouldBeTrue(back.Error?.Message);
        (await host.SessionOf(back.Value!.Token)).Account.ShouldBe(viewerAccount);
    }

    [Test]
    public async Task Revocation_HasClosuresEffectsAndIsNotReadmissible()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "hostile", "Hostile");
        var viewer = await host.Exchange(await channel.HandoffCode("6006"), "hostile");
        viewer.Succeeded.ShouldBeTrue(viewer.Error?.Message);
        var accounts = await host.WithStore(store => store.List("account/"));
        var links = await host.WithStore(store => store.List("link/"));
        var approvals = await host.WithStore(store => store.List("approval/"));

        var revoked = await host.Operator<TenantStatusView>(
            operatorToken,
            $"/api/operator/tenants/{tenant.Id}/revoke"
        );
        revoked.Value!.Status.ShouldBe("Revoked");

        (await channel.Self()).Error!.Code.ShouldBe("tenant.revoked");
        (await channel.Handoff("6006")).Error!.Code.ShouldBe("tenant.revoked");
        using (var dead = host.Client(viewer.Value!.Token))
        {
            (
                await dead.GetFromJsonAsync<ApiResponse<JsonElement?>>("/api/session/approvals")
            )!.Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        }
        (
            await host.Operator<AdmittedTenantView>(
                operatorToken,
                $"/api/operator/tenants/{tenant.Id}/rotate"
            )
        ).Error!.Code.ShouldBe("tenant.revoked");
        (
            await host.Operator<TenantStatusView>(
                operatorToken,
                $"/api/operator/tenants/{tenant.Id}/close"
            )
        ).Error!.Code.ShouldBe("tenant.revoked");
        (await host.WithStore(store => store.List("account/"))).ShouldBe(accounts);
        (await host.WithStore(store => store.List("link/"))).ShouldBe(links);
        (await host.WithStore(store => store.List("approval/"))).ShouldBe(approvals);
    }

    [Test]
    public async Task AChannel_ClosesItselfIdempotently()
    {
        await using var host = ChannelHosting.Create();
        var operatorToken = await host.OperatorToken();
        var (channel, tenant) = await host.AdmitChannel(operatorToken, "leaving", "Leaving");
        var viewer = await host.Exchange(await channel.HandoffCode("7007"), "leaving");

        var closed = await channel.Close();
        closed.Succeeded.ShouldBeTrue(closed.Error?.Message);
        closed.Value!.Status.ShouldBe("Closed");
        // The token died with the closure, so the repeat is the typed closed refusal: the
        // tenant is closed either way.
        (await channel.Close()).Error!.Code.ShouldBe("tenant.closed");
        using var dead = host.Client(viewer.Value!.Token);
        (
            await dead.GetFromJsonAsync<ApiResponse<JsonElement?>>("/api/session/approvals")
        )!.Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        var stored = (await host.WithStore(store => store.Read(tenantKey(Id(tenant.Id)))))!;
        JsonSerializer
            .Deserialize<TenantDocument>(stored.Json, json)!
            .Status.ShouldBe(TenantStatus.Closed);
    }

    [Test]
    public void OnlyTheWebHostReferencesTheFederation()
    {
        var root = RepositoryRoot();
        var projects = System
            .Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(Path.Combine(root, "Blokemon.slnx")),
                "Path=\"([^\"]+)\""
            )
            .Select(static match => match.Groups[1].Value)
            .ToList();
        projects.ShouldContain(
            "src/Blokemon.Identity.Federated/Blokemon.Identity.Federated.csproj"
        );
        var referencing = projects
            .Where(project =>
                File.ReadAllText(Path.Combine(root, project))
                    .Contains("Blokemon.Identity.Federated.csproj")
                && !project.EndsWith("Blokemon.Identity.Federated.csproj", StringComparison.Ordinal)
            )
            .ToList();
        referencing.ShouldBe(["src/Blokemon.Web/Blokemon.Web.csproj"]);
    }

    private static TenantId Id(string value) =>
        ((DomainResult<TenantId, IdentityValueFailure>.Succeeded)TenantId.Create(value)).Value;

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
