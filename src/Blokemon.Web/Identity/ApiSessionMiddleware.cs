using System.Net.Http.Headers;
using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Product;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Identity;

/// <summary>
/// Establishes the request's session from its bearer token and refuses every non-anonymous
/// <c>/api</c> request that has no valid one, before any endpoint runs. On an anonymous route a
/// token that fails validation is ignored rather than refused, so a stale token in the browser
/// never blocks the exchange that would replace it. A <c>Recovery</c> session is accepted only
/// on the replacement enrolment: elsewhere it is refused with its own typed error, and on an
/// anonymous route it is ignored like a stale token. The refusal travels as HTTP 200 in the
/// ordinary envelope like every other typed error, which is what the client reads.
/// </summary>
public sealed class ApiSessionMiddleware(RequestDelegate next, TimeProvider time)
{
    public async Task InvokeAsync(
        HttpContext context,
        StateDocumentStore documents,
        CurrentSession current
    )
    {
        var anonymous = ApiSessionPolicy.IsAnonymous(context.Request.Method, context.Request.Path);
        var token = BearerToken(context.Request);
        if (token is null)
        {
            if (anonymous)
            {
                await next(context);
                return;
            }

            await Refuse(context, SessionFailures.required());
            return;
        }

        var validation = await Sessions.validate(
            documents,
            token,
            time.GetUtcNow(),
            context.RequestAborted
        );
        if (validation is SessionValidation.Valid valid)
        {
            var recovering =
                valid.Item.Provenance == SessionProvenance.Recovery
                && !ApiSessionPolicy.IsRecoveryPermitted(
                    context.Request.Method,
                    context.Request.Path
                );
            if (!recovering)
            {
                current.Session = valid.Item;
            }
            else if (!anonymous)
            {
                await Refuse(context, SessionFailures.recovery());
                return;
            }
        }
        else if (!anonymous)
        {
            await Refuse(
                context,
                validation.IsExpired ? SessionFailures.expired() : SessionFailures.required()
            );
            return;
        }

        await next(context);
    }

    private static string? BearerToken(HttpRequest request)
    {
        if (request.Headers.Authorization.Count != 1)
        {
            return null;
        }

        return
            AuthenticationHeaderValue.TryParse(request.Headers.Authorization[0], out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(header.Parameter)
            ? header.Parameter
            : null;
    }

    private static Task Refuse(HttpContext context, ApiError error)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context.Response.WriteAsJsonAsync(
            new ApiResponse<object>(false, null, error),
            context.RequestAborted
        );
    }
}
