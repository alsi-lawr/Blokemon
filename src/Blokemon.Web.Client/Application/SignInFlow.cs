using Blokemon.App;
using Blokemon.App.Client;
using Blokemon.App.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>Where a silent sign-in surface is, named so the page can say which moment it is.</summary>
public enum SignInStage
{
    Idle,
    Finding,
    Waiting,
    SigningIn,
    SignedIn,
    Failed,
}

/// <summary>
/// The silent sign-in surfaces share one flow: read a hand-off code from the fragment (clearing
/// it first), or receive one from the hosting parent, exchange it at the route the tenant's
/// descriptor names, hold the session, switch to server-backed play and refresh. The session
/// token never enters a URL.
/// </summary>
public sealed class SignInFlow(
    SessionApiClient api,
    SessionHolder holder,
    TenantContext tenants,
    HostedFrame frame,
    IPlayModeOperations modes,
    IApplicationStateRefresher refresher,
    NavigationManager navigation,
    IJSRuntime js
)
{
    private IJSObjectReference? _module;

    public SignInStage Stage { get; private set; }

    public ApiError? Error { get; private set; }

    public TenantDescriptorView? Tenant { get; private set; }

    public string? DisplayName { get; private set; }

    public event Action? Changed;

    /// <summary>The root route: a hand-off in the fragment signs in to the default tenant.</summary>
    public async Task<bool> EnterRoot(CancellationToken cancellationToken = default)
    {
        var code = await ReadFragment(cancellationToken);
        if (code is null)
        {
            return false;
        }

        Move(SignInStage.SigningIn);
        return await Describe(null, cancellationToken)
            && await ExchangeHandoff(code, cancellationToken);
    }

    /// <summary>The hosted route: attach, describe the tenant, signal readiness, then wait or exchange.</summary>
    public async Task EnterTenant(string slug, CancellationToken cancellationToken = default)
    {
        Move(SignInStage.Finding);
        await frame.Attach(
            code => ExchangeHandoff(code, CancellationToken.None),
            cancellationToken
        );
        if (!await Describe(slug, cancellationToken))
        {
            return;
        }

        await frame.Bind(Tenant!.RegisteredParentOrigin, cancellationToken);
        var code = await ReadFragment(cancellationToken);
        if (code is null)
        {
            if (Stage == SignInStage.Finding)
            {
                Move(SignInStage.Waiting);
            }

            return;
        }

        Move(SignInStage.SigningIn);
        await ExchangeHandoff(code, cancellationToken);
    }

    /// <summary>The continuation route: a top-level window exchanging a continuation code.</summary>
    public async Task EnterContinuation(string slug, CancellationToken cancellationToken = default)
    {
        Move(SignInStage.Finding);
        if (!await Describe(slug, cancellationToken))
        {
            return;
        }

        var code = await ReadFragment(cancellationToken);
        if (code is null)
        {
            Fail(new("handoff.missing", "This link carries no sign-in code."));
            return;
        }

        Move(SignInStage.SigningIn);
        await Exchange(SessionApiClient.ContinuationExchangePath, code, cancellationToken);
    }

    private async Task<bool> Describe(string? slug, CancellationToken cancellationToken)
    {
        var response = await tenants.Resolve(slug, cancellationToken);
        if (!response.Succeeded || response.Value is null)
        {
            Fail(response.Error);
            return false;
        }

        Tenant = response.Value;
        Changed?.Invoke();
        return true;
    }

    private Task<bool> ExchangeHandoff(string code, CancellationToken cancellationToken) =>
        Tenant is { HandoffExchangePath: var path }
            ? Exchange(path, code, cancellationToken)
            : Task.FromResult(false);

    private async Task<bool> Exchange(string path, string code, CancellationToken cancellationToken)
    {
        Move(SignInStage.SigningIn);
        var response = await api.Exchange(
            path,
            code,
            Tenant is { Slug: var slug } && slug != Tenants.DefaultSlug.Value ? slug : null,
            cancellationToken
        );
        if (!response.Succeeded || response.Value is null)
        {
            Fail(response.Error);
            return false;
        }

        await holder.Establish(response.Value, cancellationToken);
        DisplayName = response.Value.DisplayName;
        await modes.SelectMode(PlayMode.ServerBacked, cancellationToken);
        await refresher.Refresh(cancellationToken);
        Move(SignInStage.SignedIn);
        return true;
    }

    /// <summary>Opens the game once a hosted or continuation sign-in has landed.</summary>
    public void OpenGame() => navigation.NavigateTo("/");

    private async Task<string?> ReadFragment(CancellationToken cancellationToken)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./signIn.js"
            );
            return await _module.InvokeAsync<string?>("readHandoffCode", cancellationToken);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private void Move(SignInStage stage)
    {
        Stage = stage;
        Error = null;
        Changed?.Invoke();
    }

    private void Fail(ApiError? error)
    {
        Stage = SignInStage.Failed;
        Error = error ?? new("unavailable", "Signing in is not available right now.");
        Changed?.Invoke();
    }
}
