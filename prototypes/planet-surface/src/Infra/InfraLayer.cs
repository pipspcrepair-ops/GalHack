using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;

namespace PlanetSurface.Infra;

/// <summary>
/// Draws what stands on the infrastructure network: bridges and viaducts (deck, parapets, piers,
/// abutments; long-neglected ones with a span or two fallen into the valley), tunnel portals
/// (some tunnels flooded), railway track on its ballast, and maglev guideways on their pylons. The
/// planet is cut into 256 m tiles; the tiles near the camera are built on workers (plain mesh data
/// and collision triangles) and turned into nodes one per frame, at the copy of the planet nearest
/// the camera, as the POI layer does. Deterministic: the same planet draws the same pieces.
/// </summary>
public partial class InfraLayer : Node3D
{
    public const float TileSize = 256f;
    /// <summary>
    /// Tiles within this distance of the camera are built; past it plus a margin they are freed. As far as
    /// the far rings reach, corners included: a pad shows in the far ground, and what stands on it must be there already.
    /// </summary>
    public static float Radius = 1320f;

    /// <summary>One thing to draw, in the tile its middle falls in.</summary>
    public abstract class Piece
    {
        /// <summary>A world point (canonical or continuous) the piece is drawn round: it decides the tile.</summary>
        public double X, Z;
        public abstract void Emit(TileBuilder b);
    }

    sealed class Tile
    {
        public int Ix, Iz;
        public Node3D Node;
        public Task<TileBuilder> Building;
        /// <summary>What is still to be made of it on the main thread: its mesh, its collision, each extra (a wreck site, a ruin).</summary>
        public Queue<Func<Node3D>> Steps;
        /// <summary>Its colliders, made only once something could touch them.</summary>
        public Queue<Action> Solids;
        public bool Done => Node != null && Steps != null && Steps.Count == 0;
        /// <summary>It's close enough to touch, and its colliders aren't all in.</summary>
        public bool SolidsDue;
        /// <summary>A tell here was broken: its rocks are built again, and this is that job.</summary>
        public bool TellsStale;
        public Task<TileBuilder> TellJob;
        public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    }

    /// <summary>Main-thread time a frame may spend making tiles' nodes, ms (at least one step a frame).</summary>
    public static double FrameBudgetMs = 1.5;
    /// <summary>The same with an unfinished tile close enough to see into, and how close that is: one step a frame there is seconds of buildings arriving in view.</summary>
    public static double NearBudgetMs = 6;
    public const float NearTiles = 600f;
    /// <summary>Tiles this close (or as close in <see cref="SolidSeconds"/> at the speed the camera is going) have their colliders; nothing further can touch them.</summary>
    public const float SolidTiles = 500f, SolidSeconds = 6f;
    /// <summary>How long ahead of a moving camera tiles are started, s, and the most that comes to, m.</summary>
    const float LeadSeconds = 4f, LeadMost = 600f;
    /// <summary>The camera's own speed and way, m/s (a game sets it): tiles load ahead of it.</summary>
    public Vector3 Heading;
    /// <summary>The same with nobody watching: the game holds its loading screen until everything near the start stands.</summary>
    public const double HurryBudgetMs = 14;
    /// <summary>Behind a loading screen: tiles take what they need.</summary>
    public bool Hurry;
    /// <summary>The slowest frame of this layer's own main-thread work, ms, and the most it made in one frame (tests).</summary>
    public double WorstMainMs;
    /// <summary>Unfinished tiles (with something in them) and how close the nearest is, m (infinity: none); each finished tile's seconds from start to standing and how far off it was by then. Measurements read these.</summary>
    public int TilesBehind { get; private set; }
    public double NearestBehind { get; private set; } = double.PositiveInfinity;
    public readonly List<(double Seconds, double Distance)> TileLog = new();
    /// <summary>The longest single step on the main thread, ms, and what it was.</summary>
    public double WorstStepMs;
    public string WorstStep = "";

    PlanetWorld _world;
    Task<Dictionary<long, List<Piece>>> _planning;
    Dictionary<long, List<Piece>> _pieces;
    readonly Dictionary<long, Tile> _tiles = new();
    int _tilesX, _tilesZ;

    /// <summary>Extra kinds of piece other systems add (power lines, wrecks, ruins, ore tells): called on the planning worker.</summary>
    public static readonly List<Func<PlanetWorld, IEnumerable<Piece>>> Sources = new();

    public bool Planned => _pieces != null;
    public int TileCount => _tiles.Count;
    public int PieceCount => _pieces?.Values.Sum(l => l.Count) ?? 0;
    public bool Built => _pieces != null && _tiles.Values.All(t => t.Done && !t.SolidsDue);

    public void Build(PlanetWorld world)
    {
        Clear();
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // nothing here moves but by whole planets at a seam
        _world = world;
        _tilesX = (int)Math.Ceiling(world.Width / TileSize);
        _tilesZ = (int)Math.Ceiling(world.Height / TileSize);
        _planning = Task.Run(() => Plan(world));
    }

    public void Clear()
    {
        foreach (Tile t in _tiles.Values) t.Node?.QueueFree();
        _tiles.Clear();
        _pieces = null;
        _planning = null;
        _world = null;
    }

    /// <summary>Every piece on the planet, by tile.</summary>
    public static Dictionary<long, List<Piece>> Plan(PlanetWorld world)
    {
        var pieces = new List<Piece>();
        pieces.AddRange(NetworkPieces(world));
        foreach (var source in Sources) pieces.AddRange(source(world));
        int tx = (int)Math.Ceiling(world.Width / TileSize);
        var byTile = new Dictionary<long, List<Piece>>();
        foreach (Piece p in pieces)
        {
            long key = Key((int)(Mathx.WrapX(p.X, world.Width) / TileSize), (int)(Mathx.Wrap(p.Z, world.Height) / TileSize));
            if (!byTile.TryGetValue(key, out var list)) byTile[key] = list = new List<Piece>();
            list.Add(p);
        }
        return byTile;
    }

    static long Key(int ix, int iz) => (long)ix << 32 | (uint)iz;

