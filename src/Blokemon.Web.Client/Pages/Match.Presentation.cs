using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using static Blokemon.Web.Client.Components.MatchText;

namespace Blokemon.Web.Client.Pages;

// ---- Applying, and the presentation that follows ---------------------------------------
//
// A move is sent, and what comes back is played out beat by beat before the table settles on the
// new frame. What is on screen in what order is worked out by MatchPresentationTimeline; the loop
// here holds each beat for as long as the stylesheet takes to play it and owns the two things
// that can interrupt it - the skip, and the reveal that refuses to be skipped.
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

        // An answer the match refused leaves nothing on the table to try again with, so the
        // decision holds here, with the answers it already has, and one press sends them again.
        if (_operationError is not null && _pending is not null && _stage == Stage.Idle)
        {
            _stage = Stage.Confirm;
        }

        // Its own questions, if it has any, are now the only thing on screen, and there is
        // nothing behind the first of them to go back to.
        _autoStarted = _stage is Stage.Choice or Stage.Confirm;
    }

    private async Task CommitPending()
    {
        if (_pending is null || !TryBuildChoices(_pending, out var choices))
        {
            return;
        }

        // The flow is not torn down before the answer goes: a move the match refuses keeps the
        // surface it was made on, and the answers already given, so it can be sent again. One it
        // accepts is cleared by the mutation itself.
        await Apply(_pending, choices);
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
        _overlay = MatchPresentationOverlay.Empty;

        var beats = MatchPresentationTimeline.Beats(presentation, previousFrame);
        for (var index = 0; index < beats.Count; index++)
        {
            var beat = beats[index];
            if (!ReferenceEquals(_skipSignal, skipSignal))
            {
                return;
            }

            // The wait a declared blow takes before it is thrown is worked out once, on the cue
            // that declares it, and held still for as long as the blow is in the air: changing it
            // part way through would move the movement rather than delay it.
            if (beat.Cue?.Kind == MatchAnimationKindView.Attack)
            {
                _blowLead = LeadToBlow(beats, index);
            }
            else if (beat.Overlay.StrikingCardInstanceId is null)
            {
                _blowLead = 0;
            }

            // A reveal carries gameplay information: it stays up until the player confirms,
            // even when the rest of the presentation is skipped.
            var mandatoryReveal =
                beat.Cue is { Kind: MatchAnimationKindView.Reveal, RevealedCards.Length: > 0 };
            if (skipSignal.Task.IsCompleted && !mandatoryReveal)
            {
                continue;
            }

            // A skipped presentation stops at its reveals without moving the table underneath
            // them: the frame it lands on is the one the mutation already carries.
            if (!skipSignal.Task.IsCompleted)
            {
                _presentedFrame = beat.Frame;
                _overlay = beat.Overlay;
            }

            _activeCue = beat.Cue;
            _presentationCard = beat.Cue is null ? null : PresentationCard(beat.Frame, beat.Cue);
            if (beat.Cue is not null)
            {
                _animationStatus = PublicText(beat.Cue.Label);
            }
            await InvokeAsync(StateHasChanged);
            await PositionCueMotion(beat.Cue);

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
                await WaitForPresentation(BeatDuration(beat.Cue), skipSignal.Task);
            }
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
        _overlay = MatchPresentationOverlay.Empty;
        _blowLead = 0;
        _skipSignal = null;
        _revealSignal = null;
        _animationStatus = "Animation complete.";
    }
}
