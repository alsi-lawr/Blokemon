// The render tree is how the page is actually built, and reading it is how the marks a component
// puts on a card can be asked about without a browser and without parsing anything back out of a
// string. Guessing at the markup instead is the technique this whole check exists to replace.
#pragma warning disable BL0006

using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blokemon.Web.Tests;

// The table as the product would draw it for one beat of a presentation: the real presenters, the
// real marks, and the class the real page composes for the cue on screen.
internal sealed class MatchTable : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly TreeRenderer _renderer;

    private MatchTable(ServiceProvider services, TreeRenderer renderer)
    {
        _services = services;
        _renderer = renderer;
    }

    public static MatchTable Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        return new(provider, new(provider, provider.GetRequiredService<ILoggerFactory>()));
    }

    // Everything on screen for one beat, under the class the page composes for it. The three
    // presenters are the three the match page puts inside the battle screen; the cue class comes
    // from the production seam rather than being spelled out here, so a change to how it is
    // composed changes what is examined.
    public MatchElement Draw(MatchPresentationBeat beat, MatchAuraView auras)
    {
        // The plain box the page keeps between the screen and the presenters is left out: nothing
        // is written for it and it would begin every path below.
        var screen = Element("section", $"battle-screen {MatchCueMarking.Table(beat.Cue)}");

        // A card being played is shown travelling over the table, which is what the page does with
        // it while the hand copy is concealed underneath.
        var travelling =
            beat.Cue?.Kind is MatchAnimationKindView.Play or MatchAnimationKindView.Evolve;
        Attach(
            screen,
            Render(
                typeof(MatchCueOverlays),
                new()
                {
                    ["Cue"] = beat.Cue,
                    ["PresentationCard"] = travelling ? MatchTableFixture.Face : null,
                    ["ShowPresentationCard"] = travelling,
                    ["CardTravels"] = travelling && beat.Overlay.Landing is not null,
                }
            )
        );
        Attach(
            screen,
            Render(
                typeof(MatchBattlefield),
                new()
                {
                    ["Frame"] = beat.Frame,
                    ["Auras"] = auras,
                    ["Cue"] = beat.Cue,
                    ["Overlay"] = beat.Overlay,
                }
            )
        );
        Attach(
            screen,
            Render(
                typeof(MatchHandZone),
                new()
                {
                    ["Hand"] = beat.Frame.Player.Hand,
                    ["Auras"] = auras,
                    ["Cue"] = beat.Cue,
                    ["Overlay"] = beat.Overlay,
                }
            )
        );

        return screen;
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _services.Dispose();
    }

    private IReadOnlyList<MatchElement> Render(
        Type component,
        Dictionary<string, object?> parameters
    )
    {
        var id = _renderer
            .Render(component, ParameterView.FromDictionary(parameters))
            .GetAwaiter()
            .GetResult();
        var host = Element("div", null);
        Read(_renderer.Frames(id), host);
        return host.Children;
    }

    private void Read(ArrayRange<RenderTreeFrame> frames, MatchElement parent) =>
        Read(frames, 0, frames.Count, parent);

    private void Read(ArrayRange<RenderTreeFrame> frames, int from, int to, MatchElement parent)
    {
        var index = from;
        while (index < to)
        {
            ref var frame = ref frames.Array[index];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    var element = Element(
                        frame.ElementName,
                        Class(frames, index, frame.ElementSubtreeLength)
                    );
                    Attach(parent, element);
                    Read(frames, index + 1, index + frame.ElementSubtreeLength, element);
                    index += frame.ElementSubtreeLength;
                    break;
                case RenderTreeFrameType.Component:
                    Read(_renderer.Frames(frame.ComponentId), parent);
                    index += frame.ComponentSubtreeLength;
                    break;
                default:
                    index++;
                    break;
            }
        }
    }

    private static string? Class(ArrayRange<RenderTreeFrame> frames, int element, int subtree)
    {
        for (var index = element + 1; index < element + subtree; index++)
        {
            ref var frame = ref frames.Array[index];
            if (frame.FrameType != RenderTreeFrameType.Attribute)
            {
                break;
            }

            if (frame.AttributeName == "class")
            {
                return frame.AttributeValue as string;
            }
        }

        return null;
    }

    private static MatchElement Element(string tag, string? classes) =>
        new()
        {
            Tag = tag,
            Classes =
            [
                .. (classes ?? string.Empty).Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                ),
            ],
        };

    private static void Attach(MatchElement parent, MatchElement child)
    {
        child.Parent = parent;
        child.SiblingIndex = parent.Children.Count;
        parent.Children.Add(child);
    }

    private static void Attach(MatchElement parent, IReadOnlyList<MatchElement> children)
    {
        foreach (var child in children)
        {
            Attach(parent, child);
        }
    }

    private sealed class TreeRenderer(IServiceProvider services, ILoggerFactory loggers)
        : Renderer(services, loggers)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        protected override void HandleException(Exception exception) =>
            throw new InvalidOperationException("A presenter failed to draw.", exception);

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) =>
            Task.CompletedTask;

        public Task<int> Render(Type component, ParameterView parameters) =>
            Dispatcher.InvokeAsync(async () =>
            {
                var id = AssignRootComponentId(InstantiateComponent(component));
                await RenderRootComponentAsync(id, parameters);
                return id;
            });

        public ArrayRange<RenderTreeFrame> Frames(int componentId) =>
            GetCurrentRenderTreeFrames(componentId);
    }
}
