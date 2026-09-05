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
public sealed record AccountRegistrationView(IssuedSessionView Session, string[] RecoveryCodes);

/// <summary>
/// A new account with a simple login: the player name (also the first display name) and the
/// password. The slug is the tenant the page runs as, null at the root.
/// </summary>
public sealed record PasswordRegistrationRequest(string Name, string Password, string? Slug = null);

/// <summary>A simple sign-in: the player name and password of an existing login.</summary>
public sealed record PasswordSignInRequest(string Name, string Password, string? Slug = null);

/// <summary>
/// Sets the session's account's password. The name is taken only when the account has no login
/// name yet; an account with one keeps it.
/// </summary>
public sealed record PasswordSetRequest(string? Name, string Password);

/// <summary>The login as it now stands and, when this set produced one, the new code set.</summary>
public sealed record PasswordSetView(string LoginName, string[]? RecoveryCodes);

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
/// The account's credentials as the profile shows them: its login name when it has one, its
/// passkeys and recovery codes, with the actions this session may take stated by the server so
/// the client draws no rule of its own.
/// </summary>
public sealed record PasskeyStateView(
    PasskeyView[] Passkeys,
    int? RecoveryCodesRemaining,
    bool CanAddPasskey,
    bool CanMakeNewCodes,
    string? LoginName = null,
    bool CanSetPassword = false
);

/// <summary>A passkey just enrolled and, when this enrolment produced one, the new code set.</summary>
public sealed record PasskeyEnrolmentView(PasskeyView Passkey, string[]? RecoveryCodes);

public sealed record RecoveryCodesView(string[] Codes);
