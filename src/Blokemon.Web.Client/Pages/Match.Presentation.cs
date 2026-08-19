using Blokemon.App.Contracts;
using Microsoft.JSInterop;
using static Blokemon.Web.Client.Components.MatchText;

namespace Blokemon.Web.Client.Pages;

// ---- Applying, and the presentation that follows ---------------------------------------
//
// A move is sent, and what comes back is played out cue by cue before the table settles on the
// new frame. The loop, its skip and reveal signals, and the durations matched to the stylesheet
// all stay here: the overlay layer is only ever told which cue is on screen.
public partial class Match
{
    private Task ChooseAttack(MatchAttackView attack)
    {
        if (attack.ActionId is null || _view?.Match is not { } match)
        {
            return Task.CompletedTask;
        }

        CancelFlow();
        _originCardInstanceId = attack.SourceCardInstanceId;
        return StartAction(match.LegalActions.Single(candidate => candidate.Id == attack.ActionId));
    }

    // A decision the engine offers with a single possible answer is taken as soon as the table
    // settles: it is not a question, and a sheet asking it would be a step with no alternative.
    private async Task ResolveAutomaticDecisions()
    {
        if (_view?.Match is not { } match || _stage != Stage.Idle || Busy())
        {
            return;
        }

        var forced = ForcedDecision(match);
        if (forced.Length == 0 || !ForcedIsAutomatic(forced))
        {
            return;
        }

        await StartAction(forced[0]);
        // Its own questions, if it has any, are now the only thing on screen, and there is
        // nothing behind the first of them to go back to.
        _autoStarted = _stage == Stage.Choice;
    }

    private async Task CommitPending()
    {
        if (_pending is null || !TryBuildChoices(_pending, out var choices))
        {
            return;
        }

        var action = _pending;
        CancelFlow();
        await Apply(action, choices);
    }

    private async Task Apply(MatchActionView action, MatchChoiceSelectionRequest[] choices)
    {
        _working = true;
        _commandId ??= Guid.NewGuid();
        var match = _view!.Match!;
        var response = await Api.ApplyMatchAction(
            match.Frame.Id,
            new(_commandId.Value, match.Frame.Revision, action.Id, choices)
        );
        await CompleteMutation(response, DisplayFrame());
    }

    private async Task CompleteMutation(
        ApiResponse<MatchMutationView> response,
        MatchFrameView? previousFrame
    )
    {
        if (!response.Succeeded || response.Value is null)
        {
            _working = false;
            _operationError = response.Error?.Message ?? "That move did not work. Try it again.";
            return;
        }

        _commandId = null;
        _operationError = null;
        CancelFlow();
        _view = response.Value.Application;

        if (response.Value.Presentation is { Steps.Length: > 0 } presentation)
        {
            await PlayPresentation(presentation, previousFrame);
        }
        else
        {
            FinishPresentation();
        }

        _working = false;
        EnsureCardSelection(selectDefault: false);
        await ResolveAutomaticDecisions();
    }

    private async Task PlayPresentation(
        MatchPresentationView presentation,
        MatchFrameView? previousFrame
    )
    {
        _skipSignal?.TrySetResult();
        _revealSignal?.TrySetResult();
        var skipSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _skipSignal = skipSignal;
        if (_reducedMotion)
        {
            // Reduced motion fast-forwards every decorative cue but still stops on reveals.
            skipSignal.TrySetResult();
        }
        _animating = true;
        _presentedFrame = previousFrame ?? presentation.Steps[0].Frame;

        foreach (var step in presentation.Steps)
        {
            foreach (var cue in step.Events)
            {
                if (!ReferenceEquals(_skipSignal, skipSignal))
                {
                    return;
                }

                // A reveal carries gameplay information: it stays up until the player
                // confirms, even when the rest of the presentation is skipped.
                var mandatoryReveal =
                    cue.Kind == MatchAnimationKindView.Reveal && cue.RevealedCards.Length > 0;
                if (skipSignal.Task.IsCompleted && !mandatoryReveal)
                {
                    continue;
                }

                _activeCue = cue;
                if (cue.Kind == MatchAnimationKindView.Draw && cue.TargetCardInstanceIds.Length > 0)
                {
                    _presentedFrame = step.Frame;
                }
                _presentationCard = PresentationCard(step.Frame, cue);
                _animationStatus = PublicText(cue.Label);
                await InvokeAsync(StateHasChanged);
                if (cue.Kind == MatchAnimationKindView.Draw && _presentationModule is not null)
                {
                    await _presentationModule.InvokeVoidAsync("positionDrawCards", _battleScreen);
                }
                if (mandatoryReveal)
                {
                    await WaitForRevealAcknowledgement();
                    if (!ReferenceEquals(_skipSignal, skipSignal))
                    {
                        return;
                    }
                    _activeCue = null;
                    await InvokeAsync(StateHasChanged);
                }
                else
                {
                    await WaitForPresentation(AnimationDuration(cue.Kind), skipSignal.Task);
                }
            }

            if (skipSignal.Task.IsCompleted)
            {
                continue;
            }

            _activeCue = null;
            _presentationCard = null;
            _presentedFrame = step.Frame;
            await InvokeAsync(StateHasChanged);
            await WaitForPresentation(140, skipSignal.Task);
        }

        if (ReferenceEquals(_skipSignal, skipSignal))
        {
            FinishPresentation();
        }
    }

