using Blokemon.App;
using Blokemon.App.Client;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Application;

/// <summary>Recovery codes to be shown once, and where the screen goes when they are acknowledged.</summary>
public sealed record RecoveryCodesToShow(string[] Codes, string ContinueLabel, string ContinueTo);

/// <summary>
/// The first-party flows: create an account, sign in, add a passkey, set a password, make new
/// codes, recover. A passkey flow is the server's options, the browser's ceremony, the server's
/// verdict; a password flow is one call. On a sign-in the session is held and server-backed
/// play selected, as the hand-off flow does. The codes a flow produces are kept here for the
/// one screen that shows them.
/// </summary>
public sealed class PasskeyFlow(
    PasskeyApiClient api,
    PasskeyCeremony ceremony,
    SessionHolder holder,
    TenantContext tenants,
    IPlayModeOperations modes,
    IApplicationStateRefresher refresher
)
{
    public static readonly ApiError Declined = new("passkey.declined", "No passkey was used.");

    public static readonly ApiError Unsupported = new(
        "passkey.unsupported",
        "This browser cannot use passkeys."
    );

    /// <summary>Codes waiting to be shown; cleared once the screen has shown them.</summary>
    public RecoveryCodesToShow? PendingCodes { get; private set; }

    public void ShownCodes() => PendingCodes = null;

    public async Task<ApiResponse<AccountRegistrationView>> CreateAccount(
        string displayName,
        CancellationToken cancellationToken = default
    )
    {
        var options = await api.RegisterOptions(displayName, cancellationToken);
        if (!options.Succeeded || options.Value is null)
        {
            return Fail<AccountRegistrationView>(options.Error);
        }

        var credential = await ceremony.Create(options.Value.Options, cancellationToken);
        if (credential is null)
        {
            return Fail<AccountRegistrationView>(Declined);
        }

        var registered = await api.Register(
            new(options.Value.Challenge, credential.Value, Slug()),
            cancellationToken
        );
        if (registered.Succeeded && registered.Value is { } view)
        {
            await Hold(view.Session, cancellationToken);
            PendingCodes = new(view.RecoveryCodes, "Continue to your game", "/");
        }

        return registered;
    }

    public async Task<ApiResponse<AccountRegistrationView>> CreateAccountWithPassword(
        string name,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var registered = await api.RegisterWithPassword(
            new(name, password, Slug()),
            cancellationToken
        );
        if (registered.Succeeded && registered.Value is { } view)
        {
            await Hold(view.Session, cancellationToken);
            PendingCodes = new(view.RecoveryCodes, "Continue to your game", "/");
        }

        return registered;
    }

    public async Task<ApiResponse<IssuedSessionView>> SignInWithPassword(
        string name,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var signedIn = await api.SignInWithPassword(new(name, password, Slug()), cancellationToken);
        if (signedIn.Succeeded && signedIn.Value is { } session)
        {
            await Hold(session, cancellationToken);
        }

        return signedIn;
    }

    /// <summary>
    /// Sets the held session's account's password, with a player name when it has none yet.
    /// From a recovery session this is the replacement: the server ends that session and the
    /// person signs in with the password.
    /// </summary>
    public async Task<ApiResponse<PasswordSetView>> SetPassword(
        string? name,
        string password,
        string continueLabel,
        string continueTo,
        CancellationToken cancellationToken = default
    )
    {
        var set = await api.SetPassword(new(name, password), cancellationToken);
        if (set.Succeeded && set.Value is { } view)
        {
            if (holder.Current?.Recovery is true)
            {
                await holder.Discard(cancellationToken);
            }

            if (view.RecoveryCodes is { } codes)
            {
                PendingCodes = new(codes, continueLabel, continueTo);
            }
        }

        return set;
    }

    public async Task<ApiResponse<IssuedSessionView>> SignIn(
        CancellationToken cancellationToken = default
    )
    {
        var options = await api.AuthenticateOptions(cancellationToken);
        if (!options.Succeeded || options.Value is null)
        {
            return Fail<IssuedSessionView>(options.Error);
        }

        var credential = await ceremony.Get(options.Value.Options, cancellationToken);
        if (credential is null)
        {
            return Fail<IssuedSessionView>(Declined);
        }

        var signedIn = await api.Authenticate(
            new(options.Value.Challenge, credential.Value, Slug()),
            cancellationToken
        );
        if (signedIn.Succeeded && signedIn.Value is { } session)
        {
            await Hold(session, cancellationToken);
        }

        return signedIn;
    }

    /// <summary>Consumes a recovery code; the held session can then only enrol a replacement.</summary>
    public async Task<ApiResponse<IssuedSessionView>> Recover(
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var recovered = await api.Recover(new(code, Slug()), cancellationToken);
        if (recovered.Succeeded && recovered.Value is { } session)
        {
            await holder.Establish(session, cancellationToken);
        }

        return recovered;
    }

    /// <summary>
    /// Adds a passkey to the held session's account. From a recovery session this is the
    /// replacement: the server ends that session and the person signs in with the passkey.
    /// </summary>
    public async Task<ApiResponse<PasskeyEnrolmentView>> AddPasskey(
        string continueLabel,
        string continueTo,
        CancellationToken cancellationToken = default
    )
    {
        var options = await api.EnrolOptions(cancellationToken);
        if (!options.Succeeded || options.Value is null)
        {
            return Fail<PasskeyEnrolmentView>(options.Error);
        }

        var credential = await ceremony.Create(options.Value.Options, cancellationToken);
        if (credential is null)
        {
            return Fail<PasskeyEnrolmentView>(Declined);
        }

        var enrolled = await api.Enrol(
            new(options.Value.Challenge, credential.Value, Slug()),
            cancellationToken
        );
        if (enrolled.Succeeded && enrolled.Value is { } view)
        {
            if (holder.Current?.Recovery is true)
            {
                await holder.Discard(cancellationToken);
            }

            if (view.RecoveryCodes is { } codes)
            {
                PendingCodes = new(codes, continueLabel, continueTo);
            }
        }

        return enrolled;
    }

    public async Task<ApiResponse<RecoveryCodesView>> MakeNewCodes(
        string continueLabel,
        string continueTo,
        CancellationToken cancellationToken = default
    )
    {
        var made = await api.MakeNewCodes(cancellationToken);
        if (made.Succeeded && made.Value is { } view)
        {
            PendingCodes = new(view.Codes, continueLabel, continueTo);
        }

        return made;
    }

    public Task<ApiResponse<PasskeyStateView>> State(
        CancellationToken cancellationToken = default
    ) => api.Credentials(cancellationToken);

    private async Task Hold(IssuedSessionView session, CancellationToken cancellationToken)
    {
        await holder.Establish(session, cancellationToken);
        await modes.SelectMode(PlayMode.ServerBacked, cancellationToken);
        await refresher.Refresh(cancellationToken);
    }

    private string? Slug() =>
        tenants.Current is { } tenant
        && !string.Equals(tenant.Slug, Tenants.DefaultSlug.Value, StringComparison.Ordinal)
            ? tenant.Slug
            : null;

    private static ApiResponse<T> Fail<T>(ApiError? error) =>
        new(false, default, error ?? new("unavailable", "Sign-in is not available right now."));
}
