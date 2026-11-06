using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Wildlife;

namespace Slice.Surface;

/// <summary>
/// A sim of creatures that live inside something: a place's rooms (in its blocks) or a cave (in
/// the planet's own rock). It is anchored where the place or cave is, so it never moves and is
/// never rebuilt when the surface patch follows you: its creatures keep their dens, ambushes,
/// nests, routes and wounds for the whole game. It is drawn (wildlife's own creature views and
/// lair marks, put in a few at a time so a full place never hitches), heard and told like the
/// patch's own, and its creatures hunt, bite, die and drop bodies through the patch's events, so
/// the game treats them all alike. Steps only while it stands near.
/// </summary>
public partial class DeepSite : Node3D
{
    public WildlifePatch Patch;
    public WildSim Sim { get; private set; }
    public TellView Tells { get; private set; }

    /// <summary>The player in this sim's frame.</summary>
    public FrameTarget Player { get; private set; }

    /// <summary>Tells shown so far (tests).</summary>
    public int TellsShown { get; private set; }
    public event Action<TellEvent> Told;

    /// <summary>The most time one frame spent putting views in, ms (tests).</summary>
    public double WorstViewMs { get; private set; }

    public float FarDistance = 120f, NearDistance = 40f;
    const double BudgetMs = 1.2;

    readonly Dictionary<Creature, CreatureView> _views = new();
    readonly List<Creature> _gone = new();
    Node3D _creatures, _lairs;
    int _lairNext;

    public static DeepSite Create(WildlifePatch patch, WildSim sim, string name)
    {
        var site = new DeepSite { Name = name, Patch = patch, Sim = sim };
        site._creatures = new Node3D { Name = "Creatures" };
        site.AddChild(site._creatures);
        site._lairs = new Node3D { Name = "Lairs" };
        site.AddChild(site._lairs);
        // Tells play through the patch's own sounds, at this site's frame (set round each call).
        site.Tells = new TellView { Name = "Tells" };
        site.AddChild(site.Tells);
        return site;
    }

    public override void _Ready()
    {
        Player = new FrameTarget(Patch.Player, () => GlobalPosition, () => Patch.PlayerWorld());
        if (!Sim.Targets.Contains(Player)) Sim.Targets.Add(Player);
        Tells.Bind(Sim);
        Tells.Shown += OnTold;
        Sim.Telegraph += OnTelegraph;
        Sim.Warned += OnWarned;
        Sim.Attacked += OnAttacked;
        Sim.Died += OnDied;
        Patch.Sites.Add(this);
    }

    public override void _ExitTree()
    {
        Tells.Shown -= OnTold;
        Sim.Telegraph -= OnTelegraph;
        Sim.Warned -= OnWarned;
        Sim.Attacked -= OnAttacked;
        Sim.Died -= OnDied;
        Sim.Targets.Remove(Player);
        Patch.Sites.Remove(this);
    }

    void OnTold(TellEvent e)
    {
        TellsShown++;
        Patch.Sound(GlobalPosition, a => a.Tell(e));
        Told?.Invoke(e);
    }

    void OnTelegraph(AttackEvent e)
    {
        Patch.Sound(GlobalPosition, a => a.Telegraph(e.Creature));
        if (e.Target == Player) Patch.RaiseTelegraphed(e);
    }

    void OnWarned(Creature c) => Patch.Sound(GlobalPosition, a => a.Telegraph(c));

    void OnAttacked(AttackEvent e)
    {
        if (e.Target == Player) Patch.RaiseAttacked(e);
    }

    void OnDied(Creature c, ITarget killer) => Patch.RaiseDied(c, killer == Player ? Patch.Player : killer);

    public Vector3 Local(Vector3 world) => world - GlobalPosition;
    public Vector3 World(Vector3 local) => local + GlobalPosition;

    public CreatureView ViewOf(Creature c) => _views.GetValueOrDefault(c);

