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
    StateDocumentStore documents,
    LocalMatchService matches
)
{
    private const string _profileKey = "profile";
    private const int _productSchemaVersion = 1;

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
        var profileId = Required(ProfileId.Create(Guid.NewGuid().ToString("D")));
        var created = LocalProfile.Create(
            profileId,
            ((DomainResult<DisplayName, DisplayNameCreationFailure>.Succeeded)displayName).Value,
            catalogue.Mechanics
        );
        if (created is DomainResult<LocalProfile, LocalProfileCreationFailure>.Failed)
        {
            return Failure<ApplicationView>(
                new(
                    "profile.authority",
                    "The current card authority does not contain a Regular starter Blokemon."
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
            ? Success(await ToView(new(written.Revision, document, profile), cancellationToken))
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
                new("profile.required", "Create a local profile before saving a deck.")
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
                    new("deck.card_id", "Every deck entry must identify a card.")
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
                    new("deck.revision", "The saved deck revision is required.")
                );
            }
            var revision = DeckRevision.Create(request.ExpectedRevision.Value);
            if (revision is DomainResult<DeckRevision, DeckRevisionFailure>.Failed)
            {
                return Failure<ApplicationView>(
                    new("deck.revision", "The saved deck revision is invalid.")
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

    public async Task<ApiResponse<ApplicationView>> StartMatch(
        StartMatchRequest request,
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
            ? Failure<ApplicationView>(match.Error)
            : Success(await ToView(loaded.Profile, cancellationToken, match));
    }

    public async Task<ApiResponse<ApplicationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
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
            ? Failure<ApplicationView>(match.Error)
            : Success(await ToView(loaded.Profile, cancellationToken, match));
    }

    private async Task<ApiResponse<ApplicationView>> Save(
        LoadedProfile loaded,
        CancellationToken cancellationToken
    )
    {
        var write = await documents.Update(
            _profileKey,
            loaded.Revision,
            JsonSerializer.Serialize(loaded.Document, _json),
            cancellationToken
        );
        return write is DocumentWriteResult.Written written
            ? Success(await ToView(loaded with { Revision = written.Revision }, cancellationToken))
            : Failure<ApplicationView>(Conflict());
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
        return restored switch
        {
            DomainResult<LocalProfile, LocalProfileRestorationFailure>.Succeeded succeeded => new(
                new(stored.Revision, document, succeeded.Value),
                null
            ),
            DomainResult<LocalProfile, LocalProfileRestorationFailure>.Failed => InvalidState(),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<ApplicationView> ToView(
        LoadedProfile? loaded,
        CancellationToken cancellationToken,
        MatchServiceResult? knownMatch = null
    )
    {
        if (loaded is null)
        {
            return new(
                null,
                catalogue.CardsWithOwnership(new Dictionary<string, int>()),
                [],
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
        var cards = catalogue.CardsWithOwnership(ownership);
        var decks = loaded
            .Profile.SavedDecks.Values.OrderBy(static deck => deck.Name.Value)
            .Select(deck => DeckView(loaded.Profile, deck))
            .ToArray();
        var lastPack = loaded
            .Profile.PackReceipts.Values.OrderByDescending(static receipt => receipt.Sequence)
            .Select(receipt => new PackReceiptView(
                ParseGuid(receipt.Id.Value, "pack receipt"),
                receipt.Sequence,
                receipt
                    .SampledCollectibleIds.Select(id => CurrentCard(id.Value, ownership))
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
                ParseGuid(loaded.Profile.Id.Value, "profile"),
                loaded.Profile.DisplayName.Value,
                loaded.Revision,
                loaded.Profile.AvailablePackEntitlements
            ),
            cards,
            decks,
            lastPack,
            match.View,
            match.Error
        );
    }

    private DeckView DeckView(LocalProfile profile, SavedDeck deck)
    {
        var currentAuthority = string.Equals(
            profile.BoundAuthorityManifestVersion,
            catalogue.Mechanics.ManifestVersion,
            StringComparison.Ordinal
        );
        var validation = currentAuthority
            ? DeckValidator.Validate(
                profile,
                catalogue.Mechanics,
                deck.Cards.Select(static entry => new DeckCardSelection(entry.Key, entry.Value))
            )
            : null;
        var issues =
            !currentAuthority ? ["The card authority changed. Revalidate this deck before playing."]
            : validation is DeckValidationResult.Invalid invalid
                ? invalid.Issues.Select(DeckIssue).ToArray()
            : [];
        return new(
            ParseGuid(deck.Id.Value, "deck"),
            deck.Name.Value,
            deck.Revision.Value,
            deck.Cards.OrderBy(static entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(static entry => new DeckEntryView(entry.Key.Value, entry.Value))
                .ToArray(),
            issues.Length == 0,
            issues
        );
    }

    private CardView CurrentCard(string id, IReadOnlyDictionary<string, int> ownership) =>
        catalogue.Cards.FirstOrDefault(card => card.Id == id) is { } current
            ? current with
            {
                OwnedQuantity = ownership.GetValueOrDefault(id),
            }
            : new(
                id,
                "Unavailable card",
                CardKindView.Blokemon,
                "Historical",
                "Not in the current authority",
                "/art/card-back.svg",
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
                $"The deck has {actual} cards; exactly {required} are required.",
            static cardId => $"{cardId.Value} is not in the current card authority.",
            static (cardId, actual, allowed) =>
                $"{cardId.Value} has {actual} copies; at most {allowed} are allowed.",
            static () => "The deck needs at least one Regular Blokemon.",
            static (cardId, requested, owned) =>
                $"{cardId.Value} requests {requested} copies, but only {owned} are owned.",
            static cardId => $"{cardId.Value} is not freely available."
        );

    private static ApiError DeckFailure(DeckSaveFailure failure) =>
        failure.Match<ApiError>(
            static _ => new("deck.exists", "That deck already exists."),
            static _ => new("deck.not_found", "The saved deck no longer exists."),
            static (_, _, _) =>
                new(
                    "deck.stale",
                    "The deck changed in another operation. Reload it before saving."
                ),
            static issues => new("deck.invalid", string.Join(" ", issues.Select(DeckIssue))),
            static _ => new("deck.revision", "The deck revision cannot advance."),
            static (_, _) =>
                new(
                    "deck.authority_changed",
                    "The card authority changed. Revalidate the profile before saving decks."
                )
        );

    private static ApiError PackFailure(PackOpenFailure failure) =>
        failure switch
        {
            PackOpenFailure.EntitlementUnavailable => new(
                "pack.empty",
                "No unopened packs remain."
            ),
            PackOpenFailure.ReceiptIdAlreadyUsed => new(
                "pack.receipt",
                "That pack receipt ID is already in use."
            ),
            PackOpenFailure.ElevenCardPackUnavailable => new(
                "pack.authority",
                "The current authority cannot supply an eleven-card pack."
            ),
            PackOpenFailure.AuthorityVersionMismatch => new(
                "pack.authority_changed",
                "The card authority changed. Revalidate the profile before opening packs."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

    private static ulong PackSeed(ProfileId profileId, CommandId commandId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{profileId.Value}:{commandId.Value}"));
        return BitConverter.ToUInt64(bytes);
    }

    private static Guid ParseGuid(string value, string kind) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException($"The persisted {kind} ID is invalid.");

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
        new("state.conflict", "The local state changed in another operation. Retry this action.");

    private static ProfileLoad InvalidState() =>
        new(
            null,
            new("state.invalid", "The persisted local profile is invalid and was left unchanged.")
        );

    private sealed record ProductDocument(
        int SchemaVersion,
        Guid CreationCommandId,
        LocalProfileSnapshot Profile
    );

    private sealed record LoadedProfile(
        long Revision,
        ProductDocument Document,
        LocalProfile Profile
    );

    private sealed record ProfileLoad(LoadedProfile? Profile, ApiError? Error);
}
