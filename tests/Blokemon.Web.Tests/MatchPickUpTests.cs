using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Components;
using Blokemon.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// A card is played by picking it up and putting it down where it goes, whether the two steps are
// two taps or one drag. Picking a card up focuses the table on what it can do: the card holds the
// chosen glow, the places it can go glow as targets, and nothing else is lit, on the table or in
// the hand. Every move has a target - the card the engine names, or, where it names none, the
// place the physical game would use - and a card with nowhere at all to go is used where it
// stands.
//
// These are asked of the page through what it hands its presenters and what it sends the
// application, so nothing here rests on a class name: the aura view the battlefield and the hand
// are drawn from, the sheet the page decided to show, and the action the application was asked
// to apply. The drag reports through the same three entry points the browser calls.
public sealed class MatchPickUpTests
{
    [Test]
    public async Task AtRestEveryCardWithAMoveGlowsAndNothingIsFocused()
    {
        var (harness, _) = await Table(Playing);

        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Cards.ShouldBe([Energy, Basic, Kit, Fossil, Active, Bench], ignoreOrder: true);
        auras.Draggable.ShouldBe(auras.Cards, ignoreOrder: true);
        auras.Selected.ShouldBeEmpty();
        auras.Targets.ShouldBeEmpty();
        auras.Places.ShouldBe(MatchTargetPlaces.None);
        auras.Focused.ShouldBeFalse();
        // The hand is drawn from the same view as the table, so the two never disagree.
        harness.Showing<MatchHandZone>().Auras.ShouldBeSameAs(auras);
    }

    [Test]
    public async Task PickingUpAnEnergyLightsOnlyItsBlokemonAsTargets()
    {
        var (harness, _) = await Table(Playing);

        await harness.ActivateButton("Choose Beer Vim in your hand");

        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Selected.ShouldBe([Energy]);
        auras.Targets.ShouldBe([Active, Bench], ignoreOrder: true);
        auras.Cards.ShouldBeEmpty();
        auras.Places.ShouldBe(MatchTargetPlaces.None);
        auras.Focused.ShouldBeTrue();
        // Every other card that could be played is still there to be picked up instead.
        auras.Draggable.ShouldContain(Basic);
        // Where it goes is asked by the table itself, so no sheet is put over it.
        harness.IsShowing<MatchActionSheet>().ShouldBeFalse();
    }

    [Test]
    public async Task EscapeAndATapOnTheTablePutThePickedUpCardDown()
    {
        var (harness, application) = await Table(Playing);

        await harness.ActivateButton("Choose Beer Vim in your hand");
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeTrue();
        await harness.PressKey("Blokemon match table", "Escape");

        var afterEscape = harness.Showing<MatchBattlefield>().Auras;
        afterEscape.Focused.ShouldBeFalse();
        afterEscape.Selected.ShouldBeEmpty();
        afterEscape.Cards.ShouldContain(Energy);

        await harness.ActivateButton("Choose Beer Vim in your hand");
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeTrue();
        await harness.Press(() =>
            harness.Showing<MatchBattlefield>().BackgroundTapped.InvokeAsync()
        );

        var afterTap = harness.Showing<MatchBattlefield>().Auras;
        afterTap.Focused.ShouldBeFalse();
        afterTap.Cards.ShouldContain(Energy);
        application.Applied.ShouldBeEmpty();
    }

    [Test]
    public async Task PickingUpAnotherCardPutsTheFirstOneDown()
    {
        var (harness, _) = await Table(Playing);

        await harness.ActivateButton("Choose Beer Vim in your hand");
        await harness.ActivateButton("Blocke in your hand");

        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Selected.ShouldBe([Basic]);
        auras.Targets.ShouldBeEmpty();
        auras.Places.ShouldBe(MatchTargetPlaces.Bench);
        auras.Focused.ShouldBeTrue();
    }

    [Test]
    public async Task TappingATargetPlaysTheMoveThatGoesThere()
    {
        var (harness, application) = await Table(Playing);

        await harness.ActivateButton("Choose Beer Vim in your hand");
        await harness.ActivateButton("Choose Blocke, Benched Blokemon");

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(AttachToBench);
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();
    }

