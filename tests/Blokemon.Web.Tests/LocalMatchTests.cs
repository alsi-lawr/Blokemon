using System.Text.Json;
using Blokemon.Core.SetDesign;
using Blokemon.Product;
using Blokemon.Web.Application;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;
using Microsoft.EntityFrameworkCore;

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
        await Assert.That(eligibleBooth.Length).IsGreaterThan(0);
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
        var applied = Value(await fixture.Application.ApplyMatchAction(openingMatch.Id, request));

        var restarted = fixture.Restart();
        var restored = Value(await restarted.State());

        await Assert.That(restored.Profile!.Id).IsEqualTo(applied.Profile!.Id);
        await Assert.That(restored.Decks.Single().Id).IsEqualTo(_firstDeckCommand);
        await Assert.That(restored.Match!.Id).IsEqualTo(applied.Match!.Id);
        await Assert.That(applied.Match.Player.Oche!.Id).IsEqualTo(selectedOche.Card.Id);
        await Assert
            .That(applied.Match.Player.Booth.Select(static card => card.Id))
            .Contains(selectedBooth.Card.Id);
        await AssertEquivalent(restored.Match, applied.Match);
        await Assert.That(restored.MatchError).IsNull();
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
        var conflict = await fixture.Application.StartMatch(new(_matchCommand, _secondDeckCommand));
        var afterConflict = await fixture.Store.Read("match");

        await AssertEquivalent(retried.Match!, started.Match!);
        await Assert.That(afterRetry).IsEqualTo(afterStart);
        await Assert.That(Error(conflict).Code).IsEqualTo("match.command_conflict");
        await Assert.That(afterConflict).IsEqualTo(beforeConflict);
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

        await Assert.That(responses.All(static response => response.Succeeded)).IsTrue();
        await Assert
            .That(responses.Select(static response => response.Value!.Match!.Id).Distinct())
            .HasSingleItem();
        await Assert.That((await fixture.Store.Read("match"))!.Revision).IsEqualTo(1);
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

        var applied = Value(await fixture.Application.ApplyMatchAction(started.Match.Id, request));
        var afterApply = await fixture.Store.Read("match");
        var retried = Value(await fixture.Application.ApplyMatchAction(started.Match.Id, request));
        var afterRetry = await fixture.Store.Read("match");
        var beforeConflict = await fixture.Store.Read("match");
        var conflict = await fixture.Application.ApplyMatchAction(
            started.Match.Id,
            request with
            {
                ActionId = "not-the-original-action",
            }
        );
        var afterConflict = await fixture.Store.Read("match");

        await AssertEquivalent(retried.Match!, applied.Match!);
        await Assert.That(afterRetry).IsEqualTo(afterApply);
        await Assert.That(Error(conflict).Code).IsEqualTo("match.command_conflict");
        await Assert.That(afterConflict).IsEqualTo(beforeConflict);
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
            fixture.Application.ApplyMatchAction(started.Match.Id, request),
            other.ApplyMatchAction(started.Match.Id, request)
        );
        var after = await fixture.Store.Read("match");

        await Assert.That(responses.All(static response => response.Succeeded)).IsTrue();
        await Assert
            .That(responses.Select(static response => response.Value!.Match!.Revision).Distinct())
            .HasSingleItem();
        await Assert.That(after!.Revision).IsEqualTo(before!.Revision + 1);
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
            started.Match.Id,
            RequestFor(started.Match, action, Guid.NewGuid()) with
            {
                ExpectedRevision = started.Match.Revision - 1,
            }
        );
        var illegal = await fixture.Application.ApplyMatchAction(
            started.Match.Id,
            new(Guid.NewGuid(), started.Match.Revision, "missing-action", [])
        );
        var after = await fixture.Store.Read("match");

        await Assert.That(Error(stale).Code).IsEqualTo("match.stale");
        await Assert.That(Error(illegal).Code).IsEqualTo("match.action_illegal");
        await Assert.That(after).IsEqualTo(original);
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
            opening.Match!.Id,
            validRequest with
            {
                Choices = [invalidChoice],
            }
        );
        var afterInvalid = await fixture.Store.Read("match");
        var malformed = await fixture.Application.ApplyMatchAction(
            opening.Match.Id,
            validRequest with
            {
                CommandId = Guid.NewGuid(),
                Choices = [invalidChoice with { Id = null! }],
            }
        );
        var afterMalformed = await fixture.Store.Read("match");
        var missing = await fixture.Application.ApplyMatchAction(
            opening.Match.Id,
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
            opening.Match.Id,
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
            opening.Match.Id,
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
            await fixture.Application.ApplyMatchAction(opening.Match.Id, validRequest)
        );
        var restored = Value(await fixture.Restart().State());

        await Assert.That(Error(invalid).Code).IsEqualTo("match.choice_invalid");
        await Assert.That(Error(malformed).Code).IsEqualTo("match.choice_invalid");
        await Assert.That(Error(missing).Code).IsEqualTo("match.choice_required");
        await Assert.That(Error(extra).Code).IsEqualTo("match.choice_invalid");
        await Assert.That(Error(wrongKind).Code).IsEqualTo("match.choice_invalid");
        await Assert.That(afterInvalid).IsEqualTo(original);
        await Assert.That(afterMalformed).IsEqualTo(original);
        await Assert.That(afterMissing).IsEqualTo(original);
        await Assert.That(afterExtra).IsEqualTo(original);
        await Assert.That(afterWrongKind).IsEqualTo(original);
        await Assert.That(applied.Match!.Revision).IsGreaterThan(opening.Match.Revision);
        await AssertEquivalent(restored.Match!, applied.Match);
    }

    [Test]
    public async Task NonOpeningChoiceFailures_AreTypedAndNonMutating()
    {
        await using var database = await TestDatabase.Create();
        var fixture = ChoiceMatchFixture.Create(database);
        var (match, action) = await ReachCiggySamAttack(fixture);

        await AssertChoiceFailuresDoNotMutate(fixture, match, action);
    }

    [Test]
    public async Task DeferredChoiceFailuresAndRestart_AreTotalAndNonMutating()
    {
        await using var database = await TestDatabase.Create();
        var fixture = ChoiceMatchFixture.Create(database);
        var (deferred, action) = await ReachCiggySamDeferredChoice(fixture);
        var requirement = action.ChoiceRequirements.Single();
        await Assert.That(requirement.Kind).IsEqualTo(MatchChoiceKindView.Cards);
        await Assert.That(requirement.EligibleCards.Length).IsGreaterThan(0);

        await AssertChoiceFailuresDoNotMutate(fixture, deferred, action);
        var beforeRestart = await fixture.Store.Read("match");
        var restartedService = fixture.Restart();
        var restored = Match(await restartedService.State(fixture.Profile, "Local Player"));
        var afterRestart = await fixture.Store.Read("match");
        await AssertEquivalent(restored, deferred);
        await Assert.That(afterRestart).IsEqualTo(beforeRestart);

        var selection = SelectionFor(requirement) with
        {
            CardInstanceIds = [requirement.EligibleCards[0].Id],
        };
        var request = RequestFor(restored, action, Guid.NewGuid()) with { Choices = [selection] };
        var resolved = Match(
            await restartedService.Apply(fixture.Profile, "Local Player", restored.Id, request)
        );
        var restoredResolution = Match(
            await fixture.Restart().State(fixture.Profile, "Local Player")
        );

        await AssertEquivalent(restoredResolution, resolved);
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
        await Assert.That(secondMatchDocument!.Json).IsEqualTo(firstMatchDocument!.Json);
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
                ? original!.Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":999")[..^1]
                    + ",\"futureField\":true}"
                : corruption;
        await fixture.Store.Update("match", original!.Revision, invalidJson);
        var invalid = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        await Assert.That(state.Match).IsNull();
        await Assert.That(state.MatchError!.Code).IsEqualTo(errorCode);
        await Assert.That(after).IsEqualTo(invalid);
        await Assert.That(state.Profile).IsNotNull();
        await Assert.That(state.Decks).HasSingleItem();
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
                started.Match.Id,
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
        await Assert.That(invalidJson).IsNotEqualTo(original.Json);
        await fixture.Store.Update("match", original.Revision, invalidJson);
        var invalid = await fixture.Store.Read("match");

        var state = Value(await fixture.Restart().State());
        var after = await fixture.Store.Read("match");

        await Assert.That(state.Match).IsNull();
        await Assert.That(state.MatchError!.Code).IsEqualTo("match.replay_invalid");
        await Assert.That(after).IsEqualTo(invalid);
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

        await Assert.That(Error(rejected).Code).IsEqualTo("match.active");
        await Assert.That(after).IsEqualTo(original);
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

        await Assert.That(completed.Match!.IsComplete).IsTrue();
        await Assert.That(completed.Match.Winner).IsNotNull();
        await Assert.That(replaced.Match!.Id).IsEqualTo(nextCommand);
        await Assert.That(replaced.Match.IsComplete).IsFalse();
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
                    current.Match.Id,
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
            if (current.Match!.IsComplete)
            {
                return current;
            }
            if (current.Match.LegalActions.Length == 0)
            {
                throw new InvalidOperationException("The local player has no legal action.");
            }

            var action = current.Match.LegalActions[0];
            current = Value(
                await application.ApplyMatchAction(
                    current.Match.Id,
                    RequestFor(current.Match, action, Guid.NewGuid())
                )
            );
        }

        throw new InvalidOperationException("The match did not complete inside the test bound.");
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
        var openingCards = started
            .LegalActions.SelectMany(static action => action.ChoiceRequirements)
            .SelectMany(static requirement => requirement.EligibleCards)
            .DistinctBy(static card => card.Id)
            .ToDictionary(static card => card.Id, StringComparer.Ordinal);
        var opening = started.LegalActions.First(action =>
            action.Id.StartsWith("opening:", StringComparison.Ordinal)
            && openingCards[action.Id["opening:".Length..]].Card.Id == "BLK-016"
            && action
                .ChoiceRequirements.Single(requirement => requirement.Id == "opening:booth")
                .EligibleCards.Any(static card => card.Card.Id == "BLK-001")
        );
        var ocheId = opening.Id["opening:".Length..];
        var boothRequirement = opening.ChoiceRequirements.Single(requirement =>
            requirement.Id == "opening:booth"
        );
        var boothCard = boothRequirement.EligibleCards.Single(static card =>
            card.Card.Id == "BLK-001"
        );
        var openingRequest = RequestFor(started, opening, Guid.NewGuid()) with
        {
            Choices = [SelectionFor(boothRequirement) with { CardInstanceIds = [boothCard.Id] }],
        };
        var opened = Match(
            await fixture.Service.Apply(fixture.Profile, "Local Player", started.Id, openingRequest)
        );
        var attach = opened.LegalActions.First(action =>
            action.Id.StartsWith("attach:", StringComparison.Ordinal)
            && action.Id.EndsWith($":{ocheId}", StringComparison.Ordinal)
        );
        var attached = Match(
            await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                opened.Id,
                RequestFor(opened, attach, Guid.NewGuid())
            )
        );
        var endRound = attached.LegalActions.Single(action => action.Id == "end");
        var nextRound = Match(
            await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                attached.Id,
                RequestFor(attached, endRound, Guid.NewGuid())
            )
        );
        var attack = nextRound.LegalActions.SingleOrDefault(action =>
            action.Id == $"attack:{ocheId}:BLK-016-B01"
        );
        if (attack is null)
        {
            throw new InvalidOperationException(
                $"The {nextRound.Player.Oche?.Id} attack was unavailable: {string.Join(", ", nextRound.LegalActions.Select(static action => action.Id))}"
            );
        }
        await Assert
            .That(attack.ChoiceRequirements.Single().Kind)
            .IsEqualTo(MatchChoiceKindView.Optional);
        return (nextRound, attack);
    }

    private static async Task<(
        MatchView Match,
        MatchActionView Action
    )> ReachCiggySamDeferredChoice(ChoiceMatchFixture fixture)
    {
        var (match, attack) = await ReachCiggySamAttack(fixture);
        var optional = attack.ChoiceRequirements.Single();
        var request = RequestFor(match, attack, Guid.NewGuid()) with
        {
            Choices = [SelectionFor(optional) with { Accepted = true }],
        };
        var deferred = Match(
            await fixture.Service.Apply(fixture.Profile, "Local Player", match.Id, request)
        );
        return (
            deferred,
            deferred.LegalActions.Single(action =>
                action.Id.StartsWith("choice:", StringComparison.Ordinal)
            )
        );
    }

    private static async Task AssertChoiceFailuresDoNotMutate(
        ChoiceMatchFixture fixture,
        MatchView match,
        MatchActionView action
    )
    {
        var original = await fixture.Store.Read("match");
        var requirement = action.ChoiceRequirements.Single();
        var valid = RequestFor(match, action, Guid.NewGuid());
        var wrongKind =
            requirement.Kind == MatchChoiceKindView.Optional
                ? MatchChoiceKindView.Cards
                : MatchChoiceKindView.Optional;
        var cases = new[]
        {
            new ChoiceFailureCase(
                valid with
                {
                    CommandId = Guid.NewGuid(),
                    Choices = [],
                },
                "match.choice_required"
            ),
            new ChoiceFailureCase(
                valid with
                {
                    CommandId = Guid.NewGuid(),
                    Choices =
                    [
                        .. valid.Choices,
                        EmptySelection(requirement) with
                        {
                            Id = "unknown-choice",
                        },
                    ],
                },
                "match.choice_invalid"
            ),
            new ChoiceFailureCase(
                valid with
                {
                    CommandId = Guid.NewGuid(),
                    Choices =
                    [
                        EmptySelection(requirement) with
                        {
                            Kind = wrongKind,
                            Accepted = wrongKind == MatchChoiceKindView.Optional ? false : null,
                        },
                    ],
                },
                "match.choice_invalid"
            ),
        };

        foreach (var candidate in cases)
        {
            var response = await fixture.Service.Apply(
                fixture.Profile,
                "Local Player",
                match.Id,
                candidate.Request
            );
            var after = await fixture.Store.Read("match");
            await Assert.That(response.View).IsNull();
            await Assert.That(response.Error!.Code).IsEqualTo(candidate.ErrorCode);
            await Assert.That(after).IsEqualTo(original);
        }
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
            match.Revision,
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
                    new(ProductValue(CardId.Create("BLK-001")), 1),
                    new(ProductValue(CardId.Create("BLK-016")), 4),
                    new(ProductValue(CardId.Create("VIM-DODGY")), 55),
                ],
                catalogue.Mechanics
            )
        ).Profile;
    }

    private static async Task AssertEquivalent(MatchView actual, MatchView expected)
    {
        await Assert
            .That(JsonSerializer.Serialize(actual))
            .IsEqualTo(JsonSerializer.Serialize(expected));
    }

    private sealed record ChoiceFailureCase(ApplyMatchActionRequest Request, string ErrorCode);

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
            var catalogue = BlokemonCatalogue.Load(
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
            var catalogue = BlokemonCatalogue.Load(
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
                        [new("BLK-001", 1), new("VIM-DODGY", 59)]
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
                            [new("BLK-001", 1), new("VIM-DODGY", 59)]
                        )
                    )
                );
            }
            return fixture;
        }

        public static async Task<ReadyFixture> CreateOpeningChoice(TestDatabase database)
        {
            var catalogue = BlokemonCatalogue.Load(
                Path.Combine(AppContext.BaseDirectory, "content")
            );
            var fixture = FromExisting(database, catalogue);
            var profile = CreateChoiceProfile(catalogue);
            var document = JsonSerializer.Serialize(
                new PersistedProfileDocument(1, _profileCommand, profile.ToSnapshot()),
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
                new(catalogue, store, new LocalMatchService(catalogue, store))
            );
        }

        public LocalApplicationService Restart()
        {
            var store = new StateDocumentStore(Database);
            return new(Catalogue, store, new LocalMatchService(Catalogue, store));
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
