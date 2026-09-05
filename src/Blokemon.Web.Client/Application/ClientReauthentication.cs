using Blokemon.App;
using Microsoft.AspNetCore.Components;

namespace Blokemon.Web.Client.Application;

/// <summary>
/// Fulfils the application tier's re-authentication outcome for this host: the held session is
/// discarded, then a hosted client asks its parent page for a fresh hand-off and a standalone
/// client goes to the sign-in page, which names why.
/// </summary>
public sealed class ClientReauthentication(
    SessionHolder holder,
    HostedFrame frame,
    NavigationManager navigation
) : IReauthenticationHost
{
    public const string ReauthenticationMessage = "blokemon.reauth";

    public async Task Reauthenticate(
        ReauthenticationReason reason,
        CancellationToken cancellationToken
    )
    {
        await holder.Discard(cancellationToken);
        if (frame.IsBound && await frame.Post(ReauthenticationMessage, cancellationToken))
        {
            return;
        }

        navigation.NavigateTo(
            $"signin?reason={(reason == ReauthenticationReason.Expired ? "expired" : "required")}"
        );
    }
}
