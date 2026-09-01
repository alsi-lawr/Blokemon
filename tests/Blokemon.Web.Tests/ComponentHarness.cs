using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Blokemon.Web.Tests;

// ---- Running a component without a browser ---------------------------------------------
//
// A page like the match table is not a function anyone can call: what it does, it does over a
// sequence of renders, driven by its own lifecycle and by the callbacks its presenters hand back.
// Nothing in this suite could start one, so anything that only happens across renders - the
// presentation loop above all - was untestable, and the defects that lived there were found by
// reading rather than by failing.
//
// This is the smallest thing that fixes that: Blazor's own renderer, which is an ordinary abstract
// class, given a dispatcher, somewhere to put exceptions, and a way of being told a batch has been
// painted. There is no browser and no DOM here, which is the point rather than a limitation - what
// is asked of a component afterwards is what it did and what it settled on, never what markup it
// produced.
//
// Components are collected as the renderer makes them, so a test can reach the presenter it wants
// to press or read a parameter the page handed down. That is the seam a real press uses too: a
// button on a presenter invokes the callback the page gave it, so invoking it here goes through
// exactly the code the press goes through.
//
// BL0006 warns that the render tree types are not meant to be used outside the framework, and it
// is disabled for this file alone. It is the price of the alternative being a third-party renderer
// - which suppresses the same warning internally, and would have to be pinned in two places and
// fetched into the offline nix build. What is used of it is five members, all of them the ones
// every renderer Microsoft ships is itself built on, and a change to them stops this build rather
// than misreporting anything.
#pragma warning disable BL0006
internal sealed class ComponentHarness : Renderer
{
    private readonly Cast _cast;

    private readonly List<Exception> _failures = [];

    private ComponentHarness(IServiceProvider services, Cast cast)
        : base(services, NullLoggerFactory.Instance, cast)
    {
        _cast = cast;
        // What a component captures with @ref is an element the browser is holding, and asking
        // anything of one - focus above all - goes back out through the same browser. There is no
        // browser here, so the references are pointed at the one the test is standing in for, and
        // what a page asks of an element arrives there like any other call.
        ElementReferenceContext = new WebElementReferenceContext(
            services.GetRequiredService<IJSRuntime>()
        );
    }

    public static ComponentHarness For(IServiceProvider services) => new(services, new Cast());

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    // Called once for every batch the renderer finishes, which is once for every time the page
    // said something had changed. A test that cares about the sequence a page went through rather
    // than the state it ended on reads what it needs here.
    public Action? Painted { get; set; }

    // The component of this type the renderer is currently showing. The last one made is the one
    // on screen: a presenter torn down and built again is a new instance, and the old one is no
    // longer part of anything.
    public T Showing<T>()
        where T : IComponent => _cast.Instances.OfType<T>().Last();

    public bool IsShowing<T>()
        where T : IComponent => _cast.Instances.OfType<T>().Any();

    // Every one of them, for the surfaces a page draws once per card rather than once per page.
    public IEnumerable<T> AllShowing<T>()
        where T : IComponent => _cast.Instances.OfType<T>();

    // Puts the component up and waits for it to have finished arriving: its asynchronous
    // initialisation, and everything that initialisation started.
    public Task Show<TComponent>()
        where TComponent : IComponent => Show<TComponent>(ParameterView.Empty);

    public async Task Show<TComponent>(ParameterView parameters)
        where TComponent : IComponent
    {
        await Dispatcher.InvokeAsync(() =>
            RenderRootComponentAsync(
                AssignRootComponentId(InstantiateComponent(typeof(TComponent))),
                parameters
            )
        );
        Rethrow();
    }

    // One thing a player does, done on the renderer's own thread, waited out to the end of
    // whatever it set going.
    public async Task Press(Func<Task> press)
    {
        await Dispatcher.InvokeAsync(press);
        Rethrow();
    }

