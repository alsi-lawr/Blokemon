using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class MatchRematchTests
{
    [Test]
    public async Task BattleAgain_AllowsAnotherDifficultyBeforeStarting()
    {
        var application = new CompletedBattle();
        await using var services = new ServiceCollection()
            .AddSingleton<IApplicationStateReader>(application)
            .AddSingleton<IMatchOperations>(application)
            .AddSingleton<IMatchRecoveryOperations>(application)
            .AddSingleton<IJSRuntime>(new Browser())
            .AddSingleton<SoundBoard>()
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        await harness.Show<Match>();
        await harness.ActivateButton("Battle again");

        application.Starts.ShouldBeEmpty();
        await harness.ChangeSelect("match-difficulty", nameof(CpuDifficultyView.Hard));
        await harness.ActivateButton("Start battle");

        application.Starts.ShouldHaveSingleItem().Difficulty.ShouldBe(CpuDifficultyView.Hard);
    }

    private sealed class CompletedBattle
        : IApplicationStateReader,
            IMatchOperations,
            IMatchRecoveryOperations
    {
        private static readonly Guid DeckId = Guid.Parse("0f000000-0000-0000-0000-000000000201");

        private static readonly ApplicationView View = new(
            new(Guid.Parse("0f000000-0000-0000-0000-000000000202"), "You", 1, "starter-beer"),
            [],
            [new(DeckId, "Yours", 1, [new("BLK-001", 60)], true, [], [])],
            [],
            new(
                new(string.Empty, string.Empty, string.Empty),
                new(string.Empty, string.Empty, string.Empty)
            ),
            null,
            new(
                new(
                    Guid.Parse("0f000000-0000-0000-0000-000000000203"),
                    12,
                    6,
                    MatchPhaseView.Complete,
                    Side("CPU"),
                    Side("You"),
                    true,
                    "You"
                ),
                [],
                [],
                [],
                CpuDifficultyView.Easy
            ),
            null
        );

        public List<StartMatchRequest> Starts { get; } = [];

        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ApiResponse<ApplicationView>(true, View, null));

        public Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Starts.Add(request);
            return Task.FromResult(new ApiResponse<MatchMutationView>(true, new(View, null), null));
        }

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> AbandonSavedMatch(
            AbandonSavedMatchRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> DiscardMatchHistory(
            DiscardMatchHistoryRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        private static MatchSideView Side(string name) =>
            new(name, name, 0, 0, 0, null, [], [], [], [], false);
    }

    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(Answer<TValue>(identifier));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(Answer<TValue>(identifier));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private TValue Answer<TValue>(string identifier) =>
            identifier switch
            {
                "import" => (TValue)(object)this,
                "prefersReducedMotion" => (TValue)(object)false,
                _ => default!,
            };
    }
}
