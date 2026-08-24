using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Client.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class ApplicationCapabilityTests
{
    private static readonly PackStockPresentationView Stock = new(
        string.Empty,
        string.Empty,
        string.Empty
    );

    private static readonly ApplicationView Application = new(
        null,
        [],
        [],
        [],
        new(Stock, Stock),
        null,
        null,
        null
    );

    private static readonly ApiResponse<ApplicationView> ApplicationResult = new(
        true,
        Application,
        null
    );

    private static readonly ApiResponse<ApplicationView> ApplicationError = new(
        false,
        null,
        new("application-error", "The application operation failed.")
    );

    private static readonly ApiResponse<MatchMutationView> MatchResult = new(
        true,
        new(Application, null),
        null
    );

    private static readonly ApiResponse<MatchMutationView> MatchError = new(
        false,
        null,
        new("match-error", "The match operation failed.")
    );

    [Test]
    public async Task Capabilities_ForwardApplicationRequestsCancellationResultsAndErrors()
    {
        var application = new RecordingApplication();
        var capabilities = new ApplicationCapabilities(application);
        var profile = new CreateProfileRequest(Guid.NewGuid(), "Alex");
        var pack = new OpenPackRequest(Guid.NewGuid());
        var starter = new ClaimStarterDeckRequest(Guid.NewGuid(), "growroom");
        var save = new SaveDeckRequest(Guid.NewGuid(), null, null, "Deck", []);
        var delete = new DeleteDeckRequest(Guid.NewGuid(), Guid.NewGuid());

        await ShouldForward(
            application,
            Operation.State,
            null,
            token => ((IApplicationStateReader)capabilities).State(token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.CreateProfile,
            profile,
            token => ((IProfileOperations)capabilities).CreateProfile(profile, token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.OpenPack,
            pack,
            token => ((IPackOperations)capabilities).OpenPack(pack, token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.ClaimStarterDeck,
            starter,
            token => ((IStarterDeckOperations)capabilities).ClaimStarterDeck(starter, token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.SaveDeck,
            save,
            token => ((IDeckOperations)capabilities).SaveDeck(save, token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.DeleteDeck,
            delete,
            token => ((IDeckOperations)capabilities).DeleteDeck(delete, token),
            ApplicationResult,
            ApplicationError
        );
        await ShouldForward(
            application,
            Operation.PurgeData,
            null,
            token => ((IProfileOperations)capabilities).PurgeData(token),
            ApplicationResult,
            ApplicationError
        );
    }

    [Test]
    public async Task Capabilities_ForwardMatchRequestsCancellationResultsAndErrors()
    {
        var application = new RecordingApplication();
        var capabilities = new ApplicationCapabilities(application);
        var start = new StartMatchRequest(Guid.NewGuid(), Guid.NewGuid());
        var matchId = Guid.NewGuid();
        var apply = new ApplyMatchActionRequest(Guid.NewGuid(), 3, "attack", []);

        await ShouldForward(
            application,
            Operation.StartMatch,
            start,
            token => ((IMatchOperations)capabilities).StartMatch(start, token),
            MatchResult,
            MatchError
        );
        await ShouldForward(
            application,
            Operation.ApplyMatchAction,
            apply,
            token => ((IMatchOperations)capabilities).ApplyMatchAction(matchId, apply, token),
            MatchResult,
            MatchError,
            matchId
        );
    }

    [Test]
    public async Task BrowserComposition_UsesOneScopedCapabilityAdapterAndOneScopedModeRouter()
    {
        var bootstrap = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json")
        );
        using var http = new HttpClient { BaseAddress = new Uri("https://browser.invalid/") };
        await using var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(new UnusedJsRuntime())
            .AddBlokemonClient(
                http,
                BlokemonCatalogue.FromBootstrapJson(bootstrap),
                new PlayModeAvailability(serverBacked: false),
                EconomyRules.Unlimited
            )
            .BuildServiceProvider();
        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();

        var firstRoles = Roles(firstScope.ServiceProvider);
        var secondRoles = Roles(secondScope.ServiceProvider);
        var firstModes = firstScope.ServiceProvider.GetRequiredService<PlayModeApplication>();
        var firstFacade = firstScope.ServiceProvider.GetRequiredService<IBlokemonApplication>();
        var secondModes = secondScope.ServiceProvider.GetRequiredService<PlayModeApplication>();

        firstRoles.Skip(1).ShouldAllBe(role => ReferenceEquals(role, firstRoles[0]));
        secondRoles.Skip(1).ShouldAllBe(role => ReferenceEquals(role, secondRoles[0]));
        ReferenceEquals(firstFacade, firstModes).ShouldBeTrue();
        ReferenceEquals(firstRoles[0], secondRoles[0]).ShouldBeFalse();
        ReferenceEquals(firstModes, secondModes).ShouldBeFalse();
    }

    private static object[] Roles(IServiceProvider services) =>
        [
            services.GetRequiredService<IApplicationStateReader>(),
            services.GetRequiredService<IDeckOperations>(),
            services.GetRequiredService<IStarterDeckOperations>(),
            services.GetRequiredService<IMatchOperations>(),
            services.GetRequiredService<IPackOperations>(),
            services.GetRequiredService<IProfileOperations>(),
        ];

    private static async Task ShouldForward<T>(
        RecordingApplication application,
        Operation operation,
        object? request,
        Func<CancellationToken, Task<ApiResponse<T>>> invoke,
        ApiResponse<T> result,
        ApiResponse<T> error,
        Guid? matchId = null
    )
    {
        using var cancellation = new CancellationTokenSource();
        application.Responses.Enqueue(result);
        application.Responses.Enqueue(error);
        var callCount = application.Calls.Count;

        var forwardedResult = await invoke(cancellation.Token);
        var forwardedError = await invoke(cancellation.Token);

        ReferenceEquals(forwardedResult, result).ShouldBeTrue();
        ReferenceEquals(forwardedError, error).ShouldBeTrue();
        ReferenceEquals(forwardedResult.Value, result.Value).ShouldBeTrue();
        ReferenceEquals(forwardedError.Error, error.Error).ShouldBeTrue();
        application.Calls.Count.ShouldBe(callCount + 2);
        foreach (var call in application.Calls.Skip(callCount))
        {
            call.Operation.ShouldBe(operation);
            ReferenceEquals(call.Request, request).ShouldBeTrue();
            call.MatchId.ShouldBe(matchId);
            call.CancellationToken.ShouldBe(cancellation.Token);
        }
    }

    private enum Operation
    {
        State,
        CreateProfile,
        OpenPack,
        ClaimStarterDeck,
        SaveDeck,
        DeleteDeck,
        StartMatch,
        ApplyMatchAction,
        PurgeData,
    }

    private sealed record Call(
        Operation Operation,
        object? Request,
        Guid? MatchId,
        CancellationToken CancellationToken
    );

    private sealed class RecordingApplication : IBlokemonApplication
    {
        public Queue<object> Responses { get; } = new();

        public List<Call> Calls { get; } = [];

        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.State, null, null, cancellationToken);

        public Task<ApiResponse<ApplicationView>> CreateProfile(
            CreateProfileRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.CreateProfile, request, null, cancellationToken);

        public Task<ApiResponse<ApplicationView>> OpenPack(
            OpenPackRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.OpenPack, request, null, cancellationToken);

        public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
            ClaimStarterDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.ClaimStarterDeck, request, null, cancellationToken);

        public Task<ApiResponse<ApplicationView>> SaveDeck(
            SaveDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.SaveDeck, request, null, cancellationToken);

        public Task<ApiResponse<ApplicationView>> DeleteDeck(
            DeleteDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.DeleteDeck, request, null, cancellationToken);

        public Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
            CancellationToken cancellationToken = default
        ) => Respond<MatchMutationView>(Operation.StartMatch, request, null, cancellationToken);

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        ) =>
            Respond<MatchMutationView>(
                Operation.ApplyMatchAction,
                request,
                matchId,
                cancellationToken
            );

        public Task<ApiResponse<ApplicationView>> PurgeData(
            CancellationToken cancellationToken = default
        ) => Respond<ApplicationView>(Operation.PurgeData, null, null, cancellationToken);

        private Task<ApiResponse<T>> Respond<T>(
            Operation operation,
            object? request,
            Guid? matchId,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(new(operation, request, matchId, cancellationToken));
            return Task.FromResult((ApiResponse<T>)Responses.Dequeue());
        }
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => throw new NotSupportedException();
    }
}
