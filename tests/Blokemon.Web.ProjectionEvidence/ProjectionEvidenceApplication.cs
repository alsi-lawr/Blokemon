using Blokemon.App;
using Blokemon.App.Catalogue;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

// This file is compiled into the checked-out Web client only when the browser evidence publish
// sets ProjectionEvidence=true. It supplies raw game state to the real application service; the
// route, MatchView projection, pages, and components remain the production ones under test.
public static class ProjectionEvidenceComposition
{
    private static readonly Guid _profileCommand = Guid.Parse(
        "7f3c9d5a-2e84-4b61-9a07-c58de04f6b93"
    );
    private static readonly Guid _starterCommand = Guid.Parse(
        "a6214f8c-0d73-45be-8c29-f04a7d96e351"
    );
    private static readonly Guid _deckCommand = Guid.Parse("3d98b7e1-6a54-4c02-bf83-27e1a95c640d");
    private static readonly Guid _matchCommand = Guid.Parse("e5b2074c-91fa-43d8-a6ce-708f2b39d154");
    private static readonly Guid _receiptId = Guid.Parse("49ac6e70-b3d2-4f85-970c-d16b8e53a204");
    private static string? _submittedMechanicalType;

    public static async Task AddProjectionEvidence(
        this IServiceCollection services,
        string bootstrapJson
    )
    {
        _submittedMechanicalType = null;
        var catalogue = EvidenceCatalogue(bootstrapJson);
        var documents = new MemoryStateDocumentStore();
        var local = new LocalApplicationService(
            catalogue,
            documents,
            new LocalMatchService(catalogue, documents),
            EconomyRules.Unlimited,
            ProfileAuthorityPolicy.Preserve
        );

        Value(await local.CreateProfile(new(_profileCommand, "Projection Player")));
        Value(await local.ClaimStarterDeck(new(_starterCommand, "growroom")));
        Value(
            await local.SaveDeck(
                new(
                    _deckCommand,
                    null,
                    null,
                    "Projection evidence deck",
                    [new("BLK-137", 4), new("VIM-BEER", 56)]
                )
            )
        );
        var started = Value(await local.StartMatch(new(_matchCommand, _deckCommand))).Application;
        await ReachMechanicalTypeAttack(local, started);

        var evidence = new ProjectionEvidenceApplication(local);
        var modes = new PlayModeApplication(
            evidence,
            evidence,
            documents,
            new PlayModeAvailability(serverBacked: false)
        );
        Value(await modes.SelectMode(PlayMode.BrowserLocal));

        services.AddSingleton(catalogue);
        services.AddSingleton<IStateDocumentStore>(documents);
        services.AddSingleton(evidence);
        services.AddSingleton(modes);
        services.AddSingleton<IBlokemonApplication>(modes);
        services.AddApplicationCapabilities();
        services.AddScoped<CardArtWarmup>();
        services.AddScoped<SoundBoard>();
    }

    [JSInvokable("ProjectionEvidenceSubmittedMechanicalType")]
    public static string? SubmittedMechanicalType() => _submittedMechanicalType;