    public override void _PhysicsProcess(double delta)
    {
        Sim.Clock.Time01 = (float)Patch.Zone.Sky.TimeOfDay;
        Sim.Frozen = Patch.Frozen;
        Sim.Step((float)delta);
    }

    public override void _Process(double delta)
    {
        var clock = Stopwatch.StartNew();
        // The lair marks (cracks, moss and scraps, nests), a handful at a time: wildlife's own
        // lair view, each drawing a share of them (they keep scaling with the nests and food).
        int lairs = Sim.Cracks.Count + Sim.Food.Count + Sim.Nests.Count;
        while (_lairNext < lairs && clock.Elapsed.TotalMilliseconds < BudgetMs) BuildLairs(2);
        if (Patch.Zone.Camera != null) SyncViews(Local(Patch.Zone.Camera.GlobalPosition), clock);
        WorstViewMs = Math.Max(WorstViewMs, clock.Elapsed.TotalMilliseconds);
    }

    void BuildLairs(int count)
    {
        var part = new WildSim(Sim.Data, Sim.Terrain, Sim.Nav);
        for (int i = 0; i < count && _lairNext < Sim.Cracks.Count + Sim.Food.Count + Sim.Nests.Count; i++, _lairNext++)
        {
            int k = _lairNext;
            if (k < Sim.Cracks.Count) part.Cracks.Add(Sim.Cracks[k]);
            else if ((k -= Sim.Cracks.Count) < Sim.Food.Count) part.Food.Add(Sim.Food[k]);
            else part.Nests.Add(Sim.Nests[k - Sim.Food.Count]);
        }
        var view = new LairView { Name = "Lair" };
        _lairs.AddChild(view);
        view.Build(part);
    }

    /// <summary>Wildlife's creature views for the creatures in sight range (new ones within the frame's budget), rigged as often as their distance warrants.</summary>
    void SyncViews(Vector3 camera, Stopwatch clock)
    {
        foreach (Creature c in Sim.Creatures)
        {
            if (c.Removed) continue;
            float d = WildSim.Flat(c.Position - camera).Length() + MathF.Abs(c.Position.Y - camera.Y) * 0.5f;
            if (!_views.TryGetValue(c, out CreatureView view))
            {
                if (d > FarDistance || clock.Elapsed.TotalMilliseconds > BudgetMs) continue;
                view = new CreatureView();
                _creatures.AddChild(view);
                view.Build(c);
                _views[c] = view;
            }
            c.RigInterval = d < NearDistance ? 1 : d < FarDistance * 0.6f ? 2 : 4;
            bool visible = d < FarDistance;
            if (view.Visible != visible) view.Visible = visible;
            if (visible && (c.RigInterval == 1 || c.RigPending == 0f)) view.Sync();
        }
        _gone.Clear();
        foreach (var (c, v) in _views)
            if (c.Removed)
            {
                v.QueueFree();
                _gone.Add(c);
            }
        foreach (Creature c in _gone) _views.Remove(c);
    }
}

/// <summary>A target seen from another sim's frame: the same body, its position moved into that frame.</summary>
public sealed class FrameTarget : ITarget
{
    readonly ITarget _inner;
    readonly Func<Vector3> _origin, _world;

    public FrameTarget(ITarget inner, Func<Vector3> origin, Func<Vector3> world)
    {
        _inner = inner;
        _origin = origin;
        _world = world;
    }

    public ITarget Inner => _inner;
    public string Name => _inner.Name;
    public Vector3 Position => _world() - _origin();
    public Vector3 Forward => _inner.Forward;
    public Health Health => _inner.Health;
    /// <summary>What lives in a place or a cave can reach you there, under its roof: only on foot and alive.</summary>
    public bool Attackable => _inner is Slice.Game.PlayerTarget p ? p.OnFoot : _inner.Attackable;
    public float ThreatScale => _inner.ThreatScale;
}
