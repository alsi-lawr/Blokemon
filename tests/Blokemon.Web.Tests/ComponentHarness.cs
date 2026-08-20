using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

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
        : base(services, NullLoggerFactory.Instance, cast) => _cast = cast;

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

    // Puts the component up and waits for it to have finished arriving: its asynchronous
    // initialisation, and everything that initialisation started.
    public async Task Show<TComponent>()
        where TComponent : IComponent
    {
        await Dispatcher.InvokeAsync(() =>
            RenderRootComponentAsync(
                AssignRootComponentId(InstantiateComponent(typeof(TComponent)))
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

#pragma warning restore BL0006
