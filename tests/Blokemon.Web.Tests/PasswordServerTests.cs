using System.Net.Http.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Identity;
using Blokemon.Web.Tests.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Blokemon.Web.Tests;

/// <summary>
/// The first-party simple login over HTTP (BLOKEMON-163): a host with the provider enabled and
/// no relying party offers the login alone; one with the relying party offers both, and an
/// account made with either credential takes the other.
/// </summary>
public sealed class PasswordServerTests
{
    private const string Prefix = "/api/session/firstparty";
    private const string Origin = "http://localhost";
    private const string RpId = "localhost";
    private const string Password = "correct horse battery";

    private static SessionHost LoginHost() =>
        SessionHost.Create(builder =>
            builder.UseSetting(IdentityConfigurationModule.providerEnabledKey("FirstParty"), "true")
        );

    private static SessionHost PasskeyHost() =>
        SessionHost.Create(builder =>
        {
            builder.UseSetting(
                IdentityConfigurationModule.providerEnabledKey("FirstParty"),
                "true"
            );
            builder.UseSetting(IdentityConfigurationModule.PasskeysRelyingPartyIdKey, RpId);
            builder.UseSetting($"{IdentityConfigurationModule.PasskeysOriginsKey}:0", Origin);
        });

    [Test]
    public async Task Registration_ThenSignInFromAnotherClient_ReachesTheSameAccountWithTenCodes()
    {
        await using var host = LoginHost();

        var registered = await Register(host, "  Alex_1  ", Password);
        var account = await PasskeyServerTests.AccountOf(host, registered.Session.Token);
        var signedIn = await SignIn(host, "alex_1", Password);

        registered.Session.DisplayName.ShouldBe("Alex_1");
        registered.Session.Recovery.ShouldBeFalse();
        registered.RecoveryCodes.Length.ShouldBe(10);
        registered.RecoveryCodes.Distinct().Count().ShouldBe(10);
        signedIn.Succeeded.ShouldBeTrue(signedIn.Error?.Message);
        signedIn.Value!.DisplayName.ShouldBe("Alex_1");
        (await PasskeyServerTests.AccountOf(host, signedIn.Value.Token)).ShouldBe(account);
        var keys = (await host.WithStore(store => store.List("")))
            .Select(static s => s.Key)
            .ToList();
        keys.ShouldContain($"account/{account}");
        keys.ShouldContain($"link/firstparty/{account}");
        keys.ShouldContain($"a/{account}/profile");
        keys.ShouldContain(RecoveryCodes.key(account));
        keys.ShouldContain(Logins.key(account));
        keys.Count(static key => key.StartsWith("loginname/", StringComparison.Ordinal))
            .ShouldBe(1);
        keys.Count(static key => key.StartsWith("credential/", StringComparison.Ordinal))
            .ShouldBe(0);
        var login = await host.WithStore(store => store.Read(Logins.key(account)));
        login!.Json.ShouldNotContain(Password);
        var session = await host.WithStore(store =>
            Sessions.validate(store, signedIn.Value.Token, DateTimeOffset.UtcNow, default)
        );
        ((SessionValidation.Valid)session).Item.Provenance.ShouldBe(SessionProvenance.FirstParty);

        // The provider stands without a relying party: listed, and its ceremonies unavailable.
        using var client = host.Client();
        var descriptor = await client.GetFromJsonAsync<ApiResponse<TenantDescriptorView>>(
            $"/api/tenant/{Tenants.DefaultSlug.Value}"
        );
        descriptor!.Value!.EnabledProviders.ShouldContain("firstparty");
        descriptor.Value.Passkeys.ShouldBeFalse();
        (
            await PasskeyServerTests.Post<PasskeyOptionsView>(
                client,
                $"{Prefix}/authenticate/options",
                new { }
            )
        ).Error!.Code.ShouldBe("passkey.unavailable");
        (
            await PasskeyServerTests.Post<PasskeyOptionsView>(
                client,
                $"{Prefix}/register/options",
                new PasskeyRegisterOptionsRequest("x")
            )
        ).Error!.Code.ShouldBe("passkey.unavailable");
    }

