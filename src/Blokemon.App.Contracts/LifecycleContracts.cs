namespace Blokemon.App.Contracts;

// The operator's and the tenant owner's routes (BLOKEMON-152). Listings carry identifiers,
// status and timestamps only; no view here ever holds a profile, a credential, a code, a
// session token or an integration token.

/// <summary>One account as the operator's listing shows it.</summary>
public sealed record AccountSummaryView(
    string Id,
    string? Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ErasedAt
);

/// <summary>One tenant as the operator's listing shows it.</summary>
public sealed record TenantSummaryView(string Id, string? Status, DateTimeOffset? CreatedAt);

/// <summary>One approval record of a tenant as its owner's listing shows it.</summary>
public sealed record ApprovalSummaryView(
    string AccountId,
    string? Status,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExcludedAt
);

/// <summary>One typed sign-in outcome and how often it happened.</summary>
public sealed record SignInOutcomeView(string Code, long Count);

/// <summary>The sign-in outcomes since the host started: counts and typed reasons only.</summary>
public sealed record SignInDiagnosticsView(DateTimeOffset Since, SignInOutcomeView[] Outcomes);

/// <summary>An account's lifecycle state after an operator's change.</summary>
public sealed record AccountLifecycleView(string Id, string Status, bool Operator);

/// <summary>An account erased: when, and whether this call found it erased already.</summary>
public sealed record AccountErasedView(DateTimeOffset ErasedAt, bool Repeated);

/// <summary>The operator's assignment of the default tenant's owner.</summary>
public sealed record OwnerAssignmentRequest(string? AccountId);

/// <summary>The default tenant with its assigned owner.</summary>
public sealed record TenantOwnerView(string TenantId, string OwnerAccountId);

/// <summary>A tenant the session holds owner authority in, named for the owner's own page.</summary>
public sealed record OwnedTenantView(string Id, string Slug, string Label);

/// <summary>What the held session may do here beyond playing: the roles derived for it now.</summary>
public sealed record SessionRolesView(bool Operator, OwnedTenantView[] OwnedTenants);

/// <summary>An account's standing in a tenant after its owner's exclusion or readmission.</summary>
public sealed record ExclusionView(string AccountId, bool Excluded);
