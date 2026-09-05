using Blokemon.App;
using Blokemon.Web.Persistence;

namespace Blokemon.Web.Identity;

/// <summary>
/// Runs the session sweep, and the sweep of expired hand-off and continuation codes, once at
/// start-up and then on the configured interval (hourly by default). The sweeps themselves are
/// <see cref="SessionSweep.run"/> and <see cref="HandoffCodes.sweep"/>, which tests call
/// directly; each mint also removes what has expired.
/// </summary>
public sealed class SessionSweepService(
    IServiceScopeFactory scopes,
    IdentityConfiguration identity,
    TimeProvider time,
    ILogger<SessionSweepService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Sweep(stoppingToken);
        using var timer = new PeriodicTimer(identity.SessionSweepInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await Sweep(stoppingToken);
        }
    }

    private async Task Sweep(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var documents = scope.ServiceProvider.GetRequiredService<StateDocumentStore>();
            var removed = await SessionSweep.run(
                documents,
                documents,
                time.GetUtcNow(),
                cancellationToken
            );
            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} expired session documents.", removed);
            }

            var codes = await HandoffCodes.sweep(
                documents,
                documents,
                time.GetUtcNow(),
                cancellationToken
            );
            if (codes > 0)
            {
                logger.LogInformation("Removed {Count} expired sign-in codes.", codes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The session sweep did not complete; it runs again.");
        }
    }
}
