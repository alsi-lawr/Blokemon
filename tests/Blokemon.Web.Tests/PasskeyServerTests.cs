using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using static Blokemon.App.TenancyDocuments;

namespace Blokemon.Web.Tests;

/// <summary>
/// The first-party ceremonies over HTTP against the host with the passkey relying party
/// configured, driven by a software authenticator; the provider double supplies the Issuer
/// sessions the enrolment rules are checked from.
/// </summary>
public sealed class PasskeyServerTests
{
    private const string Prefix = "/api/session/firstparty";
    private const string Origin = "http://localhost";
    private const string RpId = "localhost";

    private static SessionHost PasskeyHost(Action<IWebHostBuilder>? configure = null) =>
        SessionHost.Create(builder =>
        {
            builder.UseSetting(
                IdentityConfigurationModule.providerEnabledKey("FirstParty"),
                "true"
            );
            builder.UseSetting(IdentityConfigurationModule.PasskeysRelyingPartyIdKey, RpId);
            builder.UseSetting($"{IdentityConfigurationModule.PasskeysOriginsKey}:0", Origin);
            configure?.Invoke(builder);
        });

    [Test]
    public async Task Registration_ThenAuthentication_SignsInTheSameAccountWithTenCodesAndOneOfEachDocument()
    {
        await using var host = PasskeyHost();
        using var authenticator = new SoftwareAuthenticator(Origin, RpId);

        var registered = await Register(host, authenticator, "  Alex  ");
        var account = await AccountOf(host, registered.Session.Token);
        var signedIn = await Authenticate(host, authenticator);

        registered.Session.DisplayName.ShouldBe("Alex");
        registered.Session.Recovery.ShouldBeFalse();
        registered.RecoveryCodes.Length.ShouldBe(10);
        registered.RecoveryCodes.Distinct().Count().ShouldBe(10);
        registered.RecoveryCodes.ShouldAllBe(static code => RecoveryCodes.normalize(code) != null);
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        signedIn.Value!.DisplayName.ShouldBe("Alex");
        (await AccountOf(host, signedIn.Value.Token)).ShouldBe(account);
        var keys = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ToList();
        keys.ShouldContain($"account/{account}");
        keys.ShouldContain($"link/firstparty/{account}");
        keys.ShouldContain($"a/{account}/profile");
        keys.ShouldContain(RecoveryCodes.key(account));
        keys.Count(key => key.StartsWith(Credentials.prefix(account), StringComparison.Ordinal))
            .ShouldBe(1);
        var stored = await host.WithStore(store => store.Read(RecoveryCodes.key(account)));
        foreach (var code in registered.RecoveryCodes)
        {
            stored!.Json.ShouldNotContain(RecoveryCodes.normalize(code)!.Value);
        }
        // The provenance the session was issued with.
        var session = await host.WithStore(store =>
            Sessions.validate(store, signedIn.Value.Token, DateTimeOffset.UtcNow, default)
        );
        ((SessionValidation.Valid)session).Item.Provenance.ShouldBe(SessionProvenance.FirstParty);
    }

    [Test]
    public async Task Authentication_RefusesUnknownOtherAccountsAndReplayedCredentialsAndMutatesNothing()
    {
        await using var host = PasskeyHost();
        using var alice = new SoftwareAuthenticator(Origin, RpId);
        using var stranger = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, alice, "Alice");
        var other = await host.SignIn("other-subject", "Other");
        var before = await host.WithStore(store => store.List(""));

        // A credential the server never saw, claiming an existing account.
        var unknown = await Authenticate(host, stranger, userHandle: alice.UserHandle);
        unknown.Succeeded.ShouldBeFalse();
        unknown.Error!.Code.ShouldBe("credential.unknown");
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // Alice's credential presented as another account's.
        var mismatched = await Authenticate(
            host,
            alice,
            userHandle: PasskeyUserHandle(other.Session.Account)
        );
        mismatched.Succeeded.ShouldBeFalse();
        mismatched.Error!.Code.ShouldBe("credential.unknown");
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // No user handle at all.
        var anonymous = await Authenticate(host, alice, userHandle: []);
        anonymous.Error!.Code.ShouldBe("credential.unknown");

