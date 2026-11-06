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
    /// <summary>Tiles within this distance of the camera are built; past it plus a margin they are freed.</summary>
    public static float Radius = 900f;

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
    }

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
    public bool Built => _pieces != null && _tiles.Values.All(t => t.Node != null);

    public void Build(PlanetWorld world)
    {
        Clear();
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
        int r = (int)Math.Ceiling(Radius / TileSize) + 1;
        int ci = (int)(cx / TileSize), cj = (int)(cz / TileSize);
        // Start the tiles in reach that have something in them.
        for (int dj = -r; dj <= r; dj++)
        for (int di = -r; di <= r; di++)
        {
            int ix = ((ci + di) % _tilesX + _tilesX) % _tilesX, iz = ((cj + dj) % _tilesZ + _tilesZ) % _tilesZ;
            long key = Key(ix, iz);
            if (_tiles.ContainsKey(key) || !_pieces.TryGetValue(key, out var list)) continue;
            if (TileDistance(ix, iz, cx, cz) > Radius) continue;
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
        // One finished tile a frame becomes a node; tiles far out are freed.
        bool made = false;
        var drop = new List<long>();
        foreach (var (key, tile) in _tiles)
        {
            if (TileDistance(tile.Ix, tile.Iz, cx, cz) > Radius + TileSize)
            {
                drop.Add(key);
                continue;
            }
            if (tile.Node == null && !made && tile.Building.IsCompleted)
            {
                tile.Node = tile.Building.Result.ToNode($"Infra {tile.Ix},{tile.Iz}");
                AddChild(tile.Node);
                made = true;
            }
            if (tile.Node != null)
                tile.Node.Position = _world.NearestCopy(new Vector3((float)(tile.Ix * TileSize), 0f, (float)(tile.Iz * TileSize)), camera);
        }
        foreach (long key in drop)
        {
            _tiles[key].Node?.QueueFree();
            _tiles.Remove(key);
        }
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
        PowerLines power = net.Power;
        for (int i = 0; i < power.Poles.Count; i++)
            yield return new PolePiece { Lines = power, Index = i, X = power.Poles[i].X, Z = power.Poles[i].Z };
        foreach (PowerWire wire in power.Wires)
            yield return new WirePiece { Lines = power, Wire = wire, X = power.Poles[wire.A].X, Z = power.Poles[wire.A].Z };
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
                // The fallen deck lies broken on the ground below, two slabs tilted against each other.
                float g0 = b.Ground(r.X[K], r.Z[K]), g1 = b.Ground(r.X[K + 1], r.Z[K + 1]);
                NV p0 = new(a.X, g0 + thick * 0.5f + 0.1f, a.Z), p1 = new(c.X, g1 + thick * 0.5f + 0.1f, c.Z);
                NV m = (p0 + p1) * 0.5f;
                NV tilt = NV.Normalize(dir + up * 0.25f);
                NV tUp = NV.Normalize(NV.Cross(side, tilt));
                if (tUp.Y < 0) tUp = -tUp;
                b.Box(m + up * 0.6f, tilt * (len * 0.45f), tUp * (thick * 0.5f), side * (half * 0.9f), deck, surface, collide: true);
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
            int k = AtStart ? Structure.First : Structure.Last, n = AtStart ? Math.Min(r.Count - 1, k + 1) : Math.Max(0, k - 1);
            NV a = b.Local(r.X[k], r.Y[k], r.Z[k]), c = b.Local(r.X[n], r.Y[n], r.Z[n]);
            NV into = NV.Normalize(new NV(c.X - a.X, 0, c.Z - a.Z)), side = new(-into.Z, 0, into.X);
            float bore = r.HalfWidth + 1.2f, height = r.Kind == RouteKind.Rail ? 7f : 6.5f;
            DecoRgb col = r.Tech == TechLevel.Low ? Stone : r.Tech == TechLevel.Mid ? Concrete : Composite;
            NV face = a + into * 0.6f;
            // Pillars either side of the bore, and the lintel over it, a metre into the slope.
            for (int s = -1; s <= 1; s += 2)
                b.Box(face + side * (s * (bore + 1.2f)) + NV.UnitY * (height * 0.5f + 0.5f), into * 0.8f, NV.UnitY * (height * 0.5f + 1.5f), side * 1.2f, col, DecoSurface.Matte, collide: true);
            b.Box(face + NV.UnitY * (height + 1.2f), into * 0.8f, NV.UnitY * 1.0f, side * (bore + 2.4f), col, DecoSurface.Matte, collide: true);
            // A lining a few metres into the bore, so the mouth reads as built, not dug.
            for (int s = -1; s <= 1; s += 2)
                b.Box(face + into * 3f + side * (s * (bore + 0.25f)) + NV.UnitY * (height * 0.5f), into * 2.4f, NV.UnitY * (height * 0.5f), side * 0.25f, col, DecoSurface.Matte, collide: true);
        }
    }

    /// <summary>Standing water in a neglected tunnel's low middle.</summary>
    sealed class FloodStretch : Piece
    {
        public InfraStructure Structure;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Structure.Route;
            float level = Math.Min(r.Y[Structure.First], r.Y[Structure.Last]) - 0.3f;
            if (Structure.Flooded && level <= Math.Min(r.Y[K], r.Y[K + 1]) + 0.05f) level = Math.Min(r.Y[K], r.Y[K + 1]) + 0.45f;
            if (level <= Math.Min(r.Y[K], r.Y[K + 1])) return;
            NV a = b.Local(r.X[K], level, r.Z[K]), c = b.Local(r.X[K + 1], level, r.Z[K + 1]);
            NV dir = c - a;
            if (dir.LengthSquared() < 1e-6f) return;
            NV side = NV.Normalize(new NV(-dir.Z, 0, dir.X)) * (r.HalfWidth + 1.1f);
            b.Quad(a - side, a + side, c + side, c - side, Water, DecoSurface.Glass, 0.8f);
        }
    }

    // ---- power lines -------------------------------------------------------------------------------

    internal static readonly DecoRgb PoleWood = DecoRgb.Hex("4f3d2c"), PylonSteel = DecoRgb.Hex("6e7072"), Insulator = DecoRgb.Hex("a8b4b8"), WireDark = DecoRgb.Hex("1e2024");

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
            if (p.Fallen)
            {
                // Lying along the line, its foot snapped off at the stump.
                b.Box(foot + NV.UnitY * 0.4f, NV.UnitX * 0.14f, NV.UnitY * 0.4f, NV.UnitZ * 0.14f, p.Pylon ? PylonSteel : PoleWood, DecoSurface.Matte, collide: true);
                NV lieDir = NV.Normalize(along + side * 0.25f);
                NV mid = foot + lieDir * (h * 0.5f) + NV.UnitY * 0.2f;
                if (p.Pylon) b.Box(mid, lieDir * (h * 0.5f), NV.UnitY * 0.9f, side * 1.4f, PylonSteel, DecoSurface.Metal, collide: true);
                else
                {
                    b.Box(mid, lieDir * (h * 0.5f), NV.UnitY * 0.13f, side * 0.13f, PoleWood, DecoSurface.Matte, collide: true);
                    b.Box(foot + lieDir * (h - 0.4f) + NV.UnitY * 0.3f, lieDir * 0.07f, NV.UnitY * 0.07f, side * 1.25f, PoleWood, DecoSurface.Matte, collide: false);
                }
                return;
            }
            if (!p.Pylon)
            {
                b.Box(foot + NV.UnitY * (h * 0.5f - 0.6f), NV.UnitX * 0.13f, NV.UnitY * (h * 0.5f + 0.6f), NV.UnitZ * 0.13f, PoleWood, DecoSurface.Matte, collide: true);
                b.Box(foot + NV.UnitY * (h - 0.45f), along * 0.07f, NV.UnitY * 0.07f, side * 1.3f, PoleWood, DecoSurface.Matte, collide: false);
                for (int k = 0; k < Lines.WiresOn(p); k++)
                {
                    var a = p.Anchor(k, Lines.WiresOn(p));
                    b.Box(b.Local(a.X, a.Y - 0.1f, a.Z), NV.UnitX * 0.05f, NV.UnitY * 0.1f, NV.UnitZ * 0.05f, Insulator, DecoSurface.Matte, collide: false);
                }
                return;
            }
            // A lattice pylon: four legs tapering up, braced in tiers, and two crossarms.
            float baseHalf = 3.2f, topHalf = 0.8f;
            NV[] bottom = { foot + (side + along) * baseHalf, foot + (side - along) * baseHalf, foot + (-side - along) * baseHalf, foot + (-side + along) * baseHalf };
            NV TopOf(NV c) => foot + (c - foot) * (topHalf / baseHalf) + NV.UnitY * h;
            for (int i = 0; i < 4; i++)
            {
                b.Beam(bottom[i] - NV.UnitY * 1f, TopOf(bottom[i]), 0.12f, PylonSteel);
                NV n0 = bottom[i], n1 = bottom[(i + 1) % 4];
                for (int tier = 0; tier < 4; tier++)
                {
                    float t0 = tier / 4f, t1 = (tier + 1) / 4f;
                    NV a0 = NV.Lerp(n0, TopOf(n0), t0), b1 = NV.Lerp(n1, TopOf(n1), t1);
                    b.Beam(a0, b1, 0.05f, PylonSteel);
                    b.Beam(NV.Lerp(n0, TopOf(n0), t1), NV.Lerp(n1, TopOf(n1), t1), 0.05f, PylonSteel);
                }
            }
            for (int arm = 0; arm < 2; arm++)
            {
                NV at = foot + NV.UnitY * (h - 2f - arm * 4f);
                b.Box(at, along * 0.25f, NV.UnitY * 0.25f, side * (Lines.WiresOn(p) / 2 * 4.2f + 0.6f), PylonSteel, DecoSurface.Metal, collide: false);
            }
            for (int k = 0; k < Lines.WiresOn(p); k++)
            {
                var a = p.Anchor(k, Lines.WiresOn(p));
                b.Box(b.Local(a.X, a.Y + 0.6f, a.Z), NV.UnitX * 0.08f, NV.UnitY * 0.6f, NV.UnitZ * 0.08f, Insulator, DecoSurface.Matte, collide: false);
            }
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
public sealed class TileBuilder
{
    public readonly PlanetWorld World;
    public readonly double OriginX, OriginZ;
    public readonly DecoMeshData Mesh = new();
    public readonly List<Vector3> Collision = new();
    /// <summary>Godot nodes a piece wants added as they are (deco placements and the like): made on the main thread.</summary>
    public readonly List<Func<Node3D>> Extra = new();

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

