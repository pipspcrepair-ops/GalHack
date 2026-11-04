using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace GalHack.Physics.Engine;

/// <summary>
/// Steps the shared <see cref="PhysicsWorld"/> inside Godot's physics tick. It runs at physics
/// priority -10, before anything reads a transform (cameras, art, passengers), and does one
/// tick in the world's fixed order: sync free bodies, forces, integrate, move kinematic bodies
/// (hulls, then creatures, then characters), resolve, then hand free bodies their forces for
/// the engine's own step, which follows.
///
/// Add it to the scene, give it a world, and bind each body's node once.
/// </summary>
public partial class PhysicsHost : Node
{
    public PhysicsWorld World { get; private set; }
    public GodotQuery Query { get; private set; }

    /// <summary>Step every physics tick by itself. Tests may turn this off and call <see cref="Step"/>.</summary>
    public bool AutoStep = true;

    /// <summary>Wall-clock cost of the last <see cref="Step"/> (the shared module's own work), ms.</summary>
    public double LastStepMs { get; private set; }

    readonly List<BodyBinding> _bindings = new();
    readonly List<BodyBinding> _moveOrder = new();
    readonly Dictionary<ulong, BodyBinding> _byCollider = new();
    readonly Dictionary<Body, BodyBinding> _byBody = new();
    readonly Stopwatch _watch = new();
    bool _orderDirty;

    public PhysicsHost() { }

    public PhysicsHost(PhysicsWorld world)
    {
        Name = "PhysicsHost";
        Attach(world);
    }

    void Attach(PhysicsWorld world)
    {
        World = world;
        Query = new GodotQuery(this);
        world.Query = Query;
        world.Rescued += r => BindingOf(r.Body)?.Teleport();
        world.RegionChanged += WakeIn;
        world.Unregistered += b => Unbind(b);
    }

    public override void _Ready() => ProcessPhysicsPriority = -10;

    public override void _PhysicsProcess(double delta)
    {
        if (AutoStep) Step((float)delta);
    }

    // ---- Binding ------------------------------------------------------------------------

    /// <summary>Ties a registered body to its node. Do this once, right after registering.</summary>
    public T Bind<T>(Body body, T binding) where T : BodyBinding
    {
        binding.Body = body;
        body.Tag = binding;
        _bindings.Add(binding);
        _byBody[body] = binding;
        if (binding.Collider != null) _byCollider[binding.Collider.GetInstanceId()] = binding;
        _orderDirty = true;
        return binding;
    }

    /// <summary>Swaps the node a body is represented by (a vessel turned into a free wreck).</summary>
    public T Rebind<T>(Body body, T binding) where T : BodyBinding
    {
        Unbind(body);
        Query.Forget(body);
        return Bind(body, binding);
    }

    public void Unbind(Body body)
    {
        if (!_byBody.Remove(body, out BodyBinding binding)) return;
        _bindings.Remove(binding);
        if (binding.Collider != null) _byCollider.Remove(binding.Collider.GetInstanceId());
        _orderDirty = true;
    }

    public BodyBinding BindingOf(Body body) => body != null && _byBody.TryGetValue(body, out BodyBinding b) ? b : null;

    /// <summary>The registered body a collider belongs to, or null for terrain and anything unregistered.</summary>
    public Body BodyOf(GodotObject collider) =>
        collider != null && _byCollider.TryGetValue(collider.GetInstanceId(), out BodyBinding b) ? b.Body : null;

    public IReadOnlyList<BodyBinding> Bindings => _bindings;

    // ---- One tick -------------------------------------------------------------------------

    public void Step(float dt)
    {
        _watch.Restart();
        if (_orderDirty) SortMoveOrder();
        Query.Space = GetViewport()?.World3D?.DirectSpaceState;

        World.BeginTick();
        for (int i = 0; i < _bindings.Count; i++) _bindings[i].Sync(this, dt);
        World.ApplyForces(dt);
        World.Integrate(dt);
        for (int i = 0; i < _moveOrder.Count; i++)
            if (_moveOrder[i].Body.Motion == Motion.Kinematic) _moveOrder[i].Move(this, dt);
        World.EndTick(dt);
        for (int i = 0; i < _bindings.Count; i++) _bindings[i].Flush(this, dt);
        LastStepMs = _watch.Elapsed.TotalMilliseconds;
    }

    void SortMoveOrder()
    {
        _bindings.Sort((a, b) => a.Body.Id.CompareTo(b.Body.Id));
        _moveOrder.Clear();
        _moveOrder.AddRange(_bindings);
        _moveOrder.Sort((a, b) => a.MoveOrder != b.MoveOrder ? a.MoveOrder.CompareTo(b.MoveOrder) : a.Body.Id.CompareTo(b.Body.Id));
        _orderDirty = false;
    }

    /// <summary>Engines don't wake a resting body when the ground under it changes, so do it here.</summary>
    void WakeIn(Region region)
    {
        foreach (BodyBinding b in _bindings)
            if (region.Contains(b.Body.Position) || region.Contains(b.Body.CenterOfMassPosition))
                b.Wake();
    }
}