    // Activates the one rendered button a player reaches by this accessible name. This goes
    // through the event-handler id Blazor gave the browser rather than through a component method.
    // Where the button captures an element reference, that exact reference is the one the event
    // carries into the component lifecycle.
    public async Task<string> ActivateButton(string accessibleName)
    {
        var matches = new List<(ulong HandlerId, string ReferenceId)>();
        await Dispatcher.InvokeAsync(() =>
        {
            foreach (var component in _cast.Instances)
            {
                ArrayRange<RenderTreeFrame> frames;
                try
                {
                    frames = GetCurrentRenderTreeFrames(GetComponentState(component).ComponentId);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                FindButtons(frames, accessibleName, matches);
            }
        });

        var match = matches.ShouldHaveSingleItem(accessibleName);
        await Dispatcher.InvokeAsync(() =>
            DispatchEventAsync(match.HandlerId, default, new MouseEventArgs())
        );
        Rethrow();
        return match.ReferenceId;
    }

    public async Task ChangeSelect(string elementId, string value)
    {
        var matches = new List<ulong>();
        await Dispatcher.InvokeAsync(() =>
        {
            foreach (var component in _cast.Instances)
            {
                ArrayRange<RenderTreeFrame> frames;
                try
                {
                    frames = GetCurrentRenderTreeFrames(GetComponentState(component).ComponentId);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                FindSelects(frames, elementId, matches);
            }
        });

        var handler = matches.ShouldHaveSingleItem(elementId);
        await Dispatcher.InvokeAsync(() =>
            DispatchEventAsync(handler, default, new ChangeEventArgs { Value = value })
        );
        Rethrow();
    }

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        Painted?.Invoke();
        return Task.CompletedTask;
    }

    // A component that throws does not fault the task the test is waiting on, so failures are
    // held and thrown at the next thing the test asks for. Swallowing one would leave the test
    // asserting against a page that had stopped halfway.
    protected override void HandleException(Exception exception) => _failures.Add(exception);

    private void Rethrow()
    {
        if (_failures.Count == 0)
        {
            return;
        }

        var thrown = _failures.ToArray();
        _failures.Clear();
        throw thrown.Length == 1 ? thrown[0] : new AggregateException(thrown);
    }

    private static void FindButtons(
        ArrayRange<RenderTreeFrame> frames,
        string accessibleName,
        List<(ulong HandlerId, string ReferenceId)> matches
    )
    {
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames.Array[index];
            if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button")
            {
                continue;
            }

            var end = index + frame.ElementSubtreeLength;
            ulong handlerId = 0;
            string? label = null;
            string? referenceId = null;
            var text = new List<string>();
            for (var child = index + 1; child < end; child++)
            {
                var candidate = frames.Array[child];
                if (candidate.FrameType == RenderTreeFrameType.Attribute)
                {
                    if (candidate.AttributeName == "aria-label")
                    {
                        label = candidate.AttributeValue as string;
                    }
                    else if (candidate.AttributeName == "onclick")
                    {
                        handlerId = candidate.AttributeEventHandlerId;
                    }
                }
                else if (candidate.FrameType == RenderTreeFrameType.ElementReferenceCapture)
                {
                    referenceId = candidate.ElementReferenceCaptureId;
                }
                else if (candidate.FrameType == RenderTreeFrameType.Text)
                {
                    text.Add(candidate.TextContent);
                }
            }

            label ??= string.Concat(text).Trim();
            if (label == accessibleName && handlerId != 0)
            {
                matches.Add((handlerId, referenceId ?? string.Empty));
            }
        }
    }

    private static void FindSelects(
        ArrayRange<RenderTreeFrame> frames,
        string elementId,
        List<ulong> matches
    )
    {
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames.Array[index];
            if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "select")
            {
                continue;
            }

            var end = index + frame.ElementSubtreeLength;
            ulong handlerId = 0;
            string? id = null;
            for (var child = index + 1; child < end; child++)
            {
                var candidate = frames.Array[child];
                if (candidate.FrameType != RenderTreeFrameType.Attribute)
                {
                    continue;
                }

                if (candidate.AttributeName == "id")
                {
                    id = candidate.AttributeValue as string;
                }
                else if (candidate.AttributeName == "onchange")
                {
                    handlerId = candidate.AttributeEventHandlerId;
                }
            }

            if (id == elementId && handlerId != 0)
            {
                matches.Add(handlerId);
            }
        }
    }

    private sealed class Cast : IComponentActivator
    {
        public List<IComponent> Instances { get; } = [];

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Instances.Add(component);
            return component;
        }
    }
}

internal static class ComponentHarnessMatches
{
    public static T ShouldHaveSingleItem<T>(this IReadOnlyList<T> matches, string accessibleName) =>
        matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No rendered button is named '{accessibleName}'."
            ),
            _ => throw new InvalidOperationException(
                $"More than one rendered button is named '{accessibleName}'."
            ),
        };
}

#pragma warning restore BL0006