    /// <summary>A cable along a polyline (drawn as thin tubes), in the tile's frame.</summary>
    public void Cable(List<NV> pts, float radius, DecoRgb color)
    {
        foreach (DecoPart part in DecoCables.Tubes(pts, radius, DecoSlot.Dark))
            DecoEmit.Part(Mesh, part, (_, _, _) => color, _ => DecoSurface.Matte);
    }

    /// <summary>A thin square beam between two points.</summary>
    public void Beam(NV from, NV to, float half, DecoRgb color)
    {
        NV d = to - from;
        float len = d.Length();
        if (len < 1e-3f) return;
        NV y = d / len;
        NV helper = MathF.Abs(y.Y) < 0.95f ? NV.UnitY : NV.UnitX;
        NV x = NV.Normalize(NV.Cross(helper, y)), z = NV.Cross(x, y);
        Box((from + to) * 0.5f, y * (len * 0.5f), x * half, z * half, color, DecoSurface.Matte, collide: false);
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

    public Node3D ToNode(string name)
    {
        var node = new Node3D { Name = name };
        if (Mesh.Triangles > 0)
            node.AddChild(new MeshInstance3D { Mesh = DecoGodot.ToMesh(Mesh), Name = "Mesh" });
        if (Collision.Count > 0)
        {
            var body = new StaticBody3D { Name = "Collision", CollisionLayer = Layers.Poi, CollisionMask = 0 };
            var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
            shape.SetFaces(Collision.ToArray());
            body.AddChild(new CollisionShape3D { Shape = shape });
            node.AddChild(body);
        }
        foreach (var make in Extra) node.AddChild(make());
        return node;
    }
}
