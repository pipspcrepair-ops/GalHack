using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PoiGenerator.Gen;
using Slice.Surface;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;
using WNav = Wildlife.NavGrid;
using WTerrain = Wildlife.Terrain;

namespace Slice.Places;

/// <summary>
/// What lives under the town: its sewers, drains and galleries, filled cell by cell (<see cref="IncidentalFauna"/>) as the
/// survivor comes near, in sims anchored where the cell is, so a creature keeps to its tunnel for the whole game. A cell's
/// sim stands (is drawn and stepped) only while the survivor is within reach, and at most three stand at once.
/// </summary>
public partial class Underworld : Node3D
{
    public SurfaceZone Zone;
    public Func<Vector3> Feet;
    public Func<Vector3> Look = () => Vector3.Forward;
    public PlaceFauna.Deep Deep;

    sealed class Spot
    {
        public Vector3I Anchor;
        public WildSim Sim;
        public DeepSite Site;
        public ISolidSpace Space;
        public Task<List<IncidentalFauna.Made>> Filling;
        public List<IncidentalFauna.Made> Made = new();
    }

    readonly Dictionary<(int, int), Spot> _spots = new();
    UnderBuilt _built;
    HashSet<PInt3> _air;
    List<(int X, int Z)> _cells;
    Task<(HashSet<PInt3> Air, List<(int X, int Z)> Cells)> _airTask;
    double _timer;

    const float Stand = 90f, Free = 150f;
    const int MostStanding = 3;

    /// <summary>Everyone filled so far, with the role they were put down for (tests).</summary>
    public IEnumerable<IncidentalFauna.Made> All => _spots.Values.SelectMany(s => s.Made);

    public int Standing => _spots.Values.Count(s => s.Site != null);

    /// <summary>The longest one look over the network took on the main thread, ms (tests).</summary>
    public double WorstMs { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null || Deep == null) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 1.0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Survey();
        WorstMs = Math.Max(WorstMs, clock.Elapsed.TotalMilliseconds);
    }

    void Survey()
    {
        _built ??= Zone.Planet.StartTown?.Under?.Built;
        if (_built == null) return;
        if (_airTask == null) { UnderBuilt b = _built; _airTask = Task.Run(() => { var air = IncidentalFauna.OpenAir(b); return (air, IncidentalFauna.Cells(b, air).ToList()); }); }
        if (!_airTask.IsCompleted) return;
        if (_airTask.IsFaulted) { _built = null; return; }
        (_air, _cells) = _airTask.Result;
        Vector3 feet = Feet();
        var planet = Zone.Planet;
        foreach (var ((cx, cz), spot) in _spots)
        {
            Vector3 at = planet.NearestCopy(Canon(spot.Anchor), feet);
            float d = at.DistanceTo(feet);
            if (spot.Site != null && d > Free) { spot.Site.QueueFree(); spot.Site = null; }
            else if (spot.Site == null && spot.Filling is { IsCompleted: true } && spot.Sim != null && d < Stand && Standing < MostStanding && spot.Made.Count > 0) Raise(spot, at);
            else if (spot.Site != null) spot.Site.Position = at;
        }
        if (_spots.Values.Any(s => s.Filling is { IsCompleted: false })) return;
        // The cell nearest you that is within reach and not yet filled.
        (int, int)? next = null;
        float best = Stand;
        foreach (var key in _cells)
        {
            if (_spots.ContainsKey(key)) continue;
            var anchor = AnchorOf(key);
            float d = planet.NearestCopy(Canon(anchor), feet).DistanceTo(feet);
            if (d < best) { best = d; next = key; }
        }
        if (next is { } k) Start(k, feet);
    }

    static Vector3 Canon(Vector3I a) => new(a.X, a.Y, a.Z);

    Vector3I AnchorOf((int X, int Z) key)
    {
        int y = (_built.Min.Y + _built.Max.Y) / 2;
        return new Vector3I(key.X * IncidentalFauna.Cell + IncidentalFauna.Cell / 2, y, key.Z * IncidentalFauna.Cell + IncidentalFauna.Cell / 2);
    }

    void Start((int X, int Z) key, Vector3 feet)
    {
        var planet = Zone.Planet;
        Vector3I anchor = AnchorOf(key);
        var spot = new Spot { Anchor = anchor, Space = IncidentalFauna.Space(_built, anchor) };
        float Height(float x, float z) => planet.GroundHeight(Planets.Gen.Mathx.WrapX(anchor.X + x, planet.Width), Planets.Gen.Mathx.Wrap(anchor.Z + z, planet.Height)) - anchor.Y;
        _spots[key] = spot;
        float time = Zone.Wildlife.Sim?.Time ?? 0f;
        SpawnRules rules = Deep.Data.Spawning;
        Vector3 look = Look();
        look.Y = 0f;
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward;
        Vector3 eye = planet.NearestCopy(Canon(anchor), feet);
        eye = feet - eye + Vector3.Up * 1.6f; // the survivor's eye in the sim's frame
        float cone = MathF.Cos(Mathf.DegToRad(rules.ViewConeDegrees));
        bool Unseen(Vector3 p) => PlaceFauna.Unseen(eye, look, cone, rules.ViewDistance, rules.CaveClearance, spot.Space, p);
        UnderBuilt b = _built;
        HashSet<PInt3> air = _air;
        ulong seed = Zone.Planet.Spec.Seed ^ 0x5E3E;
        spot.Filling = Task.Run(() =>
        {
            // The sim and its ground are made here too: its terrain reads the planet at hundreds of points.
            var terrain = new WTerrain(Height, 40f);
            spot.Sim = new WildSim(Deep.Data, terrain, new WNav(terrain.HalfSize, 3f)) { Time = time };
            List<IncidentalFauna.Made> made;
            lock (Deep.Lock) made = IncidentalFauna.Fill(spot.Sim, b, air, spot.Space, anchor, key, Deep, seed, Unseen);
            spot.Made = made;
            return made;
        });
    }

    void Raise(Spot spot, Vector3 at)
    {
        spot.Site = DeepSite.Create(Zone.Wildlife, spot.Sim, "Under");
        AddChild(spot.Site);
        spot.Site.GlobalPosition = at;
    }
}