    public override void _Process(double delta)
    {
        if (_world == null) return;
        if (_pieces == null)
        {
            if (_planning == null || !_planning.IsCompleted) return;
            _pieces = _planning.Result;
        }
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        double cx = Mathx.WrapX(camera.X, _world.Width), cz = Mathx.Wrap(camera.Z, _world.Height);
        // Where the camera is heading: what stands there is in before it arrives.
        Vector3 lead = new Vector3(Heading.X, 0f, Heading.Z) * LeadSeconds;
        lead = lead.LimitLength(LeadMost);
        double ax = Mathx.WrapX(cx + lead.X, _world.Width), az = Mathx.Wrap(cz + lead.Z, _world.Height);
        double Reach(Tile t) => Math.Min(TileDistance(t.Ix, t.Iz, cx, cz), TileDistance(t.Ix, t.Iz, ax, az));
        float solidReach = MathF.Max(SolidTiles, MathF.Min(Heading.Length() * SolidSeconds, Radius));
        int r = (int)Math.Ceiling((Radius + lead.Length()) / TileSize) + 1;
        int ci = (int)(cx / TileSize), cj = (int)(cz / TileSize);
        // Start the tiles in reach that have something in them.
        for (int dj = -r; dj <= r; dj++)
        for (int di = -r; di <= r; di++)
        {
            int ix = ((ci + di) % _tilesX + _tilesX) % _tilesX, iz = ((cj + dj) % _tilesZ + _tilesZ) % _tilesZ;
            long key = Key(ix, iz);
            if (_tiles.ContainsKey(key) || !_pieces.TryGetValue(key, out var list)) continue;
            if (Math.Min(TileDistance(ix, iz, cx, cz), TileDistance(ix, iz, ax, az)) > Radius) continue;
            var tile = new Tile { Ix = ix, Iz = iz };
            double ox = ix * TileSize, oz = iz * TileSize;
            PlanetWorld world = _world;
            tile.Building = Task.Run(() =>
            {
                var b = new TileBuilder(world, ox, oz);
                foreach (Piece p in list) p.Emit(b);
                return b;
            });
            _tiles[key] = tile;
        }
        // Finished tiles become nodes a step at a time (the mesh, the collision, each wreck site or
        // ruin), within a small budget a frame, nearest first; tiles far out are freed.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var drop = new List<long>();
        int steps = 0;
        double budget = Hurry ? HurryBudgetMs : FrameBudgetMs;
        TilesBehind = 0;
        NearestBehind = double.PositiveInfinity;
        foreach (var (key, tile) in _tiles.OrderBy(t => Reach(t.Value)))
        {
            bool waiting = !tile.Done;
            if (waiting)
            {
                TilesBehind++;
                NearestBehind = Math.Min(NearestBehind, TileDistance(tile.Ix, tile.Iz, cx, cz));
            }
            double seen = Reach(tile);
            if (!tile.Done && seen < NearTiles) budget = Math.Max(budget, NearBudgetMs);
            if (seen > Radius + TileSize)
            {
                drop.Add(key);
                continue;
            }
            if (!tile.Done && tile.Building.IsCompleted && (steps == 0 || watch.Elapsed.TotalMilliseconds < budget))
            {
                if (tile.Node == null)
                {
                    tile.Node = new Node3D { Name = $"Infra {tile.Ix},{tile.Iz}" };
                    tile.Steps = tile.Building.Result.Steps();
                    tile.Solids = tile.Building.Result.SolidSteps();
                    AddChild(tile.Node);
                }
                while (tile.Steps.Count > 0 && (steps == 0 || watch.Elapsed.TotalMilliseconds < budget))
                {
                    long stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    Node3D part = tile.Steps.Dequeue()();
                    if (part != null && part.GetParent() == null) tile.Node.AddChild(part);
                    double stepMs = System.Diagnostics.Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
                    if (stepMs > WorstStepMs)
                    {
                        WorstStepMs = stepMs;
                        WorstStep = part?.Name ?? "a block chunk";
                    }
                    if (part is DecoPlanView wreck && wreck.HasMeta("wreck"))
                    {
                        foreach (DecoNode gone in wreck.Nodes.Where(n => !n.Visible)) gone.QueueFree();
                        wreck.Nodes.RemoveAll(n => !n.Visible);
                        WreckStood?.Invoke(_world.Infra.Wrecks[(int)wreck.GetMeta("wreck")], wreck);
                    }
                    steps++;
                }
            }
            tile.SolidsDue = tile.Solids is { Count: > 0 } && seen < solidReach;
            if (tile.SolidsDue && tile.Done)
            {
                while (tile.Solids.Count > 0 && (steps == 0 || watch.Elapsed.TotalMilliseconds < budget))
                {
                    long stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    tile.Solids.Dequeue()();
                    double stepMs = System.Diagnostics.Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
                    if (stepMs > WorstStepMs)
                    {
                        WorstStepMs = stepMs;
                        WorstStep = "a collider";
                    }
                    steps++;
                }
                tile.SolidsDue = tile.Solids.Count > 0;
            }
            if (waiting && tile.Done && TileLog.Count < 4096) TileLog.Add((tile.Clock.Elapsed.TotalSeconds, TileDistance(tile.Ix, tile.Iz, cx, cz)));
            if (tile.Done) UpdateTells(tile, key);
            if (tile.Node != null)
            {
                // Written only when it changes: a same-spot write still tells every body and mesh under the node.
                Vector3 at = _world.NearestCopy(new Vector3((float)(tile.Ix * TileSize), 0f, (float)(tile.Iz * TileSize)), camera);
                if (tile.Node.Position != at) tile.Node.Position = at;
            }
        }
        WorstMainMs = Math.Max(WorstMainMs, watch.Elapsed.TotalMilliseconds);
        foreach (long key in drop)
        {
            _tiles[key].Node?.QueueFree();
            _tiles.Remove(key);
        }
    }

    /// <summary>Swaps a tile's tells for a build without the ones broken since, and starts that build when one is due.</summary>
    void UpdateTells(Tile tile, long key)
    {
        if (tile.TellJob is { IsCompleted: true })
        {
            TileBuilder built = tile.TellJob.Result;
            tile.TellJob = null;
            if (tile.Node.GetNodeOrNull("Tells") is Node old)
            {
                old.Name = "Tells gone"; // the old node stays in the tree to the end of the frame
                old.QueueFree();
            }
            if (built.HasContent) tile.Node.AddChild(built.ToNode("Tells"));
        }
        if (!tile.TellsStale || tile.TellJob != null || !_pieces.TryGetValue(key, out var pieces)) return;
        tile.TellsStale = false;
        tile.TellJob = Task.Run(() => TellsOf(tile, pieces));
    }