    [Test]
    public async Task Registration_RefusesATakenNameInAnyCaseAndBadInputsAndMutatesNothing()
    {
        await using var host = LoginHost();
        await Register(host, "Taken", Password);
        var before = await host.WithStore(store => store.List(""));
        using var client = host.Client();

        var cases = new (string Name, string Password, string Code)[]
        {
            ("TAKEN", Password, "login.taken"),
            ("taken", "another password", "login.taken"),
            ("ab", Password, "login.name"),
            ("no spaces", Password, "login.name"),
            (new string('a', 33), Password, "login.name"),
            ("Fine", "short", "login.password"),
            ("Fine", "", "login.password"),
            ("Fine", new string('x', 129), "login.password"),
        };
        foreach (var (name, password, code) in cases)
        {
            var refused = await PasskeyServerTests.Post<AccountRegistrationView>(
                client,
                $"{Prefix}/password/register",
                new PasswordRegistrationRequest(name, password)
            );
            refused.Succeeded.ShouldBeFalse($"{name} / {password.Length} chars");
            refused.Error!.Code.ShouldBe(code, $"{name} / {password.Length} chars");
        }

        (await host.WithStore(store => store.List(""))).ShouldBe(before);
    }

    [Test]
    public async Task SignIn_RefusesWrongPasswordAndUnknownNameAlike_ThenLocksTheNameAndTheClient()
    {
        await using var host = LoginHost();
        await Register(host, "Locked", Password);
        await Register(host, "Other", Password);
        var now = DateTimeOffset.UtcNow;

        // The name lock-out on its own: five failures against Other from anywhere lock Other
        // for everyone and nobody else, whatever the client's own record.
        var lockouts = host.Factory.Services.GetRequiredService<ClientLockouts>();
        for (var failure = 0; failure < 5; failure++)
        {
            lockouts.LoginName.RecordFailure("other", now);
        }
        (await SignIn(host, "OTHER", Password)).Error!.Code.ShouldBe("login.locked");
        (await SignIn(host, "Locked", Password)).Succeeded.ShouldBeTrue();
        var before = await host.WithStore(store => store.List(""));

        // A wrong password and an unknown name are refused alike; every test client is the
        // same address to the host, so five refusals lock the client.
        var wrong = await SignIn(host, "Locked", "wrong password");
        var unknown = await SignIn(host, "Nobody", Password);
        wrong.Error!.Code.ShouldBe("login.refused");
        unknown.Error!.Code.ShouldBe("login.refused");
        unknown.Error.Message.ShouldBe(wrong.Error.Message);
        for (var attempt = 3; attempt <= 5; attempt++)
        {
            (await SignIn(host, "Locked", "wrong password")).Error!.Code.ShouldBe(
                "login.refused",
                $"attempt {attempt}"
            );
        }

        (await SignIn(host, "Locked", Password)).Error!.Code.ShouldBe("login.locked");
        lockouts.LoginName.IsLockedOut("locked", now).ShouldBeFalse();
        lockouts.LoginName.IsLockedOut("other", now).ShouldBeTrue();
        (await host.WithStore(store => store.List(""))).ShouldBe(before);
    }

