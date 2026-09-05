using System.Text.Json;

namespace Blokemon.App.Contracts;

/// <summary>The display name the new account's profile takes.</summary>
public sealed record PasskeyRegisterOptionsRequest(string DisplayName);

/// <summary>
/// A ceremony's options as the browser's credential API takes them, and the challenge that
/// names the pending ceremony when the browser's response comes back.
/// </summary>
public sealed record PasskeyOptionsView(string Challenge, JsonElement Options);

/// <summary>
/// The browser's credential as it serialises it, for the pending ceremony the challenge names.
/// The slug is the tenant the page runs as, null at the root; the session is issued there.
/// </summary>
public sealed record PasskeyCeremonyRequest(
    string Challenge,
    JsonElement Response,
    string? Slug = null
);

/// <summary>A new account's session and the recovery codes shown exactly once with it.</summary>
public sealed record PasskeyRegistrationView(IssuedSessionView Session, string[] RecoveryCodes);

/// <summary>One recovery code, the sole identifier presented during recovery.</summary>
public sealed record RecoveryRequest(string Code, string? Slug = null);

/// <summary>
/// One of the account's passkeys: when it was added and from what kind of sign-in, naming the
/// channel when a channel's page enrolled it.
/// </summary>
public sealed record PasskeyView(
    string Id,
    DateTimeOffset EnrolledAt,
    string Provenance,
    string? TenantLabel
);

/// <summary>
/// The account's passkeys and recovery codes as the profile shows them, with the actions this
/// session may take stated by the server so the client draws no rule of its own.
/// </summary>
public sealed record PasskeyStateView(
    PasskeyView[] Passkeys,
    int? RecoveryCodesRemaining,
    bool CanAddPasskey,
    bool CanMakeNewCodes
);

/// <summary>A passkey just enrolled and, when this enrolment produced one, the new code set.</summary>
public sealed record PasskeyEnrolmentView(PasskeyView Passkey, string[]? RecoveryCodes);

public sealed record RecoveryCodesView(string[] Codes);
