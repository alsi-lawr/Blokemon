using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;

namespace Blokemon.Web.Identity.Google;

/// <summary>
/// The <c>google</c> provider: its proof is the authorization code Google's callback carried,
/// with the pending authorization it answers. The code is exchanged at Google's token endpoint
/// over TLS with the client secret and the PKCE verifier; the id token in the answer is read
/// and its issuer, audience, expiry and nonce checked. Its signature is not verified: the token
/// came straight from Google's token endpoint over TLS, which is the case Google's own guidance
/// exempts, and verifying it would mean fetching and caching Google's keys. The subject is
/// Google's stable account id and the name its display-name hint; every session carries
/// <c>FirstParty</c> provenance, since the person proved themselves to their own account.
/// </summary>
internal sealed class GoogleProvider(
    IHttpClientFactory http,
    GoogleDiscovery discovery,
    IdentityConfiguration identity,
    TimeProvider time
) : IIdentityProvider
{
    public IdentityProviderName Name => GoogleSignIn.Name;

    /// <summary>What the callback hands the completion path: the code and what it answers.</summary>
    public sealed record CallbackProof(
        string Code,
        string Nonce,
        string CodeVerifier,
        string RedirectUri
    );

    public static string Proof(string code, PendingAuthorization pending) =>
        JsonSerializer.Serialize(
            new CallbackProof(code, pending.Nonce, pending.CodeVerifier, pending.RedirectUri),
            JsonSerializerOptions.Web
        );

    public async Task<DomainResult<VerifiedIdentity, SignInFailure>> Verify(
        string proof,
        CancellationToken cancellationToken
    )
    {
        CallbackProof? callback;
        try
        {
            callback = JsonSerializer.Deserialize<CallbackProof>(proof, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            callback = null;
        }

        if (callback is null || string.IsNullOrEmpty(callback.Code))
        {
            return Refused(GoogleFailures.State);
        }

        if (
            identity.Provider(Name)
            is not { ClientId: { } clientId, ClientSecret: { } clientSecret }
        )
        {
            return Refused(GoogleFailures.Unavailable);
        }

        var answer = await Exchange(callback, clientId, clientSecret, cancellationToken);
        if (answer is DomainResult<JsonElement, ApiError>.Failed failed)
        {
            return Refused(failed.Error);
        }

        var claims = ((DomainResult<JsonElement, ApiError>.Succeeded)answer).Value;
        if (
            !Text(claims, "iss").Is(issuer => discovery.Issuers.Contains(issuer))
            || !Text(claims, "aud")
                .Is(issuer => string.Equals(issuer, clientId, StringComparison.Ordinal))
            || !Text(claims, "nonce")
                .Is(nonce => string.Equals(nonce, callback.Nonce, StringComparison.Ordinal))
            || !(
                claims.TryGetProperty("exp", out var expiry)
                && expiry.ValueKind == JsonValueKind.Number
                && expiry.TryGetInt64(out var expiresAt)
                && DateTimeOffset.FromUnixTimeSeconds(expiresAt) > time.GetUtcNow()
            )
        )
        {
            return Refused(GoogleFailures.Token);
        }

        // Google's subject is a decimal account id, well within the subject alphabet.
        if (
            ExternalSubject.Create(Text(claims, "sub"))
            is not DomainResult<ExternalSubject, ExternalIdentityFailure>.Succeeded subject
        )
        {
            return Refused(GoogleFailures.Token);
        }

        return DomainResult<VerifiedIdentity, SignInFailure>.NewSucceeded(
            new VerifiedIdentity(
                Name,
                subject.Value,
                Text(claims, "name"),
                SessionProvenance.FirstParty
            )
        );
    }

    /// <summary>The id token's claims, from the token endpoint's answer to the code.</summary>
    private async Task<DomainResult<JsonElement, ApiError>> Exchange(
        CallbackProof callback,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken
    )
    {
        using var client = http.CreateClient(GoogleSignIn.HttpClientName);
        using var form = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["code"] = callback.Code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = callback.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = callback.CodeVerifier,
            }
        );
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(discovery.TokenEndpoint, form, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return DomainResult<JsonElement, ApiError>.NewFailed(GoogleFailures.Unreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DomainResult<JsonElement, ApiError>.NewFailed(GoogleFailures.Unreachable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return DomainResult<JsonElement, ApiError>.NewFailed(GoogleFailures.Exchange);
            }

            string? idToken;
            try
            {
                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(cancellationToken)
                );
                idToken = Text(document.RootElement, "id_token");
            }
            catch (JsonException)
            {
                idToken = null;
            }

            var claims = Claims(idToken);
            return claims is { } payload
                ? DomainResult<JsonElement, ApiError>.NewSucceeded(payload)
                : DomainResult<JsonElement, ApiError>.NewFailed(GoogleFailures.Token);
        }
    }

    /// <summary>The payload of a compact JWT as JSON, or null when it is not one.</summary>
    private static JsonElement? Claims(string? idToken)
    {
        if (idToken is null || idToken.Split('.') is not { Length: 3 } parts)
        {
            return null;
        }

        try
        {
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DomainResult<VerifiedIdentity, SignInFailure> Refused(ApiError error) =>
        DomainResult<VerifiedIdentity, SignInFailure>.NewFailed(
            SignInFailure.NewProviderRefused(error)
        );
}

internal static class ClaimChecks
{
    public static bool Is(this string? value, Func<string, bool> check) =>
        value is not null && check(value);
}