    [Test]
    public async Task APasskeyAccountSetsAPasswordAndSignsInWithItElsewhere_AndAPasswordAccountAddsAPasskey()
    {
        await using var host = PasskeyHost();
        using var authenticator = new SoftwareAuthenticator(Origin, RpId);
        var byPasskey = await PasskeyServerTests.Register(host, authenticator, "Alex");
        var account = await PasskeyServerTests.AccountOf(host, byPasskey.Session.Token);
        using var client = host.Client(byPasskey.Session.Token);

        var state = await client.GetFromJsonAsync<ApiResponse<PasskeyStateView>>(
            $"{Prefix}/credentials"
        );
        state!.Value!.LoginName.ShouldBeNull();
        state.Value.CanSetPassword.ShouldBeTrue();

        // A name is needed the first time; the account has a credential, so no codes come.
        var unnamed = await Set(client, null, Password);
        unnamed.Error!.Code.ShouldBe("login.name");
        var named = await Set(client, "Alex", Password);
        named.Succeeded.ShouldBeTrue(named.Error?.Message);
        named.Value!.LoginName.ShouldBe("Alex");
        named.Value.RecoveryCodes.ShouldBeNull();
        (
            await client.GetFromJsonAsync<ApiResponse<PasskeyStateView>>($"{Prefix}/credentials")
        )!.Value!.LoginName.ShouldBe("Alex");
        var elsewhere = await SignIn(host, "alex", Password);
        elsewhere.Succeeded.ShouldBeTrue(elsewhere.Error?.Message);
        (await PasskeyServerTests.AccountOf(host, elsewhere.Value!.Token)).ShouldBe(account);

        // The name stays; the password changes.
        (await Set(client, "Someone", "another password")).Error!.Code.ShouldBe("login.named");
        (await Set(client, null, "another password")).Succeeded.ShouldBeTrue();
        (await SignIn(host, "Alex", Password)).Error!.Code.ShouldBe("login.refused");
        (await SignIn(host, "Alex", "another password")).Succeeded.ShouldBeTrue();
        (await host.WithStore(store => store.List("loginname/"))).Count.ShouldBe(1);

        // The other way round: an account made with a password adds a passkey, and no codes
        // come with it either.
        using var second = new SoftwareAuthenticator(Origin, RpId);
        var byPassword = await Register(host, "Pw_Player", Password);
        var enrolled = await PasskeyServerTests.Enrol(host, byPassword.Session.Token, second);
        enrolled.Succeeded.ShouldBeTrue(enrolled.Error?.Message);
        enrolled.Value!.RecoveryCodes.ShouldBeNull();
        using var passwordClient = host.Client(byPassword.Session.Token);
        var both = await passwordClient.GetFromJsonAsync<ApiResponse<PasskeyStateView>>(
            $"{Prefix}/credentials"
        );
        both!.Value!.LoginName.ShouldBe("Pw_Player");
        both.Value.Passkeys.Length.ShouldBe(1);
        var withPasskey = await PasskeyServerTests.Authenticate(host, second);
        (await PasskeyServerTests.AccountOf(host, withPasskey.Value!.Token)).ShouldBe(
            await PasskeyServerTests.AccountOf(host, byPassword.Session.Token)
        );
    }