    double TileDistance(int ix, int iz, double cx, double cz)
    {
        double mx = (ix + 0.5) * TileSize, mz = (iz + 0.5) * TileSize;
        double dx = Math.Abs(Mathx.WrapDelta(mx - cx, _world.Width)) - TileSize * 0.5, dz = Math.Abs(Mathx.WrapDelta(mz - cz, _world.Height)) - TileSize * 0.5;
        dx = Math.Max(0, dx);
        dz = Math.Max(0, dz);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    // ---- the network's own pieces ---------------------------------------------------------------

    static IEnumerable<Piece> NetworkPieces(PlanetWorld world)
    {
        InfraNetwork net = world.Infra;
        if (net == null) yield break;
        foreach (InfraStructure s in net.Structures)
        {
            if (s.Route.Maglev) continue;
            if (s.Kind == RoadSpan.Bridge)
            {
                for (int k = s.First; k < s.Last; k++)
                    yield return new BridgeStretch { Structure = s, K = k, X = s.Route.X[k], Z = s.Route.Z[k] };
                yield return new Abutment { Structure = s, AtStart = true, X = s.Route.X[s.First], Z = s.Route.Z[s.First] };
                yield return new Abutment { Structure = s, AtStart = false, X = s.Route.X[s.Last], Z = s.Route.Z[s.Last] };
            }
            else
            {
                yield return new Portal { Structure = s, AtStart = true, X = s.Route.X[s.First], Z = s.Route.Z[s.First] };
                yield return new Portal { Structure = s, AtStart = false, X = s.Route.X[s.Last], Z = s.Route.Z[s.Last] };
                if (s.Flooded)
                    for (int k = s.First; k < s.Last; k++)
                        yield return new FloodStretch { Structure = s, K = k, X = s.Route.X[k], Z = s.Route.Z[k] };
            }
        }
        foreach (WreckSite w in net.Wrecks)
        {
            var (mx, mz) = w.World(w.Width / 2, w.Length / 2);
            yield return new WreckPiece { Site = w, X = mx, Z = mz };
        }
        foreach (Piece p in RuinPieces(world)) yield return p;
        foreach (Piece p in OrePieces(world)) yield return p;
        foreach (Piece p in UnderPieces(world)) yield return p;
        foreach (Piece p in SeawallPieces(world)) yield return p;
        foreach (Piece p in DressPieces(world)) yield return p;
        PowerLines power = net.Power;
        for (int i = 0; i < power.Poles.Count; i++)
            if (power.Poles[i].Role != PowerRole.Plant) yield return new PolePiece { Lines = power, Index = i, X = power.Poles[i].X, Z = power.Poles[i].Z };
        foreach (PowerWire wire in power.Wires)
            yield return new WirePiece { Lines = power, Wire = wire, X = power.Poles[wire.A].X, Z = power.Poles[wire.A].Z };
        foreach (ServiceDrop drop in power.Drops)
            yield return new DropPiece { Lines = power, Drop = drop, X = power.Poles[drop.Pole].X, Z = power.Poles[drop.Pole].Z };
        // A station at each end of a line: a platform, a shelter and the buffers the track stops at.
        foreach (InfraRoute r in net.Routes)
        {
            if (r.Kind != RouteKind.Rail || r.Count < 16) continue;
            if (r.From != null) yield return new Station { Route = r, AtStart = true, X = r.X[0], Z = r.Z[0] };
            if (r.To != null) yield return new Station { Route = r, AtStart = false, X = r.X[^1], Z = r.Z[^1] };
        }
        // A train standing on the line brings its own track: the line's is left out under it.
        var underTrains = new HashSet<(int, int)>();
        foreach (WreckSite w in net.Wrecks)
            if (w.Route != null && w.Route.Kind == RouteKind.Rail && w.First >= 0)
                for (int k = w.First; k <= w.Last; k++) underTrains.Add((w.Route.Id, k));
        foreach (InfraRoute r in net.Routes)
        {
            if (r.Kind != RouteKind.Rail) continue;
            for (int k = 0; k + 1 < r.Count; k++)
            {
                if (underTrains.Contains((r.Id, k))) continue;
                if (r.Maglev) yield return new Guideway { Route = r, K = k, X = r.X[k], Z = r.Z[k] };
                else yield return new Track { Route = r, K = k, X = r.X[k], Z = r.Z[k] };
            }
        }
    }

    // ---- colours ----------------------------------------------------------------------------------

    internal static readonly DecoRgb Concrete = DecoRgb.Hex("8d8a84"), ConcreteDark = DecoRgb.Hex("6c6a66"), Timber = DecoRgb.Hex("6b5038"),
        TimberDark = DecoRgb.Hex("4a3826"), Steel = DecoRgb.Hex("6a4a3a"), SteelDark = DecoRgb.Hex("4c3a32"), Composite = DecoRgb.Hex("b8bcc0"),
        Ballast = DecoRgb.Hex("5f5a52"), Rail = DecoRgb.Hex("5a4438"), Sleeper = DecoRgb.Hex("4d3c2c"), SleeperConcrete = DecoRgb.Hex("7c7a74"),
        Water = DecoRgb.Hex("2c4a52"), Stone = DecoRgb.Hex("7a7268");

    // ---- bridges ----------------------------------------------------------------------------------

    /// <summary>One 4 m stretch of a bridge: its deck slab and parapets, a pier every fifth point; or, fallen, its deck lying broken below.</summary>
    sealed class BridgeStretch : Piece
    {
        public InfraStructure Structure;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Structure.Route;
            NV a = b.Local(r.X[K], r.Y[K], r.Z[K]), c = b.Local(r.X[K + 1], r.Y[K + 1], r.Z[K + 1]);
            (DecoRgb deck, DecoRgb dark, float thick, DecoSurface surface) = Structure.Build switch
            {
                "timber" => (Timber, TimberDark, 0.45f, DecoSurface.Matte),
                "steel" => (Concrete, Steel, 0.9f, DecoSurface.Matte),
                _ => (Composite, ConcreteDark, 1.1f, DecoSurface.Matte),
            };
            float half = r.HalfWidth + 0.3f;
            NV along = c - a;
            float len = along.Length();
            if (len < 1e-3f) return;
            NV dir = along / len, side = NV.Normalize(new NV(-dir.Z, 0, dir.X)), up = NV.Normalize(NV.Cross(side, dir));
            if (up.Y < 0) up = -up;
            bool fallen = Structure.Fallen.Contains(K);
            if (!fallen)
            {
                NV mid = (a + c) * 0.5f - up * (thick * 0.5f);
                // A little overlap so stretches meet without a gap.
                b.Box(mid, dir * (len * 0.5f + 0.05f), up * (thick * 0.5f), side * half, deck, surface, collide: true);
                // Parapets or rails along both edges, broken here and there on a neglected bridge.
                ulong h = Planets.Gen.Hash.Of((ulong)r.Id * 7919UL + (ulong)K, 0xB41D);
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!r.Kept && (h >> (s > 0 ? 3 : 9) & 7) == 0) continue;
                    NV edge = (a + c) * 0.5f + side * (s * (half - 0.15f));
                    if (Structure.Build == "timber")
                    {
                        b.Box(edge + up * 0.95f, dir * (len * 0.5f), up * 0.06f, side * 0.06f, dark, DecoSurface.Matte, collide: false);
                        b.Box(edge + up * 0.5f, dir * 0.07f, up * 0.5f, side * 0.07f, dark, DecoSurface.Matte, collide: false);
                    }
                    else b.Box(edge + up * 0.45f, dir * (len * 0.5f + 0.02f), up * 0.45f, side * 0.15f, Structure.Build == "steel" ? ConcreteDark : Composite, DecoSurface.Matte, collide: true);
                }
            }
            else
            {
                // The fallen deck lies broken on the ground below, draped over whatever it came down on.
                b.Drape((a + c) * 0.5f, new NV(dir.X, 0, dir.Z) * (len * 0.46f), side * (half * 0.9f), thick, deck, surface);
            }
            // A pier every fifth point (20 m), standing on the ground below (under a fallen span too).
            if ((K - Structure.First) % 5 == 0 && K > Structure.First)
            {
                float ground = b.Ground(r.X[K], r.Z[K]);
                float top = a.Y - thick;
                if (top - ground > 0.5f)
                {
                    float h = top - ground + 1.5f;
                    NV foot = new(a.X, ground - 1.5f + h * 0.5f, a.Z);
                    if (Structure.Build == "timber")
                    {
                        // A timber trestle: posts and a cross brace.
                        for (int s = -1; s <= 1; s += 2)
                            b.Box(foot + side * (s * half * 0.7f), dir * 0.18f, NV.UnitY * (h * 0.5f), side * 0.18f, dark, DecoSurface.Matte, collide: true);
                        NV x0 = new(a.X, ground, a.Z), x1 = new(a.X, top, a.Z);
                        b.Beam(x0 + side * (half * 0.7f), x1 - side * (half * 0.7f), 0.12f, dark);
                        b.Beam(x0 - side * (half * 0.7f), x1 + side * (half * 0.7f), 0.12f, dark);
                    }
                    else b.Box(foot, dir * 1.0f, NV.UnitY * (h * 0.5f), side * (half * 0.55f), Structure.Build == "steel" ? Concrete : Composite, DecoSurface.Matte, collide: true);
                }
            }
        }
    }

    /// <summary>A bridge's end: a block from its deck down into the bank, so the deck never ends in the air.</summary>
    sealed class Abutment : Piece
    {
        public InfraStructure Structure;
        public bool AtStart;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Structure.Route;
            int k = AtStart ? Structure.First : Structure.Last, n = AtStart ? Math.Min(r.Count - 1, k + 1) : Math.Max(0, k - 1);
            NV a = b.Local(r.X[k], r.Y[k], r.Z[k]), c = b.Local(r.X[n], r.Y[n], r.Z[n]);
            NV dir = NV.Normalize(new NV(c.X - a.X, 0, c.Z - a.Z)), side = new(-dir.Z, 0, dir.X);
            float ground = Math.Min(b.Ground(r.X[k], r.Z[k]), a.Y);
            float h = a.Y - ground + 2.5f;
            b.Box(new NV(a.X, a.Y - h * 0.5f - 0.05f, a.Z), dir * 1.6f, NV.UnitY * (h * 0.5f), side * (r.HalfWidth + 0.8f),
                Structure.Build == "timber" ? Stone : ConcreteDark, DecoSurface.Matte, collide: true);
        }
    }

    // ---- tunnels -----------------------------------------------------------------------------------

    /// <summary>A tunnel mouth: a face wall round the bore, pillars and a lintel, set into the slope.</summary>
    sealed class Portal : Piece
    {
        public InfraStructure Structure;
        public bool AtStart;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Structure.Route;
            int k = AtStart ? Structure.First : Structure.Last, outward = AtStart ? -1 : 1;
            // The face stands forward of the mouth, in the cutting, with a covered box back to the bore:
            // the raw edge where the bore meets the slope is behind it, and so is the far ring's ramp.
            int boxPoints = (int)Math.Ceiling(TerrainStamp.MouthBox / InfraNetwork.Spacing);
            int w = Math.Clamp(k + outward * boxPoints, 0, r.Count - 1);
            NV mouth = b.Local(r.X[k], r.Y[k], r.Z[k]), wall = b.Local(r.X[w], r.Y[w], r.Z[w]);
            NV flat = new(mouth.X - wall.X, 0, mouth.Z - wall.Z);
            if (flat.LengthSquared() < 1e-6f) return;
            float boxLength = flat.Length() + 3f;
            NV into = NV.Normalize(flat), side = new(-into.Z, 0, into.X), up = NV.UnitY;
            float bore = r.HalfWidth + 1.2f, height = r.Kind == RouteKind.Rail ? 7f : 6.5f;
            // Dressed stone, or timber framing on a low-tech track; cast concrete; composite panels.
            DecoRgb col = r.Tech == TechLevel.Low ? (r.Kind == RouteKind.Track ? PoleWood : Stone) : r.Tech == TechLevel.Mid ? Concrete : Composite;
            float capped = height + 2.4f, half = bore + 2.4f;
            // The face: piers either side of the opening, the lintel over it, a coping a little proud.
            foreach (int s in new[] { -1, 1 })
                b.Box(wall + side * (s * (bore + 1.2f)) + up * (capped * 0.5f - 0.75f), into * 0.7f, up * (capped * 0.5f + 0.75f), side * 1.2f, col, DecoSurface.Matte, collide: true);
            b.Box(wall + up * (height + 1.2f), into * 0.7f, up * 1.2f, side * half, col, DecoSurface.Matte, collide: true);
            b.Box(wall + up * (capped + 0.2f), into * 1f, up * 0.2f, side * (half + 0.35f), col, DecoSurface.Matte, collide: true);
            // The covered box from the face back into the hill: walls and roof over the raw cut.
            NV box = wall + into * (boxLength * 0.5f);
            foreach (int s in new[] { -1, 1 })
                b.Box(box + side * (s * (bore + 0.3f)) + up * (height * 0.5f), into * (boxLength * 0.5f), up * (height * 0.5f + 0.4f), side * 0.3f, col, DecoSurface.Matte, collide: true);
            b.Box(box + up * (height + 0.3f), into * (boxLength * 0.5f), up * 0.4f, side * (bore + 0.6f), col, DecoSurface.Matte, collide: true);
            // Wing walls down each side of the cutting, on its edge as it narrows, as high as the bank they hold.
            for (int j = 0; j < 4; j++)
            {
                int p = w + outward * j, q = p + outward;
                if (p < 0 || q < 0 || p >= r.Count || q >= r.Count || r.Span[Math.Min(p, q)] != RoadSpan.Ground) break;
                NV pa = b.Local(r.X[p], r.Y[p], r.Z[p]), pb = b.Local(r.X[q], r.Y[q], r.Z[q]);
                NV along = new(pb.X - pa.X, 0, pb.Z - pa.Z);
                float len = along.Length();
                if (len < 1e-3f) break;
                NV dir = along / len, across = new(-dir.Z, 0, dir.X);
                float far = Math.Clamp((j + 0.5f) * InfraNetwork.Spacing / TerrainStamp.MouthFade, 0f, 1f);
                float edge = r.HalfWidth + (bore + TerrainStamp.MouthRoom - r.HalfWidth) * (1f - far) + 0.35f;
                foreach (int s in new[] { -1, 1 })
                {
                    double gx = (r.X[p] + r.X[q]) * 0.5 + across.X * s * (edge + 2.5f), gz = (r.Z[p] + r.Z[q]) * 0.5 + across.Z * s * (edge + 2.5f);
                    float tall = Math.Clamp(b.Ground(gx, gz) - Math.Min(r.Y[p], r.Y[q]) + 0.4f, 0.5f, capped);
                    NV c = (pa + pb) * 0.5f + across * (s * edge) + up * (tall * 0.5f - 0.75f);
                    b.Box(c, dir * (len * 0.5f + 0.25f), up * (tall * 0.5f + 0.75f), across * 0.35f, col, DecoSurface.Matte, collide: true);
                }
            }
        }
    }

    /// <summary>Standing water in a neglected tunnel's low middle.</summary>
    /// <summary>The water standing on a stretch of a flooded tunnel, if any.</summary>
    public static bool FloodLevel(InfraStructure s, int k, PlanetTerrainSource source, out float level)
    {
        InfraRoute r = s.Route;
        float deck = Math.Min(r.Y[k], r.Y[k + 1]);
        level = Math.Min(r.Y[s.First], r.Y[s.Last]) - 0.3f;
        if (s.Flooded && level <= deck + 0.05f) level = deck + 0.45f;
        if (level <= deck) return false;
        // Only in the bore: where the ground dips over the tunnel's line (or its bore never opened) there's no water to hold.
        float over = float.MaxValue;
        foreach (int i in new[] { k, k + 1 })
            over = Math.Min(over, source.SurfaceHeight(Mathx.WrapX(r.X[i], source.Width), Mathx.Wrap(r.Z[i], source.Height)));
        return over >= level + PlanetTerrainSource.TunnelRoof;
    }

    sealed class FloodStretch : Piece
    {
        public InfraStructure Structure;
        public int K;

        public override void Emit(TileBuilder b)
        {
            if (!FloodLevel(Structure, K, b.World.Source, out float level)) return;
            InfraRoute r = Structure.Route;
            NV a = b.Local(r.X[K], level, r.Z[K]), c = b.Local(r.X[K + 1], level, r.Z[K + 1]);
            NV dir = c - a;
            if (dir.LengthSquared() < 1e-6f) return;
            NV side = NV.Normalize(new NV(-dir.Z, 0, dir.X)) * (r.HalfWidth + 1.1f);
            b.Quad(a - side, a + side, c + side, c - side, Water, DecoSurface.Glass, 0.8f);
        }
    }

    // ---- power lines -------------------------------------------------------------------------------

    internal static readonly DecoRgb PoleWood = DecoRgb.Hex("4f3d2c"), PylonSteel = DecoRgb.Hex("6e7072"), Insulator = DecoRgb.Hex("a8b4b8"), WireDark = DecoRgb.Hex("1e2024");

    internal static readonly DecoRgb PylonConcrete = DecoRgb.Hex("8a8a86");
    /// <summary>How far a pylon leg's end sinks below the ground under its foot.</summary>
    internal const float PylonSink = 1f;

    /// <summary>The four feet of a standing pylon: world x, z, and the height each leg's lower end is drawn at (a little below the ground under it).</summary>
    public static (double X, double Z, float Y)[] PylonFeet(PowerPole p, Func<double, double, float> ground)
    {
        var xz = p.FeetXZ();
        var feet = new (double X, double Z, float Y)[4];
        for (int i = 0; i < 4; i++) feet[i] = (xz[i].X, xz[i].Z, ground(xz[i].X, xz[i].Z) - PylonSink);
        return feet;
    }

    /// <summary>A pole or pylon, standing, or fallen flat along the line.</summary>
    sealed class PolePiece : Piece
    {
        public PowerLines Lines;
        public int Index;

        public override void Emit(TileBuilder b)
        {
            PowerPole p = Lines.Poles[Index];
            NV foot = b.Local(p.X, p.Ground, p.Z), side = new(p.SideX, 0, p.SideZ), along = new(-p.SideZ, 0, p.SideX);
            float h = p.Height;
            if (p.Role == PowerRole.Substation)
            {
                SubstationYard(b, p, foot, side, along);
                return;
            }
            if (p.Fallen)
            {
                // Lying along the line, its foot snapped off at the stump, and along the ground, however it falls away.
                b.Box(foot + NV.UnitY * 0.4f, NV.UnitX * 0.14f, NV.UnitY * 0.4f, NV.UnitZ * 0.14f, p.Pylon ? PylonSteel : PoleWood, DecoSurface.Matte, collide: true);
                NV flat = NV.Normalize(along + side * 0.25f);
                NV top = foot + flat * h;
                top.Y = b.Ground(p.X + flat.X * h, p.Z + flat.Z * h);
                NV lieDir = NV.Normalize(top - foot), lieSide = NV.Normalize(NV.Cross(NV.UnitY, lieDir)), lieUp = NV.Normalize(NV.Cross(lieDir, lieSide));
                if (lieUp.Y < 0) lieUp = -lieUp;
                NV mid = foot + lieDir * (h * 0.5f) + lieUp * (p.Pylon ? 0.7f : 0.1f);
                float length = (top - foot).Length();
                if (p.Pylon) b.Box(mid, lieDir * (length * 0.5f), lieUp * 0.9f, lieSide * 1.4f, PylonSteel, DecoSurface.Metal, collide: true);
                else
                {
                    b.Box(mid, lieDir * (length * 0.5f), lieUp * 0.13f, lieSide * 0.13f, PoleWood, DecoSurface.Matte, collide: true);
                    b.Box(foot + lieDir * (length - 0.4f) + lieUp * 0.3f, lieDir * 0.07f, lieUp * 0.07f, lieSide * 1.25f, PoleWood, DecoSurface.Matte, collide: true);
                }
                return;
            }
            if (!p.Pylon)
            {
                b.Box(foot + NV.UnitY * (h * 0.5f - 0.6f), NV.UnitX * 0.13f, NV.UnitY * (h * 0.5f + 0.6f), NV.UnitZ * 0.13f, PoleWood, DecoSurface.Matte, collide: true);
                b.Box(foot + NV.UnitY * (h - 0.45f), along * 0.07f, NV.UnitY * 0.07f, side * 1.3f, PoleWood, DecoSurface.Matte, collide: true);
                for (int k = 0; k < Lines.WiresOn(p); k++)
                {
                    var a = p.Anchor(k, Lines.WiresOn(p));
                    b.Box(b.Local(a.X, a.Y - 0.1f, a.Z), NV.UnitX * 0.05f, NV.UnitY * 0.1f, NV.UnitZ * 0.05f, Insulator, DecoSurface.Matte, collide: false);
                }
                return;
            }
            // A lattice pylon: four legs tapering up, braced in tiers, and two crossarms.
            float baseHalf = h * 0.13f, topHalf = 0.9f;
            int tiers = Math.Max(4, (int)(h / 6f));
            // Each leg runs down to the ground under its own foot (sunk a little), with a concrete footing round it.
            var feet = PylonFeet(p, b.Ground);
            NV[] bottom = new NV[4];
            for (int i = 0; i < 4; i++) bottom[i] = b.Local(feet[i].X, feet[i].Y, feet[i].Z);
            NV TopOf(NV c) => foot + new NV(c.X - foot.X, 0, c.Z - foot.Z) * (topHalf / baseHalf) + NV.UnitY * h;
            for (int i = 0; i < 4; i++)
            {
                float gy = bottom[i].Y + PylonSink;
                b.Box(new NV(bottom[i].X, gy - 0.1f, bottom[i].Z), NV.UnitX * 0.55f, NV.UnitY * 0.5f, NV.UnitZ * 0.55f, PylonConcrete, DecoSurface.Matte, collide: true);
                b.Beam(bottom[i], TopOf(bottom[i]), 0.16f, PylonSteel, collide: true);
                NV n0 = bottom[i], n1 = bottom[(i + 1) % 4];
                for (int tier = 0; tier < tiers; tier++)
                {
                    float t0 = tier / (float)tiers, t1 = (tier + 1) / (float)tiers;
                    NV a0 = NV.Lerp(n0, TopOf(n0), t0), b1 = NV.Lerp(n1, TopOf(n1), t1);
                    b.Beam(a0, b1, 0.05f, PylonSteel);
                    b.Beam(NV.Lerp(n0, TopOf(n0), t1), NV.Lerp(n1, TopOf(n1), t1), 0.05f, PylonSteel);
                }
            }
            for (int arm = 0; arm < 2; arm++)
            {
                NV at = foot + NV.UnitY * (h - 2f - arm * 4f);
                b.Box(at, along * 0.25f, NV.UnitY * 0.25f, side * (Lines.WiresOn(p) / 2 * 4.2f + 0.6f), PylonSteel, DecoSurface.Metal, collide: true);
            }
            for (int k = 0; k < Lines.WiresOn(p); k++)
            {
                var a = p.Anchor(k, Lines.WiresOn(p));
                b.Box(b.Local(a.X, a.Y + 0.6f, a.Z), NV.UnitX * 0.08f, NV.UnitY * 0.6f, NV.UnitZ * 0.08f, Insulator, DecoSurface.Matte, collide: false);
            }
        }
    }

    internal static readonly DecoRgb Transformer =DecoRgb.Hex("5b6458"), Fence = DecoRgb.Hex("8e9294"), Bushing = DecoRgb.Hex("7a4a32");

    /// <summary>
    /// A substation: a concrete slab on the ground (its foot down to the lowest corner), a fence round it with a gate,
    /// the tall steel gantry the high-voltage line lands on at one end, two transformers in the middle, and the low
    /// gantry the town's lines leave from at the other end.
    /// </summary>
    static void SubstationYard(TileBuilder b, PowerPole p, NV foot, NV side, NV along)
    {
        float ha = Substation.HalfAlong, hc = Substation.HalfAcross;
        float lo = float.MaxValue;
        foreach (int i in new[] { -1, 1 })
            foreach (int j in new[] { -1, 1 })
                lo = Math.Min(lo, b.Ground(p.X - p.SideZ * ha * i + p.SideX * hc * j, p.Z + p.SideX * ha * i + p.SideZ * hc * j));
        float depth = p.Ground - lo + 0.5f;
        b.Box(foot - NV.UnitY * (depth * 0.5f), along * ha, NV.UnitY * (depth * 0.5f), side * hc, PylonConcrete, DecoSurface.Matte, collide: true);
        // The fence: posts round the slab's edge and two rails, a gap for the gate on the low end.
        for (int edge = 0; edge < 4; edge++)
        {
            bool longSide = edge < 2;
            float sgn = edge % 2 == 0 ? 1 : -1, half = longSide ? ha : hc;
            NV o = longSide ? side * (hc - 0.2f) * sgn : along * (ha - 0.2f) * sgn, dir = longSide ? along : side, thin = longSide ? side : along;
            int posts = (int)(half * 2 / 2.6f);
            for (int k = 0; k <= posts; k++)
            {
                float t = -half + 0.2f + (half * 2 - 0.4f) * k / posts;
                if (edge == 3 && Math.Abs(t) < 1.8f) continue;
                b.Box(foot + o + dir * t + NV.UnitY * 1.1f, NV.UnitX * 0.04f, NV.UnitY * 1.1f, NV.UnitZ * 0.04f, Fence, DecoSurface.Metal, collide: true);
            }
            foreach (float y in new[] { 1.1f, 2.15f })
                if (edge == 3)
                    foreach (float s2 in new[] { -1f, 1f })
                        b.Box(foot + o + dir * (s2 * (half + 1.8f) * 0.5f) + NV.UnitY * y, dir * ((half - 1.8f) * 0.5f), NV.UnitY * 0.025f, thin * 0.025f, Fence, DecoSurface.Metal, collide: false);
                else b.Box(foot + o + NV.UnitY * y, dir * (half - 0.2f), NV.UnitY * 0.025f, thin * 0.025f, Fence, DecoSurface.Metal, collide: false);
        }
        // The tall gantry: two steel legs and a beam, insulators hanging where the line lands.
        foreach (float c in new[] { -5f, 5f })
            b.Box(foot + along * Substation.HighAt + side * c + NV.UnitY * (Substation.High * 0.5f), along * 0.22f, NV.UnitY * (Substation.High * 0.5f), side * 0.22f, PylonSteel, DecoSurface.Metal, collide: true);
        b.Box(foot + along * Substation.HighAt + NV.UnitY * (Substation.High + 0.2f), along * 0.2f, NV.UnitY * 0.2f, side * 5.2f, PylonSteel, DecoSurface.Metal, collide: true);
        for (int k = 0; k < 6; k++)
        {
            var a = p.Anchor(k, 6);
            b.Box(b.Local(a.X, a.Y + 0.3f, a.Z), NV.UnitX * 0.07f, NV.UnitY * 0.3f, NV.UnitZ * 0.07f, Insulator, DecoSurface.Matte, collide: false);
        }
        // Two transformers on plinths, finned, with bushings on top.
        foreach (float c in new[] { -2.4f, 2.4f })
        {
            NV t = foot + side * c + along * 0.2f;
            b.Box(t + NV.UnitY * 0.15f, along * 1.5f, NV.UnitY * 0.15f, side * 1.2f, PylonConcrete, DecoSurface.Matte, collide: true);
            b.Box(t + NV.UnitY * 1.5f, along * 1.2f, NV.UnitY * 1.2f, side * 0.9f, Transformer, DecoSurface.Metal, collide: true);
            for (int f = -3; f <= 3; f++)
                b.Box(t + along * (f * 0.33f) + side * (c > 0 ? 1.0f : -1.0f) + NV.UnitY * 1.35f, along * 0.03f, NV.UnitY * 0.9f, side * 0.12f, Transformer, DecoSurface.Metal, collide: false);
            for (int k = -1; k <= 1; k++)
                b.Box(t + along * (k * 0.5f) + NV.UnitY * 3.0f, NV.UnitX * 0.08f, NV.UnitY * 0.3f, NV.UnitZ * 0.08f, Bushing, DecoSurface.Matte, collide: false);
        }
        // The low gantry the town's lines leave from.
        foreach (float c in new[] { -2.2f, 2.2f })
            b.Box(foot + along * Substation.LowAt + side * c + NV.UnitY * (Substation.Low * 0.5f), along * 0.14f, NV.UnitY * (Substation.Low * 0.5f), side * 0.14f, PylonSteel, DecoSurface.Metal, collide: true);
        b.Box(foot + along * Substation.LowAt + NV.UnitY * (Substation.Low + 0.12f), along * 0.12f, NV.UnitY * 0.12f, side * 2.4f, PylonSteel, DecoSurface.Metal, collide: true);
        for (int k = 0; k < 3; k++)
        {
            var a = p.Anchor(k, 3);
            b.Box(b.Local(a.X, a.Y + 0.3f, a.Z), NV.UnitX * 0.05f, NV.UnitY * 0.3f, NV.UnitZ * 0.05f, Insulator, DecoSurface.Matte, collide: false);
        }
    }

    /// <summary>A house's service drop: one thin wire from its pole's bracket to the fixing on its wall, or down where it broke.</summary>
    sealed class DropPiece : Piece
    {
        public PowerLines Lines;
        public ServiceDrop Drop;

        public override void Emit(TileBuilder b)
        {
            PowerPole p = Lines.Poles[Drop.Pole];
            var pts = Lines.DropPath(Drop, b.Ground).Select(q => b.Local(q.X, q.Y, q.Z)).ToList();
            b.Cable(pts, 0.012f, WireDark);
            // The bracket on the pole, and the fixing on the wall where the wire still holds.
            if (!p.Fallen)
            {
                var a = p.DropAnchor(Drop.Fixing.X, Drop.Fixing.Z);
                b.Box(b.Local(a.X, a.Y + 0.06f, a.Z), NV.UnitX * 0.04f, NV.UnitY * 0.08f, NV.UnitZ * 0.04f, Insulator, DecoSurface.Matte, collide: false);
            }
            if (Drop.State is DropState.Strung or DropState.FromHouse)
                b.Box(b.Local(Drop.Fixing.X, Drop.Fixing.Y, Drop.Fixing.Z), NV.UnitX * 0.06f, NV.UnitY * 0.06f, NV.UnitZ * 0.06f, Insulator, DecoSurface.Matte, collide: false);
        }
    }

    /// <summary>A wire: strung with its sag between two standing poles, or hanging from the one that holds it down to the ground.</summary>
    sealed class WirePiece : Piece
    {
        public PowerLines Lines;
        public PowerWire Wire;

        public override void Emit(TileBuilder b)
        {
            float radius = Lines.Poles[Wire.A].Pylon ? 0.04f : 0.022f;
            var pts = Lines.Path(Wire, b.Ground).Select(p => b.Local(p.X, p.Y, p.Z)).ToList();
            b.Cable(pts, radius, WireDark);
        }
    }

    // ---- railways ----------------------------------------------------------------------------------

    /// <summary>One 4 m stretch of railway: sleepers on the ballast and two rails (a few sleepers gone on a long-neglected line).</summary>
    sealed class Track : Piece
    {
        public InfraRoute Route;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Route;
            NV a = b.Local(r.X[K], r.Y[K], r.Z[K]), c = b.Local(r.X[K + 1], r.Y[K + 1], r.Z[K + 1]);
            NV along = c - a;
            float len = along.Length();
            if (len < 1e-3f) return;
            NV dir = along / len, side = NV.Normalize(new NV(-dir.Z, 0, dir.X));
            bool concrete = r.Tech != TechLevel.Low;
            for (int i = 0; i < 6; i++)
            {
                ulong h = Planets.Gen.Hash.Of((ulong)r.Id * 104729UL + (ulong)(K * 6 + i), 0x51EE);
                if (r.Wear > 0.6f && h % 9 == 0) continue;
                NV p = a + dir * (len * (i + 0.5f) / 6f) + NV.UnitY * 0.12f;
                b.Box(p, dir * 0.13f, NV.UnitY * 0.09f, side * 1.3f, concrete ? SleeperConcrete : Sleeper, DecoSurface.Matte, collide: false);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                NV off = side * (s * 0.72f) + NV.UnitY * 0.28f;
                b.Box((a + c) * 0.5f + off, dir * (len * 0.5f + 0.02f), NV.UnitY * 0.08f, side * 0.04f, Rail, DecoSurface.Metal, collide: false);
            }
        }
    }

    /// <summary>The end of a line: the track stops at a buffer, beside a platform with a roof over part of it. All of it stands in the ground.</summary>
    sealed class Station : Piece
    {
        public InfraRoute Route;
        public bool AtStart;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Route;
            int k = AtStart ? 0 : r.Count - 1, step = AtStart ? 1 : -1, far = Math.Clamp(k + step * 12, 0, r.Count - 1);
            NV a = b.Local(r.X[k], r.Y[k], r.Z[k]), c = b.Local(r.X[far], r.Y[far], r.Z[far]);
            NV along = new(c.X - a.X, 0, c.Z - a.Z);
            float length = along.Length();
            if (length < 1f) return;
            NV dir = along / length, side = new(-dir.Z, 0, dir.X);
            DecoRgb body = r.Tech == TechLevel.Low ? Timber : r.Tech == TechLevel.Mid ? Concrete : Composite, dark = r.Tech == TechLevel.Low ? TimberDark : ConcreteDark;
            float deck = (r.Y[k] + r.Y[far]) * 0.5f;
            // Buffers across the end of the track.
            b.Box(a + NV.UnitY * 0.45f, dir * 0.3f, NV.UnitY * 1.2f, side * 1.5f, dark, DecoSurface.Matte, collide: true);
            foreach (int s in new[] { -1, 1 })
            {
                // The platform beside the track on the one side, its back wall on the other way.
                if (s < 0) continue;
                NV mid = a + dir * (length * 0.5f) + side * (r.HalfWidth + 2.2f);
                float low = MathF.Min(deck, b.Ground(b.OriginX + mid.X, b.OriginZ + mid.Z)) - 1.5f, top = deck + 0.9f;
                mid.Y = (low + top) * 0.5f;
                b.Box(mid, dir * (length * 0.5f), NV.UnitY * ((top - low) * 0.5f), side * 2.2f, body, DecoSurface.Matte, collide: true);
                // The shelter: posts on the platform, and a roof on them.
                NV foot = new(mid.X, top, mid.Z);
                foreach (float along2 in new[] { -9f, -3f, 3f, 9f })
                    foreach (float across in new[] { -1.6f, 1.6f })
                        b.Box(foot + dir * along2 + side * across + NV.UnitY * 1.5f, dir * 0.12f, NV.UnitY * 1.5f, side * 0.12f, dark, DecoSurface.Matte, collide: true);
                b.Box(foot + NV.UnitY * 3.1f, dir * 10.5f, NV.UnitY * 0.12f, side * 2.3f, body, DecoSurface.Matte, collide: true);
            }
        }
    }

    /// <summary>A maglev's guideway: a beam high on pylons over everything between its stations.</summary>
    sealed class Guideway : Piece
    {
        public InfraRoute Route;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Route;
            bool raised = r.Span[K] == RoadSpan.Bridge;
            float lift = raised ? 7f : 0.8f;
            NV a = b.Local(r.X[K], r.Y[K] + lift, r.Z[K]), c = b.Local(r.X[K + 1], r.Y[K + 1] + lift, r.Z[K + 1]);
            NV along = c - a;
            float len = along.Length();
            if (len < 1e-3f) return;
            NV dir = along / len, side = NV.Normalize(new NV(-dir.Z, 0, dir.X));
            b.Box((a + c) * 0.5f, dir * (len * 0.5f + 0.03f), NV.UnitY * 0.8f, side * 1.3f, Composite, DecoSurface.Matte, collide: true);
            if (raised && K % 6 == 0)
            {
                float ground = b.Ground(r.X[K], r.Z[K]);
                float h = a.Y - 0.8f - ground + 1f;
                if (h > 0.5f) b.Box(new NV(a.X, ground - 1f + h * 0.5f, a.Z), dir * 0.9f, NV.UnitY * (h * 0.5f), side * 0.9f, Composite, DecoSurface.Matte, collide: true);
            }
        }
    }
}

