using Blokemon.App.Contracts;
using Blokemon.Web.Client.Application;
using Blokemon.Web.Client.Components;
using Microsoft.JSInterop;
using Shouldly;

namespace Blokemon.Web.Tests;

// What the table is heard to do, and what it costs when it cannot be heard at all.
//
// Nothing here pins a duration or a level: those are tuned by listening and a test that fixed them
// would only make tuning them harder. What is asserted is the handful of decisions that are about
// the GAME rather than about the sound - which beat a blow belongs to, which prize rings the bell,
// which way a tossed mat came down - plus the guarantee that a browser that cannot make a noise
// still plays the match.
public sealed class MatchCueSoundTests
{
    // The one that was worth all the work. Contact is 658 ms into attack-lunge and the Attack beat
    // is held for exactly 658 ms, so the blow belongs to the first frame of Damage. Put it on
    // Attack and the hit lands 658 ms before the card carrying it arrives.
    [Test]
    public async Task TheBlowBelongsToDamageAndTheSwingBelongsToAttack()
    {
        Sound(MatchAnimationKindView.Attack)!.Name.ShouldBe("attack");
        Sound(MatchAnimationKindView.Damage)!.Name.ShouldBe("damage");
        Sound(MatchAnimationKindView.Attack)!
            .Name.ShouldNotBe(Sound(MatchAnimationKindView.Damage)!.Name);
        await Task.CompletedTask;
    }

    // The bell is last orders, so it belongs to the prize that ends the match and to no other. A
    // prize taken while any remain is the same sound without it.
    [Test]
    public async Task OnlyThePrizeThatEmptiesThePileRingsTheBell()
    {
        Sound(MatchAnimationKindView.Prize, lastPrize: true)!.Last.ShouldBeTrue();
        Sound(MatchAnimationKindView.Prize, lastPrize: false)!.Last.ShouldBeFalse();
        await Task.CompletedTask;
    }

    // A flat mat chops twice per turn, and badge and blank are a different number of degrees - so
    // which side came up decides how many chops are heard before it lands.
    [Test]
    public async Task TheTossedMatIsHeardLandingOnTheSideItLandedOn()
    {
        Sound(MatchAnimationKindView.Coin, badge: true)!.Badge.ShouldBeTrue();
        Sound(MatchAnimationKindView.Coin, badge: false)!.Badge.ShouldBeFalse();
        await Task.CompletedTask;
    }

    // A sound on every event is how a table turns into noise. A cue the presentation has nothing
    // particular to say about is exactly the one to say nothing about.
    [Test]
    public async Task ACueWithNothingParticularToSayIsSilent()
    {
        Sound(MatchAnimationKindView.Other).ShouldBeNull();
        await Task.CompletedTask;
    }

    // A shuffle at a mulligan's quarter speed is a shorter shuffle, not a shuffle cut off part way
    // through, so the pace the table is playing at reaches the sound.
    [Test]
    public async Task AShuffleCarriesThePaceTheTableIsPlayingItAt()
    {
        Sound(MatchAnimationKindView.Shuffle, pace: 0.25)!.Pace.ShouldBe(0.25);
        await Task.CompletedTask;
    }

    // The guarantee that matters most, and the same one the measurements carry: a browser with no
    // audio, or a module that will not import, costs the sound and nothing else. Every call still
    // returns, and the beat that made it carries on.
    [Test]
    public async Task ABrowserThatCannotMakeASoundStillPlaysTheMatch()
    {
        var browser = new SilentBrowser();
        var board = new SoundBoard(browser);

        await Should.NotThrowAsync(async () =>
        {
            await board.Start();
            await board.Play(new SoundCue("damage"));
            await board.Music(SoundTheme.Battle);
            await board.LastPrize(true);
            await board.SetMusicVolume(0);
            await board.SetEffectsVolume(0.4);
            await board.DisposeAsync();
        });

        // And the board genuinely tried, so a version that quietly stopped calling the browser at
        // all cannot pass this by never failing.
        browser.Imported.ShouldBeTrue();
    }

    // A player's choice is theirs until they change it, so a board that could not reach the browser
    // still answers about itself rather than reporting whatever the browser last managed to say.
    [Test]
    public async Task TheChosenLevelsAreRememberedEvenWhenTheBrowserCannotBeReached()
    {
        var board = new SoundBoard(new SilentBrowser());
        await board.Start();

        await board.SetMusicVolume(0.4);
        await board.SetEffectsVolume(0.9);

        board.MusicVolume.ShouldBe(0.4);
        board.EffectsVolume.ShouldBe(0.9);
    }

    // The two levels are two because they are wanted at different times: a table you are playing
    // in company is quiet with the music still on, and a theme you have heard enough of goes
    // without taking the table with it.
    [Test]
    public async Task SilencingOneChannelLeavesTheOtherWhereItWas()
    {
        var board = new SoundBoard(new SilentBrowser());
        await board.Start();
        await board.SetEffectsVolume(0.9);

        await board.ToggleMusicMute();

        board.MusicVolume.ShouldBe(0);
        board.EffectsVolume.ShouldBe(0.9);
    }

    // Muting is not a level, so coming back off it returns the player to the level they set rather
    // than to a default they never chose.
    [Test]
    public async Task UnmutingReturnsToTheLevelThePlayerLastHadItAt()
    {
        var board = new SoundBoard(new SilentBrowser());
        await board.Start();
        await board.SetMusicVolume(0.35);

        await board.ToggleMusicMute();
        await board.ToggleMusicMute();

        board.MusicVolume.ShouldBe(0.35);
    }

    private static SoundCue? Sound(
        MatchAnimationKindView kind,
        double pace = 1,
        bool lastPrize = false,
        bool badge = true
    ) => MatchCueSound.For(Cue(kind, badge), pace, lastPrize);

    private static MatchEventCueView Cue(MatchAnimationKindView kind, bool badge) =>
        new(1, kind, kind.ToString(), null, [], 0, badge, true, []);

    // A browser where every call into the audio module fails the way a real one fails: the module
    // answers, and then refuses to do the thing.
    private sealed class SilentBrowser : IJSRuntime
    {
        public bool Imported { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            Answer<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => Answer<TValue>(identifier);

        private ValueTask<TValue> Answer<TValue>(string identifier)
        {
            if (identifier == "import")
            {
                Imported = true;
            }
            return ValueTask.FromException<TValue>(new JSException("No audio in this browser."));
        }
    }
}
