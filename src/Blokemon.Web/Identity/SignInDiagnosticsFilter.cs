using Blokemon.App;
using Blokemon.App.Contracts;

namespace Blokemon.Web.Identity;

/// <summary>
/// Counts what a sign-in exchange answered: a session issued, or the typed reason it was
/// refused. Added to every route that can issue a session, so the operator's diagnostics see
/// each exchange once. Nothing about the request enters; only the envelope's outcome.
/// </summary>
public sealed class SignInDiagnosticsFilter(SignInDiagnostics diagnostics) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var result = await next(context);
        switch (result)
        {
            case ApiResponse<IssuedSessionView> issued:
                diagnostics.Observe(issued);
                break;
            case ApiResponse<AccountRegistrationView> registered:
                diagnostics.Observe(registered);
                break;
        }

        return result;
    }
}
