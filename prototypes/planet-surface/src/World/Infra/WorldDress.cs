using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

/// <summary>One piece of a world's dressing, placed: what it is, where, which way, in which era and how worn.</summary>
public sealed class DressItem
{
    public DressPart Part = null!;
    public string Model = "", Style = "";
    public DressSite Site;
    /// <summary>World metres (canonical) of its middle on the ground; a span's middle between its two posts.</summary>
    public double X, Z;
    /// <summary>Radians about +y turning the model's +z onto the world (Godot's convention: +z turned by yaw).</summary>
    public float Yaw;
    /// <summary>A span's length between its posts, metres.</summary>
    public float Length = 6f;
    public DecoTech Era;
    public float Wear;
    public bool Lit;
    /// <summary>Kept by the living: put up fresh and kept up.</summary>
    public bool Kept;
    public ulong Seed;
    /// <summary>The house whose front or garden it is on (null elsewhere).</summary>
    public FlavourRuin Ruin;
    /// <summary>A wall mark: which way along the wall from the door, metres (its foot is the door's).</summary>
    public float Along;

    public DressArgs Args(float leftY = 0f, float rightY = 0f) => new(Style, Era, Wear, Lit, Seed, Length, leftY, rightY);

    /// <summary>The model's own x axis in the world (across a span).</summary>
    public (double X, double Z) Across => (Math.Cos(Yaw), -Math.Sin(Yaw));
}

/// <summary>
/// A world dressed by its layers (docs/DESIGN.md section 8): the dressing table (data/world-dressing.txt)
/// read against the world's roll, and each part set down by its rule: bunting and tape across the streets,
/// stalls, banners and lamps along them, floats and barricades in their lanes, stands and tents in the open
/// ground at a town's crossings, tables and washing in the gardens, marks on the doors, luggage and wire and
/// shell holes along the roads out, thinning with distance from where people lived. How strongly the world
/// leans to a part sets how thick it lies. Every part takes the world's era and its age. Deterministic from
/// the world; nothing here names a theme, a fall or a part.
/// </summary>
public sealed class WorldDress
{
    public readonly List<DressItem> Items = new();
    /// <summary>The marks and fittings on each house's front (drawn with the house, on what's left of its walls).</summary>
    public readonly Dictionary<FlavourRuin, List<DressItem>> OnHouse = new();
    /// <summary>How strongly the world leans to each part, the dead's and the living's.</summary>
    public Dictionary<string, float> Old = new(), Now = new();
    /// <summary>The world's emblem (its banners, flags, shrines and sigils) and a second for the other side's flag.</summary>
    public string Emblem = "disc", Rival = "ring";
    public DecoTech Before = DecoTech.Mid, After = DecoTech.Low;
    /// <summary>Where people lived: settlement middles, for the thinning.</summary>
    public readonly List<(double X, double Z)> Towns = new();
    public double PlanMs;

    public static readonly WorldDress None = new();

    static readonly string[] Emblems = { "disc", "ring", "star", "triangle", "sun", "eye", "stripes", "wave", "spiral", "wheel" };

    public static DecoTech Era(TechLevel t) => t switch { TechLevel.Low => DecoTech.Low, TechLevel.High => DecoTech.High, _ => DecoTech.Mid };

