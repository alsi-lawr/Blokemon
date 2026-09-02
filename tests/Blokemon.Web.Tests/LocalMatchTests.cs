using System.Text.Json;
using System.Text.Json.Nodes;
using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Core.SetDesign;
using Blokemon.Cpu;
using Blokemon.Product;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Blokemon.Web.Tests;

public sealed class LocalMatchTests
{
    private static readonly Guid _profileCommand = Guid.Parse(
        "10000000-0000-0000-0000-000000000001"
    );
    private static readonly Guid _firstDeckCommand = Guid.Parse(
        "20000000-0000-0000-0000-000000000001"
    );
    private static readonly Guid _secondDeckCommand = Guid.Parse(
        "20000000-0000-0000-0000-000000000002"
    );
    private static readonly Guid _matchCommand = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Test]
    public async Task StarterClaim_IsIdempotentAndPersistsTheGrantedEditableDeck()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var fixture = ReadyFixture.FromExisting(database, catalogue);
        Value(await fixture.Application.CreateProfile(new(_profileCommand, "Local Player")));
        var commandId = Guid.Parse("11000000-0000-0000-0000-000000000001");
        var request = new ClaimStarterDeckRequest(commandId, "growroom");

        var claimed = Value(await fixture.Application.ClaimStarterDeck(request));
        var afterClaim = await fixture.Store.Read("profile");
        var retried = Value(await fixture.Application.ClaimStarterDeck(request));
        var afterRetry = await fixture.Store.Read("profile");
        var second = Value(
            await fixture.Application.ClaimStarterDeck(new(Guid.NewGuid(), "early-shift"))
        );
        var restored = Value(await fixture.Restart().State());

        claimed.Profile!.StarterDeckId.ShouldBe("growroom");
        claimed.StarterDecks.Single(static deck => deck.IsClaimed).Id.ShouldBe("growroom");
        claimed.Decks.ShouldHaveSingleItem();
        claimed.Decks[0].CardCount.ShouldBe(60);
        claimed.Decks[0].IsLegal.ShouldBeTrue();
        claimed.Decks[0].Warnings.ShouldBeEmpty();
        retried.Profile!.Revision.ShouldBe(claimed.Profile.Revision);
        afterRetry.ShouldBe(afterClaim);
        second.Profile!.StarterDeckId.ShouldBe("early-shift");
        second
            .StarterDecks.Where(static deck => deck.IsClaimed)
            .Select(static deck => deck.Id)
            .ShouldBe(["early-shift", "growroom"], ignoreOrder: true);
        second.Decks.Length.ShouldBe(2);
        restored.Profile!.StarterDeckId.ShouldBe("early-shift");
        restored.Decks.Length.ShouldBe(2);
    }

    [Test]
    public async Task RepeatedStarterClaim_GrantsItsCardsAgainWithoutDuplicatingTheDeck()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var fixture = ReadyFixture.FromExisting(database, catalogue);
        Value(await fixture.Application.CreateProfile(new(_profileCommand, "Local Player")));

        var first = Value(
            await fixture.Application.ClaimStarterDeck(
                new(Guid.Parse("11000000-0000-0000-0000-000000000001"), "growroom")
            )
        );
        var again = Value(
            await fixture.Application.ClaimStarterDeck(
                new(Guid.Parse("11000000-0000-0000-0000-000000000002"), "growroom")
            )
        );

        var ownedAfterFirst = first
            .Cards.Where(static card => card.OwnedQuantity > 0)
            .ToDictionary(static card => card.Id, static card => card.OwnedQuantity);
        var ownedAfterAgain = again
            .Cards.Where(static card => card.OwnedQuantity > 0)
            .ToDictionary(static card => card.Id, static card => card.OwnedQuantity);
        var starterEntries = first
            .StarterDecks.Single(static deck => deck.Id == "growroom")
            .Entries.Where(entry => ownedAfterFirst.ContainsKey(entry.CardId))
            .ToArray();

        starterEntries.ShouldNotBeEmpty();
        foreach (var entry in starterEntries)
        {
            ownedAfterAgain[entry.CardId].ShouldBe(ownedAfterFirst[entry.CardId] + entry.Quantity);
        }
        again.Decks.ShouldHaveSingleItem();
        again.Profile!.StarterDeckId.ShouldBe("growroom");
    }

    [Test]
    public async Task PurgeData_DeletesEveryStoredDocumentAndAllowsAFreshStart()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        Value<MatchMutationView>(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );

        var purged = Value(await fixture.Application.PurgeData());
        var profileDocument = await fixture.Store.Read("profile");
        var matchDocument = await fixture.Store.Read("match");
        var historyDocument = await fixture.Store.Read("match-history");
        var recreated = Value(
            await fixture.Application.CreateProfile(new(Guid.NewGuid(), "Fresh Player"))
        );

        purged.Profile.ShouldBeNull();
        purged.Match.ShouldBeNull();
        profileDocument.ShouldBeNull();
        matchDocument.ShouldBeNull();
        historyDocument.ShouldBeNull();
        recreated.Profile!.DisplayName.ShouldBe("Fresh Player");
    }

    [Test]
    public async Task EnergylessLegalDeck_WarnsButStartsAgainstDistinctRedactedCpuStarter()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var fixture = ReadyFixture.FromExisting(database, catalogue);
        Value(await fixture.Application.CreateProfile(new(_profileCommand, "Local Player")));
        Value(
            await fixture.Application.ClaimStarterDeck(
                new(Guid.Parse("11000000-0000-0000-0000-000000000002"), "growroom")
            )
        );
        var deckId = Guid.Parse("21000000-0000-0000-0000-000000000001");
        var energyless = Value(
            await fixture.Application.SaveDeck(
                new(
                    deckId,
                    null,
                    null,
                    "Warning-only deck",
                    [
                        new("BLK-001", 3),
                        new("BLK-002", 1),
                        .. Enumerable
                            .Range(1, 14)
                            .Select(index => new DeckEntryView($"KIT-{index:D3}", 4)),
                    ]
                )
            )
        );
        var saved = energyless.Decks.Single(deck => deck.Id == deckId);

        var startResponse = await fixture.Application.StartMatch(new(_matchCommand, deckId));
        var started = Value<MatchMutationView>(startResponse);
        var match = started.Application.Match!;
        var cues = started
            .Presentation!.Steps.SelectMany(static step => step.Events)
            .Select(static cue => cue.Sequence)
            .ToArray();
        var retried = Value<MatchMutationView>(
            await fixture.Application.StartMatch(new(_matchCommand, deckId))
        );

        saved.IsLegal.ShouldBeTrue();
        saved.Warnings.ShouldNotBeEmpty();
        match.Frame.Player.DeckName.ShouldBe("Custom deck");
        match.Frame.Opponent.DeckName.ShouldBe("Brick Lane Heat");
        match.Frame.Opponent.DeckName.ShouldNotBe(match.Frame.Player.DeckName);
        match.Frame.Opponent.Hand.ShouldBeEmpty();
        match.Frame.Opponent.HandCount.ShouldBeGreaterThan(0);
        match.Frame.Player.Hand.Length.ShouldBe(match.Frame.Player.HandCount);
        cues.SequenceEqual(cues.Order()).ShouldBeTrue();
        retried.Presentation.ShouldBeNull();
    }

    [Test]
    public async Task Restart_PreservesNonEmptyHumanOpeningAndVisibleState()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.CreateOpeningChoice(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var opening = await AdvanceToOpeningChoice(fixture.Application, started);
        var openingMatch = opening.Match!;
        var action = OpeningAction(openingMatch);
        var request = RequestFor(openingMatch, action, Guid.NewGuid());
        var selectedOcheId = action.Id["opening:".Length..];
        var selectedOche = openingMatch
            .LegalActions.SelectMany(static candidate => candidate.ChoiceRequirements)
            .SelectMany(static requirement => requirement.EligibleCards)
            .First(card => card.Id == selectedOcheId);
        var boothRequirement = action.ChoiceRequirements.Single(requirement =>
            requirement.Id == "opening:booth"
        );
        var eligibleBooth = boothRequirement
            .EligibleCards.Where(card => card.Id != selectedOche.Id)
            .ToArray();
        eligibleBooth.Length.ShouldBeGreaterThan(0);
        var selectedBooth = eligibleBooth[0];
        var humanBoothSelection = SelectionFor(boothRequirement) with
        {
            CardInstanceIds = [selectedBooth.Id],
        };
        request = request with
        {
            Choices = request
                .Choices.Select(choice =>
                    choice.Id == boothRequirement.Id ? humanBoothSelection : choice
                )
                .ToArray(),
        };
        var applied = Value(
            await fixture.Application.ApplyMatchAction(openingMatch.Frame.Id, request)
        );

        var restarted = fixture.Restart();
        var restored = Value(await restarted.State());

        restored.Profile!.Id.ShouldBe(applied.Profile!.Id);
        restored.Decks.Single().Id.ShouldBe(_firstDeckCommand);
        restored.Match!.Frame.Id.ShouldBe(applied.Match!.Frame.Id);
        applied.Match.Frame.Player.Active!.Card.Id.ShouldBe(selectedOche.Card.Id);
        applied
            .Match.Frame.Player.Bench.Select(static card => card.Card.Id)
            .ShouldContain(selectedBooth.Card.Id);
        await AssertEquivalent(restored.Match, applied.Match);
        restored.MatchError.ShouldBeNull();
    }

    [Test]
    public async Task PlayBlokemon_MovesTheSelectedHandCardToTheBench()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.CreateOpeningChoice(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var opening = await AdvanceToOpeningChoice(fixture.Application, started);
        var openingMatch = opening.Match!;
        var openingAction = OpeningAction(openingMatch);
        var playing = await AdvanceThroughSetup(
            fixture.Application,
            Value(
                await fixture.Application.ApplyMatchAction(
                    openingMatch.Frame.Id,
                    RequestFor(openingMatch, openingAction, Guid.NewGuid())
                )
            )
        );
        var playingMatch = playing.Match!;
        var playAction = playingMatch.LegalActions.First(action =>
            action.Kind == MatchActionKindView.PlayBlokemon
        );
        var cardId = playAction.SourceCardInstanceId!;

        var played = Value<MatchMutationView>(
            await fixture.Application.ApplyMatchAction(
                playingMatch.Frame.Id,
                RequestFor(playingMatch, playAction, Guid.NewGuid())
            )
        );
        var playCue = played
            .Presentation!.Steps.SelectMany(static step => step.Events)
            .Single(cue => cue.Kind == MatchAnimationKindView.Play);

        playingMatch.Frame.Player.Hand.Select(static card => card.Id).ShouldContain(cardId);
        played
            .Application.Match!.Frame.Player.Hand.Select(static card => card.Id)
            .ShouldNotContain(cardId);
        played
            .Application.Match.Frame.Player.Bench.Select(static card => card.Id)
            .ShouldContain(cardId);
        playCue.SourceCardInstanceId.ShouldBe(cardId);
        playCue.ActorIsLocalPlayer.ShouldBe(true);
    }

    // A mulligan is the rules' own reveal: the hand goes back, and it is shown before it goes.
    //
    // BOTH hands. A player has already seen their own seven, so the rulebook never troubles to
    // compel that half of the disclosure, and reading its silence as permission left the local
    // player watching their own Deck reshuffle with nothing on screen to account for it. The cue
    // carried no faces, so the overlay had nothing to draw and the beat was not even mandatory.
    [Test]
    public async Task AMulliganedHandIsShownWholeToWhoeverThrewItAway()
    {
        await using var database = await TestDatabase.Create();
        var catalogue = BlokemonCatalogueBuilder.Load(
            Path.Combine(AppContext.BaseDirectory, "content")
        );
        var fixture = ReadyFixture.FromExisting(database, catalogue);
        Value(await fixture.Application.CreateProfile(new(_profileCommand, "Local Player")));
        var deckCommand = Guid.Parse("20000000-0000-0000-0000-000000000009");
        // One Basic in sixty, so an opening hand nearly always has to go back.
        Value(
            await fixture.Application.SaveDeck(
                new(
                    deckCommand,
                    null,
                    null,
                    "Mulligan deck",
                    [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                )
            )
        );

        var reveals = new List<MatchEventCueView>();
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var started = Value<MatchMutationView>(
                await fixture.Application.StartMatch(
                    new(Guid.Parse($"50000000-0000-0000-0000-00000000000{attempt}"), deckCommand)
                )
            );
            reveals.AddRange(
                started
                    .Presentation!.Steps.SelectMany(static step => step.Events)
                    .Where(static cue => cue.Kind == MatchAnimationKindView.Reveal)
            );

            // Cleared before the next deal: a battle still in progress refuses another start.
            var opening = started.Application.Match!;
            Value(
                await fixture.Application.ApplyMatchAction(
                    opening.Frame.Id,
                    RequestFor(
                        opening,
                        opening.LegalActions.Single(static action =>
                            action.Kind == MatchActionKindView.Resign
                        ),
                        Guid.NewGuid()
                    )
                )
            );
        }

        // The player's own mulligan reaches them, which is the half that was filtered away.
        reveals.ShouldContain(static cue => cue.ActorIsLocalPlayer == true);
        // And every card a reveal names is a card it shows. A returned hand is nowhere by the time
        // this is read, so anything that asks where its cards are now loses the ones the reshuffle
        // dealt straight back out - which was most mulligans, by about one card each.
        reveals.ShouldAllBe(cue =>
            cue.TargetCardInstanceIds.Length > 0
            && cue.RevealedCards.Length == cue.TargetCardInstanceIds.Length
        );
    }

    [Test]
    public async Task DuplicateStart_IsIdempotentButConflictingPayloadFails()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database, includeSecondDeck: true);
        var request = new StartMatchRequest(_matchCommand, _firstDeckCommand);

        var started = Value(await fixture.Application.StartMatch(request));
        var afterStart = await fixture.Store.Read("match");
        var retried = Value(await fixture.Application.StartMatch(request));
        var afterRetry = await fixture.Store.Read("match");
        var beforeConflict = await fixture.Store.Read("match");
        var difficultyConflict = await fixture.Application.StartMatch(
            new(_matchCommand, _firstDeckCommand, CpuDifficultyView.Hard)
        );
        var afterDifficultyConflict = await fixture.Store.Read("match");
        var conflict = await fixture.Application.StartMatch(new(_matchCommand, _secondDeckCommand));
        var afterConflict = await fixture.Store.Read("match");

        await AssertEquivalent(retried.Match!, started.Match!);
        afterRetry.ShouldBe(afterStart);
        Error(difficultyConflict).Code.ShouldBe("match.command_conflict");
        afterDifficultyConflict.ShouldBe(beforeConflict);
        Error(conflict).Code.ShouldBe("match.command_conflict");
        afterConflict.ShouldBe(beforeConflict);
    }

    [Test]
    public async Task ConcurrentDuplicateStart_IsReconciledAsIdempotent()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var other = fixture.Restart();
        var request = new StartMatchRequest(_matchCommand, _firstDeckCommand);

        var responses = await Task.WhenAll(
            fixture.Application.StartMatch(request),
            other.StartMatch(request)
        );

        responses.All(static response => response.Succeeded).ShouldBeTrue();
        responses
            .Select(static response => response.Value!.Application.Match!.Frame.Id)
            .Distinct()
            .ShouldHaveSingleItem();
        (await fixture.Store.Read("match"))!.Revision.ShouldBe(1);
    }

    [Test]
    public async Task DuplicateAction_IsIdempotentButConflictingPayloadFails()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var action = started.Match!.LegalActions[0];
        var commandId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var request = RequestFor(started.Match, action, commandId);

        var applied = Value(
            await fixture.Application.ApplyMatchAction(started.Match.Frame.Id, request)
        );
        var afterApply = await fixture.Store.Read("match");
        var retried = Value(
            await fixture.Application.ApplyMatchAction(started.Match.Frame.Id, request)
        );
        var afterRetry = await fixture.Store.Read("match");
        var beforeConflict = await fixture.Store.Read("match");
        var conflict = await fixture.Application.ApplyMatchAction(
            started.Match.Frame.Id,
            request with
            {
                ActionId = "not-the-original-action",
            }
        );
        var afterConflict = await fixture.Store.Read("match");

        await AssertEquivalent(retried.Match!, applied.Match!);
        afterRetry.ShouldBe(afterApply);
        Error(conflict).Code.ShouldBe("match.command_conflict");
        afterConflict.ShouldBe(beforeConflict);
    }

    [Test]
    public async Task ConcurrentDuplicateAction_IsReconciledAsIdempotent()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var other = fixture.Restart();
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var before = await fixture.Store.Read("match");
        var action = started.Match!.LegalActions[0];
        var request = RequestFor(started.Match, action, Guid.NewGuid());

        var responses = await Task.WhenAll(
            fixture.Application.ApplyMatchAction(started.Match.Frame.Id, request),
            other.ApplyMatchAction(started.Match.Frame.Id, request)
        );
        var after = await fixture.Store.Read("match");

        responses.All(static response => response.Succeeded).ShouldBeTrue();
        responses
            .Select(static response => response.Value!.Application.Match!.Frame.Revision)
            .Distinct()
            .ShouldHaveSingleItem();
        after!.Revision.ShouldBe(before!.Revision + 1);
    }

    [Test]
    public async Task RejectedActionRequests_DoNotMutateTheMatchDocument()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var action = started.Match!.LegalActions[0];
        var original = await fixture.Store.Read("match");

        var stale = await fixture.Application.ApplyMatchAction(
            started.Match.Frame.Id,
            RequestFor(started.Match, action, Guid.NewGuid()) with
            {
                ExpectedRevision = started.Match.Frame.Revision - 1,
            }
        );
        var illegal = await fixture.Application.ApplyMatchAction(
            started.Match.Frame.Id,
            new(Guid.NewGuid(), started.Match.Frame.Revision, "missing-action", [])
        );
        var after = await fixture.Store.Read("match");

        Error(stale).Code.ShouldBe("match.stale");
        Error(illegal).Code.ShouldBe("match.action_illegal");
        after.ShouldBe(original);
    }

    // The reported defect: a retreat nobody can pay for used to be filtered out of the legal set
    // altogether, so the affordance did not grey out - it ceased to exist, and the player was
    // given no way to learn why. An Active that has just come down has nothing attached to pay a
    // fare with, which is the same position from the first turn of any battle.
    [Test]
    public async Task UnaffordableRetreat_ReachesTheClientDisabledAndCannotBeSubmitted()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.CreateOpeningChoice(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var opening = await AdvanceToOpeningChoice(fixture.Application, started);
        var playing = await ChooseOpeningWithBench(fixture.Application, opening.Match!);
        var retreat = playing.Match!.LegalActions.First(static action =>
            action.Kind == MatchActionKindView.Retreat
        );
        var original = await fixture.Store.Read("match");

        var refused = await fixture.Application.ApplyMatchAction(
            playing.Match.Frame.Id,
            RequestFor(playing.Match, retreat, Guid.NewGuid())
        );
        var after = await fixture.Store.Read("match");

        retreat.DisabledReason.ShouldNotBeNullOrWhiteSpace();
        Error(refused).Code.ShouldBe("match.action_unaffordable");
        after.ShouldBe(original);
    }

    [Test]
    public async Task OpeningChoiceFailures_AreTypedAndMutateNothing()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var opening = await AdvanceToOpeningChoice(fixture.Application, started);
        var action = OpeningAction(opening.Match!);
        var original = await fixture.Store.Read("match");
        var validRequest = RequestFor(opening.Match!, action, Guid.NewGuid());
        var requirement = action.ChoiceRequirements.Single(candidate =>
            candidate.Id == "opening:booth"
        );
        var invalidChoice = SelectionFor(requirement) with
        {
            CardInstanceIds = ["not-an-eligible-card"],
        };

        var invalid = await fixture.Application.ApplyMatchAction(
            opening.Match!.Frame.Id,
            validRequest with
            {
                Choices = [invalidChoice],
            }
        );
        var afterInvalid = await fixture.Store.Read("match");
        var malformed = await fixture.Application.ApplyMatchAction(
            opening.Match.Frame.Id,
            validRequest with
            {
                CommandId = Guid.NewGuid(),
                Choices = [invalidChoice with { Id = null! }],
            }
        );
        var afterMalformed = await fixture.Store.Read("match");
        var missing = await fixture.Application.ApplyMatchAction(
            opening.Match.Frame.Id,
            validRequest with
            {
                CommandId = Guid.NewGuid(),
                Choices = validRequest
                    .Choices.Where(choice => choice.Id != requirement.Id)
                    .ToArray(),
            }
        );
        var afterMissing = await fixture.Store.Read("match");
        var extra = await fixture.Application.ApplyMatchAction(
            opening.Match.Frame.Id,
            validRequest with
            {
                CommandId = Guid.NewGuid(),
                Choices =
                [
                    .. validRequest.Choices,
                    EmptySelection(requirement) with
                    {
                        Id = "unknown-choice",
                    },
                ],
            }
        );
        var afterExtra = await fixture.Store.Read("match");
        var wrongKind = await fixture.Application.ApplyMatchAction(
            opening.Match.Frame.Id,
            validRequest with
            {
                CommandId = Guid.NewGuid(),
                Choices = validRequest
                    .Choices.Select(choice =>
                        choice.Id == requirement.Id
                            ? choice with
                            {
                                Kind = MatchChoiceKindView.Amount,
                                Amount = 0,
                                CardInstanceIds = [],
                            }
                            : choice
                    )
                    .ToArray(),
            }
        );
        var afterWrongKind = await fixture.Store.Read("match");
        var applied = Value(
            await fixture.Application.ApplyMatchAction(opening.Match.Frame.Id, validRequest)
        );
        var restored = Value(await fixture.Restart().State());

        Error(invalid).Code.ShouldBe("match.choice_invalid");
        Error(malformed).Code.ShouldBe("match.choice_invalid");
        Error(missing).Code.ShouldBe("match.choice_required");
        Error(extra).Code.ShouldBe("match.choice_invalid");
        Error(wrongKind).Code.ShouldBe("match.choice_invalid");
        afterInvalid.ShouldBe(original);
        afterMalformed.ShouldBe(original);
        afterMissing.ShouldBe(original);
        afterExtra.ShouldBe(original);
        afterWrongKind.ShouldBe(original);
        applied.Match!.Frame.Revision.ShouldBeGreaterThan(opening.Match.Frame.Revision);
        await AssertEquivalent(restored.Match!, applied.Match);
    }

    [Test]
    public async Task Whirlwind_PresentationReportsPrintedDamage()
    {
        await using var database = await TestDatabase.Create();
        var fixture = ChoiceMatchFixture.Create(database);
        var (match, action) = await ReachCiggySamAttack(fixture);
        var defending = match.Frame.Opponent.Active!;

        var result = await fixture.Service.Apply(
            fixture.Profile,
            "Local Player",
            match.Frame.Id,
            RequestFor(match, action, Guid.NewGuid())
        );
        if (result.Error is not null || result.Presentation is null)
        {
            throw new InvalidOperationException(
                result.Error?.Message ?? "No presentation returned."
            );
        }
        var resolved = Match(result);
        var cue = result
            .Presentation.Steps.SelectMany(static step => step.Events)
            .Single(eventCue =>
                eventCue.Kind == MatchAnimationKindView.Attack
                && eventCue.ActorIsLocalPlayer == true
            );

        cue.Label.ShouldBe("Local Player used Whirlwind.");
        cue.Amount.ShouldBe(10);
        resolved.Frame.Opponent.Active!.Id.ShouldNotBe(defending.Id);
        resolved.Frame.Opponent.Bench.Single(card => card.Id == defending.Id).Damage.ShouldBe(10);
    }

    // The reveal must not become a standing window into the opponent's hand: nothing outside a
    // choice the player is answering may show a card they are not entitled to see.
    [Test]
    public async Task OpponentHiddenCards_NeverAppearInAnyViewOrCue()
    {
        await using var database = await TestDatabase.Create();
        var fixture = ChoiceMatchFixture.Create(database);
        var match = Match(
            await fixture.Service.Start(
                fixture.Profile,
                "Local Player",
                new(_matchCommand, _firstDeckCommand)
            )
        );
        var inspected = 0;
        for (var step = 0; step < 60 && !match.Frame.IsComplete; step++)
        {
            AssertNoHiddenOpponentCards(match);
            inspected++;
            var next = ForwardAction(match);
            if (next is null)
            {
                break;
            }
            var mutation = await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                match.Frame.Id,
                RequestFor(match, next, Guid.NewGuid())
            );
            foreach (var frame in mutation.Presentation?.Steps ?? [])
            {
                AssertNoHiddenOpponentCardInstances(frame.Frame);
                foreach (var cue in frame.Events)
                {
                    // A cue only ever turns a card face up for the local player's own hidden
                    // cards - their Prize Cards and their deck. The two decks share no card, so
                    // a face from the opponent's deck showing up here would be a leak.
                    cue.RevealedCards.ShouldAllBe(card =>
                        card.Id == "BLK-016" || card.Id == "VIM-BLAZED"
                    );
                }
            }
            match = Match(mutation);
        }

        inspected.ShouldBeGreaterThan(3);
        AssertNoHiddenOpponentCards(match);
    }

    [Test]
    [Arguments(CpuDifficultyView.Easy)]
    [Arguments(CpuDifficultyView.Normal)]
    [Arguments(CpuDifficultyView.Hard)]
    [Arguments(CpuDifficultyView.Impossible)]
    public async Task SelectedCpuDifficulty_SurvivesRefreshColdReplayAndLeavesTheProfileUnchanged(
        CpuDifficultyView difficulty
    )
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var profile = await fixture.Store.Read("profile");

        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand, difficulty))
        );
        var stored = (await fixture.Store.Read("match"))!;
        var document = StoredMatch(stored);
        var refreshed = Value(await fixture.Application.State());
        var restarted = Value(await fixture.Restart().State());

        started.Match!.Difficulty.ShouldBe(difficulty);
        document.StartCommand.CpuPolicy.Difficulty.ShouldBe(difficulty);
        document.CpuPolicy.Difficulty.ShouldBe(difficulty);
        document.StartCommand.CpuPolicy.Version.ShouldBe(CpuPolicyVersion.active);
        document.CpuPolicy.Version.ShouldBe(CpuPolicyVersion.active);
        document.StartCommand.CpuPolicy.Seed.ShouldBe(document.Start.Seed.Value);
        document.CpuPolicy.Seed.ShouldBe(document.Start.Seed.Value);
        document.StartCommand.CpuPolicy.DecisionIndex.ShouldBe(0UL);
        document.CpuPolicy.DecisionIndex.ShouldBe(
            (ulong)document.Commands.Count(static command => command.Actor.Value == "cpu:local")
        );
        document.CpuPolicy.DecisionIndex.ShouldBeGreaterThan(0UL);
        MatchCpuPolicy.isValid(document.StartCommand.CpuPolicy).ShouldBeTrue();
        MatchCpuPolicy.isValid(document.CpuPolicy).ShouldBeTrue();
        await AssertEquivalent(refreshed.Match!, started.Match);
        await AssertEquivalent(restarted.Match!, started.Match);
        (await fixture.Store.Read("match")).ShouldBe(stored);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
    }

    [Test]
    public async Task MatchStart_DefaultsToNormalDifficulty()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);

        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );

        started.Match!.Difficulty.ShouldBe(CpuDifficultyView.Normal);
    }

    [Test]
    public async Task SameProfileAndSeed_ProduceTheSameCpuLogAndState()
    {
        await using var firstDatabase = await TestDatabase.Create();
        await using var secondDatabase = await TestDatabase.Create();
        var first = await ReadyFixture.Create(firstDatabase);
        var profileDocument = await first.Store.Read("profile");
        var secondStore = new StateDocumentStore(secondDatabase);
        await secondStore.Create("profile", profileDocument!.Json);
        var second = ReadyFixture.FromExisting(secondDatabase, first.Catalogue);

        var firstStarted = Value(
            await first.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var secondStarted = Value(
            await second.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var firstMatchDocument = await first.Store.Read("match");
        var secondMatchDocument = await second.Store.Read("match");

        await AssertEquivalent(secondStarted.Match!, firstStarted.Match!);
        secondMatchDocument!.Json.ShouldBe(firstMatchDocument!.Json);
    }

    [Test]
    public async Task DifferentMatchCommands_DeriveDifferentPersistedPolicySeeds()
    {
        await using var firstDatabase = await TestDatabase.Create();
        await using var secondDatabase = await TestDatabase.Create();
        var first = await ReadyFixture.Create(firstDatabase);
        var profileDocument = await first.Store.Read("profile");
        var secondStore = new StateDocumentStore(secondDatabase);
        await secondStore.Create("profile", profileDocument!.Json);
        var second = ReadyFixture.FromExisting(secondDatabase, first.Catalogue);
        var otherMatchCommand = Guid.Parse("30000000-0000-0000-0000-000000000002");

        Value(await first.Application.StartMatch(new(_matchCommand, _firstDeckCommand)));
        Value(await second.Application.StartMatch(new(otherMatchCommand, _firstDeckCommand)));
        var firstDocument = StoredMatch((await first.Store.Read("match"))!);
        var secondDocument = StoredMatch((await second.Store.Read("match"))!);

        firstDocument.StartCommand.CpuPolicy.Seed.ShouldBe(firstDocument.Start.Seed.Value);
        secondDocument.StartCommand.CpuPolicy.Seed.ShouldBe(secondDocument.Start.Seed.Value);
        secondDocument.StartCommand.CpuPolicy.Seed.ShouldNotBe(
            firstDocument.StartCommand.CpuPolicy.Seed
        );
    }

    [Test]
    [Arguments("{broken", "match.document_corrupt")]
    [Arguments("version", "match.document_version")]
    public async Task InvalidMatchJson_IsTypedAndNonMutating(string corruption, string errorCode)
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        Value(await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand)));
        var original = await fixture.Store.Read("match");
        var invalidJson =
            corruption == "version"
                ? original!.Json.Replace("\"schemaVersion\":3", "\"schemaVersion\":999")[..^1]
                    + ",\"futureField\":true}"
                : corruption;
        await fixture.Store.Update("match", original!.Revision, invalidJson);
        var invalid = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        state.Match.ShouldBeNull();
        state.MatchError!.Code.ShouldBe(errorCode);
        after.ShouldBe(invalid);
        state.Profile.ShouldNotBeNull();
        state.Decks.ShouldHaveSingleItem();
    }

    [Test]
    public async Task UnsupportedCpuPolicyVersion_IsTypedAndPreservesTheMatchAndProfile()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        Value(await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand)));
        var original = (await fixture.Store.Read("match"))!;
        var profile = await fixture.Store.Read("profile");
        var future = JsonNode.Parse(original.Json)!.AsObject();
        future["startCommand"]!["cpuPolicy"]!["version"] = 999;
        future["cpuPolicy"]!["version"] = 999;
        await fixture.Store.Update("match", original.Revision, future.ToJsonString());
        var savedFuture = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());

        state.Match.ShouldBeNull();
        state.MatchError!.Code.ShouldBe("match.cpu_policy_version");
        (await fixture.Store.Read("match")).ShouldBe(savedFuture);
        (await fixture.Store.Read("profile")).ShouldBe(profile);
    }

    [Test]
    public async Task RejectedPersistedCommandLog_IsTypedAndNonMutating()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var action = started.Match!.LegalActions[0];
        Value(
            await fixture.Application.ApplyMatchAction(
                started.Match.Frame.Id,
                RequestFor(started.Match, action, Guid.NewGuid())
            )
        );
        var original = await fixture.Store.Read("match");
        const string revisionMarker = "\"expectedRevision\":{\"value\":";
        var revisionStart = original!.Json.IndexOf(revisionMarker, StringComparison.Ordinal);
        var revisionEnd = original.Json.IndexOf('}', revisionStart);
        var invalidJson =
            original.Json[..revisionStart]
            + revisionMarker
            + "99}"
            + original.Json[(revisionEnd + 1)..];
        invalidJson.ShouldNotBe(original.Json);
        await fixture.Store.Update("match", original.Revision, invalidJson);
        var invalid = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        state.Match.ShouldBeNull();
        state.MatchError!.Code.ShouldBe("match.replay_invalid");
        after.ShouldBe(invalid);
    }

    [Test]
    public async Task PersistedCommandWithoutItsChoicesMember_LoadsWithNoChoices()
    {
        // A stored command that never had its choices member written loads as a command with no
        // choices, which is what the saved battle has always promised: the collection type behind
        // it changed in BLOKEMON-069, the promise did not.
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var applied = Value(
            await fixture.Application.ApplyMatchAction(
                started.Match!.Frame.Id,
                RequestFor(started.Match, started.Match.LegalActions[0], Guid.NewGuid())
            )
        );
        var original = await fixture.Store.Read("match");
        var strippedJson = original!.Json.Replace(
            ",\"choices\":[]",
            string.Empty,
            StringComparison.Ordinal
        );
        strippedJson.ShouldNotBe(original.Json);
        await fixture.Store.Update("match", original.Revision, strippedJson);
        var stripped = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        state.MatchError.ShouldBeNull();
        state.Match.ShouldNotBeNull();
        await AssertEquivalent(state.Match, applied.Match!);
        after.ShouldBe(stripped);
    }

    [Test]
    [Arguments("absent")]
    [Arguments("null")]
    public async Task PersistedCommandWithoutItsActionMember_IsTypedAndNonMutating(string damage)
    {
        // The other half of the same promise: a stored command whose action member is absent, or
        // written as an explicit null, carries a null union that the deserializer does not refuse.
        // Reading its case would throw a NullReferenceException past every JsonException handler
        // around the load, so the ingress guard rejects the saved battle as damaged instead.
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        Value(
            await fixture.Application.ApplyMatchAction(
                started.Match!.Frame.Id,
                RequestFor(started.Match, started.Match.LegalActions[0], Guid.NewGuid())
            )
        );
        var original = await fixture.Store.Read("match");
        var document = JsonNode.Parse(original!.Json)!.AsObject();
        var command = document["commands"]!.AsArray()[0]!.AsObject();
        if (damage == "absent")
        {
            command.Remove("action");
        }
        else
        {
            command["action"] = null;
        }
        var damagedJson = document.ToJsonString();
        damagedJson.ShouldNotBe(original.Json);
        await fixture.Store.Update("match", original.Revision, damagedJson);
        var damaged = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        state.Match.ShouldBeNull();
        state.MatchError!.Code.ShouldBe("match.replay_invalid");
        state.MatchError.Message.ShouldBe("The saved battle is damaged. No data changed.");
        after.ShouldBe(damaged);
        state.Profile.ShouldNotBeNull();
        state.Decks.ShouldHaveSingleItem();
    }

    [Test]
    public async Task SubmittedActionWithoutItsChoicesMember_IsAcceptedWithNoChoices()
    {
        // The same promise on the wire: a submitted payload that never mentions its choices is
        // read as a submission with no choices rather than refused.
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var (view, action) = await ReachChoicelessAction(fixture.Application, started);
        var match = view.Match!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var wire = JsonSerializer
            .Serialize(RequestFor(match, action, Guid.NewGuid()), options)
            .Replace(",\"choices\":[]", string.Empty, StringComparison.Ordinal);
        wire.ShouldNotContain("choices");
        var submitted = JsonSerializer.Deserialize<ApplyMatchActionRequest>(wire, options)!;

        var applied = Value(await fixture.Application.ApplyMatchAction(match.Frame.Id, submitted));

        submitted.Choices.ShouldBeNull();
        applied.MatchError.ShouldBeNull();
        applied.Match!.Frame.Revision.ShouldBeGreaterThan(match.Frame.Revision);
    }

    [Test]
    public async Task ActiveMatchCannotBeReplaced()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        Value(await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand)));
        var original = await fixture.Store.Read("match");

        var rejected = await fixture.Application.StartMatch(new(Guid.NewGuid(), _firstDeckCommand));
        var after = await fixture.Store.Read("match");

        Error(rejected).Code.ShouldBe("match.active");
        after.ShouldBe(original);
    }

    [Test]
    public async Task Resigning_AppendsOneActionAndReloadsAsACompletedMatchWithTheRecordedWinner()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );
        var match = started.Match!;
        var resign = match.LegalActions.Single(static action =>
            action.Kind == MatchActionKindView.Resign
        );
        var before = await fixture.Store.Read("match");

        var resigned = Value(
            await fixture.Application.ApplyMatchAction(
                match.Frame.Id,
                RequestFor(match, resign, Guid.NewGuid())
            )
        );
        var after = await fixture.Store.Read("match");
        var restored = Value(await fixture.Restart().State());
        var rejected = await fixture.Application.ApplyMatchAction(
            match.Frame.Id,
            RequestFor(match, resign, Guid.NewGuid())
        );

        resigned.Match!.Frame.IsComplete.ShouldBeTrue();
        resigned.Match.Frame.Winner.ShouldBe("The Regular");
        resigned.Match.LegalActions.ShouldBeEmpty();
        after!.Revision.ShouldBe(before!.Revision + 1);
        after.Json.ShouldContain("\"$command\":\"resign\"");
        restored.Match!.Frame.IsComplete.ShouldBeTrue();
        restored.Match.Frame.Winner.ShouldBe("The Regular");
        restored.MatchError.ShouldBeNull();
        await AssertEquivalent(restored.Match, resigned.Match);
        Error(rejected).Code.ShouldBe("match.complete");
    }

    [Test]
    public async Task CompletedMatchCanBeReplacedByANewStart()
    {
        await using var database = await TestDatabase.Create();
        var fixture = await ReadyFixture.Create(database);
        var started = Value(
            await fixture.Application.StartMatch(new(_matchCommand, _firstDeckCommand))
        );

        var completed = await CompleteMatch(fixture.Application, started);
        var nextCommand = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var replaced = Value(
            await fixture.Application.StartMatch(new(nextCommand, _firstDeckCommand))
        );

        completed.Match!.Frame.IsComplete.ShouldBeTrue();
        completed.Match.Frame.Winner.ShouldNotBeNull();
        replaced.Match!.Frame.Id.ShouldBe(nextCommand);
        replaced.Match.Frame.IsComplete.ShouldBeFalse();
    }

    // The first move the local player can submit that asks them nothing, so the submitted payload
    // is one whose choice list is legitimately empty.
    private static async Task<(ApplicationView View, MatchActionView Action)> ReachChoicelessAction(
        LocalApplicationService application,
        ApplicationView initial
    )
    {
        var current = initial;
        for (var step = 0; step < 24; step++)
        {
            var match = current.Match!;
            var choiceless = match.LegalActions.FirstOrDefault(static candidate =>
                candidate.ChoiceRequirements.Length == 0
                && candidate.Kind != MatchActionKindView.Resign
            );
            if (choiceless is not null)
            {
                return (current, choiceless);
            }

            current = Value(
                await application.ApplyMatchAction(
                    match.Frame.Id,
                    RequestFor(match, ForwardAction(match)!, Guid.NewGuid())
                )
            );
        }

        throw new InvalidOperationException("The match never offered a move with no choices.");
    }

    // The opening taken with somebody put on the Bench beside the Active, which is what makes a
    // retreat a move the match would offer at all.
    private static async Task<ApplicationView> ChooseOpeningWithBench(
        LocalApplicationService application,
        MatchView opening
    )
    {
        var action = OpeningAction(opening);
        var request = RequestFor(opening, action, Guid.NewGuid());
        var oche = action.Id["opening:".Length..];
        var boothRequirement = action.ChoiceRequirements.Single(static requirement =>
            requirement.Id == "opening:booth"
        );
        var booth = boothRequirement.EligibleCards.First(card => card.Id != oche);
        var selection = SelectionFor(boothRequirement) with { CardInstanceIds = [booth.Id] };

        var placed = Value(
            await application.ApplyMatchAction(
                opening.Frame.Id,
                request with
                {
                    Choices =
                    [
                        .. request.Choices.Select(choice =>
                            choice.Id == boothRequirement.Id ? selection : choice
                        ),
                    ],
                }
            )
        );

        return await AdvanceThroughSetup(application, placed);
    }

    // Setup no longer ends when the Active is standing: the mulligan bonus is drawn after it, and
    // a Basic that came with the bonus may then go to the Bench.
    private static async Task<ApplicationView> AdvanceThroughSetup(
        LocalApplicationService application,
        ApplicationView current
    )
    {
        for (var count = 0; count < 8; count++)
        {
            var match = current.Match!;
            if (match.Frame.Phase == MatchPhaseView.Playing || match.LegalActions.Length == 0)
            {
                return current;
            }

            current = Value(
                await application.ApplyMatchAction(
                    match.Frame.Id,
                    RequestFor(match, match.LegalActions[0], Guid.NewGuid())
                )
            );
        }

        return current;
    }

    private static async Task<ApplicationView> AdvanceToOpeningChoice(
        LocalApplicationService application,
        ApplicationView initial
    )
    {
        var current = initial;
        for (var count = 0; count < 8; count++)
        {
            if (
                current.Match!.LegalActions.Any(action =>
                    action.ChoiceRequirements.Any(requirement => requirement.Id == "opening:booth")
                )
            )
            {
                return current;
            }

            var action = current.Match.LegalActions[0];
            current = Value(
                await application.ApplyMatchAction(
                    current.Match.Frame.Id,
                    RequestFor(current.Match, action, Guid.NewGuid())
                )
            );
        }

        throw new InvalidOperationException("The opening choice was not reached.");
    }

    private static async Task<ApplicationView> CompleteMatch(
        LocalApplicationService application,
        ApplicationView initial
    )
    {
        var current = initial;
        for (var count = 0; count < 256; count++)
        {
            if (current.Match!.Frame.IsComplete)
            {
                return current;
            }
            if (current.Match.LegalActions.Length == 0)
            {
                throw new InvalidOperationException("The local player has no legal action.");
            }

            var action =
                current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.Attack
                )
                ?? current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.AttachEnergy
                )
                ?? current.Match.LegalActions.FirstOrDefault(static action =>
                    action.Kind == MatchActionKindView.EndTurn
                )
                ?? current.Match.LegalActions[0];
            current = Value(
                await application.ApplyMatchAction(
                    current.Match.Frame.Id,
                    RequestFor(current.Match, action, Guid.NewGuid())
                )
            );
        }

        throw new InvalidOperationException("The match did not complete inside the test bound.");
    }

    // The least interesting legal move: settle anything the engine is waiting on, otherwise end
    // the round. Resigning is never taken, so the walk always makes progress through the match.
    private static MatchActionView? ForwardAction(MatchView match) =>
        match.LegalActions.FirstOrDefault(static action =>
            action.Kind
                is MatchActionKindView.ChooseMulliganBonus
                    or MatchActionKindView.ChooseOpening
                    or MatchActionKindView.ChooseReplacement
                    or MatchActionKindView.ResolveChoice
                    or MatchActionKindView.ResolveKnockout
                    or MatchActionKindView.TakePrize
                    or MatchActionKindView.DiscardFossil
        )
        ?? match.LegalActions.FirstOrDefault(static action =>
            action.Kind == MatchActionKindView.EndTurn
        )
        ?? match.LegalActions.FirstOrDefault(static action =>
            action.Kind != MatchActionKindView.Resign
        );

    private static IEnumerable<MatchCardInstanceView> AllInstances(MatchFrameView frame) =>
        SideInstances(frame.Player).Concat(SideInstances(frame.Opponent));

    private static IEnumerable<MatchCardInstanceView> SideInstances(MatchSideView side) =>
        (side.Active is { } active ? new[] { active } : [])
            .Concat(side.Bench)
            .Concat(side.Hand)
            .Concat(side.InPlayKits);

    private static void AssertNoHiddenOpponentCards(MatchView match)
    {
        AssertNoHiddenOpponentCardInstances(match.Frame);
        foreach (var requirement in match.LegalActions.SelectMany(static a => a.ChoiceRequirements))
        {
            AssertEntitledCandidates(requirement.EligibleCards);
            AssertEntitledCandidates(requirement.EligibleTargets);
        }
    }

    private static void AssertNoHiddenOpponentCardInstances(MatchFrameView frame)
    {
        frame.Opponent.Hand.ShouldBeEmpty();
        AssertEntitledCandidates(AllInstances(frame));
    }

    // Nothing the opponent keeps to themselves - their hand, their deck, their face-down Prize
    // Cards - may be projected with a face on it.
    private static void AssertEntitledCandidates(IEnumerable<MatchCardInstanceView> cards)
    {
        foreach (var card in cards.Where(static card => card.OwnerName != "Local Player"))
        {
            card.Zone.ShouldBeOneOf("Oche", "Booth", "Local", "Attached", "Empties Tray");
        }
    }

    private static async Task<(MatchView Match, MatchActionView Action)> ReachCiggySamAttack(
        ChoiceMatchFixture fixture
    )
    {
        var started = Match(
            await fixture.Service.Start(
                fixture.Profile,
                "Local Player",
                new(_matchCommand, _firstDeckCommand)
            )
        );
        for (var count = 0; count < 8; count++)
        {
            if (
                started.LegalActions.Any(action =>
                    action.Id.StartsWith("opening:", StringComparison.Ordinal)
                )
            )
            {
                break;
            }
            var preliminary = started.LegalActions[0];
            started = Match(
                await fixture.Service.Apply(
                    fixture.Profile,
                    "Local Player",
                    started.Frame.Id,
                    RequestFor(started, preliminary, Guid.NewGuid())
                )
            );
        }
        var openingCards = started
            .LegalActions.SelectMany(static action => action.ChoiceRequirements)
            .SelectMany(static requirement => requirement.EligibleCards)
            .DistinctBy(static card => card.Id)
            .ToDictionary(static card => card.Id, StringComparer.Ordinal);
        var opening = started.LegalActions.First(action =>
            action.Id.StartsWith("opening:", StringComparison.Ordinal)
            && openingCards[action.Id["opening:".Length..]].Card.Id == "BLK-016"
        );
        var ocheId = opening.Id["opening:".Length..];
        var openingRequest = RequestFor(started, opening, Guid.NewGuid());
        var opened = Match(
            await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                started.Frame.Id,
                openingRequest
            )
        );
        for (var count = 0; count < 8 && opened.Frame.Phase != MatchPhaseView.Playing; count++)
        {
            opened = Match(
                await fixture.Service.Apply(
                    fixture.Profile,
                    "Local Player",
                    opened.Frame.Id,
                    RequestFor(opened, opened.LegalActions[0], Guid.NewGuid())
                )
            );
        }
        var attach = opened.LegalActions.First(action =>
            action.Id.StartsWith("attach:", StringComparison.Ordinal)
            && action.Id.EndsWith($":{ocheId}", StringComparison.Ordinal)
        );
        var attached = Match(
            await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                opened.Frame.Id,
                RequestFor(opened, attach, Guid.NewGuid())
            )
        );
        var endRound = attached.LegalActions.Single(action => action.Id == "end");
        var nextRound = Match(
            await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                attached.Frame.Id,
                RequestFor(attached, endRound, Guid.NewGuid())
            )
        );
        var attack = nextRound.LegalActions.SingleOrDefault(action =>
            action.Id == $"attack:{ocheId}:BLK-016-B01"
        );
        if (attack is null)
        {
            var secondAttach = nextRound.LegalActions.FirstOrDefault(action =>
                action.Id.StartsWith("attach:", StringComparison.Ordinal)
                && action.Id.EndsWith($":{ocheId}", StringComparison.Ordinal)
            );
            if (secondAttach is not null)
            {
                nextRound = Match(
                    await fixture.Service.Apply(
                        fixture.Profile,
                        "Local Player",
                        nextRound.Frame.Id,
                        RequestFor(nextRound, secondAttach, Guid.NewGuid())
                    )
                );
                attack = nextRound.LegalActions.SingleOrDefault(action =>
                    action.Id == $"attack:{ocheId}:BLK-016-B01"
                );
            }
        }
        if (attack is null)
        {
            throw new InvalidOperationException(
                $"The {nextRound.Frame.Player.Active?.Card.Id} attack was unavailable: {string.Join(", ", nextRound.LegalActions.Select(static action => action.Id))}"
            );
        }
        attack.ChoiceRequirements.Single().Kind.ShouldBe(MatchChoiceKindView.Cards);
        return (nextRound, attack);
    }

    private static MatchActionView OpeningAction(MatchView match) =>
        match.LegalActions.First(action =>
            action.ChoiceRequirements.Any(requirement => requirement.Id == "opening:booth")
        );

    private static ApplyMatchActionRequest RequestFor(
        MatchView match,
        MatchActionView action,
        Guid commandId
    ) =>
        new(
            commandId,
            match.Frame.Revision,
            action.Id,
            action
                .ChoiceRequirements.Where(static requirement => requirement.Chooser.IsLocalPlayer)
                .Where(requirement =>
                    requirement.DependsOnOptional is null
                    || action
                        .ChoiceRequirements.Single(parent =>
                            parent.Id == requirement.DependsOnOptional
                        )
                        .Kind != MatchChoiceKindView.Optional
                )
                .Select(SelectionFor)
                .ToArray()
        );

    private static MatchChoiceSelectionRequest SelectionFor(
        MatchChoiceRequirementView requirement
    ) =>
        requirement.Kind switch
        {
            MatchChoiceKindView.Optional => EmptySelection(requirement) with { Accepted = false },
            MatchChoiceKindView.Amount => EmptySelection(requirement) with
            {
                Amount = requirement.Minimum,
            },
            MatchChoiceKindView.Cards => EmptySelection(requirement) with
            {
                CardInstanceIds = requirement
                    .EligibleCards.Take(requirement.Minimum)
                    .Select(static card => card.Id)
                    .ToArray(),
            },
            MatchChoiceKindView.MechanicalType => EmptySelection(requirement) with
            {
                MechanicalType = requirement.EligibleMechanicalTypes[0].Value,
            },
            MatchChoiceKindView.Attack => EmptySelection(requirement) with
            {
                EffectId = requirement.EligibleEffects[0].Id,
            },
            MatchChoiceKindView.Distribution => EmptySelection(requirement) with
            {
                Distribution = [new(requirement.EligibleCards[0].Id, requirement.Maximum)],
            },
            MatchChoiceKindView.Attachments => EmptySelection(requirement) with
            {
                Attachments = requirement
                    .EligibleCards.Take(requirement.Minimum)
                    .Select(card => new MatchAttachmentRequest(
                        card.Id,
                        requirement.EligibleTargets[0].Id
                    ))
                    .ToArray(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(requirement)),
        };

    private static MatchChoiceSelectionRequest EmptySelection(
        MatchChoiceRequirementView requirement
    ) => new(requirement.Id, requirement.Kind, null, null, [], null, null, [], []);

    private static ApplicationView Value(ApiResponse<MatchMutationView> response) =>
        Value<MatchMutationView>(response).Application;

    private static T Value<T>(ApiResponse<T> response)
        where T : class
    {
        if (!response.Succeeded || response.Value is null)
        {
            throw new InvalidOperationException(response.Error?.Message);
        }
        return response.Value;
    }

    private static ApiError Error<T>(ApiResponse<T> response)
    {
        if (response.Succeeded || response.Error is null)
        {
            throw new InvalidOperationException("Expected an API failure.");
        }
        return response.Error;
    }

    private static MatchView Match(MatchServiceResult response)
    {
        if (response.Error is not null || response.View is null)
        {
            throw new InvalidOperationException(response.Error?.Message);
        }
        return response.View;
    }

    private static TValue ProductValue<TValue, TFailure>(DomainResult<TValue, TFailure> result)
        where TValue : notnull
        where TFailure : notnull =>
        result is DomainResult<TValue, TFailure>.Succeeded succeeded
            ? succeeded.Value
            : throw new InvalidOperationException("The product fixture transition failed.");

    private static LocalProfile CreateChoiceProfile(BlokemonCatalogue catalogue)
    {
        var profile = ProductValue(
            LocalProfile.Create(
                ProductValue(ProfileId.Create("00000000-0000-0000-0000-000000000003")),
                ProductValue(DisplayName.Create("Local Player")),
                catalogue.Mechanics
            )
        );
        for (var index = 1; index <= 4; index++)
        {
            var identity = $"70000000-0000-0000-0000-{index:D12}";
            profile = ProductValue(
                profile.OpenPack(
                    ProductValue(CommandId.Create(identity)),
                    ProductValue(PackReceiptId.Create(identity)),
                    catalogue.Mechanics,
                    new BlokemonSeededRandom(22)
                )
            ).Profile;
        }
        return ProductValue(
            profile.CreateDeck(
                ProductValue(DeckId.Create(_firstDeckCommand.ToString("D"))),
                ProductValue(DeckName.Create("Choice deck")),
                [
                    new(ProductValue(CardId.Create("BLK-016")), 4),
                    new(ProductValue(CardId.Create("VIM-BLAZED")), 56),
                ],
                catalogue.Mechanics
            )
        ).Profile;
    }

    private static async Task AssertEquivalent(MatchView actual, MatchView expected)
    {
        JsonSerializer.Serialize(actual).ShouldBe(JsonSerializer.Serialize(expected));
    }

    private static MatchDocument StoredMatch(StoredDocument stored) =>
        JsonSerializer.Deserialize<MatchDocument>(stored.Json, MatchJson.Options)
        ?? throw new InvalidOperationException("The saved match document did not deserialize.");

    private sealed record PersistedProfileDocument(
        int SchemaVersion,
        Guid CreationCommandId,
        LocalProfileSnapshot Profile
    );

    private sealed record ChoiceMatchFixture(
        BlokemonCatalogue Catalogue,
        TestDatabase Database,
        StateDocumentStore Store,
        LocalMatchService Service,
        LocalProfile Profile
    )
    {
        public static ChoiceMatchFixture Create(TestDatabase database)
        {
            var catalogue = BlokemonCatalogueBuilder.Load(
                Path.Combine(AppContext.BaseDirectory, "content")
            );
            var profile = CreateChoiceProfile(catalogue);
            var store = new StateDocumentStore(database);
            return new(catalogue, database, store, new(catalogue, store), profile);
        }

        public LocalMatchService Restart() => new(Catalogue, new StateDocumentStore(Database));
    }

    private sealed record ReadyFixture(
        BlokemonCatalogue Catalogue,
        TestDatabase Database,
        StateDocumentStore Store,
        LocalApplicationService Application
    )
    {
        public static async Task<ReadyFixture> Create(
            TestDatabase database,
            bool includeSecondDeck = false
        )
        {
            var catalogue = BlokemonCatalogueBuilder.Load(
                Path.Combine(AppContext.BaseDirectory, "content")
            );
            var fixture = FromExisting(database, catalogue);
            Value(await fixture.Application.CreateProfile(new(_profileCommand, "Local Player")));
            Value(
                await fixture.Application.SaveDeck(
                    new(
                        _firstDeckCommand,
                        null,
                        null,
                        "First deck",
                        [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                    )
                )
            );
            if (includeSecondDeck)
            {
                Value(
                    await fixture.Application.SaveDeck(
                        new(
                            _secondDeckCommand,
                            null,
                            null,
                            "Second deck",
                            [new("BLK-001", 1), new("VIM-BLAZED", 59)]
                        )
                    )
                );
            }
            return fixture;
        }

        public static async Task<ReadyFixture> CreateOpeningChoice(TestDatabase database)
        {
            var catalogue = BlokemonCatalogueBuilder.Load(
                Path.Combine(AppContext.BaseDirectory, "content")
            );
            var fixture = FromExisting(database, catalogue);
            var profile = CreateChoiceProfile(catalogue);
            var document = JsonSerializer.Serialize(
                new PersistedProfileDocument(3, _profileCommand, profile.ToSnapshot()),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
            if (await fixture.Store.Create("profile", document) is not DocumentWriteResult.Written)
            {
                throw new InvalidOperationException("The opening-choice profile was not stored.");
            }
            return fixture;
        }

        public static ReadyFixture FromExisting(TestDatabase database, BlokemonCatalogue catalogue)
        {
            var store = new StateDocumentStore(database);
            return new(
                catalogue,
                database,
                store,
                new(
                    catalogue,
                    store,
                    new LocalMatchService(catalogue, store),
                    EconomyRules.Unlimited,
                    ProfileAuthorityPolicy.Preserve
                )
            );
        }

        public LocalApplicationService Restart()
        {
            var store = new StateDocumentStore(Database);
            return new(
                Catalogue,
                store,
                new LocalMatchService(Catalogue, store),
                EconomyRules.Unlimited,
                ProfileAuthorityPolicy.Preserve
            );
        }
    }

    private sealed class TestDatabase : IDbContextFactory<BlokemonDbContext>, IAsyncDisposable
    {
        private readonly string _path;
        private readonly DbContextOptions<BlokemonDbContext> _options;

        private TestDatabase(string path)
        {
            _path = path;
            _options = new DbContextOptionsBuilder<BlokemonDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public static async Task<TestDatabase> Create()
        {
            var database = new TestDatabase(
                Path.Combine(AppContext.BaseDirectory, $"match-{Guid.NewGuid():N}.db")
            );
            await using var context = database.CreateDbContext();
            await context.Database.MigrateAsync();
            return database;
        }

        public BlokemonDbContext CreateDbContext() => new(_options);

        public Task<BlokemonDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = _path + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