        // One assertion presented twice: the challenge answers once.
        using var client = host.Client();
        var options = await Options(client, $"{Prefix}/authenticate/options", new { });
        var assertion = alice.Assert(options.Options);
        var request = new PasskeyCeremonyRequest(options.Challenge, assertion);
        var first = await Post<IssuedSessionView>(client, $"{Prefix}/authenticate", request);
        var afterFirst = await host.WithStore(store => store.List(""));
        var replay = await Post<IssuedSessionView>(client, $"{Prefix}/authenticate", request);
        first.Succeeded.ShouldBeTrue();
        replay.Succeeded.ShouldBeFalse();
        replay.Error!.Code.ShouldBe("passkey.challenge");
        (await host.WithStore(store => store.List(""))).ShouldBe(afterFirst);

        // A fresh challenge answered with a counter that did not advance: a cloned key.
        var stale = await Authenticate(host, alice, advanceCounter: false);
        stale.Error!.Code.ShouldBe("passkey.refused");
        (await host.WithStore(store => store.List(""))).ShouldBe(afterFirst);

        // A response for another origin.
        var elsewhere = await Authenticate(host, alice, origin: "http://elsewhere.example");
        elsewhere.Error!.Code.ShouldBe("passkey.refused");

        registered.Session.Token.ShouldNotBeNull();
    }

    [Test]
    public async Task Registration_AlwaysCreatesANewAccountAndNeverAttachesToAnExistingOne()
    {
        await using var host = PasskeyHost();
        using var first = new SoftwareAuthenticator(Origin, RpId);
        using var second = new SoftwareAuthenticator(Origin, RpId);

        var one = await Register(host, first, "One");
        var two = await Register(host, second, "Two");

        (await AccountOf(host, one.Session.Token)).ShouldNotBe(
            await AccountOf(host, two.Session.Token)
        );
        (await host.WithStore(store => store.List("account/"))).Count.ShouldBe(2);
        (await host.WithStore(store => store.List("link/firstparty/"))).Count.ShouldBe(2);
    }

    [Test]
    public async Task Enrolment_RequiresASessionForTheAccountAndRefusesAnotherAccountsChallenge()
    {
        await using var host = PasskeyHost();
        using var aliceKey = new SoftwareAuthenticator(Origin, RpId);
        using var bobKey = new SoftwareAuthenticator(Origin, RpId);
        using var newKey = new SoftwareAuthenticator(Origin, RpId);
        var alice = await Register(host, aliceKey, "Alice");
        var bob = await Register(host, bobKey, "Bob");
        var before = await host.WithStore(store => store.List(""));

        using var anonymous = host.Client();
        var refused = await Post<PasskeyOptionsView>(anonymous, $"{Prefix}/enrol/options", new { });
        refused.Error!.Code.ShouldBe(SessionFailures.RequiredCode);

        // Alice's pending enrolment answered by Bob's session.
        using var aliceClient = host.Client(alice.Session.Token);
        using var bobClient = host.Client(bob.Session.Token);
        var options = await Options(aliceClient, $"{Prefix}/enrol/options", new { });
        var hijacked = await Post<PasskeyEnrolmentView>(
            bobClient,
            $"{Prefix}/enrol",
            new PasskeyCeremonyRequest(options.Challenge, newKey.Register(options.Options))
        );
        hijacked.Succeeded.ShouldBeFalse();
        hijacked.Error!.Code.ShouldBe("passkey.challenge");
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // And the challenge is spent: Alice cannot answer it either now.
        var spent = await Post<PasskeyEnrolmentView>(
            aliceClient,
            $"{Prefix}/enrol",
            new PasskeyCeremonyRequest(options.Challenge, newKey.Register(options.Options))
        );
        spent.Error!.Code.ShouldBe("passkey.challenge");
    }

    [Test]
    public async Task Enrolment_FollowsTheProvenanceRulesAndRecordsProvenanceAndTenant()
    {
        await using var host = PasskeyHost();
        using var ownKey = new SoftwareAuthenticator(Origin, RpId);
        using var secondKey = new SoftwareAuthenticator(Origin, RpId);
        using var channelKey = new SoftwareAuthenticator(Origin, RpId);
        using var thirdKey = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, ownKey, "Own");
        var tenant = await host.DefaultTenantId();

        // A FirstParty session adds a second passkey: no new codes.
        var added = await Enrol(host, registered.Session.Token, secondKey);
        added.Succeeded.ShouldBeTrue(added.Error?.Message);
        added.Value!.RecoveryCodes.ShouldBeNull();
        added.Value.Passkey.Provenance.ShouldBe("FirstParty");
        added.Value.Passkey.TenantLabel.ShouldBeNull();

        // An Issuer session enrols the first passkey on a credential-less account, with codes,
        // and the credential names the channel it came from.
        var channel = await host.SignIn("viewer-1", "Viewer", SessionProvenance.Issuer);
        var first = await Enrol(host, channel.Token, channelKey);
        first.Succeeded.ShouldBeTrue(first.Error?.Message);
        first.Value!.RecoveryCodes!.Length.ShouldBe(10);
        first.Value.Passkey.Provenance.ShouldBe("Issuer");
        first.Value.Passkey.TenantLabel.ShouldBe(Tenants.DefaultLabel);
        var stored = await host.WithStore(store =>
            Credentials.find(
                store,
                store,
                channel.Session.Account,
                channelKey.CredentialIdText,
                default
            )
        );
        stored!.Value.Document.Tenant.ShouldBe(tenant.Value);

        // The same Issuer session may not add a second, nor make new codes.
        var before = await host.WithStore(store => store.List(""));
        var second = await Enrol(host, channel.Token, thirdKey);
        second.Error!.Code.ShouldBe("passkey.provenance");
        using var channelClient = host.Client(channel.Token);
        var codes = await Post<RecoveryCodesView>(
            channelClient,
            $"{Prefix}/recovery-codes",
            new { }
        );
        codes.Error!.Code.ShouldBe("passkey.provenance");
        var state = await channelClient.GetFromJsonAsync<ApiResponse<PasskeyStateView>>(
            $"{Prefix}/credentials"
        );
        state!.Value!.CanAddPasskey.ShouldBeFalse();
        state.Value.CanMakeNewCodes.ShouldBeFalse();
        state.Value.Passkeys.Length.ShouldBe(1);
        state.Value.RecoveryCodesRemaining.ShouldBe(10);
        (await host.WithStore(store => store.List(""))).ShouldBe(before);

        // An Issuer session on an account with a live code set and no passkey is refused too.
        var recovering = await host.SignIn("viewer-2", "Viewer Two", SessionProvenance.Issuer);
        await host.WithStore(store =>
            RecoveryCodes.issue(store, recovering.Session.Account, DateTimeOffset.UtcNow, default)
        );
        var withCodes = await Enrol(host, recovering.Token, thirdKey);
        withCodes.Error!.Code.ShouldBe("passkey.provenance");

        // The channel-enrolled passkey signs the viewer in first-party from now on.
        var signedIn = await Authenticate(host, channelKey);
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        (await AccountOf(host, signedIn.Value!.Token)).ShouldBe(channel.Session.Account);
    }

    [Test]
    public async Task ACredentialSurvivesClosureAndRevocationOfTheTenantItWasEnrolledFrom()
    {
        await using var host = PasskeyHost();
        using var channelKey = new SoftwareAuthenticator(Origin, RpId);
        var channel = await host.SignIn("viewer-3", "Viewer", SessionProvenance.Issuer);
        var enrolled = await Enrol(host, channel.Token, channelKey);
        enrolled.Succeeded.ShouldBeTrue(enrolled.Error?.Message);
        var credentialKeys = await host.WithStore(store =>
            store.List(Credentials.prefix(channel.Session.Account))
        );

        foreach (var status in new[] { TenantStatus.Closed, TenantStatus.Revoked })
        {
            await host.WithStore(async store =>
            {
                var key = tenantKey(await host.DefaultTenantId());
                var stored = (await store.Read(key))!;
                var document = JsonNode.Parse(stored.Json)!.AsObject();
                document["status"] = status.ToString();
                await store.Update(key, stored.Revision, document.ToJsonString());
            });

            (await host.WithStore(store => store.List(Credentials.prefix(channel.Session.Account))))
                .Select(static s => s.Key)
                .ShouldBe(credentialKeys.Select(static s => s.Key));
            var signedIn = await Authenticate(host, channelKey);
            signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        }
    }

    [Test]
    public async Task Recovery_ConsumesOnceRevokesSessionsAndIssuesASessionThatCanOnlyEnrol()
    {
        await using var host = PasskeyHost();
        using var lostKey = new SoftwareAuthenticator(Origin, RpId);
        using var newKey = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, lostKey, "Recoverer");
        var account = await AccountOf(host, registered.Session.Token);
        var second = await Authenticate(host, lostKey);
        using var anonymous = host.Client();

        // Two concurrent uses of one code: exactly one succeeds.
        var code = registered.RecoveryCodes[0];
        var races = await Task.WhenAll(
            Post<IssuedSessionView>(anonymous, $"{Prefix}/recover", new RecoveryRequest(code)),
            Post<IssuedSessionView>(anonymous, $"{Prefix}/recover", new RecoveryRequest(code))
        );
        races.Count(static r => r.Succeeded).ShouldBe(1);
        races.Single(static r => !r.Succeeded).Error!.Code.ShouldBe("recovery.refused");
        var recovered = races.Single(static r => r.Succeeded).Value!;
        recovered.Recovery.ShouldBeTrue();
        recovered.DisplayName.ShouldBe("Recoverer");

        // Every earlier session of the account is revoked.
        foreach (var token in new[] { registered.Session.Token, second.Value!.Token })
        {
            using var old = host.Client(token);
            (
                await Post<ApplicationView>(
                    old,
                    "/api/profile",
                    new CreateProfileRequest(Guid.NewGuid(), "x")
                )
            ).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        }

        // The recovery session is refused everywhere but the replacement enrolment.
        using var recovering = host.Client(recovered.Token);
        (string Path, object? Body)[] elsewhere =
        [
            ("/api/profile", new CreateProfileRequest(Guid.NewGuid(), "x")),
            ("/api/packs/open", new OpenPackRequest(Guid.NewGuid())),
            ("/api/purge", new { }),
            ("/api/session/signout", new { }),
            ("/api/session/continue", new { }),
            ("/api/operator/bootstrap", new OperatorBootstrapRequest("x")),
            ($"{Prefix}/recovery-codes", new { }),
        ];
        foreach (var (path, body) in elsewhere)
        {
            (await Post<JsonElement?>(recovering, path, body!)).Error!.Code.ShouldBe(
                SessionFailures.RecoveryCode,
                path
            );
        }
        (
            await recovering.GetFromJsonAsync<ApiResponse<PasskeyStateView>>(
                $"{Prefix}/credentials"
            )
        )!.Error!.Code.ShouldBe(SessionFailures.RecoveryCode);
        // On an anonymous route it is ignored: the state is the signed-out view.
        (
            await recovering.GetFromJsonAsync<ApiResponse<ApplicationView>>("/api/state")
        )!.Value!.Profile.ShouldBeNull();

        // The one permitted operation: a replacement passkey with a new code set, after which
        // the recovery session is gone and the old codes are dead.
        var replaced = await Enrol(host, recovered.Token, newKey);
        replaced.Succeeded.ShouldBeTrue(replaced.Error?.Message);
        replaced.Value!.RecoveryCodes!.Length.ShouldBe(10);
        replaced.Value.Passkey.Provenance.ShouldBe("Recovery");
        (
            await Post<PasskeyOptionsView>(recovering, $"{Prefix}/enrol/options", new { })
        ).Error!.Code.ShouldBe(SessionFailures.RequiredCode);
        var oldCode = await Post<IssuedSessionView>(
            anonymous,
            $"{Prefix}/recover",
            new RecoveryRequest(registered.RecoveryCodes[1])
        );
        oldCode.Error!.Code.ShouldBe("recovery.refused");

        // The replacement signs in, and the lost passkey still does too: nothing removed it.
        (await AccountOf(host, (await Authenticate(host, newKey)).Value!.Token)).ShouldBe(account);
        (await Authenticate(host, lostKey)).Succeeded.ShouldBeTrue();
        (await host.WithStore(store => store.List("recovery/"))).Count.ShouldBe(1);
    }

    [Test]
    public async Task Recovery_LocksOutAfterFiveFailuresPerClientAndMutatesNothing()
    {
        await using var host = PasskeyHost();
        using var key = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, key, "Locked");
        var before = await host.WithStore(store => store.List(""));
        using var client = host.Client();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var refused = await Post<IssuedSessionView>(
                client,
                $"{Prefix}/recover",
                new RecoveryRequest(new string('0', 32))
            );
            refused.Error!.Code.ShouldBe("recovery.refused", $"attempt {attempt}");
        }

        var locked = await Post<IssuedSessionView>(
            client,
            $"{Prefix}/recover",
            new RecoveryRequest(registered.RecoveryCodes[0])
        );
        locked.Error!.Code.ShouldBe("recovery.locked");
        (await host.WithStore(store => store.List(""))).ShouldBe(before);
    }

    [Test]
    public async Task Regeneration_NeedsAFirstPartySessionAndInvalidatesThePreviousSet()
    {
        await using var host = PasskeyHost();
        using var key = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, key, "Regen");
        using var client = host.Client(registered.Session.Token);
        using var anonymous = host.Client();

        var made = await Post<RecoveryCodesView>(client, $"{Prefix}/recovery-codes", new { });
        made.Succeeded.ShouldBeTrue();
        made.Value!.Codes.Length.ShouldBe(10);
        made.Value.Codes.ShouldNotContain(registered.RecoveryCodes[0]);

        var stale = await Post<IssuedSessionView>(
            anonymous,
            $"{Prefix}/recover",
            new RecoveryRequest(registered.RecoveryCodes[0])
        );
        stale.Error!.Code.ShouldBe("recovery.refused");
        var fresh = await Post<IssuedSessionView>(
            anonymous,
            $"{Prefix}/recover",
            new RecoveryRequest(made.Value.Codes[0])
        );
        fresh.Succeeded.ShouldBeTrue(fresh.Error?.Message);
        fresh.Value!.Recovery.ShouldBeTrue();
    }

    [Test]
    public async Task AFirstPartySession_ActsInEveryTenantTheAccountIsNotExcludedFrom()
    {
        await using var host = PasskeyHost();
        using var key = new SoftwareAuthenticator(Origin, RpId);
        var registered = await Register(host, key, "Everywhere");
        var account = await AccountOf(host, registered.Session.Token);
        var channel = TenantId.Mint();
        await host.WithStore(store =>
            store.Create(
                tenantKey(channel),
                JsonSerializer.Serialize(
                    newTenant(
                        channel,
                        Value(TenantSlug.Create("the-regular")),
                        "The Regular",
                        DateTimeOffset.UtcNow
                    ),
                    json
                )
            )
        );

        var inChannel = await Authenticate(host, key, slug: "the-regular");
        inChannel.Succeeded.ShouldBeTrue(inChannel.Error?.Message);
        var session = await host.WithStore(store =>
            Sessions.validate(store, inChannel.Value!.Token, DateTimeOffset.UtcNow, default)
        );
        ((SessionValidation.Valid)session).Item.Tenant.ShouldBe(channel);

        await host.WithStore(store =>
            Approvals.exclude(store, account, channel, DateTimeOffset.UtcNow, default)
        );
        var excluded = await Authenticate(host, key, slug: "the-regular");
        excluded.Error!.Code.ShouldBe("tenant.excluded");
        (await Authenticate(host, key)).Succeeded.ShouldBeTrue();
        (await Authenticate(host, key, slug: "nobody")).Error!.Code.ShouldBe("tenant.not_found");
    }

    [Test]
    public async Task WithoutTheProvider_TheCeremonyRoutesAnswerUnavailableAndTheRegistryIsEmptyOfIt()
    {
        await using var host = SessionHost.Create();
        using var client = host.Client();

        (
            await Post<PasskeyOptionsView>(
                client,
                $"{Prefix}/register/options",
                new PasskeyRegisterOptionsRequest("x")
            )
        ).Error!.Code.ShouldBe("passkey.unavailable");
        (
            await Post<PasskeyOptionsView>(client, $"{Prefix}/authenticate/options", new { })
        ).Error!.Code.ShouldBe("passkey.unavailable");
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );
        descriptor!.Value!.EnabledProviders.ShouldNotContain("firstparty");
    }

    [Test]
    public void OnlyTheWebHostReferencesTheWebAuthnLibrary()
    {
        var root = RepositoryRoot();
        var projects = System
            .Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(Path.Combine(root, "Blokemon.slnx")),
                "Path=\"([^\"]+)\""
            )
            .Select(static match => match.Groups[1].Value)
            .ToList();
        projects.Count.ShouldBeGreaterThan(20);
        var referencing = projects
            .Where(project =>
                File.ReadAllText(Path.Combine(root, project)).Contains("Include=\"Fido2\"")
            )
            .ToList();
        referencing.ShouldBe(["src/Blokemon.Web/Blokemon.Web.csproj"]);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static async Task<PasskeyRegistrationView> Register(
        SessionHost host,
        SoftwareAuthenticator authenticator,
        string displayName
    )
    {
        using var client = host.Client();
        var options = await Options(
            client,
            $"{Prefix}/register/options",
            new PasskeyRegisterOptionsRequest(displayName)
        );
        options.Options.GetProperty("rp").GetProperty("id").GetString().ShouldBe(RpId);
        options
            .Options.GetProperty("authenticatorSelection")
            .GetProperty("residentKey")
            .GetString()
            .ShouldBe("required");
        var registered = await Post<PasskeyRegistrationView>(
            client,
            $"{Prefix}/register",
            new PasskeyCeremonyRequest(options.Challenge, authenticator.Register(options.Options))
        );
        registered.Succeeded.ShouldBeTrue(registered.Error?.Message);
        return registered.Value!;
    }

    private static async Task<ApiResponse<IssuedSessionView>> Authenticate(
        SessionHost host,
        SoftwareAuthenticator authenticator,
        string? slug = null,
        byte[]? userHandle = null,
        bool advanceCounter = true,
        string? origin = null
    )
    {
        using var client = host.Client();
        var options = await Options(client, $"{Prefix}/authenticate/options", new { });
        return await Post<IssuedSessionView>(
            client,
            $"{Prefix}/authenticate",
            new PasskeyCeremonyRequest(
                options.Challenge,
                authenticator.Assert(options.Options, userHandle, advanceCounter, origin),
                slug
            )
        );
    }

    private static async Task<ApiResponse<PasskeyEnrolmentView>> Enrol(
        SessionHost host,
        string token,
        SoftwareAuthenticator authenticator
    )
    {
        using var client = host.Client(token);
        var options = await Post<PasskeyOptionsView>(client, $"{Prefix}/enrol/options", new { });
        if (!options.Succeeded)
        {
            return new(false, null, options.Error);
        }

        return await Post<PasskeyEnrolmentView>(
            client,
            $"{Prefix}/enrol",
            new PasskeyCeremonyRequest(
                options.Value!.Challenge,
                authenticator.Register(options.Value.Options)
            )
        );
    }

    private static async Task<AccountId> AccountOf(SessionHost host, string token)
    {
        var validation = await host.WithStore(store =>
            Sessions.validate(store, token, DateTimeOffset.UtcNow, default)
        );
        return ((SessionValidation.Valid)validation).Item.Account;
    }

    private static byte[] PasskeyUserHandle(AccountId account) =>
        System.Text.Encoding.UTF8.GetBytes(account.Value);

    private static async Task<ApiResponse<T>> Post<T>(HttpClient client, string path, object body)
    {
        using var content = JsonContent.Create(body, body.GetType());
        using var response = await client.PostAsync(path, content);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!;
    }

    private static async Task<PasskeyOptionsView> Options(
        HttpClient client,
        string path,
        object body
    )
    {
        var envelope = await Post<PasskeyOptionsView>(client, path, body);
        envelope.Succeeded.ShouldBeTrue(envelope.Error?.Message);
        return envelope.Value!;
    }

    private static T Value<T, TFailure>(DomainResult<T, TFailure> result) =>
        result.Match(
            static value => value,
            static failure => throw new InvalidOperationException(failure!.ToString())
        );

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
