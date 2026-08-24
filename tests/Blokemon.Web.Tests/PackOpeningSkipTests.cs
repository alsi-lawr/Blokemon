using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Components;
using Blokemon.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// Getting past one slow opening and never wanting to see another are different wishes, and the
// pack screen used to grant the second when a player asked for the first: one flag behind two
// controls, so skipping the pack in front of you also turned the ceremony off for every pack you
// would ever open, silently.
public sealed class PackOpeningSkipTests
{
    [Test]
    public async Task SkippingOneOpeningLeavesTheNextPackAnimating()
    {
        var browser = new WillingBrowser();
        var application = new OnePackAtATime();
        await using var services = new ServiceCollection()
            .AddSingleton<IApplicationStateReader>(application)
            .AddSingleton<IPackOperations>(application)
            .AddSingleton<IJSRuntime>(browser)
            .AddSingleton<SoundBoard>()
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        await harness.Show<Packs>();
        // A screen that never asked would answer this test the same way while proving nothing: the
        // preference starts from what the browser says about motion.
        browser.WasAskedAboutMotion.ShouldBeTrue();

        var packs = harness.Showing<Packs>();
        await harness.Press(() => harness.Showing<PackOrder>().Opened.InvokeAsync());
        packs.PlayingTheOpening.ShouldBeTrue();

        await harness.Press(() => harness.Showing<PackOpeningHead>().Skipped.InvokeAsync());

        packs.PlayingTheOpening.ShouldBeFalse();
        harness.Showing<PackOrder>().SkipsOpenings.ShouldBeFalse();

        // And so the pack after it is opened the way it would have been: torn, not tallied.
        packs.CloseOpening();
        await harness.Press(() => harness.Showing<PackOrder>().Opened.InvokeAsync());

        packs.PlayingTheOpening.ShouldBeTrue();
    }

    private static readonly Guid PackId = Guid.Parse("0f000000-0000-0000-0000-000000000081");

    private static readonly CardView Card = new(
        "blocke",
        "Blocke",
        CardKindView.Blokemon,
        "Beer",
        "A bloke.",
        string.Empty,
        [],
        1,
        false
    );

    private static readonly PackStockPresentationView Stock = new(
        "<svg></svg>",
        "<svg></svg>",
        "<svg></svg>"
    );

    // A browser with no opinion against motion, which is the only state in which an opening has an
    // animation to skip in the first place.
    private sealed class WillingBrowser : IJSRuntime, IJSObjectReference
    {
        public bool WasAskedAboutMotion { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(Answer<TValue>(identifier, args));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(Answer<TValue>(identifier, args));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private TValue Answer<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                    return (TValue)(object)this;
                case "prefersReducedMotion":
                    WasAskedAboutMotion = true;
                    return (TValue)(object)false;
            }

            // A pack put up to be torn is focused, and focus is moved through the browser like
            // anything else about an element.
            if (
                identifier.EndsWith("focus", StringComparison.Ordinal)
                && args is [ElementReference, ..]
            )
            {
                return default!;
            }

            throw new NotSupportedException(
                $"The pack screen asked the browser for '{identifier}'."
            );
        }
    }

    // A player who can keep opening packs, each one a pack further along than the last.
    private sealed class OnePackAtATime : IApplicationStateReader, IPackOperations
    {
        private int _sequence;

        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ApiResponse<ApplicationView>(true, View(null), null));

        public Task<ApiResponse<ApplicationView>> OpenPack(
            OpenPackRequest request,
            CancellationToken cancellationToken = default
        )
        {
            _sequence++;
            return Task.FromResult(
                new ApiResponse<ApplicationView>(
                    true,
                    View(new(PackId, _sequence, [Card, Card, Card])),
                    null
                )
            );
        }

        private static ApplicationView View(PackReceiptView? opened) =>
            new(
                new(Guid.Parse("0f000000-0000-0000-0000-000000000042"), "You", 1, "starter-beer"),
                [Card],
                [],
                [],
                new(Stock, Stock),
                opened,
                null,
                null
            );
    }
}
