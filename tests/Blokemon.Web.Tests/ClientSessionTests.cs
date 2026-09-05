using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class ClientSessionTests
{
    [Test]
    public async Task AuthorizationHandler_SendsTheBearerHeaderOnlyWhileATokenIsHeld()
    {
        var tokens = new SessionTokenStore();
        var server = new RecordingServer();
        using var http = new HttpClient(
            new SessionAuthorizationHandler(tokens) { InnerHandler = server }
        )
        {
            BaseAddress = new Uri("https://blokemon.test/"),
        };
        var api = new BlokemonApiClient(http);

        await api.State();
        tokens.Token = "held.token";
        await api.State();
        tokens.Token = null;
        await api.State();

        server
            .Requests.Select(static request => request.Authorization)
            .ShouldBe([null, "Bearer held.token", null]);
    }

    [Test]
    public async Task SessionHolder_KeepsTheSessionInStorageAcrossAReloadAndDropsItOnDiscardOrExpiry()
    {
        var storage = new BrowserStorage();
        var tokens = new SessionTokenStore();
        var time = new FixedTime(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var holder = new SessionHolder(new ScriptedJs(storage), tokens, time);
        var issued = new IssuedSessionView("first.secret", time.Now.AddHours(8), "Alex");

        await holder.Establish(issued);
        storage.Stored!.Token.ShouldBe("first.secret");
        tokens.Token.ShouldBe("first.secret");

        // The next page load reads the copy back.
        var reloaded = new SessionHolder(new ScriptedJs(storage), tokens, time);
        (await reloaded.Load()).ShouldBe(new HeldSession("first.secret", issued.ExpiresAt, "Alex"));

        await reloaded.Discard();
        storage.Stored.ShouldBeNull();
        tokens.Token.ShouldBeNull();
        reloaded.Current.ShouldBeNull();

        // A copy past its expiry is dropped unread rather than presented.
        await holder.Establish(issued);
        time.Now = issued.ExpiresAt;
        var expired = new SessionHolder(new ScriptedJs(storage), tokens, time);
        (await expired.Load()).ShouldBeNull();
        storage.Stored.ShouldBeNull();
        tokens.Token.ShouldBeNull();
    }

    [Test]
    public async Task SignInFlow_ClearsTheFragmentBeforeTheExchangeAndKeepsTheTokenOutOfTheUrl()
    {
        var world = new ClientWorld { FragmentCode = "handoff-code" };
        world.Server.Exchange = _ => new IssuedSessionView(
            "minted.secret",
            DateTimeOffset.UtcNow.AddHours(8),
            "Alex"
        );

        var signedIn = await world.Flow.EnterRoot();

        signedIn.ShouldBeTrue();
        world.Flow.Stage.ShouldBe(SignInStage.SignedIn);
        var exchange = world.Server.Requests.Single(static request =>
            request.Path == "/" + HandoffPath
        );
        exchange.FragmentClearedWhenSent.ShouldBeTrue();
        exchange.Body.ShouldContain("handoff-code");
        world.Holder.Current!.Token.ShouldBe("minted.secret");
        world.Storage.Stored!.Token.ShouldBe("minted.secret");
        world.Modes.Selected.ShouldBe(PlayMode.ServerBacked);
        world.Refresher.Refreshes.ShouldBe(1);
        world.Navigation.Visited.ShouldAllBe(static uri => !uri.Contains("minted.secret"));
        world.Server.Requests.ShouldAllBe(static request =>
            !request.Path.Contains("minted.secret")
        );
    }

    [Test]
    public async Task SignInFlow_ReportsAnAbsentExchangeAsTheTypedUnavailableOutcome()
    {
        var world = new ClientWorld { FragmentCode = "code" };

        var signedIn = await world.Flow.EnterRoot();

        signedIn.ShouldBeFalse();
        world.Flow.Stage.ShouldBe(SignInStage.Failed);
        world.Flow.Error!.Code.ShouldBe("unavailable");
        world.Holder.Current.ShouldBeNull();
        world.Modes.Selected.ShouldBeNull();
    }

    [Test]
    public async Task SignInFlow_WithoutAFragmentDoesNothingAtTheRoot()
    {
        var world = new ClientWorld();

        (await world.Flow.EnterRoot()).ShouldBeFalse();

        world.Flow.Stage.ShouldBe(SignInStage.Idle);
        world.Server.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task HostedFlow_BindsTheReceiverOnlyAfterTheDescriptorThenWaitsForTheParent()
    {
        var world = new ClientWorld();
        world.Server.Tenants["core"] = new TenantDescriptorView(
            "id",
            "core",
            "Blokemon",
            [],
            "https://parent.example",
            null,
            HandoffPath
        );
        world.Server.Exchange = _ => new IssuedSessionView(
            "hosted.secret",
            DateTimeOffset.UtcNow.AddHours(8),
            "Alex"
        );

        await world.Flow.EnterTenant("core");

        world.Flow.Stage.ShouldBe(SignInStage.Waiting);
        world.Js.Receiver.ShouldNotBeNull();
        world.Js.Receiver.BoundTo.ShouldBe("https://parent.example");
        world.Js.Receiver.BoundAfterRequest.ShouldBe("/api/tenant/core");
        world.Frame.IsBound.ShouldBeTrue();

        await world.Js.Receiver.Deliver("parent-code");

        world.Flow.Stage.ShouldBe(SignInStage.SignedIn);
        world.Server.Requests.Last().Path.ShouldBe("/" + HandoffPath);
        world.Holder.Current!.Token.ShouldBe("hosted.secret");
    }

    [Test]
    public async Task HostedFlow_NeverBindsForAnUnknownTenantAndBindsToNothingWithoutAnOrigin()
    {
        var unknown = new ClientWorld();
        await unknown.Flow.EnterTenant("nobody");
        unknown.Flow.Stage.ShouldBe(SignInStage.Failed);
        unknown.Flow.Error!.Code.ShouldBe("tenant.not_found");
        unknown.Js.Receiver!.BoundTo.ShouldBeNull();
        unknown.Js.Receiver.BindCalls.ShouldBe(0);

        var originless = new ClientWorld();
        originless.Server.Tenants["core"] = new(
            "id",
            "core",
            "Blokemon",
            [],
            null,
            null,
            HandoffPath
        );
        await originless.Flow.EnterTenant("core");
        originless.Flow.Stage.ShouldBe(SignInStage.Waiting);
        originless.Js.Receiver!.BindCalls.ShouldBe(1);
        originless.Frame.IsBound.ShouldBeFalse();
    }

    [Test]
    public async Task Continuation_ExchangesAtTheResumeRouteAndRefusesALinkWithoutACode()
    {
        var world = new ClientWorld { FragmentCode = "continuation" };
        world.Server.Tenants["core"] = new("id", "core", "Blokemon", [], null, null, HandoffPath);
        world.Server.Exchange = _ => new IssuedSessionView(
            "resumed.secret",
            DateTimeOffset.UtcNow.AddHours(8),
            "Alex"
        );

        await world.Flow.EnterContinuation("core");
        world.Flow.Stage.ShouldBe(SignInStage.SignedIn);
        world.Server.Requests.Last().Path.ShouldBe("/api/session/resume");
        world.Server.Requests.Last().FragmentClearedWhenSent.ShouldBeTrue();

        var bare = new ClientWorld();
        bare.Server.Tenants["core"] = new("id", "core", "Blokemon", [], null, null, HandoffPath);
        await bare.Flow.EnterContinuation("core");
        bare.Flow.Stage.ShouldBe(SignInStage.Failed);
        bare.Flow.Error!.Code.ShouldBe("handoff.missing");
    }

    [Test]
    public async Task Reauthentication_DiscardsTheSessionThenAsksTheParentWhenHostedOrGoesToSignIn()
    {
        var standalone = new ClientWorld();
        await standalone.Holder.Establish(
            new("old.secret", DateTimeOffset.UtcNow.AddHours(1), "Alex")
        );
        await standalone.Reauthentication.Reauthenticate(
            ReauthenticationReason.Expired,
            CancellationToken.None
        );
        standalone.Holder.Current.ShouldBeNull();
        standalone.Storage.Stored.ShouldBeNull();
        standalone.Navigation.Visited.Last().ShouldEndWith("/signin?reason=expired");

        var hosted = new ClientWorld();
        hosted.Server.Tenants["core"] = new(
            "id",
            "core",
            "Blokemon",
            [],
            "https://parent.example",
            null,
            HandoffPath
        );
        await hosted.Flow.EnterTenant("core");
        await hosted.Holder.Establish(new("old.secret", DateTimeOffset.UtcNow.AddHours(1), "Alex"));
        var visitedBefore = hosted.Navigation.Visited.Count;
        await hosted.Reauthentication.Reauthenticate(
            ReauthenticationReason.Required,
            CancellationToken.None
        );
        hosted.Holder.Current.ShouldBeNull();
        hosted.Js.Receiver!.Posted.ShouldBe(["blokemon.reauth"]);
        hosted.Navigation.Visited.Count.ShouldBe(visitedBefore);
    }

    // ---- The client's world, faked at its edges ------------------------------------------------

    // The route the fake server's descriptor names; the client learns it there and nowhere else.
    private const string HandoffPath = "api/session/handoff-exchange";

    private sealed class ClientWorld
    {
        public ClientWorld()
        {
            Js = new ScriptedJs(Storage) { FragmentCode = () => FragmentCode };
            var tokens = new SessionTokenStore();
            Server = new RecordingServer { FragmentCleared = () => Js.FragmentCleared };
            Server.Tenants["core"] = new("id", "core", "Blokemon", [], null, null, HandoffPath);
            Js.LastServerRequest = () => Server.Requests.LastOrDefault()?.Path;
            var http = new HttpClient(
                new SessionAuthorizationHandler(tokens) { InnerHandler = Server }
            )
            {
                BaseAddress = new Uri("https://blokemon.test/"),
            };
            var api = new SessionApiClient(http);
            Holder = new SessionHolder(Js, tokens, new FixedTime(DateTimeOffset.UtcNow));
            Frame = new HostedFrame(Js);
            Navigation = new BrowserNavigation();
            Flow = new SignInFlow(
                api,
                Holder,
                new TenantContext(api),
                Frame,
                Modes,
                Refresher,
                Navigation,
                Js
            );
            Reauthentication = new ClientReauthentication(Holder, Frame, Navigation);
        }

        public string? FragmentCode { get; init; }

        public BrowserStorage Storage { get; } = new();

        public ScriptedJs Js { get; }

        public RecordingServer Server { get; }

        public SessionHolder Holder { get; }

        public HostedFrame Frame { get; }

        public BrowserNavigation Navigation { get; }

        public FakeModes Modes { get; } = new();

        public FakeRefresher Refresher { get; } = new();

        public SignInFlow Flow { get; }

        public ClientReauthentication Reauthentication { get; }
    }

    private sealed class BrowserStorage
    {
        public StoredSession? Stored { get; set; }
    }

    private sealed record StoredSession(
        string Token,
        DateTimeOffset? ExpiresAt,
        string? DisplayName
    );

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordedRequest
    {
        public required string Path { get; init; }

        public string? Authorization { get; init; }

        public string Body { get; init; } = string.Empty;

        public bool FragmentClearedWhenSent { get; init; }
    }

    private sealed class RecordingServer : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public Dictionary<string, TenantDescriptorView> Tenants { get; } =
            new(StringComparer.Ordinal);

        public Func<string, IssuedSessionView?>? Exchange { get; set; }

        public Func<bool> FragmentCleared { get; init; } = static () => false;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new()
                {
                    Path = path,
                    Authorization = request.Headers.Authorization?.ToString(),
                    Body = body,
                    FragmentClearedWhenSent = FragmentCleared(),
                }
            );

            if (path == "/api/state")
            {
                return Json(new ApiResponse<ApplicationView?>(true, null, null));
            }

            if (path.StartsWith("/api/tenant/", StringComparison.Ordinal))
            {
                var slug = path["/api/tenant/".Length..];
                return Tenants.TryGetValue(slug, out var tenant)
                    ? Json(new ApiResponse<TenantDescriptorView>(true, tenant, null))
                    : Json(
                        new ApiResponse<TenantDescriptorView>(
                            false,
                            null,
                            new("tenant.not_found", "That channel is not on this server.")
                        )
                    );
            }

            if (path is "/" + HandoffPath or "/api/session/resume" && Exchange is not null)
            {
                var code = JsonSerializer
                    .Deserialize<SessionExchangeRequest>(body, JsonSerializerOptions.Web)!
                    .Code;
                return Json(new ApiResponse<IssuedSessionView>(true, Exchange(code), null));
            }

            // A route that is not on this server.
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class FakeModes : IPlayModeOperations
    {
        public PlayMode? Selected { get; private set; }

        public Task<PlayModeState> Mode(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlayModeState(Selected, null, null, true));

        public Task<ApiResponse<PlayModeState>> SelectMode(
            PlayMode mode,
            CancellationToken cancellationToken = default
        )
        {
            Selected = mode;
            return Task.FromResult(
                new ApiResponse<PlayModeState>(
                    true,
                    new PlayModeState(mode, null, null, true),
                    null
                )
            );
        }
    }

    private sealed class FakeRefresher : IApplicationStateRefresher
    {
        public int Refreshes { get; private set; }

        public Task<ApiResponse<ApplicationView>> Refresh(
            CancellationToken cancellationToken = default
        )
        {
            Refreshes++;
            return Task.FromResult(new ApiResponse<ApplicationView>(true, null, null));
        }
    }

    private sealed class BrowserNavigation : NavigationManager
    {
        public BrowserNavigation() =>
            Initialize("https://blokemon.test/", "https://blokemon.test/");

        public List<string> Visited { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            Visited.Add(Uri);
        }
    }

    /// <summary>The two JS modules the client imports, scripted: storage, the fragment and the receiver.</summary>
    private sealed class ScriptedJs(BrowserStorage storage) : IJSRuntime
    {
        public Func<string?> FragmentCode { get; init; } = static () => null;

        public bool FragmentCleared { get; private set; }

        public FakeReceiver? Receiver { get; private set; }

        public Func<string?> LastServerRequest { get; set; } = static () => null;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            identifier.ShouldBe("import");
            IJSObjectReference module = (string)args![0]! switch
            {
                "./sessionHolder.js" => new HolderModule(storage),
                "./signIn.js" => new SignInModule(this),
                var other => throw new NotSupportedException(other),
            };
            return ValueTask.FromResult((TValue)module);
        }

        private sealed class HolderModule(BrowserStorage storage) : IJSObjectReference
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public ValueTask<TValue> InvokeAsync<TValue>(
                string identifier,
                CancellationToken cancellationToken,
                object?[]? args
            )
            {
                switch (identifier)
                {
                    case "read":
                        // The client's private record is reached the way the browser would: as
                        // JSON of the same shape.
                        var stored = storage.Stored is null
                            ? default
                            : JsonSerializer.Deserialize<TValue>(
                                JsonSerializer.Serialize(storage.Stored, JsonSerializerOptions.Web),
                                JsonSerializerOptions.Web
                            );
                        return ValueTask.FromResult(stored!);
                    case "write":
                        storage.Stored = new(
                            (string)args![0]!,
                            (DateTimeOffset?)args[1],
                            (string?)args[2]
                        );
                        return ValueTask.FromResult(default(TValue)!);
                    case "clear":
                        storage.Stored = null;
                        return ValueTask.FromResult(default(TValue)!);
                    default:
                        throw new NotSupportedException(identifier);
                }
            }
        }

        private sealed class SignInModule(ScriptedJs owner) : IJSObjectReference
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public ValueTask<TValue> InvokeAsync<TValue>(
                string identifier,
                CancellationToken cancellationToken,
                object?[]? args
            )
            {
                switch (identifier)
                {
                    case "readHandoffCode":
                        var code = owner.FragmentCode();
                        if (code is not null)
                        {
                            owner.FragmentCleared = true;
                        }
                        return ValueTask.FromResult((TValue)(object?)code!);
                    case "attachReceiver":
                        owner.Receiver = new FakeReceiver(
                            (DotNetObjectReference<HostedFrame>)args![0]!,
                            owner
                        );
                        return ValueTask.FromResult((TValue)(object)owner.Receiver);
                    default:
                        throw new NotSupportedException(identifier);
                }
            }
        }
    }

    private sealed class FakeReceiver(DotNetObjectReference<HostedFrame> frame, ScriptedJs owner)
        : IJSObjectReference
    {
        public string? BoundTo { get; private set; }

        public int BindCalls { get; private set; }

        public string? BoundAfterRequest { get; private set; }

        public List<string> Posted { get; } = [];

        public Task Deliver(string code) => frame.Value.ReceiveHandoff(code);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        )
        {
            switch (identifier)
            {
                case "bind":
                    BindCalls++;
                    BoundTo = (string?)args![0];
                    BoundAfterRequest = owner.LastServerRequest();
                    return ValueTask.FromResult((TValue)(object)(BoundTo is not null));
                case "post":
                    var bound = BoundTo is not null;
                    if (bound)
                    {
                        Posted.Add((string)args![0]!);
                    }
                    return ValueTask.FromResult((TValue)(object)bound);
                case "detach":
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new NotSupportedException(identifier);
            }
        }
    }
}