    /// <summary>Plans a world's dressing (on a worker: the infrastructure layer asks for it with its other pieces).</summary>
    public static WorldDress Plan(PlanetWorld world)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        WorldProfile profile = world.Profile;
        PoiLibrary lib = world.Library;
        WorldRoll roll = world.Layers;
        if (lib == null || roll == null || profile == null || !profile.Inhabited) return None;
        var dress = new WorldDress();
        var planner = new Planner(world, lib.Dressing, roll, dress);
        planner.Run();
        dress.PlanMs = watch.Elapsed.TotalMilliseconds;
        return dress;
    }

    sealed class Planner
    {
        readonly PlanetWorld _w;
        readonly InfraNetwork _net;
        readonly DressBook _book;
        readonly WorldRoll _roll;
        readonly WorldDress _d;
        readonly double _width, _height;
        readonly PoiGenerator.Gen.Rng _rng;
        readonly Dictionary<(int, int), List<(double X, double Z, float R, bool Solid)>> _claims = new();
        readonly HashSet<InfraRoute> _streets = new();
        readonly float _wear;
        readonly bool _falling;
        readonly List<(DressPart Part, float S)> _parts = new();
        (double X, double Z)? _keepClear;

        public Planner(PlanetWorld w, DressBook book, WorldRoll roll, WorldDress d)
        {
            _w = w;
            _net = w.Infra;
            _book = book;
            _roll = roll;
            _d = d;
            _width = w.Width;
            _height = w.Height;
            _rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(w.Spec.Seed, 0xD7E55UL));
            RuinAge ruin = w.Profile.Ruin ?? RuinAge.Default;
            _falling = ruin.Falling;
            _wear = ruin.Falling ? 0.05f : ruin.Wear;
            d.Before = Era(w.Profile.Before);
            d.After = Era(w.Profile.Now);
        }

        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, _width), 2) + Math.Pow(Mathx.WrapDelta(az - bz, _height), 2));

        public void Run()
        {
            (_d.Old, _d.Now) = _book.Strengths(_roll);
            foreach (DressPart p in _book.Parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                float s = Math.Min(DressBook.Most, _d.Old.GetValueOrDefault(p.Id) + _d.Now.GetValueOrDefault(p.Id));
                if (s > 0.01f) _parts.Add((p, s));
            }
            // The world's emblem: a fist on a world that struck, the sun or an eye on one that prayed, else its own.
            bool Has(string id) => _roll.Rolled.Any(l => l.Id == id);
            _d.Emblem = Has("strike_banners") || Has("general_stoppage") || Has("mutiny") ? "fist"
                : Has("faith_takeover") || Has("pilgrim_roads") || Has("coreward_leaners") ? (_rng.Chance(0.5f) ? "sun" : "eye")
                : Emblems[(int)(_rng.NextU64() % (ulong)Emblems.Length)];
            do _d.Rival = Emblems[(int)(_rng.NextU64() % (ulong)Emblems.Length)]; while (_d.Rival == _d.Emblem);
            if (_parts.Count == 0) return;

            Gather();
            // The bunker's ground at a start town's edge stays clear.
            if (_net.Town is StartTown t) _keepClear = (t.EdgeX, t.EdgeZ);
            Claim(_w.Landing.X, _w.Landing.Y, 20f, true);
            var ways = _net.Routes.Where(r => r.Kind is RouteKind.Highway or RouteKind.Road or RouteKind.Track && r.Count > 4).OrderBy(r => r.Id).ToList();
            // In town a way is a street; out of it, a road.
            foreach (InfraRoute r in ways) Street(r);
            Squares(ways);
            Houses();
            foreach (InfraRoute r in ways) Road(r);
            Gates();
        }

        /// <summary>Town ground, by 16 m cell: round houses that stand on a way, and along a town's or a city's own streets.</summary>
        readonly HashSet<(int, int)> _town = new();

        bool InTown(double x, double z) => _town.Contains(Cell(Mathx.WrapX(x, _width), Mathx.Wrap(z, _height)));

        void MarkTown(double x, double z, double r)
        {
            for (double dz = -r; dz <= r; dz += 8)
                for (double dx = -r; dx <= r; dx += 8)
                    if (dx * dx + dz * dz <= r * r) _town.Add(Cell(Mathx.WrapX(x + dx, _width), Mathx.Wrap(z + dz, _height)));
        }

        /// <summary>Where the towns are, and where people lived: town middles, cities, settled places, each street of houses.</summary>
        void Gather()
        {
            void Streets(IEnumerable<InfraRoute> streets)
            {
                foreach (InfraRoute r in streets)
                {
                    _streets.Add(r);
                    for (int k = 0; k < r.Count; k += 4) MarkTown(r.X[k], r.Z[k], 24);
                }
            }
            if (_net.Town is StartTown t)
            {
                Streets(t.Streets);
                _d.Towns.Add((t.CenterX, t.CenterZ));
            }
            if (_net.City is CityPlan c)
            {
                Streets(c.Streets);
                _d.Towns.Add((c.CenterX, c.CenterZ));
            }
            // Houses on a way make it a street there; one town point for each 200 m of them, for the thinning.
            var clusters = new HashSet<(int, int)>();
            foreach (FlavourRuin f in _net.Ruins)
            {
                if (f.Route == null || !(f.Family.Tags.Contains("house") || f.Family.Tags.Contains("shop"))) continue;
                var (fx, fz) = f.Center;
                MarkTown(fx, fz, 48);
                if (clusters.Add(((int)Math.Floor(fx / 200), (int)Math.Floor(fz / 200)))) _d.Towns.Add((fx, fz));
            }
            foreach (PoiSite p in _w.Pois)
                if (p.Tier is PoiTier.Town or PoiTier.City or PoiTier.Spaceport || p.Family.Tags.Contains("settlement")) _d.Towns.Add(p.Center);
        }

        /// <summary>How thick a part lies this far from where people lived: whole in a town, a third at its <c>far</c>.</summary>
        float Fade(double x, double z, DressPart p)
        {
            double d = double.MaxValue;
            foreach (var (tx, tz) in _d.Towns) d = Math.Min(d, Dist(x, z, tx, tz));
            if (d <= 250) return 1f;
            return (float)Math.Pow(1.0 / 3.0, (d - 250) / Math.Max(50, p.Far));
        }

        // ---- claims: nothing set down inside another's reach, nor on a road, a pad, a house or water ----

        static (int, int) Cell(double x, double z) => ((int)Math.Floor(x / 16), (int)Math.Floor(z / 16));

        bool Free(double x, double z, float r, bool solid)
        {
            x = Mathx.WrapX(x, _width);
            z = Mathx.Wrap(z, _height);
            var (cx, cz) = Cell(x, z);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (_claims.TryGetValue((cx + dx, cz + dz), out var list))
                        foreach (var c in list)
                            // Flat things may lie under a span's line, never under one another or a solid.
                            if (Dist(x, z, c.X, c.Z) < (solid || c.Solid ? r + c.R : (r + c.R) * 0.6f)) return false;
            if (_keepClear is { } k && Dist(x, z, k.X, k.Z) < 40 + r) return false;
            return true;
        }

        void Claim(double x, double z, float r, bool solid)
        {
            x = Mathx.WrapX(x, _width);
            z = Mathx.Wrap(z, _height);
            var key = Cell(x, z);
            if (!_claims.TryGetValue(key, out var list)) _claims[key] = list = new();
            list.Add((x, z, r, solid));
        }

        /// <summary>Open ground: no water, no place's pad, no house, no wreck, not too steep under it.</summary>
        bool Ground(double x, double z, float r)
        {
            PlanetTerrainSource src = _w.Source;
            double wx = Mathx.WrapX(x, _width), wz = Mathx.Wrap(z, _height);
            if (src.WaterSurfaceAt(wx, wz, out _)) return false;
            foreach (PoiSite p in _w.Pois)
                if (Math.Abs(Mathx.WrapDelta(p.Center.X - x, _width)) < p.Stamp.ReachX + r + 4 && Math.Abs(Mathx.WrapDelta(p.Center.Z - z, _height)) < p.Stamp.ReachZ + r + 4 && Near(p.Stamp, x, z, r + 2)) return false;
            foreach (FlavourRuin f in _net.Ruins)
            {
                var (fx, fz) = f.Center;
                if (Math.Abs(Mathx.WrapDelta(fx - x, _width)) > 60 || Math.Abs(Mathx.WrapDelta(fz - z, _height)) > 60) continue;
                if (InBuilding(f, x, z, r + 0.6f)) return false;
            }
            foreach (WreckSite w in _net.Wrecks)
            {
                var (mx, mz) = w.World(w.Width / 2, w.Length / 2);
                if (Dist(x, z, mx, mz) < Math.Max(w.Width, w.Length) / 2 + r + 1) return false;
            }
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < 5; k++)
            {
                double a = k * Math.PI * 2 / 5;
                float g = src.SurfaceHeight(Mathx.WrapX(x + Math.Cos(a) * r, _width), Mathx.Wrap(z + Math.Sin(a) * r, _height));
                lo = Math.Min(lo, g);
                hi = Math.Max(hi, g);
            }
            return hi - lo <= 0.35f + r * 0.18f;
        }

        static bool Near(TerrainStamp s, double x, double z, double r)
        {
            for (int k = 0; k < 9; k++)
            {
                double a = k * Math.PI * 2 / 8, d = k == 8 ? 0 : r;
                if (s.OnFootprint(x + Math.Cos(a) * d, z + Math.Sin(a) * d)) return true;
            }
            return false;
        }

        /// <summary>Within a house's built cells (its lot's yard is open ground).</summary>
        bool InBuilding(FlavourRuin f, double x, double z, float r)
        {
            CellMask m = f.Layout.Occupied;
            double lx = Mathx.WrapDelta(x - f.OriginX, _width), lz = Mathx.WrapDelta(z - f.OriginZ, _height);
            return lx > m.Min.X - r && lx < m.Min.X + m.Size.X + r && lz > m.Min.Z - r && lz < m.Min.Z + m.Size.Z + r;
        }

        bool OffRoads(double x, double z, float margin) => _net.OffRoad(Mathx.WrapX(x, _width), Mathx.Wrap(z, _height), margin);

        // ---- placing --------------------------------------------------------------------------------

        DressItem Make(DressPart p, DressSite site, double x, double z, float yaw)
        {
            float old = _d.Old.GetValueOrDefault(p.Id), now = _d.Now.GetValueOrDefault(p.Id);
            bool kept = now > old;
            DecoTech era = kept ? _d.After : _d.Before;
            float wear = kept ? 0.08f : _wear;
            // Lamps that still burn: kept ones mostly, a high world's cells now and then, almost never an old one.
            float lit = kept ? 0.8f : _falling ? 0.55f : era switch { DecoTech.High => 0.45f * (1 - wear * 0.5f), DecoTech.Mid => 0.06f, _ => 0.02f };
            string style = p.Style;
            if (p.Model == "mark" && style == "sigil") style = "sigil:" + _d.Emblem;
            else if (p.Model is "banner" or "banner_span" or "shrine" or "memorial" or "float" or "stage") style = _d.Emblem;
            else if (p.Model == "two_flags") style = _d.Emblem + ":" + _d.Rival;
            else if (p.Model == "picket") style = _d.Emblem == "fist" ? "fist" : _d.Emblem;
            else if (p.Model == "bell_post" && _roll.Rolled.Any(l => l.Id == "silence")) style = "silent";
            ulong seed = PoiGenerator.Gen.Hash.Combine(_rng.NextU64(), PoiGenerator.Gen.Hash.Of(p.Id));
            return new DressItem
            {
                Part = p, Model = p.Model, Style = style, Site = site, X = Mathx.WrapX(x, _width), Z = Mathx.Wrap(z, _height), Yaw = yaw,
                Era = era, Wear = wear, Kept = kept, Lit = _rng.Chance(lit), Seed = seed,
            };
        }

        void Add(DressItem item, float reach)
        {
            _d.Items.Add(item);
            Claim(item.X, item.Z, reach, item.Part.Solid);
        }

        static float YawOf(double dx, double dz) => (float)Math.Atan2(dx, dz);

        /// <summary>A way's direction and its left-hand normal at a point.</summary>
        static (double Dx, double Dz, double Nx, double Nz) Frame(InfraRoute r, int k)
        {
            int a = Math.Max(0, k - 1), b = Math.Min(r.Count - 1, k + 1);
            double dx = r.X[b] - r.X[a], dz = r.Z[b] - r.Z[a], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            dx /= len;
            dz /= len;
            return (dx, dz, -dz, dx);
        }

        bool OnGround(InfraRoute r, int k) => r.Span.Length <= k || r.Span[k] == RoadSpan.Ground;

        /// <summary>Walks a way's points, each part at its own spacing (thicker where the world leans harder and nearer a town).</summary>
        void Walk(InfraRoute r, IEnumerable<(DressPart Part, float S, DressSite Site)> parts, Action<DressPart, DressSite, int, float> at)
        {
            foreach (var (p, s, site) in parts)
            {
                double run = 0, next = p.Every * (0.3 + 0.7 * _rng.Float()) / s;
                for (int k = 2; k + 2 < r.Count; k++)
                {
                    run += InfraNetwork.Spacing;
                    if (run < next || !OnGround(r, k)) continue;
                    float f = Fade(r.X[k], r.Z[k], p);
                    float density = s * f;
                    if (density < 0.04f) { next = run + p.Every; continue; }
                    at(p, site, k, density);
                    next = run + p.Every * (0.6 + 0.8 * _rng.Float()) / density;
                }
            }
        }

        void Street(InfraRoute r)
        {
            var parts = _parts.SelectMany(t => t.Part.At.Where(s => s is DressSite.Span or DressSite.Kerb or DressSite.Lane).Select(s => (t.Part, t.S, s))).ToList();
            Walk(r, parts, (p, site, k, density) =>
            {
                if (!InTown(r.X[k], r.Z[k])) return;
                var (dx, dz, nx, nz) = Frame(r, k);
                double cx = r.X[k], cz = r.Z[k];
                switch (site)
                {
                    case DressSite.Span:
                    {
                        // Across the street from kerb to kerb: both posts on open ground, no other way near.
                        double half = r.HalfWidth + 0.9;
                        double lx = cx - nx * half, lz = cz - nz * half, rx = cx + nx * half, rz = cz + nz * half;
                        if (!OffRoads(lx, lz, 0.35f) || !OffRoads(rx, rz, 0.35f) || !Free(cx, cz, 1f, false)) return;
                        if (!Ground(lx, lz, 0.4f) || !Ground(rx, rz, 0.4f)) return;
                        DressItem it = Make(p, site, cx, cz, YawOf(dx, dz));
                        it.Length = (float)(half * 2);
                        Add(it, p.Reach);
                        Claim(lx, lz, 0.4f, true);
                        Claim(rx, rz, 0.4f, true);
                        break;
                    }
                    case DressSite.Kerb:
                    {
                        int side = _rng.Chance(0.5f) ? 1 : -1;
                        double off = r.HalfWidth + 1.1 + p.Reach * 0.7;
                        double x = cx + nx * off * side, z = cz + nz * off * side;
                        if (!OffRoads(x, z, Math.Max(0.3f, p.Reach * 0.5f)) || !Free(x, z, p.Reach, p.Solid) || !Ground(x, z, p.Reach)) return;
                        // Facing the street.
                        Add(Make(p, site, x, z, YawOf(nx * side, nz * side)), p.Reach);
                        break;
                    }
                    case DressSite.Lane:
                    {
                        // In one lane, the other kept clear; a float or a barricade only where the street is wide enough to pass.
                        if (p.Solid && r.HalfWidth < 3.4f) return;
                        int side = _rng.Chance(0.5f) ? 1 : -1;
                        double off = p.Solid ? r.HalfWidth * 0.5 : _rng.Float(-r.HalfWidth * 0.7f, r.HalfWidth * 0.7f);
                        double x = cx + nx * off * side, z = cz + nz * off * side;
                        if (_net.Nearest(x, z, r.HalfWidth + p.Reach + 6, out _, out _, new[] { r }) != null) return;
                        if (!Free(x, z, p.Reach, p.Solid) || !Ground(x, z, p.Solid ? 1f : 0.3f)) return;
                        Add(Make(p, site, x, z, YawOf(dx, dz) + (_rng.Chance(0.5f) ? MathF.PI : 0f)), p.Reach);
                        break;
                    }
                }
            });
        }

        /// <summary>Open ground at a town's crossings and round its middle: stands, stages, tents, a soup kitchen.</summary>
        void Squares(List<InfraRoute> ways)
        {
            var parts = _parts.Where(t => t.Part.At.Contains(DressSite.Square)).ToList();
            if (parts.Count == 0) return;
            var spots = new List<(double X, double Z)>();
            foreach (InfraRoute r in ways)
                foreach (int k in new[] { 0, r.Count - 1, r.Count / 2 })
                    if (InTown(r.X[k], r.Z[k])) spots.Add((r.X[k], r.Z[k]));
            if (_net.Town is StartTown t)
                for (int c = -1; c <= 1; c++) spots.Add((t.CenterX + t.AlongX * c * t.Block, t.CenterZ + t.AlongZ * c * t.Block));
            // Each crossing once.
            var crossings = new List<(double X, double Z)>();
            foreach (var s in spots)
                if (!crossings.Any(c => Dist(c.X, c.Z, s.X, s.Z) < 30)) crossings.Add(s);
            foreach (var (jx, jz) in crossings.OrderBy(_ => _rng.Float()))
                foreach (var (p, s) in parts.OrderBy(_ => _rng.Float()))
                {
                    float f = Fade(jx, jz, p);
                    if (!_rng.Chance(Math.Min(0.95f, p.Chance * s * f / 1.5f))) continue;
                    for (int tries = 0; tries < 14; tries++)
                    {
                        double a = _rng.Float() * Math.PI * 2, d = _rng.Float(9f + p.Reach, 26f + p.Reach);
                        double x = jx + Math.Cos(a) * d, z = jz + Math.Sin(a) * d;
                        if (!OffRoads(x, z, p.Reach + 0.8f) || !Free(x, z, p.Reach, p.Solid) || !Ground(x, z, p.Reach)) continue;
                        // Facing the crossing.
                        Add(Make(p, DressSite.Square, x, z, YawOf(jx - x, jz - z) + MathF.PI), p.Reach);
                        break;
                    }
                }
        }

        /// <summary>House fronts: marks by the door and an awning or a sign over a shop's, things in the front garden.</summary>
        void Houses()
        {
            var wall = _parts.Where(t => t.Part.At.Contains(DressSite.Wall)).ToList();
            var garden = _parts.Where(t => t.Part.At.Contains(DressSite.Garden)).ToList();
            foreach (FlavourRuin f in _net.Ruins)
            {
                if (f.Route == null || !(f.Family.Tags.Contains("house") || f.Family.Tags.Contains("shop"))) continue;
                PoiGenerator.Gen.Int3 v = f.Layout.EntranceFacing.Vec();
                double doorX = f.OriginX + f.Layout.EntranceCell.X + 0.5, doorZ = f.OriginZ + f.Layout.EntranceCell.Z + 0.5;
                var (cx, cz) = f.Center;
                float near = Fade(cx, cz, wall.Count > 0 ? wall[0].Part : garden.Count > 0 ? garden[0].Part : _parts[0].Part);
                // Two marks at most on one front, either side of the door.
                int marks = 0;
                foreach (var (p, s) in wall.OrderBy(_ => _rng.Float()))
                {
                    if (p.Only != null && !f.Family.Tags.Contains(p.Only)) continue;
                    if (p.Only == null && marks >= 2) continue;
                    if (!_rng.Chance(Math.Min(0.95f, p.Chance * s * near))) continue;
                    DressItem it = Make(p, DressSite.Wall, doorX, doorZ, YawOf(v.X, v.Z) + MathF.PI);
                    it.Ruin = f;
                    it.Along = p.Only != null ? 0f : (marks == 0 ? 1 : -1) * _rng.Float(1.1f, 1.8f);
                    if (p.Model == "shop_sign") it.Along = 1.6f;
                    if (p.Only == null) marks++;
                    if (!_d.OnHouse.TryGetValue(f, out var list)) _d.OnHouse[f] = list = new();
                    list.Add(it);
                    _d.Items.Add(it);
                }
                foreach (var (p, s) in garden)
                {
                    if (p.Only != null && !f.Family.Tags.Contains(p.Only)) continue;
                    if (!_rng.Chance(Math.Min(0.9f, p.Chance * s * near))) continue;
                    for (int tries = 0; tries < 6; tries++)
                    {
                        double out_ = _rng.Float(2.5f + p.Reach, 5f + p.Reach), side = _rng.Float(-4f, 4f);
                        double x = doorX + v.X * out_ - v.Z * side, z = doorZ + v.Z * out_ + v.X * side;
                        if (!OffRoads(x, z, p.Reach + 0.4f) || !Free(x, z, p.Reach, p.Solid) || !Ground(x, z, p.Reach)) continue;
                        DressItem it = Make(p, DressSite.Garden, x, z, YawOf(v.X, v.Z) + (p.Model == "tape" ? MathF.PI / 2 : 0f));
                        it.Ruin = f;
                        it.Length = 6f;
                        Add(it, p.Reach);
                        break;
                    }
                }
            }
        }

        /// <summary>The ways out of town: luggage and wire on the verges, floats and burnt-out cars in a lane, shell holes in the fields.</summary>
        void Road(InfraRoute r)
        {
            var parts = _parts.SelectMany(t => t.Part.At.Where(s => s is DressSite.Verge or DressSite.Road or DressSite.Field).Select(s => (t.Part, t.S, s))).ToList();
            Walk(r, parts, (p, site, k, density) =>
            {
                if (InTown(r.X[k], r.Z[k])) return;
                var (dx, dz, nx, nz) = Frame(r, k);
                double cx = r.X[k], cz = r.Z[k];
                int side = _rng.Chance(0.5f) ? 1 : -1;
                double off;
                float yaw;
                switch (site)
                {
                    case DressSite.Road:
                        if (p.Solid && r.HalfWidth < 3.4f) return;
                        off = p.Solid ? r.HalfWidth * 0.5 : _rng.Float(0f, r.HalfWidth * 0.7f);
                        yaw = YawOf(dx, dz) + (_rng.Chance(0.5f) ? MathF.PI : 0f) + _rng.Float(-0.15f, 0.15f);
                        break;
                    case DressSite.Verge:
                        off = r.HalfWidth + 1.3 + p.Reach * 0.8;
                        yaw = p.Model is "wire" or "fence" ? YawOf(-nx * side, -nz * side) + MathF.PI / 2 : YawOf(nx * side, nz * side);
                        if (p.Model is "wire" or "fence") yaw = YawOf(nx, nz);
                        break;
                    default:
                        off = r.HalfWidth + _rng.Float(7f, 22f) + p.Reach;
                        yaw = _rng.Float(0f, MathF.Tau);
                        if (p.Model is "wire" or "survey_stakes" or "fence") yaw = YawOf(nx, nz);
                        break;
                }
                double x = cx + nx * off * side, z = cz + nz * off * side;
                if (site == DressSite.Road)
                {
                    if (_net.Nearest(x, z, r.HalfWidth + p.Reach + 6, out _, out _, new[] { r }) != null) return;
                }
                else if (!OffRoads(x, z, Math.Max(0.4f, p.Reach * 0.6f))) return;
                if (!Free(x, z, p.Reach, p.Solid) || !Ground(x, z, site == DressSite.Road && !p.Solid ? 0.3f : p.Reach)) return;
                Add(Make(p, site, x, z, yaw), p.Reach);
            });
        }

        /// <summary>Places' ways in: tents, barriers and sandbags, queues and tape.</summary>
        void Gates()
        {
            var parts = _parts.Where(t => t.Part.At.Contains(DressSite.Gate)).ToList();
            if (parts.Count == 0) return;
            foreach (PoiSite site in _w.Pois)
            {
                if (site.Gates.Count == 0 || site.Tier == PoiTier.Wreck) continue;
                PlanetSurface.Poi.PoiGate g = site.Gates[0];
                double gx = g.Cell.X + 0.5, gz = g.Cell.Z + 0.5;
                foreach (var (p, s) in parts.OrderBy(_ => _rng.Float()))
                {
                    if (p.Only != null && !site.Family.Tags.Contains(p.Only)) continue;
                    if (!_rng.Chance(Math.Min(0.9f, p.Chance * s * Fade(gx, gz, p) * 0.8f))) continue;
                    for (int tries = 0; tries < 8; tries++)
                    {
                        double out_ = _rng.Float(5f + p.Reach, 14f + p.Reach), side = _rng.Float(-7f, 7f);
                        double x = gx + g.OutX * out_ - g.OutZ * side, z = gz + g.OutZ * out_ + g.OutX * side;
                        if (!OffRoads(x, z, p.Reach + 0.5f) || !Free(x, z, p.Reach, p.Solid) || !Ground(x, z, p.Reach)) continue;
                        DressItem it = Make(p, DressSite.Gate, x, z, YawOf(g.OutX, g.OutZ) + (p.Model == "tape" ? MathF.PI / 2 : 0f));
                        it.Length = 7f;
                        Add(it, p.Reach);
                        break;
                    }
                }
            }
        }
    }
}