    private static BlokemonCatalogue EvidenceCatalogue(string bootstrapJson)
    {
        // No checked-in card currently offers Colorless as a mechanical-type choice. The evidence
        // authority adds that raw option to an existing real choice before catalogue validation,
        // so all ten labels still have to travel through MatchViewProjection.
        const string darknessThenDragon =
            "                \\u0022Darkness\\u0022,\\n" + "                \\u0022Dragon\\u0022,";
        const string darknessColorlessThenDragon =
            "                \\u0022Darkness\\u0022,\\n"
            + "                \\u0022Colorless\\u0022,\\n"
            + "                \\u0022Dragon\\u0022,";
        var facebookDadStart = bootstrapJson.IndexOf(
            "\\u0022id\\u0022: \\u0022BLK-137\\u0022",
            StringComparison.Ordinal
        );
        var facebookDadEnd = bootstrapJson.IndexOf(
            "\\u0022id\\u0022: \\u0022BLK-138\\u0022",
            facebookDadStart,
            StringComparison.Ordinal
        );
        var facebookDad = bootstrapJson[facebookDadStart..facebookDadEnd];
        facebookDad = ReplaceSingle(
            facebookDad,
            darknessThenDragon,
            darknessColorlessThenDragon,
            "Facebook Dad mechanical-type choice"
        );
        var bootstrap =
            bootstrapJson[..facebookDadStart] + facebookDad + bootstrapJson[facebookDadEnd..];

        // The deterministic browser profile claims its cards through the normal starter operation.
        // Four collectible slots become Facebook Dad slots only in this evidence bootstrap.
        var starterAuthority = bootstrap.IndexOf("\"starterDecksJson\":", StringComparison.Ordinal);
        var growroomStart = bootstrap.IndexOf(
            "\\u0022id\\u0022: \\u0022growroom\\u0022",
            starterAuthority,
            StringComparison.Ordinal
        );
        var growroomEnd = bootstrap.IndexOf(
            "\\u0022id\\u0022: \\u0022brick-lane-heat\\u0022",
            growroomStart,
            StringComparison.Ordinal
        );
        var growroom = bootstrap[growroomStart..growroomEnd];
        growroom = ReplaceSingle(
            growroom,
            StarterEntry("BLK-127", 2),
            StarterEntry("BLK-137", 4),
            "Growroom BLK-127 entry"
        );
        growroom = ReplaceSingle(
            growroom,
            StarterEntry("BLK-132", 1),
            string.Empty,
            "Growroom BLK-132 entry"
        );
        growroom = ReplaceSingle(
            growroom,
            StarterEntry("BLK-143", 1),
            string.Empty,
            "Growroom BLK-143 entry"
        );
        bootstrap = bootstrap[..growroomStart] + growroom + bootstrap[growroomEnd..];
        return BlokemonCatalogue.FromBootstrapJson(bootstrap);
    }

    private static string StarterEntry(string cardId, int quantity) =>
        "        {\\n"
        + $"          \\u0022cardId\\u0022: \\u0022{cardId}\\u0022,\\n"
        + $"          \\u0022quantity\\u0022: {quantity}\\n"
        + "        },\\n";

    private static string ReplaceSingle(
        string source,
        string expected,
        string replacement,
        string description
    )
    {
        var index = source.IndexOf(expected, StringComparison.Ordinal);
        if (
            index < 0
            || source.IndexOf(expected, index + expected.Length, StringComparison.Ordinal) >= 0
        )
        {
            throw new InvalidOperationException(
                $"The evidence bootstrap did not contain exactly one {description}."
            );
        }
        return source[..index] + replacement + source[(index + expected.Length)..];
    }

    private static async Task ReachMechanicalTypeAttack(
        LocalApplicationService application,
        ApplicationView initial
    )
    {
        var current = initial;
        for (var step = 0; step < 32; step++)
        {
            var match = current.Match!;
            if (
                match.LegalActions.Any(static action =>
                    action.Kind == MatchActionKindView.Attack
                    && action.EffectId == "BLK-137-B01"
                    && action.ChoiceRequirements.Any(static requirement =>
                        requirement.Kind == MatchChoiceKindView.MechanicalType
                        && requirement.EligibleMechanicalTypes.Length == 10
                    )
                )
            )
            {
                return;
            }

            var active = match.Frame.Player.Active;
            var action =
                match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind
                        is MatchActionKindView.ChooseMulliganBonus
                            or MatchActionKindView.ChooseOpening
                            or MatchActionKindView.ChooseBonusPlacement
                            or MatchActionKindView.ChooseReplacement
                            or MatchActionKindView.ResolveChoice
                            or MatchActionKindView.ResolveKnockout
                            or MatchActionKindView.TakePrize
                )
                ?? (
                    active is { AttachedEnergy.Length: 0 }
                        ? match.LegalActions.FirstOrDefault(candidate =>
                            candidate.Kind == MatchActionKindView.AttachEnergy
                            && candidate.TargetCardInstanceId == active.Id
                        )
                        : null
                )
                ?? match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind == MatchActionKindView.EndTurn
                )
                ?? match.LegalActions.FirstOrDefault(static candidate =>
                    candidate.Kind != MatchActionKindView.Resign
                );

