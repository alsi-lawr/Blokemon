using System.Text.Json;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Persistence;

/// <summary>
/// The declared per-type summary a listing surfaces: lifecycle fields for account and tenant
/// documents, the expiry for hand-off and session documents, the status and timestamps of an
/// approval document (BLOKEMON-152's owner listing), and nothing for any other type.
/// Field names follow the application's camel-cased document members.
/// </summary>
public static class DocumentSummaryProjection
{
    public static DocumentProjection? Project(string key, string json)
    {
        if (
            key.StartsWith("account/", StringComparison.Ordinal)
            || key.StartsWith("tenant/", StringComparison.Ordinal)
        )
        {
            return Read(
                json,
                static root => new DocumentProjection.Lifecycle(
                    Text(root, "status"),
                    Timestamp(root, "createdAt"),
                    Timestamp(root, "erasedAt")
                )
            );
        }

        if (
            key.StartsWith("handoff/", StringComparison.Ordinal)
            || key.StartsWith("session/", StringComparison.Ordinal)
        )
        {
            return Read(
                json,
                static root => new DocumentProjection.Expiry(Timestamp(root, "expiresAt"))
            );
        }

        if (key.StartsWith("approval/", StringComparison.Ordinal))
        {
            return Read(
                json,
                static root => new DocumentProjection.Approval(
                    Text(root, "status"),
                    Timestamp(root, "approvedAt"),
                    Timestamp(root, "excludedAt")
                )
            );
        }

        return null;
    }

    private static DocumentProjection? Read(
        string json,
        Func<JsonElement, DocumentProjection> project
    )
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? project(document.RootElement)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Timestamp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : null;
}