    [Test]
    public async Task Recovery_ReplacesThePasswordEndsTheSessionAndIssuesNewCodes()
    {
        await using var host = LoginHost();
        var registered = await Register(host, "Recover", Password);
        var account = await PasskeyServerTests.AccountOf(host, registered.Session.Token);
        using var anonymous = host.Client();

        var recovered = await PasskeyServerTests.Post<IssuedSessionView>(
            anonymous,
            $"{Prefix}/recover",
            new RecoveryRequest(registered.RecoveryCodes[0])
        );
        recovered.Succeeded.ShouldBeTrue(recovered.Error?.Message);
        recovered.Value!.Recovery.ShouldBeTrue();
        using var recovering = host.Client(recovered.Value.Token);
        (
            await recovering.GetFromJsonAsync<ApiResponse<PasskeyStateView>>(
                $"{Prefix}/credentials"
            )
        )!.Error!.Code.ShouldBe(SessionFailures.RecoveryCode);

        // The replacement keeps the name: a new password, ten new codes, and the recovery
        // session is spent.
        var replaced = await Set(recovering, null, "a new password");
        replaced.Succeeded.ShouldBeTrue(replaced.Error?.Message);
        replaced.Value!.LoginName.ShouldBe("Recover");
        replaced.Value.RecoveryCodes!.Length.ShouldBe(10);
        replaced.Value.RecoveryCodes.ShouldNotContain(registered.RecoveryCodes[1]);
        (await Set(recovering, null, "yet another")).Error!.Code.ShouldBe(
            SessionFailures.RequiredCode
        );
        (await SignIn(host, "Recover", Password)).Error!.Code.ShouldBe("login.refused");
        var fresh = await SignIn(host, "Recover", "a new password");
        fresh.Succeeded.ShouldBeTrue(fresh.Error?.Message);
        (await PasskeyServerTests.AccountOf(host, fresh.Value!.Token)).ShouldBe(account);
        (
            await PasskeyServerTests.Post<IssuedSessionView>(
                anonymous,
                $"{Prefix}/recover",
                new RecoveryRequest(registered.RecoveryCodes[1])
            )
        ).Error!.Code.ShouldBe("recovery.refused");
        (await host.WithStore(store => store.List("recovery/"))).Count.ShouldBe(1);
    }

    [Test]
    public async Task AnIssuerSession_SetsTheFirstPasswordOnAnAccountWithNoCredentialAndNoOther()
    {
        await using var host = LoginHost();
        var viewer = await host.SignIn("viewer-9", "Viewer", SessionProvenance.Issuer);
        using var client = host.Client(viewer.Token);

        var first = await Set(client, "Viewer_9", Password);
        first.Succeeded.ShouldBeTrue(first.Error?.Message);
        first.Value!.RecoveryCodes!.Length.ShouldBe(10);
        (await SignIn(host, "viewer_9", Password)).Succeeded.ShouldBeTrue();

        // With a credential on the account, the channel's session may not touch it again.
        (await Set(client, null, "another password")).Error!.Code.ShouldBe("passkey.provenance");
        (await SignIn(host, "viewer_9", "another password")).Error!.Code.ShouldBe("login.refused");
    }

    [Test]
    public async Task WithoutTheProvider_TheLoginRoutesAnswerUnavailable()
    {
        await using var host = SessionHost.Create();
        using var client = host.Client();

        (
            await PasskeyServerTests.Post<AccountRegistrationView>(
                client,
                $"{Prefix}/password/register",
                new PasswordRegistrationRequest("Alex", Password)
            )
        ).Error!.Code.ShouldBe("login.unavailable");
        (
            await PasskeyServerTests.Post<IssuedSessionView>(
                client,
                $"{Prefix}/password",
                new PasswordSignInRequest("Alex", Password)
            )
        ).Error!.Code.ShouldBe("login.unavailable");
        (await host.WithStore(store => store.List("login"))).ShouldBeEmpty();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static async Task<AccountRegistrationView> Register(
        SessionHost host,
        string name,
        string password
    )
    {
        using var client = host.Client();
        var registered = await PasskeyServerTests.Post<AccountRegistrationView>(
            client,
            $"{Prefix}/password/register",
            new PasswordRegistrationRequest(name, password)
        );
        registered.Succeeded.ShouldBeTrue(registered.Error?.Message);
        return registered.Value!;
    }

    private static async Task<ApiResponse<IssuedSessionView>> SignIn(
        SessionHost host,
        string name,
        string password,
        string? slug = null
    )
    {
        using var client = host.Client();
        return await PasskeyServerTests.Post<IssuedSessionView>(
            client,
            $"{Prefix}/password",
            new PasswordSignInRequest(name, password, slug)
        );
    }

    private static Task<ApiResponse<PasswordSetView>> Set(
        HttpClient client,
        string? name,
        string password
    ) =>
        PasskeyServerTests.Post<PasswordSetView>(
            client,
            $"{Prefix}/password/set",
            new PasswordSetRequest(name, password)
        );
}