/// <summary>A tile's mesh data and collision triangles, built on a worker, in the tile's own frame (its corner at the origin).</summary>
public sealed partial class TileBuilder
{
    public readonly PlanetWorld World;
    public readonly double OriginX, OriginZ;
    public readonly DecoMeshData Mesh = new();
    public readonly List<Vector3> Collision = new();
    /// <summary>Godot nodes a piece wants added as they are (deco placements and the like): made on the main thread.</summary>
    public readonly List<Func<Node3D>> Extra = new();
    /// <summary>Colliders a piece wants added (made on the main thread, when something comes near enough to touch them).</summary>
    public readonly List<Action> Solid = new();
    /// <summary>The tile's ore tells, on their own: a broken one takes its rock and collider with it, and the rest of the tile stands.</summary>
    public TileBuilder Tells;
    public bool HasContent => Mesh.Triangles > 0 || Collision.Count > 0;

    public TileBuilder(PlanetWorld world, double ox, double oz)
    {
        World = world;
        OriginX = ox;
        OriginZ = oz;
    }

    /// <summary>A world point (any copy) in the tile's frame, the short way round the seams.</summary>
    public NV Local(double x, float y, double z) =>
        new((float)Mathx.WrapDelta(x - OriginX, World.Width), y, (float)Mathx.WrapDelta(z - OriginZ, World.Height));