    [Test]
    public async Task ADragIsPickedUpAndDroppedThroughTheSameRouting()
    {
        var (harness, application) = await Table(Playing);
        var page = harness.Showing<Match>();

        var picked = false;
        await harness.Press(async () => picked = await page.CardPickedUp(Energy));
        picked.ShouldBeTrue();
        var carrying = harness.Showing<MatchBattlefield>().Auras;
        carrying.Selected.ShouldBe([Energy]);
        carrying.Targets.ShouldBe([Active, Bench], ignoreOrder: true);
        carrying.Focused.ShouldBeTrue();

        await harness.Press(() => page.CardDropped(Energy, MatchDropPlaces.Card, Active));

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(AttachToActive);
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();
    }

    [Test]
    public async Task ADropOverNothingOrOnAPlaceThatIsNotATargetPlaysNothing()
    {
        var (harness, application) = await Table(Playing);
        var page = harness.Showing<Match>();

        await harness.Press(() => page.CardPickedUp(Energy));
        await harness.Press(() => page.CardReleased(Energy));
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();

        await harness.Press(() => page.CardPickedUp(Energy));
        // An Energy has no business on an empty Bench position, whatever the browser says.
        await harness.Press(() => page.CardDropped(Energy, MatchDropPlaces.Bench, null));

        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Focused.ShouldBeFalse();
        auras.Cards.ShouldContain(Energy);
        application.Applied.ShouldBeEmpty();
    }

    [Test]
    public async Task ACardTheTableWillNotGiveUpIsNotPickedUpByADrag()
    {
        var (harness, _) = await Table(Playing);
        var page = harness.Showing<Match>();

        var picked = true;
        await harness.Press(async () => picked = await page.CardPickedUp(Theirs));

        picked.ShouldBeFalse();
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();
    }

    [Test]
    public async Task ABlokemonGoesToTheBenchAKitToTheInPlayPlaceAndAFossilToTheEmptiesTray()
    {
        var (harness, application) = await Table(Playing);
        var battlefield = harness.Showing<MatchBattlefield>();

        await harness.ActivateButton("Choose Blocke in your hand");
        battlefield.Auras.Places.ShouldBe(MatchTargetPlaces.Bench);
        await harness.Press(() => battlefield.BenchTapped.InvokeAsync());

        await harness.ActivateButton("Choose Bar Bill in your hand");
        battlefield.Auras.Places.ShouldBe(MatchTargetPlaces.InPlay);
        await harness.Press(() => battlefield.InPlayTapped.InvokeAsync());

        await harness.ActivateButton("Choose Old Fossil, Benched Blokemon");
        battlefield.Auras.Places.ShouldBe(MatchTargetPlaces.Empties);
        await harness.Press(() => battlefield.EmptiesTapped.InvokeAsync());

        application
            .Applied.Select(static request => request.ActionId)
            .ShouldBe([PlayBasic, PlayKit, ChuckFossil]);
    }

    [Test]
    public async Task TheActiveIsThrownAtTheOpponentsActiveAndTwoAttacksAreAChoice()
    {
        var (harness, application) = await Table(Playing);

        await harness.ActivateButton("Choose Blocke, Active Blokemon");
        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Selected.ShouldBe([Active]);
        auras.Targets.ShouldBe([Theirs]);
        harness.IsShowing<MatchActionSheet>().ShouldBeFalse();

        await harness.ActivateButton("Choose Blocke, Active Blokemon");

        // Two ready attacks are a real choice between effects, asked once the card is put down.
        var sheet = harness.Showing<MatchActionSheet>().Sheet;
        sheet.Mode.ShouldBe(MatchSheetMode.Actions);
        sheet.Options.Select(static option => option.Id).ShouldBe([Swing, Jab]);
        application.Applied.ShouldBeEmpty();
    }

    [Test]
    public async Task OneReadyAttackIsThrownByTheDropItself()
    {
        var (harness, application) = await Table(Playing, without: Jab);

        await harness.ActivateButton("Choose Blocke, Active Blokemon");
        await harness.ActivateButton("Choose Blocke, Active Blokemon");

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(Swing);
    }

    [Test]
    public async Task ABenchBlokemonRetreatsOntoTheActive()
    {
        var (harness, application) = await Table(Playing);

        await harness.ActivateButton("Choose Blocke, Benched Blokemon");
        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Selected.ShouldBe([Bench]);
        auras.Targets.ShouldBe([Active]);
        await harness.ActivateButton("Choose Blocke, Active Blokemon");

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(Retreat);
    }

