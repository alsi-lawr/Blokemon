using System.Net.Http.Json;
using System.Text.Json;

namespace Blokemon.Web.Client.Api;

public sealed record ApiError(string Code, string Message);

public sealed record ApiResponse<T>(bool Succeeded, T? Value, ApiError? Error);

public sealed record ProfileView(Guid Id, string DisplayName, long Revision, int UnopenedPacks);

public enum CardKindView
{
    Blokemon,
    Kit,
    BasicVim,
}

public sealed record CardView(
    string Id,
    string Name,
    CardKindView Kind,
    string Type,
    string Detail,
    string ArtUrl,
    int OwnedQuantity,
    bool FreelyAvailable
);

public sealed record PackReceiptView(Guid Id, int Sequence, CardView[] Cards);

public sealed record DeckEntryView(string CardId, int Quantity);

public sealed record DeckView(
    Guid Id,
    string Name,
    long Revision,
    DeckEntryView[] Entries,
    bool IsLegal,
    string[] Errors
)
{
    public int CardCount => Entries.Sum(static entry => entry.Quantity);
}

public sealed record MatchSideView(
    string Name,
    int StackCount,
    int MittCount,
    int BarChits,
    CardView? Oche,
    CardView[] Booth,
    int Damage,
    int PrintedStayingPower,
    bool HasTurn
);

public enum MatchChoiceKindView
{
    Optional,
    Amount,
    Cards,
    MechanicalType,
    Attack,
    Distribution,
    Attachments,
}

public sealed record MatchChooserView(string Id, string Name, bool IsLocalPlayer);

public sealed record MatchCardInstanceView(
    string Id,
    CardView Card,
    string OwnerName,
    string Zone,
    int Damage
);

public sealed record MatchMechanicalTypeOptionView(string Value, string Label);

public sealed record MatchEffectOptionView(string Id, string Label);

public sealed record MatchCardTypesView(string CardInstanceId, string[] MechanicalTypes);

public sealed record MatchChoiceRequirementView(
    string Id,
    MatchChoiceKindView Kind,
    string Label,
    MatchChooserView Chooser,
    int Minimum,
    int Maximum,
    MatchCardInstanceView[] EligibleCards,
    MatchMechanicalTypeOptionView[] EligibleMechanicalTypes,
    MatchEffectOptionView[] EligibleEffects,
    string? DependsOnOptional,
    MatchCardInstanceView[] EligibleTargets,
    bool RequireDifferentMechanicalTypes,
    MatchCardTypesView[] EligibleCardTypes
);

public sealed record MatchActionView(
    string Id,
    string Label,
    bool Primary,
    MatchChoiceRequirementView[] ChoiceRequirements
);

public sealed record MatchView(
    Guid Id,
    long Revision,
    int Round,
    string Status,
    MatchSideView Opponent,
    MatchSideView Player,
    MatchActionView[] LegalActions,
    string[] RecentEvents,
    bool IsComplete,
    string? Winner
);

public sealed record ApplicationView(
    ProfileView? Profile,
    CardView[] Cards,
    DeckView[] Decks,
    PackReceiptView? LastPack,
    MatchView? Match,
    ApiError? MatchError
);

public sealed record CreateProfileRequest(Guid CommandId, string DisplayName);

public sealed record OpenPackRequest(Guid CommandId);

public sealed record SaveDeckRequest(
    Guid CommandId,
    Guid? DeckId,
    long? ExpectedRevision,
    string Name,
    DeckEntryView[] Entries
);

public sealed record StartMatchRequest(Guid CommandId, Guid DeckId);

public sealed record ApplyMatchActionRequest(
    Guid CommandId,
    long ExpectedRevision,
    string ActionId,
    MatchChoiceSelectionRequest[] Choices
);

public sealed record MatchDamageAllocationRequest(string CardInstanceId, int Counters);

public sealed record MatchAttachmentRequest(string VimCardInstanceId, string BlokeCardInstanceId);

public sealed record MatchChoiceSelectionRequest(
    string Id,
    MatchChoiceKindView Kind,
    bool? Accepted,
    int? Amount,
    string[] CardInstanceIds,
    string? MechanicalType,
    string? EffectId,
    MatchDamageAllocationRequest[] Distribution,
    MatchAttachmentRequest[] Attachments
);

public sealed class BlokemonApiClient(HttpClient http)
{
    public Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    ) => Get<ApplicationView>("api/state", cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) => Post<CreateProfileRequest, ApplicationView>("api/profile", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    ) => Post<OpenPackRequest, ApplicationView>("api/packs/open", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Post<SaveDeckRequest, ApplicationView>("api/decks", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    ) => Post<StartMatchRequest, ApplicationView>("api/matches", request, cancellationToken);

    public Task<ApiResponse<ApplicationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<ApplyMatchActionRequest, ApplicationView>(
            $"api/matches/{matchId:D}/actions",
            request,
            cancellationToken
        );

    private async Task<ApiResponse<T>> Get<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetFromJsonAsync<ApiResponse<T>>(path, cancellationToken)
                ?? Unavailable<T>();
        }
        catch (HttpRequestException)
        {
            return Unavailable<T>();
        }
        catch (JsonException)
        {
            return Unavailable<T>();
        }
        catch (NotSupportedException)
        {
            return Unavailable<T>();
        }
    }

    private async Task<ApiResponse<TResponse>> Post<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, request, cancellationToken);
            return await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>(
                    cancellationToken
                ) ?? Unavailable<TResponse>();
        }
        catch (HttpRequestException)
        {
            return Unavailable<TResponse>();
        }
        catch (JsonException)
        {
            return Unavailable<TResponse>();
        }
        catch (NotSupportedException)
        {
            return Unavailable<TResponse>();
        }
    }

    private static ApiResponse<T> Unavailable<T>() =>
        new(false, default, new("unavailable", "The local game service is unavailable."));
}
