using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class ApplicationSnapshotCoordinatorTests
{
    [Test]
    public async Task ConcurrentPageAndWarmupHydration_SharesOneStateAndNavigationReadsTheSnapshot()
    {
        var uncoordinated = new ScriptedApplication();
        uncoordinated.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(-1))));
        uncoordinated.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(-1))));
        await Task.WhenAll(uncoordinated.State(), uncoordinated.State());
        uncoordinated.StateCalls.ShouldBe(2);

        var application = new ScriptedApplication();
        var pendingState = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.StateResponses.Enqueue(token => pendingState.Task.WaitAsync(token));
        var invalidations = new ManualDocumentInvalidations();
        var coordinator = Coordinator(application, invalidations);
        var catalogue = Catalogue();
        var js = new ArtJsRuntime();
        var warmup = new CardArtWarmup(js, catalogue, coordinator);
        await using var services = new ServiceCollection()
            .AddSingleton<IApplicationStateReader>(coordinator)
            .AddSingleton(catalogue)
            .AddSingleton<IJSRuntime>(js)
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        var warming = warmup.Start();
        var page = harness.Show<Collection>();
        await application.StateCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        application.StateCalls.ShouldBe(1);
        pendingState.SetResult(Succeeded(View(1)));
        await Task.WhenAll(warming, page);
        var afterNavigation = await coordinator.State();

        application.StateCalls.ShouldBe(1);
        afterNavigation.Value.ShouldBeSameAs(ViewFrom(await coordinator.State()));
        js.WarmCalls.ShouldBe(1);

        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task SuccessfulMutationsPublishReturnedViewsWithoutAnotherStateCall()
    {
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(0))));
        var coordinator = Coordinator(application);
        await coordinator.State();
        var next = 1;

        foreach (var mutation in ApplicationMutations(coordinator, application))
        {
            var view = View(next++);
            application.ApplicationResponses.Enqueue(_ => Task.FromResult(Succeeded(view)));

            var response = await mutation.Invoke(CancellationToken.None);

            response.Value.ShouldBeSameAs(view);
            ViewFrom(await coordinator.State()).ShouldBeSameAs(view);
            application.StateCalls.ShouldBe(1);
        }

        foreach (var mutation in MatchMutations(coordinator, application))
        {
            var view = View(next++);
            application.MatchResponses.Enqueue(_ =>
                Task.FromResult(Succeeded(new MatchMutationView(view, null)))
            );

            var response = await mutation.Invoke(CancellationToken.None);

            response.Value!.Application.ShouldBeSameAs(view);
            ViewFrom(await coordinator.State()).ShouldBeSameAs(view);
            application.StateCalls.ShouldBe(1);
        }

        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task FailedAndCancelledMutationsPreserveTheCurrentSnapshotAndExactFailure()
    {
        var current = View(10);
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(current)));
        var coordinator = Coordinator(application);
        await coordinator.State();
        var error = new ApiError("operation.failed", "The operation failed.");

        foreach (var mutation in ApplicationMutations(coordinator, application))
        {
            var failure = new ApiResponse<ApplicationView>(false, null, error);
            application.ApplicationResponses.Enqueue(_ => Task.FromResult(failure));

            var response = await mutation.Invoke(CancellationToken.None);

            response.ShouldBeSameAs(failure);
            ViewFrom(await coordinator.State()).ShouldBeSameAs(current);
        }

        var purgeFailure = new ApiResponse<ApplicationView>(false, null, error);
        application.ApplicationResponses.Enqueue(_ => Task.FromResult(purgeFailure));
        (await coordinator.PurgeData()).ShouldBeSameAs(purgeFailure);
        ViewFrom(await coordinator.State()).ShouldBeSameAs(current);

        var nullSuccess = new ApiResponse<ApplicationView>(true, null, null);
        application.ApplicationResponses.Enqueue(_ => Task.FromResult(nullSuccess));
        (await coordinator.OpenPack(new(Guid.NewGuid()))).ShouldBeSameAs(nullSuccess);
        ViewFrom(await coordinator.State()).ShouldBeSameAs(current);

        foreach (var mutation in MatchMutations(coordinator, application))
        {
            var failure = new ApiResponse<MatchMutationView>(false, null, error);
            application.MatchResponses.Enqueue(_ => Task.FromResult(failure));

            var response = await mutation.Invoke(CancellationToken.None);

            response.ShouldBeSameAs(failure);
            ViewFrom(await coordinator.State()).ShouldBeSameAs(current);
        }

        foreach (var mutation in CancellableMutations(coordinator))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => mutation(cancellation.Token));
            ViewFrom(await coordinator.State()).ShouldBeSameAs(current);
        }

        using (var modeCancellation = new CancellationTokenSource())
        {
            modeCancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() =>
                coordinator.SelectMode(PlayMode.BrowserLocal, modeCancellation.Token)
            );
        }

        application.MutationCalls.ShouldBe(9);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task CallerCancellationAfterAcquisition_DoesNotCancelDurableMutationPublication()
    {
        var current = View(1);
        var published = View(2);
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(current)));
        var coordinator = Coordinator(application);
        await coordinator.State();
        var acquired = NewSignal();
        var delivery = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken operationToken = default;
        application.ApplicationResponses.Enqueue(async token =>
        {
            operationToken = token;
            acquired.SetResult();
            return await delivery.Task.WaitAsync(token);
        });
        using var cancellation = new CancellationTokenSource();

        var caller = coordinator.OpenPack(new(Guid.NewGuid()), cancellation.Token);
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => caller);
        operationToken.ShouldNotBe(cancellation.Token);
        operationToken.IsCancellationRequested.ShouldBeFalse();
        delivery.SetResult(Succeeded(published));
        await Eventually(async () =>
            ReferenceEquals(ViewFrom(await coordinator.State()), published)
        );

        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task CancelledCaller_BackgroundFailureIsObservedAndLaterMatchMutationStillRuns()
    {
        var current = View(3);
        var later = View(4);
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(current)));
        var coordinator = Coordinator(application);
        await coordinator.State();
        var acquired = NewSignal();
        var delivery = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.ApplicationResponses.Enqueue(async token =>
        {
            acquired.SetResult();
            return await delivery.Task.WaitAsync(token);
        });
        using var cancellation = new CancellationTokenSource();

        var caller = coordinator.SaveDeck(
            new(Guid.NewGuid(), null, null, "Deck", []),
            cancellation.Token
        );
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => caller);
        delivery.SetException(new InvalidOperationException("Injected background failure."));

        application.MatchResponses.Enqueue(_ =>
            Task.FromResult(Succeeded(new MatchMutationView(later, null)))
        );
        var response = await coordinator
            .StartMatch(new(Guid.NewGuid(), Guid.NewGuid()))
            .WaitAsync(TimeSpan.FromSeconds(5));

        response.Value!.Application.ShouldBeSameAs(later);
        ViewFrom(await coordinator.State()).ShouldBeSameAs(later);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task CancelledPurgeCaller_CompletesTheClearBoundaryInBackground()
    {
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(5))));
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(6))));
        var coordinator = Coordinator(application);
        await coordinator.State();
        var acquired = NewSignal();
        var delivery = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.ApplicationResponses.Enqueue(async token =>
        {
            acquired.SetResult();
            return await delivery.Task.WaitAsync(token);
        });
        using var cancellation = new CancellationTokenSource();

        var caller = coordinator.PurgeData(cancellation.Token);
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => caller);
        delivery.SetResult(Succeeded(View(50)));

        await Eventually(async () => ViewFrom(await coordinator.State()).Profile!.Revision == 6);
        application.StateCalls.ShouldBe(2);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task CancelledModeCaller_StillPublishesTheDurablySavedModeBoundary()
    {
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(30))));
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(31))));
        var documents = new BlockingModeDocumentStore();
        var modes = new PlayModeApplication(
            application,
            application,
            documents,
            new PlayModeAvailability(serverBacked: true)
        );
        var coordinator = new ApplicationSnapshotCoordinator(
            application,
            modes,
            new ManualDocumentInvalidations()
        );
        await coordinator.State();
        using var cancellation = new CancellationTokenSource();

        var caller = coordinator.SelectMode(PlayMode.BrowserLocal, cancellation.Token);
        await documents.DurablyCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => caller);
        documents.DeliverCreate();

        await Eventually(async () => ViewFrom(await coordinator.State()).Profile!.Revision == 31);
        (await coordinator.Mode()).Selected.ShouldBe(PlayMode.BrowserLocal);
        application.StateCalls.ShouldBe(2);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task DisposalCancelsAcquiredWorkWithoutPublishingOrWaitingForever()
    {
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(7))));
        var invalidations = new ManualDocumentInvalidations();
        var coordinator = Coordinator(application, invalidations);
        await coordinator.State();
        var acquired = NewSignal();
        application.ApplicationResponses.Enqueue(async token =>
        {
            acquired.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Succeeded(View(8));
        });

        var operation = coordinator.DeleteDeck(new(Guid.NewGuid(), Guid.NewGuid()));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<OperationCanceledException>(() => operation);
        invalidations.SubscriptionDisposed.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(() => coordinator.State());
    }

    [Test]
    public async Task ExternalInvalidationOrdersBeforeOrAfterMutationPublicationConservatively()
    {
        var application = new ScriptedApplication();
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(10))));
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(13))));
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(15))));
        var invalidations = new ManualDocumentInvalidations();
        var coordinator = Coordinator(application, invalidations);
        await coordinator.State();

        invalidations.Signal("profile");
        var afterEarlierSignal = View(11);
        application.ApplicationResponses.Enqueue(_ =>
            Task.FromResult(Succeeded(afterEarlierSignal))
        );
        await coordinator.OpenPack(new(Guid.NewGuid()));
        ViewFrom(await coordinator.State()).ShouldBeSameAs(afterEarlierSignal);

        var acquired = NewSignal();
        var delivery = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.ApplicationResponses.Enqueue(async token =>
        {
            acquired.SetResult();
            return await delivery.Task.WaitAsync(token);
        });
        var inFlight = coordinator.CreateProfile(new(Guid.NewGuid(), "Alex"));
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        invalidations.Signal("profile");
        delivery.SetResult(Succeeded(View(12)));
        await inFlight;

        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(13);

        application.ApplicationResponses.Enqueue(_ => Task.FromResult(Succeeded(View(14))));
        await coordinator.OpenPack(new(Guid.NewGuid()));
        invalidations.Signal("profile");
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(15);
        application.StateCalls.ShouldBe(3);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task FailedInvalidationSubscriptionIsRetriedAndConcurrentAttemptsAreCoalesced()
    {
        var application = new ScriptedApplication();
        var state = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.StateResponses.Enqueue(_ => state.Task);
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(22))));
        var invalidations = new ScriptedDocumentInvalidations();
        invalidations.BlockNextSubscription(succeeds: false);
        var coordinator = Coordinator(application, invalidations);

        var first = coordinator.State();
        var concurrent = coordinator.State();
        await invalidations.SubscriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        invalidations.ReleaseSubscription();
        state.SetResult(Succeeded(View(21)));
        await Task.WhenAll(first, concurrent);

        invalidations.Attempts.ShouldBe(1);
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(21);
        invalidations.Attempts.ShouldBe(2);
        invalidations.Signal("profile");
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(22);
        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task DisposalDoesNotWaitForPendingSubscriptionAndDisposesLateSuccess()
    {
        var application = new ScriptedApplication();
        var invalidations = new ScriptedDocumentInvalidations();
        invalidations.BlockNextSubscription(succeeds: true);
        var coordinator = Coordinator(application, invalidations);
        var state = coordinator.State();
        await invalidations.SubscriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        invalidations.ReleaseSubscription();

        await Should.ThrowAsync<ObjectDisposedException>(() => state);
        await Eventually(() => Task.FromResult(invalidations.SubscriptionDisposed));
    }

    [Test]
    public async Task StaleHydrationCannotOverwriteAMutationOrDocumentInvalidation()
    {
        var application = new ScriptedApplication();
        var staleMutationHydration = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var staleInvalidationHydration = new TaskCompletionSource<ApiResponse<ApplicationView>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        application.StateResponses.Enqueue(_ => staleMutationHydration.Task);
        application.StateResponses.Enqueue(_ => staleInvalidationHydration.Task);
        application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(View(4))));
        var invalidations = new ManualDocumentInvalidations();
        var coordinator = Coordinator(application, invalidations);

        var firstHydration = coordinator.State();
        await application.WaitForStateCalls(1);
        var mutationView = View(2);
        application.ApplicationResponses.Enqueue(_ => Task.FromResult(Succeeded(mutationView)));
        await coordinator.CreateProfile(new(Guid.NewGuid(), "Alex"));
        staleMutationHydration.SetResult(Succeeded(View(1)));
        ViewFrom(await firstHydration).ShouldBeSameAs(mutationView);
        ViewFrom(await coordinator.State()).ShouldBeSameAs(mutationView);

        var secondHydration = coordinator.Refresh();
        await application.WaitForStateCalls(2);
        invalidations.Signal("profile");
        staleInvalidationHydration.SetResult(Succeeded(View(3)));
        ViewFrom(await secondHydration).Profile!.Revision.ShouldBe(4);
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(4);
        application.StateCalls.ShouldBe(3);

        await coordinator.DisposeAsync();
    }

    [Test]
    public async Task ModeChangePurgeRefreshExternalSignalAndDisposalAreExplicitBoundaries()
    {
        var application = new ScriptedApplication();
        for (var revision = 1; revision <= 5; revision++)
        {
            var view = View(revision);
            application.StateResponses.Enqueue(_ => Task.FromResult(Succeeded(view)));
        }
        var invalidations = new ManualDocumentInvalidations();
        var documents = new MemoryDocumentStore();
        var modes = new PlayModeApplication(
            application,
            application,
            documents,
            new PlayModeAvailability(serverBacked: true)
        );
        var coordinator = new ApplicationSnapshotCoordinator(application, modes, invalidations);

        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(1);
        var selected = await coordinator.SelectMode(PlayMode.BrowserLocal);
        selected.Succeeded.ShouldBeTrue();
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(2);

        await coordinator.SelectMode(PlayMode.BrowserLocal);
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(2);

        var purgeView = View(20);
        application.ApplicationResponses.Enqueue(_ => Task.FromResult(Succeeded(purgeView)));
        (await coordinator.PurgeData()).Value.ShouldBeSameAs(purgeView);
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(3);

        ViewFrom(await coordinator.Refresh()).Profile!.Revision.ShouldBe(4);
        invalidations.Signal("match");
        ViewFrom(await coordinator.State()).Profile!.Revision.ShouldBe(5);
        application.StateCalls.ShouldBe(5);

        await coordinator.DisposeAsync();
        invalidations.SubscriptionDisposed.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(() => coordinator.State());
    }

    [Test]
    public async Task BrowserCompositionSharesOneScopedCoordinatorAndKeepsOneScopedModeRouter()
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
            services.GetRequiredService<IApplicationStateRefresher>(),
            services.GetRequiredService<IDeckOperations>(),
            services.GetRequiredService<IStarterDeckOperations>(),
            services.GetRequiredService<IMatchOperations>(),
            services.GetRequiredService<IPackOperations>(),
            services.GetRequiredService<IProfileOperations>(),
            services.GetRequiredService<IPlayModeOperations>(),
        ];

    private static IEnumerable<ApplicationMutation> ApplicationMutations(
        ApplicationSnapshotCoordinator coordinator,
        ScriptedApplication application
    )
    {
        yield return new(token => coordinator.CreateProfile(new(Guid.NewGuid(), "Alex"), token));
        yield return new(token => coordinator.OpenPack(new(Guid.NewGuid()), token));
        yield return new(token =>
            coordinator.ClaimStarterDeck(new(Guid.NewGuid(), "growroom"), token)
        );
        yield return new(token =>
            coordinator.SaveDeck(new(Guid.NewGuid(), null, null, "Deck", []), token)
        );
        yield return new(token =>
            coordinator.DeleteDeck(new(Guid.NewGuid(), Guid.NewGuid()), token)
        );
    }

    private static IEnumerable<MatchMutation> MatchMutations(
        ApplicationSnapshotCoordinator coordinator,
        ScriptedApplication application
    )
    {
        yield return new(token =>
            coordinator.StartMatch(new(Guid.NewGuid(), Guid.NewGuid()), token)
        );
        yield return new(token =>
            coordinator.ApplyMatchAction(
                Guid.NewGuid(),
                new(Guid.NewGuid(), 1, "action", []),
                token
            )
        );
    }

    private static IEnumerable<Func<CancellationToken, Task>> CancellableMutations(
        ApplicationSnapshotCoordinator coordinator
    )
    {
        yield return token => coordinator.CreateProfile(new(Guid.NewGuid(), "Alex"), token);
        yield return token => coordinator.OpenPack(new(Guid.NewGuid()), token);
        yield return token => coordinator.ClaimStarterDeck(new(Guid.NewGuid(), "growroom"), token);
        yield return token =>
            coordinator.SaveDeck(new(Guid.NewGuid(), null, null, "Deck", []), token);
        yield return token => coordinator.DeleteDeck(new(Guid.NewGuid(), Guid.NewGuid()), token);
        yield return token => coordinator.StartMatch(new(Guid.NewGuid(), Guid.NewGuid()), token);
        yield return token =>
            coordinator.ApplyMatchAction(
                Guid.NewGuid(),
                new(Guid.NewGuid(), 1, "action", []),
                token
            );
        yield return token => coordinator.PurgeData(token);
    }

    private static ApplicationSnapshotCoordinator Coordinator(
        ScriptedApplication application,
        IApplicationDocumentInvalidations? invalidations = null
    )
    {
        var modes = new PlayModeApplication(
            application,
            application,
            new MemoryDocumentStore(),
            new PlayModeAvailability(serverBacked: true)
        );
        return new(application, modes, invalidations ?? new ManualDocumentInvalidations());
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static BlokemonCatalogue Catalogue() =>
        BlokemonCatalogue.FromBootstrapJson(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "catalogue.json"))
        );

    private static ApiResponse<ApplicationView> Succeeded(ApplicationView view) =>
        new(true, view, null);

    private static ApiResponse<MatchMutationView> Succeeded(MatchMutationView view) =>
        new(true, view, null);

    private static ApplicationView View(int revision)
    {
        var stock = new PackStockPresentationView(string.Empty, string.Empty, string.Empty);
        return new(
            new(Guid.Empty, $"Player {revision}", revision, null),
            [],
            [],
            [],
            new(stock, stock),
            null,
            null,
            null
        );
    }

    private static ApplicationView ViewFrom(ApiResponse<ApplicationView> response) =>
        response.Value ?? throw new InvalidOperationException("Expected an application view.");

    private sealed record ApplicationMutation(
        Func<CancellationToken, Task<ApiResponse<ApplicationView>>> Invoke
    );

    private sealed record MatchMutation(
        Func<CancellationToken, Task<ApiResponse<MatchMutationView>>> Invoke
    );

    private sealed class ScriptedApplication : IBlokemonApplication
    {
        public Queue<
            Func<CancellationToken, Task<ApiResponse<ApplicationView>>>
        > StateResponses { get; } = new();

        public Queue<
            Func<CancellationToken, Task<ApiResponse<ApplicationView>>>
        > ApplicationResponses { get; } = new();

        public Queue<
            Func<CancellationToken, Task<ApiResponse<MatchMutationView>>>
        > MatchResponses { get; } = new();

        public TaskCompletionSource StateCalled { get; private set; } = NewSignal();

        public int StateCalls { get; private set; }

        public int MutationCalls { get; private set; }

        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        )
        {
            StateCalls++;
            StateCalled.TrySetResult();
            return StateResponses.Dequeue()(cancellationToken);
        }

        public Task<ApiResponse<ApplicationView>> CreateProfile(
            CreateProfileRequest request,
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public Task<ApiResponse<ApplicationView>> OpenPack(
            OpenPackRequest request,
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
            ClaimStarterDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public Task<ApiResponse<ApplicationView>> SaveDeck(
            SaveDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public Task<ApiResponse<ApplicationView>> DeleteDeck(
            DeleteDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
            CancellationToken cancellationToken = default
        ) => Match(cancellationToken);

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        ) => Match(cancellationToken);

        public Task<ApiResponse<ApplicationView>> PurgeData(
            CancellationToken cancellationToken = default
        ) => Application(cancellationToken);

        public async Task WaitForStateCalls(int count)
        {
            while (StateCalls < count)
            {
                var signal = StateCalled;
                await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (StateCalls < count && ReferenceEquals(signal, StateCalled))
                {
                    StateCalled = NewSignal();
                }
            }
        }

        private Task<ApiResponse<ApplicationView>> Application(CancellationToken token)
        {
            MutationCalls++;
            return ApplicationResponses.Dequeue()(token);
        }

        private Task<ApiResponse<MatchMutationView>> Match(CancellationToken token)
        {
            MutationCalls++;
            return MatchResponses.Dequeue()(token);
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ManualDocumentInvalidations : IApplicationDocumentInvalidations
    {
        private Func<string, Task>? _invalidated;

        public bool SubscriptionDisposed { get; private set; }

        public Task<IAsyncDisposable?> Subscribe(Func<string, Task> invalidated)
        {
            _invalidated = invalidated;
            return Task.FromResult<IAsyncDisposable?>(new Subscription(this));
        }

        public void Signal(string key) => _invalidated?.Invoke(key).GetAwaiter().GetResult();

        private sealed class Subscription(ManualDocumentInvalidations owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner._invalidated = null;
                owner.SubscriptionDisposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ScriptedDocumentInvalidations : IApplicationDocumentInvalidations
    {
        private Func<string, Task>? _invalidated;
        private TaskCompletionSource? _release;
        private bool _pendingSucceeds;

        public int Attempts { get; private set; }

        public TaskCompletionSource SubscriptionStarted { get; private set; } = NewSignal();

        public bool SubscriptionDisposed { get; private set; }

        public void BlockNextSubscription(bool succeeds)
        {
            _pendingSucceeds = succeeds;
            _release = NewSignal();
            SubscriptionStarted = NewSignal();
        }

        public void ReleaseSubscription() => _release!.SetResult();

        public async Task<IAsyncDisposable?> Subscribe(Func<string, Task> invalidated)
        {
            Attempts++;
            if (_release is { } release)
            {
                SubscriptionStarted.SetResult();
                await release.Task;
                _release = null;
                if (!_pendingSucceeds)
                {
                    return null;
                }
            }

            _invalidated = invalidated;
            return new Subscription(this);
        }

        public void Signal(string key) => _invalidated?.Invoke(key).GetAwaiter().GetResult();

        private sealed class Subscription(ScriptedDocumentInvalidations owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner._invalidated = null;
                owner.SubscriptionDisposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BlockingModeDocumentStore : IStateDocumentStore
    {
        private readonly TaskCompletionSource _delivery = NewSignal();
        private StoredDocument? _document;

        public TaskCompletionSource DurablyCreated { get; } = NewSignal();

        public Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_document);

        public async Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            _document = new(1, json);
            DurablyCreated.SetResult();
            await _delivery.Task.WaitAsync(cancellationToken);
            return new DocumentWriteResult.Written(1);
        }

        public Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task Delete(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void DeliverCreate() => _delivery.SetResult();
    }

    private sealed class MemoryDocumentStore : IStateDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(
            StringComparer.Ordinal
        );

        public Task<StoredDocument?> Read(
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_documents.GetValueOrDefault(key));

        public Task<DocumentWriteResult> Create(
            string key,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (_documents.ContainsKey(key))
            {
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
            }

            _documents[key] = new(1, json);
            return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(1));
        }

        public Task<DocumentWriteResult> Update(
            string key,
            long expectedRevision,
            string json,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !_documents.TryGetValue(key, out var current)
                || current.Revision != expectedRevision
            )
            {
                return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Conflict());
            }

            var revision = expectedRevision + 1;
            _documents[key] = new(revision, json);
            return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(revision));
        }

        public Task Delete(string key, CancellationToken cancellationToken = default)
        {
            _documents.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ArtJsRuntime : IJSRuntime
    {
        public int WarmCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult((TValue)(object)new Module(this));

        private sealed class Module(ArtJsRuntime owner) : IJSObjectReference
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
                if (identifier == "warm")
                {
                    owner.WarmCalls++;
                }
                return ValueTask.FromResult(default(TValue)!);
            }
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