    [Test]
    public async Task APowerAimedAtACardOnTheTableIsAnsweredByTheDrop()
    {
        var (harness, application) = await Table(Playing, with: Aimed(Bench, [Theirs]));

        await harness.ActivateButton("Choose Blocke, Benched Blokemon");
        var auras = harness.Showing<MatchBattlefield>().Auras;
        // Its retreat goes to the Active and its Power to the card the Power asks for.
        auras.Targets.ShouldBe([Active, Theirs], ignoreOrder: true);
        harness.IsShowing<MatchActionSheet>().ShouldBeFalse();

        await harness.ActivateButton("Choose Brawler, Active Blokemon");
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();
        var applied = application.Applied.ShouldHaveSingleItem();
        applied.ActionId.ShouldBe(Power);
        applied.Choices.ShouldHaveSingleItem().CardInstanceIds.ShouldBe([Theirs]);
    }

    [Test]
    public async Task APowerThatNamesNoCardIsUsedWhereItsBlokemonStands()
    {
        var (harness, application) = await Table(Playing, with: InPlace(Bench), without: Retreat);

        await harness.ActivateButton("Choose Blocke, Benched Blokemon");
        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Selected.ShouldBe([Bench]);
        auras.Targets.ShouldBeEmpty();
        auras.Places.ShouldBe(MatchTargetPlaces.None);
        auras.Focused.ShouldBeTrue();
        harness.IsShowing<MatchActionSheet>().ShouldBeFalse();

        await harness.ActivateButton("Choose Blocke, Benched Blokemon");

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(Power);
    }

    [Test]
    public async Task APowerBesideARetreatKeepsItsSheetWhileTheTableShowsTheActive()
    {
        var (harness, _) = await Table(Playing, with: InPlace(Bench));

        await harness.ActivateButton("Choose Blocke, Benched Blokemon");

        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Targets.ShouldBe([Active]);
        var sheet = harness.Showing<MatchActionSheet>().Sheet;
        sheet.Mode.ShouldBe(MatchSheetMode.Destination);
        sheet.Options.Select(static option => option.Id).ShouldBe([Retreat, Power]);
    }

    [Test]
    public async Task TheOpeningBasicGoesToTheEmptyActivePosition()
    {
        var (harness, application) = await Table(Opening);
        var battlefield = harness.Showing<MatchBattlefield>();

        var rest = battlefield.Auras;
        rest.Cards.ShouldBe([Basic, Kit], ignoreOrder: true);
        rest.Focused.ShouldBeFalse();

        await harness.ActivateButton("Choose Blocke in your hand");
        var carrying = battlefield.Auras;
        carrying.Selected.ShouldBe([Basic]);
        carrying.Places.ShouldBe(MatchTargetPlaces.Active);
        carrying.Focused.ShouldBeTrue();

        // Escape puts the card down; the decision the match posed is still waiting.
        await harness.PressKey("Blokemon match table", "Escape");
        battlefield.Auras.Focused.ShouldBeFalse();
        battlefield.Auras.Cards.ShouldBe([Basic, Kit], ignoreOrder: true);

        await harness.ActivateButton("Choose Bar Bill in your hand");
        await harness.Press(() => battlefield.ActiveSlotTapped.InvokeAsync());

        application.Applied.ShouldHaveSingleItem().ActionId.ShouldBe(OpenWithKit);
    }

    // Both sides choose their opening Blokemon at once, and the computer decides off the thread
    // that draws: the player's candidates are not offered until its decision has landed, or a
    // choice made meanwhile would be refused against the battle the computer changed.
    [Test]
    public async Task NothingOfThePlayersIsOfferedWhileTheComputerIsChoosing()
    {
        var thinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (harness, application) = await Table(Opening, theirTurnToo: true, deciding: thinking);

        harness.Showing<MatchBattlefield>().Thinking.ShouldBeTrue();
        var auras = harness.Showing<MatchBattlefield>().Auras;
        auras.Cards.ShouldBeEmpty();
        auras.Draggable.ShouldBeEmpty();

        var picked = true;
        await harness.Press(async () =>
            picked = await harness.Showing<Match>().CardPickedUp(Basic)
        );
        picked.ShouldBeFalse();
        await harness.Press(() =>
            harness.Showing<MatchBattlefield>().CardTapped.InvokeAsync(Basic)
        );
        harness.Showing<MatchBattlefield>().Auras.Focused.ShouldBeFalse();
        application.Applied.ShouldBeEmpty();

        thinking.SetResult();
        await Eventually(harness, static battlefield => !battlefield.Thinking);

        harness.Showing<MatchBattlefield>().Auras.Cards.ShouldBe([Basic, Kit], ignoreOrder: true);
    }

