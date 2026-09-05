using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Blokemon.Identity.Federated;

/// <summary>
/// The calls a channel's plugin makes server-to-server with its integration token: its own
/// descriptor, a hand-off for a viewer it authenticated, the relay of a viewer's erasure, and
/// its own closure. Every call authenticates the token first and mutates nothing when refused.
/// </summary>
public static class ChannelEndpoints
{
    public static readonly ApiError RateLimited = new(
        "handoff.rate_limited",
        "This channel is minting hand-offs faster than its limit allows."
    );

    public static void Map(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/self", Self);
        tenant.MapPost("/handoff", Handoff);
        tenant.MapPost("/erasure", Erasure);
        tenant.MapPost("/close", Close);
    }

    private static async Task<ApiResponse<TenantDescriptorView>> Self(
        HttpContext context,
        IStateDocumentStore documents,
        IdentityProviderRegistry registry,
        IdentityConfiguration identity,
        CancellationToken cancellationToken
    )
    {
        var authenticated = await ChannelCalls.Authenticate(context, documents, cancellationToken);
        return authenticated is DomainResult<TenantDocument, ApiError>.Succeeded tenant
            ? ChannelCalls.Ok(TenantDescriptors.Describe(tenant.Value, registry, identity))
            : ChannelCalls.Fail<TenantDescriptorView>(
                ((DomainResult<TenantDocument, ApiError>.Failed)authenticated).Error
            );
    }

    private static async Task<ApiResponse<HandoffCodeView>> Handoff(
        HandoffRequest request,
        HttpContext context,
        IStateDocumentStore documents,
        IDocumentListing listing,
        HandoffRateLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken
    )
    {
        var authenticated = await ChannelCalls.Authenticate(context, documents, cancellationToken);
        if (authenticated is not DomainResult<TenantDocument, ApiError>.Succeeded tenant)
        {
            return ChannelCalls.Fail<HandoffCodeView>(
                ((DomainResult<TenantDocument, ApiError>.Failed)authenticated).Error
            );
        }

        // The subject is required and never inferred: a hand-off without one is refused
        // before anything is counted or minted.
        var subject = TwitchSubjects.Parse(request.TwitchUserId);
        if (subject is not DomainResult<ExternalSubject, ApiError>.Succeeded parsed)
        {
            return ChannelCalls.Fail<HandoffCodeView>(
                ((DomainResult<ExternalSubject, ApiError>.Failed)subject).Error
            );
        }

        var now = time.GetUtcNow();
        if (!limits.Allow(tenant.Value, now))
        {
            return ChannelCalls.Fail<HandoffCodeView>(RateLimited);
        }

        // Each mint removes what has expired, so the store never holds dead codes for long.
        await HandoffCodes.sweep(documents, listing, now, cancellationToken);
        var issued = await HandoffCodes.mint(
            documents,
            HandoffBinding.NewChannel(
                Tenants.idOf(tenant.Value),
                parsed.Value,
                Hint(request.DisplayName, request.Login)
            ),
            now,
            cancellationToken
        );
        return ChannelCalls.Ok(new HandoffCodeView(issued.Code, issued.ExpiresAt));
    }

    private static async Task<ApiResponse<ErasureView>> Erasure(
        ErasureRequest request,
        HttpContext context,
        IStateDocumentStore documents,
        CancellationToken cancellationToken
    )
    {
        var authenticated = await ChannelCalls.Authenticate(context, documents, cancellationToken);
        if (authenticated is not DomainResult<TenantDocument, ApiError>.Succeeded tenant)
        {
            return ChannelCalls.Fail<ErasureView>(
                ((DomainResult<TenantDocument, ApiError>.Failed)authenticated).Error
            );
        }

        var subject = TwitchSubjects.Parse(request.TwitchUserId);
        if (subject is not DomainResult<ExternalSubject, ApiError>.Succeeded parsed)
        {
            return ChannelCalls.Fail<ErasureView>(
                ((DomainResult<ExternalSubject, ApiError>.Failed)subject).Error
            );
        }

        var relayed = await IssuerAdmission.relayErasure(
            documents,
            BlokeBotProvider.LinkProvider,
            parsed.Value,
            Tenants.idOf(tenant.Value),
            cancellationToken
        );
        return relayed is DomainResult<bool, ApprovalRefusal>.Succeeded changed
            ? ChannelCalls.Ok(new ErasureView(changed.Value))
            : ChannelCalls.Fail<ErasureView>(
                IssuerAdmission.toError(((DomainResult<bool, ApprovalRefusal>.Failed)relayed).Error)
            );
    }

    private static async Task<ApiResponse<TenantStatusView>> Close(
        HttpContext context,
        IStateDocumentStore documents,
        IDocumentListing listing,
        CancellationToken cancellationToken
    )
    {
        var authenticated = await ChannelCalls.Authenticate(context, documents, cancellationToken);
        if (authenticated is not DomainResult<TenantDocument, ApiError>.Succeeded tenant)
        {
            return ChannelCalls.Fail<TenantStatusView>(
                ((DomainResult<TenantDocument, ApiError>.Failed)authenticated).Error
            );
        }

        var closed = await TenantAdmission.close(
            documents,
            listing,
            Tenants.idOf(tenant.Value),
            cancellationToken
        );
        return closed is DomainResult<TenantDocument, AdmissionFailure>.Succeeded document
            ? ChannelCalls.Ok(
                new TenantStatusView(
                    document.Value.Id,
                    document.Value.Slug,
                    document.Value.Status.ToString()
                )
            )
            : ChannelCalls.Fail<TenantStatusView>(
                TenantAdmission.toError(
                    ((DomainResult<TenantDocument, AdmissionFailure>.Failed)closed).Error
                )
            );
    }

    private static string? Hint(string? displayName, string? login) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName
        : !string.IsNullOrWhiteSpace(login) ? login
        : null;
}