            if (action is null)
            {
                break;
            }

            var mutation = Value(
                await application.ApplyMatchAction(match.Frame.Id, RequestFor(match, action))
            );
            current = mutation.Application;
        }

        throw new InvalidOperationException(
            "The deterministic evidence match did not reach Facebook Dad's type choice."
        );
    }

    private static ApplyMatchActionRequest RequestFor(MatchView match, MatchActionView action) =>
        new(
            Guid.NewGuid(),
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

    private static T Value<T>(ApiResponse<T> response)
        where T : class
    {
        if (!response.Succeeded || response.Value is null)
        {
            throw new InvalidOperationException(response.Error?.Message);
        }
        return response.Value;
    }

    private sealed class ProjectionEvidenceApplication(LocalApplicationService inner)
        : IBlokemonApplication
    {
        public async Task<ApiResponse<ApplicationView>> State(
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.State(cancellationToken));

        public async Task<ApiResponse<ApplicationView>> CreateProfile(
            CreateProfileRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.CreateProfile(request, cancellationToken));

        public async Task<ApiResponse<ApplicationView>> OpenPack(
            OpenPackRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.OpenPack(request, cancellationToken));

        public async Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
            ClaimStarterDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.ClaimStarterDeck(request, cancellationToken));

        public async Task<ApiResponse<ApplicationView>> SaveDeck(
            SaveDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.SaveDeck(request, cancellationToken));

        public async Task<ApiResponse<ApplicationView>> DeleteDeck(
            DeleteDeckRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.DeleteDeck(request, cancellationToken));

        public async Task<ApiResponse<MatchMutationView>> StartMatch(
            StartMatchRequest request,
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.StartMatch(request, cancellationToken));

        public async Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
            Guid matchId,
            ApplyMatchActionRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var selected = request
                .Choices.Select(static choice => choice.MechanicalType)
                .FirstOrDefault(static mechanicalType => mechanicalType is not null);
            if (selected is not null)
            {
                _submittedMechanicalType = selected;
            }
            return Decorate(await inner.ApplyMatchAction(matchId, request, cancellationToken));
        }

        public async Task<ApiResponse<ApplicationView>> PurgeData(
            CancellationToken cancellationToken = default
        ) => Decorate(await inner.PurgeData(cancellationToken));

        private static ApiResponse<ApplicationView> Decorate(
            ApiResponse<ApplicationView> response
        ) =>
            response.Value is null
                ? response
                : response with
                {
                    Value = HomeEnergy(response.Value),
                };

        private static ApiResponse<MatchMutationView> Decorate(
            ApiResponse<MatchMutationView> response
        ) =>
            response.Value is null
                ? response
                : response with
                {
                    Value = response.Value with
                    {
                        Application = HomeEnergy(response.Value.Application),
                    },
                };

        private static ApplicationView HomeEnergy(ApplicationView view) =>
            view with
            {
                LastPack = new(
                    _receiptId,
                    1,
                    [view.Cards.Single(static card => card.Id == "VIM-BEER")]
                ),
            };
    }

    private sealed class MemoryStateDocumentStore : IStateDocumentStore
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
            _documents.Add(key, new(1, json));
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
            var revision = current.Revision + 1;
            _documents[key] = new(revision, json);
            return Task.FromResult<DocumentWriteResult>(new DocumentWriteResult.Written(revision));
        }

        public Task Delete(string key, CancellationToken cancellationToken = default)
        {
            _documents.Remove(key);
            return Task.CompletedTask;
        }
    }
}
