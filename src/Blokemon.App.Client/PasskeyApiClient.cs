using Blokemon.App.Contracts;

namespace Blokemon.App.Client;

/// <summary>
/// The first-party routes: the two passkey ceremonies, the simple login, recovery, and the
/// credentials and recovery codes of the account a session names. A route that is not on this
/// server answers with the typed <c>unavailable</c> outcome.
/// </summary>
public sealed class PasskeyApiClient(HttpClient http)
{
    private const string Prefix = "api/session/firstparty";

    private static readonly ApiError UnavailableError = new(
        "unavailable",
        "Sign-in is not available on this server."
    );

    public Task<ApiResponse<AccountRegistrationView>> RegisterWithPassword(
        PasswordRegistrationRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasswordRegistrationRequest, AccountRegistrationView>(
            $"{Prefix}/password/register",
            request,
            cancellationToken
        );

    public Task<ApiResponse<IssuedSessionView>> SignInWithPassword(
        PasswordSignInRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasswordSignInRequest, IssuedSessionView>(
            $"{Prefix}/password",
            request,
            cancellationToken
        );

    public Task<ApiResponse<PasswordSetView>> SetPassword(
        PasswordSetRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasswordSetRequest, PasswordSetView>(
            $"{Prefix}/password/set",
            request,
            cancellationToken
        );

    public Task<ApiResponse<PasskeyOptionsView>> RegisterOptions(
        string displayName,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasskeyRegisterOptionsRequest, PasskeyOptionsView>(
            $"{Prefix}/register/options",
            new(displayName),
            cancellationToken
        );

    public Task<ApiResponse<AccountRegistrationView>> Register(
        PasskeyCeremonyRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasskeyCeremonyRequest, AccountRegistrationView>(
            $"{Prefix}/register",
            request,
            cancellationToken
        );

    public Task<ApiResponse<PasskeyOptionsView>> AuthenticateOptions(
        CancellationToken cancellationToken = default
    ) =>
        Post<object, PasskeyOptionsView>(
            $"{Prefix}/authenticate/options",
            new(),
            cancellationToken
        );

    public Task<ApiResponse<IssuedSessionView>> Authenticate(
        PasskeyCeremonyRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasskeyCeremonyRequest, IssuedSessionView>(
            $"{Prefix}/authenticate",
            request,
            cancellationToken
        );

    public Task<ApiResponse<IssuedSessionView>> Recover(
        RecoveryRequest request,
        CancellationToken cancellationToken = default
    ) => Post<RecoveryRequest, IssuedSessionView>($"{Prefix}/recover", request, cancellationToken);

    public Task<ApiResponse<PasskeyOptionsView>> EnrolOptions(
        CancellationToken cancellationToken = default
    ) => Post<object, PasskeyOptionsView>($"{Prefix}/enrol/options", new(), cancellationToken);

    public Task<ApiResponse<PasskeyEnrolmentView>> Enrol(
        PasskeyCeremonyRequest request,
        CancellationToken cancellationToken = default
    ) =>
        Post<PasskeyCeremonyRequest, PasskeyEnrolmentView>(
            $"{Prefix}/enrol",
            request,
            cancellationToken
        );

    public Task<ApiResponse<PasskeyStateView>> Credentials(
        CancellationToken cancellationToken = default
    ) =>
        ApiEnvelopeTransport.Get<PasskeyStateView>(
            http,
            $"{Prefix}/credentials",
            UnavailableError,
            cancellationToken
        );

    public Task<ApiResponse<RecoveryCodesView>> MakeNewCodes(
        CancellationToken cancellationToken = default
    ) => Post<object, RecoveryCodesView>($"{Prefix}/recovery-codes", new(), cancellationToken);

    private Task<ApiResponse<TResponse>> Post<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken
    ) =>
        ApiEnvelopeTransport.Post<TRequest, TResponse>(
            http,
            path,
            request,
            UnavailableError,
            cancellationToken
        );
}
