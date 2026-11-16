using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GalHack.Items;
using GalHack.Structures;
using PoiGenerator.Gen;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Orbit;

/// <summary>One wreck (or one piece of a wreck) to be set adrift in orbit: its blocks, where it floats, and what its hold carries.</summary>
public sealed class DerelictPlan
{
    public string Id;
    public string Name;
    /// <summary>The POI generator's site (poi-generator/data/orbit.txt) it comes from.</summary>
    public SpaceSiteDef Site;
    public PoiState State;
    public string Zone;
    public Structure Structure;
    public N.Vector3 Position;
    public N.Quaternion Rotation;
    /// <summary>What its container blocks hold (its hold, shared storage).</summary>
    public Bill Loot = new();
    /// <summary>Its way in from outside: a hull door, dead and shut, forced by hand (structure cells), if it has one.</summary>
    public Int3? Door;
    /// <summary>A capital ship's piece: what the hull builder made of it (its rooms, its dressing, its core's seat), else null.</summary>
    public Slice.Places.HullVessel.Result Hull;
}

/// <summary>
/// The start system's wrecks near the world, from the POI generator's data: its start-system
/// placement (<see cref="OrbitPlacement"/>: kinds, salvage grade, the "near" and "orbit" bands, a
/// bearing and a distance) brought in close, and each kind built as real blocks (building's
/// structure model, on the 1 m grid) so it can be boarded, looted, salvaged block by block and claimed
/// with a core:
/// <list type="bullet">
/// <item>a capital ship (any site orbit.txt gives a "hull": derelict freighters, warships, drifting and generation hulks): the one hull builder the planet's crashed hulks are built by (HullVessel), adrift, its rooms dressed and lit</item>
/// <item>a broken station (broken, and any dead station or outpost: a start system holds no live faction): modules on truss tubes, a hull door, one module torn off and drifting</item>
/// <item>a wreck field (field): hull fragments and loose cargo boxes tumbling apart</item>
/// <item>an escape pod cluster (field): pods that launched and were never picked up</item>
/// </list>
/// None has a core or power: no field holds any air in their doors, so their insides are vacuum,
/// nothing holds you to a floor, and any of them can be claimed.
/// Everything is seeded from the system's seed, so the same world always has the same wrecks.
/// </summary>
public static class Derelicts
{
    public static List<DerelictPlan> Plan(PoiLibrary library, ulong systemSeed, GlobeMap globe, double atmosphereRadius,
        OrbitRules rules, Catalogue catalogue, ItemCatalogue items)
    {
        var plans = new List<DerelictPlan>();
        int site = 0;
        foreach (OrbitPoi poi in OrbitPlacement.Place(library, systemSeed, start: true))
        {
            if (poi.Zone is not ("near" or "orbit")) continue; // moons, belts and deep space are other zones
            site++;
            var rng = new Random(unchecked((int)(poi.Seed ^ (poi.Seed >> 32))));
            // The bearing is its longitude; its latitude from its seed, within the band.
            double latitude = (rng.NextDouble() * 2 - 1) * rules.Wrecks.MaxLatitude;
            double x = poi.Bearing / 360.0 * globe.Width, z = (90 - latitude) / 180 * globe.Height;
            float above = Altitude(poi, rules.Wrecks);
            N.Vector3 centre = globe.Direction(x, z) * (float)(atmosphereRadius + above);
            string id = $"site{site}-{poi.Site.Id.Replace('_', '-')}";
            List<DerelictPlan> pieces = poi.Site.Hull != null && library.Sites.TryGetValue(poi.Site.Hull, out SiteDef hull) ? Capital(library, hull, poi, id, catalogue, items, rules) : poi.Site.Kind switch
            {
                "field" when poi.Site.Id.Contains("pod") => Pods(rng, poi, id, catalogue),
                "field" => Field(rng, poi, id, catalogue),
                _ => Station(rng, poi, id, catalogue, HullStyle.For(library.HullStyles, poi.Site.Faction, poi.Seed)),
            };
            // Pieces were laid out round the site's centre in its own frame: turn the whole site.
            N.Quaternion siteTurn = RandomTurn(rng);
            foreach (DerelictPlan p in pieces)
            {
                p.Site = poi.Site;
                p.State = poi.State;
                p.Zone = poi.Zone;
                p.Position = centre + N.Vector3.Transform(p.Position, siteTurn);
                p.Rotation = N.Quaternion.Normalize(siteTurn * p.Rotation);
                if (p.Loot.IsEmpty) Stock(p, rng, poi.Site.Salvage, rules, items);
                plans.Add(p);
            }
        }
        return plans;
    }