    // The computer's decision lands on the renderer's own time: the table is asked until it has
    // drawn what the decision left.
    private static async Task Eventually(
        ComponentHarness harness,
        Func<MatchBattlefield, bool> drawn
    )
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (drawn(harness.Showing<MatchBattlefield>()))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The table did not draw what the computer's decision left.");
    }

    private const string Energy = "hand-energy";

    private const string Basic = "hand-basic";

    private const string Kit = "hand-kit";

    private const string Active = "you-active";

    private const string Bench = "you-bench";

    private const string Fossil = "you-fossil";

    private const string Theirs = "cpu-active";

    private const string AttachToActive = "attach:active";

    private const string AttachToBench = "attach:bench";

    private const string PlayBasic = "play:basic";

    private const string PlayKit = "play:kit";

    private const string ChuckFossil = "chuck:fossil";

    private const string Swing = "attack:swing";

    private const string Jab = "attack:jab";

    private const string Retreat = "retreat:bench";

    private const string Power = "power:bench";

    private const string OpenWithBasic = "open:basic";

    private const string OpenWithKit = "open:kit";

    private static readonly Guid MatchId = Guid.Parse("0f000000-0000-0000-0000-000000000173");

    private static CardView Card(string id, string name, CardKindView kind) =>
        new(id, name, kind, "Beer", "A card.", string.Empty, [], 0, false);

    private static readonly CardView Blocke = Card("blocke", "Blocke", CardKindView.Blokemon);

    private static readonly CardView Brawler = Card("brawler", "Brawler", CardKindView.Blokemon);

    private static readonly CardView Beer = Card("beer", "Beer Vim", CardKindView.Energy);

    private static readonly CardView Bill = Card("bill", "Bar Bill", CardKindView.Trainer);

    private static readonly CardView Fossilised = Card(
        "fossil",
        "Old Fossil",
        CardKindView.Trainer
    );

    private static MatchActionView Action(
        string id,
        MatchActionKindView kind,
        string source,
        string? target = null,
        MatchChoiceRequirementView[]? requirements = null
    ) => new(id, kind, id, false, source, target, null, requirements ?? [], null);

    private static readonly MatchActionView[] Playing =
    [
        Action(AttachToActive, MatchActionKindView.AttachEnergy, Energy, Active),
        Action(AttachToBench, MatchActionKindView.AttachEnergy, Energy, Bench),
        Action(PlayBasic, MatchActionKindView.PlayBlokemon, Basic),
        Action(PlayKit, MatchActionKindView.PlayTrainer, Kit),
        Action(ChuckFossil, MatchActionKindView.DiscardFossil, Fossil),
        Action(Swing, MatchActionKindView.Attack, Active),
        Action(Jab, MatchActionKindView.Attack, Active),
        Action(Retreat, MatchActionKindView.Retreat, Bench),
        new("end", MatchActionKindView.EndTurn, "End turn", false, null, null, null, [], null),
    ];

    private static readonly MatchActionView[] Opening =
    [
        Action(OpenWithBasic, MatchActionKindView.ChooseOpening, Basic),
        Action(OpenWithKit, MatchActionKindView.ChooseOpening, Kit),
    ];

    // A Power that asks first for a card the table shows.
    private static MatchActionView Aimed(string source, string[] eligible) =>
        Action(
            Power,
            MatchActionKindView.UsePokemonPower,
            source,
            requirements:
            [
                new(
                    "pick",
                    MatchChoiceKindView.Cards,
                    "Choose a Blokemon",
                    new("you", "You", true),
                    1,
                    1,
                    [.. eligible.Select(id => Instance(id, Blocke))],
                    [],
                    [],
                    null,
                    [],
                    false,
                    []
                ),
            ]
        );

    // A Power that asks nothing the table can show.
    private static MatchActionView InPlace(string source) =>
        Action(Power, MatchActionKindView.UsePokemonPower, source);

    private static MatchCardInstanceView Instance(string id, CardView card) =>
        new(id, card, "You", "Field", 0, 60, [], [], [], []);

    private static MatchFrameView Frame(bool opening, bool theirTurnToo = false) =>
        new(
            MatchId,
            1,
            3,
            opening ? MatchPhaseView.OpeningPlacement : MatchPhaseView.Playing,
            new(
                "CPU",
                "Theirs",
                20,
                3,
                6,
                opening ? null : Instance(Theirs, Brawler),
                [],
                [],
                [],
                [],
                theirTurnToo
            ),
            new(
                "You",
                "Yours",
                20,
                3,
                6,
                opening ? null : Instance(Active, Blocke),
                opening ? [] : [Instance(Bench, Blocke), Instance(Fossil, Fossilised)],
                [Instance(Energy, Beer), Instance(Basic, Blocke), Instance(Kit, Bill)],
                [],
                [],
                true
            ),
            false,
            null
        );

    private static async Task<(ComponentHarness Harness, OneTurn Application)> Table(
        MatchActionView[] actions,
        MatchActionView? with = null,
        string? without = null,
        bool theirTurnToo = false,
        TaskCompletionSource? deciding = null
    )
    {
        var legal = actions.Where(action => action.Id != without).ToList();
        if (with is not null)
        {
            legal.Add(with);
        }

        var application = new OneTurn(
            Frame(ReferenceEquals(actions, Opening), theirTurnToo),
            [.. legal],
            deciding
        );
        var services = new ServiceCollection()
            .AddSingleton<IApplicationStateReader>(application)
            .AddSingleton<IMatchOperations>(application)
            .AddSingleton<IMatchRecoveryOperations>(application)
            .AddSingleton<IComputerRuntime>(StillComputer.Instance)
            .AddSingleton<IPlayModeOperations>(StillComputer.Instance)
            .AddSingleton<IJSRuntime>(new StillBrowser())
            .AddSingleton<NavigationManager>(new BrowserNavigation())
            .AddSingleton<SoundBoard>()
            .BuildServiceProvider();
        var harness = ComponentHarness.For(services);
        await harness.Show<Match>();
        return (harness, application);
    }

    // A browser that answers what a table asks of one and nothing more.
    private sealed class StillBrowser : IJSRuntime, IJSObjectReference
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
                "prefersReducedMotion" => (TValue)(object)true,
                "armPresses"
                or "armDrags"
                or "disarmDrags"
                or "settleDrag"
                or "restoreFocus"
                or "focusSurface" => default!,
                _ => throw new NotSupportedException(
                    $"A still table asked the browser for '{identifier}'."
                ),
            };
    }

    private sealed class BrowserNavigation : NavigationManager
    {
        public BrowserNavigation() =>
            Initialize("https://blokemon.test/", "https://blokemon.test/");

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    // One turn of one battle: the same table before and after every move, and every move asked
    // for written down.
    private sealed class OneTurn(
        MatchFrameView frame,
        MatchActionView[] legal,
        TaskCompletionSource? deciding
    ) : IApplicationStateReader, IMatchOperations, IMatchRecoveryOperations
    {
        public List<ApplyMatchActionRequest> Applied { get; } = [];

        // Set once the computer's one decision has been answered.
        public TaskCompletionSource Decided { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ApiResponse<ApplicationView>(true, View(), null));

        public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Applied.Add(request);
            return Task.FromResult(
                new ApiResponse<MatchMutationView>(true, new(View(), null), null)
            );
        }

        // The computer's decision takes as long as the test says, and lands on a table where
        // the computer has chosen: its half no longer has the turn.
        public async Task<ApiResponse<MatchMutationView>> AdvanceComputer(
            Guid matchId,
            AdvanceComputerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (deciding is not null)
            {
                await deciding.Task;
            }

            frame = frame with
            {
                Opponent = frame.Opponent with { HasTurn = false },
                Revision = frame.Revision + 1,
            };
            Decided.TrySetResult();
            return new ApiResponse<MatchMutationView>(true, new(View(), null), null);
        }

        public Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
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

        private ApplicationView View() =>
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
                new(frame, legal, [], []),
                null
            );
    }
}
