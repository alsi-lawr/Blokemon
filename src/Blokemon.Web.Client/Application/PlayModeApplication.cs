using System.Text.Json;
using System.Text.Json.Serialization;
using Blokemon.Web.Application;
using Blokemon.Web.Client.Api;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Client.Application;

public enum PlayMode
{
    ServerBacked,
    BrowserLocal,
}

public sealed record PlayModeAvailability(bool ServerBacked);

public sealed record PlayModeState(
    PlayMode? Selected,
    string? StorageLocation,
    string? BrowserStorageError,
    bool ServerBackedAvailable
);

public sealed class PlayModeApplication(
    BlokemonApiClient server,
    LocalApplicationService browser,
    IStateDocumentStore browserDocuments,
    PlayModeAvailability availability
) : IBlokemonApplication
{
    private const string _settingsKey = "settings";
    private const int _settingsSchemaVersion = 1;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private bool _loaded;
    private long? _settingsRevision;
    private PlayMode? _selected;
    private string? _browserStorageError;

    public async Task<PlayModeState> Mode(CancellationToken cancellationToken = default)
    {
        await EnsureLoaded(cancellationToken);
        return Current();
    }

    public async Task<ApiResponse<PlayModeState>> SelectMode(
        PlayMode mode,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureLoaded(cancellationToken);
        if (mode == PlayMode.ServerBacked && !availability.ServerBacked)
        {
            return Failure<PlayModeState>(
                new("mode.unavailable", "This build saves games in this browser only.")
            );
        }
        if (_selected == mode)
        {
            return Success(Current());
        }

        if (_browserStorageError is not null)
        {
            if (mode == PlayMode.ServerBacked && _settingsRevision is null)
            {
                _selected = mode;
                return Success(Current());
            }
            return Failure<PlayModeState>(new("storage.unavailable", _browserStorageError));
        }

        var settings = JsonSerializer.Serialize(
            new BrowserSettings(_settingsSchemaVersion, mode),
            _json
        );
        try
        {
            var write = _settingsRevision is { } revision
                ? await browserDocuments.Update(_settingsKey, revision, settings, cancellationToken)
                : await browserDocuments.Create(_settingsKey, settings, cancellationToken);
            if (write is not DocumentWriteResult.Written written)
            {
                return Failure<PlayModeState>(
                    new(
                        "settings.changed",
                        "Your save choice changed in another tab. Reload and choose again."
                    )
                );
            }

            _settingsRevision = written.Revision;
            _selected = mode;
            return Success(Current());
        }
        catch (DocumentStorageException exception)
        {
            var message = StorageMessage(exception.Failure);
            if (mode == PlayMode.ServerBacked && _settingsRevision is null)
            {
                _browserStorageError = message;
                _selected = mode;
                return Success(Current());
            }
            return Failure<PlayModeState>(new(StorageCode(exception.Failure), message));
        }
    }

    public Task<ApiResponse<ApplicationView>> State(
        CancellationToken cancellationToken = default
    ) => Invoke(static (application, token) => application.State(token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> CreateProfile(
        CreateProfileRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Invoke(
            (application, token) => application.CreateProfile(request, token),
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> OpenPack(
        OpenPackRequest request,
        CancellationToken cancellationToken = default
    ) => Invoke((application, token) => application.OpenPack(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> ClaimStarterDeck(
        ClaimStarterDeckRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Invoke(
            (application, token) => application.ClaimStarterDeck(request, token),
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> SaveDeck(
        SaveDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Invoke((application, token) => application.SaveDeck(request, token), cancellationToken);

    public Task<ApiResponse<ApplicationView>> DeleteDeck(
        DeleteDeckRequest request,
        CancellationToken cancellationToken = default
    ) => Invoke((application, token) => application.DeleteDeck(request, token), cancellationToken);

    public Task<ApiResponse<MatchMutationView>> StartMatch(
        StartMatchRequest request,
        CancellationToken cancellationToken = default
    ) => Invoke((application, token) => application.StartMatch(request, token), cancellationToken);

    public Task<ApiResponse<MatchMutationView>> ApplyMatchAction(
        Guid matchId,
        ApplyMatchActionRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Invoke(
            (application, token) => application.ApplyMatchAction(matchId, request, token),
            cancellationToken
        );

    public Task<ApiResponse<ApplicationView>> PurgeData(
        CancellationToken cancellationToken = default
    ) => Invoke(static (application, token) => application.PurgeData(token), cancellationToken);

    private async Task<ApiResponse<T>> Invoke<T>(
        Func<IBlokemonApplication, CancellationToken, Task<ApiResponse<T>>> operation,
        CancellationToken cancellationToken
    )
    {
        await EnsureLoaded(cancellationToken);
        if (_selected is null)
        {
            return Failure<T>(
                new("mode.required", "Choose where to save your game on the Home page.")
            );
        }

        try
        {
            return await operation(
                _selected == PlayMode.BrowserLocal ? browser : server,
                cancellationToken
            );
        }
        catch (DocumentStorageException exception)
        {
            return Failure<T>(
                new(StorageCode(exception.Failure), StorageMessage(exception.Failure))
            );
        }
    }

    private async Task EnsureLoaded(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return;
            }

            try
            {
                var stored = await browserDocuments.Read(_settingsKey, cancellationToken);
                if (stored is not null)
                {
                    BrowserSettings? settings;
                    try
                    {
                        settings = JsonSerializer.Deserialize<BrowserSettings>(stored.Json, _json);
                    }
                    catch (JsonException)
                    {
                        settings = null;
                    }

                    if (
                        settings is null
                        || settings.SchemaVersion != _settingsSchemaVersion
                        || !Enum.IsDefined(settings.Mode)
                    )
                    {
                        _browserStorageError =
                            "The saved browser settings are damaged or incompatible. No data changed.";
                    }
                    else
                    {
                        _settingsRevision = stored.Revision;
                        if (settings.Mode != PlayMode.ServerBacked || availability.ServerBacked)
                        {
                            _selected = settings.Mode;
                        }
                    }
                }
            }
            catch (DocumentStorageException exception)
            {
                _browserStorageError = StorageMessage(exception.Failure);
            }
            _loaded = true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private PlayModeState Current() =>
        new(
            _selected,
            _selected switch
            {
                PlayMode.BrowserLocal => "Saved in this browser",
                PlayMode.ServerBacked => "Saved on this server",
                _ => null,
            },
            _browserStorageError,
            availability.ServerBacked
        );

    private static string StorageCode(DocumentStorageFailure failure) =>
        failure switch
        {
            DocumentStorageFailure.Full => "storage.full",
            DocumentStorageFailure.Rejected => "storage.rejected",
            _ => "storage.unavailable",
        };

    private static string StorageMessage(DocumentStorageFailure failure) =>
        failure switch
        {
            DocumentStorageFailure.Full =>
                "This browser is out of storage. Your last saved game is unchanged.",
            DocumentStorageFailure.Rejected =>
                "This browser refused access to its storage. Your last saved game is unchanged.",
            _ => "Browser storage is unavailable. Your last saved game is unchanged.",
        };

    private static ApiResponse<T> Success<T>(T value) => new(true, value, null);

    private static ApiResponse<T> Failure<T>(ApiError error) => new(false, default, error);

    private sealed record BrowserSettings(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] PlayMode Mode
    );
}
