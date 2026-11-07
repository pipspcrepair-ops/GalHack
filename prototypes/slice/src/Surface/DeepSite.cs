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

    /// <summary>
    /// The most time one frame spent putting views in, ms (tests). A collector pause that lands in the
    /// frame is left out and named apart (<see cref="WorstPauseMs"/>), as the terrain streamer's timing
    /// does: a gen1 collection stops the whole game for up to about 100 ms wherever it lands, whatever
    /// this code does, and views are where it lands most often because they allocate.
    /// </summary>
    public double WorstViewMs { get; private set; }

    /// <summary>What that worst frame was made of (tests): lair marks, views built, the walk over the creatures, collector pauses.</summary>
    public string WorstViewNote { get; private set; } = "";

    /// <summary>The longest the collector stopped the game between two frames while this site stood, ms, and what kind of collection it was (tests).</summary>
    public double WorstPauseMs { get; private set; }
    public string WorstPauseNote { get; private set; } = "";

    public float FarDistance = 120f, NearDistance = 40f;
    const double BudgetMs = 1.2;

    readonly Dictionary<Creature, CreatureView> _views = new();
    readonly List<Creature> _gone = new();
    readonly HashSet<Creature> _unseen = new();
    const int SightEvery = 6;
    int _sightFrame;
    Node3D _creatures, _lairs;
    int _lairNext;
    TimeSpan _pausedAtLastFrame;
    int _built;
    double _buildMs;

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
        Sim.SightLine = (a, b) => Patch.SightClear(ToGlobal(a), ToGlobal(b));
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
        Sim.SightLine = null;
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
        // What lives here keeps quiet; what it's doing out of sight gives it away.
        _quiet.Clear();
        Quiet.Step(Sim, Player, (float)delta, _quiet);
        foreach (QuietSound q in _quiet) Patch.Sound(GlobalPosition, a => a.Quiet(q));
        QuietSounds += _quiet.Count;
    }

    readonly List<QuietSound> _quiet = new();

    /// <summary>Small sounds made so far (tests).</summary>
    public int QuietSounds { get; private set; }

    public override void _Process(double delta)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan paused = GC.GetTotalPauseDuration();
        double pause = (paused - _pausedAtLastFrame).TotalMilliseconds;
        if (_pausedAtLastFrame != default && pause > WorstPauseMs)
        {
            WorstPauseMs = pause;
            GCMemoryInfo gc = GC.GetGCMemoryInfo(GCKind.Any);
            WorstPauseNote = $"gen{gc.Generation} {(gc.Concurrent ? "background" : "blocking")}{(gc.Compacted ? ", compacted" : "")}, {gc.PromotedBytes / 1e6:F0} MB promoted, heap {gc.HeapSizeBytes / 1e6:F0} MB";
        }
        // The lair marks (cracks, moss and scraps, nests), a handful at a time: wildlife's own
        // lair view, each drawing a share of them (they keep scaling with the nests and food).
        int lairs = Sim.Cracks.Count + Sim.Food.Count + Sim.Nests.Count;
        int lairsBefore = _lairNext;
        while (_lairNext < lairs && clock.Elapsed.TotalMilliseconds < BudgetMs) BuildLairs(2);
        double lairMs = clock.Elapsed.TotalMilliseconds;
        _built = 0;
        _buildMs = 0;
        if (Patch.Zone.Camera != null) SyncViews(Local(Patch.Zone.Camera.GlobalPosition), clock);
        _pausedAtLastFrame = GC.GetTotalPauseDuration();
        double all = clock.Elapsed.TotalMilliseconds, stopped = (_pausedAtLastFrame - paused).TotalMilliseconds, ms = all - stopped;
        if (ms > WorstViewMs)
        {
            WorstViewMs = ms;
            WorstViewNote = $"{lairMs:F1} ms on {_lairNext - lairsBefore} lair marks, {_buildMs:F1} ms on {_built} views, {all - lairMs - _buildMs:F1} ms on the rest, with {stopped:F1} ms of collector pauses left out";
        }
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
        _sightFrame++;
        foreach (Creature c in Sim.Creatures)
        {
            if (c.Removed) continue;
            float d = WildSim.Flat(c.Position - camera).Length() + MathF.Abs(c.Position.Y - camera.Y) * 0.5f;
            // Set for every creature, drawn or not: a place holds hundreds, and one left at the default
            // re-posed its body every tick though nobody could see it (the patch's own views go to 12 too).
            c.RigInterval = d < NearDistance ? 1 : d < FarDistance * 0.6f ? 2 : d < FarDistance ? 4 : 12;
            // Behind a wall from the eye it needs no more than the far rate: a full place is hundreds of
            // bodies within a few rooms. (Looked at again every few frames, so turning a corner shows it moving.)
            if ((_sightFrame + c.Id) % SightEvery == 0 && d < FarDistance)
            {
                if (Sees(c, camera)) _unseen.Remove(c);
                else _unseen.Add(c);
            }
            if (c.RigInterval < 4 && _unseen.Contains(c)) c.RigInterval = 4;
            if (!_views.TryGetValue(c, out CreatureView view))
            {
                if (d > FarDistance || clock.Elapsed.TotalMilliseconds > BudgetMs) continue;
                double before = clock.Elapsed.TotalMilliseconds;
                view = new CreatureView();
                _creatures.AddChild(view);
                view.Build(c);
                _views[c] = view;
                _built++;
                _buildMs += clock.Elapsed.TotalMilliseconds - before;
            }
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
        foreach (Creature c in _gone)
        {
            _views.Remove(c);
            _unseen.Remove(c);
        }
    }

    /// <summary>Whether a line from the eye reaches a creature living in solid space: cell by cell, half a metre at a time, up to a metre short of it (it lies against a wall).</summary>
    static bool Sees(Creature c, Vector3 eye)
    {
        if (c.Space == null) return true;
        Vector3 to = c.Position - eye;
        float len = to.Length();
        Vector3 step = to / MathF.Max(len, 0.01f) * 0.5f, p = eye;
        for (float t = 0f; t < len - 1f; t += 0.5f, p += step)
            if (c.Space.SolidCell(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z))) return false;
        return true;
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
    public bool Crouched => _inner.Crouched;
}
