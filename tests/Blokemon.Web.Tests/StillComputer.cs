using Blokemon.App;
using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;

namespace Blokemon.Web.Tests;

/// <summary>
/// The battle page's computer as a rendered page under test sees it: a server game, so there
/// is no runtime to start, and nothing to stop.
/// </summary>
internal sealed class StillComputer : IComputerRuntime, IPlayModeOperations
{
    public static StillComputer Instance { get; } = new();

    public Task Start() => Task.CompletedTask;

    public Task Stop() => Task.CompletedTask;

    public Task<PlayModeState> Mode(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new PlayModeState(PlayMode.ServerBacked, "Saved by this server", null, true)
        );

    public Task<ApiResponse<PlayModeState>> SelectMode(
        PlayMode mode,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
}