    /// <summary>The ground's height at a world point (stamps applied: what the player walks on, before digging).</summary>
    public float Ground(double x, double z) => World.Source.SurfaceHeight(Mathx.WrapX(x, World.Width), Mathx.Wrap(z, World.Height));

    /// <summary>A box from its centre and three half-axes (any orientation), with its faces wound outward.</summary>
    public void Box(NV centre, NV ax, NV ay, NV az, DecoRgb color, DecoSurface surface, bool collide)
    {
        Span<NV> v = stackalloc NV[8];
        for (int i = 0; i < 8; i++)
            v[i] = centre + ((i & 1) != 0 ? ax : -ax) + ((i & 2) != 0 ? ay : -ay) + ((i & 4) != 0 ? az : -az);
        ReadOnlySpan<int> faces = stackalloc int[] { 0, 2, 6, 4, 1, 5, 7, 3, 0, 4, 5, 1, 2, 3, 7, 6, 0, 1, 3, 2, 4, 6, 7, 5 };
        for (int f = 0; f < 6; f++)
        {
            NV p0 = v[faces[f * 4]], p1 = v[faces[f * 4 + 1]], p2 = v[faces[f * 4 + 2]], p3 = v[faces[f * 4 + 3]];
            Quad(p0, p1, p2, p3, color, surface, 1f, centre, collide);
        }
    }

