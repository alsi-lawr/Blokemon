using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Blokemon.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// A player who has asked for less movement is still playing the same game, and the beats of a
// presentation are how the table says what just happened: whose go it is, what was played, what an
// attack did. Reduced motion takes the runs away; it does not take the announcements with them.
//
// The table used to fast-forward the lot, which is the defect this pins. It was invisible for two
// days because nothing in this repository could start a page and watch it play, and it is the kind
// of thing that comes back the next time someone reaches for the skip signal to mean "no motion".
public sealed class MatchReducedMotionTests
{
    [Test]
    public async Task ReducedMotion_PutsUpEveryBeatRatherThanSkippingToWhereItEnds()
    {
        var before = Table(struck: 0, yourGo: true);
        var after = Table(struck: 30, yourGo: false);
        var presentation = new MatchPresentationView([new(after, [Thrown, Landed, HandedOver])]);
        var browser = new StillBrowser();
        var application = new OneAttack(before, after, presentation);
        await using var services = new ServiceCollection()
            .AddSingleton<IBlokemonApplication>(application)
            .AddSingleton<IJSRuntime>(browser)
            .BuildServiceProvider();
        await using var harness = ComponentHarness.For(services);

        // What the table put on screen, in the order it put it there, for as long as it was
        // playing something. The page renders more often than it changes beat, so a beat is
        // recorded when the cue on screen becomes a different one.
        var putUp = new List<MatchEventCueView?>();
        harness.Painted = () =>
        {
            if (!harness.IsShowing<MatchHud>() || !harness.Showing<MatchHud>().Animating)
            {
                return;
            }

            var cue = harness.Showing<MatchCueOverlays>().Cue;
            if (putUp.Count == 0 || !ReferenceEquals(putUp[^1], cue))
            {
                putUp.Add(cue);
            }
        };

        await harness.Show<Match>();
        // Every beat is also played when motion is on, so a run that never asked about reduced
        // motion would pass this test while proving nothing at all.
        browser.WasAskedAboutMotion.ShouldBeTrue();

        await harness.Press(() =>
            harness.Showing<MatchActionDock>().AttackChosen.InvokeAsync(Swing)
        );

        // Every beat the presentation is made of, in order, and then the table left standing on
        // the position the command ended in.
        putUp.ShouldBe(
            MatchPresentationTimeline.Beats(presentation, before).Select(beat => beat.Cue)
        );
        harness.Showing<MatchHud>().Animating.ShouldBeFalse();
        harness.Showing<MatchBattlefield>().Frame.ShouldBe(after);
    }

    private const string Mine = "you-active";

    private const string Theirs = "cpu-active";

    private const string SwingId = "action-attack";

    private static readonly Guid MatchId = Guid.Parse("0f000000-0000-0000-0000-000000000109");

    private static readonly MatchEventCueView Thrown = new(
        1,
        MatchAnimationKindView.Attack,
        "It swings.",
        Mine,
        [Theirs],
        30,
        null,
        true,
        []
    );

    private static readonly MatchEventCueView Landed = new(
        2,
        MatchAnimationKindView.Damage,
        "It connects.",
        Mine,
        [Theirs],
        30,
        null,
        true,
        []
    );

    private static readonly MatchEventCueView HandedOver = new(
        3,
        MatchAnimationKindView.Turn,
        "Their go.",
        null,
        [],
        0,
        null,
        true,
        []
    );

    private static readonly MatchAttackView Swing = new(
        Mine,
        "effect-swing",
        "Swing",
        [],
        30,
        SwingId,
        null
    );

    private static readonly MatchActionView Attacking = new(
        SwingId,
        MatchActionKindView.Attack,
        "Swing",
        true,
        Mine,
        Theirs,
        "effect-swing",
        [],
        null
    );

    private static readonly CardView Face = new(
        "blocke",
        "Blocke",
        CardKindView.Blokemon,
        "Beer",
        "A bloke.",
        string.Empty,
        [],
        0,
        false
    );

    private static MatchFrameView Table(int struck, bool yourGo) =>
        new(
            MatchId,
            yourGo ? 1 : 2,
            3,
            MatchPhaseView.Playing,
            Side("CPU", "Theirs", Theirs, struck, !yourGo),
            Side("You", "Yours", Mine, 0, yourGo),
            false,
            null
        );

    private static MatchSideView Side(
        string name,
        string deck,
        string standing,
        int damage,
        bool hasTurn
    ) =>
        new(
            name,
            deck,
            20,
            0,
            6,
            new(standing, Face, name, "Oche", damage, 60, [], [], [], []),
            [],
            [],
            [],
            [],
            hasTurn
        );

    private static ApplicationView State(MatchFrameView frame, MatchActionView[] legal) =>
        new(
            new(Guid.Parse("0f000000-0000-0000-0000-000000000042"), "You", 1, "starter-beer"),
            [],
            [],
            [],
            new(
                new(string.Empty, string.Empty, string.Empty),
                new(string.Empty, string.Empty, string.Empty)
            ),
            null,
            new(frame, legal, legal.Length == 0 ? [] : [Swing], []),
            null
        );

    // A browser that has been asked for a screen with Reduce Motion turned on. Nothing else is
    // answered: with the runs suppressed the page has nothing left to measure, and a measurement
    // arriving here would mean it had gone looking for one.
    private sealed class StillBrowser : IJSRuntime, IJSObjectReference
    {
        public bool WasAskedAboutMotion { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(Answer<TValue>(identifier));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(Answer<TValue>(identifier));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private TValue Answer<TValue>(string identifier)
        {
            switch (identifier)
            {
                case "import":
                    return (TValue)(object)this;
                case "prefersReducedMotion":
                    WasAskedAboutMotion = true;
                    return (TValue)(object)true;
                default:
                    throw new NotSupportedException(
                        $"A still table asked the browser for '{identifier}'."
                    );
            }
        }
    }

    // One battle, one attack, and the presentation the engine sends back for it. Everything else
    // an application can be asked is out of this test's way.
    private sealed class OneAttack(
        MatchFrameView before,
        MatchFrameView after,
        MatchPresentationView presentation
    ) : IBlokemonApplication
    {
        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ApiResponse<ApplicationView>(true, Playing(before, [Attacking]), null)
            );

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ApiResponse<MatchMutationView>(
                    true,
                    new(Playing(after, []), presentation),
                    null
                )
            );

        private static ApplicationView Playing(MatchFrameView frame, MatchActionView[] legal) =>
            MatchReducedMotionTests.State(frame, legal);

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

        public Task<ApiResponse<ApplicationView>> PurgeData(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
