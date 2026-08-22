using Microsoft.JSInterop;

namespace Blokemon.Web.Client.Application;

/// <summary>The table's voice: what the player hears, and whether they hear it at all.</summary>
/// <remarks>
/// Every sound in the game is synthesised in the browser - there is no audio file anywhere in it -
/// but none of that reaches here. This says what happened and how fast the table is playing it,
/// and the browser decides what that sounds like.
///
/// Nothing here can cost a beat. A browser with no audio, a module that fails to import, storage
/// the player has blocked: each of those costs the sound and nothing else, exactly as a failed
/// measurement costs a card its journey and leaves the game running.
/// </remarks>
public sealed class SoundBoard(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _failed;

    /// <summary>Whether the player has sound switched on.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>How loud, from silent to full.</summary>
    public double Volume { get; private set; } = 0.7;

    /// <summary>Raised when the player changes either, so the controls showing them can redraw.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the browser side and reads back what the player chose last time. Later calls do
    /// nothing, so every page may call it on first render without minding whether another already
    /// has.
    /// </summary>
    public async Task Start()
    {
        if (_module is not null || _failed)
        {
            return;
        }

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./sound.js");
            var settings = await _module.InvokeAsync<SoundSettings>("initialise");
            Enabled = settings.Enabled;
            Volume = settings.Volume;
            Changed?.Invoke();
        }
        catch (JSException)
        {
            // A browser that cannot import the module plays the game in silence.
            _failed = true;
        }
    }

    /// <summary>Plays what just happened on the table.</summary>
    /// <param name="cue">The sound to play, named by <see cref="Components.MatchCueSound"/>.</param>
    public ValueTask Play(SoundCue cue) =>
        Invoke(
            "cue",
            cue.Name,
            new
            {
                pace = cue.Pace,
                badge = cue.Badge,
                last = cue.Last,
                kraft = cue.Kraft,
            }
        );

    /// <summary>Puts a theme under the page, or takes the one that is there away.</summary>
    /// <param name="theme">The theme to play, or <c>null</c> for none.</param>
    public ValueTask Music(SoundTheme? theme) =>
        Invoke("setMusic", theme is null ? null : theme.Value.ToString().ToLowerInvariant());

    /// <summary>Arms the tension layer while a player is one prize from winning.</summary>
    public ValueTask LastPrize(bool armed) => Invoke("setLastPrize", armed);

    /// <summary>Switches sound on or off and remembers the choice.</summary>
    public async Task SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Changed?.Invoke();
        await Invoke("setEnabled", enabled);
    }

    /// <summary>Sets the volume and remembers it.</summary>
    public async Task SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        Changed?.Invoke();
        await Invoke("setVolume", Volume);
    }

    private async ValueTask Invoke(string method, params object?[] arguments)
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync(method, arguments);
        }
        catch (JSException)
        {
            // One sound lost. The beat that asked for it carries on.
        }
        catch (ObjectDisposedException)
        {
            // The page navigated away while the sound was on its way out.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSException)
        {
            // The browser has already gone.
        }
        _module = null;
    }

    private sealed record SoundSettings(bool Enabled, double Volume);
}

/// <summary>Which theme is playing under the page.</summary>
public enum SoundTheme
{
    /// <summary>Everywhere that is not a battle.</summary>
    Menu,

    /// <summary>The table, while a match is being played.</summary>
    Battle,
}

/// <summary>One sound, and the few things about the table that change how it is played.</summary>
/// <param name="Name">The cue to play.</param>
/// <param name="Pace">The share of full speed the table is playing this cue at.</param>
/// <param name="Badge">Which way a tossed beer mat came down.</param>
/// <param name="Last">Whether a prize being taken is the one that wins the game.</param>
/// <param name="Kraft">Whether a pack being opened is kraft rather than gloss foil.</param>
public sealed record SoundCue(
    string Name,
    double Pace = 1,
    bool Badge = true,
    bool Last = false,
    bool Kraft = false
);
