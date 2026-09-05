using Blokemon.App.Contracts;

namespace Blokemon.App.Client;

/// <summary>
/// The operator's and the tenant owner's routes: listings, the account lifecycle, the
/// admission of tenants and their rotation, closure and revocation, and the owner's exclusion
/// and readmission. Every route needs a session; the server derives the role on each call and
/// answers a refusal with its typed error. A route that is not on this server answers with the
/// typed <c>unavailable</c> outcome.
/// </summary>
public sealed class AdministrationApiClient(HttpClient http)
{
    private static readonly ApiError UnavailableError = new(
        "unavailable",
        "Administration is not available on this server."
    );

    public Task<ApiResponse<AccountSummaryView[]>> Accounts(
        CancellationToken cancellationToken = default
    ) => Get<AccountSummaryView[]>("api/operator/accounts", cancellationToken);

    public Task<ApiResponse<TenantSummaryView[]>> Tenants(
        CancellationToken cancellationToken = default
    ) => Get<TenantSummaryView[]>("api/operator/tenants", cancellationToken);

    public Task<ApiResponse<SignInDiagnosticsView>> Diagnostics(
        CancellationToken cancellationToken = default
    ) => Get<SignInDiagnosticsView>("api/operator/diagnostics", cancellationToken);

    public Task<ApiResponse<AccountLifecycleView>> Disable(
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<AccountLifecycleView>(AccountRoute(accountId, "disable"), cancellationToken);

    public Task<ApiResponse<AccountLifecycleView>> Enable(
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<AccountLifecycleView>(AccountRoute(accountId, "enable"), cancellationToken);

    public Task<ApiResponse<AccountLifecycleView>> GrantOperator(
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<AccountLifecycleView>(AccountRoute(accountId, "grant-operator"), cancellationToken);

    public Task<ApiResponse<AccountErasedView>> Erase(
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<AccountErasedView>(AccountRoute(accountId, "erase"), cancellationToken);

    public Task<ApiResponse<TenantOwnerView>> AssignOwner(
        string tenantId,
        string accountId,
        CancellationToken cancellationToken = default
    ) =>
        ApiEnvelopeTransport.Post<OwnerAssignmentRequest, TenantOwnerView>(
            http,
            $"api/operator/tenants/{Uri.EscapeDataString(tenantId)}/owner",
            new(accountId),
            UnavailableError,
            cancellationToken
        );

    public Task<ApiResponse<AdmittedTenantView>> Admit(
        TenantAdmissionRequest request,
        CancellationToken cancellationToken = default
    ) =>
        ApiEnvelopeTransport.Post<TenantAdmissionRequest, AdmittedTenantView>(
            http,
            "api/operator/tenants",
            request,
            UnavailableError,
            cancellationToken
        );

    public Task<ApiResponse<AdmittedTenantView>> Rotate(
        string tenantId,
        CancellationToken cancellationToken = default
    ) => Post<AdmittedTenantView>(TenantRoute(tenantId, "rotate"), cancellationToken);

    public Task<ApiResponse<TenantStatusView>> Close(
        string tenantId,
        CancellationToken cancellationToken = default
    ) => Post<TenantStatusView>(TenantRoute(tenantId, "close"), cancellationToken);

    public Task<ApiResponse<TenantStatusView>> Revoke(
        string tenantId,
        CancellationToken cancellationToken = default
    ) => Post<TenantStatusView>(TenantRoute(tenantId, "revoke"), cancellationToken);

    public Task<ApiResponse<OwnedTenantView[]>> OwnedTenants(
        CancellationToken cancellationToken = default
    ) => Get<OwnedTenantView[]>("api/owner/tenants", cancellationToken);

    public Task<ApiResponse<ApprovalSummaryView[]>> Approvals(
        string tenantId,
        CancellationToken cancellationToken = default
    ) =>
        Get<ApprovalSummaryView[]>(
            $"api/owner/{Uri.EscapeDataString(tenantId)}/approvals",
            cancellationToken
        );

    public Task<ApiResponse<ExclusionView>> Exclude(
        string tenantId,
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<ExclusionView>(OwnerRoute(tenantId, accountId, "exclude"), cancellationToken);

    public Task<ApiResponse<ExclusionView>> Readmit(
        string tenantId,
        string accountId,
        CancellationToken cancellationToken = default
    ) => Post<ExclusionView>(OwnerRoute(tenantId, accountId, "readmit"), cancellationToken);

    private static string AccountRoute(string accountId, string action) =>
        $"api/operator/accounts/{Uri.EscapeDataString(accountId)}/{action}";

    private static string TenantRoute(string tenantId, string action) =>
        $"api/operator/tenants/{Uri.EscapeDataString(tenantId)}/{action}";

    private static string OwnerRoute(string tenantId, string accountId, string action) =>
        $"api/owner/{Uri.EscapeDataString(tenantId)}/accounts/{Uri.EscapeDataString(accountId)}/{action}";

    private Task<ApiResponse<T>> Get<T>(string path, CancellationToken cancellationToken) =>
        ApiEnvelopeTransport.Get<T>(http, path, UnavailableError, cancellationToken);

    private Task<ApiResponse<T>> Post<T>(string path, CancellationToken cancellationToken) =>
        ApiEnvelopeTransport.Post<object, T>(
            http,
            path,
            new(),
            UnavailableError,
            cancellationToken
        );
}