    private static async Task<bool> WaitForPresentation(int milliseconds, Task skipSignal)
    {
        var delay = Task.Delay(milliseconds);
        return await Task.WhenAny(delay, skipSignal) == delay;
    }

    private void SkipPresentation()
    {
        _skipSignal?.TrySetResult();
    }

    private Task WaitForRevealAcknowledgement()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _revealSignal = signal;
        return signal.Task;
    }

    private void AcknowledgeReveal()
    {
        _revealSignal?.TrySetResult();
    }

    private void FinishPresentation()
    {
        _animating = false;
        _presentedFrame = null;
        _activeCue = null;
        _presentationCard = null;
        _skipSignal = null;
        _revealSignal = null;
        _animationStatus = "Animation complete.";
    }

    private async Task ToggleFullscreen()
    {
        if (_presentationModule is null)
        {
            return;
        }

        try
        {
            await _presentationModule.InvokeAsync<bool>("toggleFullscreen", _battleScreen);
        }
        catch (JSException)
        {
            _operationError = "Full screen is not available in this browser.";
        }
    }

    private CardView? PresentationCard(MatchFrameView frame, MatchEventCueView cue)
    {
        if (!IsCardPresentationCue(cue.Kind))
        {
            return null;
        }

        var cardInstanceId = cue.SourceCardInstanceId ?? cue.TargetCardInstanceIds.FirstOrDefault();
        if (cardInstanceId is null)
        {
            return null;
        }

        return AllVisibleCards(frame).FirstOrDefault(card => card.Id == cardInstanceId)?.Card
            ?? (
                _presentedFrame is null
                    ? null
                    : AllVisibleCards(_presentedFrame)
                        .FirstOrDefault(card => card.Id == cardInstanceId)
                        ?.Card
            );
    }

    private string? AnimationClass() =>
        _activeCue is null
            ? null
            : $"cue-{_activeCue.Kind.ToString().ToLowerInvariant()}{ActorCueClass(_activeCue)}";

    private static string ActorCueClass(MatchEventCueView cue) =>
        cue.ActorIsLocalPlayer switch
        {
            true => " cue-actor-local",
            false => " cue-actor-opponent",
            null => string.Empty,
        };

    private static bool IsCardPresentationCue(MatchAnimationKindView kind) =>
        kind is MatchAnimationKindView.Play or MatchAnimationKindView.Evolve;

    // Each cue is held for as long as the stylesheet takes to play it, so the words and the
    // motion finish together. Changing one of these without the keyframe it belongs to
    // desynchronises them.
    private static int AnimationDuration(MatchAnimationKindView kind) =>
        kind switch
        {
            MatchAnimationKindView.Setup => 900,
            MatchAnimationKindView.Shuffle => 700,
            MatchAnimationKindView.Draw => 900,
            MatchAnimationKindView.Play or MatchAnimationKindView.Evolve => 1000,
            MatchAnimationKindView.Attack => 850,
            MatchAnimationKindView.Damage => 700,
            MatchAnimationKindView.Knockout => 900,
            MatchAnimationKindView.Turn => 950,
            MatchAnimationKindView.Coin => 1400,
            MatchAnimationKindView.Victory => 1100,
            _ => 520,
        };
}