    /// <summary>
    /// A flat slab laid on the ground, bending with it: a grid of points each on the ground under it, its
    /// top a thickness above and its edges down into the ground, solid on top.
    /// </summary>
    public void Drape(NV centre, NV along, NV across, float thick, DecoRgb color, DecoSurface surface)
    {
        int nx = Math.Clamp((int)MathF.Ceiling(along.Length()), 1, 8), nz = Math.Clamp((int)MathF.Ceiling(across.Length()), 1, 8);
        NV Top(int i, int j)
        {
            NV p = centre + along * (2f * i / nx - 1f) + across * (2f * j / nz - 1f);
            p.Y = Ground(OriginX + p.X, OriginZ + p.Z) + thick * 0.5f;
            return p;
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
            Quad(Top(i, j), Top(i + 1, j), Top(i + 1, j + 1), Top(i, j + 1), color, surface, collide: true);
        void Edge(NV p, NV q)
        {
            NV m = (p + q) * 0.5f;
            Quad(p, q, q - NV.UnitY * (thick + 0.3f), p - NV.UnitY * (thick + 0.3f), color, surface, 1f, new NV(centre.X, m.Y, centre.Z));
        }
        for (int i = 0; i < nx; i++) { Edge(Top(i, 0), Top(i + 1, 0)); Edge(Top(i, nz), Top(i + 1, nz)); }
        for (int j = 0; j < nz; j++) { Edge(Top(0, j), Top(0, j + 1)); Edge(Top(nx, j), Top(nx, j + 1)); }
    }

    /// <summary>A cable along a polyline (drawn as thin tubes), in the tile's frame.</summary>
    public void Cable(List<NV> pts, float radius, DecoRgb color)
    {
        foreach (DecoPart part in DecoCables.Tubes(pts, radius, DecoSlot.Dark))
            DecoEmit.Part(Mesh, part, (_, _, _) => color, _ => DecoSurface.Matte);
    }

    /// <summary>A box of collision only (its faces into the tile's collision, nothing drawn).</summary>
    public void CollisionBox(NV centre, NV ax, NV ay, NV az)
    {
        Span<NV> v = stackalloc NV[8];
        for (int i = 0; i < 8; i++)
            v[i] = centre + ((i & 1) != 0 ? ax : -ax) + ((i & 2) != 0 ? ay : -ay) + ((i & 4) != 0 ? az : -az);
        ReadOnlySpan<int> faces = stackalloc int[] { 0, 2, 6, 4, 1, 5, 7, 3, 0, 4, 5, 1, 2, 3, 7, 6, 0, 1, 3, 2, 4, 6, 7, 5 };
        for (int f = 0; f < 6; f++)
            foreach (int k in new[] { 0, 1, 2, 0, 2, 3 })
            {
                NV p = v[faces[f * 4 + k]];
                Collision.Add(new Vector3(p.X, p.Y, p.Z));
            }
    }

    /// <summary>A thin square beam between two points.</summary>
    public void Beam(NV from, NV to, float half, DecoRgb color, bool collide = false)
    {
        NV d = to - from;
        float len = d.Length();
        if (len < 1e-3f) return;
        NV y = d / len;
        NV helper = MathF.Abs(y.Y) < 0.95f ? NV.UnitY : NV.UnitX;
        NV x = NV.Normalize(NV.Cross(helper, y)), z = NV.Cross(x, y);
        Box((from + to) * 0.5f, y * (len * 0.5f), x * half, z * half, color, DecoSurface.Matte, collide);
    }

    /// <summary>A quad (two triangles), wound to face away from <paramref name="inside"/> when given.</summary>
    public void Quad(NV a, NV b, NV c, NV d, DecoRgb color, DecoSurface surface, float alpha = 1f, NV? inside = null, bool collide = false)
    {
        NV n = NV.Cross(b - a, c - a);
        if (inside is NV o && NV.Dot(n, (a + c) * 0.5f - o) < 0) (b, d) = (d, b);
        else if (inside == null && n.Y < 0) (b, d) = (d, b);
        Mesh.AddTriangle(surface, a, b, c, color, alpha);
        Mesh.AddTriangle(surface, a, c, d, color, alpha);
        if (!collide) return;
        // Godot's collision faces wind the other way round from the deco mesh (it swaps them for drawing).
        foreach (NV p in new[] { a, c, b, a, d, c }) Collision.Add(new Vector3(p.X, p.Y, p.Z));
    }

    /// <summary>
    /// Block meshes as a node of their own, a chunk to a step, so a big ruin never stops a frame:
    /// made one at a time on the main thread, their colliders with <see cref="Solid"/>.
    /// </summary>
    public void Blocks(string name, Vector3 at, List<PoiGenerator.Gen.ChunkMesh> meshes, Func<Godot.Material[]> materials)
    {
        Node3D node = null;
        StaticBody3D body = null;
        Extra.Add(() =>
        {
            node = new Node3D { Name = name, Position = at };
            body = new StaticBody3D { Name = "Collision", CollisionLayer = PlanetSurface.Poi.PoiLayer.PlaceLayer, CollisionMask = 0 };
            node.AddChild(body);
            return node;
        });
        foreach (PoiGenerator.Gen.ChunkMesh chunk in meshes)
        {
            Extra.Add(() =>
            {
                node.AddChild(new MeshInstance3D { Mesh = PoiGenerator.MeshBuild.ToMesh(chunk, materials()) });
                return null;
            });
            if (chunk.Collision.Count > 0) Solid.Add(() => body.AddChild(new CollisionShape3D { Shape = PoiGenerator.MeshBuild.ToShape(chunk) }));
        }
    }

    /// <summary>The tile's nodes as steps for the main thread: its mesh, its collision's body, then each extra. A step may return nothing (it added to a node of its own).</summary>
    public Queue<Func<Node3D>> Steps()
    {
        var steps = new Queue<Func<Node3D>>();
        if (Mesh.Triangles > 0) steps.Enqueue(() => new MeshInstance3D { Mesh = DecoGodot.ToMesh(Mesh), Name = "Mesh" });
        if (Collision.Count > 0) steps.Enqueue(() => _body = new StaticBody3D { Name = "Collision", CollisionLayer = PlanetSurface.Poi.PoiLayer.PlaceLayer, CollisionMask = 0 });
        if (Tells is { HasContent: true }) steps.Enqueue(() => Tells.ToNode("Tells"));
        foreach (var make in Extra) steps.Enqueue(make);
        return steps;
    }

    StaticBody3D _body;

    /// <summary>The tile's colliders as steps for the main thread, after <see cref="Steps"/>.</summary>
    public Queue<Action> SolidSteps()
    {
        var steps = new Queue<Action>();
        if (Collision.Count > 0)
        {
            Vector3[] faces = Collision.ToArray();
            steps.Enqueue(() =>
            {
                var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
                shape.SetFaces(faces);
                _body.AddChild(new CollisionShape3D { Shape = shape });
            });
        }
        foreach (Action make in Solid) steps.Enqueue(make);
        return steps;
    }

    /// <summary>The whole tile as one node at once (screenshots and tests).</summary>
    public Node3D ToNode(string name)
    {
        var node = new Node3D { Name = name };
        foreach (var make in Steps())
            if (make() is Node3D part && part.GetParent() == null) node.AddChild(part);
        foreach (Action make in SolidSteps()) make();
        return node;
    }
}
