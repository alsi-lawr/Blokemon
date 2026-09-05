using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Identity.Federated;

/// <summary>
/// A Twitch user id as a link subject: digits only, one to sixty-four of them. The number is
/// the stable identity; a login can change and is only ever a hint.
/// </summary>
internal static class TwitchSubjects
{
    public static readonly ApiError Required = new(
        "handoff.subject",
        "A hand-off names the viewer's Twitch user id."
    );

    public static readonly ApiError Malformed = new(
        "handoff.subject",
        "A Twitch user id is digits only."
    );

    public static DomainResult<ExternalSubject, ApiError> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DomainResult<ExternalSubject, ApiError>.NewFailed(Required);
        }

        var trimmed = value.Trim();
        if (
            trimmed.Length > ExternalSubject.MaximumLength
            || !trimmed.All(static character => character is >= '0' and <= '9')
        )
        {
            return DomainResult<ExternalSubject, ApiError>.NewFailed(Malformed);
        }

        return
            ExternalSubject.Create(trimmed)
                is DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded subject
            ? DomainResult<ExternalSubject, ApiError>.NewSucceeded(subject.Value)
            : DomainResult<ExternalSubject, ApiError>.NewFailed(Malformed);
    }
}