    /// <summary>Metres above the atmosphere's top for a site: its band's range, nearer sites lower (orbit's band on a log scale).</summary>
    static float Altitude(OrbitPoi poi, OrbitRules.WreckRules r)
    {
        if (poi.Zone == "near")
        {
            float t = Math.Clamp((poi.RadiusKm - 200f) / 1800f, 0f, 1f);
            return r.Near[0] + (r.Near[1] - r.Near[0]) * t;
        }
        float u = Math.Clamp(MathF.Log(poi.RadiusKm / 1000f) / MathF.Log(30f), 0f, 1f);
        return r.Orbit[0] + (r.Orbit[1] - r.Orbit[0]) * u;
    }

    static N.Quaternion RandomTurn(Random rng)
    {
        // A uniformly random orientation (Shoemake).
        double u1 = rng.NextDouble(), u2 = rng.NextDouble() * Math.Tau, u3 = rng.NextDouble() * Math.Tau;
        double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
        return N.Quaternion.Normalize(new N.Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3))));
    }

    /// <summary>Fills a plan's hold: one roll of its grade's loot table for each container block.</summary>
    static void Stock(DerelictPlan p, Random rng, string grade, OrbitRules rules, ItemCatalogue items)
    {
        int containers = p.Structure.Blocks().Count(b => p.Structure.TypeOf(b.block).IsContainer);
        for (int i = 0; i < containers; i++)
            foreach (var (item, least, most) in rules.LootFor(grade))
            {
                if (!items.Has(item)) continue;
                int n = rng.Next(least, most + 1);
                if (n > 0) p.Loot.Add(item, n);
            }
    }

    // ---- Kinds ---------------------------------------------------------------------------------

    /// <summary>
    /// A capital ship adrift: the same plan the planet's crash sites have (poi-generator's CapitalHull, the site
    /// named by orbit.txt's "hull") built by the same hull builder (HullVessel) of the player's own blocks, its rooms
    /// dressed and lit, damaged as its state says, no core; its pieces float apart as the break left them. Its holds
    /// carry a few rolls of its salvage grade.
    /// </summary>
    static List<DerelictPlan> Capital(PoiLibrary library, SiteDef site, OrbitPoi poi, string id, Catalogue catalogue, ItemCatalogue items, OrbitRules rules)
    {
        SitePart part = site.Parts[0];
        CapitalHull.HullPlan plan = CapitalHull.Plan(library.Kits[site.Style], WorldProfile.Parse(part.Arg("tech", "mid")), CapitalHull.SpecOf(part) with { Husk = poi.State == PoiState.Infested }, poi.Seed);
        var pieces = new List<DerelictPlan>();
        var rng = new Random(unchecked((int)(poi.Seed ^ (poi.Seed >> 17))));
        N.Vector3 first = plan.Pieces[0].Origin;
        List<Slice.Places.HullVessel.Result> built = Slice.Places.Hulks.Build(plan, catalogue, poi.State, poi.Seed, Slice.Places.Hulks.Deco());
        for (int i = 0; i < built.Count; i++)
        {
            Slice.Places.HullVessel.Result hull = built[i];
            Structure s = hull.Structure;
            s.Name = poi.Site.Name + hull.Piece.Name;
            var p = new DerelictPlan
            {
                Id = plan.Pieces.Count == 1 ? id : $"{id}-{i + 1}", Name = s.Name, Structure = s,
                Position = hull.Piece.Origin - first, Rotation = hull.Piece.Rotation, Door = hull.Door, Hull = hull,
            };
            int containers = s.Blocks().Count(b => s.TypeOf(b.block).IsContainer);
            for (int r = 0; r < Math.Min(24, containers / 12 + 2); r++)
                foreach (var (item, least, most) in rules.LootFor(poi.Site.Salvage))
                {
                    if (!items.Has(item)) continue;
                    int n = rng.Next(least, most + 1);
                    if (n > 0) p.Loot.Add(item, n);
                }
            pieces.Add(p);
        }
        return pieces;
    }
    /// <summary>
    /// A broken station in its faction's style (data/hull-styles.txt, the hulls' rules): a hub with its airlock, arms out
    /// on truss tubes to modules, one module torn off and drifting. The style sets it all: the hub's size and form (a
    /// block, a segmented chain swept to one side, a ribbed and mirrored frame), how many arms and whether they mirror,
    /// arms swept in short straight runs, modules stacked as cargo pods, bolted on mismatched, studded with spikes or
    /// scrap plates, an engine block, and the colours.
    /// </summary>
    static List<DerelictPlan> Station(Random rng, OrbitPoi poi, string id, Catalogue catalogue, HullStyle st)
    {
        var hub = new Cells(rng, st);
        int h = (int)MathF.Round(3 * st.Beam * (st.Segments > 0 ? 0.8f : 1f) + (st.Length > 1.1f ? 1 : 0)), up = 4 + (st.Length > 1.1f ? 2 : 0);
        hub.Box(-h, 0, -h, h, up, h, "hull", hollow: true);
        hub.Box(-h + 1, 2, -h + 1, h - 1, 2, h - 1, "deck");
        hub.Clear(-1, 2, -1, 0, 2, 0);
        // A segmented hub: a chain of shrinking segments aft, each stepped to one side (the carapace's sweep).
        for (int k = 1; k <= st.Segments; k++)
        {
            int w = Math.Max(2, h - k), z0 = h + 1 + (k - 1) * (2 * w + 2), dx = k;
            hub.Box(-w + dx, 0, z0 + 1, w + dx, up - k / 2, z0 + 2 * w + 1, "hull", hollow: true);
            hub.Tube(dx, 2, z0 - 1, 0, 0, 1, 3);
        }
        hub.HullDoor(h, 1);
        foreach (var (x, z) in new[] { (-h + 1, -h + 1), (h - 1, -h + 1), (-h + 1, h - 1) }) hub.Set(x, 1, z, "container", paint: "cargo");
        // Frames round the hub (a lattice style's ribs), mirrored all round.
        if (st.Ribs > 0)
            for (int z = -h; z <= h; z += 2)
                if (Math.Abs(z - 1) > 1) hub.Ring(-h - 1, -1, z, h + 1, up + 1, z, "trim");
        // The arms: along the axes (mirrored for a symmetric style), out to a module each; a swept style steps them aside as they go.
        var dirs = new List<(int dx, int dz)> { (0, -1), (-1, 0), (1, 0), (0, 1) };
        int arms = st.Symmetric ? 4 : st.Length > 1.1f ? 2 : rng.Next(2, 4);
        if (!st.Symmetric) dirs = st.Length > 1.1f ? new() { (0, -1), (0, 1) } : dirs.Take(3).OrderBy(_ => rng.Next()).ToList();
        int len = (int)MathF.Round(5 * st.Length) + (st.Symmetric ? 0 : rng.Next(0, 3)), size = st.Symmetric ? 2 : 2 + rng.Next(0, 2);
        var modules = new List<(int x, int z)>();
        for (int i = 0; i < arms && i < dirs.Count; i++)
        {
            var (dx, dz) = dirs[i];
            if (dx == 1) continue; // (the hull door's side stays clear)
            // From the hub's wall (the tube opens it), in one run or three swept aside, to the module's wall.
            int x = dx * h, z = dz * h, sweep = st.Fins > 0 ? (i % 2 == 0 ? 1 : -1) : 0, runs = sweep != 0 ? 3 : 1;
            for (int run = 0; run < runs; run++)
            {
                int n = sweep != 0 ? Math.Max(2, len / 3) : len;
                hub.Tube(x, 2, z, dx, 0, dz, n);
                x += dx * n;
                z += dz * n;
                if (run == runs - 1) break;
                int sx = dz != 0 ? sweep : 0, sz = dx != 0 ? sweep : 0;
                hub.Tube(x - dx, 2, z - dz, sx, 0, sz, 3);
                x += 2 * sx;
                z += 2 * sz;
            }
            int m = size + (st.Symmetric ? 0 : rng.Next(0, 2));
            hub.Tube(x, 2, z, dx, 0, dz, 1);
            int cx = x + dx * m, cz = z + dz * m;
            Module(hub, rng, st, cx, cz, m, up);
            modules.Add((cx, cz));
        }
        // An engine block on the stern of a long hull's station (its spine's end), and spikes all over a raider's.
        if (st.EngineLength > 1.4f) hub.Box(-h, 0, -h - 4, h, up, -h - 1, "dark");
        for (int i = 0, n = (int)MathF.Round(st.Spikes * 0.6f); i < n; i++)
        {
            var (mx, mz) = modules.Count > 0 && rng.Next(2) == 0 ? modules[rng.Next(modules.Count)] : (0, 0);
            int sx = rng.Next(2) == 0 ? -1 : 1, sl = 2 + rng.Next(0, 4), sy = 1 + rng.Next(0, up);
            for (int k = 0; k < sl; k++) hub.Set(mx + sx * (h + 1 + k + (mx == 0 && mz == 0 ? 0 : 1)), sy, mz + rng.Next(-1, 2), "steel", "dark");
        }
        hub.Plates(st.Plates);
        hub.Scars(8);
        DerelictPlan main = hub.Plan(id, poi.Site.Name, catalogue);
        // The torn-off module, its stub of tube open to space.
        var torn = new Cells(rng, st);
        torn.Tube(-size - 3, 2, 0, 1, 0, 0, 4);
        Module(torn, rng, st, 0, 0, size, up, fixedWidth: true);
        torn.Scars(4);
        DerelictPlan off = torn.Plan(id + "-module", poi.Site.Name + " (module)", catalogue);
        off.Position = new N.Vector3(26f, 6f, 9f);
        off.Rotation = N.Quaternion.CreateFromYawPitchRoll(0.6f, 0.4f, -0.3f);
        return new List<DerelictPlan> { main, off };
    }

    /// <summary>A station's module round a point, by its style: cargo pods stacked (colour-coded), a ribbed frame, a mismatched box, or a plain one.</summary>
    static void Module(Cells c, Random rng, HullStyle st, int x, int z, int m, int up, bool fixedWidth = false)
    {
        if (st.Pods > 0)
        {
            for (int px = -1; px <= 1; px += 2)
                for (int py = 0; py < 2; py++)
                    c.Box(x + px * (m + 1) - 1, py * 3, z - m, x + px * (m + 1) + 1, py * 3 + 2, z + m, "pod" + rng.Next(0, Math.Max(1, st.Cargo.Count)));
        }
        int mu = st.Bolted > 0 && !fixedWidth ? m + rng.Next(0, 2) : m;
        c.Box(x - mu, 0, z - m, x + mu, up - 1, z + m, "hull", hollow: true);
        c.Box(x - mu + 1, 2, z - m + 1, x + mu - 1, 2, z + m - 1, "deck");
        c.Set(x, 3, z, "container", paint: "cargo");
        if (st.Ribs > 0) c.Ring(x - mu - 1, -1, z, x + mu + 1, up, z, "trim");
        if (st.Bolted > 0) c.Box(x + mu + 1, 1, z - 1, x + mu + 2 + rng.Next(0, 2), 3, z + 1 + rng.Next(0, 2), "scrap" + rng.Next(0, Math.Max(1, st.Scrap.Count)));
    }

    /// <summary>A wreck field: hull fragments and loose cargo boxes, scattered and tumbling apart.</summary>
    static List<DerelictPlan> Field(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        var pieces = new List<DerelictPlan>();
        int n = 5 + rng.Next(0, 4);
        string colour = HullColour(rng);
        for (int i = 0; i < n; i++)
        {
            var f = new Cells(rng, colour);
            int size = 6 + rng.Next(0, 22);
            f.Clump(size);
            if (i % 2 == 0) f.Set(0, 1, 0, "container", paint: "cargo");
            DerelictPlan p = f.Plan($"{id}-{i + 1}", $"{poi.Site.Name}: fragment {i + 1}", catalogue);
            double a = i * Math.Tau / n + rng.NextDouble() * 0.5, r = 25 + rng.NextDouble() * 70;
            p.Position = new N.Vector3((float)(Math.Cos(a) * r), (float)((rng.NextDouble() - 0.5) * 40), (float)(Math.Sin(a) * r));
            p.Rotation = RandomTurn(rng);
            pieces.Add(p);
        }
        return pieces;
    }

    /// <summary>An escape pod cluster: a ring of pods, each a seat, a locker and a hatch.</summary>
    static List<DerelictPlan> Pods(Random rng, OrbitPoi poi, string id, Catalogue catalogue)
    {
        var pods = new List<DerelictPlan>();
        int n = 4 + rng.Next(0, 3);
        for (int i = 0; i < n; i++)
        {
            var p = new Cells(rng, "e85d04", accent: "2b2d42");
            p.Box(-1, 0, -2, 1, 2, 2, "hull", hollow: true);
            p.Set(0, 1, -2, "door");
            p.Set(0, 1, 0, "cockpit");
            p.Set(0, 1, 1, "container", paint: "cargo");
            p.Scars(1);
            DerelictPlan plan = p.Plan($"{id}-{i + 1}", $"Escape pod {i + 1}", catalogue);
            plan.Door = new Int3(0, 1, -2);
            double a = i * Math.Tau / n;
            plan.Position = new N.Vector3((float)(Math.Cos(a) * 22), (float)((rng.NextDouble() - 0.5) * 8), (float)(Math.Sin(a) * 22));
            plan.Rotation = RandomTurn(rng);
            pods.Add(plan);
        }
        return pods;
    }

    static string HullColour(Random rng) => new[] { "8d99ae", "a3a380", "6c757d", "b08968", "7f8c8d" }[rng.Next(0, 5)];

    // ---- Building a layout as blueprint text ---------------------------------------------------

    /// <summary>A layout in progress: cells to block statements, written out as building's blueprint text and built by its own loader.</summary>
    sealed class Cells
    {
        readonly Random _rng;
        readonly SortedDictionary<(int y, int z, int x), string> _cells = new();
        /// <summary>The tubes' passages: nothing laid after them fills them (a door may stand in one).</summary>
        readonly HashSet<(int y, int z, int x)> _open = new();
        readonly string _hull, _accent;
        readonly HullStyle _style;
        Int3? _door;

        public Cells(Random rng, string hull, string accent = "2b2d42")
        {
            _rng = rng;
            _hull = hull;
            _accent = accent;
        }

        /// <summary>A layout in a faction's style: its hull and trim colours, its cargo's and its scrap's.</summary>
        public Cells(Random rng, HullStyle style) : this(rng, Hex(style.Hull), Hex(style.Trim)) => _style = style;

        static string Hex(Rgb c) => $"{c.R:x2}{c.G:x2}{c.B:x2}";

        /// <summary>A frame round a box's edge in a plane across z (or x when x0 == x1): the cells of its rim.</summary>
        public void Ring(int x0, int y0, int z, int x1, int y1, int z1, string paint)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x == x0 || x == x1 || y == y0 || y == y1) && !_cells.ContainsKey((y, z, x))) Set(x, y, z, "steel", paint);
        }

        /// <summary>Scrap plates over a share of the hull: patches of its shell repainted in mismatched colours.</summary>
        public void Plates(float share)
        {
            if (share <= 0 || _style == null || _style.Scrap.Count == 0) return;
            foreach (var key in _cells.Where(c => c.Value.Contains("paint=hull")).Select(c => c.Key).ToList())
                if (Math.Abs(((key.x >> 1) * 73856093) ^ ((key.y >> 1) * 19349663) ^ ((key.z >> 1) * 83492791)) % 100 < share * 100)
                    _cells[key] = _cells[key].Replace("paint=hull", "paint=scrap" + Math.Abs((key.x >> 1) + (key.z >> 1)) % _style.Scrap.Count);
        }

        public void Set(int x, int y, int z, string type, string paint = null, string rot = null, bool open = false)
        {
            if (type != "door" && _open.Contains((y, z, x))) return;
            paint ??= type switch { "steel" or "hardened" => "hull", "glass" => null, _ => null };
            var line = new StringBuilder(type);
            if (rot != null) line.Append(" rot=").Append(rot);
            if (paint != null) line.Append(" paint=").Append(paint);
            if (open) line.Append(" open");
            _cells[(y, z, x)] = line.ToString();
        }

        public void Clear(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        _cells.Remove((y, z, x));
        }

        /// <summary>A box of a kind: "hull" (its shell; steel, hardened on the edges), "wall" or "deck" (inner steel).</summary>
        public void Box(int x0, int y0, int z0, int x1, int y1, int z1, string kind, bool hollow = false)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int edges = (x == x0 || x == x1 ? 1 : 0) + (y == y0 || y == y1 ? 1 : 0) + (z == z0 || z == z1 ? 1 : 0);
                        if (hollow && edges == 0) continue;
                        if (kind == "hull") Set(x, y, z, edges >= 2 ? "hardened" : "steel", edges >= 2 ? "trim" : "hull");
                        else if (kind is "deck" or "wall") Set(x, y, z, "steel", kind == "deck" ? "deck" : "wall");
                        else Set(x, y, z, "steel", kind);
                    }
        }

        /// <summary>A way in through a side wall: a hull door at floor height (dead, shut until it is forced).</summary>
        public void HullDoor(int wallX, int z)
        {
            Set(wallX, 1, z, "door", paint: "hazard");
            _door = new Int3(wallX, 1, z);
        }

        /// <summary>A square tube (3 by 3, a 1-cell passage) from a cell along an axis.</summary>
        public void Tube(int x, int y, int z, int dx, int dy, int dz, int length)
        {
            // The two axes across the tube.
            (int, int, int) p = dx != 0 ? (0, 1, 0) : (1, 0, 0), q = dz != 0 ? (0, 1, 0) : (0, 0, 1);
            if (dy != 0) (p, q) = ((1, 0, 0), (0, 0, 1));
            for (int i = 0; i < length; i++)
            {
                int cx = x + dx * i, cy = y + dy * i, cz = z + dz * i;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                    {
                        int px = cx + p.Item1 * a + q.Item1 * b, py = cy + p.Item2 * a + q.Item2 * b, pz = cz + p.Item3 * a + q.Item3 * b;
                        if (a == 0 && b == 0)
                        {
                            _cells.Remove((py, pz, px));
                            _open.Add((py, pz, px));
                        }
                        else Set(px, py, pz, "steel", "trim");
                    }
            }
        }

        /// <summary>A hole torn through: the cells within a radius of a point gone.</summary>
        public void Hole(int x, int y, int z, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy + dz * dz <= r * r + 1) _cells.Remove((y + dy, z + dz, x + dx));
        }

        /// <summary>Old damage: a few shell cells knocked out (never a door, a seat or a box).</summary>
        public void Scars(int count)
        {
            var shell = _cells.Where(c => c.Value.StartsWith("steel") && c.Value.Contains("paint=hull")).Select(c => c.Key).ToList();
            for (int i = 0; i < count && shell.Count > 0; i++)
            {
                int k = _rng.Next(0, shell.Count);
                _cells.Remove(shell[k]);
                shell.RemoveAt(k);
            }
        }

        /// <summary>A fragment: a random clump of plates and blocks grown from the origin.</summary>
        public void Clump(int size)
        {
            var at = new List<(int x, int y, int z)> { (0, 0, 0) };
            Set(0, 0, 0, "steel", "hull");
            while (_cells.Count < size)
            {
                var (x, y, z) = at[_rng.Next(0, at.Count)];
                int d = _rng.Next(0, 6);
                (x, y, z) = d switch { 0 => (x + 1, y, z), 1 => (x - 1, y, z), 2 => (x, y + 1, z), 3 => (x, y - 1, z), 4 => (x, y, z + 1), _ => (x, y, z - 1) };
                if (Math.Abs(y) > 1) continue;
                if (_cells.ContainsKey((y, z, x))) continue;
                Set(x, y, z, _rng.Next(0, 4) == 0 ? "hardened" : "steel", _rng.Next(0, 3) == 0 ? "trim" : "hull");
                at.Add((x, y, z));
            }
        }

        public DerelictPlan Plan(string id, string name, Catalogue catalogue)
        {
            var text = new StringBuilder();
            text.AppendLine("schema galhack.blueprint");
            text.AppendLine("version 1");
            text.AppendLine($"name \"{name}\"");
            text.AppendLine("grid large");
            text.AppendLine("mode capital");
            text.AppendLine("gravity off");
            text.AppendLine($"paint hull base={_hull} grime=3 texture=plates");
            text.AppendLine($"paint trim base={_accent} grime=3");
            text.AppendLine("paint wall base=4a4e69 grime=3");
            text.AppendLine("paint deck base=22223b grime=3 texture=ribbed");
            text.AppendLine("paint cargo base=e85d04 accent=2b2d42 pattern=stripes grime=3");
            text.AppendLine("paint hazard base=f4a100 accent=2b2d42 pattern=hazard grime=2");
            if (_style != null)
            {
                text.AppendLine($"paint dark base={Hex(_style.Dark)} grime=2");
                for (int i = 0; i < Math.Max(1, _style.Cargo.Count); i++)
                    text.AppendLine($"paint pod{i} base={(_style.Cargo.Count > 0 ? Hex(_style.Cargo[i]) : "e85d04")} accent=2b2d42 pattern=stripes grime=2");
                for (int i = 0; i < Math.Max(1, _style.Scrap.Count); i++)
                    text.AppendLine($"paint scrap{i} base={(_style.Scrap.Count > 0 ? Hex(_style.Scrap[i]) : _accent)} grime=3");
            }
            foreach (var ((y, z, x), what) in _cells)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"block {x} {y} {z} {what}"));
            BlueprintData data = Blueprint.Parse(text.ToString());
            // A wreck has no core (that is what makes it a wreck): every other rule holds.
            List<string> errors = Blueprint.Validate(data, catalogue).Where(e => !e.StartsWith("no core")).ToList();
            if (errors.Count > 0) throw new InvalidOperationException($"wreck '{name}': " + string.Join("; ", errors.Take(5)));
            Structure s = Blueprint.Build(data, catalogue);
            s.DirtyChunks.Clear();
            return new DerelictPlan { Id = id, Name = name, Structure = s, Rotation = N.Quaternion.Identity, Door = _door };
        }
    }
}
