using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Components;
using Blokemon.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// In Blokemon the printed text on a card is the rules of that card, so a player who cannot reach a
// card's face is playing a different game from one who can. The viewer was reachable only by
// holding a pointer on a card, which left every card in the game unreadable from the keyboard - and
// a card attached to a Blokemon is drawn inside its host's press surface, so it could not even be
// focused, let alone read.
//
// What is pinned here is the whole of that journey for the hardest case: a card carried by another
// card is opened for itself rather than for the card carrying it, the viewer takes focus while it
// is up, and putting it down hands focus back to the card it was opened from. All three are the
// shared viewer's own doing and all three break silently - a viewer that opens the host, or one
// that leaves focus stranded on a surface that is no longer there, still looks right in a
// screenshot. The shared host now owns that journey, so the second case proves the same lifecycle
// outside Match.
public sealed class MatchCardReadingTests
{
    [Test]
    public async Task AnAttachedCardIsReadForItselfAndHandsFocusBackToTheCardCarryingIt()
    {
        var browser = new Browser();
        await using var services = new ServiceCollection()
            .AddSingleton<IBlokemonApplication>(new OneTable())
            .AddSingleton<IJSRuntime>(browser)
            .AddSingleton<NavigationManager>(new BrowserNavigation())
            .AddSingleton<SoundBoard>()
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        await harness.Show<CardViewerHost>(HostParameters(Page<Match>()));

        // The Blokemon standing in the Oche is carrying a Spanner and two Beer.
        var openerId = await harness.ActivateButton($"Read {Spanner.Name}");

        var viewer = harness.Showing<CardViewer>();
        viewer.Card.ShouldBe(Spanner);
        browser.Focused.ShouldBe([viewer.Element]);
        browser.Guarded.ShouldBe([viewer.Element]);

        await harness.Press(() => viewer.Closed.InvokeAsync());

        browser
            .Focused.Select(static element => element.Id)
            .ShouldBe([viewer.Element.Id, openerId]);
    }

    [Test]
    public async Task ACardOutsideTheMatchUsesTheSameGuardedViewerAndExactFocusReturn()
    {
        var browser = new Browser();
        await using var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(browser)
            .AddSingleton<NavigationManager>(new BrowserNavigation())
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        await harness.Show<CardViewerHost>(HostParameters(CardReader(Spanner)));
        var openerId = await harness.ActivateButton($"Read {Spanner.Name}");

        var viewer = harness.Showing<CardViewer>();
        viewer.Card.ShouldBe(Spanner);
        browser.Focused.ShouldBe([viewer.Element]);
        browser.Guarded.ShouldBe([viewer.Element]);

        await harness.Press(() => viewer.Closed.InvokeAsync());

        browser
            .Focused.Select(static element => element.Id)
            .ShouldBe([viewer.Element.Id, openerId]);
    }

    private const string Mine = "you-active";

    private const string Theirs = "cpu-active";

    private static readonly Guid MatchId = Guid.Parse("0f000000-0000-0000-0000-000000000108");

    private static CardView Card(string id, string name, CardKindView kind) =>
        new(id, name, kind, "Beer", "A card.", string.Empty, [], 0, false);

    private static readonly CardView Blocke = Card("blocke", "Blocke", CardKindView.Blokemon);

    private static readonly CardView Spanner = Card("spanner", "Rusty Spanner", CardKindView.Kit);

    private static readonly CardView Beer = Card("beer", "Beer Vim", CardKindView.BasicVim);

    private static ParameterView HostParameters(RenderFragment child) =>
        ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(CardViewerHost.ChildContent)] = child }
        );

    private static RenderFragment Page<TComponent>()
        where TComponent : IComponent =>
        builder =>
        {
            builder.OpenComponent<TComponent>(0);
            builder.CloseComponent();
        };

    private static RenderFragment CardReader(CardView card) =>
        builder =>
        {
            builder.OpenComponent<CardPress>(0);
            builder.AddAttribute(1, nameof(CardPress.Card), card);
            builder.AddAttribute(2, nameof(CardPress.TapReads), true);
            builder.AddAttribute(3, nameof(CardPress.AriaLabel), $"Read {card.Name}");
            builder.CloseComponent();
        };

    private static MatchFrameView Table() =>
        new(
            MatchId,
            1,
            3,
            MatchPhaseView.Playing,
            Side("CPU", Theirs, hasTurn: false, carrying: false),
            Side("You", Mine, hasTurn: true, carrying: true),
            false,
            null
        );

    private static MatchSideView Side(string name, string standing, bool hasTurn, bool carrying) =>
        new(
            name,
            "Yours",
            20,
            0,
            6,
            new(
                standing,
                Blocke,
                name,
                "Oche",
                0,
                60,
                carrying ? [Beer, Beer] : [],
                carrying ? [Spanner] : [],
                [],
                []
            ),
            [],
            [],
            [],
            [],
            hasTurn
        );

    // A table with one battle on it and nothing waiting to be decided, which is every card in the
    // game sitting where a player can reach it.
    private sealed class OneTable : IBlokemonApplication
    {
        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ApiResponse<ApplicationView>(
                    true,
                    new(
                        new(
                            Guid.Parse("0f000000-0000-0000-0000-000000000042"),
                            "You",
                            1,
                            "starter-beer"
                        ),
                        [],
                        [],
                        [],
                        new(
                            new(string.Empty, string.Empty, string.Empty),
                            new(string.Empty, string.Empty, string.Empty)
                        ),
                        null,
                        new(Table(), [], [], []),
                        null
                    ),
                    null
                )
            );

        public Task<ApiResponse<ApplicationView>> CreateProfile(
            CreateProfileRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> OpenPack(
            OpenPackRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
            ClaimStarterDeckRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> SaveDeck(
            SaveDeckRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> DeleteDeck(
            DeleteDeckRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ApiResponse<ApplicationView>> PurgeData(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    // A browser that answers what a table asks of one and writes down every element it is told to
    // put focus on, in the order it was told. Where focus went is the whole of what this test is
    // about, and it is the one thing about a page that cannot be read off the page itself.
    private sealed class Browser : IJSRuntime, IJSObjectReference
    {
        private readonly List<ElementReference> _focused = [];
        private readonly List<ElementReference> _guarded = [];

        public IReadOnlyList<ElementReference> Focused => _focused;

        public IReadOnlyList<ElementReference> Guarded => _guarded;

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
                    return (TValue)(object)false;
                case "viewerScale":
                case "artworkScale":
                    return (TValue)(object)0.5d;
                case "guardViewer":
                    if (args is [ElementReference guardedElement, ..])
                    {
                        _guarded.Add(guardedElement);
                    }
                    return default!;
                case "armViewer":
                    return default!;
            }

            // Focus is moved through the browser like anything else about an element, and this is
            // the call the framework makes to move it.
            if (
                identifier.EndsWith("focus", StringComparison.Ordinal)
                && args is [ElementReference element, ..]
            )
            {
                _focused.Add(element);
                return default!;
            }

            throw new NotSupportedException($"A still table asked the browser for '{identifier}'.");
        }
    }

    private sealed class BrowserNavigation : NavigationManager
    {
        public BrowserNavigation() =>
            Initialize("https://blokemon.test/", "https://blokemon.test/");

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }
}
