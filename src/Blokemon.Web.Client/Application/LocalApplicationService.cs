using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.Core.SetDesign;
using Blokemon.Product;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Content;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Application;

public sealed class LocalApplicationService(
    BlokemonCatalogue catalogue,
    IStateDocumentStore documents,
    LocalMatchService matches,
    EconomyRules economy
) : IBlokemonApplication
{
    private const string _profileKey = "profile";
    private const int _productSchemaVersion = 2;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        return loaded.Error is not null
            ? Failure<ApplicationView>(loaded.Error)
            : Success(await ToView(loaded.Profile, cancellationToken));
    }

    public async Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ApplicationView>(loaded.Error);
        }
        if (loaded.Profile is not null)
        {
            return loaded.Profile.Document.CreationCommandId == request.CommandId
                ? Success(await ToView(loaded.Profile, cancellationToken))
                : Failure<ApplicationView>(
                    new("profile.exists", "This machine already has a local profile.")
                );
        }

        var displayName = DisplayName.Create(request.DisplayName);
        if (displayName is DomainResult<DisplayName, DisplayNameCreationFailure>.Failed invalidName)
        {
            return Failure<ApplicationView>(
                new(
                    "profile.display_name",
                    invalidName.Error == DisplayNameCreationFailure.TooLong
                        ? "The display name must be 32 characters or fewer."
                        : "Enter a display name."
                )
            );
        }
        var persistedProfileId = Guid.NewGuid();
        var profileId = Required(ProfileId.Create(persistedProfileId.ToString("D")));
        var created = LocalProfile.Create(
            profileId,
            ((DomainResult<DisplayName, DisplayNameCreationFailure>.Succeeded)displayName).Value,
            catalogue.Mechanics,
            economy
        );
        if (created is DomainResult<LocalProfile, LocalProfileCreationFailure>.Failed)
        {
            return Failure<ApplicationView>(
                new(
                    "profile.authority",
                    "The current card set does not contain a starter Blokemon."
                )
            );
        }

        var profile = (
            (DomainResult<LocalProfile, LocalProfileCreationFailure>.Succeeded)created
        ).Value;
        var document = new ProductDocument(
            _productSchemaVersion,
            request.CommandId,
            profile.ToSnapshot()
        );
        var write = await documents.Create(
            _profileKey,
            JsonSerializer.Serialize(document, _json),
            cancellationToken
        );
        return write is DocumentWriteResult.Written written
            ? Success(
                await ToView(
                    new(
                        written.Revision,
                        document,
                        profile,
                        new(
                            persistedProfileId,
                            new Dictionary<DeckId, Guid>(),
                            new Dictionary<PackReceiptId, Guid>()
                        )
                    ),
                    cancellationToken
                )
            )
            : Failure<ApplicationView>(Conflict());
    }

    public async Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ApplicationView>(loaded.Error);
        }
        if (loaded.Profile is null)
        {
            return Failure<ApplicationView>(
                new("profile.required", "Create a local profile before opening a pack.")
            );
        }

        var commandId = Required(CommandId.Create(request.CommandId.ToString("D")));
        var receiptId = Required(PackReceiptId.Create(request.CommandId.ToString("D")));
        var transition = loaded.Profile.Profile.OpenPack(
            commandId,
            receiptId,
            catalogue.Mechanics,
            new BlokemonSeededRandom(PackSeed(loaded.Profile.Profile.Id, commandId))
        );
        if (transition is DomainResult<PackOpenTransition, PackOpenFailure>.Failed failed)
        {
            return Failure<ApplicationView>(PackFailure(failed.Error));
        }

        var opened = (
            (DomainResult<PackOpenTransition, PackOpenFailure>.Succeeded)transition
        ).Value;
        if (opened.Disposition == PackOpenDisposition.AlreadyOpened)
        {
            return Success(await ToView(loaded.Profile, cancellationToken));
        }

        var updated = loaded.Profile with
        {
            Profile = opened.Profile,
            Document = loaded.Profile.Document with { Profile = opened.Profile.ToSnapshot() },
        };
        return await Save(updated, cancellationToken);
    }

    public async Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ApplicationView>(loaded.Error);
        }
        if (loaded.Profile is null)
        {
            return Failure<ApplicationView>(
                new("profile.required", "Create a player before you choose a starter deck.")
            );
        }
        if (request.CommandId == Guid.Empty)
        {
            return Failure<ApplicationView>(
                new("starter.command_id", "Choose the starter deck again.")
            );
        }
        if (catalogue.StarterDecks.Find(request.StarterDeckId) is not { } selected)
        {
            return Failure<ApplicationView>(
                new("starter.not_found", "Choose one of the available starter decks.")
            );
        }

        var commandId = Required(CommandId.Create(request.CommandId.ToString("D")));
        var definition = StarterDefinition(selected);
        var transition = loaded.Profile.Profile.ClaimStarterDeck(
            commandId,
            definition,
            catalogue.Mechanics
        );
        if (
            transition
            is DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Failed failed
        )
        {
            return Failure<ApplicationView>(StarterFailure(failed.Error));
        }

        var outcome = (
            (DomainResult<StarterDeckClaimOutcome, StarterDeckClaimFailure>.Succeeded)transition
        ).Value;
        if (outcome is StarterDeckClaimOutcome.AlreadyClaimed)
        {
            return Success(await ToView(loaded.Profile, cancellationToken));
        }

        var claimed = (StarterDeckClaimOutcome.Claimed)outcome;
        var updated = loaded.Profile with
        {
            Profile = claimed.Profile,
            Document = loaded.Profile.Document with { Profile = claimed.Profile.ToSnapshot() },
        };
        return await Save(updated, cancellationToken);
    }

    public async Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<ApplicationView>(loaded.Error);
        }
        if (loaded.Profile is null)
        {
            return Failure<ApplicationView>(
                new("profile.required", "Create a player before you save a deck.")
            );
        }

        var nameResult = DeckName.Create(request.Name);
        if (nameResult is DomainResult<DeckName, TextValueFailure>.Failed)
        {
            return Failure<ApplicationView>(new("deck.name", "Enter a deck name."));
        }
        var deckId = Required(DeckId.Create((request.DeckId ?? request.CommandId).ToString("D")));
        var selections = new List<DeckCardSelection>(request.Entries.Length);
        foreach (var entry in request.Entries)
        {
            var cardId = CardId.Create(entry.CardId);
            if (cardId is DomainResult<CardId, TextValueFailure>.Failed)
            {
                return Failure<ApplicationView>(
                    new("deck.card_id", "The deck contains an unknown card.")
                );
            }
            selections.Add(
                new(
                    ((DomainResult<CardId, TextValueFailure>.Succeeded)cardId).Value,
                    entry.Quantity
                )
            );
        }

        var name = ((DomainResult<DeckName, TextValueFailure>.Succeeded)nameResult).Value;
        if (
            loaded.Profile.Profile.SavedDecks.TryGetValue(deckId, out var existing)
            && SameDeck(existing, name, selections)
        )
        {
            return Success(await ToView(loaded.Profile, cancellationToken));
        }

        DomainResult<DeckSaveTransition, DeckSaveFailure> transition;
        if (request.DeckId is null)
        {
            transition = loaded.Profile.Profile.CreateDeck(
                deckId,
                name,
                selections,
                catalogue.Mechanics
            );
        }
        else
        {
            if (request.ExpectedRevision is null)
            {
                return Failure<ApplicationView>(
                    new("deck.revision", "The saved deck changed. Reload the page.")
                );
            }
            var revision = DeckRevision.Create(request.ExpectedRevision.Value);
            if (revision is DomainResult<DeckRevision, DeckRevisionFailure>.Failed)
            {
                return Failure<ApplicationView>(
                    new("deck.revision", "The saved deck changed. Reload the page.")
                );
            }
            transition = loaded.Profile.Profile.ReviseDeck(
                deckId,
                ((DomainResult<DeckRevision, DeckRevisionFailure>.Succeeded)revision).Value,
                name,
                selections,
                catalogue.Mechanics
            );
        }

        if (transition is DomainResult<DeckSaveTransition, DeckSaveFailure>.Failed failed)
        {
            return Failure<ApplicationView>(DeckFailure(failed.Error));
        }
        var saved = ((DomainResult<DeckSaveTransition, DeckSaveFailure>.Succeeded)transition).Value;
        var updated = loaded.Profile with
        {
            Profile = saved.Profile,
            Document = loaded.Profile.Document with { Profile = saved.Profile.ToSnapshot() },
        };
        return await Save(updated, cancellationToken);
    }

    public async Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<MatchMutationView>(loaded.Error);
        }
        if (loaded.Profile is null)
        {
            return Failure<MatchMutationView>(
                new("profile.required", "Create a local profile before starting a match.")
            );
        }

        var match = await matches.Start(
            loaded.Profile.Profile,
            loaded.Profile.Profile.DisplayName.Value,
            request,
            cancellationToken
        );
        return match.Error is not null
            ? Failure<MatchMutationView>(match.Error)
            : Success<MatchMutationView>(
                new MatchMutationView(
                    await ToView(loaded.Profile, cancellationToken, match),
                    match.Presentation
                )
            );
    }

    public async Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadProfile(cancellationToken);
        if (loaded.Error is not null)
        {
            return Failure<MatchMutationView>(loaded.Error);
        }
        if (loaded.Profile is null)
        {
            return Failure<MatchMutationView>(
                new("profile.required", "Create a local profile before playing a match.")
            );
        }

        var match = await matches.Apply(
            loaded.Profile.Profile,
            loaded.Profile.Profile.DisplayName.Value,
            matchId,
            request,
            cancellationToken
        );
        return match.Error is not null
            ? Failure<MatchMutationView>(match.Error)
            : Success<MatchMutationView>(
                new MatchMutationView(
                    await ToView(loaded.Profile, cancellationToken, match),
                    match.Presentation
                )
            );
    }

    private async Task<ApiResponse<ApplicationView>> Save(
        LoadedProfile loaded,
        CancellationToken cancellationToken
    )
    {
        var ids = WebLocalIds.TryCreate(loaded.Profile);
        if (ids is null)
        {
            return Failure<ApplicationView>(InvalidStateError());
        }

        var write = await documents.Update(
            _profileKey,
            loaded.Revision,
            JsonSerializer.Serialize(loaded.Document, _json),
            cancellationToken
        );
        return write is DocumentWriteResult.Written written
            ? Success(
                await ToView(
                    loaded with
                    {
                        Revision = written.Revision,
                        Ids = ids,
                    },
                    cancellationToken
                )
            )
            : Failure<ApplicationView>(Conflict());
    }

    public async Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    )
    {
        await matches.PurgeSavedMatches(cancellationToken);
        await documents.Delete(_profileKey, cancellationToken);
        return Success(await ToView(null, cancellationToken));
    }

    private async Task<ProfileLoad> LoadProfile(CancellationToken cancellationToken)
    {
        var stored = await documents.Read(_profileKey, cancellationToken);
        if (stored is null)
        {
            return new(null, null);
        }

        ProductDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProductDocument>(stored.Json, _json);
        }
        catch (JsonException)
        {
            return InvalidState();
        }
        if (document is null || document.SchemaVersion != _productSchemaVersion)
        {
            return InvalidState();
        }

        var restored = LocalProfile.Restore(document.Profile, catalogue.Mechanics);
        if (
            restored
                is not DomainResult<
                    LocalProfile,
                    LocalProfileRestorationFailure
                >.Succeeded succeeded
            || WebLocalIds.TryCreate(succeeded.Value) is not { } ids
        )
        {
            return InvalidState();
        }

        return new(new(stored.Revision, document, succeeded.Value, ids), null);
    }

    private async Task<ApplicationView> ToView(
        LoadedProfile? loaded,
        CancellationToken cancellationToken,
        MatchServiceResult? knownMatch = null
    )
    {
        if (loaded is null)
        {
            var emptyCards = catalogue.CardsWithOwnership(new Dictionary<string, int>());
            return new(
                null,
                emptyCards,
                [],
                StarterViews(new HashSet<string>(StringComparer.Ordinal), emptyCards),
                catalogue.PackPresentation,
                null,
                null,
                null
            );
        }

        var ownership = loaded.Profile.CollectibleOwnership.ToDictionary(
            static entry => entry.Key.Value,
            static entry => entry.Value,
            StringComparer.Ordinal
        );
        var currentCards = catalogue.Cards.ToDictionary(
            static card => card.Id,
            StringComparer.Ordinal
        );
        var cards = currentCards
            .Keys.Concat(ownership.Keys)
            .Concat(
                loaded.Profile.SavedDecks.Values.SelectMany(static deck =>
                    deck.Cards.Keys.Select(static cardId => cardId.Value)
                )
            )
            .Concat(
                loaded.Profile.PackReceipts.Values.SelectMany(static receipt =>
                    receipt.SampledCollectibleIds.Select(static cardId => cardId.Value)
                )
            )
            .Distinct(StringComparer.Ordinal)
            .Select(id => CurrentCard(id, ownership, currentCards))
            .OrderBy(static card => card.Kind)
            .ThenBy(static card => card.Id, StringComparer.Ordinal)
            .ToArray();
        var decks = loaded
            .Profile.SavedDecks.Values.OrderBy(static deck => deck.Name.Value)
            .Select(deck => DeckView(loaded.Profile, deck, loaded.Ids.Decks[deck.Id]))
            .ToArray();
        var lastPack = loaded
            .Profile.PackReceipts.Values.OrderByDescending(static receipt => receipt.Sequence)
            .Select(receipt => new PackReceiptView(
                loaded.Ids.PackReceipts[receipt.Id],
                receipt.Sequence,
                receipt
                    .SampledCollectibleIds.Select(id =>
                        CurrentCard(id.Value, ownership, currentCards)
                    )
                    .ToArray()
            ))
            .FirstOrDefault();
        var match =
            knownMatch
            ?? await matches.State(
                loaded.Profile,
                loaded.Profile.DisplayName.Value,
                cancellationToken
            );
        return new(
            new(
                loaded.Ids.Profile,
                loaded.Profile.DisplayName.Value,
                loaded.Revision,
                loaded.Profile.LatestStarterDeckClaim?.Id.Value,
                loaded.Profile.RemainingPackAllowance,
                loaded.Profile.RemainingStarterDeckClaimAllowance is { } remainingClaims
                    ? remainingClaims == 0
                    : null
            ),
            cards,
            decks,
            StarterViews(
                loaded
                    .Profile.StarterDeckClaims.Select(static claim => claim.Id.Value)
                    .ToHashSet(StringComparer.Ordinal),
                cards
            ),
            catalogue.PackPresentation,
            lastPack,
            match.View,
            match.Error
        );
    }

    private DeckView DeckView(LocalProfile profile, SavedDeck deck, Guid deckId)
    {
        var validation = DeckValidator.Validate(
            profile,
            catalogue.Mechanics,
            deck.Cards.Select(static entry => new DeckCardSelection(entry.Key, entry.Value))
        );
        var issues = validation is DeckValidationResult.Invalid invalid
            ? invalid.Issues.Select(DeckIssue).ToArray()
            : [];
        var warnings = issues.Length == 0 ? DeckWarnings(deck) : [];
        return new(
            deckId,
            deck.Name.Value,
            deck.Revision.Value,
            deck.Cards.OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(static entry => new DeckEntryView(entry.Key.Value, entry.Value))
                .ToArray(),
            issues.Length == 0,
            issues,
            warnings
        );
    }

    private StarterDeckView[] StarterViews(
        IReadOnlySet<string> claimedIds,
        IReadOnlyCollection<CardView> cards
    )
    {
        var currentCards = cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);
        return catalogue
            .StarterDecks.Decks.OrderBy(static deck => deck.Id, StringComparer.Ordinal)
            .Select(deck => new StarterDeckView(
                deck.Id,
                deck.Name,
                deck.Type,
                deck.Role,
                deck.Description,
                currentCards[deck.LeaderCardId],
                deck.Entries.Select(static entry => new DeckEntryView(entry.CardId, entry.Quantity))
                    .ToArray(),
                deck.Entries.Where(entry =>
                        currentCards[entry.CardId].Kind == CardKindView.Blokemon
                    )
                    .Sum(static entry => entry.Quantity),
                deck.Entries.Where(entry => currentCards[entry.CardId].Kind == CardKindView.Kit)
                    .Sum(static entry => entry.Quantity),
                deck.Entries.Where(entry =>
                        currentCards[entry.CardId].Kind == CardKindView.BasicVim
                    )
                    .Sum(static entry => entry.Quantity),
                claimedIds.Contains(deck.Id)
            ))
            .ToArray();
    }

    private string[] DeckWarnings(SavedDeck deck)
    {
        var includedEnergy = deck
            .Cards.Keys.Select(static id => id.Value)
            .Where(static id => id.StartsWith("VIM-", StringComparison.Ordinal))
            .Select(id =>
                catalogue.Mechanics.BasicVim.Single(card =>
                    string.Equals(card.Id, id, StringComparison.Ordinal)
                )
            )
            .Select(static energy => energy.MechanicalType)
            .ToHashSet();
        if (includedEnergy.Count == 0)
        {
            return ["This deck has no Basic Energy. Its Blokemon cannot attack."];
        }

        var hasPayableAttack = deck
            .Cards.Keys.Select(static id => id.Value)
            .Where(static id => id.StartsWith("BLK-", StringComparison.Ordinal))
            .Select(id =>
                catalogue.Mechanics.Collectibles.Single(card =>
                    string.Equals(card.Id, id, StringComparison.Ordinal)
                )
            )
            .SelectMany(static card => card.Attacks)
            .Any(attack =>
                attack.VimCost.All(cost =>
                    cost == BlokemonMechanicalType.Colorless || includedEnergy.Contains(cost)
                )
            );
        return hasPayableAttack ? [] : ["The Basic Energy in this deck cannot pay for an attack."];
    }

    private CardView CurrentCard(
        string id,
        IReadOnlyDictionary<string, int> ownership,
        IReadOnlyDictionary<string, CardView> currentCards
    ) =>
        currentCards.TryGetValue(id, out var current)
            ? current with
            {
                OwnedQuantity = ownership.GetValueOrDefault(id),
            }
            : new(
                id,
                "Unavailable card",
                CardKindView.Blokemon,
                "Historical",
                "Not in the current card set",
                catalogue.ReverseFaceHtml,
                [],
                ownership.GetValueOrDefault(id),
                false
            );

    private static bool SameDeck(
        SavedDeck existing,
        DeckName name,
        IEnumerable<DeckCardSelection> selections
    )
    {
        var requested = selections
            .GroupBy(static entry => entry.CardId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(row => row.Quantity)
            );
        return existing.Name == name
            && existing.Cards.Count == requested.Count
            && existing.Cards.All(entry => requested.GetValueOrDefault(entry.Key) == entry.Value);
    }

    private static string DeckIssue(DeckValidationIssue issue) =>
        issue.Match(
            static (cardId, _) => $"{cardId.Value} must have a positive quantity.",
            static (actual, required) =>
                $"The deck has {actual} cards. It must have {required} cards.",
            static cardId => $"{cardId.Value} is not in the current card set.",
            static (cardId, actual, allowed) =>
                $"{cardId.Value} has {actual} copies. The limit is {allowed}.",
            static () => "The deck needs at least one Regular Blokemon.",
            static (cardId, requested, owned) =>
                $"{cardId.Value} requests {requested} copies, but only {owned} are owned.",
            static cardId => $"{cardId.Value} is not freely available."
        );

    private static ApiError DeckFailure(DeckSaveFailure failure) =>
        failure.Match<ApiError>(
            static _ => new("deck.exists", "That deck already exists."),
            static _ => new("deck.not_found", "The saved deck no longer exists."),
            static (_, _, _) => new("deck.stale", "The saved deck changed. Reload the page."),
            static issues => new("deck.invalid", string.Join(" ", issues.Select(DeckIssue))),
            static _ => new("deck.revision", "The saved deck changed. Reload the page.")
        );

    private static StarterDeckDefinition StarterDefinition(StarterDeck starter) =>
        new(
            Required(StarterDeckId.Create(starter.Id)),
            Required(DeckId.Create(starter.SavedDeckId.ToString("D"))),
            Required(DeckName.Create(starter.Name)),
            starter.Entries.Select(entry => new DeckCardSelection(
                Required(CardId.Create(entry.CardId)),
                entry.Quantity
            ))
        );

    private static ApiError StarterFailure(StarterDeckClaimFailure failure) =>
        failure.Match<ApiError>(
            static (_, _, _) =>
                new(
                    "starter.command_conflict",
                    "This request conflicts with a saved choice. Choose the starter deck again."
                ),
            static (_, _) =>
                new(
                    "starter.already_claimed",
                    "This player already opened its Starter Deck. This game allows one."
                ),
            static issues => new("starter.invalid", string.Join(" ", issues.Select(DeckIssue)))
        );

    private static ApiError PackFailure(PackOpenFailure failure) =>
        failure switch
        {
            PackOpenFailure.ReceiptIdAlreadyUsed => new(
                "pack.receipt",
                "This pack was already opened."
            ),
            PackOpenFailure.ElevenCardPackUnavailable => new(
                "pack.authority",
                "The current card set cannot supply an 11-card pack."
            ),
            PackOpenFailure.AuthorityVersionMismatch => new(
                "pack.authority_changed",
                "The card set changed. Reload the page before you open a pack."
            ),
            PackOpenFailure.PackAllowanceExhausted => new(
                "pack.allowance",
                "You have opened every pack this player is allowed."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

    private static ulong PackSeed(ProfileId profileId, CommandId commandId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{profileId.Value}:{commandId.Value}"));
        return BitConverter.ToUInt64(bytes);
    }

    private static TValue Required<TValue>(DomainResult<TValue, TextValueFailure> result)
        where TValue : notnull =>
        result switch
        {
            DomainResult<TValue, TextValueFailure>.Succeeded succeeded => succeeded.Value,
            DomainResult<TValue, TextValueFailure>.Failed => throw new UnreachableException(),
            _ => throw new UnreachableException(),
        };

    private static ApiResponse<T> Success<T>(T value) => new(true, value, null);

    private static ApiResponse<T> Failure<T>(ApiError error) => new(false, default, error);

    private static ApiError Conflict() =>
        new("state.conflict", "The saved data changed. Select the action again.");

    private static ProfileLoad InvalidState() => new(null, InvalidStateError());

    private static ApiError InvalidStateError() =>
        new("state.invalid", "The saved player data is damaged. No data changed.");

    private sealed record ProductDocument(
        int SchemaVersion,
        Guid CreationCommandId,
        LocalProfileSnapshot Profile
    );

    private sealed record LoadedProfile(
        long Revision,
        ProductDocument Document,
        LocalProfile Profile,
        WebLocalIds Ids
    );

    private sealed record WebLocalIds(
        Guid Profile,
        IReadOnlyDictionary<DeckId, Guid> Decks,
        IReadOnlyDictionary<PackReceiptId, Guid> PackReceipts
    )
    {
        public static WebLocalIds? TryCreate(LocalProfile profile)
        {
            if (!Guid.TryParse(profile.Id.Value, out var profileId))
            {
                return null;
            }

            var decks = new Dictionary<DeckId, Guid>();
            foreach (var deckId in profile.SavedDecks.Keys)
            {
                if (!Guid.TryParse(deckId.Value, out var parsed))
                {
                    return null;
                }
                decks.Add(deckId, parsed);
            }

            var packReceipts = new Dictionary<PackReceiptId, Guid>();
            foreach (var receiptId in profile.PackReceipts.Keys)
            {
                if (!Guid.TryParse(receiptId.Value, out var parsed))
                {
                    return null;
                }
                packReceipts.Add(receiptId, parsed);
            }

            return new(profileId, decks, packReceipts);
        }
    }

    private sealed record ProfileLoad(LoadedProfile? Profile, ApiError? Error);
}
